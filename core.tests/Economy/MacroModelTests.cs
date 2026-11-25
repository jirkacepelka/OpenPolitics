using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Economy;

public class MacroModelTests
{
    private const double Dt = 0.25;
    private static readonly IReadOnlyDictionary<string, double> NoParams = new Dictionary<string, double>();
    private static readonly IReadOnlyDictionary<string, Rate> NoRates = new Dictionary<string, Rate>();

    private static PolicyDef Tax(double rate = 40) =>
        new("t:tax", "percent", 0, 100, rate, null, 0, 0, MacroModelV1.Ids.Gdp, "f");

    private static PolicyDef Spend(double pct = 40) =>
        new("t:spend", "percent_of_gdp", 0, 100, pct, null, 1.0, 0, null, "f");

    private static Dictionary<string, double> Initial()
    {
        double y0 = 4.6e12, lf = 5.3e6, u = 5, alpha = 0.35;
        double l = lf * (1 - u / 100), k = 4 * y0;
        double a = y0 / (Math.Pow(k, alpha) * Math.Pow(l, 1 - alpha));
        return new Dictionary<string, double>
        {
            [MacroModelV1.Ids.Gdp] = y0, [MacroModelV1.Ids.Capital] = k, [MacroModelV1.Ids.Productivity] = a,
            [MacroModelV1.Ids.LabourForce] = lf, [MacroModelV1.Ids.Unemployment] = u, [MacroModelV1.Ids.Inflation] = 2,
            [MacroModelV1.Ids.Debt] = 0.4 * y0, [MacroModelV1.Ids.InterestRate] = 4.1,
            [MacroModelV1.Ids.Revenue] = 0.4 * y0, [MacroModelV1.Ids.Spending] = 0.4 * y0,
        };
    }

    private static Dictionary<string, double> Step(MacroModelV1 m, Dictionary<string, double> v,
        IReadOnlyDictionary<string, Rate>? rates = null, Dictionary<string, double>? policies = null)
    {
        var next = m.Step(new EconomyInput(v, rates ?? NoRates, policies ?? new Dictionary<string, double>(), NoParams), Dt);
        var merged = new Dictionary<string, double>(v);
        foreach (var (k, x) in next) merged[k] = x;
        return merged;
    }

    // Primary surplus of 1.5% of GDP: sustainable with 0.4 debt/GDP (a balanced primary budget is not, because interest
    // keeps raising debt, which raises the risk premium and crowds out saving; see LargeDeficit... below).
    private static MacroModelV1 Model(double tax = 40, double spend = 38.5) =>
        new(new[] { Tax(tax), Spend(spend) });

    [Fact]
    public void OwnsExactlyTheSixteenMacroValues()
    {
        var m = Model();
        Assert.Equal("macro_v1", m.Id);
        Assert.Equal(16, m.OwnedValueIds.Count); // decision 0014 added base:debt_pct, decision 0021 base:core_inflation
        Assert.Contains(MacroModelV1.Ids.DebtPct, m.OwnedValueIds);
        Assert.Contains(MacroModelV1.Ids.Gdp, m.OwnedValueIds);
    }

    [Fact]
    public void OutputMatchesCobbDouglas()
    {
        var s = Step(Model(), Initial());
        double l = s[MacroModelV1.Ids.LabourForce] * (1 - s[MacroModelV1.Ids.Unemployment] / 100);
        double expected = s[MacroModelV1.Ids.Productivity] * Math.Pow(s[MacroModelV1.Ids.Capital], 0.35) * Math.Pow(l, 0.65);
        Assert.Equal(expected, s[MacroModelV1.Ids.Gdp], expected * 1e-9);
    }

    [Fact]
    public void DiminishingReturnsOfCapital()
    {
        double one = MacroModelV1.Production(100, 1e12, 5e6, 0.35);
        double two = MacroModelV1.Production(100, 2e12, 5e6, 0.35);
        Assert.True(two > one);
        Assert.True(two < 2 * one);
        Assert.Equal(Math.Pow(2, 0.35), two / one, 9);
    }

    [Fact]
    public void NoFixedCeilingWithProductivityGrowth()
    {
        var m = Model();
        var s = Initial();
        var gdp = new List<double> { s[MacroModelV1.Ids.Gdp] };
        for (int i = 0; i < 400; i++) { s = Step(m, s); gdp.Add(s[MacroModelV1.Ids.Gdp]); }
        Assert.True(gdp[80] > gdp[40]);
        Assert.True(gdp[400] > 2 * gdp[80], $"gdp[400]={gdp[400]:E3} gdp[80]={gdp[80]:E3}");
        double lateGrowth = Math.Pow(gdp[80] / gdp[72], 1 / (8 * Dt)) - 1;      // annualised, turns 72..80
        double veryLate = Math.Pow(gdp[400] / gdp[392], 1 / (8 * Dt)) - 1;
        Assert.InRange(lateGrowth, 0.005, 0.06);
        Assert.InRange(veryLate, 0.005, 0.06);
    }

