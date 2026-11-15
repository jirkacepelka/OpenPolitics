using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Changing a policy costs the leader political capital (decision 0013).</summary>
public class PolicyCapitalCostTests
{
    private static readonly PackSet Packs = BasePackTests.LoadBase().Packs!;
    private const string Scenario = "base:cz_2015";

    private static CountryPolitics Pol(Game g) => g.State.Countries.Values.Single().Politics!;
    private static double Capital(Game g) => Pol(g).Party!.PoliticalCapital;
    private static PolicyDef Def(string id) => Packs.Policies.Single(p => p.Id == id);
    private static double Now(Game g, string id) => g.State.Countries.Values.Single().Policies[id];

    private static PolicyDef Lever(double weight) => new("t:x", "", 0, 100, 50, null, 0, 0, null, "t", CapitalCost: weight);

    [Fact]
    public void CostGrowsWithTheSizeOfTheMoveAndTheWeight()
    {
        Assert.Equal(0, PolicyCost.For(Lever(1), 50, 50));
        Assert.Equal(1, PolicyCost.For(Lever(1), 50, 50.1)); // a tiny move still costs one point
        Assert.Equal(3, PolicyCost.For(Lever(1), 50, 60));   // 10 % of the range
        Assert.Equal(3, PolicyCost.For(Lever(1), 60, 50));   // lowering costs the same as raising
        Assert.Equal(11, PolicyCost.For(Lever(1), 0, 50));
        Assert.Equal(22, PolicyCost.For(Lever(2), 0, 50));
        Assert.Equal(0, PolicyCost.For(Lever(0), 0, 100));   // a free lever
        Assert.True(PolicyCost.For(Lever(1.5), 40, 60) > PolicyCost.For(Lever(1), 40, 60));
    }

    [Theory]
    [InlineData("  capital_cost: 2.5\n", 2.5)]
    [InlineData("", 1.0)]
    public void PacksReadCapitalCostWithADefaultOfOne(string extra, double expected)
    {
        var r = OpenPolitics.Core.Tests.Packs.TestPacks.Load(f => f["policies/p.yaml"] += extra);
        Assert.True(r.Ok, string.Join("\n", r.Diagnostics));
        Assert.Equal(expected, r.Packs!.Policies.Single().CapitalCost);
    }

    [Theory]
    [InlineData("  capital_cost: -1\n")]
    [InlineData("  capital_cost: 11\n")]
    public void CapitalCostOutsideZeroToTenIsE007(string extra)
    {
        var r = OpenPolitics.Core.Tests.Packs.TestPacks.Load(f => f["policies/p.yaml"] += extra);
        Assert.Contains(r.Diagnostics, d => d.Code == "E007" && d.Message.Contains("capital_cost"));
    }

    [Fact]
    public void HeavyPoliciesWeighMoreThanAddOns()
    {
        Assert.Equal(1.0, Lever(1).CapitalCost);
        foreach (var heavy in new[] { "base:pensions", "base:income_tax", "base:retirement_age", "base:electoral_threshold", "base:electoral_method", "base:migration" })
            Assert.True(Def(heavy).CapitalCost > 1.0, heavy);
        Assert.True(Def("base:culture_sport").CapitalCost < 1.0);
        // one step of the electoral method must be affordable with full capital
        Assert.True(PolicyCost.For(Def("base:electoral_method"), 0, 1) <= PoliticsLimits.MaxCapital);
    }

    [Fact]
    public void TheLeaderPaysForAPolicyChangeWhenItIsAccepted()
    {
        var g = Simulation.NewGame(Packs, Scenario, 1);
        double before = Capital(g), target = Now(g, "base:research") + 0.3;
        double cost = g.PolicyChangeCost("base:research", target);
        Assert.True(cost >= 1);
        Assert.True(g.Apply(new SetPolicy("base:research", target)).Accepted);
        Assert.Equal(before - cost, Capital(g), 9);
    }

    [Fact]
    public void AnUnaffordableChangeIsRefusedAndChangesNothing()
    {
        var g = Simulation.NewGame(Packs, Scenario, 1);
        var p = Def("base:pensions");
        double target = Now(g, p.Id) > (p.Min + p.Max) / 2 ? p.Min : p.Max;
        double cost = g.PolicyChangeCost(p.Id, target);
        Assert.True(cost > Capital(g));
        double before = Capital(g);
        var r = g.Apply(new SetPolicy(p.Id, target));
        Assert.False(r.Accepted);
        Assert.Equal("error.policy_cost", r.ErrorKey);
        Assert.Equal($"{cost:0}|{Math.Floor(before):0}", r.Detail);
        Assert.Equal(before, Capital(g));
        Assert.Empty(g.PendingPolicies);
    }

