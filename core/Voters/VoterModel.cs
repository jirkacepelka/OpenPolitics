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
    private readonly double _lambda;

    public VoterModel(IReadOnlyList<VoterGroupDef> groups, IReadOnlyList<ValueDef> values,
        IReadOnlyList<PolicyDef> policies, double lambda = 0.5)
    {
        _groups = groups.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        _policyRange = policies.ToDictionary(p => p.Id, p => Math.Max(p.Max - p.Min, 1e-12));
        _lambda = lambda;
    }

    /// <param name="valueChangePerYear">annual relative change per value id: (new-old)/max(|old|,eps)/dt</param>
    public IReadOnlyDictionary<string, double> Step(
        IReadOnlyDictionary<string, double> satisfaction,
        IReadOnlyDictionary<string, double> valueChangePerYear,
        IReadOnlyDictionary<string, double> policyParams,
        double dt)
    {
        var result = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var g in _groups)
        {
            double s = satisfaction.TryGetValue(g.Id, out var s0) && double.IsFinite(s0) ? s0 : 0.0;
            double d = -_lambda * s;
            foreach (var (valueId, w) in g.ValueWeights.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (valueChangePerYear.TryGetValue(valueId, out var change) && double.IsFinite(change)) d += w * change;
            foreach (var (policyId, w) in g.PolicyWeights.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (g.PolicyPrefs.TryGetValue(policyId, out var pref) && policyParams.TryGetValue(policyId, out var p)
                    && _policyRange.TryGetValue(policyId, out var range))
                    d -= w * Math.Abs(p - pref) / range;
            double next = s + dt * d;
            result[g.Id] = double.IsNaN(next) ? s : Math.Clamp(next, -1.0, 1.0);
        }
        return result;
    }
}
