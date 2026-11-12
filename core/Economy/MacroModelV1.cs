using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Economy;

/// <summary>
/// Small macro model: Cobb-Douglas production (diminishing returns of capital), capital accumulation, Okun-style
/// unemployment, output-gap inflation, a budget with a convex debt-risk premium. No value has a fixed ceiling;
/// limits come from the equations. Units: money in CZK (real), unemployment/inflation/interest in percent,
/// labour force in persons, policy parameters in the unit declared by the policy.
/// Graph edges on A, K, labour force, unemployment, inflation and debt change their rates. Edges on the derived
/// values (GDP, revenue, spending, interest rate) act as a one-turn flow adjustment (they do not accumulate).
/// </summary>
public sealed class MacroModelV1 : IEconomyModel
{
    public static class Ids
    {
        public const string Gdp = "base:gdp";
        public const string Capital = "base:capital";
        public const string Productivity = "base:productivity";
        public const string LabourForce = "base:labour_force";
        public const string Unemployment = "base:unemployment";
        public const string Inflation = "base:inflation";
        public const string Debt = "base:debt";
        public const string InterestRate = "base:interest_rate";
        public const string Revenue = "base:revenue";
        public const string Spending = "base:spending";
    }

    /// <summary>Parameter defaults; override per country in data (<c>parameters:</c>).</summary>
    public static readonly IReadOnlyDictionary<string, double> Defaults = new Dictionary<string, double>
    {
        ["alpha"] = 0.35,                       // capital share
        ["delta"] = 0.05,                       // depreciation per year
        ["okun_beta"] = 0.4,                    // pp unemployment change per year per 1pp growth gap
        ["base_productivity_growth"] = 0.012,   // TFP growth per year (fraction)
        ["labour_force_growth"] = 0.0,          // per year (fraction)
        ["natural_unemployment"] = 5.0,         // percent
        ["unemployment_reversion"] = 0.5,       // per year
        ["base_interest"] = 2.0,                // real interest before risk premium, percent
        ["inflation_target"] = 2.0,             // percent
        ["inflation_reversion"] = 0.5,          // per year
        ["kappa"] = 0.3,                        // inflation response to output gap
        ["fiscal_inflation"] = 0.1,             // inflation push per percent of GDP primary deficit
        ["saving_rate"] = 0.20,                 // share of output invested at the reference tax burden
        ["tax_sensitivity"] = 0.3,              // saving rate change per unit change of tax burden
        ["interest_sensitivity"] = 0.5,         // saving rate change per unit of real rate deviation
        ["revenue_ratio_ref"] = 0.40,           // reference revenue / GDP
        ["premium_max"] = 15.0,                 // max debt risk premium, percentage points
        ["premium_ratio_half"] = 1.5,           // debt/GDP at which the premium is about 76% of max
    };

    private const double MinA = 1e-9;
    private readonly IReadOnlyList<PolicyDef> _policies;

