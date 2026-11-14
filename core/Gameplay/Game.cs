using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Expressions;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Voters;

namespace OpenPolitics.Core.Gameplay;

/// <summary>
/// One running game. All state lives here and changes only through <see cref="Apply"/> and <see cref="EndTurn"/>.
/// Turn order (spec section 6, events moved before voters): queued actions and policy delays, edges, economy model,
/// integration of other values, events, voters, report. Everything iterates in ordinal id order so results are reproducible.
/// </summary>
public sealed class Game
{
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
    private readonly List<PendingPolicy> _pending = new();
    private readonly List<(int Turn, PlayerAction Action)> _log = new();
    private readonly GraphEvaluator _graph;
    private readonly List<IEconomyModel> _models = new();
    private readonly VoterModel _voters;
    private readonly Pcg32 _rng;
    private readonly List<(EventDef Def, Expr Condition)> _events = new();
    private int _turn;

    private readonly PoliticsEngine? _politics;
    private readonly SortedDictionary<string, IndexSetting> _indexation = new(Ord);

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

        if (_scenario.Politics != null && _country.Politics != null)
            _politics = new PoliticsEngine(packs, _scenario, _country, seed, _satisfaction,
                (kind, id) => kind == "policy" ? _policies.GetValueOrDefault(id) : _values.GetValueOrDefault(id),
                _scenario.TurnsPerYear);
        _politics?.UseLatestPolicy(LatestPolicy);
    }

    /// <summary>The value a policy will have once its queued changes are in (the last queued one, else the current one).</summary>
    private double LatestPolicy(string id) =>
        _pending.LastOrDefault(p => p.PolicyId == id)?.Value ?? _policies[id];

    private double CurrentYear => _scenario.StartYear + (double)_turn / _scenario.TurnsPerYear;

    public GameState State => new(_turn, CurrentYear, new SortedDictionary<string, CountryState>(Ord)
    {
        [_country.Id] = new CountryState(
            new SortedDictionary<string, double>(_values, Ord),
            new SortedDictionary<string, double>(_policies, Ord),
            new SortedDictionary<string, double>(_satisfaction, Ord),
            _politics?.Snapshot()),
    });

    public ActionResult Apply(PlayerAction action)
    {
        if (action is PoliticsAction)
        {
            if (_politics == null) return new ActionResult(false, "error.no_politics", null);
            var result = _politics.Execute(action, _turn + 1);
            if (result.Accepted) _log.Add((_turn, action));
            return result;
        }
        if (action is SetIndexation ix) return ApplyIndexation(ix);
        if (action is not SetPolicy set)
            return new ActionResult(false, "error.unknown_action", action.GetType().Name);
        if (_politics?.PolicyBlock() is { } blocked) return new ActionResult(false, blocked, null);
        if (!_policyDefs.TryGetValue(set.PolicyId, out var policy))
            return new ActionResult(false, "error.unknown_policy", set.PolicyId);
        if (!double.IsFinite(set.Value) || set.Value < policy.Min || set.Value > policy.Max)
            return new ActionResult(false, "error.value_out_of_range", $"{set.PolicyId} must be in [{policy.Min}, {policy.Max}]");

        _pending.Add(new PendingPolicy(set.PolicyId, set.Value, _turn + policy.DelayTurns));
        _log.Add((_turn, action));
        return ActionResult.Ok;
    }

    /// <summary>Spending policies that are not tied to GDP, with their mode ("inflation" or "none").</summary>
    public IReadOnlyDictionary<string, string> Indexation =>
        _indexation.ToDictionary(kv => kv.Key, kv => kv.Value.Mode, Ord);

    private ActionResult ApplyIndexation(SetIndexation ix)
    {
        if (_politics?.PolicyBlock() is { } blocked) return new ActionResult(false, blocked, null);
        if (!_policyDefs.TryGetValue(ix.PolicyId, out var policy))
            return new ActionResult(false, "error.unknown_policy", ix.PolicyId);
        if (ix.Mode is not ("gdp" or "inflation" or "none"))
            return new ActionResult(false, "error.bad_indexation", ix.Mode);
        if (policy.CostPerUnit == 0) return new ActionResult(false, "error.not_indexable", ix.PolicyId);
        if (ix.Mode == "gdp") _indexation.Remove(ix.PolicyId);
        else
            _indexation[ix.PolicyId] = new IndexSetting(ix.Mode, _values.GetValueOrDefault(MacroModelV1.Ids.Gdp),
                _values.TryGetValue(MacroModelV1.Ids.PriceLevel, out var pl) && pl > 0 ? pl : 100.0);
        _log.Add((_turn, ix));
        return ActionResult.Ok;
    }

    /// <summary>The coalition the AI would propose if the player's party is the formateur now (null otherwise).</summary>
    public CoalitionProposal? SuggestCoalition() => _politics?.Suggest();

    /// <summary>
    /// What setting <paramref name="policyId"/> to <paramref name="value"/> would do, measured from the setting it will have
    /// once queued changes are in. Null for an unknown policy or a value outside its range. Changes nothing.
    /// </summary>
    public PolicyPreview? PreviewPolicy(string policyId, double value)
    {
        if (!_policyDefs.TryGetValue(policyId, out var policy) || !double.IsFinite(value) || value < policy.Min || value > policy.Max)
            return null;
        double from = LatestPolicy(policyId);
        var effects = new SortedDictionary<string, (double Add, double Rel)>(Ord);
        foreach (var (_, to, kind, amount) in _graph.Preview(policyId, from, value))
        {
            var (a, r) = effects.GetValueOrDefault(to);
            effects[to] = kind == EdgeKind.Additive ? (a + amount, r) : (a, r + amount);
        }
        double range = Math.Max(policy.Max - policy.Min, 1e-12);
        var groups = new List<GroupEffect>();
        foreach (var g in _packs.VoterGroups.OrderBy(g => g.Id, Ord))
            if (g.PolicyPrefs.TryGetValue(policyId, out var pref) && g.PolicyWeights.TryGetValue(policyId, out var weight))
                groups.Add(new GroupEffect(g.Id, -weight * (Math.Abs(value - pref) - Math.Abs(from - pref)) / range));
        double delta = value - from;
        double budget = delta * policy.CostPerUnit - (policy.RevenueBaseValueId != null ? delta : 0.0);
        return new PolicyPreview(policyId, from, value,
            effects.Select(kv => new ValueEffect(kv.Key, kv.Value.Add, kv.Value.Rel)).ToList(), groups, budget);
    }

    /// <summary>Accepted policy changes that are still waiting for their delay to pass, in the order they were made.</summary>
    public IReadOnlyList<PendingPolicy> PendingPolicies => _pending.ToList();

    public SaveData Save() => new(_scenario.Id, _seed, _packs.PackHashes, _log.ToList(), _turn);

    /// <summary>The share of the vote a party needs for seats right now: the threshold policy if the pack has one, else the country's rule (null without politics).</summary>
    /// <summary>Where each party stands on the economic (X) and social (Y) axes, -1..1 (empty without politics).</summary>
    public IReadOnlyDictionary<string, (double X, double Y)> Compass =>
        _politics?.Compass() ?? new Dictionary<string, (double X, double Y)>();

    public double? ElectoralThreshold => _politics?.CurrentThreshold;

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
            var next = model.Step(new EconomyInput(before, eval.Rates, _policies, _country.Parameters, _indexation), _dt);
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
        foreach (var (id, s) in _voters.Step(_satisfaction, change, _policies, _dt, after)) nextSatisfaction[id] = s;
        _satisfaction = nextSatisfaction;

        // 9. Commit and report.
        foreach (var (id, v) in after) _values[id] = v;
        _turn++;

        // Politics runs on the committed state: today's poll, loyalty, the election countdown, formation.
        var politicalEvents = _politics?.Step(_turn, _satisfaction) ?? new List<PoliticalEvent>();
        if (_politics?.GovernorMove(_turn, LatestPolicy) is { } move)
        {
            double from = LatestPolicy(move.PolicyId);
            politicalEvents.Add(new GovernmentMeasure(_turn, move.PolicyId, from, move.Value));
            _politics.RecordMeasure(_turn, move.PolicyId, from, move.Value);
            _pending.Add(new PendingPolicy(move.PolicyId, move.Value, _turn + _policyDefs[move.PolicyId].DelayTurns));
        }
        foreach (var (policyId, to) in _politics?.TakeApprovedProposals() ?? Array.Empty<(string, double)>())
        {
            var def = _policyDefs[policyId];
            double from = LatestPolicy(policyId); // the engine vetted exactly this move, from the same queued value
            if (to == from) continue;
            politicalEvents.Add(new GovernmentMeasure(_turn, policyId, from, to));
            _politics!.RecordMeasure(_turn, policyId, from, to);
            _pending.Add(new PendingPolicy(policyId, to, _turn + def.DelayTurns));
        }
        var changes = before.Keys.Select(id => new ValueChange(id, before[id], after[id],
            eval.Contributions.TryGetValue(id, out var c) ? c : Array.Empty<Contribution>())).ToList();
        return new TurnReport(_turn, CurrentYear, changes, fired, warnings, politicalEvents);
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
