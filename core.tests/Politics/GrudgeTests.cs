using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class GrudgeTests
{
    [Fact]
    public void StartLoyaltyDependsOnRelationAndGrudge()
    {
        Assert.Equal(70.0, LoyaltyModel.StartLoyalty(70, 0.3, 0), 9);
        Assert.Equal(85.0, LoyaltyModel.StartLoyalty(70, 1.0, 0), 9);   // 90 clamped
        Assert.Equal(40.0, LoyaltyModel.StartLoyalty(70, 0.3, 1.0), 9);
        Assert.Equal(20.0, LoyaltyModel.StartLoyalty(70, 0.0, 3.0), 9); // clamped from below
    }

    private static CoalitionProposal Proposal => new("a", new[] { "b" }, Array.Empty<AgreedDemand>());

    private static IReadOnlyList<Refusal> Eval(Func<string, string, double> grudge) => CoalitionRules.Evaluate(
        Proposal, CoalitionRulesTests.Parties(CoalitionRulesTests.P("a"), CoalitionRulesTests.P("b", price: 2,
            demands: new[] { CoalitionRulesTests.D("d1", "t:tax", ">=", 25, 2), CoalitionRulesTests.D("d2", "t:tax", ">=", 26, 2) })),
        CoalitionRulesTests.Seats(("a", 60), ("b", 60)), 0.5, 6, CoalitionRulesTests.Rel, grudge);

    [Fact]
    public void GrudgeAboveHalfRefuses()
    {
        var r = Eval((x, y) => x == "b" && y == "a" ? 0.5 : 0.0);
        Assert.Contains(r, f => f.Reason == "grudge" && f.PartyId == "b");
        Assert.DoesNotContain(Eval((x, y) => 0.49), f => f.Reason == "grudge");
    }

    [Fact]
    public void GrudgeRaisesPrice()
    {
        // price 2 -> needs weight 1.7 at relation 0.3; agreements of weight 2 are enough without grudge...
        var agreed = new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "d1") });
        var parties = CoalitionRulesTests.Parties(CoalitionRulesTests.P("a"), CoalitionRulesTests.P("b", price: 2,
            demands: new[] { CoalitionRulesTests.D("d1", "t:tax", ">=", 25, 2) }));
        var seats = CoalitionRulesTests.Seats(("a", 60), ("b", 60));
        Assert.DoesNotContain(CoalitionRules.Evaluate(agreed, parties, seats, 0.5, 6, CoalitionRulesTests.Rel, (x, y) => 0.0), f => f.Reason == "price");
        // ...but with a grudge of 0.4 the price is 1.7 * 1.4 = 2.38
        Assert.Contains(CoalitionRules.Evaluate(agreed, parties, seats, 0.5, 6, CoalitionRulesTests.Rel, (x, y) => x == "b" ? 0.4 : 0.0), f => f.Reason == "price");
    }

    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 3, Tax: 31),
        new Spec("c", 0.22, Price: 3, Tax: 45), new Spec("z", 0.03),
    };

    private static AgreedDemand Ag(string party, string demand) => new("base:" + party, demand);

    private static Game AfterBLeft()
    {
        var g = New(Load(Parties(), "a", interval: 60, maxFormation: 12), 1);
        g.EndTurn();
        Assert.True(g.Apply(new ProposeCoalition(new[] { "base:b" }, new[] { Ag("b", "d1"), Ag("b", "d2") })).Accepted);
        for (int i = 0; i < 30 && Pol(g).Phase == Phase.Governing; i++) g.EndTurn();
        Assert.Equal(Phase.Formation, Pol(g).Phase);
        return g;
    }

    [Fact]
    public void ReformingTheSameCoalitionRightAfterTheFallIsRefused()
    {
        var g = AfterBLeft();
        double grudge = Pol(g).Grudge!["base:b|base:a"];
        Assert.True(grudge >= 0.5, $"grudge {grudge}");
        var r = g.Apply(new ProposeCoalition(new[] { "base:b" }, new[] { Ag("b", "d1"), Ag("b", "d2") }));
        Assert.False(r.Accepted);
        Assert.Equal("error.refused.grudge", r.ErrorKey);
    }

    [Fact]
    public void LaterTheSameCoalitionStartsWithLowerLoyalty()
    {
        var g = AfterBLeft();
        for (int i = 0; i < 6; i++) g.EndTurn(); // grudge halves roughly every 8 turns
        Assert.True(Pol(g).Phase == Phase.Formation && Pol(g).Formation!.PlayerIsFormateur);
        var r = g.Apply(new ProposeCoalition(new[] { "base:b" }, new[] { Ag("b", "d1"), Ag("b", "d2"), Ag("b", "d3") }));
        Assert.True(r.Accepted, r.ErrorKey);
        g.EndTurn();
        Assert.True(Pol(g).Loyalty!["base:b"] < 70.0, $"loyalty {Pol(g).Loyalty!["base:b"]}");
    }

    [Fact]
    public void GrudgeDecays()
    {
        var g = AfterBLeft();
        double first = Pol(g).Grudge!["base:b|base:a"];
        for (int i = 0; i < 16; i++) g.EndTurn();
        Assert.True(Pol(g).Grudge!.GetValueOrDefault("base:b|base:a") <= 0.25 * first);
    }
}
