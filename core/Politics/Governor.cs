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

    public const double DeficitCrisisPct = 5.0, UnemploymentCrisisPct = 8.0, CrisisStepOfRange = 0.10, MinPlatformGapOfRange = 0.05;

    /// <summary>
    /// One move of the AI leader. Unmet commitments come first; then (when a leader party is known) crisis responses
    /// (every turn) and drift toward the leader's own platform (every second turn). Moves that would newly cross a
    /// partner's red line are skipped while an alternative exists.
    /// </summary>
    public static (string PolicyId, double Value)? Move(
        Government g, Func<string, double> policy, Func<string, double> value,
        IReadOnlyDictionary<string, PolicyDef> policies, int turn,
        PartyDef? leader = null, IReadOnlyList<RedLineDef>? partnerRedLines = null)
    {
        var fromCommitments = MoveForCommitments(g, policy, policies);
        if (fromCommitments != null || leader == null) return fromCommitments;

        var candidates = new List<(double Score, string Id, double Next)>();
        double deficit = value("base:deficit_pct"), unemployment = value("base:unemployment"), inflation = value("base:inflation");
        bool Unmet(string valueId) => g.Commitments.Any(c => c.Demand.Kind == "value" && c.Demand.Subject == valueId
            && !CommitmentMath.Satisfied(c.Demand, value(valueId)));
        if (deficit > DeficitCrisisPct || Unmet("base:deficit_pct") || Unmet("base:inflation"))
        {
            var biggest = policies.Values.Where(p => p.CostPerUnit != 0 && policy(p.Id) > p.Min + 1e-9)
                .OrderByDescending(p => policy(p.Id) * p.CostPerUnit).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (biggest != null)
                candidates.Add((10 + deficit, biggest.Id, Math.Max(biggest.Min, policy(biggest.Id) - CrisisStepOfRange * (biggest.Max - biggest.Min))));
        _ = inflation; // reserved: a hot economy is cooled by the same tightening when a partner demands low inflation
        }
        if (unemployment >= UnemploymentCrisisPct || Unmet("base:unemployment"))
        {
            foreach (var p in policies.Values.Where(p => p.CostPerUnit != 0).OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                if (!leader.Platform.TryGetValue(p.Id, out var pos) || !(p.Max > p.Min)) continue;
                double x = policy(p.Id);
                if (pos.Target <= x) continue;
                candidates.Add((10 + (pos.Target - x) / (p.Max - p.Min), p.Id, Math.Min(Math.Min(p.Max, pos.Target), x + CrisisStepOfRange * (p.Max - p.Min))));
            }
        }
        if (turn % 2 == 0)
            foreach (var p in policies.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                if (!leader.Platform.TryGetValue(p.Id, out var pos) || !(p.Max > p.Min)) continue;
                double x = policy(p.Id), range = p.Max - p.Min, remaining = pos.Target - x, gap = Math.Abs(remaining) / range;
                if (gap <= MinPlatformGapOfRange) continue;
                double step = remaining * Fraction;
                double floor = Math.Min(Math.Abs(remaining), MinStepOfRange * range);
                if (Math.Abs(step) < floor) step = Math.Sign(remaining) * floor;
                candidates.Add((0.5 * pos.Weight * gap, p.Id, Math.Clamp(x + step, p.Min, p.Max)));
            }

        var ordered = candidates.Where(c => c.Next != policy(c.Id)).OrderByDescending(c => c.Score).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        bool Crosses((double Score, string Id, double Next) c) => (partnerRedLines ?? Array.Empty<RedLineDef>()).Any(r =>
            r.Policy == c.Id && LoyaltyModel.RedLineViolated(r, c.Next) && !LoyaltyModel.RedLineViolated(r, policy(c.Id)));
        foreach (var c in ordered) if (!Crosses(c)) return (c.Id, c.Next);
        return ordered.Count > 0 ? (ordered[0].Id, ordered[0].Next) : null;
    }

    private static (string PolicyId, double Value)? MoveForCommitments(
        Government g, Func<string, double> policy, IReadOnlyDictionary<string, PolicyDef> policies)
    {        // Largest unmet commitment first; one that cannot move any further (its policy sits at the limit of its range)
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
