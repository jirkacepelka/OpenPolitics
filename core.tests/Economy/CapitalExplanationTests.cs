using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;
using Xunit;

namespace OpenPolitics.Core.Tests.Economy;

/// <summary>The capital stock explains itself: what it adds to output and what it gains and loses each year.</summary>
public class CapitalExplanationTests
{
    private static readonly IReadOnlyDictionary<string, double> NoParams = new Dictionary<string, double>();
    private static readonly IReadOnlyDictionary<string, Rate> NoRates = new Dictionary<string, Rate>();

    private static Dictionary<string, double> Values()
    {
        double y0 = 4.6e12, lf = 5.3e6, u = 5, alpha = 0.35;
        double l = lf * (1 - u / 100), k = 4 * y0;
        double a = y0 / (Math.Pow(k, alpha) * Math.Pow(l, 1 - alpha));
        return new Dictionary<string, double>
        {
            [MacroModelV1.Ids.Gdp] = y0, [MacroModelV1.Ids.Capital] = k, [MacroModelV1.Ids.Productivity] = a,
            [MacroModelV1.Ids.LabourForce] = lf, [MacroModelV1.Ids.Unemployment] = u, [MacroModelV1.Ids.Inflation] = 2,
            [MacroModelV1.Ids.Debt] = 0.4 * y0, [MacroModelV1.Ids.InterestRate] = 4.1,
            [MacroModelV1.Ids.Revenue] = 0.4 * y0, [MacroModelV1.Ids.Spending] = 0.38 * y0,
        };
    }

    private static MacroModelV1 Model() => new(new[]
    {
        new PolicyDef("t:tax", "percent", 0, 100, 40, null, 0, 0, MacroModelV1.Ids.Gdp, "f"),
        new PolicyDef("t:spend", "percent_of_gdp", 0, 100, 38, null, 1.0, 0, null, "f"),
    });

    [Fact]
    public void NetFlowIsWhatOneStepActuallyChangesCapitalBy()
    {
        var v = Values();
        var e = MacroModelV1.ExplainCapital(v, NoParams);
        var next = Model().Step(new EconomyInput(v, NoRates, new Dictionary<string, double>(), NoParams), 0.25);
        double stepped = next[MacroModelV1.Ids.Capital] - v[MacroModelV1.Ids.Capital];
        Assert.InRange(Math.Abs(stepped - 0.25 * (e.Investment - e.Depreciation)), 0, 1000); // money units: a few thousand of 18 trillion
        Assert.Equal(e.Investment - e.Depreciation, e.NetChange, 3);
    }

    [Fact]
    public void OnePercentMoreCapitalAddsAlphaPercentToOutput()
    {
        var v = Values();
        var e = MacroModelV1.ExplainCapital(v, NoParams);
        Assert.Equal(0.35, e.OutputElasticity, 6);
        Assert.InRange(Math.Abs(0.35 * 0.01 * v[MacroModelV1.Ids.Gdp] - e.OutputPerOnePercent), 0, 1e6);
    }

    [Fact]
    public void SavingRateExplainsItsPartsAndTheyAddUp()
    {
        var v = Values();
        v[MacroModelV1.Ids.Debt] = 1.2 * v[MacroModelV1.Ids.Gdp]; // heavy debt crowds saving out
        var e = MacroModelV1.ExplainCapital(v, NoParams);
        Assert.True(e.DebtCrowding > 0);
        Assert.Equal(e.BaseSaving - e.TaxEffect - e.InterestEffect - e.DebtCrowding, e.SavingRate, 9);
        Assert.InRange(e.SavingRate, 0, 0.6);
    }
}

public class ModelExplanationTests
{
    private static readonly IReadOnlyDictionary<string, double> NoParams = new Dictionary<string, double>();

    private static Dictionary<string, double> Values()
    {
        double y0 = 4.6e12, lf = 5.3e6, u = 5, alpha = 0.35;
        double l = lf * (1 - u / 100), k = 4 * y0;
        double a = y0 / (Math.Pow(k, alpha) * Math.Pow(l, 1 - alpha));
        return new Dictionary<string, double>
        {
            [MacroModelV1.Ids.Gdp] = y0, [MacroModelV1.Ids.Capital] = k, [MacroModelV1.Ids.Productivity] = a,
            [MacroModelV1.Ids.LabourForce] = lf, [MacroModelV1.Ids.Unemployment] = u, [MacroModelV1.Ids.Inflation] = 3.5,
            [MacroModelV1.Ids.Debt] = 0.6 * y0, [MacroModelV1.Ids.InterestRate] = 5.0, [MacroModelV1.Ids.GdpNominal] = y0 * 1.1,
            [MacroModelV1.Ids.Revenue] = 0.4 * y0, [MacroModelV1.Ids.Spending] = 0.42 * y0, [MacroModelV1.Ids.DeficitPct] = 3, [MacroModelV1.Ids.RealGrowth] = 2,
        };
    }

    [Theory]
    [InlineData("base:capital")] [InlineData("base:inflation")] [InlineData("base:price_level")] [InlineData("base:interest_rate")]
    [InlineData("base:debt")] [InlineData("base:unemployment")] [InlineData("base:gdp")]
    public void EveryModelValueExplainsItselfWithFiniteNumbers(string id)
    {
        var e = ModelExplainer.Explain(id, Values(), NoParams);
        Assert.NotNull(e);
        Assert.NotEmpty(e!.Drivers);
        Assert.NotEmpty(e.Effects);
        Assert.All(e.Drivers.Concat(e.Effects), l => { Assert.True(double.IsFinite(l.Amount), l.Key); Assert.StartsWith("model." + id.Replace("base:", ""), l.Key); });
    }

    [Fact]
    public void InflationDriversAreTheModelsTerms()
    {
        var e = ModelExplainer.Explain("base:inflation", Values(), NoParams)!;
        double reversion = e.Drivers.Single(d => d.Key == "model.inflation.reversion").Amount;
        Assert.Equal(-0.5 * (3.5 - 2.0), reversion, 9);   // pulled back to the 2 % target
        Assert.Equal(0.1 * 3, e.Drivers.Single(d => d.Key == "model.inflation.deficit").Amount, 9);
    }

    [Fact]
    public void DebtNetIsPrimaryDeficitPlusInterest()
    {
        var e = ModelExplainer.Explain("base:debt", Values(), NoParams)!;
        double Get(string k) => e.Drivers.Single(d => d.Key == k).Amount;
        Assert.Equal(Get("model.debt.primary") + Get("model.debt.interest"), Get("model.debt.net"), 3);
        Assert.True(Get("model.debt.interest") > 0);
    }

    [Fact]
    public void ValuesTheModelDoesNotOwnHaveNoExplanation() =>
        Assert.Null(ModelExplainer.Explain("base:crime", Values(), NoParams));
}
