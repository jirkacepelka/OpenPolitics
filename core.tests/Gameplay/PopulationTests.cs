using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using Xunit;

namespace OpenPolitics.Core.Tests.Gameplay;

/// <summary>Decision 0021: laws in force, the moving population, income classes, demographic costs and private pensions.</summary>
public class PopulationTests
{
    private static readonly PackSet Packs = Content.BasePackTests.LoadBase().Packs!;
    private static Game Sandbox(ulong seed = 1) => Simulation.NewGame(Packs, "base:cz_2015_sandbox", seed);
    private static IReadOnlyDictionary<string, double> Values(Game g) => g.State.Countries.Values.Single().Values;
    private static void Run(Game g, int turns) { for (int i = 0; i < turns; i++) g.EndTurn(); }

    // ---- laws in force
    [Fact]
    public void ALawThatIsNotInForceCannotBeSetUntilItIsBroughtIn()
    {
        var g = Sandbox();
        Assert.False(g.IsActive("base:bank_levy"));
        Assert.Equal("error.policy_inactive", g.Apply(new SetPolicy("base:bank_levy", 0.2)).ErrorKey);
        Assert.True(g.Apply(new IntroducePolicy("base:bank_levy")).Accepted);
        Assert.True(g.IsActive("base:bank_levy"));
        Assert.Equal("error.already_active", g.Apply(new IntroducePolicy("base:bank_levy")).ErrorKey);
        Assert.True(g.Apply(new SetPolicy("base:bank_levy", 0.2)).Accepted);
    }

    [Fact]
    public void AnIntroducedLawStartsAtItsIntroductionSettingAfterItsDelay()
    {
        var g = Sandbox();
        Assert.True(g.Apply(new IntroducePolicy("base:rail_network")).Accepted);
        double start = g.IntroductionValue("base:rail_network");
        Assert.True(start > 0);
        Run(g, 8);
        Assert.Equal(start, g.State.Countries.Values.Single().Policies["base:rail_network"], 9);
    }

    [Fact]
    public void RepealingSendsALawBackToItsDefaultAndOffTheMap()
    {
        var g = Sandbox();
        Assert.True(g.IsActive("base:business_regulation"));
        Assert.Equal("error.not_abolishable", g.Apply(new AbolishPolicy("base:income_tax")).ErrorKey);
        Assert.True(g.Apply(new IntroducePolicy("base:rail_network")).Accepted);
        Run(g, 8);
        Assert.True(g.Apply(new AbolishPolicy("base:rail_network")).Accepted);
        Assert.False(g.IsActive("base:rail_network"));
        Run(g, 8);
        Assert.Equal(0.0, g.State.Countries.Values.Single().Policies["base:rail_network"], 9);
        Assert.Equal("error.not_active", g.Apply(new AbolishPolicy("base:rail_network")).ErrorKey);
    }

