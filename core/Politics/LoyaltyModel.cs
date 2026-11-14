using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>Tunable loyalty constants (spec M2 section 6). Defaults are the spec's numbers.</summary>
public sealed record LoyaltyParams(
    double Start = 70, double WarnBelow = 60, double WarnReset = 65, double UltimatumBelow = 40, int UltimatumTurns = 4,
    double ResolveAbove = 50, double LeaveAt = 15, double Shock = 30, double Lambda = 1.0, double SwingLambda = 2.0);

/// <summary>Target-and-lag model of a partner's willingness to stay, in [0, 100].</summary>
public static class LoyaltyModel
{
    public static double Target(double fulfil, double shortfall, double baseMood, bool redLineViolated) =>
        Math.Clamp(50 + 40 * fulfil - 60 * shortfall + 20 * baseMood - (redLineViolated ? 20 : 0), 0, 100);

    /// <summary>A partner's loyalty when a government forms: better relations help, a grudge against the leader hurts.</summary>
    public static double StartLoyalty(double start, double relation, double grudge) =>
        Math.Clamp(start + 20 * (relation - 0.3) / 0.7 - 30 * grudge, 20, 85);

    public static double Step(double loyalty, double target, double lambdaPerYear, double dt) =>
        Math.Clamp(loyalty + lambdaPerYear * dt * (target - loyalty), 0, 100);

    public static bool RedLineViolated(RedLineDef r, double policyValue) =>
        r.Op == ">=" ? policyValue >= r.Threshold : policyValue <= r.Threshold;

    /// <summary>How happy the party's own voters are: group satisfaction weighted by size and the party's affinity.</summary>
    public static double BaseMood(PartyDef p, IReadOnlyList<VoterGroupDef> groups, IReadOnlyDictionary<string, double> satisfaction)
    {
        double total = 0, mood = 0;
        foreach (var g in groups.OrderBy(g => g.Id, StringComparer.Ordinal))
        {
            double w = g.Size * DetMath.Exp(p.Affinity.GetValueOrDefault(g.Id, 0.0));
            total += w;
            mood += w * Math.Clamp(satisfaction.GetValueOrDefault(g.Id, 0.0), -1.0, 1.0);
        }
        return total > 0 ? Math.Clamp(mood / total, -1.0, 1.0) : 0.0;
    }
}