    [Fact]
    public void DebtCrowdsOutInvestmentWhenDebtIsHigh()
    {
        var low = Initial();
        var high = Initial();
        high[MacroModelV1.Ids.Debt] = 3.0 * high[MacroModelV1.Ids.Gdp];
        double kLow = Step(Model(), low)[MacroModelV1.Ids.Capital];
        double kHigh = Step(Model(), high)[MacroModelV1.Ids.Capital];
        Assert.True(kHigh < kLow);
    }

    [Fact]
    public void InterestPremiumRisesMonotonicallyWithDebtRatio()
    {
        double prev = -1;
        for (double ratio = 0; ratio <= 6; ratio += 0.1)
        {
            double p = MacroModelV1.InterestPremium(ratio, 15, 1.5);
            Assert.True(p >= prev);
            prev = p;
        }
        Assert.True(MacroModelV1.InterestPremium(1.0, 15, 1.5) > 4 * MacroModelV1.InterestPremium(0.5, 15, 1.5) * 0.9);
    }

    [Fact]
    public void LargeDeficitRaisesDebtRatioAndInterestRate()
    {
        var m = Model(tax: 35, spend: 55);
        var s = Initial();
        double ratio0 = s[MacroModelV1.Ids.Debt] / s[MacroModelV1.Ids.Gdp];
        double r0 = s[MacroModelV1.Ids.InterestRate];
        for (int i = 0; i < 80; i++) s = Step(m, s);
        Assert.True(s[MacroModelV1.Ids.Debt] / s[MacroModelV1.Ids.Gdp] > ratio0 * 2);
        Assert.True(s[MacroModelV1.Ids.InterestRate] > r0 + 1);
    }

    [Theory]
    [InlineData(1e9, 0)]
    [InlineData(-1e9, 0)]
    [InlineData(0, 1e9)]
    [InlineData(0, -1e9)]
    public void UnemploymentStaysInPhysicalRange(double additive, double relative)
    {
        var rates = new Dictionary<string, Rate> { [MacroModelV1.Ids.Unemployment] = new Rate(additive, relative) };
        var s = Step(Model(), Initial(), rates);
        Assert.InRange(s[MacroModelV1.Ids.Unemployment], 0.0, 100.0);
        Assert.True(double.IsFinite(s[MacroModelV1.Ids.Gdp]));
    }

    [Fact]
    public void ProductivityEdgeRaisesGrowth()
    {
        var withEdge = new Dictionary<string, Rate> { [MacroModelV1.Ids.Productivity] = new Rate(0, 0.01) };
        var a = Step(Model(), Initial());
        var b = Step(Model(), Initial(), withEdge);
        Assert.True(b[MacroModelV1.Ids.Productivity] > a[MacroModelV1.Ids.Productivity]);
    }

    [Fact]
    public void RevenueAndSpendingFollowPolicies()
    {
        var m = Model();
        var s = Step(m, Initial(), policies: new Dictionary<string, double> { ["t:tax"] = 30, ["t:spend"] = 10 });
        double y = s[MacroModelV1.Ids.Gdp];
        Assert.Equal(0.30 * y, s[MacroModelV1.Ids.Revenue], y * 1e-9);
        Assert.Equal(0.10 * y, s[MacroModelV1.Ids.Spending], y * 1e-9);
    }

    [Fact]
    public void NoNaNOrInfOverTenThousandTurns()
    {
        var m = Model();
        var s = Initial();
        for (int i = 0; i < 10_000; i++)
        {
            s = Step(m, s);
            foreach (var (k, v) in s) Assert.True(double.IsFinite(v), $"{k} at turn {i}: {v}");
        }
    }

    [Fact]
    public void ReplacingValuesWithGarbageDoesNotProduceNaN()
    {
        var s = Initial();
        s[MacroModelV1.Ids.Capital] = 0;
        s[MacroModelV1.Ids.LabourForce] = 0;
        s[MacroModelV1.Ids.Gdp] = 0;
        var next = Step(Model(), s);
        foreach (var (k, v) in next) Assert.True(double.IsFinite(v), k);
    }
}