    [Fact]
    public void BringingALawInCostsPoliticalCapital()
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015", 1, "base:social_alliance");
        double cost = g.IntroductionCost("base:rail_network");
        Assert.True(cost >= 2);
        Assert.True(g.Apply(new IntroducePolicy("base:rail_network")).Accepted || g.Apply(new IntroducePolicy("base:rail_network")).ErrorKey == "error.policy_cost");
    }

    [Fact]
    public void LawsInForceAreSavedAndReplayed()
    {
        var g = Sandbox(3);
        Assert.True(g.Apply(new IntroducePolicy("base:rail_network")).Accepted);
        Run(g, 3);
        Assert.True(g.Apply(new AbolishPolicy("base:rail_network")).Accepted);
        Assert.True(g.Apply(new IntroducePolicy("base:sugar_tax")).Accepted);
        Run(g, 4);
        var loaded = Simulation.Load(Packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(g.ActivePolicies.OrderBy(x => x), loaded.ActivePolicies.OrderBy(x => x));
        Assert.Equal(Values(g)["base:gdp"], Values(loaded)["base:gdp"], 3);
    }

    // ---- population
    [Fact]
    public void TheStartingPopulationIsConsistent()
    {
        var v = Values(Sandbox());
        Assert.Equal(1.0, v["base:share_children"] + v["base:share_young"] + v["base:share_working"] + v["base:share_pensioners"], 9);
        Assert.Equal(1.0, new[] { "base:class_poor", "base:class_low", "base:class_middle", "base:class_upper", "base:class_high" }.Sum(id => v[id]), 9);
    }

    [Fact]
    public void ThePopulationAgesAndTheClassesStayStableInQuietTimes()
    {
        var g = Sandbox();
        var start = Values(g);
        Run(g, 80);
        var later = Values(g);
        Assert.True(later["base:share_pensioners"] > start["base:share_pensioners"] + 0.01, "a country that does nothing ages");
        Assert.InRange(later["base:class_middle"], start["base:class_middle"] - 0.04, start["base:class_middle"] + 0.04);
        Assert.Equal(1.0, new[] { "base:class_poor", "base:class_low", "base:class_middle", "base:class_upper", "base:class_high" }.Sum(id => later[id]), 6);
    }

    [Fact]
    public void GroupSizesFollowThePopulation()
    {
        var g = Sandbox();
        double before = g.Groups.First(x => x.Id == "base:pensioners").Size;
        Run(g, 80);
        Assert.True(g.Groups.First(x => x.Id == "base:pensioners").Size > before);
        Assert.Equal(Packs.VoterGroups.Count, g.Groups.Count);
        Assert.True(g.Groups.Count >= 12);
    }

    [Fact]
    public void ALaterRetirementAgeLowersTheNumberOfPensionersForGood()
    {
        var control = Sandbox(); var reform = Sandbox();
        Assert.True(reform.Apply(new SetPolicy("base:retirement_age", 67)).Accepted);
        Run(control, 160); Run(reform, 160);
        Assert.True(Values(reform)["base:share_pensioners"] < Values(control)["base:share_pensioners"] - 0.02);
        Assert.True(Values(reform)["base:share_working"] > Values(control)["base:share_working"]);
    }

    [Fact]
    public void FamilySupportRaisesBirthsAndTheChildrenSchoolsAndFamiliesPayForFirst()
    {
        var control = Sandbox(); var support = Sandbox();
        Assert.True(support.Apply(new IntroducePolicy("base:family_allowances")).Accepted);
        Assert.True(support.Apply(new SetPolicy("base:family_allowances", 2)).Accepted);
        Run(control, 60); Run(support, 60);
        Assert.True(Values(support)["base:fertility"] > Values(control)["base:fertility"] + 0.1, $"fertility {Values(support)["base:fertility"]} vs {Values(control)["base:fertility"]}");
        Assert.True(Values(support)["base:share_children"] > Values(control)["base:share_children"], $"children {Values(support)["base:share_children"]} vs {Values(control)["base:share_children"]}");
        Assert.True(Values(support)["base:student_load"] > Values(control)["base:student_load"], "student load");
        // more children, more school to pay for, before they work: the share of people aged 30 and over who work is not yet larger
        Assert.True(Values(support)["base:share_working"] <= Values(control)["base:share_working"] * 1.002, "the extra children are not workers yet");
    }

    [Fact]
    public void MigrationRaisesPopulationAndWorkers()
    {
        var control = Sandbox(); var open = Sandbox();
        Assert.True(open.Apply(new SetPolicy("base:migration", 8)).Accepted);
        Run(control, 80); Run(open, 80);
        Assert.True(Values(open)["base:population"] > Values(control)["base:population"] * 1.02);
        Assert.True(Values(open)["base:labour_force"] > Values(control)["base:labour_force"]);
    }

    [Fact]
    public void GoodTimesLiftPoorHouseholdsIntoTheMiddleClass()
    {
        var control = Sandbox(); var boost = Sandbox();
        foreach (var law in new[] { "base:vocational_training", "base:scholarships" })
        {
            Assert.True(boost.Apply(new IntroducePolicy(law)).Accepted);
            Assert.True(boost.Apply(new SetPolicy(law, 1.0)).Accepted);
        }
        Run(control, 80); Run(boost, 80);
        Assert.True(Values(boost)["base:mobility_up"] > Values(control)["base:mobility_up"]);
        Assert.True(Values(boost)["base:class_poor"] < Values(control)["base:class_poor"]);
    }

    [Fact]
    public void ABadLabourMarketPushesHouseholdsDown()
    {
        // the down rate rises with unemployment: feed the model a high-unemployment state directly
        var model = new DemographyModelV1();
        var v = Packs.Values.ToDictionary(x => x.Id, x => x.Initial);
        v["base:mobility_down"] = 8; v["base:mobility_up"] = 2;
        var next = model.Step(new EconomyInput(v, new Dictionary<string, OpenPolitics.Core.Graph.Rate>(), new Dictionary<string, double>(), new Dictionary<string, double>()), 0.25);
        Assert.True(next["base:class_poor"] > v["base:class_poor"]);
        Assert.True(next["base:class_high"] < v["base:class_high"]);
    }

    // ---- what the population costs
    [Fact]
    public void ASpendingPolicyCostsMoreWhenItsPopulationGrows()
    {
        var policies = Packs.Policies.Where(p => p.Id == "base:education_spending").ToList();
        var model = new MacroModelV1(policies, Packs.Values);
        var v = Packs.Values.ToDictionary(x => x.Id, x => x.Initial);
        EconomyInput In(double load) { var c = new Dictionary<string, double>(v) { ["base:student_load"] = load }; return new EconomyInput(c, new Dictionary<string, OpenPolitics.Core.Graph.Rate>(), new Dictionary<string, double>(), new Dictionary<string, double>()); }
        double normal = model.Step(In(v["base:student_load"]), 0.25)["base:spending"];
        double crowded = model.Step(In(v["base:student_load"] * 1.2), 0.25)["base:spending"];
        Assert.True(crowded > normal * 1.05);
    }

    // ---- prices and private pensions
    [Fact]
    public void ATaxRaisesPricesOnceAndInflationSettles()
    {
        var control = Sandbox(); var vat = Sandbox();
        Assert.True(vat.Apply(new SetPolicy("base:vat", 16)).Accepted);
        double peak = 0;
        for (int i = 1; i <= 16; i++)
        {
            control.EndTurn(); vat.EndTurn();
            double diff = Values(vat)["base:inflation"] - Values(control)["base:inflation"];
            peak = Math.Max(peak, diff);
            if (i >= 10) Assert.InRange(diff, -1.5, 1.5);
        }
        Assert.True(peak > 3, $"the jump shows in inflation ({peak:0.0} points)");
        Assert.True(Values(vat)["base:price_level"] > Values(control)["base:price_level"] + 2);
    }

    [Fact]
    public void PeopleSaveMoreWhenStatePensionsAreSmallAndTheRetirementAgeIsHigh()
    {
        var model = new PensionModelV1();
        var v = Packs.Values.ToDictionary(x => x.Id, x => x.Initial);
        double Next(double pensions, double age, double relief = 0)
        {
            var p = new Dictionary<string, double> { ["base:pensions"] = pensions, ["base:retirement_age"] = age, ["base:pension_tax_relief"] = relief };
            return model.Step(new EconomyInput(v, new Dictionary<string, OpenPolitics.Core.Graph.Rate>(), p, new Dictionary<string, double>()), 0.25)["base:pension_participation"];
        }
        double baseline = Next(9, 63);
        Assert.True(Next(6, 63) > baseline);
        Assert.True(Next(9, 67) > baseline);
        Assert.True(Next(9, 63, 0.4) > baseline);
    }

    [Fact]
    public void PrivatePensionFundsGrowAndLowerTheStatePensionBurden()
    {
        var control = Sandbox(); var funded = Sandbox();
        Assert.True(funded.Apply(new IntroducePolicy("base:pension_subsidy")).Accepted);
        Assert.True(funded.Apply(new SetPolicy("base:pension_subsidy", 0.8)).Accepted);
        Run(control, 160); Run(funded, 160);
        Assert.True(Values(funded)["base:pension_fund"] > Values(control)["base:pension_fund"] * 2);
        Assert.True(Values(funded)["base:pension_funded_share"] > Values(control)["base:pension_funded_share"]);
        Assert.True(Values(funded)["base:pension_burden"] < Values(control)["base:pension_burden"]);
    }
}
