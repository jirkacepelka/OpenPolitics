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

    public PoliticsEngine(PackSet packs, ScenarioDef scenario, CountryDef country, ulong seed,
        IReadOnlyDictionary<string, double> satisfaction, Func<string, string, double> lookup, double turnsPerYear)
    {
        _scenario = scenario.Politics!;
        _rules = country.Politics!;
        _lookup = lookup;
        _dt = 1.0 / turnsPerYear;
        _groups = packs.VoterGroups;
        _parties = packs.Parties.OrderBy(p => p.Id, Ord).ToDictionary(p => p.Id, Ord);
        _ranges = PartyMath.PolicyRanges(packs.Policies);
        var govParties = _scenario.Government.Concat(_scenario.Leader != null ? new[] { _scenario.Leader } : Array.Empty<string>())
            .Distinct(Ord).OrderBy(x => x, Ord).ToList();
        _government = new Government(_scenario.Leader ?? govParties.FirstOrDefault() ?? "", govParties, new List<Commitment>(), 0);
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

    private SupportInput Input(IReadOnlyDictionary<string, double> satisfaction) => new(
        new SortedDictionary<string, double>(satisfaction.ToDictionary(k => k.Key, k => k.Value), Ord),
        new HashSet<string>(_government.Parties, Ord), new Dictionary<string, double>());

    public Role PlayerRole =>
        _government.Leader == _scenario.PlayerParty ? Role.Leader
        : _government.Parties.Contains(_scenario.PlayerParty) ? Role.Partner : Role.Opposition;

    private bool PlayerIsFormateur => _phase == Phase.Formation && _attempt >= 1 && _attempt <= _queue.Count
        && _queue[_attempt - 1] == _scenario.PlayerParty;

    private static IReadOnlyDictionary<string, T> Sorted<T>(IReadOnlyDictionary<string, T> d) =>
        d.OrderBy(k => k.Key, Ord).ToDictionary(k => k.Key, k => k.Value);

    public CountryPolitics Snapshot() => new(
        _phase, _turnsToElection, Sorted(_poll), Sorted(_seats), _government.Parties.ToList(), _government.Leader,
        _scenario.PlayerParty, _government,
        _phase == Phase.Formation && _attempt >= 1 && _attempt <= _queue.Count
            ? new FormationInfo(_queue[_attempt - 1], _attempt, _turnsLeft, PlayerIsFormateur, _offer) : null,
        PlayerRole,
        new SortedDictionary<string, double>(_loyalty, Ord),
        _government.Commitments.Select(c => new CommitmentStatus(c.PartyId, c.Demand.Id,
            CommitmentMath.Progress(c, Current(c)), CommitmentMath.Expected(c, _lastTurn, _government.FormedTurn))).ToList(),
        _ultimatums.Select(kv => new UltimatumInfo(kv.Key, kv.Value.DemandId, kv.Value.Left)).ToList());

    private double Current(Commitment c) => _lookup(c.Demand.Kind, c.Demand.Subject);

    /// <summary>Error key when the player may not change policies right now, otherwise null.</summary>
    public string? PolicyBlock() =>
        _phase != Phase.Governing ? "error.no_majority" : PlayerRole != Role.Leader ? "error.not_leader" : null;

    /// <summary>What the AI would propose if the player's party were formateur now; null when it is not.</summary>
    public CoalitionProposal? Suggest() => PlayerIsFormateur ? BuildFor(_scenario.PlayerParty) : null;

    public ActionResult Propose(IReadOnlyList<string> partners, IReadOnlyList<AgreedDemand> agreements, int reportTurn)
    {
        if (!PlayerIsFormateur) return new ActionResult(false, "error.not_formateur", null);
        var proposal = new CoalitionProposal(_scenario.PlayerParty,
            partners.Where(p => p != _scenario.PlayerParty).Distinct(Ord).OrderBy(p => p, Ord).ToList(), agreements);
        var refusals = CoalitionRules.Evaluate(proposal, _parties, _seats, _rules.MajorityFraction, _rules.ProgramSlots, (_, _) => DefaultRelation);
        if (refusals.Count > 0)
            return new ActionResult(false, "error.refused." + refusals[0].Reason, $"{refusals[0].PartyId}:{refusals[0].DemandId}");
        _pending.Add(Form(reportTurn, proposal));
        return ActionResult.Ok;
    }

    public ActionResult AcceptOffer(int reportTurn)
    {
        if (_offer == null) return new ActionResult(false, "error.no_offer", null);
        _pending.Add(Form(reportTurn, _offer));
        return ActionResult.Ok;
    }

    public ActionResult RejectOffer(int reportTurn)
    {
        if (_offer == null) return new ActionResult(false, "error.no_offer", null);
        _pending.Add(new OfferLapsed(reportTurn));
        _excluded.Add(_scenario.PlayerParty);
        _offer = null;
        return ActionResult.Ok;
    }

    public ActionResult CallEarlyElection()
    {
        if (_phase != Phase.Governing) return new ActionResult(false, "error.wrong_phase", null);
        if (PlayerRole != Role.Leader) return new ActionResult(false, "error.not_leader", null);
        _turnsToElection = 1;
        return ActionResult.Ok;
    }

    /// <summary>One turn of politics. <paramref name="turn"/> is the turn number the report will carry.</summary>
    public List<PoliticalEvent> Step(int turn, IReadOnlyDictionary<string, double> satisfaction)
    {
        _lastTurn = turn;
        var events = new List<PoliticalEvent>(_pending);
        _pending.Clear();
        _poll = _support.Compute(Input(satisfaction), _rng);

        bool electionDue = --_turnsToElection <= 0;
        if (!electionDue && _phase == Phase.Caretaker) electionDue = --_caretakerLeft <= 0;
        if (electionDue) { HoldElection(turn, events); return events; }
        if (_phase == Phase.Formation) StepFormation(turn, events);
        else if (_phase == Phase.Governing) StepLoyalty(turn, satisfaction, events);
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
            (_, _) => DefaultRelation, _excluded);

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
            _loyalty[p] = _loyaltyParams.Start;
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
        if (departed && !CoalitionRules.HasMajority(_government.Parties, _seats, _rules.MajorityFraction))
        {
            events.Add(new GovernmentFell(turn));
            BeginFormation(turn, events, _government.Leader);
        }
    }

    private void Depart(string partner, int turn, List<PoliticalEvent> events)
    {
        events.Add(new PartnerLeft(turn, partner));
        _government = new Government(_government.Leader, _government.Parties.Where(p => p != partner).ToList(),
            _government.Commitments.Where(c => c.PartyId != partner).ToList(), _government.FormedTurn);
        _loyalty.Remove(partner); _warned.Remove(partner); _ultimatums.Remove(partner);
    }
}
