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
    private readonly Func<string, string, double> _baseline;

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
        IReadOnlyDictionary<string, double> satisfaction, Func<string, string, double> baseline)
    {
        _scenario = scenario.Politics!;
        _rules = country.Politics!;
        _baseline = baseline;
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
        PlayerRole);

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
        var events = new List<PoliticalEvent>(_pending);
        _pending.Clear();
        _poll = _support.Compute(Input(satisfaction), _rng);

        bool electionDue = --_turnsToElection <= 0;
        if (!electionDue && _phase == Phase.Caretaker) electionDue = --_caretakerLeft <= 0;
        if (electionDue) { HoldElection(turn, events); return events; }
        if (_phase == Phase.Formation) StepFormation(turn, events);
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

        _queue = _seats.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value).ThenByDescending(kv => _votes[kv.Key]).ThenBy(kv => kv.Key, Ord)
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
            commitments.Add(new Commitment(a.PartyId, d, _baseline(d.Kind, d.Subject), turn + d.DueTurns));
        }
        var parties = new[] { proposal.Formateur }.Concat(proposal.Partners).Distinct(Ord).OrderBy(x => x, Ord).ToList();
        _government = new Government(proposal.Formateur, parties, commitments, turn);
        _phase = Phase.Governing;
        _offer = null;
        _caretakerLeft = 0;
        return new GovernmentFormed(turn, _government);
    }
}
