using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>Pure math on coalition commitments: how far a demand is delivered and how far it should be by now.</summary>
public static class CommitmentMath
{
    public static bool Satisfied(DemandDef d, double x) => d.Op == ">=" ? x >= d.Target : x <= d.Target;

    /// <summary>0..1: 1 when satisfied, otherwise how far the value has moved from its baseline toward the target.</summary>
    public static double Progress(Commitment c, double x)
    {
        if (Satisfied(c.Demand, x)) return 1.0;
        double span = c.Demand.Target - c.Baseline;
        if (span == 0) return 0.0;
        return Math.Clamp((x - c.Baseline) / span, 0.0, 1.0);
    }

    /// <summary>Delivery is expected gradually: 0 at formation, 1 at the demand's due time.</summary>
    public static double Expected(Commitment c, int turn, int formedTurn) =>
        Math.Min(1.0, Math.Max(0, turn - (c.StartTurn >= 0 ? c.StartTurn : formedTurn)) / (double)Math.Max(1, c.Demand.DueTurns));

    public static double Fulfil(IReadOnlyList<Commitment> mine, Func<Commitment, double> current, int turn, int formedTurn)
    {
        double w = mine.Sum(c => c.Demand.Weight);
        return w > 0 ? mine.Sum(c => c.Demand.Weight * Progress(c, current(c))) / w : 0.5;
    }

    public static double Shortfall(IReadOnlyList<Commitment> mine, Func<Commitment, double> current, int turn, int formedTurn)
    {
        double w = mine.Sum(c => c.Demand.Weight);
        return w > 0
            ? mine.Sum(c => c.Demand.Weight * Math.Max(0.0, Expected(c, turn, formedTurn) - Progress(c, current(c)))) / w
            : 0.0;
    }

    /// <summary>The commitment that is furthest behind (weight times the gap); null when none is late.</summary>
    public static Commitment? MostLate(IReadOnlyList<Commitment> mine, Func<Commitment, double> current, int turn, int formedTurn)
    {
        Commitment? best = null;
        double bestGap = 1e-12;
        foreach (var c in mine.OrderBy(c => c.Demand.Id, StringComparer.Ordinal))
        {
            double gap = c.Demand.Weight * (Expected(c, turn, formedTurn) - Progress(c, current(c)));
            if (gap > bestGap) { best = c; bestGap = gap; }
        }
        return best;
    }
}
