using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Voters;

/// <summary>
/// Decision 0014 (e): when the deficit or the debt is high, voters accept the cure more readily. The dissatisfaction a group feels about a policy
/// set on the "austere" side of its preference (spending below it, a tax above it) is scaled by <see cref="Factor"/>: 1 up to a deficit of
/// <see cref="DeficitFrom"/> % and a debt of <see cref="DebtFrom"/> % of GDP, then falling, never below <see cref="Floor"/>. Austerity is still
/// disliked (the factor is positive); the generous side of a preference is never softened.
/// </summary>
public static class Necessity
{
    public const string DeficitId = "base:deficit_pct", DebtId = "base:debt_pct";
    public const double DeficitFrom = 3.0, DebtFrom = 60.0, PerDeficitPoint = 0.08, PerDebtPoint = 0.01, Floor = 0.4;

    public static double Factor(double deficitPct, double debtPct) =>
        Math.Max(Floor, 1.0 - PerDeficitPoint * Math.Max(0.0, deficitPct - DeficitFrom) - PerDebtPoint * Math.Max(0.0, debtPct - DebtFrom));

    /// <summary>The factor for the current values (1 when the pack has neither value).</summary>
    public static double Factor(IReadOnlyDictionary<string, double>? values) =>
        values == null ? 1.0 : Factor(values.TryGetValue(DeficitId, out var d) && double.IsFinite(d) ? d : 0.0,
            values.TryGetValue(DebtId, out var b) && double.IsFinite(b) ? b : 0.0);

    /// <summary>+1 when raising the policy lowers the deficit (a tax), -1 when lowering it does (spending), 0 otherwise.</summary>
    public static int AusterityDirection(PolicyDef p) => p.RevenueBaseValueId != null ? 1 : p.CostPerUnit > 0 ? -1 : 0;

    /// <summary>Whether a setting lies on the austere side of a preference (spending below it, a tax above it).</summary>
    public static bool Austere(PolicyDef p, double setting, double preference) =>
        AusterityDirection(p) is var dir && dir != 0 && Math.Sign(setting - preference) == dir;
}
