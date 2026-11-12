using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Expressions;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Voters;

namespace OpenPolitics.Core.Gameplay;

/// <summary>
/// One running game. All state lives here and changes only through <see cref="Apply"/> and <see cref="EndTurn"/>.
/// Turn order (spec section 6, events moved before voters): queued actions and policy delays, edges, economy model,
/// integration of other values, events, voters, report. Everything iterates in ordinal id order so results are reproducible.
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
    private readonly Dictionary<string, ValueDef> _valueDefs;
    private readonly SortedDictionary<string, double> _values = new(Ord);
    private readonly SortedDictionary<string, double> _policies = new(Ord);
    private SortedDictionary<string, double> _satisfaction = new(Ord);
    private readonly List<Pending> _pending = new();
    private readonly List<(int Turn, PlayerAction Action)> _log = new();
    private readonly GraphEvaluator _graph;
    private readonly List<IEconomyModel> _models = new();
    private readonly VoterModel _voters;
    private readonly Pcg32 _rng;
    private readonly List<(EventDef Def, Expr Condition)> _events = new();
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
            _models.Add(EconomyModels.Create(owner, packs.Policies));
        _valueDefs = packs.Values.ToDictionary(v => v.Id);

        _graph = new GraphEvaluator(packs.Edges, packs.Values, packs.Policies);
        _voters = new VoterModel(packs.VoterGroups, packs.Values, packs.Policies);
        _rng = new Pcg32(seed);
        foreach (var ev in packs.Events)
        {
            if (!ExprParser.TryParse(ev.Condition, out var expr, out var error))
                throw new InvalidOperationException($"Event '{ev.Id}' has an invalid condition: {error}");
            _events.Add((ev, expr!));
        }
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

        // 6. Events. Their effects change values before the voters look, so shocks are felt.
        var fired = RollEvents(after);

        // 7. Voter satisfaction from the change of each value in units of max(|before|, |reference|, scale)
        //    per year (robust near zero), and from the current policies.
        var change = new SortedDictionary<string, double>(Ord);
        foreach (var id in before.Keys)
        {
            var def = _valueDefs[id];
            double denominator = Math.Max(Math.Max(Math.Abs(before[id]), Math.Abs(def.Reference)), def.Scale);
            change[id] = (after[id] - before[id]) / denominator / _dt;
        }
        var nextSatisfaction = new SortedDictionary<string, double>(Ord);
        foreach (var (id, s) in _voters.Step(_satisfaction, change, _policies, _dt)) nextSatisfaction[id] = s;
        _satisfaction = nextSatisfaction;

        // 8. Commit and report.
        foreach (var (id, v) in after) _values[id] = v;
        _turn++;
        var changes = before.Keys.Select(id => new ValueChange(id, before[id], after[id],
            eval.Contributions.TryGetValue(id, out var c) ? c : Array.Empty<Contribution>())).ToList();
        return new TurnReport(_turn, CurrentYear, changes, fired, warnings);
    }

    /// <summary>
    /// Events are visited in id order. Conditions read the values as they were after the step (before any event
    /// effect). One RNG draw is made for every event whose condition holds, so unrelated events never shift the stream.
    /// </summary>
    private IReadOnlyList<string> RollEvents(SortedDictionary<string, double> values)
    {
        var fired = new List<string>();
        if (_events.Count == 0) return fired;
        var snapshot = new SortedDictionary<string, double>(values, Ord);
        double Lookup(string id) => snapshot.TryGetValue(id, out var v) ? v : 0.0;
        foreach (var (def, condition) in _events)
        {
            if (condition.Eval(Lookup) == 0) continue;
            if (_rng.NextDouble() >= def.Probability) continue;
            fired.Add(def.Id);
            foreach (var effect in def.Effects)
                if (values.TryGetValue(effect.ValueId, out var current)) values[effect.ValueId] = current + effect.Delta;
        }
        return fired;
    }
}
