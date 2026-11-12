using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

public static class PartyMath
{
    /// <summary>
    /// Ideological distance in [0, 1]: weighted mean of |target difference| / policy range over the policies both
    /// platforms mention (weight = product of the two platform weights); 0.5 when they share none.
    /// </summary>
    public static double Distance(PartyDef a, PartyDef b, IReadOnlyDictionary<string, double> policyRange)
    {
        double num = 0, den = 0;
        foreach (var policy in a.Platform.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!b.Platform.TryGetValue(policy, out var pb) || !policyRange.TryGetValue(policy, out var range) || !(range > 0)) continue;
            var pa = a.Platform[policy];
            double w = pa.Weight * pb.Weight;
            if (!(w > 0)) continue;
            num += w * Math.Min(1.0, Math.Abs(pa.Target - pb.Target) / range);
            den += w;
        }
        return den > 0 ? num / den : 0.5;
    }

    /// <summary>Range (max - min) of every policy, keyed by policy id.</summary>
    public static Dictionary<string, double> PolicyRanges(IEnumerable<PolicyDef> policies) =>
        policies.ToDictionary(p => p.Id, p => p.Max - p.Min, StringComparer.Ordinal);

    /// <summary>
    /// How united the party is behind its platform, in [0, 1]: one minus the weight-averaged distance of its factions'
    /// preferred settings from the platform (policies the party does not mention or without a range are skipped).
    /// </summary>
    public static double Cohesion(PartyDef party, IReadOnlyDictionary<string, double> policyRange)
    {
        double totalWeight = party.Factions.Sum(f => Math.Max(0.0, f.Weight));
        if (!(totalWeight > 0)) return 1.0;
        double distance = 0;
        foreach (var f in party.Factions)
        {
            double sum = 0; int n = 0;
            foreach (var (policy, target) in f.Platform.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!party.Platform.TryGetValue(policy, out var pos) || !policyRange.TryGetValue(policy, out var range) || !(range > 0)) continue;
                sum += Math.Min(1.0, Math.Abs(target - pos.Target) / range); n++;
            }
            distance += Math.Max(0.0, f.Weight) * (n > 0 ? sum / n : 0.0);
        }
        return Math.Clamp(1.0 - distance / totalWeight, 0.0, 1.0);
    }
}