    [Fact]
    public void CostIsMeasuredFromTheQueuedValueAndCancellingIsFreeWithoutRefund()
    {
        var g = Simulation.NewGame(Packs, Scenario, 1);
        string id = "base:pensions"; // delay of several quarters, so the change stays queued
        Assert.True(Def(id).DelayTurns >= 1);
        double now = Now(g, id), step = (Def(id).Max - Def(id).Min) * 0.05;
        Assert.True(g.Apply(new SetPolicy(id, now + step)).Accepted);
        double afterFirst = Capital(g);
        Assert.Equal(g.PolicyChangeCost(id, now + 2 * step), PolicyCost.For(Def(id), now + step, now + 2 * step));
        Assert.Equal(0, g.PolicyChangeCost(id, now)); // back to the value in force: cancelling
        Assert.True(g.Apply(new SetPolicy(id, now)).Accepted);
        Assert.Equal(afterFirst, Capital(g)); // free, and nothing comes back
    }

    [Fact]
    public void IndexationIsFree()
    {
        var g = Simulation.NewGame(Packs, Scenario, 1);
        double before = Capital(g);
        Assert.True(g.Apply(new SetIndexation("base:pensions", "inflation")).Accepted);
        Assert.Equal(before, Capital(g));
    }

    [Fact]
    public void WithoutPoliticsPolicyChangesAreFree()
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015_sandbox", 1);
        Assert.Equal(0, g.PolicyChangeCost("base:pensions", Def("base:pensions").Max));
        Assert.Null(g.AffordableRange("base:pensions"));
        Assert.True(g.Apply(new SetPolicy("base:pensions", Def("base:pensions").Max)).Accepted);
    }

    [Fact]
    public void PreviewShowsTheCostAndWhetherItIsAffordable()
    {
        var g = Simulation.NewGame(Packs, Scenario, 1);
        var p = Def("base:pensions");
        var small = g.PreviewPolicy(p.Id, Now(g, p.Id) + 0.1)!;
        Assert.Equal(g.PolicyChangeCost(p.Id, Now(g, p.Id) + 0.1), small.CapitalCost);
        Assert.True(small.Affordable);
        var huge = g.PreviewPolicy(p.Id, Now(g, p.Id) > (p.Min + p.Max) / 2 ? p.Min : p.Max)!;
        Assert.False(huge.Affordable);
    }

    [Fact]
    public void AffordableRangeIsExactlyWhatTheCapitalPays()
    {
        var g = Simulation.NewGame(Packs, Scenario, 1);
        foreach (var id in new[] { "base:pensions", "base:research", "base:income_tax", "base:electoral_method" })
        {
            var p = Def(id);
            var (lo, hi) = g.AffordableRange(id)!.Value;
            double now = Now(g, id), capital = Capital(g), eps = (p.Max - p.Min) * 1e-4;
            Assert.True(lo <= now && now <= hi, id);
            Assert.True(g.PolicyChangeCost(id, lo) <= capital && g.PolicyChangeCost(id, hi) <= capital, id);
            if (lo - eps >= p.Min) Assert.True(g.PolicyChangeCost(id, lo - eps) > capital, id);
            if (hi + eps <= p.Max) Assert.True(g.PolicyChangeCost(id, hi + eps) > capital, id);
        }
    }

    [Fact]
    public void ANonLeaderStillGetsTheRoleErrorFirst()
    {
        var g = Simulation.NewGame(Packs, Scenario, 1, "base:civic_right");
        Assert.Equal("error.not_leader", g.Apply(new SetPolicy("base:research", Now(g, "base:research") + 0.1)).ErrorKey);
    }

    [Fact]
    public void EveryPartyEarnsMoreAndGoverningPaysLessExtra()
    {
        // base income for every party is PoliticsLimits.BaseIncome; the leader's flat bonus is smaller than the base and than before (2)
        Assert.True(PoliticsLimits.BaseIncome >= 3.0);
        Assert.True(PoliticsLimits.LeaderIncome < 2.0 && PoliticsLimits.LeaderIncome > 0);
        Assert.True(PoliticsLimits.CoalitionIncome < 3.0);
        var g = Simulation.NewGame(Packs, Scenario, 1, "base:civic_right");
        g.EndTurn();
        var ps = Pol(g).Party!;
        double share = Pol(g).Support["base:civic_right"];
        Assert.Equal(PoliticsLimits.BaseIncome + 8.0 * ps.Organization * share, ps.Income, 1);
    }
}
