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

    private readonly bool _randomStart;

    internal Game(PackSet packs, string scenarioId, ulong seed, string? playerParty = null, bool randomStart = false)
    {
        _randomStart = randomStart;
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

        bool politics = _scenario.Politics != null && _country.Politics != null;
        if (playerParty != null && (!politics || packs.Parties.All(p => p.Id != playerParty)))
            throw new ArgumentException(politics
                ? $"Unknown party '{playerParty}'."
                : $"Scenario '{scenarioId}' has no politics, so no party can be chosen.", nameof(playerParty));
        if (politics)
            _politics = new PoliticsEngine(packs, _scenario, _country, seed, _satisfaction,
                (kind, id) => kind == "policy" ? _policies.GetValueOrDefault(id) : _values.GetValueOrDefault(id),
                _scenario.TurnsPerYear, playerParty, randomStart);
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
        if (_politics != null)
        {
            double cost = PolicyChangeCost(set.PolicyId, set.Value);
            if (!_politics.TrySpendCapital(cost))
                return new ActionResult(false, "error.policy_cost",
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{cost:0}|{Math.Floor(_politics.Capital + 1e-9):0}"));
        }

        _pending.Add(new PendingPolicy(set.PolicyId, set.Value, _turn + policy.DelayTurns));
        _log.Add((_turn, action));
        return ActionResult.Ok;
    }

    /// <summary>
    /// Political capital the leader pays for setting <paramref name="policyId"/> to <paramref name="value"/> now (decision 0013): measured from the
    /// value the policy will have once queued changes are in (<see cref="PolicyCost"/>); cancelling a queued change - going back to the value in
    /// force - is free and refunds nothing. 0 without politics, for an unknown policy and for indexation (a separate action).
    /// </summary>
    public double PolicyChangeCost(string policyId, double value)
    {
        if (_politics == null || !_policyDefs.TryGetValue(policyId, out var policy) || !double.IsFinite(value)) return 0.0;
        bool queued = _pending.Any(p => p.PolicyId == policyId);
        if (queued && Math.Abs(value - _policies[policyId]) <= 1e-9 * Math.Max(1.0, Math.Abs(value))) return 0.0;
        return PolicyCost.For(policy, LatestPolicy(policyId), value);
    }

    /// <summary>The settings of a policy the player's capital pays for now, around its queued value (null without politics).</summary>
    public (double Lo, double Hi)? AffordableRange(string policyId)
    {
        if (_politics == null || !_policyDefs.TryGetValue(policyId, out var policy)) return null;
        return PolicyCost.Affordable(policy, LatestPolicy(policyId), _politics.Capital);
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

    /// <summary>
    /// What reshuffling the coalition would do (decision 0013): the seats it would hold and need, the cost, a hard error (ErrorKey, e.g.
    /// error.would_lose_majority or error.reshuffle_cooldown) or the refusals the parties would answer with at the end of the quarter. Changes nothing.
    /// </summary>
    public ReshufflePreview PreviewReshuffle(IReadOnlyList<string> drop, IReadOnlyList<string> add, IReadOnlyList<AgreedDemand> agreements)
    {
        if (_politics == null) return new ReshufflePreview(0, 0, false, 0, "error.no_politics", null, Array.Empty<Refusal>(), 0, false);
        var c = _politics.CheckReshuffle(drop, add, agreements, _turn + 1);
        return new ReshufflePreview(c.SeatsHeld, c.SeatsNeeded, c.SeatsHeld >= c.SeatsNeeded, c.Cost, c.ErrorKey, c.Detail, c.Refusals, c.CooldownLeft,
            _politics.Capital + 1e-9 >= c.Cost);
    }

    /// <summary>The agreements the AI would offer the parties brought in by a reshuffle (null when the player does not lead or a price cannot be paid).</summary>
    public IReadOnlyList<AgreedDemand>? SuggestReshuffle(IReadOnlyList<string> drop, IReadOnlyList<string> add) => _politics?.SuggestReshuffle(drop, add);

    /// <summary>What announcing a policy proposal would do now (decision 0014 g); null without politics. Changes nothing.</summary>
    public AnnounceReaction? PreviewAnnouncement(string policyId, double target) => _politics?.Reaction(policyId, target);

    /// <summary>
    /// What proposing a policy would do now (decision 0015): to the leader (partner) or public (anyone else outside the lead), cost, the voters'
    /// reaction, the points of support it moves per group and the leader's likely answer; null without politics. Changes nothing.
    /// </summary>
    public ProposalPreview? PreviewProposal(string policyId, double target) => _politics?.PreviewProposal(policyId, target, _turn + 1);

    /// <summary>How many points of support (share × 100) a campaign would win in its voter group right now (decision 0015); 0 without politics.</summary>
    public double PreviewCampaign(string groupId, int effort) =>
        _politics == null ? 0.0 : _politics.SupportPoints(new Dictionary<string, double> { [groupId] = _politics.CampaignMomentum(effort) }).GetValueOrDefault(groupId);

    /// <summary>What giving the leader an ultimatum would bring now (decision 0016); null without politics. Changes nothing.</summary>
    public UltimatumPreview? PreviewUltimatum(string kind, string subjectId, int deadline) => _politics?.PreviewUltimatum(kind, subjectId, deadline, _turn + 1);

    /// <summary>
    /// How the invited parties see the player's coalition offer as formateur (decision 0016): price, what the offer pays, answer and reason, demand
    /// statuses, portfolios, counter-offers and fixes. Null without politics. Changes nothing.
    /// </summary>
    public NegotiationView? AssessCoalition(CoalitionProposal draft) => _politics?.AssessCoalition(draft);

    /// <summary>What the AI formateur would answer to the player's counter-offer as an invited party (decision 0016); null without politics.</summary>
    /// <summary>What a joint list of the player's party and these partners would do (decision 0019); null without politics. Changes nothing.</summary>
    public AlliancePreview? PreviewAlliance(IReadOnlyList<string> partners) => _politics?.PreviewAlliance(partners);

    public CounterPreview? PreviewCounter(IReadOnlyList<AgreedDemand> extra, IReadOnlyList<PortfolioGrant> portfolios) => _politics?.PreviewCounter(extra, portfolios);

    /// <summary>The cabinet portfolios of the country (decision 0016); empty without politics or without portfolios in the pack.</summary>
    public IReadOnlyList<PortfolioDef> Portfolios => _country.Politics?.Portfolios ?? Array.Empty<PortfolioDef>();

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
        double necessity = Necessity.Factor(_values); // the same softening the voters apply (decision 0014 e)
        double Dislike(double x, double pref) => Math.Abs(x - pref) * (Necessity.Austere(policy, x, pref) ? necessity : 1.0);
        foreach (var g in _packs.VoterGroups.OrderBy(g => g.Id, Ord))
            if (g.PolicyPrefs.TryGetValue(policyId, out var pref) && g.PolicyWeights.TryGetValue(policyId, out var weight))
                groups.Add(new GroupEffect(g.Id, -weight * (Dislike(value, pref) - Dislike(from, pref)) / range));
        double delta = value - from;
        double budget = delta * policy.CostPerUnit - (policy.RevenueBaseValueId != null ? delta : 0.0);
        double cost = PolicyChangeCost(policyId, value);
        return new PolicyPreview(policyId, from, value,
            effects.Select(kv => new ValueEffect(kv.Key, kv.Value.Add, kv.Value.Rel)).ToList(), groups, budget,
            cost, _politics == null || _politics.Capital + 1e-9 >= cost, necessity);
    }

    /// <summary>Why a value the economic model owns moves and what it moves in turn, from the model's own terms; null for other values.</summary>
    public ModelExplanation? ExplainValue(string valueId) => ModelExplainer.Explain(valueId, _values, _country.Parameters);

    /// <summary>Accepted policy changes that are still waiting for their delay to pass, in the order they were made.</summary>
    public IReadOnlyList<PendingPolicy> PendingPolicies => _pending.ToList();

    /// <summary>The save always names the player's party (null without politics), so a replay never depends on the scenario's default.</summary>
    public SaveData Save() => new(_scenario.Id, _seed, _packs.PackHashes, _log.ToList(), _turn, _politics?.Player, _randomStart);

    /// <summary>The share of the vote a party needs for seats right now: the threshold policy if the pack has one, else the country's rule (null without politics).</summary>
    /// <summary>Where each party stands on the economic (X) and social (Y) axes, -1..1 (empty without politics).</summary>
    public IReadOnlyDictionary<string, (double X, double Y)> Compass =>
        _politics?.Compass() ?? new Dictionary<string, (double X, double Y)>();

    public double? ElectoralThreshold => _politics?.CurrentThreshold;

    /// <summary>"proportional", "fptp" or "two_round": the kind of electoral system at the next election (null without politics; decision 0017).</summary>
    public string? ElectoralKind => _politics?.ElectoralKind;

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
        // proposals settled this turn (decision 0014) come first, so the governor already sees them in the queued settings
        foreach (var (policyId, to, answered) in _politics?.TakePassedProposals() ?? Array.Empty<(string, double, bool)>())
        {
            double from = LatestPolicy(policyId);
            if (to == from) continue;
            politicalEvents.Add(new GovernmentMeasure(_turn, policyId, from, to));
            _politics!.RecordMeasure(_turn, policyId, from, to, stanceTaken: answered);
            _pending.Add(new PendingPolicy(policyId, to, _turn + _policyDefs[policyId].DelayTurns));
        }
        if (_politics?.GovernorMove(_turn, LatestPolicy) is { } move)
        {
            double from = LatestPolicy(move.PolicyId);
            // now and then the measure is put to parliament first and the player's party answers during the next quarter
            if (_politics.OfferMeasure(_turn, move.PolicyId, from, move.Value) is { } put) politicalEvents.Add(new ProposalPut(_turn, put));
            else
            {
                politicalEvents.Add(new GovernmentMeasure(_turn, move.PolicyId, from, move.Value));
                _politics.RecordMeasure(_turn, move.PolicyId, from, move.Value);
                _pending.Add(new PendingPolicy(move.PolicyId, move.Value, _turn + _policyDefs[move.PolicyId].DelayTurns));
            }
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
