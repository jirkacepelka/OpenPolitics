using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;
using Xunit;

namespace OpenPolitics.Core.Tests.Economy;

public class PriceLevelTests
{
    private const double Dt = 0.25;
    private static readonly IReadOnlyDictionary<string, double> NoParams = new Dictionary<string, double>();
    private static readonly IReadOnlyDictionary<string, Rate> NoRates = new Dictionary<string, Rate>();
    private static readonly MacroModelV1 Model = new(new[]
    {
        new PolicyDef("t:tax", "percent", 0, 100, 40, null, 0, 0, MacroModelV1.Ids.Gdp, "f"),
        new PolicyDef("t:spend", "percent_of_gdp", 0, 100, 38.5, null, 1.0, 0, null, "f"),
    });

    private static Dictionary<string, double> Initial(double inflation = 2)
    {
        double y0 = 4.6e12, lf = 5.3e6, u = 5, alpha = 0.35;
        double l = lf * (1 - u / 100), k = 4 * y0;
        double a = y0 / (Math.Pow(k, alpha) * Math.Pow(l, 1 - alpha));
        return new Dictionary<string, double>
        {
            [MacroModelV1.Ids.Gdp] = y0, [MacroModelV1.Ids.Capital] = k, [MacroModelV1.Ids.Productivity] = a,
            [MacroModelV1.Ids.LabourForce] = lf, [MacroModelV1.Ids.Unemployment] = u, [MacroModelV1.Ids.Inflation] = inflation,
            [MacroModelV1.Ids.Debt] = 0.4 * y0, [MacroModelV1.Ids.InterestRate] = 4.1,
            [MacroModelV1.Ids.Revenue] = 0.4 * y0, [MacroModelV1.Ids.Spending] = 0.4 * y0,
            [MacroModelV1.Ids.PriceLevel] = 100,
        };
    }

    private static Dictionary<string, double> Step(Dictionary<string, double> v)
    {
        var next = Model.Step(new EconomyInput(v, NoRates, new Dictionary<string, double>(), NoParams), Dt);
        var merged = new Dictionary<string, double>(v);
        foreach (var (k, x) in next) merged[k] = x;
        return merged;
    }

    [Fact]
    public void PriceLevelGrowsWithInflation()
    {
        var v = Initial(4);
        double expected = 100;
        for (int i = 0; i < 4; i++)
        {
            v = Step(v);
            expected *= 1 + Dt * v[MacroModelV1.Ids.Inflation] / 100;
        }
        Assert.Equal(expected, v[MacroModelV1.Ids.PriceLevel], 6);
        Assert.InRange(v[MacroModelV1.Ids.PriceLevel], 102.5, 104.5);
    }

    [Fact]
    public void NominalGdpIsRealTimesPriceLevel()
    {
        var v = Step(Initial());
        Assert.Equal(v[MacroModelV1.Ids.Gdp] * v[MacroModelV1.Ids.PriceLevel] / 100, v[MacroModelV1.Ids.GdpNominal], 3);
    }

    [Fact]
    public void RealGrowthMatchesGdpChange()
    {
        var v0 = Initial();
        var v1 = Step(v0);
        double expected = (v1[MacroModelV1.Ids.Gdp] / v0[MacroModelV1.Ids.Gdp] - 1) / Dt * 100;
        Assert.Equal(expected, v1[MacroModelV1.Ids.RealGrowth], 9);
    }

    [Fact]
    public void DeficitPercentIsBudgetBalanceIncludingInterest()
    {
        var v = Step(Initial());
        // primary surplus of 1.5 % of GDP minus about 0.8 % of interest
        Assert.InRange(v[MacroModelV1.Ids.DeficitPct], -1.5, 0.0);
    }

    [Fact]
    public void PriceLevelStaysPositiveAtNegativeInflation()
    {
        var v = Initial(-20);
        for (int i = 0; i < 40; i++) v = Step(v);
        Assert.True(v[MacroModelV1.Ids.PriceLevel] > 0 && double.IsFinite(v[MacroModelV1.Ids.PriceLevel]));
    }
}
