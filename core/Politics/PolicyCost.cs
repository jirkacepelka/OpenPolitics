using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>
/// What a policy change costs the leading party in political capital (decision 0013), Democracy-4 style: a whole number that grows with the
/// size of the move relative to the policy's range and with the policy's weight (<see cref="PolicyDef.CapitalCost"/>, 1 by default, heavier
/// for pensions, income tax, the retirement age, electoral rules, migration):
/// <c>cost = round(weight x (Base + PerRange x |to - from| / range))</c>, at least 1 for any move of a lever with a weight, 0 for no move.
/// So a small step of an ordinary lever costs 1-3 points, a tenth of the range 3, half the range 11 (twice that for a weight-2 lever).
/// </summary>
public static class PolicyCost
{
    public const double Base = 1.0, PerRange = 20.0;

    public static double For(PolicyDef policy, double from, double to)
    {
        double range = policy.Max - policy.Min;
        double fraction = range > 0 ? Math.Abs(to - from) / range : 0.0;
        if (fraction < 1e-9 || !(policy.CapitalCost > 0)) return 0.0;
        return Math.Max(1.0, Math.Round(policy.CapitalCost * (Base + PerRange * fraction), MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// How far from <paramref name="from"/> the lever can move for at most <paramref name="capital"/> points (in the lever's units, clamped to
    /// its range): the band the slider shows as affordable. Zero when even the smallest move costs more than that.
    /// </summary>
    public static (double Lo, double Hi) Affordable(PolicyDef policy, double from, double capital)
    {
        double range = policy.Max - policy.Min;
        if (!(policy.CapitalCost > 0) || !(range > 0)) return (policy.Min, policy.Max);
        // the largest fraction f with round(w x (Base + PerRange x f)) <= capital, i.e. w x (Base + PerRange x f) < capital + 0.5
        double f = ((Math.Floor(capital + 1e-9) + 0.5) / policy.CapitalCost - Base) / PerRange;
        if (For(policy, from, from + range * 1e-9) > capital + 1e-9 || f <= 0) return (from, from);
        double d = f * range * (1 - 1e-9);
        return (Math.Max(policy.Min, from - d), Math.Min(policy.Max, from + d));
    }
}
