using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Voters;

namespace OpenPolitics.Core.Gameplay;

/// <summary>
/// One running game. All state lives here and changes only through <see cref="Apply"/> and <see cref="EndTurn"/>.
/// Turn order (spec section 6): queued actions and policy delays, edges, economy model, integration of other values,
/// voters, events, report. Everything iterates in ordinal id order so results are reproducible.
/// </summary>
public sealed class Game
{
    private sealed record Pending(int EffectiveTurn, string PolicyId, double Value);

    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private readonly PackSet _packs;
    private readonly ScenarioDef _scenario;
    private readonly CountryDef _country;
    private readonly ulong _seed;
    private readonly double _dt;
    private readonly Dictionary<string, PolicyDef> _policyDefs;
    private readonly SortedDictionary<string, double> _values = new(Ord);
    private readonly SortedDictionary<string, double> _policies = new(Ord);
    private SortedDictionary<string, double> _satisfaction = new(Ord);
    private readonly List<Pending> _pending = new();
    private readonly List<(int Turn, PlayerAction Action)> _log = new();
    private readonly GraphEvaluator _graph;
    private readonly List<IEconomyModel> _models = new();
    private readonly VoterModel _voters;
    private readonly Pcg32 _rng;
    private int _turn;

    internal Game(PackSet packs, string scenarioId, ulong seed)
    {
        _packs = packs;
        _scenario = packs.GetScenario(scenarioId);
        _country = packs.Countries.FirstOrDefault(c => c.Id == _scenario.CountryId)
            ?? throw new KeyNotFoundException($"Unknown country '{_scenario.CountryId}'.");
        _seed = seed;
        _dt = 1.0 / _scenario.TurnsPerYear;
        _policyDefs = packs.Policies.ToDictionary(p => p.Id);

        foreach (var v in packs.Values)
            _values[v.Id] = _scenario.StartValues.TryGetValue(v.Id, out var sv) ? sv : v.Initial;
        foreach (var p in packs.Policies)
            _policies[p.Id] = _scenario.StartPolicies.TryGetValue(p.Id, out var sp) ? sp : p.Default;
        foreach (var g in packs.VoterGroups) _satisfaction[g.Id] = 0.0;

        foreach (var owner in packs.Values.Where(v => v.Owner != null).Select(v => v.Owner!).Distinct(Ord).OrderBy(o => o, Ord))
            _models.Add(owner switch
            {
                "macro_v1" => new MacroModelV1(packs.Policies),
                _ => throw new InvalidOperationException($"Unknown economy model '{owner}'."),
            });

        _graph = new GraphEvaluator(packs.Edges, packs.Values, packs.Policies);
        _voters = new VoterModel(packs.VoterGroups, packs.Values, packs.Policies);
        _rng = new Pcg32(seed);
    }

    private double CurrentYear => _scenario.StartYear + (double)_turn / _scenario.TurnsPerYear;

    public GameState State => new(_turn, CurrentYear, new SortedDictionary<string, CountryState>(Ord)
    {
        [_country.Id] = new CountryState(
            new SortedDictionary<string, double>(_values, Ord),
            new SortedDictionary<string, double>(_policies, Ord),
            new SortedDictionary<string, double>(_satisfaction, Ord)),
    });

    public ActionResult Apply(PlayerAction action)
    {
        if (action is not SetPolicy set)
            return new ActionResult(false, "error.unknown_action", action.GetType().Name);
        if (!_policyDefs.TryGetValue(set.PolicyId, out var policy))
            return new ActionResult(false, "error.unknown_policy", set.PolicyId);
        if (!double.IsFinite(set.Value) || set.Value < policy.Min || set.Value > policy.Max)
            return new ActionResult(false, "error.value_out_of_range", $"{set.PolicyId} must be in [{policy.Min}, {policy.Max}]");

        _pending.Add(new Pending(_turn + policy.DelayTurns, set.PolicyId, set.Value));
        _log.Add((_turn, action));
        return ActionResult.Ok;
    }

    public SaveData Save() => new(_scenario.Id, _seed, _packs.PackHashes, _log.ToList(), _turn);

    public TurnReport EndTurn()
    {
        var warnings = new List<string>();

        // 1-2. Queued policy changes become effective once their delay has passed (later actions win).
        for (int i = 0; i < _pending.Count;)
        {
            if (_pending[i].EffectiveTurn <= _turn) { _policies[_pending[i].PolicyId] = _pending[i].Value; _pending.RemoveAt(i); }
            else i++;
        }

        var before = new SortedDictionary<string, double>(_values, Ord);

        // 3. Edges, evaluated against the state at the start of the turn.
        var eval = _graph.Evaluate(before, _policies);

        // 4. Economy models step the values they own.
        var after = new SortedDictionary<string, double>(before, Ord);
        var owned = new HashSet<string>(Ord);
        foreach (var model in _models)
        {
            var next = model.Step(new EconomyInput(before, eval.Rates, _policies, _country.Parameters), _dt);
            foreach (var id in model.OwnedValueIds)
                if (after.ContainsKey(id) && next.TryGetValue(id, out var nv)) { after[id] = nv; owned.Add(id); }
        }

        // 5. Integrate the remaining values: x += dt * (additive + x * relative).
        foreach (var id in before.Keys)
        {
            if (owned.Contains(id)) continue;
            double x = before[id];
            var rate = eval.Rates.TryGetValue(id, out var r) ? r : default;
            after[id] = x + _dt * (rate.Additive + x * rate.Relative);
        }
        foreach (var id in before.Keys.ToList())
            if (!double.IsFinite(after[id]))
            {
                after[id] = before[id];
                warnings.Add($"Value '{id}' was not finite after the step and was kept at its previous value.");
            }

        // 6. Voter satisfaction from annual relative changes and current policies.
        var change = new SortedDictionary<string, double>(Ord);
        foreach (var id in before.Keys)
            change[id] = (after[id] - before[id]) / Math.Max(Math.Abs(before[id]), 1e-9) / _dt;
        var nextSatisfaction = new SortedDictionary<string, double>(Ord);
        foreach (var (id, s) in _voters.Step(_satisfaction, change, _policies, _dt)) nextSatisfaction[id] = s;
        _satisfaction = nextSatisfaction;

        // 7. Events.
        var fired = RollEvents(after);

        // 8. Commit and report.
        foreach (var (id, v) in after) _values[id] = v;
        _turn++;
        var changes = before.Keys.Select(id => new ValueChange(id, before[id], after[id],
            eval.Contributions.TryGetValue(id, out var c) ? c : Array.Empty<Contribution>())).ToList();
        return new TurnReport(_turn, CurrentYear, changes, fired, warnings);
    }

    /// <summary>Filled in by the events task; returns the ids of events fired this turn.</summary>
    private IReadOnlyList<string> RollEvents(SortedDictionary<string, double> values) => Array.Empty<string>();
}
