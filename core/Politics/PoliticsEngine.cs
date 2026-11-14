using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>
/// All political state of one country: polls, elections, the phase machine (governing, formation, caretaker),
/// the sitting government. Owned by <see cref="Game"/>, which calls <see cref="Step"/> once per turn.
/// Uses its own PCG stream so adding politics never shifts economy or event draws.
/// </summary>
internal sealed class PoliticsEngine
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;
    private const double DefaultRelation = 0.3;

    private readonly ScenarioPoliticsDef _scenario;
    private readonly CountryPoliticsDef _rules;
    private readonly Dictionary<string, PartyDef> _parties;
    private readonly Dictionary<string, double> _ranges;
    private readonly SupportModel _support;
    private readonly IElectoralSystem _electoral;
    private readonly Pcg32 _rng;
    private readonly Func<string, string, double> _lookup; // current value of (kind, subject): "policy" or "value"
    private readonly double _dt;
    private readonly IReadOnlyList<VoterGroupDef> _groups;
    private readonly LoyaltyParams _loyaltyParams = new();
    private readonly SortedDictionary<string, double> _loyalty = new(Ord);
    private readonly HashSet<string> _warned = new(Ord);
    private readonly SortedDictionary<string, (string? DemandId, int Left)> _ultimatums = new(Ord);
    private readonly HashSet<string> _redLinesActive = new(Ord); // "party|policy|op"
    private int _lastTurn;

    // The player's own party.
    private double _pc = 6.0, _org = 0.2, _credibility = 1.0;
    private readonly SortedDictionary<string, double> _relations = new(Ord);
    private readonly SortedDictionary<string, double> _momentum = new(Ord);
    private readonly SortedDictionary<string, double> _pressure = new(Ord);
    /// <summary>How responsible voters hold each party for the state of the country, 0..1; moves slowly toward the government role.</summary>
    private readonly SortedDictionary<string, double> _attribution = new(Ord);
    /// <summary>Resentment between parties, keyed "a|b" = how much a resents b; raised by leaving and falls, fades slowly.</summary>
    private readonly SortedDictionary<string, double> _grudge = new(Ord);
    private static readonly double GrudgeDecay = DetMath.Pow(0.5, 1.0 / 8.0); // half-life of 8 turns

    private double Resent(string a, string b) => _grudge.GetValueOrDefault(a + "|" + b);
    private void AddGrudge(string a, string b, double amount) =>
        _grudge[a + "|" + b] = Math.Min(2.0, Resent(a, b) + amount);
    private readonly List<MeasureInfo> _measures = new();
    private readonly List<string> _valueIds;
    private readonly List<Dictionary<string, double>> _history = new(); // value snapshots of the last 5 turns

    private IReadOnlyDictionary<string, double> _poll = new Dictionary<string, double>();
    private IReadOnlyDictionary<string, int> _seats = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, double> _votes = new Dictionary<string, double>();
    private int _turnsToElection;
    private Phase _phase = Phase.Governing;
    private Government _government;
    private List<string> _queue = new();
    private int _attempt;
    private int _turnsLeft;
    private int _caretakerLeft;
    private readonly HashSet<string> _excluded = new(Ord);
    private CoalitionProposal? _offer;
    private readonly List<PoliticalEvent> _pending = new();
    private readonly List<(string PolicyId, double Target)> _proposals = new();
    private readonly List<(string PolicyId, double Target)> _approved = new();
    private bool _leaveQueued, _noConfidenceQueued;
    private readonly List<string> _dismissQueue = new();

    public PoliticsEngine(PackSet packs, ScenarioDef scenario, CountryDef country, ulong seed,
        IReadOnlyDictionary<string, double> satisfaction, Func<string, string, double> lookup, double turnsPerYear)
    {
        _scenario = scenario.Politics!;
        _rules = country.Politics!;
        _lookup = lookup;
        _dt = 1.0 / turnsPerYear;
        _groups = packs.VoterGroups;
        _valueIds = packs.Values.Select(v => v.Id).OrderBy(x => x, Ord).ToList();
        _policyDefs = packs.Policies.ToDictionary(p => p.Id, Ord);
        _parties = packs.Parties.OrderBy(p => p.Id, Ord).ToDictionary(p => p.Id, Ord);
        _ranges = PartyMath.PolicyRanges(packs.Policies);
        var govParties = _scenario.Government.Concat(_scenario.Leader != null ? new[] { _scenario.Leader } : Array.Empty<string>())
            .Distinct(Ord).OrderBy(x => x, Ord).ToList();
        var initial = new List<Commitment>();
        foreach (var a in _scenario.Agreements ?? Array.Empty<AgreementDef>())
        {
            var d = _parties[a.Party].Demands.First(x => x.Id == a.Demand);
            initial.Add(new Commitment(a.Party, d, lookup(d.Kind, d.Subject), d.DueTurns));
        }
        _government = new Government(_scenario.Leader ?? govParties.FirstOrDefault() ?? "", govParties, initial, 0);
        foreach (var id in _parties.Keys) if (id != _scenario.PlayerParty) _relations[id] = DefaultRelation;
        foreach (var g in _groups) { _momentum[g.Id] = 0.0; _pressure[g.Id] = 0.0; }
        foreach (var id in _parties.Keys) _attribution[id] = AttributionTarget(id);
        _support = new SupportModel(packs.Parties, packs.VoterGroups, packs.Policies);
        _support.Calibrate(_scenario.Support, Input(satisfaction));
        _electoral = ElectoralSystems.Create(packs.ElectoralSystems.First(e => e.Id == _rules.ElectoralSystem));
        _rng = new Pcg32(seed, 99);
        _poll = _support.Compute(Input(satisfaction), null);
        _seats = _electoral.Allocate(_poll);
        _votes = _poll;
        _turnsToElection = _scenario.TurnsToElection;
        ResetLoyalty();
    }

    private string Player => _scenario.PlayerParty;

    /// <summary>Turns until the next election actually happens: the early election during caretaker, else the regular term.</summary>
    private int ElectionIn => _phase == Phase.Caretaker ? Math.Min(_turnsToElection, Math.Max(1, _caretakerLeft)) : _turnsToElection;

    /// <summary>Voters' mood as the parties feel it: satisfaction minus the pressure the opposition built up.</summary>
    private SortedDictionary<string, double> Effective(IReadOnlyDictionary<string, double> satisfaction)
    {
        var eff = new SortedDictionary<string, double>(Ord);
        foreach (var (id, s) in satisfaction) eff[id] = Math.Clamp(s - _pressure.GetValueOrDefault(id), -1.0, 1.0);
        return eff;
    }

    private double Cohesion() => PartyMath.Cohesion(_parties[Player], _ranges);

    private SupportInput Input(IReadOnlyDictionary<string, double> satisfaction) => new(
        Effective(satisfaction), new HashSet<string>(_government.Parties, Ord),
        new Dictionary<string, double> { [Player] = _credibility * Cohesion() },
        new Dictionary<string, double>(_momentum), Player,
        new Dictionary<string, double> { [Player] = 1.0 + 0.2 * _org },
        new Dictionary<string, double>(_attribution));

    private double AttributionTarget(string id) =>
        id == _government.Leader ? 1.0 : _government.Parties.Contains(id) ? 0.7 : 0.0;

    private double Relation(string a, string b) =>
        a == Player ? _relations.GetValueOrDefault(b, DefaultRelation)
        : b == Player ? _relations.GetValueOrDefault(a, DefaultRelation) : DefaultRelation;

    public Role PlayerRole =>
        _government.Leader == _scenario.PlayerParty ? Role.Leader
        : _government.Parties.Contains(_scenario.PlayerParty) ? Role.Partner : Role.Opposition;

    private bool PlayerIsFormateur => _phase == Phase.Formation && _attempt >= 1 && _attempt <= _queue.Count
        && _queue[_attempt - 1] == _scenario.PlayerParty;

    private static IReadOnlyDictionary<string, T> Sorted<T>(IReadOnlyDictionary<string, T> d) =>
        d.OrderBy(k => k.Key, Ord).ToDictionary(k => k.Key, k => k.Value);

    public CountryPolitics Snapshot() => new(
        _phase, ElectionIn, Sorted(_poll), Sorted(_seats), _government.Parties.ToList(), _government.Leader,
        _scenario.PlayerParty, _government,
        _phase == Phase.Formation && _attempt >= 1 && _attempt <= _queue.Count
            ? new FormationInfo(_queue[_attempt - 1], _attempt, _turnsLeft, PlayerIsFormateur, _offer) : null,
        PlayerRole,
        new SortedDictionary<string, double>(_loyalty, Ord),
        _government.Commitments.Select(c => new CommitmentStatus(c.PartyId, c.Demand.Id,
            CommitmentMath.Progress(c, Current(c)), CommitmentMath.Expected(c, _lastTurn, _government.FormedTurn))).ToList(),
        _ultimatums.Select(kv => new UltimatumInfo(kv.Key, kv.Value.DemandId, kv.Value.Left)).ToList(),
        new PartyStatus(_pc, _org, _credibility, Cohesion(),
            new SortedDictionary<string, double>(_relations, Ord), new SortedDictionary<string, double>(_momentum, Ord),
            new SortedDictionary<string, double>(_pressure, Ord),
            _parties[Player].Platform.OrderBy(k => k.Key, Ord).ToDictionary(k => k.Key, k => k.Value.Target),
            _measures.ToList()),
        new SortedDictionary<string, double>(_grudge, Ord));

    private double Current(Commitment c) => _lookup(c.Demand.Kind, c.Demand.Subject);

    /// <summary>Error key when the player may not change policies right now, otherwise null.</summary>
    public string? PolicyBlock() =>
        _phase != Phase.Governing ? "error.no_majority" : PlayerRole != Role.Leader ? "error.not_leader" : null;

    /// <summary>Capital cannot be banked without limit: a party that never acts does not stockpile an unbeatable campaign.</summary>
    public const double MaxCapital = 30.0;

    private static readonly double HalfLifeFactor = DetMath.Pow(0.5, 0.25); // half-life of 4 turns

    /// <summary>Per-turn upkeep of the player's party: decay, recovery and political capital income.</summary>
    private void TickParty()
    {
        _history.Add(_valueIds.ToDictionary(id => id, id => _lookup("value", id), Ord));
        if (_history.Count > 5) _history.RemoveAt(0);
        double share = _poll.GetValueOrDefault(Player);
        _org = Math.Max(0.0, _org - 0.01);
        _credibility = Math.Min(1.0, _credibility + 0.1 * _dt);
        foreach (var id in _relations.Keys.ToList())
        {
            double r = _relations[id];
            _relations[id] = r > DefaultRelation ? Math.Max(DefaultRelation, r - 0.02) : Math.Min(DefaultRelation, r + 0.02);
        }
        foreach (var id in _momentum.Keys.ToList()) _momentum[id] *= HalfLifeFactor;
        foreach (var id in _pressure.Keys.ToList()) _pressure[id] *= HalfLifeFactor;
        foreach (var id in _attribution.Keys.ToList())
            _attribution[id] = AttributionTarget(id) + (_attribution[id] - AttributionTarget(id)) * HalfLifeFactor;
        foreach (var key in _grudge.Keys.ToList())
        {
            double g = _grudge[key] * GrudgeDecay;
            if (g < 0.005) _grudge.Remove(key); else _grudge[key] = g;
        }
        _pc = Math.Min(MaxCapital, _pc + 2.0 + 8.0 * _org * share + (_government.Leader == Player ? 2.0 : 0.0));
    }

    private bool TrySpend(double cost)
    {
        if (_pc + 1e-12 < cost) return false;
        _pc -= cost;
        return true;
    }

    private static ActionResult Fail(string key, string? detail = null) => new(false, key, detail);

    public ActionResult Execute(PlayerAction action, int reportTurn) => action switch
    {
        ProposeCoalition p => DoPropose(p.Partners, p.Agreements, reportTurn),
        AcceptOffer => DoAcceptOffer(reportTurn),
        RejectOffer => DoRejectOffer(reportTurn),
        CallEarlyElection => DoCallEarlyElection(),
        BuildOrganization => DoBuild(),
        Court c => CourtParty(c.PartyId),
        ShiftPlatform s => Shift(s.PolicyId, s.Target),
        Campaign c => CampaignGroup(c.GroupId, c.Effort),
        Criticize c => DoCriticize(c.ValueId),
        TakeStance t => DoTakeStance(t.MeasureId, t.Stance),
        ProposePolicy p => DoProposePolicy(p.PolicyId, p.Target),
        LeaveCoalition => DoLeave(),
        DismissPartner d => DoDismiss(d.PartyId),
        NoConfidence => DoNoConfidence(),
        _ => Fail("error.unknown_action", action.GetType().Name),
    };

    private ActionResult DoBuild()
    {
        if (!TrySpend(4)) return Fail("error.not_enough_capital");
        _org = Math.Min(1.0, _org + 0.04);
        return ActionResult.Ok;
    }

    private ActionResult CourtParty(string partyId)
    {
        if (partyId == Player || !_relations.ContainsKey(partyId)) return Fail("error.unknown_party", partyId);
        if (!TrySpend(3)) return Fail("error.not_enough_capital");
        _relations[partyId] = Math.Min(1.0, _relations[partyId] + 0.1);
        return ActionResult.Ok;
    }

    private ActionResult Shift(string policyId, double target)
    {
        if (!_policyDefs.TryGetValue(policyId, out var def)) return Fail("error.unknown_policy", policyId);
        var party = _parties[Player];
        if (!party.Platform.TryGetValue(policyId, out var pos)) return Fail("error.not_in_platform", policyId);
        if (!double.IsFinite(target)) return Fail("error.value_out_of_range", policyId);
        if (!TrySpend(1)) return Fail("error.not_enough_capital");
        double range = def.Max - def.Min;
        double delta = range > 0 ? Math.Clamp(target - pos.Target, -0.1 * range, 0.1 * range) : 0.0;
        if (delta != 0.0)
        {
            var platform = party.Platform.ToDictionary(k => k.Key, k => k.Value, Ord);
            platform[policyId] = pos with { Target = Math.Clamp(pos.Target + delta, def.Min, def.Max) };
            var updated = party with { Platform = platform };
            _parties[Player] = updated;
            _support.UpdateParty(updated);
            _credibility = Math.Max(0.0, _credibility - 0.5 * Math.Abs(delta) / range);
        }
        return ActionResult.Ok;
    }

    /// <summary>Called by the game whenever the AI governor changes a policy, so the opposition can take a stance on it.</summary>
    public void RecordMeasure(int turn, string policyId, double from, double to) =>
        _measures.Add(new MeasureInfo($"{turn}:{policyId}", turn, policyId, from, to, false));

    private ActionResult DoCriticize(string valueId)
    {
        if (PlayerRole == Role.Leader) return Fail("error.not_opposition");
        if (!_valueIds.Contains(valueId)) return Fail("error.unknown_value", valueId);
        var caring = _groups.Where(g => g.ValueWeights.GetValueOrDefault(valueId) != 0.0).OrderBy(g => g.Id, Ord).ToList();
        double maxWeight = caring.Count == 0 ? 0.0 : caring.Max(g => Math.Abs(g.ValueWeights[valueId]));
        double now = _lookup("value", valueId);
        double then = _history.Count > 0 ? _history[0][valueId] : now;
        double delta = now - then;
        if (!(maxWeight > 0) || Math.Abs(delta) <= 1e-9 * Math.Max(1.0, Math.Abs(now))) return Fail("error.no_effect", valueId);
        if (!TrySpend(2)) return Fail("error.not_enough_capital");
        foreach (var g in caring)
        {
            double w = g.ValueWeights[valueId], scale = Math.Abs(w) / maxWeight;
            bool worse = Math.Sign(w) * Math.Sign(delta) < 0;
            if (worse) { _momentum[g.Id] += 0.15 * scale; _pressure[g.Id] += 0.10 * scale; }
            else _momentum[g.Id] -= 0.10 * scale;
        }
        return ActionResult.Ok;
    }

    private ActionResult DoTakeStance(string measureId, Stance stance)
    {
        if (PlayerRole == Role.Leader) return Fail("error.not_opposition");
        int i = _measures.FindIndex(m => m.Id == measureId);
        if (i < 0) return Fail("error.unknown_measure", measureId);
        if (_measures[i].StanceTaken) return Fail("error.already_taken", measureId);
        if (!TrySpend(1)) return Fail("error.not_enough_capital");
        var m = _measures[i];
        double totalSize = _groups.Sum(g => g.Size);
        foreach (var g in _groups.OrderBy(g => g.Id, Ord))
        {
            if (!g.PolicyPrefs.TryGetValue(m.PolicyId, out var pref) || !(totalSize > 0)) continue;
            double before = Math.Abs(pref - m.From), after = Math.Abs(pref - m.To);
            if (after == before) continue;
            bool approves = after < before;
            bool good = stance == Stance.Support ? approves : !approves;
            _momentum[g.Id] += (good ? 0.10 : -0.10) * g.Size / totalSize;
        }
        _measures[i] = m with { StanceTaken = true };
        return ActionResult.Ok;
    }

    private ActionResult CampaignGroup(string groupId, int effort)
    {
        if (_groups.All(g => g.Id != groupId)) return Fail("error.unknown_group", groupId);
        if (effort < 1 || effort > 3) return Fail("error.bad_effort", effort.ToString());
        if (!TrySpend(3.0 * effort)) return Fail("error.not_enough_capital");
        _momentum[groupId] += 0.06 * effort * (ElectionIn <= 4 ? 2.0 : 1.0);
        return ActionResult.Ok;
    }

    /// <summary>What the AI would propose if the player's party were formateur now; null when it is not.</summary>
    public CoalitionProposal? Suggest() => PlayerIsFormateur ? BuildFor(_scenario.PlayerParty) : null;

    private ActionResult DoPropose(IReadOnlyList<string> partners, IReadOnlyList<AgreedDemand> agreements, int reportTurn)
    {
        if (!PlayerIsFormateur) return new ActionResult(false, "error.not_formateur", null);
        var proposal = new CoalitionProposal(_scenario.PlayerParty,
            partners.Where(p => p != _scenario.PlayerParty).Distinct(Ord).OrderBy(p => p, Ord).ToList(), agreements.Distinct().ToList());
        var refusals = CoalitionRules.Evaluate(proposal, _parties, _seats, _rules.MajorityFraction, _rules.ProgramSlots, Relation, Resent);
        if (refusals.Count > 0)
            return new ActionResult(false, "error.refused." + refusals[0].Reason, $"{refusals[0].PartyId}:{refusals[0].DemandId}");
        _pending.Add(Form(reportTurn, proposal));
        return ActionResult.Ok;
    }

    private ActionResult DoAcceptOffer(int reportTurn)
    {
        if (_offer == null) return new ActionResult(false, "error.no_offer", null);
        _pending.Add(Form(reportTurn, _offer));
        return ActionResult.Ok;
    }

    private ActionResult DoRejectOffer(int reportTurn)
    {
        if (_offer == null) return new ActionResult(false, "error.no_offer", null);
        _pending.Add(new OfferLapsed(reportTurn));
        _excluded.Add(_scenario.PlayerParty);
        _offer = null;
        return ActionResult.Ok;
    }

    public const double ProposalCost = 3.0, ProposalMaxMoveOfRange = 0.25, ProposalThreshold = 0.5;

    private ActionResult DoProposePolicy(string policyId, double target)
    {
        if (_phase != Phase.Governing || PlayerRole != Role.Partner) return Fail("error.not_partner");
        if (!_policyDefs.TryGetValue(policyId, out var def)) return Fail("error.unknown_policy", policyId);
        if (!double.IsFinite(target) || target < def.Min || target > def.Max) return Fail("error.value_out_of_range", policyId);
        if (!TrySpend(ProposalCost)) return Fail("error.not_enough_capital");
        _proposals.Add((policyId, target));
        return ActionResult.Ok;
    }

    /// <summary>The leader's answer to the player's proposals, from relations, the player's weight in the government and fit with its own platform.</summary>
    private void DecideProposals(int turn, List<PoliticalEvent> events)
    {
        var proposals = _proposals.ToList();
        _proposals.Clear();
        if (_phase != Phase.Governing || PlayerRole != Role.Partner) return;
        string leader = _government.Leader;
        double govSeats = _government.Parties.Sum(p => _seats.GetValueOrDefault(p));
        double share = govSeats > 0 ? _seats.GetValueOrDefault(Player) / govSeats : 0.0;
        foreach (var (policyId, target) in proposals)
        {
            var def = _policyDefs[policyId];
            double range = def.Max - def.Min, current = _lookup("policy", policyId);
            double next = Math.Clamp(current + Math.Clamp(target - current, -ProposalMaxMoveOfRange * range, ProposalMaxMoveOfRange * range), def.Min, def.Max);
            bool redLine = _government.Parties.Where(p => p != Player).Any(p => _parties[p].RedLines.Any(r =>
                r.Policy == policyId && LoyaltyModel.RedLineViolated(r, next) && !LoyaltyModel.RedLineViolated(r, current)));
            double fit = _parties[leader].Platform.TryGetValue(policyId, out var pos) && range > 0
                ? 1.0 - Math.Min(1.0, Math.Abs(target - pos.Target) / range) : 0.5;
            double score = 0.4 * Relation(leader, Player) + 0.2 * share + 0.4 * fit;
            if (!redLine && score >= ProposalThreshold)
            {
                events.Add(new PolicyProposalAccepted(turn, policyId, target));
                _approved.Add((policyId, target));
                _relations[leader] = Math.Min(1.0, _relations.GetValueOrDefault(leader, DefaultRelation) + 0.05);
            }
            else
            {
                events.Add(new PolicyProposalRejected(turn, policyId, redLine ? "red_line" : "score"));
                _relations[leader] = Math.Max(0.0, _relations.GetValueOrDefault(leader, DefaultRelation) - 0.03);
            }
        }
    }

    /// <summary>Proposals the leader agreed to this turn (policy id, requested target); the game turns them into government measures.</summary>
    public IReadOnlyList<(string PolicyId, double Target)> TakeApprovedProposals()
    {
        var list = _approved.ToList();
        _approved.Clear();
        return list;
    }

    private ActionResult DoLeave()
    {
        if (_phase != Phase.Governing) return Fail("error.wrong_phase");
        if (PlayerRole != Role.Partner) return Fail("error.not_partner");
        _leaveQueued = true;
        return ActionResult.Ok;
    }

    private ActionResult DoDismiss(string partyId)
    {
        if (_phase != Phase.Governing) return Fail("error.wrong_phase");
        if (PlayerRole != Role.Leader) return Fail("error.not_leader");
        if (partyId == Player || !_government.Parties.Contains(partyId)) return Fail("error.unknown_party", partyId);
        if (!CoalitionRules.HasMajority(_government.Parties.Where(p => p != partyId), _seats, _rules.MajorityFraction))
            return Fail("error.would_lose_majority", partyId);
        if (!TrySpend(4)) return Fail("error.not_enough_capital");
        _dismissQueue.Add(partyId);
        return ActionResult.Ok;
    }

    private ActionResult DoNoConfidence()
    {
        if (_phase != Phase.Governing) return Fail("error.wrong_phase");
        if (PlayerRole != Role.Opposition) return Fail("error.not_opposition");
        if (!TrySpend(10)) return Fail("error.not_enough_capital");
        _noConfidenceQueued = true;
        return ActionResult.Ok;
    }

    /// <summary>Applies the player's queued exits at the start of the governing step. Returns true when the government fell.</summary>
    private bool ProcessExits(int turn, List<PoliticalEvent> events)
    {
        bool leave = _leaveQueued, confidence = _noConfidenceQueued;
        var dismissals = _dismissQueue.ToList();
        _leaveQueued = false; _noConfidenceQueued = false; _dismissQueue.Clear();

        if (leave && PlayerRole == Role.Partner)
        {
            string leader = _government.Leader;
            events.Add(new PlayerLeftCoalition(turn));
            Depart(Player, turn, events, announce: false);
            _credibility = Math.Max(0.0, _credibility - 0.15);
            _relations[leader] = Math.Max(0.0, _relations.GetValueOrDefault(leader, DefaultRelation) - 0.3);
            if (FallIfNoMajority(turn, events)) return true;
        }
        if (PlayerRole == Role.Leader)
            foreach (var partner in dismissals)
            {
                if (!_government.Parties.Contains(partner)
                    || !CoalitionRules.HasMajority(_government.Parties.Where(p => p != partner), _seats, _rules.MajorityFraction)) continue;
                events.Add(new PartnerDismissed(turn, partner));
                Depart(partner, turn, events, announce: false);
                AddGrudge(partner, _government.Leader, 0.5);
            }
        if (confidence && PlayerRole == Role.Opposition)
        {
            double total = _seats.Values.Sum();
            var willing = _seats.Where(kv => kv.Value > 0 && !_government.Parties.Contains(kv.Key)
                    && (kv.Key == Player || Relation(Player, kv.Key) >= 0.4))
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, Ord).Select(kv => kv.Key).ToList();
            double seats = willing.Sum(p => _seats[p])
                + _government.Parties.Where(p => p != _government.Leader && _loyalty.GetValueOrDefault(p, 100) < 40).Sum(p => _seats.GetValueOrDefault(p));
            if (seats > _rules.MajorityFraction * total)
            {
                events.Add(new NoConfidencePassed(turn));
                events.Add(new GovernmentFell(turn));
                foreach (var p in _government.Parties.Where(p => p != _government.Leader)) AddGrudge(p, _government.Leader, 0.2);
                BeginFormation(turn, events, willing.Count > 0 ? willing[0] : _government.Leader);
                return true;
            }
            events.Add(new NoConfidenceFailed(turn));
            _credibility = Math.Max(0.0, _credibility - 0.2);
        }
        return false;
    }

    /// <summary>The sitting government fell if it lost its majority: the leader gets the first try at a new one.</summary>
    private bool FallIfNoMajority(int turn, List<PoliticalEvent> events)
    {
        if (CoalitionRules.HasMajority(_government.Parties, _seats, _rules.MajorityFraction)) return false;
        events.Add(new GovernmentFell(turn));
        foreach (var p in _government.Parties.Where(p => p != _government.Leader)) AddGrudge(p, _government.Leader, 0.2);
        BeginFormation(turn, events, _government.Leader);
        return true;
    }

    private ActionResult DoCallEarlyElection()
    {
        if (_phase != Phase.Governing) return new ActionResult(false, "error.wrong_phase", null);
        if (PlayerRole != Role.Leader) return new ActionResult(false, "error.not_leader", null);
        _turnsToElection = 1;
        return ActionResult.Ok;
    }

    private readonly Dictionary<string, PolicyDef> _policyDefs;

    /// <summary>The governor's move for this turn; none unless the government is sitting and the player does not lead it.</summary>
    public (string PolicyId, double Value)? GovernorMove(int turn, Func<string, double> latestPolicy) =>
        _phase == Phase.Governing && PlayerRole != Role.Leader
            ? Governor.Move(_government, latestPolicy, id => _lookup("value", id), _policyDefs, turn,
                _parties.GetValueOrDefault(_government.Leader),
                _government.Parties.Where(p => p != _government.Leader).SelectMany(p => _parties[p].RedLines).ToList())
            : null;

    /// <summary>One turn of politics. <paramref name="turn"/> is the turn number the report will carry.</summary>
    public List<PoliticalEvent> Step(int turn, IReadOnlyDictionary<string, double> satisfaction)
    {
        _lastTurn = turn;
        var events = new List<PoliticalEvent>(_pending);
        _pending.Clear();
        TickParty();
        var effective = Effective(satisfaction);
        _poll = _support.Compute(Input(satisfaction), _rng);

        bool electionDue = --_turnsToElection <= 0;
        if (!electionDue && _phase == Phase.Caretaker) electionDue = --_caretakerLeft <= 0;
        if (electionDue) { _proposals.Clear(); _leaveQueued = false; _noConfidenceQueued = false; _dismissQueue.Clear(); HoldElection(turn, events); return events; }
        if (_phase == Phase.Formation) StepFormation(turn, events);
        else if (_phase == Phase.Governing)
        {
            if (!ProcessExits(turn, events)) { StepLoyalty(turn, effective, events); DecideProposals(turn, events); }
            else _proposals.Clear();
        }
        else { _proposals.Clear(); _leaveQueued = false; _noConfidenceQueued = false; _dismissQueue.Clear(); }
        return events;
    }

    private void HoldElection(int turn, List<PoliticalEvent> events)
    {
        var votes = new SortedDictionary<string, double>(Ord);
        foreach (var (id, share) in _poll)
        {
            double sum = 0;
            for (int k = 0; k < 12; k++) sum += _rng.NextDouble();
            votes[id] = Math.Max(0.0, share + 0.01 * (sum - 6.0));
        }
        double total = votes.Values.Sum();
        if (!(total > 0)) foreach (var id in votes.Keys.ToList()) votes[id] = _poll[id];
        else foreach (var id in votes.Keys.ToList()) votes[id] /= total;
        _votes = votes;
        _seats = _electoral.Allocate(votes);
        events.Add(new ElectionHeld(turn, Sorted(votes), Sorted(_seats)));
        _turnsToElection = _rules.ElectionIntervalTurns;

        BeginFormation(turn, events, null);
    }

    /// <summary>Starts a formation round. The queue is by seats, votes and id; <paramref name="first"/> (if seated) tries first.</summary>
    private void BeginFormation(int turn, List<PoliticalEvent> events, string? first)
    {
        _queue = _seats.Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key == first ? 0 : 1)
            .ThenByDescending(kv => kv.Value).ThenByDescending(kv => _votes.GetValueOrDefault(kv.Key)).ThenBy(kv => kv.Key, Ord)
            .Select(kv => kv.Key).ToList();
        _phase = Phase.Formation;
        _attempt = 1;
        _turnsLeft = _rules.MaxFormationTurns;
        _caretakerLeft = 0;
        _excluded.Clear();
        _offer = null;
        events.Add(new FormationStarted(turn, _queue[0]));
    }

    private void StepFormation(int turn, List<PoliticalEvent> events)
    {
        if (_offer != null)
        {
            events.Add(new OfferLapsed(turn));
            _excluded.Add(_scenario.PlayerParty);
            _offer = null;
            return;
        }
        string formateur = _queue[_attempt - 1];
        if (PlayerIsFormateur)
        {
            if (--_turnsLeft <= 0) FailAttempt(turn, formateur, events);
            return;
        }
        var proposal = BuildFor(formateur);
        if (proposal == null) { FailAttempt(turn, formateur, events); return; }
        if (proposal.Partners.Contains(_scenario.PlayerParty))
        {
            _offer = proposal;
            events.Add(new OfferMade(turn, proposal));
            return;
        }
        events.Add(Form(turn, proposal));
    }

    private CoalitionProposal? BuildFor(string formateur) =>
        CoalitionBuilder.Build(formateur, _parties, _seats, _ranges, _rules.MajorityFraction, _rules.ProgramSlots,
            Relation, _excluded, Resent);

    private void FailAttempt(int turn, string formateur, List<PoliticalEvent> events)
    {
        events.Add(new FormationFailed(turn, formateur));
        _attempt++;
        _excluded.Clear();
        _offer = null;
        if (_attempt > 2 || _attempt > _queue.Count)
        {
            _phase = Phase.Caretaker;
            _caretakerLeft = _rules.CaretakerTurns;
            events.Add(new EarlyElectionsCalled(turn));
            return;
        }
        _turnsLeft = _rules.MaxFormationTurns;
    }

    /// <summary>Makes the proposal the government (commitments start from the current values).</summary>
    private GovernmentFormed Form(int turn, CoalitionProposal proposal)
    {
        var commitments = new List<Commitment>();
        foreach (var a in proposal.Agreements)
        {
            var d = _parties[a.PartyId].Demands.First(x => x.Id == a.DemandId);
            commitments.Add(new Commitment(a.PartyId, d, _lookup(d.Kind, d.Subject), turn + d.DueTurns));
        }
        var parties = new[] { proposal.Formateur }.Concat(proposal.Partners).Distinct(Ord).OrderBy(x => x, Ord).ToList();
        _government = new Government(proposal.Formateur, parties, commitments, turn);
        _phase = Phase.Governing;
        _offer = null;
        _caretakerLeft = 0;
        ResetLoyalty();
        return new GovernmentFormed(turn, _government);
    }

    private static string RedKey(string party, RedLineDef r) => $"{party}|{r.Policy}|{r.Op}";

    /// <summary>Every partner (government parties but the leader) starts at the same loyalty; current red-line violations are not "crossings".</summary>
    private void ResetLoyalty()
    {
        _loyalty.Clear(); _warned.Clear(); _ultimatums.Clear(); _redLinesActive.Clear();
        foreach (var p in _government.Parties.Where(p => p != _government.Leader))
        {
            _loyalty[p] = LoyaltyModel.StartLoyalty(_loyaltyParams.Start, Relation(_government.Leader, p), Resent(p, _government.Leader));
            foreach (var r in _parties[p].RedLines)
                if (LoyaltyModel.RedLineViolated(r, _lookup("policy", r.Policy))) _redLinesActive.Add(RedKey(p, r));
        }
    }

    private void StepLoyalty(int turn, IReadOnlyDictionary<string, double> satisfaction, List<PoliticalEvent> events)
    {
        var lp = _loyaltyParams;
        bool departed = false;
        foreach (var partner in _government.Parties.Where(p => p != _government.Leader).ToList())
        {
            var party = _parties[partner];
            var mine = _government.Commitments.Where(c => c.PartyId == partner).ToList();
            double fulfil = CommitmentMath.Fulfil(mine, Current, turn, _government.FormedTurn);
            double shortfall = CommitmentMath.Shortfall(mine, Current, turn, _government.FormedTurn);
            double mood = LoyaltyModel.BaseMood(party, _groups, satisfaction);

            bool violated = false, crossed = false;
            foreach (var r in party.RedLines)
            {
                bool now = LoyaltyModel.RedLineViolated(r, _lookup("policy", r.Policy));
                string key = RedKey(partner, r);
                if (now) { violated = true; if (_redLinesActive.Add(key)) crossed = true; }
                else _redLinesActive.Remove(key);
            }

            double target = LoyaltyModel.Target(fulfil, shortfall, mood, violated);
            double loyalty = LoyaltyModel.Step(_loyalty.GetValueOrDefault(partner, lp.Start), target,
                party.Swing ? lp.SwingLambda : lp.Lambda, _dt);
            if (crossed) loyalty = Math.Max(0.0, loyalty - lp.Shock);
            _loyalty[partner] = loyalty;

            var late = CommitmentMath.MostLate(mine, Current, turn, _government.FormedTurn);
            if (loyalty < lp.WarnBelow) { if (_warned.Add(partner)) events.Add(new LoyaltyWarning(turn, partner, late?.Demand.Id)); }
            else if (loyalty >= lp.WarnReset) _warned.Remove(partner);

            bool leave = loyalty <= lp.LeaveAt;
            if (_ultimatums.TryGetValue(partner, out var ult))
            {
                var named = ult.DemandId == null ? null : mine.FirstOrDefault(c => c.Demand.Id == ult.DemandId);
                bool delivered = named != null && CommitmentMath.Progress(named, Current(named)) + 1e-9 >= CommitmentMath.Expected(named, turn, _government.FormedTurn);
                if (loyalty > lp.ResolveAbove || delivered) _ultimatums.Remove(partner);
                else if (ult.Left - 1 <= 0) leave = true;
                else _ultimatums[partner] = (ult.DemandId, ult.Left - 1);
            }
            else if (!leave && loyalty < lp.UltimatumBelow)
            {
                _ultimatums[partner] = (late?.Demand.Id, lp.UltimatumTurns);
                events.Add(new Ultimatum(turn, partner, late?.Demand.Id, lp.UltimatumTurns));
            }

            if (leave) { Depart(partner, turn, events); departed = true; }
        }

        // The sitting government fell if it lost its majority: the leader gets the first try at a new one.
        if (departed) FallIfNoMajority(turn, events);
    }

    private void Depart(string partner, int turn, List<PoliticalEvent> events, bool announce = true)
    {
        if (announce) events.Add(new PartnerLeft(turn, partner));
        int unmet = _government.Commitments.Count(c => c.PartyId == partner
            && CommitmentMath.Progress(c, Current(c)) + 1e-9 < CommitmentMath.Expected(c, turn, _government.FormedTurn));
        AddGrudge(partner, _government.Leader, 0.5 + 0.1 * unmet);
        AddGrudge(_government.Leader, partner, 0.3);
        _government = new Government(_government.Leader, _government.Parties.Where(p => p != partner).ToList(),
            _government.Commitments.Where(c => c.PartyId != partner).ToList(), _government.FormedTurn);
        _loyalty.Remove(partner); _warned.Remove(partner); _ultimatums.Remove(partner);
    }
}
