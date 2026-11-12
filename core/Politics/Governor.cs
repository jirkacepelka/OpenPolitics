using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>
/// The AI governor: when the player does not lead, it moves policy toward the coalition's largest unmet policy
/// commitment, by a quarter of the remaining gap (at least 2 % of the policy's range, so it arrives).
/// </summary>
public static class Governor
{
    public const double Fraction = 0.25;
    public const double MinStepOfRange = 0.02;

    public static (string PolicyId, double Value)? Move(
        Government g, Func<string, double> policy, Func<string, double> value,
        IReadOnlyDictionary<string, PolicyDef> policies, int turn)
    {
        // Largest unmet commitment first; one that cannot move any further (its policy sits at the limit of its range)
        // is skipped so it does not starve the others.
        var candidates = new List<(double Gap, Commitment C)>();
        foreach (var c in g.Commitments.OrderBy(c => c.PartyId, StringComparer.Ordinal).ThenBy(c => c.Demand.Id, StringComparer.Ordinal))
        {
            if (c.Demand.Kind != "policy" || !policies.ContainsKey(c.Demand.Subject)) continue;
            double x = policy(c.Demand.Subject);
            if (CommitmentMath.Satisfied(c.Demand, x)) continue;
            double gap = c.Demand.Weight * (1.0 - CommitmentMath.Progress(c, x));
            if (gap > 1e-12) candidates.Add((gap, c));
        }
        foreach (var (_, c) in candidates.OrderByDescending(x => x.Gap))
        {
            var def = policies[c.Demand.Subject];
            double current = policy(def.Id);
            double remaining = c.Demand.Target - current;
            double range = def.Max - def.Min;
            double step = remaining * Fraction;
            double floor = Math.Min(Math.Abs(remaining), MinStepOfRange * range);
            if (Math.Abs(step) < floor) step = Math.Sign(remaining) * floor;
            double next = Math.Clamp(current + step, def.Min, def.Max);
            if (next != current) return (def.Id, next);
        }
        return null;
    }
}
