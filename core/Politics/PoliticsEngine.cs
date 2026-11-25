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
    private readonly ElectoralSystemDef _electoralDef;
    private readonly PackSet _packs;
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
    /// <summary>Decision 0014 (e): how much voters blame each party for the deficit it ran up in government.</summary>
    private readonly SortedDictionary<string, double> _fiscalBlame = new(Ord);
    /// <summary>Decision 0016 (3): credit voters give each party for cutting a high deficit in government (fades like blame).</summary>
    private readonly SortedDictionary<string, double> _fiscalCredit = new(Ord);
    private double _creditCapital;
    private double _lastDeficit;
    /// <summary>Decision 0015 (7): the deficit (surplus = 0) each party inherited when it entered the current government; blame is measured from it.</summary>
    private readonly SortedDictionary<string, double> _deficitBaseline = new(Ord);

    private void SetBaseline(IEnumerable<string> parties)
    {
        double d = _lookup("value", "base:deficit_pct");
        foreach (var p in parties) _deficitBaseline[p] = double.IsFinite(d) ? Math.Max(0.0, d) : 0.0;
    }
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
    private readonly List<(string PolicyId, double Next)> _approved = new();
    private bool _leaveQueued, _noConfidenceQueued;
    private readonly SortedDictionary<string, int> _nextDemand = new(Ord);
    private readonly SortedDictionary<string, (string DemandId, int Due)> _midterm = new(Ord);
    public const int MidTermDueTurns = 8;
    private Func<string, double>? _latest;

    /// <summary>Lets the engine see policy changes that are queued but not yet in effect (the game supplies them).</summary>
    public void UseLatestPolicy(Func<string, double> latest) => _latest = latest;

    private double Latest(string policyId) => _latest != null ? _latest(policyId) : _lookup("policy", policyId);

    /// <summary>Delivered once the value is in effect, or - for a policy - once the change that satisfies it is queued.</summary>
    private bool Delivered(Commitment c) => CommitmentMath.Satisfied(c.Demand, c.Demand.Kind == "policy" ? Latest(c.Demand.Subject) : Current(c));
    private readonly List<string> _dismissQueue = new();

    public PoliticsEngine(PackSet packs, ScenarioDef scenario, CountryDef country, ulong seed,
        IReadOnlyDictionary<string, double> satisfaction, Func<string, string, double> lookup, double turnsPerYear,
        string? playerParty = null, bool randomStart = false)
    {
        _scenario = scenario.Politics!;
        _player = playerParty ?? _scenario.PlayerParty;
        _rules = country.Politics!;
        _portfolios = (_rules.Portfolios ?? Array.Empty<PortfolioDef>()).ToList();
        _packs = packs;
        _lookup = lookup;
        _dt = 1.0 / turnsPerYear;
        _groups = packs.VoterGroups;
        _valueIds = packs.Values.Select(v => v.Id).OrderBy(x => x, Ord).ToList();
        _policyDefs = packs.Policies.ToDictionary(p => p.Id, Ord);
        _parties = packs.Parties.OrderBy(p => p.Id, Ord).ToDictionary(p => p.Id, Ord);
        _ranges = PartyMath.PolicyRanges(packs.Policies);
        _levers = Governor.Levers(packs);
        var govParties = _scenario.Government.Concat(_scenario.Leader != null ? new[] { _scenario.Leader } : Array.Empty<string>())
            .Distinct(Ord).OrderBy(x => x, Ord).ToList();
        var initial = new List<Commitment>();
        foreach (var a in _scenario.Agreements ?? Array.Empty<AgreementDef>())
        {
            var d = _parties[a.Party].Demands.First(x => x.Id == a.Demand);
            initial.Add(new Commitment(a.Party, d, lookup(d.Kind, d.Subject), d.DueTurns));
        }
        _government = new Government(_scenario.Leader ?? govParties.FirstOrDefault() ?? "", govParties, initial, 0);
        foreach (var id in _parties.Keys) if (id != Player) _relations[id] = DefaultRelation;
        foreach (var g in _groups) { _momentum[g.Id] = 0.0; _pressure[g.Id] = 0.0; }
        foreach (var id in _parties.Keys) _attribution[id] = AttributionTarget(id);
        // the player's programme starts lean: the laws the party stresses most (the rest of its platform is for the player to add)
        if (_parties[Player].Platform.Count > PartyRules.StartProgramme)
        {
            var lean = _parties[Player].Platform.OrderByDescending(kv => kv.Value.Weight).ThenBy(kv => kv.Key, Ord).Take(PartyRules.StartProgramme)
                .ToDictionary(kv => kv.Key, kv => kv.Value, Ord);
            _parties[Player] = _parties[Player] with { Platform = new SortedDictionary<string, PlatformPosition>(lean, Ord) };
        }
        _electoralDef = packs.ElectoralSystems.First(e => e.Id == _rules.ElectoralSystem);
        var startSupport = _scenario.Support;
        if (randomStart && _scenario.StartNoise > 0) startSupport = RandomStart(seed);
        _support = new SupportModel(_parties.Values.ToList(), packs.VoterGroups, packs.Policies);
        _support.Calibrate(startSupport, Input(satisfaction));
        _rng = new Pcg32(seed, 99);
        _announceRng = new Pcg32(seed, 103); // its own stream: announcing never shifts the draws of the rest of politics
        _poll = _support.Compute(Input(satisfaction), null);
        _satisfaction = new SortedDictionary<string, double>(satisfaction.ToDictionary(kv => kv.Key, kv => kv.Value), Ord);
        _groupSupportLast = GroupSupport();
        _groupSupportPrev = _groupSupportLast;
        _seats = CurrentSystem().Allocate(_poll);
        _votes = _poll;
        _turnsToElection = _scenario.TurnsToElection;
        _lastDeficit = lookup("value", "base:deficit_pct");
        SetBaseline(_government.Parties);
        ResetLoyalty();
    }

    /// <summary>
    /// A start that differs from game to game (decision 0020): every party's poll share is the scenario's times exp(noise x z), z roughly normal from
    /// its own stream (rescaled to the same total), and the government is formed from the seats those shares give instead of being fixed: every
    /// seated party works out the coalition it could lead, and one of them is drawn, the better and larger ones more often.
    /// </summary>
    private IReadOnlyDictionary<string, double> RandomStart(ulong seed)
    {
        var rng = new Pcg32(seed, 211);
        var shares = new SortedDictionary<string, double>(Ord);
        foreach (var (id, s) in _scenario.Support.OrderBy(k => k.Key, Ord))
        {
            double z = 0;
            for (int k = 0; k < 12; k++) z += rng.NextDouble();
            shares[id] = s * DetMath.Exp(_scenario.StartNoise * (z - 6.0));
        }
        double scale = _scenario.Support.Values.Sum() / shares.Values.Sum();
        foreach (var id in shares.Keys.ToList()) shares[id] *= scale;

        _seats = CurrentSystem().Allocate(shares);
        var proposals = _seats.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id, Ord)
            .Select(id => BuildFor(id)).Where(p => p != null).Select(p => p!).ToList();
        if (proposals.Count == 0) return shares;
        var scores = proposals.Select(p => CoalitionBuilder.MarketScore(p, _parties, _seats, _ranges)).ToList();
        double best = scores.Max();
        var weights = proposals.Select((p, i) => _seats[p.Formateur] * DetMath.Exp(8.0 * (scores[i] - best))).ToList();
        double pick = rng.NextDouble() * weights.Sum();
        int chosen = 0;
        for (; chosen < weights.Count - 1; chosen++) { pick -= weights[chosen]; if (pick <= 0) break; }
        var proposal = proposals[chosen];
        var commitments = new List<Commitment>();
        foreach (var a in proposal.Agreements)
        {
            var d = Negotiation.Effective(_parties[a.PartyId].Demands.First(x => x.Id == a.DemandId), a);
            commitments.Add(new Commitment(a.PartyId, d, _lookup(d.Kind, d.Subject), d.DueTurns));
        }
        var parties = new[] { proposal.Formateur }.Concat(proposal.Partners).Distinct(Ord).OrderBy(x => x, Ord).ToList();
        var holders = new SortedDictionary<string, string>(Ord);
        foreach (var g in proposal.PortfolioList) holders[g.PortfolioId] = g.PartyId;
        _government = new Government(proposal.Formateur, parties, commitments, 0, holders);
        return shares;
    }

    /// <summary>
    /// The electoral rules in force: the country's system, with threshold and method taken from the policies when the pack has them. The system
    /// policy picks the family (decision 0017: proportional, single-member districts, districts with a runoff); a pack without a system of that
    /// kind keeps the country's own.
    /// </summary>
    private ElectoralSystemDef CurrentRules()
    {
        var def = _electoralDef;
        string family = "proportional";
        if (_policyDefs.ContainsKey(PoliticsLimits.SystemPolicy))
        {
            int s = (int)Math.Round(_lookup("policy", PoliticsLimits.SystemPolicy), MidpointRounding.AwayFromZero);
            family = PoliticsLimits.Systems[Math.Clamp(s, 0, PoliticsLimits.Systems.Length - 1)];
        }
        if (family != "proportional")
        {
            var majority = _packs.ElectoralSystems.FirstOrDefault(e => e.Type == family);
            if (majority != null) return majority;
        }
        if (def.Type != "proportional") def = _packs.ElectoralSystems.FirstOrDefault(e => e.Type == "proportional") ?? def;
        if (_policyDefs.ContainsKey(PoliticsLimits.ThresholdPolicy))
            def = def with { Threshold = Math.Clamp(_lookup("policy", PoliticsLimits.ThresholdPolicy) / 100.0, 0.0, 0.10) };
        if (_policyDefs.ContainsKey(PoliticsLimits.MethodPolicy))
        {
            int i = (int)Math.Round(_lookup("policy", PoliticsLimits.MethodPolicy), MidpointRounding.AwayFromZero);
            def = def with { Method = PoliticsLimits.Methods[Math.Clamp(i, 0, PoliticsLimits.Methods.Length - 1)] };
        }
        return def;
    }

    private IElectoralSystem CurrentSystem(IReadOnlyDictionary<string, (double X, double Y)>? extra = null)
    {
        var rules = CurrentRules();
        if (rules.Type == "proportional") return ElectoralSystems.Create(rules);
        var at = Compass().ToDictionary(kv => kv.Key, kv => kv.Value, Ord);
        if (extra != null) foreach (var (id, pos) in extra) at[id] = pos;
        return ElectoralSystems.Create(rules, (a, b) =>
        {
            if (!at.TryGetValue(a, out var pa) || !at.TryGetValue(b, out var pb)) return 1.0;
            return Math.Sqrt((pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y));
        });
    }

    /// <summary>"proportional", "fptp" or "two_round": the kind of electoral system in force at the next election.</summary>
    public string ElectoralKind => CurrentRules().Type;

    public double CurrentThreshold => CurrentRules().Threshold;

    /// <summary>Every party's position on the compass with its current platform (the player's moves when they shift it).</summary>
    public IReadOnlyDictionary<string, (double X, double Y)> Compass() =>
        _parties.ToDictionary(kv => kv.Key, kv => Politics.Compass.Position(kv.Value, _policyDefs), Ord);

    private readonly string _player;

    /// <summary>The player's party: the one chosen at the start, else the scenario's.</summary>
    public string Player => _player;

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

    private SupportInput Input(IReadOnlyDictionary<string, double> satisfaction, IReadOnlyDictionary<string, double>? extraMomentum = null) => new(
        Effective(satisfaction), new HashSet<string>(_government.Parties, Ord),
        new Dictionary<string, double> { [Player] = _credibility * Cohesion() },
        _momentum.ToDictionary(kv => kv.Key, kv => kv.Value + (extraMomentum?.GetValueOrDefault(kv.Key) ?? 0.0), Ord), Player,
        new Dictionary<string, double> { [Player] = 1.0 + PartyRules.OrgTurnout * _org },
        new Dictionary<string, double>(_attribution),
        FiscalPenalty());

    /// <summary>The poll penalty of fiscal blame minus the bonus of fiscal credit (decision 0016), per party.</summary>
    private Dictionary<string, double> FiscalPenalty()
    {
        var penalty = _fiscalBlame.ToDictionary(kv => kv.Key, kv => FiscalBlame.Penalty(kv.Value), Ord);
        foreach (var (party, credit) in _fiscalCredit) penalty[party] = penalty.GetValueOrDefault(party) - FiscalBlame.Bonus(credit);
        return penalty;
    }

    // ---- the player's support in each voter group (decision 0015) ---------------------------------------------
    private SortedDictionary<string, double> _satisfaction = new(Ord);
    private IReadOnlyDictionary<string, double> _groupSupportLast = new Dictionary<string, double>(), _groupSupportPrev = new Dictionary<string, double>();

    /// <summary>The share of each voter group that would vote for the player's party now (no noise), optionally with extra momentum per group.</summary>
    public IReadOnlyDictionary<string, double> GroupSupport(IReadOnlyDictionary<string, double>? extraMomentum = null) =>
        _support.GroupShares(Input(_satisfaction, extraMomentum), Player);

    /// <summary>How many points (share × 100) each group's support for the player's party would move with extra momentum (only groups that move).</summary>
    public IReadOnlyDictionary<string, double> SupportPoints(IReadOnlyDictionary<string, double> extraMomentum)
    {
        var before = GroupSupport();
        var after = GroupSupport(extraMomentum);
        var points = new SortedDictionary<string, double>(Ord);
        foreach (var (g, a) in after)
            if (Math.Abs(a - before.GetValueOrDefault(g)) > 1e-9) points[g] = 100.0 * (a - before.GetValueOrDefault(g));
        return points;
    }

    /// <summary>The momentum a campaign of the given effort would add to its group now (more in the last quarters before an election).</summary>
    public double CampaignMomentum(int effort) =>
        PartyRules.CampaignMomentumPerLevel * effort * (ElectionIn <= PartyRules.CampaignElectionTurns ? PartyRules.CampaignElectionBoost : 1.0);

    private double AttributionTarget(string id) =>
        id == _government.Leader ? 1.0 : _government.Parties.Contains(id) ? 0.7 : 0.0;

    private double Relation(string a, string b) =>
        a == Player ? _relations.GetValueOrDefault(b, DefaultRelation)
        : b == Player ? _relations.GetValueOrDefault(a, DefaultRelation) : DefaultRelation;

    public Role PlayerRole =>
        _government.Leader == Player ? Role.Leader
        : _government.Parties.Contains(Player) ? Role.Partner : Role.Opposition;

    private bool PlayerIsFormateur => _phase == Phase.Formation && _attempt >= 1 && _attempt <= _queue.Count
        && _queue[_attempt - 1] == Player;

    private static IReadOnlyDictionary<string, T> Sorted<T>(IReadOnlyDictionary<string, T> d) =>
        d.OrderBy(k => k.Key, Ord).ToDictionary(k => k.Key, k => k.Value);

    public CountryPolitics Snapshot() => new(
        _phase, ElectionIn, Sorted(_poll), Sorted(_seats), _government.Parties.ToList(), _government.Leader,
        Player, _government,
        _phase == Phase.Formation && _attempt >= 1 && _attempt <= _queue.Count
            ? new FormationInfo(_queue[_attempt - 1], _attempt, _turnsLeft, PlayerIsFormateur, _offer, _replies, _roundsLeft, _counterUsed, _counterAnswer) : null,
        PlayerRole,
        new SortedDictionary<string, double>(_loyalty, Ord),
        _government.Commitments.Select(c => new CommitmentStatus(c.PartyId, c.Demand.Id,
            CommitmentMath.Progress(c, Current(c)), CommitmentMath.Expected(c, _lastTurn, _government.FormedTurn))).ToList(),
        _ultimatums.Select(kv => new UltimatumInfo(kv.Key, kv.Value.DemandId, kv.Value.Left)).ToList(),
        new PartyStatus(_pc, _org, _credibility, Cohesion(),
            new SortedDictionary<string, double>(_relations, Ord), new SortedDictionary<string, double>(_momentum, Ord),
            new SortedDictionary<string, double>(_pressure, Ord),
            _parties[Player].Platform.OrderBy(k => k.Key, Ord).ToDictionary(k => k.Key, k => k.Value.Target),
            _measures.ToList(), _income,
            _parties[Player].Platform.OrderBy(k => k.Key, Ord).ToDictionary(k => k.Key, k => k.Value.Weight),
            _promises.Select(p => p.DemandId).ToList(), PromiseInfos(), _fiscalBlame.GetValueOrDefault(Player),
            GroupSupport(), _groupSupportPrev, _fiscalCredit.GetValueOrDefault(Player), _creditCapital),
        new SortedDictionary<string, double>(_grudge, Ord),
        new ReshuffleInfo(Math.Max(0, _nextReshuffleTurn - (_lastTurn + 1)), _reshuffle != null),
        _openProposal == null ? Array.Empty<ProposalInfo>() : new[] { Info(_openProposal) },
        new SortedDictionary<string, double>(_fiscalBlame, Ord),
        new SortedDictionary<string, double>(_fiscalCredit, Ord),
        UltimatumSnapshot(), Math.Max(0, _nextUltimatumTurn - (_lastTurn + 1)), _alliances.ToList());

    private double Current(Commitment c) => _lookup(c.Demand.Kind, c.Demand.Subject);

    /// <summary>A value goal that already holds is no concession at formation or in a reshuffle (decision 0014).</summary>
    private bool AlreadyMet(DemandDef d) => d.Kind == "value" && CommitmentMath.Satisfied(d, _lookup("value", d.Subject));

    private readonly IReadOnlyDictionary<string, IReadOnlyList<Governor.Lever>> _levers;

    /// <summary>Error key when the player may not change policies right now, otherwise null.</summary>
    public string? PolicyBlock() =>
        PlayerRole != Role.Leader ? "error.not_leader" : _phase != Phase.Governing ? "error.no_majority" : null; // decision 0015: a non-leader hears it is not the leader

    /// <summary>Capital cannot be banked without limit: a party that never acts does not stockpile an unbeatable campaign.</summary>
    public const double MaxCapital = PoliticsLimits.MaxCapital;

    /// <summary>The player's political capital right now.</summary>
    public double Capital => _pc;

    /// <summary>Charges the player's party for a policy change the game accepts; false (and nothing charged) when it cannot pay.</summary>
    public bool TrySpendCapital(double cost) => TrySpend(cost);

    private static readonly double HalfLifeFactor = DetMath.Pow(0.5, 1.0 / PartyRules.MomentumHalfLifeTurns); // half-life of 4 turns

    /// <summary>Per-turn upkeep of the player's party: decay, recovery and political capital income.</summary>
    private void TickParty(int turn, List<PoliticalEvent> events)
    {
        _history.Add(_valueIds.ToDictionary(id => id, id => _lookup("value", id), Ord));
        bool governing = _phase == Phase.Governing && _government.Parties.Contains(Player);
        // decision 0015: a promise counts the quarters of the term it is about - the one after the election that follows it
        foreach (var p in _promises.Where(p => p.Carried)) { p.Elapsed++; if (governing) p.InGovernment++; }
        double deficit = _lookup("value", "base:deficit_pct"), before = _fiscalBlame.GetValueOrDefault(Player);
        if (!double.IsFinite(deficit)) deficit = _lastDeficit;
        bool sitting = _phase == Phase.Governing;
        double prevDeficit = _lastDeficit;
        FiscalBlame.Step(_fiscalBlame, sitting ? _government.Leader : null, sitting ? _government.Parties : Array.Empty<string>(), _deficitBaseline, _lastDeficit, deficit);
        _lastDeficit = deficit;
        double gained = _fiscalBlame.GetValueOrDefault(Player) - before;
        if (gained > 0) _credibility = Math.Max(0.0, _credibility - FiscalBlame.CredibilityPerPoint * gained);
        CreditDeficitCut(turn, events, sitting, prevDeficit, deficit);
        if (_history.Count > 5) _history.RemoveAt(0);
        double share = _poll.GetValueOrDefault(Player);
        _org = Math.Max(0.0, _org - PartyRules.OrgDecay);
        _credibility = Math.Min(1.0, _credibility + PartyRules.CredibilityRecoveryPerYear * _dt);
        foreach (var id in _relations.Keys.ToList())
        {
            double r = _relations[id];
            _relations[id] = r > DefaultRelation ? Math.Max(DefaultRelation, r - PartyRules.RelationDrift) : Math.Min(DefaultRelation, r + PartyRules.RelationDrift);
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
        _income = PoliticsLimits.BaseIncome + PartyRules.OrgIncome * _org * share
            + (_phase == Phase.Governing && _government.Leader == Player ? PoliticsLimits.LeaderIncome : 0.0) + CoalitionCapitalBonus();
        _pc = Math.Clamp(_pc + _income + _creditCapital, 0.0, MaxCapital);
    }

    /// <summary>
    /// Decision 0016 (3): the government's parties earn fiscal credit for a real cut of the deficit this quarter. The player's party also gets a little
    /// political capital and credibility, and the news says what it earned in capital and in points of the poll.
    /// </summary>
    private void CreditDeficitCut(int turn, List<PoliticalEvent> events, bool sitting, double prev, double now)
    {
        var gainedBy = FiscalBlame.CreditStep(_fiscalCredit, sitting ? _government.Leader : null, sitting ? _government.Parties : Array.Empty<string>(), prev, now);
        _creditCapital = 0.0;
        if (!gainedBy.TryGetValue(Player, out var mine) || mine <= 0) return;
        _creditCapital = FiscalBlame.CreditCapital(mine);
        _credibility = Math.Min(1.0, _credibility + FiscalBlame.CredibilityPerPoint * mine);
        // the poll points the new credit is worth now (no noise): the support with it against the support without it
        double withIt = _support.Compute(Input(_satisfaction), null).GetValueOrDefault(Player);
        _fiscalCredit[Player] -= mine;
        double without = _support.Compute(Input(_satisfaction), null).GetValueOrDefault(Player);
        _fiscalCredit[Player] += mine;
        events.Add(new FiscalCreditEarned(turn, Player, _creditCapital, 100.0 * (withIt - without), prev, now));
    }

    private double _income;


    /// <summary>A happy coalition is worth political capital, an unhappy one costs it (up to <see cref="PoliticsLimits.CoalitionIncome"/> per turn either way).</summary>
    private double CoalitionCapitalBonus()
    {
        if (_phase != Phase.Governing) return 0.0;
        switch (PlayerRole)
        {
            case Role.Leader:
                var partners = _government.Parties.Where(p => p != Player).ToList();
                return partners.Count == 0 ? 0.0
                    : PoliticsLimits.CoalitionIncome * Math.Clamp((partners.Average(p => _loyalty.GetValueOrDefault(p, _loyaltyParams.Start)) - 50.0) / 50.0, -1.0, 1.0);
            case Role.Partner:
                return PoliticsLimits.CoalitionIncome * Math.Clamp((Relation(Player, _government.Leader) - DefaultRelation) / (1.0 - DefaultRelation), -1.0, 1.0);
            default:
                return 0.0;
        }
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
        ProposeCoalition p => DoPropose(p.Partners, p.Agreements, reportTurn, p.Portfolios, p.Concessions),
        NegotiateCoalition n => DoNegotiate(new CoalitionProposal(Player, n.Partners, n.Agreements, n.Portfolios, n.Concessions), reportTurn),
        CounterOffer c => DoCounter(c.Agreements, c.Portfolios, reportTurn),
        AcceptOffer => DoAcceptOffer(reportTurn),
        RejectOffer => DoRejectOffer(reportTurn),
        CallEarlyElection => DoCallEarlyElection(),
        BuildOrganization => DoBuild(),
        Court c => CourtParty(c.PartyId),
        ShiftPlatform s => Shift(s.PolicyId, s.Target),
        FormAlliance f => DoFormAlliance(f.Partners, reportTurn),
        LeaveAlliance => DoLeaveAlliance(reportTurn),
        AddToPlatform a => DoAddToPlatform(a.PolicyId),
        RemoveFromPlatform r => DoRemoveFromPlatform(r.PolicyId),
        SetPriority s => DoSetPriority(s.PolicyId, s.Weight),
        MakePromise m => DoPromise(m.DemandId, reportTurn),
        Campaign c => CampaignGroup(c.GroupId, c.Effort),
        Criticize c => DoCriticize(c.ValueId),
        TakeStance t => DoTakeStance(t.MeasureId, t.Stance),
        ProposePolicy p => DoProposeAny(p.PolicyId, p.Target, reportTurn),
        LeaveCoalition => DoLeave(),
        DismissPartner d => DoDismiss(d.PartyId),
        ReshuffleCoalition r => DoReshuffle(r, reportTurn),
        SupportProposal sp => DoAnswer(sp.ProposalId, Stance.Support),
        OpposeProposal op => DoAnswer(op.ProposalId, Stance.Oppose),
        AnnouncePolicy an => DoProposeAny(an.PolicyId, an.Target, reportTurn), // decision 0015: one way to propose; the old verb is an alias
        NoConfidence => DoNoConfidence(),
        IssueUltimatum u => DoIssueUltimatum(u.Kind, u.SubjectId, u.Deadline, reportTurn),
        WithdrawUltimatum => DoWithdrawUltimatum(reportTurn),
        _ => Fail("error.unknown_action", action.GetType().Name),
    };

    private ActionResult DoBuild()
    {
        if (!TrySpend(PartyRules.OrgBuildCost)) return Fail("error.not_enough_capital");
        _org = Math.Min(1.0, _org + PartyRules.OrgBuildGain);
        return ActionResult.Ok;
    }

    private ActionResult CourtParty(string partyId)
    {
        if (partyId == Player || !_relations.ContainsKey(partyId)) return Fail("error.unknown_party", partyId);
        if (!TrySpend(PartyRules.CourtCost)) return Fail("error.not_enough_capital");
        _relations[partyId] = Math.Min(1.0, _relations[partyId] + PartyRules.CourtGain);
        return ActionResult.Ok;
    }

    private ActionResult Shift(string policyId, double target)
    {
        if (!_policyDefs.TryGetValue(policyId, out var def)) return Fail("error.unknown_policy", policyId);
        var party = _parties[Player];
        if (!party.Platform.TryGetValue(policyId, out var pos)) return Fail("error.not_in_platform", policyId);
        if (!double.IsFinite(target)) return Fail("error.value_out_of_range", policyId);
        if (!TrySpend(PartyRules.ShiftCost)) return Fail("error.not_enough_capital");
        double range = def.Max - def.Min;
        double delta = range > 0 ? Math.Clamp(target - pos.Target, -PartyRules.ShiftMaxOfRange * range, PartyRules.ShiftMaxOfRange * range) : 0.0;
        if (delta != 0.0)
        {
            var platform = party.Platform.ToDictionary(k => k.Key, k => k.Value, Ord);
            platform[policyId] = pos with { Target = Math.Clamp(pos.Target + delta, def.Min, def.Max) };
            var updated = party with { Platform = platform };
            _parties[Player] = updated;
            _support.UpdateParty(updated);
            _talkCache.Clear();
            _credibility = Math.Max(0.0, _credibility - PartyRules.ShiftCredibility * Math.Abs(delta) / range);
        }
        return ActionResult.Ok;
    }

    // ---- joint lists before an election (decision 0019) ------------------------------------------------------

    private readonly List<Alliance> _alliances = new();
    private bool _aiAlliancesDone;

    private Alliance MakeAlliance(IEnumerable<string> members, bool byPlayer)
    {
        var ids = members.Distinct(Ord).OrderBy(m => m, Ord).ToList();
        var defs = ids.Select(m => _parties[m]).ToList();
        double distance = AllianceMath.MeanDistance(defs, _ranges);
        var joint = AllianceMath.MergePlatform(defs, ids.Select(m => _poll.GetValueOrDefault(m)).ToList());
        // each member's voters are asked to back the joint programme: the farther it is from the member's own, the more of them stay home
        var loss = ids.ToDictionary(m => m, m => Math.Min(0.6, AllianceRules.LossBase + AllianceRules.LossPerDistance * AllianceMath.DistanceTo(_parties[m], joint, _ranges)), Ord);
        return new Alliance(ids, 1.0 - distance, loss.Values.Average(), joint, byPlayer, loss);
    }

    /// <summary>
    /// The seats parties would win with these joint lists: each list stands as one party (its votes are the members' less what a compromise
    /// loses, its compass position the members' mean), a proportional count holds it to a higher threshold, and the list's seats are then split
    /// among its members by their own votes.
    /// </summary>
    private IReadOnlyDictionary<string, int> AllocateSeats(IReadOnlyDictionary<string, double> votes, IReadOnlyList<Alliance> alliances)
    {
        if (alliances.Count == 0) return CurrentSystem().Allocate(votes);
        var lists = new SortedDictionary<string, double>(votes.ToDictionary(kv => kv.Key, kv => kv.Value, Ord), Ord);
        var factors = new Dictionary<string, double>(Ord);
        var where = new Dictionary<string, (double X, double Y)>(Ord);
        var compass = Compass();
        foreach (var a in alliances)
        {
            double sum = a.Members.Sum(m => votes.GetValueOrDefault(m));
            foreach (var m in a.Members) lists.Remove(m);
            lists[a.ListId] = a.Members.Sum(m => a.Counted(m, votes.GetValueOrDefault(m)));
            factors[a.ListId] = AllianceRules.ThresholdFactor(a.Members.Count);
            where[a.ListId] = sum > 0
                ? (a.Members.Sum(m => compass[m].X * votes.GetValueOrDefault(m)) / sum, a.Members.Sum(m => compass[m].Y * votes.GetValueOrDefault(m)) / sum)
                : (0.0, 0.0);
        }
        var raw = CurrentSystem(where).Allocate(lists, factors);
        var seats = new SortedDictionary<string, int>(Ord);
        foreach (var id in votes.Keys) seats[id] = 0;
        foreach (var (id, n) in raw)
        {
            var list = alliances.FirstOrDefault(a => a.ListId == id);
            if (list == null) seats[id] = n;
            else foreach (var (m, k) in AllianceMath.SplitSeats(n, list.Members, list.Members.ToDictionary(m => m, m => list.Counted(m, votes.GetValueOrDefault(m)), Ord))) seats[m] = k;
        }
        return seats;
    }

    private bool InAlliance(string party) => _alliances.Any(a => a.Members.Contains(party));

    /// <summary>What a joint list of the player's party and these partners would do at the current poll, and who would join. Changes nothing.</summary>
    public AlliancePreview PreviewAlliance(IReadOnlyList<string> partners)
    {
        var ids = partners.Append(Player).Distinct(Ord).OrderBy(m => m, Ord).ToList();
        AlliancePreview Error(string key, string? detail = null) => new(key, detail, ids, 0, 0, 1, Array.Empty<AllianceMemberView>(), AllianceRules.Cost);
        if (_phase != Phase.Governing) return Error("error.wrong_phase");
        if (ElectionIn > AllianceRules.WindowTurns) return Error("error.alliance_too_early", AllianceRules.WindowTurns.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (ids.Count < 2 || ids.Count > AllianceRules.MaxMembers) return Error("error.alliance_size", AllianceRules.MaxMembers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var m in ids)
        {
            if (!_parties.ContainsKey(m) || _poll.GetValueOrDefault(m) <= 0) return Error("error.unknown_party", m);
            if (InAlliance(m)) return Error("error.in_alliance", m);
        }
        return AssessAlliance(MakeAlliance(ids, true));
    }

    private AlliancePreview AssessAlliance(Alliance alliance)
    {
        var alone = AllocateSeats(_poll, _alliances);
        var together = AllocateSeats(_poll, _alliances.Append(alliance).ToList());
        var views = new List<AllianceMemberView>();
        foreach (var m in alliance.Members)
        {
            var others = alliance.Members.Where(x => x != m).Select(x => _parties[x]).ToList();
            double far = others.Average(o => PartyMath.Distance(_parties[m], o, _ranges));
            string? reason = far > AllianceRules.MaxDistance ? "far" : together[m] < alone[m] ? "seats" : null;
            views.Add(new AllianceMemberView(m, alone[m], together[m], m == Player || reason == null, m == Player ? null : reason, alliance.LossOf(m)));
        }
        return new AlliancePreview(null, null, alliance.Members, alliance.Coherence, alliance.Loss, AllianceRules.ThresholdFactor(alliance.Members.Count), views, AllianceRules.Cost);
    }

    /// <summary>The AI parties' own joint lists: the closest pairs, when both would win no fewer seats and together more than apart.</summary>
    private void FormAiAlliances(int turn, List<PoliticalEvent> events)
    {
        _aiAlliancesDone = true;
        var ids = _parties.Keys.Where(id => id != Player && _poll.GetValueOrDefault(id) > 0).OrderBy(id => id, Ord).ToList();
        var pairs = (from a in ids from b in ids where string.CompareOrdinal(a, b) < 0
                     select (A: a, B: b, D: PartyMath.Distance(_parties[a], _parties[b], _ranges)))
            .Where(p => p.D <= AllianceRules.MaxDistance).OrderBy(p => p.D).ThenBy(p => p.A, Ord).ThenBy(p => p.B, Ord).ToList();
        var alone = AllocateSeats(_poll, _alliances);
        int total = alone.Values.Sum();
        // only a party that holds fewer seats than its votes deserve has a reason to look for a partner (and counting seats is the costly part)
        bool Wants(string id) => alone[id] < 0.75 * total * _poll.GetValueOrDefault(id);
        foreach (var p in pairs)
        {
            if (InAlliance(p.A) || InAlliance(p.B) || !(Wants(p.A) || Wants(p.B))) continue;
            var alliance = MakeAlliance(new[] { p.A, p.B }, false);
            var together = AllocateSeats(_poll, _alliances.Append(alliance).ToList());
            if (together[p.A] < alone[p.A] || together[p.B] < alone[p.B] || together[p.A] + together[p.B] <= alone[p.A] + alone[p.B]) continue;
            _alliances.Add(alliance);
            alone = together;
            events.Add(new AllianceFormed(turn, alliance.Members, false));
        }
    }

    private ActionResult DoFormAlliance(IReadOnlyList<string> partners, int reportTurn)
    {
        var preview = PreviewAlliance(partners);
        if (preview.ErrorKey != null) return Fail(preview.ErrorKey, preview.Detail);
        if (preview.Views.FirstOrDefault(v => !v.Accepts) is { } no) return Fail("error.alliance_refused", no.PartyId + ":" + no.Reason);
        if (!TrySpend(AllianceRules.Cost)) return Fail("error.not_enough_capital");
        var made = MakeAlliance(preview.Members, true);
        _alliances.Add(made);
        // standing on a programme that contradicts one's own costs the party its credibility with its own voters
        _credibility = Math.Max(0.0, _credibility - AllianceRules.JoinCredibilityPerDistance * AllianceMath.DistanceTo(_parties[Player], made.Platform, _ranges));
        foreach (var m in preview.Members.Where(m => m != Player && _relations.ContainsKey(m))) _relations[m] = Math.Min(1.0, _relations[m] + 0.1);
        _pending.Add(new AllianceFormed(reportTurn, preview.Members, true));
        return ActionResult.Ok;
    }

    private ActionResult DoLeaveAlliance(int reportTurn)
    {
        var mine = _alliances.FirstOrDefault(a => a.Members.Contains(Player));
        if (mine == null) return Fail("error.no_alliance");
        _alliances.Remove(mine);
        _credibility = Math.Max(0.0, _credibility - AllianceRules.LeaveCredibility);
        foreach (var m in mine.Members.Where(m => m != Player && _relations.ContainsKey(m))) _relations[m] = Math.Max(0.0, _relations[m] - 0.1);
        _pending.Add(new AllianceBroken(reportTurn, mine.Members));
        return ActionResult.Ok;
    }

    private void SetPlatform(IReadOnlyDictionary<string, PlatformPosition> platform)
    {
        var updated = _parties[Player] with { Platform = platform };
        _parties[Player] = updated;
        _support.UpdateParty(updated);
        _talkCache.Clear();
    }

    /// <summary>A law joins the programme at the setting now in force and a middling priority; the party then moves it with Shift.</summary>
    private ActionResult DoAddToPlatform(string policyId)
    {
        if (!_policyDefs.TryGetValue(policyId, out var def)) return Fail("error.unknown_policy", policyId);
        var party = _parties[Player];
        if (party.Platform.ContainsKey(policyId)) return Fail("error.already_in_platform", policyId);
        if (party.Platform.Count >= PartyRules.PlatformMax) return Fail("error.platform_full", PartyRules.PlatformMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!TrySpend(PartyRules.AddCost)) return Fail("error.not_enough_capital");
        var platform = party.Platform.ToDictionary(k => k.Key, k => k.Value, Ord);
        platform[policyId] = new PlatformPosition(Math.Clamp(Latest(policyId), def.Min, def.Max), PartyRules.AddWeight);
        SetPlatform(platform);
        return ActionResult.Ok;
    }

    /// <summary>A law leaves the programme: the party no longer stands for it (or against it), at a price in capital and credibility.</summary>
    private ActionResult DoRemoveFromPlatform(string policyId)
    {
        var party = _parties[Player];
        if (!party.Platform.ContainsKey(policyId)) return Fail("error.not_in_platform", policyId);
        if (party.Platform.Count <= PartyRules.PlatformMin) return Fail("error.platform_min", PartyRules.PlatformMin.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!TrySpend(PartyRules.RemoveCost)) return Fail("error.not_enough_capital");
        var platform = party.Platform.ToDictionary(k => k.Key, k => k.Value, Ord);
        platform.Remove(policyId);
        SetPlatform(platform);
        _credibility = Math.Max(0.0, _credibility - PartyRules.RemoveCredibility);
        return ActionResult.Ok;
    }

    /// <summary>
    /// A promise to voters (decisions 0014, 0015): "what we will do if you elect us". Made in the window before an election E1, it is carried over E1
    /// (<see cref="Carried"/>) and judged once, at the election E2 that ends the next term; it counts only if the party governed at least half of
    /// that term (Elapsed and InGovernment count the quarters since E1).
    /// </summary>
    private sealed class PromiseRecord(string demandId, int madeTurn)
    {
        public string DemandId { get; } = demandId;
        public int MadeTurn { get; } = madeTurn;
        public bool Carried;
        public int Elapsed, InGovernment;
    }
    private readonly List<PromiseRecord> _promises = new();

    /// <summary>The party answers for a promise when it sat in the government in at least half of the quarters since it was made.</summary>
    private static bool Responsible(PromiseRecord p) => p.Elapsed > 0 && 2 * p.InGovernment >= p.Elapsed;

    private ActionResult DoSetPriority(string policyId, double weight)
    {
        var party = _parties[Player];
        if (!party.Platform.TryGetValue(policyId, out var pos)) return Fail("error.not_in_platform", policyId);
        if (!double.IsFinite(weight) || weight < 0.0 || weight > 1.0) return Fail("error.value_out_of_range", policyId);
        if (Math.Abs(weight - pos.Weight) < 1e-9) return Fail("error.no_change", policyId);
        if (!TrySpend(PartyRules.PriorityCost)) return Fail("error.not_enough_capital");
        double delta = Math.Clamp(weight - pos.Weight, -PartyRules.PriorityStep, PartyRules.PriorityStep);
        if (delta != 0.0)
        {
            var platform = party.Platform.ToDictionary(k => k.Key, k => k.Value, Ord);
            platform[policyId] = pos with { Weight = Math.Clamp(pos.Weight + delta, 0.0, 1.0) };
            var updated = party with { Platform = platform };
            _parties[Player] = updated;
            _support.UpdateParty(updated);
            _talkCache.Clear();
            _credibility = Math.Max(0.0, _credibility - PartyRules.PriorityCredibility * Math.Abs(delta));
        }
        return ActionResult.Ok;
    }

    private ActionResult DoPromise(string demandId, int reportTurn)
    {
        if (_phase == Phase.Formation) return Fail("error.wrong_phase");
        var demand = _parties[Player].Demands.FirstOrDefault(d => d.Id == demandId);
        if (demand == null) return Fail("error.unknown_demand", demandId);
        if (ElectionIn > PoliticsLimits.PromiseWindowTurns) return Fail("error.too_early");
        if (CommitmentMath.Satisfied(demand, _lookup(demand.Kind, demand.Subject))) return Fail("error.already_met", demandId);
        if (_promises.Any(p => p.DemandId == demandId)) return Fail("error.already_promised", demandId);
        // the limit is per election window: promises carried over from the previous cycle (judged at the coming election) do not count
        if (_promises.Count(p => !p.Carried) >= PoliticsLimits.MaxPromises) return Fail("error.too_many_promises");
        if (!TrySpend(PartyRules.PromiseCost)) return Fail("error.not_enough_capital");
        _promises.Add(new PromiseRecord(demandId, reportTurn));
        var affinity = _parties[Player].Affinity;
        double top = affinity.Count == 0 ? 0.0 : affinity.Values.Max();
        if (top > 0)
            foreach (var g in _groups.OrderBy(g => g.Id, Ord))
                if (affinity.GetValueOrDefault(g.Id) is var a && a > 0) _momentum[g.Id] += PartyRules.PromiseMomentum * a / top;
        return ActionResult.Ok;
    }

    /// <summary>
    /// At an election the voters check the promises made before the previous one (decision 0015): a party that governed at least half of the term
    /// since answers for them - kept earns trust and goodwill, broken costs both; a party that was (mostly) in opposition could not deliver: no
    /// penalty, and no credit for what the government did either. Promises made before this election are carried over to the next one.
    /// </summary>
    private void SettlePromises(int turn, List<PoliticalEvent> events)
    {
        var affinity = _parties[Player].Affinity;
        foreach (var p in _promises.ToList())
        {
            if (!p.Carried)
            {
                p.Carried = true; p.Elapsed = 0; p.InGovernment = 0;
                events.Add(new PromiseCarried(turn, Player, p.DemandId, _rules.ElectionIntervalTurns));
                continue;
            }
            _promises.Remove(p);
            var d = _parties[Player].Demands.First(x => x.Id == p.DemandId);
            bool kept = CommitmentMath.Satisfied(d, _lookup(d.Kind, d.Subject));
            bool responsible = Responsible(p);
            events.Add(kept ? new PromiseKept(turn, Player, p.DemandId, responsible) : new PromiseBroken(turn, Player, p.DemandId, responsible));
            if (!responsible) continue;
            _credibility = kept ? Math.Min(1.0, _credibility + PartyRules.PromiseKeptCredibility) : Math.Max(0.0, _credibility - PartyRules.PromiseBrokenCredibility);
            foreach (var g in _groups.OrderBy(g => g.Id, Ord))
                if (affinity.GetValueOrDefault(g.Id) > 0) _momentum[g.Id] += kept ? 0.05 : -0.08;
        }
    }

    private IReadOnlyList<PromiseInfo> PromiseInfos() => _promises.Select(p =>
    {
        var d = _parties[Player].Demands.First(x => x.Id == p.DemandId);
        return new PromiseInfo(p.DemandId, p.MadeTurn, p.Elapsed, p.InGovernment, CommitmentMath.Satisfied(d, _lookup(d.Kind, d.Subject)),
            p.Carried, p.Carried ? ElectionIn : ElectionIn + _rules.ElectionIntervalTurns);
    }).ToList();

    /// <summary>Called by the game whenever the AI governor changes a policy, so the opposition can take a stance on it.</summary>
    public void RecordMeasure(int turn, string policyId, double from, double to, bool stanceTaken = false)
    {
        string id = $"{turn}:{policyId}";
        for (int n = 2; _measures.Any(m => m.Id == id); n++) id = $"{turn}:{policyId}#{n}";
        _measures.Add(new MeasureInfo(id, turn, policyId, from, to, stanceTaken));
    }

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
        if (!TrySpend(PartyRules.CriticizeCost)) return Fail("error.not_enough_capital");
        foreach (var g in caring)
        {
            double w = g.ValueWeights[valueId], scale = Math.Abs(w) / maxWeight;
            bool worse = Math.Sign(w) * Math.Sign(delta) < 0;
            if (worse) { _momentum[g.Id] += PartyRules.CriticizeMomentum * scale; _pressure[g.Id] += PartyRules.CriticizePressure * scale; }
            else _momentum[g.Id] -= PartyRules.CriticizeBackfire * scale;
        }
        return ActionResult.Ok;
    }

    private ActionResult DoTakeStance(string measureId, Stance stance)
    {
        if (PlayerRole == Role.Leader) return Fail("error.not_opposition");
        int i = _measures.FindIndex(m => m.Id == measureId);
        if (i < 0) return Fail("error.unknown_measure", measureId);
        if (_measures[i].StanceTaken) return Fail("error.already_taken", measureId);
        if (!TrySpend(PartyRules.StanceCost)) return Fail("error.not_enough_capital");
        var m = _measures[i];
        double totalSize = _groups.Sum(g => g.Size);
        foreach (var g in _groups.OrderBy(g => g.Id, Ord))
        {
            if (!g.PolicyPrefs.TryGetValue(m.PolicyId, out var pref) || !(totalSize > 0)) continue;
            double before = Math.Abs(pref - m.From), after = Math.Abs(pref - m.To);
            if (after == before) continue;
            bool approves = after < before;
            bool good = stance == Stance.Support ? approves : !approves;
            _momentum[g.Id] += (good ? PartyRules.StanceMomentum : -PartyRules.StanceMomentum) * g.Size / totalSize;
        }
        _measures[i] = m with { StanceTaken = true };
        return ActionResult.Ok;
    }

    private ActionResult CampaignGroup(string groupId, int effort)
    {
        if (_groups.All(g => g.Id != groupId)) return Fail("error.unknown_group", groupId);
        if (effort < 1 || effort > 3) return Fail("error.bad_effort", effort.ToString());
        if (!TrySpend(PartyRules.CampaignCostPerLevel * effort)) return Fail("error.not_enough_capital");
        _momentum[groupId] += CampaignMomentum(effort);
        return ActionResult.Ok;
    }

    /// <summary>What the AI would propose if the player's party were formateur now; null when it is not.</summary>
    public CoalitionProposal? Suggest() => PlayerIsFormateur ? BuildFor(Player) : null;

    private ActionResult DoPropose(IReadOnlyList<string> partners, IReadOnlyList<AgreedDemand> agreements, int reportTurn,
        IReadOnlyList<PortfolioGrant>? portfolios = null, IReadOnlyList<Concession>? concessions = null)
    {
        if (!PlayerIsFormateur) return new ActionResult(false, "error.not_formateur", null);
        var proposal = Normalize(new CoalitionProposal(Player, partners, agreements, portfolios, concessions));
        var refusals = CoalitionRules.Evaluate(proposal, _parties, _seats, _rules.MajorityFraction, _rules.ProgramSlots, Relation, Resent, alreadyMet: AlreadyMet, talks: Talks);
        if (refusals.Count > 0)
            return new ActionResult(false, "error.refused." + refusals[0].Reason, $"{refusals[0].PartyId}:{refusals[0].DemandId}");
        return FormOnTerms(proposal, reportTurn);
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
        _excluded.Add(Player);
        _offer = null;
        return ActionResult.Ok;
    }

    public const double ProposalCost = PoliticsLimits.ProposeToLeaderCost, ProposalMaxMoveOfRange = 0.25, ProposalThreshold = 0.5;

    /// <summary>A partner's proposal goes to the leader (decided at the end of the quarter); any other party's goes public (decision 0015).</summary>
    private bool ProposesToLeader => _phase == Phase.Governing && PlayerRole == Role.Partner;

    /// <summary>
    /// Decision 0015: one "propose a policy" for every party that does not lead a sitting government. A partner pays
    /// <see cref="ProposalCost"/> and the leader answers at the end of the quarter; anyone else pays <see cref="PoliticsLimits.AnnounceCost"/> and
    /// the proposal goes public (an AI-led government may adopt a popular one). Either way the voters react at once (<see cref="Reaction"/>), one
    /// proposal a quarter, the same policy again after <see cref="PoliticsLimits.AnnounceCooldownTurns"/> quarters.
    /// </summary>
    private ActionResult DoProposeAny(string policyId, double target, int reportTurn)
    {
        if (ProposalBlock(policyId, target, reportTurn) is { } error) return error;
        bool toLeader = ProposesToLeader;
        if (!TrySpend(toLeader ? ProposalCost : PoliticsLimits.AnnounceCost)) return Fail("error.not_enough_capital");
        _lastAnnounceTurn = reportTurn;
        _announceAllowed[policyId] = reportTurn + PoliticsLimits.AnnounceCooldownTurns;
        var reaction = Reaction(policyId, target);
        foreach (var (group, warmth) in reaction.Groups) _momentum[group] += warmth;
        _credibility = Math.Max(0.0, _credibility + reaction.Credibility);
        if (toLeader) _proposals.Add((policyId, target));
        else _announcement = reaction.MayBeAdopted ? (policyId, target) : null;
        return ActionResult.Ok;
    }

    /// <summary>Why the player cannot propose this now (refused for free), or null.</summary>
    private ActionResult? ProposalBlock(string policyId, double target, int reportTurn)
    {
        if (_phase == Phase.Governing && PlayerRole == Role.Leader) return Fail("error.not_for_leader");
        if (!_policyDefs.TryGetValue(policyId, out var def)) return Fail("error.unknown_policy", policyId);
        if (!double.IsFinite(target) || target < def.Min || target > def.Max) return Fail("error.value_out_of_range", policyId);
        double current = Latest(policyId), range = def.Max - def.Min;
        if (Math.Abs(target - current) <= 1e-9 * Math.Max(1.0, range)) return Fail("error.no_change", policyId);
        if (_lastAnnounceTurn == reportTurn) return Fail("error.announce_limit");
        // the same policy again only after a cooldown for public proposals; a partner may press the leader again next quarter
        if (!ProposesToLeader && _announceAllowed.TryGetValue(policyId, out var allowed) && reportTurn < allowed)
            return Fail("error.announce_cooldown", (allowed - reportTurn).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return null;
    }

    /// <summary>What proposing would do now: who decides, cost, the voters' reaction in warmth and in points of support, the leader's likely answer.</summary>
    public ProposalPreview PreviewProposal(string policyId, double target, int reportTurn)
    {
        var reaction = Reaction(policyId, target);
        bool toLeader = ProposesToLeader;
        (bool Agrees, string? Reason)? leader = toLeader && _policyDefs.ContainsKey(policyId) ? LeaderAnswer(policyId, target) : null;
        return new ProposalPreview(toLeader, toLeader ? ProposalCost : PoliticsLimits.AnnounceCost, reaction, leader?.Agrees, leader?.Reason,
            SupportPoints(reaction.Groups), ProposalBlock(policyId, target, reportTurn)?.ErrorKey);
    }

    /// <summary>The leader's answer to a partner's proposal: from relations, the player's weight in the government and fit with its own platform.</summary>
    private (bool Agrees, string? Reason) LeaderAnswer(string policyId, double target)
    {
        string leader = _government.Leader;
        double govSeats = _government.Parties.Sum(p => _seats.GetValueOrDefault(p));
        double share = govSeats > 0 ? _seats.GetValueOrDefault(Player) / govSeats : 0.0;
        var def = _policyDefs[policyId];
        double range = def.Max - def.Min, current = Latest(policyId);
        double next = Math.Clamp(current + Math.Clamp(target - current, -ProposalMaxMoveOfRange * range, ProposalMaxMoveOfRange * range), def.Min, def.Max);
        bool redLine = _government.Parties.Where(p => p != Player).Any(p => _parties[p].RedLines.Any(r =>
            r.Policy == policyId && LoyaltyModel.RedLineViolated(r, next) && !LoyaltyModel.RedLineViolated(r, current)));
        double fit = _parties[leader].Platform.TryGetValue(policyId, out var pos) && range > 0
            ? 1.0 - Math.Min(1.0, Math.Abs(target - pos.Target) / range) : 0.5;
        double score = 0.4 * Relation(leader, Player) + 0.2 * share + 0.4 * fit
            + (HoldsPortfolioFor(Player, policyId) ? PoliticsLimits.PortfolioProposalBonus : 0.0); // decision 0016: a partner is heard on its own portfolio
        return redLine ? (false, "red_line") : score >= ProposalThreshold ? (true, null) : (false, "score");
    }

    /// <summary>The leader's answer to the player's proposals, from relations, the player's weight in the government and fit with its own platform.</summary>
    private void DecideProposals(int turn, List<PoliticalEvent> events)
    {
        var proposals = _proposals.ToList();
        _proposals.Clear();
        if (_phase != Phase.Governing || PlayerRole != Role.Partner) return;
        string leader = _government.Leader;
        foreach (var (policyId, target) in proposals)
        {
            var def = _policyDefs[policyId];
            double range = def.Max - def.Min, current = Latest(policyId);
            double next = Math.Clamp(current + Math.Clamp(target - current, -ProposalMaxMoveOfRange * range, ProposalMaxMoveOfRange * range), def.Min, def.Max);
            var (agrees, reason) = LeaderAnswer(policyId, target);
            if (agrees)
            {
                events.Add(new PolicyProposalAccepted(turn, policyId, target));
                _approved.Add((policyId, next));
                _relations[leader] = Math.Min(1.0, _relations.GetValueOrDefault(leader, DefaultRelation) + 0.05);
            }
            else
            {
                events.Add(new PolicyProposalRejected(turn, policyId, reason!));
                _relations[leader] = Math.Max(0.0, _relations.GetValueOrDefault(leader, DefaultRelation) - 0.03);
            }
        }
    }

    /// <summary>Proposals the leader agreed to this turn (policy id, requested target); the game turns them into government measures.</summary>
    public IReadOnlyList<(string PolicyId, double Next)> TakeApprovedProposals()
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
        if (!TrySpend(PartyRules.DismissCost)) return Fail("error.not_enough_capital");
        _dismissQueue.Add(partyId);
        return ActionResult.Ok;
    }

    private ActionResult DoNoConfidence()
    {
        if (_phase != Phase.Governing) return Fail("error.wrong_phase");
        if (PlayerRole != Role.Opposition) return Fail("error.not_opposition");
        if (!TrySpend(PartyRules.NoConfidenceCost)) return Fail("error.not_enough_capital");
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
            if (_myUltimatum != null) { _myUltimatum = null; _nextUltimatumTurn = turn + PoliticsLimits.UltimatumCooldownTurns; }
            Depart(Player, turn, events, announce: false);
            _credibility = Math.Max(0.0, _credibility - PartyRules.LeaveCredibility);
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
        if (_reshuffle != null)
        {
            if (PlayerRole == Role.Leader) ProcessReshuffle(turn, events);
            else CancelReshuffle();
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
                _government.Parties.Where(p => p != _government.Leader).SelectMany(p => _parties[p].RedLines).ToList(), _levers)
            : null;

    /// <summary>One turn of politics. <paramref name="turn"/> is the turn number the report will carry.</summary>
    public List<PoliticalEvent> Step(int turn, IReadOnlyDictionary<string, double> satisfaction)
    {
        _lastTurn = turn;
        _talkCache.Clear();
        _roundsLeft = PoliticsLimits.NegotiationRoundsPerQuarter;
        var events = new List<PoliticalEvent>(_pending);
        _pending.Clear();
        TickParty(turn, events);
        // Voters judge the promises before they vote, so settle them ahead of this turn's poll when it is election day.
        if (_turnsToElection <= 1 || (_phase == Phase.Caretaker && _caretakerLeft <= 1)) SettlePromises(turn, events);
        var effective = Effective(satisfaction);
        _satisfaction = new SortedDictionary<string, double>(satisfaction.ToDictionary(kv => kv.Key, kv => kv.Value), Ord);
        _poll = _support.Compute(Input(satisfaction), _rng);
        NoteSupportMoves(turn, events);

        if (_phase == Phase.Governing && !_aiAlliancesDone && _turnsToElection <= AllianceRules.AiTurn) FormAiAlliances(turn, events);
        bool electionDue = --_turnsToElection <= 0;
        if (!electionDue && _phase == Phase.Caretaker) electionDue = --_caretakerLeft <= 0;
        if (electionDue) { _proposals.Clear(); _leaveQueued = false; _noConfidenceQueued = false; _dismissQueue.Clear(); CancelReshuffle(); LapseProposal(turn, events); LapseUltimatum(turn, events); HoldElection(turn, events); return events; }
        if (_phase == Phase.Formation) { LapseProposal(turn, events); LapseUltimatum(turn, events); StepFormation(turn, events); }
        else if (_phase == Phase.Governing)
        {
            if (!ProcessExits(turn, events))
            {
                SettleProposal(turn, events);
                SettleAnnouncement(turn, events);
                StepLoyalty(turn, effective, events);
                DecideProposals(turn, events);
                OpenRequest(turn, events);
                StepUltimatum(turn, events);
            }
            else { _proposals.Clear(); LapseProposal(turn, events); LapseUltimatum(turn, events); }
        }
        else { _proposals.Clear(); _leaveQueued = false; _noConfidenceQueued = false; _dismissQueue.Clear(); CancelReshuffle(); LapseProposal(turn, events); LapseUltimatum(turn, events); }
        return events;
    }

    /// <summary>Records this quarter's support per group and reports the groups where the player's party moved most (at least half a point).</summary>
    private void NoteSupportMoves(int turn, List<PoliticalEvent> events)
    {
        var now = GroupSupport();
        var moves = now.Select(kv => (Group: kv.Key, Before: _groupSupportLast.GetValueOrDefault(kv.Key, kv.Value), After: kv.Value))
            .Where(m => Math.Abs(m.After - m.Before) >= PoliticsLimits.SupportNewsPoints / 100.0)
            .OrderByDescending(m => Math.Abs(m.After - m.Before)).ThenBy(m => m.Group, Ord).Take(3).ToList();
        if (moves.Count > 0) events.Add(new SupportMoved(turn, Player, moves.Select(m => new GroupMove(m.Group, m.Before, m.After)).ToList()));
        _groupSupportPrev = _groupSupportLast;
        _groupSupportLast = now;
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
        _seats = AllocateSeats(votes, _alliances);
        events.Add(new ElectionHeld(turn, Sorted(votes), Sorted(_seats), CurrentRules().Type, _alliances.Select(a => a.Members).ToList()));
        _alliances.Clear(); _aiAlliancesDone = false;
        _turnsToElection = _rules.ElectionIntervalTurns;

        BeginFormation(turn, events, null);
    }

    /// <summary>Starts a formation round. The queue is by seats, votes and id; <paramref name="first"/> (if seated) tries first.</summary>
    private void BeginFormation(int turn, List<PoliticalEvent> events, string? first)
    {
        _excluded.Clear();
        _replies = Array.Empty<PartnerReply>();
        _roundsLeft = PoliticsLimits.NegotiationRoundsPerQuarter;
        _queue = FormationMarket(first);
        _phase = Phase.Formation;
        _attempt = 1;
        _turnsLeft = _rules.MaxFormationTurns;
        _caretakerLeft = 0;
        _offer = null;
        events.Add(new FormationStarted(turn, _queue[0]));
    }

    /// <summary>
    /// Who tries to form the government, in order. Every seated party works out the coalition it could build; the ones that
    /// can build one are ranked by <see cref="CoalitionBuilder.MarketScore"/> (so the largest party does not always lead),
    /// the ones that cannot follow by seats. <paramref name="first"/> (if seated) always goes first.
    /// </summary>
    private List<string> FormationMarket(string? first)
    {
        var seated = _seats.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
        var scored = new Dictionary<string, double>(Ord);
        foreach (var id in seated)
            if (BuildFor(id) is { } proposal)
                scored[id] = CoalitionBuilder.MarketScore(proposal, _parties, _seats, _ranges);
        return seated
            .OrderBy(id => id == first ? 0 : 1)
            .ThenBy(id => scored.ContainsKey(id) ? 0 : 1)
            .ThenByDescending(id => scored.GetValueOrDefault(id))
            .ThenByDescending(id => _seats[id]).ThenByDescending(id => _votes.GetValueOrDefault(id)).ThenBy(id => id, Ord)
            .ToList();
    }

    private void StepFormation(int turn, List<PoliticalEvent> events)
    {
        if (_offer != null)
        {
            events.Add(new OfferLapsed(turn));
            _excluded.Add(Player);
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
        if (proposal.Partners.Contains(Player))
        {
            _offer = proposal;
            _counterUsed = false; _counterAnswer = null;
            events.Add(new OfferMade(turn, proposal));
            return;
        }
        events.Add(Form(turn, proposal));
    }

    private CoalitionProposal? BuildFor(string formateur) =>
        CoalitionBuilder.Build(formateur, _parties, _seats, _ranges, _rules.MajorityFraction, _rules.ProgramSlots,
            Relation, _excluded, Resent, AlreadyMet, Talks);

    private void FailAttempt(int turn, string formateur, List<PoliticalEvent> events)
    {
        events.Add(new FormationFailed(turn, formateur));
        _replies = Array.Empty<PartnerReply>();
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
            var d = Negotiation.Effective(_parties[a.PartyId].Demands.First(x => x.Id == a.DemandId), a);
            commitments.Add(new Commitment(a.PartyId, d, _lookup(d.Kind, d.Subject), turn + d.DueTurns));
        }
        var parties = new[] { proposal.Formateur }.Concat(proposal.Partners).Distinct(Ord).OrderBy(x => x, Ord).ToList();
        var holders = new SortedDictionary<string, string>(Ord);
        foreach (var g in proposal.PortfolioList) holders[g.PortfolioId] = g.PartyId;
        _government = new Government(proposal.Formateur, parties, commitments, turn, holders);
        _replies = Array.Empty<PartnerReply>();
        SetBaseline(parties); // a new government is measured from the deficit it inherits
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
        _loyalty.Clear(); _warned.Clear(); _ultimatums.Clear(); _redLinesActive.Clear(); _midterm.Clear(); _nextDemand.Clear();
        // The player's own party has no loyalty meter: as a partner it decides itself when to leave.
        foreach (var p in _government.Parties.Where(p => p != _government.Leader && p != Player))
        {
            _nextDemand[p] = 6 + _rng.NextInt(5);
            _loyalty[p] = LoyaltyModel.StartLoyalty(_loyaltyParams.Start, Relation(_government.Leader, p), Resent(p, _government.Leader));
            foreach (var r in _parties[p].RedLines)
                if (LoyaltyModel.RedLineViolated(r, _lookup("policy", r.Policy))) _redLinesActive.Add(RedKey(p, r));
        }
    }

    private void StepLoyalty(int turn, IReadOnlyDictionary<string, double> satisfaction, List<PoliticalEvent> events)
    {
        var lp = _loyaltyParams;
        bool departed = false;
        foreach (var partner in _government.Parties.Where(p => p != _government.Leader && p != Player).ToList())
        {
            var party = _parties[partner];
            // mid-term demands have their own reward and penalty (see StepMidTermDemands), not the gradual shortfall
            var mine = _government.Commitments.Where(c => c.PartyId == partner && !c.MidTerm).ToList();
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

            // decision 0016: a partner that holds cabinet portfolios has a stake in the government
            int held = _government.PortfolioHolders.Count(kv => kv.Value == partner);
            double target = Math.Min(100.0, LoyaltyModel.Target(fulfil, shortfall, mood, violated)
                + Math.Min(PoliticsLimits.PortfolioLoyaltyMax, PoliticsLimits.PortfolioLoyalty * held));
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
        StepMidTermDemands(turn, events);
        if (departed && !PatchMajority(turn, events)) FallIfNoMajority(turn, events);
    }

    /// <summary>Partners whose voters are unhappy (poll below their election result) raise new demands during the term.</summary>
    private void StepMidTermDemands(int turn, List<PoliticalEvent> events)
    {
        foreach (var partner in _government.Parties.Where(p => p != _government.Leader && p != Player).ToList())
        {
            double loyalty = _loyalty.GetValueOrDefault(partner, _loyaltyParams.Start);
            if (_midterm.TryGetValue(partner, out var open))
            {
                var c = _government.Commitments.FirstOrDefault(x => x.PartyId == partner && x.Demand.Id == open.DemandId);
                if (c == null) { _midterm.Remove(partner); continue; }
                if (Delivered(c))
                {
                    _loyalty[partner] = Math.Min(100.0, loyalty + 10.0);
                    events.Add(new DemandMet(turn, partner, open.DemandId));
                    _midterm.Remove(partner);
                }
                else if (turn >= open.Due)
                {
                    _loyalty[partner] = Math.Max(0.0, loyalty - 25.0);
                    events.Add(new DemandFailed(turn, partner, open.DemandId));
                    _midterm.Remove(partner);
                    _government = _government with { Commitments = _government.Commitments.Where(x => x != c).ToList() };
                }
                continue;
            }
            if (!_nextDemand.ContainsKey(partner)) _nextDemand[partner] = 6 + _rng.NextInt(5);
            if (--_nextDemand[partner] > 0) continue;
            if (!(_poll.GetValueOrDefault(partner) < _votes.GetValueOrDefault(partner))) { _nextDemand[partner] = 2; continue; }

            var members = _government.Parties.SelectMany(m => _parties[m].RedLines).ToList();
            var pick = _parties[partner].Demands
                .Where(d => !_government.Commitments.Any(c => c.PartyId == partner && c.Demand.Id == d.Id))
                .Where(d => !CommitmentMath.Satisfied(d, _lookup(d.Kind, d.Subject)))
                .Where(d => !members.Any(r => CoalitionRules.DemandConflictsWithRedLine(d, r)))
                .OrderByDescending(d => d.Weight).ThenBy(d => d.Id, Ord).FirstOrDefault();
            _nextDemand[partner] = 8 + _rng.NextInt(5);
            if (pick == null) continue;
            var demand = pick with { DueTurns = MidTermDueTurns };
            var commitment = new Commitment(partner, demand, _lookup(pick.Kind, pick.Subject), turn + MidTermDueTurns, turn, MidTerm: true);
            _government = _government with { Commitments = _government.Commitments.Concat(new[] { commitment }).ToList() };
            _midterm[partner] = (pick.Id, turn + MidTermDueTurns);
            events.Add(new DemandRaised(turn, partner, pick.Id, MidTermDueTurns));
        }
    }

    // ---- proposals to support or oppose (decision 0014) --------------------------------------------------------

    private sealed class OpenProposal(string id, int turn, ProposalKind kind, string proposer, string policyId, double from, double to)
    {
        public string Id { get; } = id;
        public int Turn { get; } = turn;
        public ProposalKind Kind { get; } = kind;
        public string Proposer { get; } = proposer;
        public string PolicyId { get; } = policyId;
        public double From { get; } = from;
        public double To { get; } = to;
        public Stance? Answer;
    }

    private OpenProposal? _openProposal;
    private int _nextProposalTurn;
    private readonly List<(string PolicyId, double To, bool Answered)> _passed = new();

    /// <summary>How well moving a policy from one setting to another fits the player's programme: platform weight times direction (-1..1).</summary>
    private double Fit(string policyId, double from, double to)
    {
        if (!_parties[Player].Platform.TryGetValue(policyId, out var pos)) return 0.0;
        double before = Math.Abs(from - pos.Target), after = Math.Abs(to - pos.Target);
        return after < before - 1e-12 ? pos.Weight : after > before + 1e-12 ? -pos.Weight : 0.0;
    }

    /// <summary>The government has no majority without the player's seats.</summary>
    private bool PlayerPivotal() => _government.Parties.Contains(Player)
        && !CoalitionRules.HasMajority(_government.Parties.Where(p => p != Player), _seats, _rules.MajorityFraction);

    private ProposalInfo Info(OpenProposal p) => new(p.Id, p.Turn, p.Kind, p.Proposer, p.PolicyId, p.From, p.To,
        Fit(p.PolicyId, p.From, p.To), p.Kind == ProposalKind.Measure && PlayerPivotal(), p.Answer);

    private bool MayPropose(int turn) => _phase == Phase.Governing && _openProposal == null && turn >= _nextProposalTurn;

    /// <summary>
    /// The AI governor's measure of this turn (the game calls this before enacting it): when it touches the player's programme and no proposal was
    /// settled in the last <see cref="PoliticsLimits.ProposalCooldownTurns"/> quarters, it is put to parliament instead and the player's party answers
    /// during the next quarter. Returns the proposal (the game then does not enact the measure now), or null.
    /// </summary>
    public ProposalInfo? OfferMeasure(int turn, string policyId, double from, double to)
    {
        if (!MayPropose(turn) || PlayerRole == Role.Leader || to == from || !_parties[Player].Platform.ContainsKey(policyId)) return null;
        _openProposal = new OpenProposal($"{turn}:{policyId}", turn, ProposalKind.Measure, _government.Leader, policyId, from, to);
        return Info(_openProposal);
    }

    /// <summary>In the player's government an AI partner asks for a change it wants (the least loyal partner with something to ask first).</summary>
    private void OpenRequest(int turn, List<PoliticalEvent> events)
    {
        if (!MayPropose(turn) || PlayerRole != Role.Leader) return;
        foreach (var partner in _government.Parties.Where(p => p != Player)
                     .OrderBy(p => _loyalty.GetValueOrDefault(p, _loyaltyParams.Start)).ThenBy(p => p, Ord))
        {
            var others = _government.Parties.Where(p => p != partner).SelectMany(p => _parties[p].RedLines).ToList();
            if (Governor.Request(_parties[partner], _government.Commitments.Where(c => c.PartyId == partner && c.Demand.Kind == "policy").ToList(),
                    _government.Commitments, Latest, _policyDefs, others) is not { } move) continue;
            _openProposal = new OpenProposal($"{turn}:{move.PolicyId}", turn, ProposalKind.Request, partner, move.PolicyId, Latest(move.PolicyId), move.Value);
            events.Add(new ProposalPut(turn, Info(_openProposal)));
            return;
        }
    }

    /// <summary>The player answers: credibility, relations and voter warmth move at once; whether the change happens is settled at the end of the quarter.</summary>
    private ActionResult DoAnswer(string proposalId, Stance stance)
    {
        var p = _openProposal;
        if (p == null || p.Id != proposalId) return Fail("error.unknown_proposal", proposalId);
        if (p.Answer != null) return Fail("error.already_answered", proposalId);
        p.Answer = stance;
        bool support = stance == Stance.Support;
        double agree = support ? Fit(p.PolicyId, p.From, p.To) : -Fit(p.PolicyId, p.From, p.To); // > 0: the answer follows the programme
        _credibility = Math.Clamp(_credibility + (agree > 0 ? PoliticsLimits.ProposalCredibilityGain : PoliticsLimits.ProposalCredibilityLoss) * agree, 0.0, 1.0);
        if (_relations.ContainsKey(p.Proposer))
        {
            double delta = support ? PoliticsLimits.ProposalRelationSupport : -PoliticsLimits.ProposalRelationOppose;
            if (!support && p.Kind == ProposalKind.Measure && PlayerRole == Role.Partner) delta -= PoliticsLimits.ProposalPartnerStrain;
            _relations[p.Proposer] = Math.Clamp(_relations[p.Proposer] + delta, 0.0, 1.0);
        }
        double totalSize = _groups.Sum(g => g.Size);
        foreach (var g in _groups.OrderBy(g => g.Id, Ord))
        {
            if (!g.PolicyPrefs.TryGetValue(p.PolicyId, out var pref) || !(totalSize > 0)) continue;
            double before = Math.Abs(pref - p.From), after = Math.Abs(pref - p.To);
            if (after == before) continue;
            bool approves = after < before;
            _momentum[g.Id] += (support == approves ? 1.0 : -1.0) * PoliticsLimits.ProposalWarmth * g.Size / totalSize;
        }
        return ActionResult.Ok;
    }

    /// <summary>End of the quarter: a measure passes unless the player's pivotal seats opposed it; a request happens only if the leader supported it.</summary>
    private void SettleProposal(int turn, List<PoliticalEvent> events)
    {
        var p = _openProposal;
        if (p == null) return;
        _openProposal = null;
        _nextProposalTurn = turn + PoliticsLimits.ProposalCooldownTurns;
        bool valid = p.Kind == ProposalKind.Measure
            ? _government.Leader == p.Proposer && PlayerRole != Role.Leader
            : PlayerRole == Role.Leader && _government.Parties.Contains(p.Proposer);
        if (!valid) { events.Add(new ProposalLapsed(turn, p.Kind, p.Proposer, p.PolicyId)); return; }
        if (p.Kind == ProposalKind.Measure)
        {
            if (p.Answer == Stance.Oppose && PlayerPivotal())
            {
                events.Add(new ProposalFailed(turn, p.Kind, p.Proposer, p.PolicyId));
                if (_relations.ContainsKey(p.Proposer)) _relations[p.Proposer] = Math.Max(0.0, _relations[p.Proposer] - PoliticsLimits.ProposalBlockedHit);
                return;
            }
        }
        else if (p.Answer != Stance.Support)
        {
            if (p.Answer == null) { events.Add(new ProposalLapsed(turn, p.Kind, p.Proposer, p.PolicyId)); return; }
            events.Add(new ProposalFailed(turn, p.Kind, p.Proposer, p.PolicyId));
            _loyalty[p.Proposer] = Math.Max(0.0, _loyalty.GetValueOrDefault(p.Proposer, _loyaltyParams.Start) - PoliticsLimits.ProposalLoyaltyLoss);
            return;
        }
        else _loyalty[p.Proposer] = Math.Min(100.0, _loyalty.GetValueOrDefault(p.Proposer, _loyaltyParams.Start) + PoliticsLimits.ProposalLoyaltyGain);
        events.Add(new ProposalPassed(turn, p.Kind, p.Proposer, p.PolicyId, p.From, p.To, p.Answer));
        _passed.Add((p.PolicyId, p.To, p.Answer != null));
    }

    /// <summary>The government changed (election, formation, fall) before the proposal was settled: it comes to nothing.</summary>
    private void LapseProposal(int turn, List<PoliticalEvent> events)
    {
        _announcement = null; // an announcement waiting for the government lapses with it
        if (_openProposal is not { } p) return;
        _openProposal = null;
        _nextProposalTurn = turn + PoliticsLimits.ProposalCooldownTurns;
        events.Add(new ProposalLapsed(turn, p.Kind, p.Proposer, p.PolicyId));
    }

    /// <summary>Proposals that passed this turn (policy, new setting, whether the player answered); the game enacts them as government measures.</summary>
    public IReadOnlyList<(string PolicyId, double To, bool Answered)> TakePassedProposals()
    {
        var list = _passed.ToList();
        _passed.Clear();
        return list;
    }

    // ---- announced policy proposals (decision 0014 g) ----------------------------------------------------------

    private readonly Pcg32 _announceRng;
    private int _lastAnnounceTurn = -1;
    private readonly SortedDictionary<string, int> _announceAllowed = new(Ord);
    private (string PolicyId, double Target)? _announcement;

    /// <summary>What announcing a policy proposal would do: warmth per voter group, the credibility change and the size-weighted approval.</summary>
    public AnnounceReaction Reaction(string policyId, double target)
    {
        if (!_policyDefs.TryGetValue(policyId, out var def)) return new AnnounceReaction(new SortedDictionary<string, double>(Ord), 0, 0, false);
        double current = Latest(policyId), range = def.Max - def.Min;
        var caring = _groups.Where(g => g.PolicyPrefs.ContainsKey(policyId) && g.PolicyWeights.GetValueOrDefault(policyId) > 0).OrderBy(g => g.Id, Ord).ToList();
        double maxWeight = caring.Count == 0 ? 0.0 : caring.Max(g => g.PolicyWeights[policyId]), totalSize = _groups.Sum(g => g.Size), approval = 0.0;
        var groups = new SortedDictionary<string, double>(Ord);
        foreach (var g in caring)
        {
            double pref = g.PolicyPrefs[policyId];
            double delta = range > 0 ? (Math.Abs(pref - current) - Math.Abs(pref - target)) / range : 0.0; // > 0: closer to what the group wants
            groups[g.Id] = Math.Clamp(PoliticsLimits.AnnounceWarmth * delta / 0.1, -PoliticsLimits.AnnounceWarmth, PoliticsLimits.AnnounceWarmth)
                * g.PolicyWeights[policyId] / maxWeight;
            if (totalSize > 0) approval += g.Size * delta / totalSize;
        }
        double fit = Fit(policyId, current, target);
        return new AnnounceReaction(groups, fit < 0 ? PoliticsLimits.AnnounceCredibilityLoss * fit : 0.0, approval,
            _phase == Phase.Governing && PlayerRole == Role.Opposition && approval > PoliticsLimits.AnnouncePopularity);
    }

    /// <summary>End of the quarter: an AI-led government may take up a popular announcement as its own measure.</summary>
    private void SettleAnnouncement(int turn, List<PoliticalEvent> events)
    {
        if (_announcement is not { } a) return;
        _announcement = null;
        if (PlayerRole == Role.Leader || !_policyDefs.TryGetValue(a.PolicyId, out var def)) return;
        string leader = _government.Leader;
        double current = Latest(a.PolicyId), range = def.Max - def.Min;
        double next = Math.Clamp(current + Math.Clamp(a.Target - current, -ProposalMaxMoveOfRange * range, ProposalMaxMoveOfRange * range), def.Min, def.Max);
        if (_parties.GetValueOrDefault(leader)?.Platform.TryGetValue(a.PolicyId, out var pos) == true && pos.Weight > 0.3
            && Math.Abs(next - pos.Target) > Math.Abs(current - pos.Target)) return; // the leader cares and wants the opposite
        if (_government.Parties.Any(p => _parties[p].RedLines.Any(r => r.Policy == a.PolicyId && LoyaltyModel.RedLineViolated(r, next) && !LoyaltyModel.RedLineViolated(r, current))))
            return;
        if (_announceRng.NextDouble() >= PoliticsLimits.AnnounceAdoptChance) return;
        events.Add(new AnnouncementAdopted(turn, leader, a.PolicyId, a.Target));
        _approved.Add((a.PolicyId, next));
    }

    // ---- the player's ultimatum to the leader (decision 0016, item 1) ----------------------------------------------

    private sealed class OpenUltimatum(string kind, string subjectId, string policyId, double from, double target, int deadline, int issued)
    {
        public string Kind { get; } = kind;
        public string SubjectId { get; } = subjectId;
        public string PolicyId { get; } = policyId;
        public double From { get; } = from;
        public double Target { get; } = target;
        public int Deadline { get; } = deadline;
        public int Issued { get; } = issued;
        public int Left = deadline;
        public string? Answer;
    }

    private OpenUltimatum? _myUltimatum;
    private int _nextUltimatumTurn;

    private PlayerUltimatumInfo? UltimatumSnapshot() => _myUltimatum is not { } u ? null
        : new PlayerUltimatumInfo(u.Kind, u.SubjectId, u.PolicyId, u.From, u.Target, u.Left, u.Deadline, u.Answer);

    /// <summary>
    /// The policy and setting an ultimatum asks for: a policy demand's target; a value goal as a move of its strongest lever by
    /// <see cref="PoliticsLimits.UltimatumGoalStepOfRange"/> of the range; a programme position as a move toward it of at most
    /// <see cref="PoliticsLimits.UltimatumMaxMoveOfRange"/>. An error key when there is nothing to ask.
    /// </summary>
    private (string? Error, string? Detail, string PolicyId, double From, double Target) UltimatumTarget(string kind, string subjectId)
    {
        var party = _parties[Player];
        if (kind == "policy")
        {
            if (!_policyDefs.TryGetValue(subjectId, out var def)) return ("error.unknown_policy", subjectId, "", 0, 0);
            if (!party.Platform.TryGetValue(subjectId, out var pos)) return ("error.not_in_platform", subjectId, "", 0, 0);
            double cur = Latest(subjectId), range = def.Max - def.Min, step = PoliticsLimits.UltimatumMaxMoveOfRange * range;
            double target = Math.Clamp(cur + Math.Clamp(pos.Target - cur, -step, step), def.Min, def.Max);
            if (Math.Abs(target - cur) <= 1e-9 * Math.Max(1.0, range)) return ("error.ultimatum_met", subjectId, subjectId, cur, cur);
            return (null, null, subjectId, cur, target);
        }
        if (kind != "demand") return ("error.unknown_action", kind, "", 0, 0);
        var d = party.Demands.FirstOrDefault(x => x.Id == subjectId);
        if (d == null) return ("error.unknown_demand", subjectId, "", 0, 0);
        if (d.Kind == "policy")
        {
            if (!_policyDefs.ContainsKey(d.Subject)) return ("error.unknown_policy", d.Subject, "", 0, 0);
            double cur = Latest(d.Subject);
            if (CommitmentMath.Satisfied(d, cur)) return ("error.ultimatum_met", subjectId, d.Subject, cur, cur);
            return (null, null, d.Subject, cur, d.Target);
        }
        if (CommitmentMath.Satisfied(d, _lookup("value", d.Subject))) return ("error.ultimatum_met", subjectId, "", 0, 0);
        double want = d.Op == "<=" ? -1.0 : 1.0;
        if (_levers.TryGetValue(d.Subject, out var levers))
            foreach (var l in levers.OrderByDescending(l => Math.Abs(l.Sign)).ThenBy(l => l.PolicyId, Ord))
            {
                if (!_policyDefs.TryGetValue(l.PolicyId, out var def) || !(def.Max > def.Min)) continue;
                double cur = Latest(l.PolicyId);
                double target = Math.Clamp(cur + want * Math.Sign(l.Sign) * PoliticsLimits.UltimatumGoalStepOfRange * (def.Max - def.Min), def.Min, def.Max);
                if (Math.Abs(target - cur) > 1e-9) return (null, null, l.PolicyId, cur, target);
            }
        return ("error.not_deliverable", subjectId, "", 0, 0);
    }

    /// <summary>The leader could replace the player's party with outside parties (an AI patch would exist without it).</summary>
    private (int Turn, string Key, bool Value)? _replaceCache;

    private bool CanReplacePlayer()
    {
        // only the end of a quarter changes what the answer depends on (seats, grudges, red lines in force, the government), so it is cached per quarter
        string key = string.Join(",", _government.Parties) + "|" + _government.Commitments.Count;
        if (_replaceCache is { } c && c.Turn == _lastTurn && c.Key == key) return c.Value;
        bool value = ComputeCanReplacePlayer();
        _replaceCache = (_lastTurn, key, value);
        return value;
    }

    private bool ComputeCanReplacePlayer()
    {
        string leader = _government.Leader;
        var kept = _government.Parties.Where(p => p != leader && p != Player).ToList();
        if (CoalitionRules.HasMajority(_government.Parties.Where(p => p != Player), _seats, _rules.MajorityFraction)) return true;
        var excluded = new HashSet<string>(Ord) { Player };
        foreach (var p in _parties.Values)
            if (p.RedLines.Any(r => LoyaltyModel.RedLineViolated(r, _lookup("policy", r.Policy)))) excluded.Add(p.Id);
        return CoalitionBuilder.Complete(leader, kept, AgreementsOf(kept), _parties, _seats, _ranges, _rules.MajorityFraction,
            _rules.ProgramSlots, Relation, excluded, Resent, AlreadyMet, Talks) != null;
    }

    /// <summary>
    /// The AI leader's answer to the player's ultimatum (decision 0016), deterministic: a member's red line newly crossed refuses; otherwise a score
    /// from the relation, the fit with the leader's platform, how much it needs the party (pivotal without a replacement 0.3, pivotal with one 0.15,
    /// not pivotal 0.05), the party's share of the government's seats, the size of the move, the leader's grudge, a portfolio the party holds for the
    /// policy, and <see cref="PoliticsLimits.UltimatumStallGain"/> per quarter already waited.
    /// </summary>
    private (string Answer, string? Reason, double Score, bool Pivotal, bool Patchable) UltimatumAnswer(string policyId, double from, double target, int waited)
    {
        string leader = _government.Leader;
        var def = _policyDefs[policyId];
        double range = Math.Max(1e-12, def.Max - def.Min);
        bool pivotal = PlayerPivotal();
        bool patchable = CanReplacePlayer();
        bool redLine = _government.Parties.Where(p => p != Player).Any(p => _parties[p].RedLines.Any(r =>
            r.Policy == policyId && LoyaltyModel.RedLineViolated(r, target) && !LoyaltyModel.RedLineViolated(r, from)));
        double fit = _parties[leader].Platform.TryGetValue(policyId, out var pos) ? 1.0 - Math.Min(1.0, Math.Abs(target - pos.Target) / range) : 0.5;
        double govSeats = _government.Parties.Sum(p => _seats.GetValueOrDefault(p));
        double share = govSeats > 0 ? _seats.GetValueOrDefault(Player) / govSeats : 0.0;
        double move = Math.Min(1.0, Math.Abs(target - from) / range);
        double need = pivotal ? (patchable ? 0.15 : 0.3) : 0.05;
        double held = HoldsPortfolioFor(Player, policyId) ? PoliticsLimits.PortfolioProposalBonus : 0.0;
        double score = 0.3 * Relation(leader, Player) + 0.3 * fit + need + 0.15 * share - 0.6 * move - 0.2 * Resent(leader, Player) + held
            + PoliticsLimits.UltimatumStallGain * waited;
        string answer = redLine ? "refuse" : score >= PoliticsLimits.UltimatumDeliverAt ? "deliver" : score < PoliticsLimits.UltimatumRefuseBelow ? "refuse" : "stall";
        return (answer, answer == "refuse" ? (redLine ? "red_line" : "score") : null, score, pivotal, patchable);
    }

    private ActionResult? UltimatumBlock(int reportTurn)
    {
        if (_phase != Phase.Governing) return PlayerRole == Role.Partner ? Fail("error.wrong_phase") : Fail("error.not_partner");
        if (PlayerRole != Role.Partner) return Fail("error.not_partner");
        if (_myUltimatum != null) return Fail("error.ultimatum_open");
        if (reportTurn < _nextUltimatumTurn)
            return Fail("error.ultimatum_cooldown", (_nextUltimatumTurn - reportTurn).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return null;
    }

    /// <summary>What an ultimatum would bring now; changes nothing.</summary>
    public UltimatumPreview PreviewUltimatum(string kind, string subjectId, int deadline, int reportTurn)
    {
        int cooldown = Math.Max(0, _nextUltimatumTurn - reportTurn);
        UltimatumPreview Error(string key, string? detail) => new(key, detail, null, 0, 0, PoliticsLimits.UltimatumCost, null, null, null, false, false, cooldown, 0);
        if (UltimatumBlock(reportTurn) is { } block) return Error(block.ErrorKey!, block.Detail);
        if (deadline < PoliticsLimits.UltimatumMinDeadline || deadline > PoliticsLimits.UltimatumMaxDeadline) return Error("error.bad_deadline", null);
        var (error, detail, policyId, from, target) = UltimatumTarget(kind, subjectId);
        if (error != null) return Error(error, detail);
        var first = UltimatumAnswer(policyId, from, target, 0);
        int? after = null;
        if (first.Answer == "deliver") after = 0;
        else if (first.Answer == "stall")
            for (int k = 1; k < deadline && after == null; k++)
                if (UltimatumAnswer(policyId, from, target, k).Answer == "deliver") after = k;
        return new UltimatumPreview(null, null, policyId, from, target, PoliticsLimits.UltimatumCost, first.Answer, first.Reason, after,
            first.Pivotal, first.Patchable, cooldown, first.Score);
    }

    private ActionResult DoIssueUltimatum(string kind, string subjectId, int deadline, int reportTurn)
    {
        if (UltimatumBlock(reportTurn) is { } block) return block;
        if (deadline < PoliticsLimits.UltimatumMinDeadline || deadline > PoliticsLimits.UltimatumMaxDeadline) return Fail("error.bad_deadline");
        var (error, detail, policyId, from, target) = UltimatumTarget(kind, subjectId);
        if (error != null) return Fail(error, detail);
        if (!TrySpend(PoliticsLimits.UltimatumCost)) return Fail("error.not_enough_capital");
        _myUltimatum = new OpenUltimatum(kind, subjectId, policyId, from, target, deadline, reportTurn);
        return ActionResult.Ok;
    }

    private ActionResult DoWithdrawUltimatum(int reportTurn)
    {
        if (_myUltimatum == null) return Fail("error.no_ultimatum");
        _myUltimatum = null;
        _credibility = Math.Max(0.0, _credibility - PoliticsLimits.UltimatumWithdrawCredibility);
        _nextUltimatumTurn = reportTurn + PoliticsLimits.UltimatumCooldownTurns;
        return ActionResult.Ok;
    }

    /// <summary>Whether the player's ultimatum is met by the settings the government has put in (or queued).</summary>
    private bool UltimatumMet(OpenUltimatum u) =>
        u.Kind == "demand" && _parties[Player].Demands.FirstOrDefault(d => d.Id == u.SubjectId) is { Kind: "policy" } d
            ? CommitmentMath.Satisfied(d, Latest(d.Subject))
            : Math.Sign(u.Target - u.From) * (Latest(u.PolicyId) - u.Target) >= -1e-9;

    /// <summary>
    /// End of the quarter (decision 0016): the leader answers the player's ultimatum, delivers when its score allows it (the change follows as a
    /// government measure), and when the deadline passes undelivered the player's party leaves as it threatened.
    /// </summary>
    private void StepUltimatum(int turn, List<PoliticalEvent> events)
    {
        if (_myUltimatum is not { } u) return;
        if (_phase != Phase.Governing || PlayerRole != Role.Partner) { LapseUltimatum(turn, events); return; }
        string leader = _government.Leader;
        bool first = u.Answer == null;
        if (UltimatumMet(u)) { Deliver(u, turn, events, enact: false); return; }
        if (u.Answer != "refuse")
        {
            var (answer, reason, _, _, _) = UltimatumAnswer(u.PolicyId, u.From, u.Target, turn - u.Issued);
            if (first || answer == "deliver")
            {
                u.Answer = answer;
                if (first) events.Add(new UltimatumAnswered(turn, leader, answer, reason, u.PolicyId, u.Target, u.Left - 1));
                if (answer == "refuse") _relations[leader] = Math.Max(0.0, _relations.GetValueOrDefault(leader, DefaultRelation) - PoliticsLimits.UltimatumRelationRefused);
                if (answer == "deliver") { Deliver(u, turn, events, enact: true); return; }
            }
        }
        if (--u.Left > 0) return;
        // the deadline passed undelivered: the party walks out, as it said it would
        _myUltimatum = null;
        _nextUltimatumTurn = turn + PoliticsLimits.UltimatumCooldownTurns;
        events.Add(new UltimatumCarriedOut(turn, leader, u.PolicyId));
        Depart(Player, turn, events, announce: false);
        _credibility = Math.Max(0.0, _credibility - PoliticsLimits.UltimatumCarriedOutCredibility);
        _relations[leader] = Math.Max(0.0, _relations.GetValueOrDefault(leader, DefaultRelation) - 0.3);
        WarmOwnVoters(PoliticsLimits.UltimatumVoterMomentum * 0.5);
        if (!PatchMajority(turn, events)) FallIfNoMajority(turn, events);
    }

    private void Deliver(OpenUltimatum u, int turn, List<PoliticalEvent> events, bool enact)
    {
        string leader = _government.Leader;
        _myUltimatum = null;
        _nextUltimatumTurn = turn + PoliticsLimits.UltimatumCooldownTurns;
        if (enact) _approved.Add((u.PolicyId, u.Target));
        events.Add(new UltimatumDelivered(turn, leader, u.PolicyId, u.Target));
        _credibility = Math.Min(1.0, _credibility + PoliticsLimits.UltimatumDeliveredCredibility);
        _relations[leader] = Math.Max(0.0, _relations.GetValueOrDefault(leader, DefaultRelation) - PoliticsLimits.UltimatumRelationDelivered);
        WarmOwnVoters(PoliticsLimits.UltimatumVoterMomentum);
    }

    /// <summary>The groups that like the player's party warm to it (scaled by affinity, the strongest group by <paramref name="amount"/>).</summary>
    private void WarmOwnVoters(double amount)
    {
        var affinity = _parties[Player].Affinity;
        double top = affinity.Count == 0 ? 0.0 : affinity.Values.Max();
        if (!(top > 0)) return;
        foreach (var g in _groups.OrderBy(g => g.Id, Ord))
            if (affinity.GetValueOrDefault(g.Id) is var a && a > 0) _momentum[g.Id] += amount * a / top;
    }

    /// <summary>Whether a party holds the cabinet portfolio a policy belongs to (decision 0016, item 2).</summary>
    private bool HoldsPortfolioFor(string party, string policyId) =>
        _portfolios.FirstOrDefault(pf => pf.Policies.Contains(policyId)) is { } pf && _government.PortfolioHolders.GetValueOrDefault(pf.Id) == party;

    private void LapseUltimatum(int turn, List<PoliticalEvent> events)
    {
        if (_myUltimatum == null) return;
        _myUltimatum = null;
        _nextUltimatumTurn = turn + PoliticsLimits.UltimatumCooldownTurns;
        events.Add(new UltimatumLapsed(turn));
    }

    // ---- formation talks: prices that respect party differences, portfolios, rounds, counter-offers (decision 0016, item 2) ----

    private readonly List<PortfolioDef> _portfolios;
    private readonly Dictionary<string, double> _talkCache = new(Ord);
    private int _roundsLeft = PoliticsLimits.NegotiationRoundsPerQuarter;
    private IReadOnlyList<PartnerReply> _replies = Array.Empty<PartnerReply>();
    private bool _counterUsed;
    private string? _counterAnswer;

    /// <summary>The terms of talks in this country: the price factor, the distance veto and the cabinet portfolios.</summary>
    private Talks Talks => new(PriceFactor, TooFar, _portfolios);

    private double Cached(string key, Func<double> compute)
    {
        if (_talkCache.TryGetValue(key, out var v)) return v;
        v = compute();
        _talkCache[key] = v;
        return v;
    }

    private double TalkDistance(string a, string b) =>
        Cached($"d|{a}|{b}", () => Negotiation.TalkDistance(_parties[a], _parties[b], _ranges, _policyDefs));

    private bool HasAlternative(string partner, string formateur) =>
        Cached($"alt|{partner}|{formateur}", () => Negotiation.HasAlternative(partner, formateur, _seats, _rules.MajorityFraction, TalkDistance) ? 1.0 : 0.0) > 0.5;

    /// <summary>The talk factor on a partner's price toward a formateur: ideological distance × strength × alternatives (decision 0016).</summary>
    private double PriceFactor(string formateur, string partner) => Cached($"f|{formateur}|{partner}", () =>
        Negotiation.DistanceFactor(TalkDistance(formateur, partner))
        * Negotiation.StrengthFactor(_seats.GetValueOrDefault(partner), _seats.GetValueOrDefault(formateur))
        * Negotiation.AlternativesFactor(HasAlternative(partner, formateur)));

    private bool TooFar(string formateur, string partner) => TalkDistance(formateur, partner) >= Negotiation.DistanceVeto;

    private CoalitionProposal Normalize(CoalitionProposal draft) => new(Player,
        draft.Partners.Where(p => p != Player).Distinct(Ord).OrderBy(p => p, Ord).ToList(), draft.Agreements.Distinct().ToList(),
        draft.PortfolioList.Distinct().ToList(), draft.ConcessionList.Where(c => c.Units != 0).ToList());

    private double ConcessionCost(CoalitionProposal p) => Negotiation.ConcessionCost * p.ConcessionList.Sum(c => Math.Max(0, c.Units));

    /// <summary>The government forms on the player's terms: concessions are paid and win some goodwill.</summary>
    private ActionResult FormOnTerms(CoalitionProposal proposal, int reportTurn)
    {
        if (!TrySpend(ConcessionCost(proposal))) return Fail("error.not_enough_capital");
        foreach (var c in proposal.ConcessionList.Where(c => c.Units > 0 && _relations.ContainsKey(c.PartyId)))
            _relations[c.PartyId] = Math.Min(1.0, _relations[c.PartyId] + PoliticsLimits.ConcessionRelation * c.Units);
        _pending.Add(Form(reportTurn, proposal));
        return ActionResult.Ok;
    }

    /// <summary>
    /// How every invited party sees the player's offer as formateur (decision 0016): its price and what the offer pays, its answer and why, its demands
    /// (acceptable / negotiable / red line / met) and portfolios, the counter-offer it would make and what would fix a refusal. Changes nothing.
    /// </summary>
    public NegotiationView AssessCoalition(CoalitionProposal draft)
    {
        var p = Normalize(draft);
        var talks = Talks;
        int total = _seats.Values.Sum(), needed = (int)Math.Floor(_rules.MajorityFraction * total) + 1;
        int held = new[] { Player }.Concat(p.Partners).Sum(x => _seats.GetValueOrDefault(x));
        var refusals = CoalitionRules.Evaluate(p, _parties, _seats, _rules.MajorityFraction, _rules.ProgramSlots, Relation, Resent, alreadyMet: AlreadyMet, talks: talks);
        string? hard = null, detail = null;
        if (!PlayerIsFormateur) hard = "error.not_formateur";
        else if (refusals.FirstOrDefault(r => r.Reason is "unknown" or "slots" or "majority" or "portfolios") is { } h)
        { hard = "error.refused." + h.Reason; detail = $"{h.PartyId}:{h.DemandId}"; }
        var formateur = _parties[Player];
        var members = new[] { Player }.Concat(p.Partners).Where(_parties.ContainsKey).ToList();
        // every valid agreement with its effective demand
        var agreed = new List<(AgreedDemand A, DemandDef D, double Worth)>();
        foreach (var a in p.Agreements)
            if (_parties.TryGetValue(a.PartyId, out var owner) && CoalitionRules.AgreementWorth(a, owner, formateur, AlreadyMet) is { } aw) agreed.Add((a, aw.Demand, aw.Worth));
        var views = new List<PartnerView>();
        foreach (var partner in p.Partners.Where(_parties.ContainsKey))
            views.Add(AssessPartner(partner, p, agreed, members, talks));
        bool all = hard == null && views.All(v => v.Verdict == "accept");
        return new NegotiationView(views, held, needed, p.Agreements.Count, _rules.ProgramSlots, p.PortfolioList.Count, Negotiation.MaxPartnerPortfolios(_portfolios.Count),
            ConcessionCost(p), hard, detail, all, _roundsLeft);
    }

    private PartnerView AssessPartner(string partner, CoalitionProposal p, List<(AgreedDemand A, DemandDef D, double Worth)> agreed, List<string> members, Talks talks)
    {
        var def = _parties[partner];
        var formateur = _parties[Player];
        double rel = Relation(Player, partner), grudge = Resent(partner, Player);
        double dist = TalkDistance(Player, partner);
        bool alternative = HasAlternative(partner, Player);
        double fd = Negotiation.DistanceFactor(dist), fs = Negotiation.StrengthFactor(_seats.GetValueOrDefault(partner), _seats.GetValueOrDefault(Player));
        double fa = Negotiation.AlternativesFactor(alternative);
        double required = CoalitionRules.RequiredWeight(def, rel, grudge, fd * fs * fa);
        var redLines = members.SelectMany(m => _parties[m].RedLines.Select(r => (Owner: m, Line: r))).ToList();
        string? CrossedBy(DemandDef d) => redLines.FirstOrDefault(x => CoalitionRules.DemandConflictsWithRedLine(d, x.Line)).Owner;

        var demandViews = new List<DemandView>();
        foreach (var d in def.Demands)
        {
            var mine = agreed.FirstOrDefault(x => x.A.PartyId == partner && x.A.DemandId == d.Id);
            bool isAgreed = mine.A != null;
            var effective = isAgreed ? mine.D : d;
            double? anchor = Negotiation.Anchor(formateur, d);
            string? lineOwner = CrossedBy(effective);
            string status = AlreadyMet(d) ? "met" : lineOwner != null ? "red_line" : anchor != null ? "negotiable" : "acceptable";
            demandViews.Add(new DemandView(d.Id, status, d.Weight, isAgreed ? mine.Worth : CoalitionRules.Worth(d, AlreadyMet), anchor,
                anchor is { } an ? Negotiation.HalfWay(d, an) : null, isAgreed, isAgreed ? mine.A!.Target : null, lineOwner));
        }
        var grants = p.PortfolioList;
        var portfolioViews = _portfolios.Select(pf => new PortfolioView(pf.Id, Negotiation.PortfolioWorth(def, pf), grants.FirstOrDefault(g => g.PortfolioId == pf.Id)?.PartyId)).ToList();
        int units = p.ConcessionList.FirstOrDefault(c => c.PartyId == partner)?.Units ?? 0;
        double offered = agreed.Where(x => x.A.PartyId == partner).Sum(x => x.Worth)
            + grants.Where(g => g.PartyId == partner).Sum(g => talks.Portfolio(g.PortfolioId) is { } pf ? Negotiation.PortfolioWorth(def, pf) : 0.0)
            + Negotiation.ConcessionWorth * Math.Clamp(units, 0, Negotiation.MaxConcessions);
        double shortfall = Math.Max(0.0, required - offered);
        var fixes = new List<Fix>();
        string verdict = "accept"; string? reason = null, reasonDemand = null;
        CounterTerms? counter = null;

        var grudgeWith = members.FirstOrDefault(m => m != partner && (Resent(partner, m) >= CoalitionRules.GrudgeVeto || Resent(m, partner) >= CoalitionRules.GrudgeVeto));
        var crossing = agreed.FirstOrDefault(x => def.RedLines.Any(r => CoalitionRules.DemandConflictsWithRedLine(x.D, r)));
        var contradicting = agreed.Where(x => x.A.PartyId == partner).FirstOrDefault(x => agreed.Any(o => o.A != x.A && CoalitionRules.DemandsContradict(o.D, x.D)));
        if (grudgeWith != null) { verdict = "refuse"; reason = "grudge"; reasonDemand = grudgeWith; fixes.Add(new Fix("wait", grudgeWith)); }
        else if (talks.Veto(Player, partner)) { verdict = "refuse"; reason = "distance"; }
        else if (crossing.A != null)
        {
            verdict = "refuse"; reason = "red_line"; reasonDemand = $"{crossing.A.PartyId}:{crossing.A.DemandId}";
            var orig = _parties[crossing.A.PartyId].Demands.First(d => d.Id == crossing.A.DemandId);
            if (Negotiation.Anchor(formateur, orig) is { } an && !def.RedLines.Any(r => CoalitionRules.DemandConflictsWithRedLine(orig with { Target = Negotiation.HalfWay(orig, an) }, r)))
                fixes.Add(new Fix("half_way", crossing.A.PartyId, crossing.A.DemandId, null, Negotiation.HalfWay(orig, an)));
            fixes.Add(new Fix("drop_demand", crossing.A.PartyId, crossing.A.DemandId));
        }
        else if (contradicting.A != null) { verdict = "refuse"; reason = "contradiction"; reasonDemand = $"{partner}:{contradicting.A.DemandId}"; fixes.Add(new Fix("drop_demand", partner, contradicting.A.DemandId)); }
        else if (shortfall > 1e-9)
        {
            reason = "price";
            counter = Counter(partner, p, agreed, members, required, offered, talks);
            verdict = counter != null ? "counter" : "refuse";
            // what would pay the rest: free demands, portfolios, concessions, courting
            int freeSlots = _rules.ProgramSlots - p.Agreements.Count;
            foreach (var dv in demandViews.Where(v => !v.Agreed && v.Worth > 0 && v.Status is "acceptable" or "negotiable").OrderByDescending(v => v.Worth).ThenBy(v => v.DemandId, Ord).Take(Math.Max(0, Math.Min(3, freeSlots))))
                fixes.Add(new Fix("add_demand", partner, dv.DemandId, null, dv.Worth));
            foreach (var pv in portfolioViews.Where(v => v.HeldBy == null).OrderByDescending(v => v.Worth).ThenBy(v => v.PortfolioId, Ord).Take(2))
                fixes.Add(new Fix("give_portfolio", partner, null, pv.PortfolioId, pv.Worth));
            if (units < Negotiation.MaxConcessions) fixes.Add(new Fix("concede", partner, null, null, Negotiation.MaxConcessions - units));
            if (rel < 1.0) fixes.Add(new Fix("court", partner));
        }
        return new PartnerView(partner, _seats.GetValueOrDefault(partner), def.Price, required, offered, required > 0 ? offered / required : 1.0, verdict, reason, reasonDemand,
            shortfall, dist, fd, fs, fa, alternative, rel, grudge, demandViews, portfolioViews, units, counter, fixes);
    }

    /// <summary>
    /// The terms a partner would ask for itself to close a price gap (decision 0016): the full value of a demand it was offered only half-way, then its
    /// own demands in the order the AI formateur offers them (no red line of a member, no contradiction, while program slots are free), then the
    /// portfolios it values most that nobody holds. Null when even that does not pay its price.
    /// </summary>
    private CounterTerms? Counter(string partner, CoalitionProposal p, List<(AgreedDemand A, DemandDef D, double Worth)> agreed, List<string> members,
        double required, double offered, Talks talks)
    {
        var def = _parties[partner];
        var redLines = members.SelectMany(m => _parties[m].RedLines).ToList();
        var mine = agreed.Where(x => x.A.PartyId == partner).Select(x => x.A).ToList(); // only valid terms are kept
        var others = agreed.Where(x => x.A.PartyId != partner).Select(x => x.D).ToList();
        var chosen = new List<DemandDef>();
        double got = offered;
        // 1. insist on the full demand where a counter value was offered
        for (int i = 0; i < mine.Count; i++)
        {
            if (mine[i].Target == null || got + 1e-12 >= required) continue;
            var full = def.Demands.First(d => d.Id == mine[i].DemandId);
            if (redLines.Any(r => CoalitionRules.DemandConflictsWithRedLine(full, r)) || others.Any(o => CoalitionRules.DemandsContradict(o, full))) continue;
            double before = agreed.First(x => x.A == mine[i]).Worth;
            got += CoalitionRules.Worth(full, AlreadyMet) - before;
            mine[i] = mine[i] with { Target = null };
        }
        foreach (var a in mine) chosen.Add(Negotiation.Effective(def.Demands.First(d => d.Id == a.DemandId), a));
        int slotsLeft = _rules.ProgramSlots - p.Agreements.Count;
        // 2. more of its own demands
        foreach (var d in def.Demands.OrderByDescending(d => d.Weight * (0.5 + CoalitionBuilder.Priority(def, d))).ThenBy(d => d.Id, Ord))
        {
            if (got + 1e-12 >= required || slotsLeft <= 0) break;
            if (mine.Any(a => a.DemandId == d.Id) || CoalitionRules.Worth(d, AlreadyMet) <= 0) continue;
            if (redLines.Any(r => CoalitionRules.DemandConflictsWithRedLine(d, r))) continue;
            if (others.Concat(chosen).Any(o => CoalitionRules.DemandsContradict(o, d))) continue;
            mine.Add(new AgreedDemand(partner, d.Id));
            chosen.Add(d);
            got += d.Weight;
            slotsLeft--;
        }
        // 3. portfolios
        var grants = p.PortfolioList.Where(g => g.PartyId == partner).ToList();
        int all = p.PortfolioList.Count;
        foreach (var pf in _portfolios.Where(pf => p.PortfolioList.All(g => g.PortfolioId != pf.Id))
                     .OrderByDescending(pf => Negotiation.PortfolioWorth(def, pf)).ThenBy(pf => pf.Id, Ord))
        {
            if (got + 1e-12 >= required || grants.Count >= Negotiation.MaxPortfoliosPerPartner || all >= Negotiation.MaxPartnerPortfolios(_portfolios.Count)) break;
            grants.Add(new PortfolioGrant(partner, pf.Id));
            got += Negotiation.PortfolioWorth(def, pf);
            all++;
        }
        return got + 1e-12 >= required ? new CounterTerms(mine, grants) : null;
    }

    /// <summary>
    /// One round of talks (decision 0016): every invited party answers the player's offer at once. When all accept, the government forms (the
    /// concessions are paid); otherwise their answers are kept for the briefing and the parties that refused lose a little patience. A hard error
    /// (not formateur, majority, slots, portfolios, an unknown term, no rounds left this quarter) is refused for free.
    /// </summary>
    private ActionResult DoNegotiate(CoalitionProposal draft, int reportTurn)
    {
        if (!PlayerIsFormateur) return Fail("error.not_formateur");
        if (_roundsLeft <= 0) return Fail("error.no_rounds");
        var p = Normalize(draft);
        var view = AssessCoalition(p);
        if (view.HardError != null) return Fail(view.HardError, view.HardDetail);
        if (view.AllAccept)
        {
            if (FormOnTerms(p, reportTurn) is { Accepted: false } failed) return failed;
            _roundsLeft--;
            return new ActionResult(true, null, "formed");
        }
        _roundsLeft--;
        _replies = view.Partners.Select(v => new PartnerReply(v.PartyId, v.Verdict, v.Reason, v.ReasonDemand, v.Shortfall, v.Counter)).ToList();
        foreach (var v in view.Partners.Where(v => v.Verdict == "refuse" && _relations.ContainsKey(v.PartyId)))
            _relations[v.PartyId] = Math.Max(0.0, _relations[v.PartyId] - PoliticsLimits.NegotiationPatience);
        return new ActionResult(true, null, "replies");
    }

    /// <summary>
    /// What the AI formateur would say to the player's counter-offer as an invited partner (decision 0016): the offer plus the extra demands and
    /// portfolios for the player's party. Changes nothing.
    /// </summary>
    public CounterPreview PreviewCounter(IReadOnlyList<AgreedDemand> extra, IReadOnlyList<PortfolioGrant> portfolios)
    {
        if (_offer is not { } offer) return new CounterPreview(false, null, 0, 0, 0, "error.no_offer");
        if (_counterUsed) return new CounterPreview(false, "used", 0, 0, 0, "error.counter_used");
        var own = _parties[Player];
        var formateur = _parties[offer.Formateur];
        if (extra.Count + portfolios.Count == 0) return new CounterPreview(false, null, 0, 0, 0, "error.no_change");
        foreach (var a in extra)
            if (a.PartyId != Player || own.Demands.All(d => d.Id != a.DemandId) || offer.Agreements.Any(x => x.PartyId == Player && x.DemandId == a.DemandId))
                return new CounterPreview(false, null, 0, 0, 0, "error.unknown_demand");
        foreach (var g in portfolios)
            if (g.PartyId != Player || _portfolios.All(pf => pf.Id != g.PortfolioId) || offer.PortfolioList.Any(x => x.PortfolioId == g.PortfolioId))
                return new CounterPreview(false, null, 0, 0, 0, "error.unknown_portfolio");
        var proposal = offer with { Agreements = offer.Agreements.Concat(extra.Distinct()).ToList(), Portfolios = offer.PortfolioList.Concat(portfolios.Distinct()).ToList() };
        var talks = Talks;
        double required = CoalitionRules.RequiredWeight(own, Relation(offer.Formateur, Player), Resent(Player, offer.Formateur), talks.Factor(offer.Formateur, Player));
        double worth = proposal.Agreements.Where(a => a.PartyId == Player).Sum(a => CoalitionRules.AgreementWorth(a, own, formateur, AlreadyMet)?.Worth ?? 0.0)
            // the ministries already in the offer are the formateur's own gift, on top of the price; only what the player asks for counts against the ceiling
            + portfolios.Distinct().Where(g => g.PartyId == Player).Sum(g => talks.Portfolio(g.PortfolioId) is { } pf ? Negotiation.PortfolioWorth(own, pf) : 0.0);
        var excluded = new HashSet<string>(_excluded, Ord) { Player };
        bool pivotal = CoalitionBuilder.Build(offer.Formateur, _parties, _seats, _ranges, _rules.MajorityFraction, _rules.ProgramSlots, Relation, excluded, Resent, AlreadyMet, talks) == null;
        double ceiling = required * (1 + PoliticsLimits.CounterSlack) + (pivotal ? PoliticsLimits.CounterPivotalBonus : 0.0);
        var refusals = CoalitionRules.Evaluate(proposal, _parties, _seats, _rules.MajorityFraction, _rules.ProgramSlots, (a, b) => Relation(a, b), Resent, alreadyMet: AlreadyMet, talks: talks);
        string? reason = refusals.FirstOrDefault(r => r.Reason is "slots" or "red_line" or "contradiction" or "portfolios")?.Reason;
        if (reason == null && extra.Select(a => own.Demands.First(d => d.Id == a.DemandId))
                .Any(d => d.Kind == "policy" && formateur.Platform.TryGetValue(d.Subject, out var pos) && pos.Weight >= PoliticsLimits.CounterPlatformWeight
                          && !CommitmentMath.Satisfied(d, pos.Target)))
            reason = "platform";
        if (reason == null && worth > ceiling + 1e-9) reason = "too_much";
        return new CounterPreview(reason == null, reason, worth, required, ceiling, null);
    }

    /// <summary>The player's counter-offer as an invited partner: accepted, the government forms on the improved terms; refused, the offer stands.</summary>
    private ActionResult DoCounter(IReadOnlyList<AgreedDemand> extra, IReadOnlyList<PortfolioGrant> portfolios, int reportTurn)
    {
        var preview = PreviewCounter(extra, portfolios);
        if (preview.ErrorKey != null) return Fail(preview.ErrorKey);
        var offer = _offer!;
        _counterUsed = true;
        if (!preview.Accepts)
        {
            _counterAnswer = preview.Reason;
            _pending.Add(new CounterOfferAnswered(reportTurn, offer.Formateur, false, preview.Reason));
            return new ActionResult(true, null, "refused:" + preview.Reason);
        }
        var improved = offer with { Agreements = offer.Agreements.Concat(extra.Distinct()).ToList(), Portfolios = offer.PortfolioList.Concat(portfolios.Distinct()).ToList() };
        _counterAnswer = "accepted";
        _pending.Add(new CounterOfferAnswered(reportTurn, offer.Formateur, true, null));
        _pending.Add(Form(reportTurn, improved));
        return new ActionResult(true, null, "accepted");
    }

    // ---- coalition reshuffle (decision 0013) -------------------------------------------------------------------

    private sealed record QueuedReshuffle(IReadOnlyList<string> Drop, IReadOnlyList<string> Add, IReadOnlyList<AgreedDemand> Agreements, double Cost);
    private QueuedReshuffle? _reshuffle;
    private int _nextReshuffleTurn, _nextAiReshuffleTurn;

    /// <summary>Everything a client needs to show before the player reshuffles: seats, cost, a hard error or the parties' likely answer.</summary>
    public sealed record ReshuffleCheck(
        string? ErrorKey, string? Detail, IReadOnlyList<string> Drop, IReadOnlyList<string> Add, IReadOnlyList<AgreedDemand> Agreements,
        double Cost, IReadOnlyList<Refusal> Refusals, int SeatsHeld, int SeatsNeeded, int CooldownLeft);

    private IReadOnlyList<AgreedDemand> AgreementsOf(IEnumerable<string> partners) =>
        _government.Commitments.Where(c => partners.Contains(c.PartyId)).Select(c => new AgreedDemand(c.PartyId, c.Demand.Id,
            _parties[c.PartyId].Demands.FirstOrDefault(d => d.Id == c.Demand.Id) is { } orig && orig.Target != c.Demand.Target ? c.Demand.Target : null)).Distinct().ToList();

    /// <summary>Checks a reshuffle the player wants (reportTurn = the turn the report will carry); changes nothing.</summary>
    public ReshuffleCheck CheckReshuffle(IReadOnlyList<string> drop, IReadOnlyList<string> add, IReadOnlyList<AgreedDemand> agreements, int reportTurn)
    {
        var dropSet = drop.Distinct(Ord).OrderBy(x => x, Ord).ToList();
        var addSet = add.Distinct(Ord).OrderBy(x => x, Ord).ToList();
        var deals = agreements.Distinct().ToList();
        double cost = PoliticsLimits.ReshuffleBaseCost + PoliticsLimits.ReshufflePerParty * (dropSet.Count + addSet.Count);
        int total = _seats.Values.Sum(), needed = (int)Math.Floor(_rules.MajorityFraction * total) + 1;
        var kept = _government.Parties.Where(p => p != _government.Leader && !dropSet.Contains(p)).ToList();
        int held = new[] { _government.Leader }.Concat(kept).Concat(addSet).Distinct(Ord).Sum(p => _seats.GetValueOrDefault(p));
        int cooldown = Math.Max(0, _nextReshuffleTurn - reportTurn);
        ReshuffleCheck Error(string key, string? detail = null) =>
            new(key, detail, dropSet, addSet, deals, cost, Array.Empty<Refusal>(), held, needed, cooldown);

        if (_phase != Phase.Governing) return Error("error.wrong_phase");
        if (PlayerRole != Role.Leader) return Error("error.not_leader");
        if (cooldown > 0) return Error("error.reshuffle_cooldown", cooldown.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (_reshuffle != null) return Error("error.already_queued");
        foreach (var d in dropSet)
            if (d == Player || !_government.Parties.Contains(d)) return Error("error.unknown_party", d);
        foreach (var a in addSet)
            if (!_parties.ContainsKey(a) || _government.Parties.Contains(a) || _seats.GetValueOrDefault(a) <= 0) return Error("error.unknown_party", a);
        if (dropSet.Count == 0 && addSet.Count == 0) return Error("error.no_change");
        if (held < needed) return Error("error.would_lose_majority");
        var refusals = CoalitionRules.Evaluate(new CoalitionProposal(Player, addSet, deals), _parties, _seats, _rules.MajorityFraction,
            _rules.ProgramSlots, Relation, Resent, new SittingCoalition(kept, AgreementsOf(kept)), AlreadyMet, Talks with { AllowPortfolios = false });
        if (refusals.FirstOrDefault(r => r.Reason is "unknown" or "slots" or "majority") is { } hard)
            return Error("error.refused." + hard.Reason, $"{hard.PartyId}:{hard.DemandId}");
        return new ReshuffleCheck(null, null, dropSet, addSet, deals, cost, refusals, held, needed, cooldown);
    }

    /// <summary>The agreements the AI would offer the parties the player brings in (null when not leading or a price cannot be paid).</summary>
    public IReadOnlyList<AgreedDemand>? SuggestReshuffle(IReadOnlyList<string> drop, IReadOnlyList<string> add)
    {
        if (_phase != Phase.Governing || PlayerRole != Role.Leader) return null;
        var kept = _government.Parties.Where(p => p != _government.Leader && !drop.Contains(p)).ToList();
        return CoalitionBuilder.AllocateFor(Player, add.Where(a => !_government.Parties.Contains(a)).ToList(), kept, AgreementsOf(kept), _parties,
            _rules.ProgramSlots, Relation, Resent, AlreadyMet, Talks);
    }

    private ActionResult DoReshuffle(ReshuffleCoalition r, int reportTurn)
    {
        var check = CheckReshuffle(r.Drop, r.Add, r.Agreements, reportTurn);
        if (check.ErrorKey != null) return Fail(check.ErrorKey, check.Detail);
        if (!TrySpend(check.Cost)) return Fail("error.not_enough_capital");
        _reshuffle = new QueuedReshuffle(check.Drop, check.Add, check.Agreements, check.Cost);
        return ActionResult.Ok;
    }

    private void CancelReshuffle()
    {
        if (_reshuffle == null) return;
        _pc = Math.Min(MaxCapital, _pc + _reshuffle.Cost);
        _reshuffle = null;
    }

    /// <summary>The parties answer the player's reshuffle at the end of the quarter, by the same rules as at formation.</summary>
    private void ProcessReshuffle(int turn, List<PoliticalEvent> events)
    {
        var rs = _reshuffle!;
        _reshuffle = null;
        var check = CheckReshuffle(rs.Drop, rs.Add, rs.Agreements, turn);
        if (check.ErrorKey != null) // something changed since it was asked (a partner left or was dismissed): nothing happens, all paid back
        {
            _pc = Math.Min(MaxCapital, _pc + rs.Cost);
            events.Add(new ReshuffleRefused(turn, check.ErrorKey.Replace("error.refused.", "").Replace("error.", ""), null));
            return;
        }
        if (check.Refusals.Count > 0)
        {
            var first = check.Refusals[0];
            _pc = Math.Min(MaxCapital, _pc + rs.Cost - PoliticsLimits.ReshuffleRefusalCost);
            if (first.PartyId != null && _relations.ContainsKey(first.PartyId))
                _relations[first.PartyId] = Math.Max(0.0, _relations[first.PartyId] - 0.05);
            _nextReshuffleTurn = turn + 1 + PoliticsLimits.ReshuffleRefusedCooldownTurns;
            events.Add(new ReshuffleRefused(turn, first.Reason, first.PartyId));
            return;
        }
        var affinityTop = new Dictionary<string, double>(Ord);
        foreach (var d in check.Drop)
        {
            RemoveFromGovernment(d);
            AddGrudge(d, _government.Leader, PoliticsLimits.ReshuffleGrudge);
            if (_relations.ContainsKey(d)) _relations[d] = Math.Max(0.0, _relations[d] - PoliticsLimits.ReshuffleRelationHit);
            // the dropped party's voters hold it against the player's party
            var aff = _parties[d].Affinity;
            double top = aff.Count == 0 ? 0.0 : aff.Values.Max();
            if (top > 0)
                foreach (var g in _groups.OrderBy(g => g.Id, Ord))
                    if (aff.GetValueOrDefault(g.Id) is var a && a > 0) _momentum[g.Id] -= 0.04 * a / top;
        }
        foreach (var k in _government.Parties.Where(p => p != _government.Leader && p != Player).ToList())
            _loyalty[k] = Math.Max(0.0, _loyalty.GetValueOrDefault(k, _loyaltyParams.Start) - PoliticsLimits.ReshuffleLoyaltyHit);
        Join(check.Add, check.Agreements, turn);
        _nextReshuffleTurn = turn + 1 + PoliticsLimits.ReshuffleCooldownTurns;
        events.Add(new CoalitionReshuffled(turn, _government.Leader, check.Drop, check.Add));
    }

    /// <summary>Takes a party out of the government without the departure penalties (those are the caller's business).</summary>
    private void RemoveFromGovernment(string partner)
    {
        _government = new Government(_government.Leader, _government.Parties.Where(p => p != partner).ToList(),
            _government.Commitments.Where(c => c.PartyId != partner).ToList(), _government.FormedTurn, PortfoliosWithout(partner));
        _loyalty.Remove(partner); _warned.Remove(partner); _ultimatums.Remove(partner); _midterm.Remove(partner); _nextDemand.Remove(partner);
    }

    /// <summary>The partners' portfolios without those of a party that leaves (they go back to the leader).</summary>
    private SortedDictionary<string, string> PortfoliosWithout(string party) =>
        new(_government.PortfolioHolders.Where(kv => kv.Value != party).ToDictionary(kv => kv.Key, kv => kv.Value), Ord);

    /// <summary>Newcomers join a sitting government: their agreements are due from today, they start at the formation loyalty.</summary>
    private void Join(IReadOnlyList<string> added, IReadOnlyList<AgreedDemand> agreements, int turn)
    {
        var commitments = _government.Commitments.ToList();
        foreach (var a in agreements.Where(a => added.Contains(a.PartyId)))
        {
            var d = _parties[a.PartyId].Demands.First(x => x.Id == a.DemandId);
            commitments.Add(new Commitment(a.PartyId, d, _lookup(d.Kind, d.Subject), turn + d.DueTurns, turn));
        }
        var parties = _government.Parties.Concat(added).Distinct(Ord).OrderBy(x => x, Ord).ToList();
        _government = new Government(_government.Leader, parties, commitments, _government.FormedTurn, _government.Portfolios);
        SetBaseline(added);
        foreach (var p in added.Where(p => p != Player))
        {
            _nextDemand[p] = 6 + _rng.NextInt(5);
            _loyalty[p] = LoyaltyModel.StartLoyalty(_loyaltyParams.Start, Relation(_government.Leader, p), Resent(p, _government.Leader));
            foreach (var r in _parties[p].RedLines)
                if (LoyaltyModel.RedLineViolated(r, _lookup("policy", r.Policy))) _redLinesActive.Add(RedKey(p, r));
        }
    }

    /// <summary>
    /// An AI leader whose government just lost its majority because a partner walked out tries to patch it with one or two outside parties
    /// (never the player's, never one whose red line is crossed right now), at most once per reshuffle cooldown and only with
    /// <see cref="PoliticsLimits.AiPatchChance"/>. True when the majority holds.
    /// </summary>
    private bool PatchMajority(int turn, List<PoliticalEvent> events)
    {
        if (CoalitionRules.HasMajority(_government.Parties, _seats, _rules.MajorityFraction)) return true;
        string leader = _government.Leader;
        if (leader == Player || turn < _nextAiReshuffleTurn) return false;
        var excluded = new HashSet<string>(Ord) { Player };
        foreach (var p in _parties.Values)
            if (p.RedLines.Any(r => LoyaltyModel.RedLineViolated(r, _lookup("policy", r.Policy)))) excluded.Add(p.Id);
        var kept = _government.Parties.Where(p => p != leader).ToList();
        var patch = CoalitionBuilder.Complete(leader, kept, AgreementsOf(kept), _parties, _seats, _ranges, _rules.MajorityFraction,
            _rules.ProgramSlots, Relation, excluded, Resent, AlreadyMet, Talks);
        if (patch == null) return false;
        // occasionally, not always: whether the leader strikes the deal in time is a coin flip (keeps falls in the range of decision 0011)
        if (_rng.NextDouble() >= PoliticsLimits.AiPatchChance) return false;
        Join(patch.Partners, patch.Agreements, turn);
        _nextAiReshuffleTurn = turn + PoliticsLimits.ReshuffleCooldownTurns;
        events.Add(new CoalitionReshuffled(turn, leader, Array.Empty<string>(), patch.Partners));
        return true;
    }

    private void Depart(string partner, int turn, List<PoliticalEvent> events, bool announce = true)
    {
        if (announce) events.Add(new PartnerLeft(turn, partner));
        int unmet = _government.Commitments.Count(c => c.PartyId == partner
            && CommitmentMath.Progress(c, Current(c)) + 1e-9 < CommitmentMath.Expected(c, turn, _government.FormedTurn));
        AddGrudge(partner, _government.Leader, 0.5 + 0.1 * unmet);
        AddGrudge(_government.Leader, partner, 0.3);
        _government = new Government(_government.Leader, _government.Parties.Where(p => p != partner).ToList(),
            _government.Commitments.Where(c => c.PartyId != partner).ToList(), _government.FormedTurn, PortfoliosWithout(partner));
        _loyalty.Remove(partner); _warned.Remove(partner); _ultimatums.Remove(partner); _midterm.Remove(partner); _nextDemand.Remove(partner);
    }
}
