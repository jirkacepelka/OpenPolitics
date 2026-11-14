using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;

namespace OpenPolitics.Core.Tests.Economy;

public class IndexationTests
{
    private const double Dt = 0.25;
    private static readonly IReadOnlyDictionary<string, double> NoParams = new Dictionary<string, double>();
    private static readonly IReadOnlyDictionary<string, Rate> NoRates = new Dictionary<string, Rate>();
    private static readonly MacroModelV1 Model = new(new[]
    {
        new PolicyDef("t:tax", "percent", 0, 100, 40, null, 0, 0, MacroModelV1.Ids.Gdp, "f"),
        new PolicyDef("t:spend", "percent_of_gdp", 0, 100, 38.5, null, 1.0, 0, null, "f"),
    });
    private const double Y0 = 4.6e12;

    private static Dictionary<string, double> Initial(double price = 100)
    {
        double lf = 5.3e6, u = 5, alpha = 0.35;
        double l = lf * (1 - u / 100), k = 4 * Y0;
        double a = Y0 / (Math.Pow(k, alpha) * Math.Pow(l, 1 - alpha));
        return new Dictionary<string, double>
        {
            [MacroModelV1.Ids.Gdp] = Y0, [MacroModelV1.Ids.Capital] = k, [MacroModelV1.Ids.Productivity] = a,
            [MacroModelV1.Ids.LabourForce] = lf, [MacroModelV1.Ids.Unemployment] = u, [MacroModelV1.Ids.Inflation] = 2,
            [MacroModelV1.Ids.Debt] = 0.4 * Y0, [MacroModelV1.Ids.InterestRate] = 4.1,
            [MacroModelV1.Ids.Revenue] = 0.4 * Y0, [MacroModelV1.Ids.Spending] = 0.4 * Y0,
            [MacroModelV1.Ids.PriceLevel] = price,
        };
    }

    private static IReadOnlyDictionary<string, double> Step(IReadOnlyDictionary<string, IndexSetting>? ix, double price = 100) =>
        Model.Step(new EconomyInput(Initial(price), NoRates, new Dictionary<string, double>(), NoParams, ix), Dt);

    [Fact]
    public void GdpModeIsTheDefault()
    {
        var none = Step(null);
        var explicitGdp = Step(new Dictionary<string, IndexSetting>());
        Assert.Equal(none[MacroModelV1.Ids.Spending], explicitGdp[MacroModelV1.Ids.Spending]);
        Assert.Equal(0.385 * none[MacroModelV1.Ids.Gdp], none[MacroModelV1.Ids.Spending], 3);
    }

    [Fact]
    public void InflationModeKeepsTheRealAmount()
    {
        var r = Step(new Dictionary<string, IndexSetting> { ["t:spend"] = new("inflation", Y0 * 0.9, 100) }, price: 130);
        Assert.Equal(0.385 * Y0 * 0.9, r[MacroModelV1.Ids.Spending], 3); // independent of current GDP and prices
    }

    [Fact]
    public void NoneModeErodesWithPrices()
    {
        var r = Step(new Dictionary<string, IndexSetting> { ["t:spend"] = new("none", Y0, 100) }, price: 125);
        Assert.Equal(0.385 * Y0 * 100 / 125, r[MacroModelV1.Ids.Spending], 3);
    }

    private static PackSet Packs() => GameFixture.Load(f =>
    {
        f["values/macro.yaml"] += "- id: base:price_level\n  unit: index\n  initial: 100\n  scale: 10\n  owner: macro_v1\n";
        f["localization/cs.yaml"] += "name.base:price_level: cs-p\n";
        f["localization/en.yaml"] += "name.base:price_level: en-p\n";
    });

    [Fact]
    public void SetIndexationValidation()
    {
        var g = Simulation.NewGame(Packs(), "base:test", 1);
        Assert.Equal("error.unknown_policy", g.Apply(new SetIndexation("base:nope", "none")).ErrorKey);
        Assert.Equal("error.bad_indexation", g.Apply(new SetIndexation("base:spend", "weird")).ErrorKey);
        Assert.Equal("error.not_indexable", g.Apply(new SetIndexation("base:tax", "none")).ErrorKey);
        Assert.True(g.Apply(new SetIndexation("base:spend", "inflation")).Accepted);
        Assert.Equal("inflation", g.Indexation["base:spend"]);
        Assert.True(g.Apply(new SetIndexation("base:spend", "gdp")).Accepted);
        Assert.False(g.Indexation.ContainsKey("base:spend"));
    }

    private static double SpendShare(Game g)
    {
        var v = g.State.Countries.Values.Single().Values;
        return v["base:spending"] / v["base:gdp"];
    }

    [Fact]
    public void NoneModeLowersTheSpendingShareInAGame()
    {
        var packs = Packs();
        var gdp = Simulation.NewGame(packs, "base:test", 1);
        var none = Simulation.NewGame(packs, "base:test", 1);
        Assert.True(none.Apply(new SetIndexation("base:spend", "none")).Accepted);
        for (int i = 0; i < 12; i++) { gdp.EndTurn(); none.EndTurn(); }
        Assert.True(SpendShare(none) < 0.97 * SpendShare(gdp), $"{SpendShare(none)} vs {SpendShare(gdp)}");
    }

    [Fact]
    public void IndexationSurvivesSaveAndReplay()
    {
        var packs = Packs();
        var live = Simulation.NewGame(packs, "base:test", 5);
        for (int i = 0; i < 3; i++) live.EndTurn();
        Assert.True(live.Apply(new SetIndexation("base:spend", "none")).Accepted);
        for (int i = 0; i < 6; i++) live.EndTurn();
        var back = SaveData.FromYaml(live.Save().ToYaml());
        var loaded = Simulation.Load(packs, back);
        Assert.Equal(GameTests.Fingerprint(live), GameTests.Fingerprint(loaded));
        Assert.Equal("none", loaded.Indexation["base:spend"]);
    }
}
