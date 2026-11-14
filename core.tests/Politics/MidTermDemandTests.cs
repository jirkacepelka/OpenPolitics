using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class MidTermDemandTests
{
    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Tax: 30), new Spec("b", 0.33, Tax: 31, Price: 0), new Spec("c", 0.22, Tax: 45), new Spec("z", 0.03),
    };

    /// <summary>a (the player) leads b. A large unhappy group makes both governing parties lose votes, so b sits below its election result.</summary>
    private static Game Make(ulong seed = 1, string? agreements = null) =>
        New(Load(Parties(), "a", government: new[] { "a", "b" }, leader: "a", turnsToElection: 60, interval: 60, grumpy: true, agreements: agreements), seed);

    private static IEnumerable<T> Ev<T>(List<TurnReport> rs) where T : PoliticalEvent => Events<T>(rs);

    [Fact]
    public void AnUnhappyPartnerRaisesExactlyOneDemandAtATime()
    {
        var g = Make();
        var reports = new List<TurnReport>();
        while (!Ev<DemandRaised>(reports).Any() && reports.Count < 20) reports.Add(g.EndTurn());
        var first = Assert.Single(Ev<DemandRaised>(reports));
        Assert.Equal("base:b", first.PartyId);
        Assert.Equal(6, first.DueTurns);
        Assert.Contains(Pol(g).Commitments!, c => c.PartyId == "base:b" && c.DemandId == first.DemandId);
        // nothing else is raised while this one is open
        var open = Run(g, 5);
        Assert.Empty(Ev<DemandRaised>(open));
        Assert.Empty(Ev<PartnerLeft>(open));
    }

    [Fact]
    public void ADeliveredDemandPaysOffInLoyalty()
    {
        var g = Make();
        var reports = new List<TurnReport>();
        while (!Ev<DemandRaised>(reports).Any() && reports.Count < 20) reports.Add(g.EndTurn());
        Assert.NotEmpty(Ev<DemandRaised>(reports));
        double before = Pol(g).Loyalty!["base:b"];
        Assert.True(g.Apply(new SetPolicy("base:spend", 43)).Accepted); // satisfies d1..d3 (spend >= 41..43)
        var next = new List<TurnReport> { g.EndTurn(), g.EndTurn() };
        Assert.Single(Ev<DemandMet>(next));
        Assert.True(Pol(g).Loyalty!["base:b"] > before, $"{Pol(g).Loyalty!["base:b"]} vs {before}");
    }

    [Fact]
    public void AnIgnoredDemandCostsLoyaltyAtTheDeadline()
    {
        var g = Make();
        var reports = new List<TurnReport>();
        while (!Ev<DemandRaised>(reports).Any() && reports.Count < 20) reports.Add(g.EndTurn());
        int raisedAt = Ev<DemandRaised>(reports).First().Turn;
        double last = Pol(g).Loyalty!["base:b"];
        var later = new List<TurnReport>();
        double beforeDeadline = last;
        for (int i = 0; i < 6 && !Ev<DemandFailed>(later).Any(); i++)
        {
            beforeDeadline = Pol(g).Loyalty!.GetValueOrDefault("base:b", 0);
            later.Add(g.EndTurn());
        }
        var failed = Assert.Single(Ev<DemandFailed>(later));
        Assert.Equal(raisedAt + 6, failed.Turn);
        Assert.True(Pol(g).Loyalty!.GetValueOrDefault("base:b", 0) <= beforeDeadline - 20);
    }

    [Fact]
    public void SatisfiedOrAgreedDemandsAreNeverRaised()
    {
        var g = Make();
        Assert.True(g.Apply(new SetPolicy("base:spend", 43)).Accepted);
        Assert.Empty(Ev<DemandRaised>(Run(g, 40)));
    }

    [Fact]
    public void AnAgreedDemandIsNotRaisedAgain()
    {
        var g = Make(agreements: "      - {party: base:b, demand: d1}\n");
        var reports = Run(g, 10);
        Assert.DoesNotContain(Ev<DemandRaised>(reports), r => r.DemandId == "d1");
    }
}