    public MacroModelV1(IEnumerable<PolicyDef> policies)
    {
        _policies = policies.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

    public string Id => "macro_v1";

    public IReadOnlyList<string> OwnedValueIds { get; } = new[]
    {
        Ids.Gdp, Ids.Capital, Ids.Productivity, Ids.LabourForce, Ids.Unemployment,
        Ids.Inflation, Ids.Debt, Ids.InterestRate, Ids.Revenue, Ids.Spending,
    };

    public static double Production(double a, double k, double l, double alpha) =>
        a * DetMath.Pow(Math.Max(k, 0), alpha) * DetMath.Pow(Math.Max(l, 0), 1 - alpha);

    /// <summary>Debt risk premium in percentage points: convex for small ratios, saturating at <c>max</c>.</summary>
    public static double InterestPremium(double debtToGdp, double max, double ratioHalf)
    {
        double x = Math.Max(debtToGdp, 0) / ratioHalf;
        return max * DetMath.Tanh(x * x);
    }

    public IReadOnlyDictionary<string, double> Step(EconomyInput input, double dt)
    {
        double P(string name) => input.Parameters.TryGetValue(name, out var v) ? v : Defaults[name];
        double V(string id) => input.Values.TryGetValue(id, out var v) && double.IsFinite(v) ? v : 0.0;
        Rate R(string id) => input.EdgeRates.TryGetValue(id, out var r) ? r : default;

        double alpha = Math.Clamp(P("alpha"), 0.01, 0.99);
        double gA = P("base_productivity_growth"), gL = P("labour_force_growth");
        double uNat = P("natural_unemployment"), r0 = P("base_interest"), piTarget = P("inflation_target");

        double a = Math.Max(V(Ids.Productivity), MinA), k = Math.Max(V(Ids.Capital), 1.0), lf = Math.Max(V(Ids.LabourForce), 1.0);
        double u = Math.Clamp(V(Ids.Unemployment), 0, 99), pi = V(Ids.Inflation), debt = V(Ids.Debt), rate = V(Ids.InterestRate);

        double yOld = Production(a, k, lf * (1 - u / 100), alpha);

        // Productivity and labour force.
        var ra = R(Ids.Productivity);
        double a2 = Math.Max(a * (1 + dt * (gA + ra.Relative)) + dt * ra.Additive, MinA);
        var rl = R(Ids.LabourForce);
        double lf2 = Math.Max(lf * (1 + dt * (gL + rl.Relative)) + dt * rl.Additive, 1.0);

        // Capital: saving falls with tax burden and with real interest above the reference.
        double revRatio = yOld > 0 ? V(Ids.Revenue) / yOld : P("revenue_ratio_ref");
        double s = Math.Clamp(P("saving_rate")
            - P("tax_sensitivity") * (revRatio - P("revenue_ratio_ref"))
            - P("interest_sensitivity") * ((rate - pi) - r0) / 100.0, 0.02, 0.6);
        var rk = R(Ids.Capital);
        double k2 = Math.Max(k + dt * (s * yOld - P("delta") * k + rk.Additive + k * rk.Relative), 1.0);

        // Unemployment: Okun (faster than potential growth lowers it) plus reversion to the natural rate.
        double yPrelim = Production(a2, k2, lf2 * (1 - u / 100), alpha);
        double gY = yOld > 0 ? (yPrelim / yOld - 1) / dt : 0.0;
        double gPot = gA / (1 - alpha) + gL;
        var ru = R(Ids.Unemployment);
        double du = -P("okun_beta") * 100 * (gY - gPot) - P("unemployment_reversion") * (u - uNat) + ru.Additive + u * ru.Relative;
        double u2 = Math.Clamp(u + dt * du, 0, 99);

        // Output and output gap.
        double y2 = Production(a2, k2, lf2 * (1 - u2 / 100), alpha);
        double yPot = Production(a2, k2, lf2 * (1 - uNat / 100), alpha);
        double gap = yPot > 0 ? y2 / yPot - 1 : 0.0;
        var rg = R(Ids.Gdp);
        y2 = Math.Max(y2 * (1 + dt * rg.Relative) + dt * rg.Additive, 0.0);

        // Budget from policies. Spending policy parameter is percent of GDP times the policy's cost_per_unit.
        double revenue = 0, spending = 0;
        foreach (var p in _policies)
        {
            double pv = input.PolicyParams.TryGetValue(p.Id, out var given) ? given : p.Default;
            if (p.RevenueBaseValueId != null)
                revenue += pv / 100.0 * (p.RevenueBaseValueId == Ids.Gdp ? y2 : V(p.RevenueBaseValueId));
            if (p.CostPerUnit != 0) spending += pv * p.CostPerUnit * y2 / 100.0;
        }
        var rr = R(Ids.Revenue);
        revenue = revenue * (1 + dt * rr.Relative) + dt * rr.Additive;
        var rs = R(Ids.Spending);
        spending = spending * (1 + dt * rs.Relative) + dt * rs.Additive;
        double primaryDeficit = spending - revenue;
        double deficitPct = y2 > 0 ? primaryDeficit / y2 * 100 : 0.0;

        // Inflation: mean reversion, output gap, fiscal impulse.
        var rp = R(Ids.Inflation);
        double dpi = -P("inflation_reversion") * (pi - piTarget) + P("kappa") * gap * 100
            + P("fiscal_inflation") * deficitPct + rp.Additive + pi * rp.Relative;
        double pi2 = pi + dt * dpi;

        // Debt and interest rate.
        double ratio = y2 > 0 ? debt / y2 : 0.0;
        double premium = InterestPremium(ratio, P("premium_max"), P("premium_ratio_half"));
        double rReal = r0 + premium;
        var rd = R(Ids.Debt);
        double debt2 = debt + dt * (primaryDeficit + rReal / 100.0 * debt + rd.Additive + debt * rd.Relative);
        var rrate = R(Ids.InterestRate);
        double rate2 = (rReal + pi2) * (1 + dt * rrate.Relative) + dt * rrate.Additive;

        return new Dictionary<string, double>
        {
            [Ids.Gdp] = Safe(y2, V(Ids.Gdp)),
            [Ids.Capital] = Safe(k2, k),
            [Ids.Productivity] = Safe(a2, a),
            [Ids.LabourForce] = Safe(lf2, lf),
            [Ids.Unemployment] = Safe(u2, u),
            [Ids.Inflation] = Safe(pi2, pi),
            [Ids.Debt] = Safe(debt2, debt),
            [Ids.InterestRate] = Safe(rate2, rate),
            [Ids.Revenue] = Safe(revenue, V(Ids.Revenue)),
            [Ids.Spending] = Safe(spending, V(Ids.Spending)),
        };
    }

    private static double Safe(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}
