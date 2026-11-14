using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class FallTests
{
    private const string RedLineAt50 = "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 50}\n";

    private static Spec[] Parties(string cPolicy = "base:spend") => new[]
    {
        new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 3, Tax: 31),
        new Spec("c", 0.22, Price: 3, Tax: 45, DemandPolicy: cPolicy), new Spec("z", 0.03),
    };

    private static AgreedDemand Ag(string party, string demand) => new("base:" + party, demand);

    /// <summary>Player a leads a coalition with b (demands on spend); nobody delivers.</summary>
    private static Game WithB(int interval = 60, ulong seed = 1)
    {
        var g = New(Load(Parties(), "a", interval: interval), seed);
        g.EndTurn();
        Assert.True(g.Apply(new ProposeCoalition(new[] { "base:b" }, new[] { Ag("b", "d1"), Ag("b", "d2") })).Accepted);
        return g;
    }

    [Fact]
    public void PartnerLeavingWithMajorityIntactDoesNotCauseAFall()
    {
        // b's demand is on spend (delivered), c's on tax (ignored): c leaves, a + b keep the majority.
        var g = New(Load(Parties(cPolicy: "base:tax"), "a", interval: 60));
        g.EndTurn();
        Assert.True(g.Apply(new ProposeCoalition(new[] { "base:b", "base:c" }, new[] { Ag("b", "d1"), Ag("c", "d1") })).Accepted);
        Assert.True(g.Apply(new SetPolicy("base:spend", 43)).Accepted);
        var reports = Run(g, 30);
        Assert.Contains(Events<PartnerLeft>(reports), e => e.PartyId == "base:c");
        Assert.Empty(Events<GovernmentFell>(reports));
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        Assert.Contains("base:b", Pol(g).GovernmentParties);
    }

    [Fact]
    public void PartnerLeavingWithoutMajorityCausesAFallAndFormationStarts()
    {
        var g = WithB();
        var reports = new List<TurnReport>();
        for (int i = 0; i < 30 && !Events<GovernmentFell>(reports).Any(); i++) reports.Add(g.EndTurn());
        var fell = reports.Last();
        Assert.Single(fell.PoliticalEvents.OfType<GovernmentFell>());
        Assert.Equal("base:a", Assert.Single(fell.PoliticalEvents.OfType<FormationStarted>()).Formateur); // the leader tries first
        var p = Pol(g);
        Assert.Equal(Phase.Formation, p.Phase);
        Assert.True(p.Formation!.PlayerIsFormateur);
        Assert.Equal("error.no_majority", g.Apply(new SetPolicy("base:spend", 41)).ErrorKey);
    }

    [Fact]
    public void TwoPartnersLeavingInOneTurnIsOneFall()
    {
        var g = New(Load(Parties(), "a", government: new[] { "a", "b", "c" }, leader: "a", turnsToElection: 60, interval: 60, redLines: RedLineAt50));
        var reports = new List<TurnReport>();
        for (int i = 0; i < 12 && !Events<PartnerLeft>(reports).Any(); i++)
        {
            g.Apply(new SetPolicy("base:spend", 50)); reports.Add(g.EndTurn());
            if (Events<PartnerLeft>(reports).Any()) break;
            g.Apply(new SetPolicy("base:spend", 40)); reports.Add(g.EndTurn());
        }
        var turn = reports.First(r => r.PoliticalEvents.OfType<PartnerLeft>().Any());
        Assert.Equal(2, turn.PoliticalEvents.OfType<PartnerLeft>().Count());
        Assert.Single(turn.PoliticalEvents.OfType<GovernmentFell>());
        Assert.Single(turn.PoliticalEvents.OfType<FormationStarted>());
    }

    [Fact]
    public void PlayerLeaderCanRebuildWithAnotherPartnerAtFullStartLoyalty()
    {
        var g = WithB();
        for (int i = 0; i < 30 && Pol(g).Phase == Phase.Governing; i++) g.EndTurn();
        Assert.Equal(Phase.Formation, Pol(g).Phase);
        // the leaver is refused for now (grudge, see GrudgeTests); another party can be invited at its price
        var r = g.Apply(new ProposeCoalition(new[] { "base:c" }, new[] { Ag("c", "d1"), Ag("c", "d2") }));
        Assert.True(r.Accepted, r.ErrorKey);
        var p = Pol(g);
        Assert.Equal((Phase.Governing, 70.0), (p.Phase, p.Loyalty!["base:c"]));
        Assert.Single(Events<GovernmentFormed>(new[] { g.EndTurn() }));
    }

    [Fact]
    public void FailedRebuildEndsInAGovernmentOrEarlyElections()
    {
        var g = WithB();
        var reports = new List<TurnReport>();
        for (int i = 0; i < 30 && !Events<GovernmentFell>(reports).Any(); i++) reports.Add(g.EndTurn());
        int fallAt = reports.Count;
        reports.AddRange(Run(g, 20)); // the player does nothing
        var after = reports.Skip(fallAt).ToList();
        Assert.True(Events<GovernmentFormed>(after).Any() || Events<EarlyElectionsCalled>(after).Any());
    }

    [Fact]
    public void FallsNeverDoubleStartAFormationRound()
    {
        // Elections every 8 turns plus falls: every FormationStarted is caused by exactly one election or fall.
        var g = WithB(interval: 8, seed: 3);
        var reports = Run(g, 60);
        int started = Events<FormationStarted>(reports).Count();
        int caused = Events<ElectionHeld>(reports).Count() + Events<GovernmentFell>(reports).Count();
        Assert.Equal(caused, started);
        foreach (var r in reports)
            Assert.True(r.PoliticalEvents.OfType<FormationStarted>().Count() <= 1);
    }
}
