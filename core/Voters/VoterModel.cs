using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Voters;

/// <summary>
/// Voter-group satisfaction, an attitude bounded to [-1, 1] (the one bounded quantity in the model).
/// ds/dt = -lambda*s + sum_v w_gv * (annual relative change of value v) - sum_p w_gp * |policy_p - pref_gp| / range_p.
/// A policy at the group's preferred setting is neutral; any deviation lowers satisfaction.
/// </summary>
public sealed class VoterModel
{
    private readonly IReadOnlyList<VoterGroupDef> _groups;
    private readonly Dictionary<string, double> _policyRange;
    private readonly Dictionary<string, PolicyDef> _policies;
    private readonly double _lambda;
    private readonly Dictionary<string, double> _habit = new(StringComparer.Ordinal);
    private const double HabitHalfLifeYears = 3.0;

    public VoterModel(IReadOnlyList<VoterGroupDef> groups, IReadOnlyList<ValueDef> values,
        IReadOnlyList<PolicyDef> policies, double lambda = 0.5)
    {
        _groups = groups.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        _policyRange = policies.ToDictionary(p => p.Id, p => Math.Max(p.Max - p.Min, 1e-12));
        _policies = policies.ToDictionary(p => p.Id);
        _lambda = lambda;
    }

    /// <param name="valueChangePerYear">annual relative change per value id: (new-old)/max(|old|,eps)/dt</param>
    public IReadOnlyDictionary<string, double> Step(
        IReadOnlyDictionary<string, double> satisfaction,
        IReadOnlyDictionary<string, double> valueChangePerYear,
        IReadOnlyDictionary<string, double> policyParams,
        double dt,
        IReadOnlyDictionary<string, double>? values = null)
    {
        double habitKeep = DetMath.Pow(0.5, dt / HabitHalfLifeYears);
        var result = new SortedDictionary<string, double>(StringComparer.Ordinal);
        double necessity = Necessity.Factor(values); // decision 0014 (e): austerity hurts less when the deficit or debt is high
        foreach (var g in _groups)
        {
            double s = satisfaction.TryGetValue(g.Id, out var s0) && double.IsFinite(s0) ? s0 : 0.0;
            double d = -_lambda * s;
            foreach (var (valueId, w) in g.ValueWeights.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (valueChangePerYear.TryGetValue(valueId, out var change) && double.IsFinite(change)) d += w * change;
            foreach (var (policyId, w) in g.PolicyWeights.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (g.PolicyPrefs.TryGetValue(policyId, out var pref) && policyParams.TryGetValue(policyId, out var p)
                    && _policyRange.TryGetValue(policyId, out var range))
                    d -= w * Math.Abs(p - pref) / range * (necessity < 1.0 && Necessity.Austere(_policies[policyId], p, pref) ? necessity : 1.0);
            foreach (var (valueId, w) in (g.LevelWeights ?? new Dictionary<string, double>()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (values != null && values.TryGetValue(valueId, out var level) && double.IsFinite(level))
                {
                    double habit = _habit.GetValueOrDefault(valueId, level);
                    const double eps = 1e-9;
                    d += w * DetMath.Log(Math.Max(level, eps) / Math.Max(habit, eps));
                }
            double next = s + dt * d;
            result[g.Id] = double.IsNaN(next) ? s : Math.Clamp(next, -1.0, 1.0);
        }
        if (values != null)
            foreach (var id in _groups.SelectMany(g => g.LevelWeights?.Keys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
                if (values.TryGetValue(id, out var level) && double.IsFinite(level))
                    _habit[id] = _habit.TryGetValue(id, out var h) ? h * habitKeep + level * (1 - habitKeep) : level;
        return result;
    }
}
