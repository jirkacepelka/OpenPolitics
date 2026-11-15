using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class CoalitionCapitalTests
{
    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Tax: 30), new Spec("b", 0.33, Tax: 31, Price: 3), new Spec("c", 0.22, Tax: 45), new Spec("z", 0.03),
    };

    private static Game Make(string player, string[] gov, string? agreements = null) =>
        New(Load(Parties(), player, government: gov, leader: "a", turnsToElection: 60, interval: 60, agreements: agreements), 1);

    private const string Agree = "      - {party: base:b, demand: d1}\n      - {party: base:b, demand: d2}\n";

    private static double Income(Game g) => Pol(g).Party!.Income;

    [Fact]
    public void ALeaderWithLoyalPartnersEarnsMoreThanOneWithUnhappyPartners()
    {
        var loyal = Make("a", new[] { "a", "b" }, Agree);
        Assert.True(loyal.Apply(new SetPolicy("base:spend", 43)).Accepted); // delivers what b asked for
        var idle = Make("a", new[] { "a", "b" }, Agree);
        for (int i = 0; i < 6; i++) { loyal.EndTurn(); idle.EndTurn(); }
        Assert.True(Pol(loyal).Loyalty!["base:b"] > Pol(idle).Loyalty!["base:b"]);
        Assert.True(Income(loyal) > Income(idle) + 0.5, $"{Income(loyal)} vs {Income(idle)}");
    }

    [Fact]
    public void AnUnhappyCoalitionCostsCapital()
    {
        var g = Make("a", new[] { "a", "b" }, Agree);
        for (int i = 0; i < 20 && Pol(g).Loyalty!.GetValueOrDefault("base:b", 100) > 40 && Pol(g).Phase == Phase.Governing; i++) g.EndTurn();
        Assert.True(Pol(g).Loyalty!["base:b"] <= 40);
        // the leader's income without the coalition part; an unhappy partner takes some of it away
        double flat = PoliticsLimits.BaseIncome + PoliticsLimits.LeaderIncome + 8 * Pol(g).Party!.Organization * Pol(g).Support["base:a"];
        Assert.True(Income(g) < flat - 0.2, $"income {Income(g)} vs {flat}");
        Assert.InRange(Pol(g).Party!.PoliticalCapital, 0.0, 30.0);
    }

    [Fact]
    public void ALeaderWithoutPartnersGetsNoBonusAndNoError()
    {
        var g = Make("a", new[] { "a" });
        g.EndTurn();
        double flat = PoliticsLimits.BaseIncome + PoliticsLimits.LeaderIncome;
        Assert.InRange(Income(g), flat, flat + 8 * 0.2 + 1e-9);
    }

    [Fact]
    public void AGoodRelationWithTheLeaderPaysOffForAPartner()
    {
        var warm = Make("b", new[] { "a", "b" });
        Assert.True(warm.Apply(new Court("base:a")).Accepted);
        Assert.True(warm.Apply(new Court("base:a")).Accepted);
        var cold = Make("b", new[] { "a", "b" });
        warm.EndTurn(); cold.EndTurn();
        Assert.True(Income(warm) > Income(cold) + 0.5, $"{Income(warm)} vs {Income(cold)}");
        Assert.True(Pol(warm).Party!.PoliticalCapital >= 0);
    }

    [Fact]
    public void TheOppositionGetsNoBonus()
    {
        var g = Make("z", new[] { "a", "b" });
        g.EndTurn();
        Assert.InRange(Income(g), PoliticsLimits.BaseIncome, PoliticsLimits.BaseIncome + 8 * 0.2 * 0.5);
    }
}
