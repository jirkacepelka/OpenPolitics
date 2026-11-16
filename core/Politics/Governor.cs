using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>
/// The AI governor: when the player does not lead, it moves policy toward the coalition's largest unmet policy
/// commitment, by a quarter of the remaining gap (at least 2 % of the policy's range, so it arrives).
/// </summary>
public static class Governor
{
    /// <summary>A policy that drives a value through an additive edge of the pack; Sign is the edge's strength (its sign says which way).</summary>
    public sealed record Lever(string PolicyId, double Sign);

    /// <summary>For every value, the policies that drive it directly (additive edges from a policy), in policy id order (decision 0014).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Lever>> Levers(PackSet packs)
    {
        var policyIds = new HashSet<string>(packs.Policies.Select(p => p.Id), StringComparer.Ordinal);
        return packs.Edges.Where(e => e.Kind == EdgeKind.Additive && policyIds.Contains(e.From) && e.Strength != 0)
            .GroupBy(e => e.To, StringComparer.Ordinal)
            .ToDictionary(gr => gr.Key, gr => (IReadOnlyList<Lever>)gr.OrderBy(e => e.From, StringComparer.Ordinal).Select(e => new Lever(e.From, e.Strength)).ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>Values the crisis responses below already handle; other unmet value goals are worked on through their levers.</summary>
    private static readonly HashSet<string> Handled = new(StringComparer.Ordinal)
        { "base:deficit_pct", "base:inflation", "base:debt_pct", "base:unemployment", "base:real_growth" };

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
        PartyDef? leader = null, IReadOnlyList<RedLineDef>? partnerRedLines = null,
        IReadOnlyDictionary<string, IReadOnlyList<Lever>>? levers = null)
    {
        var fromCommitments = MoveForCommitments(g, policy, policies);
        if (fromCommitments != null || leader == null) return fromCommitments;

        var candidates = new List<(double Score, string Id, double Next)>();
        double deficit = value("base:deficit_pct"), unemployment = value("base:unemployment");
        bool Unmet(string valueId) => g.Commitments.Any(c => c.Demand.Kind == "value" && c.Demand.Subject == valueId
            && !CommitmentMath.Satisfied(c.Demand, value(valueId)));
        if (deficit > DeficitCrisisPct || Unmet("base:deficit_pct") || Unmet("base:inflation") || Unmet("base:debt_pct"))
        {
            var biggest = policies.Values.Where(p => p.CostPerUnit != 0 && policy(p.Id) > p.Min + 1e-9)
                .OrderByDescending(p => policy(p.Id) * p.CostPerUnit).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (biggest != null)
                candidates.Add((10 + deficit, biggest.Id, Math.Max(biggest.Min, policy(biggest.Id) - CrisisStepOfRange * (biggest.Max - biggest.Min))));
        }
        if (unemployment >= UnemploymentCrisisPct || Unmet("base:unemployment") || Unmet("base:real_growth"))
        {
            foreach (var p in policies.Values.Where(p => p.CostPerUnit != 0).OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                if (!leader.Platform.TryGetValue(p.Id, out var pos) || !(p.Max > p.Min)) continue;
                double x = policy(p.Id);
                if (pos.Target <= x) continue;
                candidates.Add((10 + (pos.Target - x) / (p.Max - p.Min), p.Id, Math.Min(Math.Min(p.Max, pos.Target), x + CrisisStepOfRange * (p.Max - p.Min))));
            }
        }
        // other unmet value goals (crime, environment, inequality, ...): a tenth of the range on the lever that moves the value most
        if (levers != null)
            foreach (var c in g.Commitments.Where(c => c.Demand.Kind == "value" && !Handled.Contains(c.Demand.Subject))
                         .OrderBy(c => c.PartyId, StringComparer.Ordinal).ThenBy(c => c.Demand.Id, StringComparer.Ordinal))
            {
                if (CommitmentMath.Satisfied(c.Demand, value(c.Demand.Subject)) || !levers.TryGetValue(c.Demand.Subject, out var list)) continue;
                double want = c.Demand.Op == "<=" ? -1.0 : 1.0;
                (double Effect, string Id, double Next)? best = null;
                foreach (var l in list)
                {
                    if (!policies.TryGetValue(l.PolicyId, out var p) || !(p.Max > p.Min)) continue;
                    double dir = want * Math.Sign(l.Sign), x = policy(p.Id);
                    double next = Math.Clamp(x + dir * CrisisStepOfRange * (p.Max - p.Min), p.Min, p.Max);
                    double effect = Math.Abs(l.Sign) * Math.Abs(next - x);
                    if (effect > 0 && (best == null || effect > best.Value.Effect)) best = (effect, p.Id, next);
                }
                if (best is { } b) candidates.Add((9 + 0.1 * c.Demand.Weight, b.Id, b.Next));
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

        // a commitment that is met stays met: drift and crisis moves never undo what the coalition agreement already got
        bool Breaks((double Score, string Id, double Next) c) => g.Commitments.Any(cm => cm.Demand.Kind == "policy" && cm.Demand.Subject == c.Id
            && CommitmentMath.Satisfied(cm.Demand, policy(c.Id)) && !CommitmentMath.Satisfied(cm.Demand, c.Next));
        var ordered = candidates.Where(c => c.Next != policy(c.Id) && !Breaks(c)).OrderByDescending(c => c.Score).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        bool Crosses((double Score, string Id, double Next) c) => (partnerRedLines ?? Array.Empty<RedLineDef>()).Any(r =>
            r.Policy == c.Id && LoyaltyModel.RedLineViolated(r, c.Next) && !LoyaltyModel.RedLineViolated(r, policy(c.Id)));
        foreach (var c in ordered) if (!Crosses(c)) return (c.Id, c.Next);
        return ordered.Count > 0 ? (ordered[0].Id, ordered[0].Next) : null;
    }

    /// <summary>
    /// What an AI partner asks the leading player for (decision 0014): its own unmet policy commitment, else the platform position furthest from the
    /// current setting (weight times gap, more than <see cref="MinPlatformGapOfRange"/> of the range), a quarter of the way there. Moves that would
    /// newly cross another member's red line or undo a met commitment are not asked for. Null when there is nothing to ask.
    /// </summary>
    public static (string PolicyId, double Value)? Request(
        PartyDef partner, IReadOnlyList<Commitment> own, IReadOnlyList<Commitment> all, Func<string, double> policy,
        IReadOnlyDictionary<string, PolicyDef> policies, IReadOnlyList<RedLineDef> othersRedLines)
    {
        bool Ok(string id, double next) => next != policy(id)
            && !othersRedLines.Any(r => r.Policy == id && LoyaltyModel.RedLineViolated(r, next) && !LoyaltyModel.RedLineViolated(r, policy(id)))
            && !all.Any(c => c.Demand.Kind == "policy" && c.Demand.Subject == id && CommitmentMath.Satisfied(c.Demand, policy(id)) && !CommitmentMath.Satisfied(c.Demand, next));
        if (MoveForCommitments(new Government(partner.Id, new[] { partner.Id }, own, 0), policy, policies) is { } m && Ok(m.PolicyId, m.Value)) return m;
        var drift = new List<(double Score, string Id, double Next)>();
        foreach (var (id, pos) in partner.Platform.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!policies.TryGetValue(id, out var p) || !(p.Max > p.Min)) continue;
            double x = policy(id), range = p.Max - p.Min, remaining = pos.Target - x, gap = Math.Abs(remaining) / range;
            if (gap <= MinPlatformGapOfRange) continue;
            double step = remaining * Fraction, floor = Math.Min(Math.Abs(remaining), MinStepOfRange * range);
            if (Math.Abs(step) < floor) step = Math.Sign(remaining) * floor;
            drift.Add((pos.Weight * gap, id, Math.Clamp(x + step, p.Min, p.Max)));
        }
        foreach (var c in drift.OrderByDescending(c => c.Score).ThenBy(c => c.Id, StringComparer.Ordinal))
            if (Ok(c.Id, c.Next)) return (c.Id, c.Next);
        return null;
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
