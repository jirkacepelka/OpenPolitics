using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class LoyaltyTests
{
    private static Spec[] Parties(bool swingB = false) => new[]
    {
        new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 3, Tax: 31, Swing: swingB),
        new Spec("c", 0.22, Price: 3, Tax: 45), new Spec("z", 0.03),
    };

    private const string RedLineAt50 = "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 50}\n";

    /// <summary>The player (a) leads a coalition with b; agreed: b's d1 (spend >= 41, weight 3) and d2 (spend >= 42).</summary>
    private static Game Coalition(bool swingB = false, string? redLines = null, ulong seed = 1)
    {
        var g = New(Load(Parties(swingB), "a", interval: 60, redLines: redLines), seed);
        g.EndTurn();
        var r = g.Apply(new ProposeCoalition(new[] { "base:b" }, new[] { new AgreedDemand("base:b", "d1"), new AgreedDemand("base:b", "d2") }));
        Assert.True(r.Accepted, r.ErrorKey + " " + r.Detail);
        return g;
    }

    private static double Loyalty(Game g) => Pol(g).Loyalty!["base:b"];
    private static void Spend(Game g, double v) => Assert.True(g.Apply(new SetPolicy("base:spend", v)).Accepted);

    [Fact]
    public void LoyaltyStartsAt70ForPartnersNotTheLeader()
    {
        var g = Coalition();
        Assert.Equal(70.0, Loyalty(g));
        Assert.False(Pol(g).Loyalty!.ContainsKey("base:a"));
    }

    [Fact]
    public void DeliveringCommitmentsKeepsLoyaltyHigh()
    {
        var g = Coalition();
        Spend(g, 43);
        Run(g, 12);
        Assert.True(Loyalty(g) > 65, $"loyalty {Loyalty(g)}");
    }

    [Fact]
    public void IgnoringCommitmentsLetsLoyaltyDecay()
    {
        var g = Coalition();
        double previous = Loyalty(g);
        for (int i = 0; i < 6; i++)
        {
            g.EndTurn();
            Assert.True(Loyalty(g) < previous, $"turn {i}: {Loyalty(g)} !< {previous}");
            previous = Loyalty(g);
        }
        Assert.True(previous < 60);
    }

    [Fact]
    public void WarningFiresOnceBelow60ThenAgainAfterRecovery()
    {
        var g = Coalition();
        var reports = new List<TurnReport>();
        for (int i = 0; i < 3; i++) reports.Add(g.EndTurn());
        var first = Assert.Single(Events<LoyaltyWarning>(reports));
        Assert.Equal(("base:b", "d1"), (first.PartyId, first.DemandId));
        Spend(g, 43);
        for (int i = 0; i < 5; i++) reports.Add(g.EndTurn());
        Assert.True(Loyalty(g) > 65);
        Assert.Single(Events<LoyaltyWarning>(reports)); // no repeat while it stays low or recovers
        Spend(g, 40);
        for (int i = 0; i < 6; i++) reports.Add(g.EndTurn());
        Assert.Equal(2, Events<LoyaltyWarning>(reports).Count());
    }

    [Fact]
    public void UltimatumNamesTheMostLateDemandAndCountsDown()
    {
        var g = Coalition();
        TurnReport? hit = null;
        for (int i = 0; i < 12 && hit == null; i++)
        {
            var r = g.EndTurn();
            if (r.PoliticalEvents.OfType<Ultimatum>().Any()) hit = r;
        }
        var u = Assert.Single(hit!.PoliticalEvents.OfType<Ultimatum>());
        Assert.Equal(("base:b", "d1", 4), (u.PartyId, u.DemandId, u.TurnsLeft));
        g.EndTurn();
        Assert.Equal(3, Assert.Single(Pol(g).Ultimatums!).TurnsLeft);
    }

    [Fact]
    public void UltimatumResolvedByDelivery()
    {
        var g = Coalition();
        for (int i = 0; i < 12 && !(Pol(g).Ultimatums?.Count > 0); i++) g.EndTurn();
        Assert.NotEmpty(Pol(g).Ultimatums!);
        Spend(g, 43);
        var reports = Run(g, 2);
        Assert.Empty(Pol(g).Ultimatums!);
        Assert.Empty(Events<PartnerLeft>(reports));
    }

    [Fact]
    public void PartnerLeavesWhenUltimatumExpires()
    {
        var g = Coalition();
        var reports = new List<TurnReport>();
        for (int i = 0; i < 30 && !Events<PartnerLeft>(reports).Any(); i++) reports.Add(g.EndTurn());
        var left = Assert.Single(Events<PartnerLeft>(reports));
        Assert.Equal("base:b", left.PartyId);
        Assert.DoesNotContain("base:b", Pol(g).GovernmentParties);
        Assert.DoesNotContain(Pol(g).Government!.Commitments, c => c.PartyId == "base:b");
    }

    [Fact]
    public void RedLineCrossingCostsThirtyAtOnce()
    {
        var g = Coalition(redLines: RedLineAt50);
        Spend(g, 43);
        Run(g, 4);
        double before = Loyalty(g);
        Spend(g, 50);
        g.EndTurn();
        double drop = before - Loyalty(g);
        Assert.InRange(drop, 30, 37); // the shock of 30 plus the lower target of this turn
    }

    [Fact]
    public void PartnerLeavesAtLoyalty15()
    {
        var g = Coalition(redLines: RedLineAt50);
        Spend(g, 43); Run(g, 2);
        var reports = new List<TurnReport>();
        for (int i = 0; i < 4 && !Events<PartnerLeft>(reports).Any(); i++)
        {
            g.Apply(new SetPolicy("base:spend", 50)); reports.Add(g.EndTurn()); // crossing: -30
            g.Apply(new SetPolicy("base:spend", 43)); reports.Add(g.EndTurn()); // back under the line (refused once the partner has left)
        }
        Assert.Single(Events<PartnerLeft>(reports));
    }

    [Fact]
    public void PartnerWithoutCommitmentsDoesNotCrash()
    {
        var g = New(Load(Parties(), "a", government: new[] { "a", "b" }, leader: "a", turnsToElection: 40, interval: 60));
        Assert.Equal(70.0, Pol(g).Loyalty!["base:b"]);
        var reports = Run(g, 12);
        Assert.Empty(Events<PartnerLeft>(reports));
        Assert.InRange(Pol(g).Loyalty!["base:b"], 0, 100);
    }

    [Fact]
    public void SnapshotExposesLoyaltyCommitmentsAndUltimatums()
    {
        var g = Coalition();
        Spend(g, 43);
        g.EndTurn(); g.EndTurn();
        var p = Pol(g);
        Assert.Equal(2, p.Commitments!.Count);
        var d1 = p.Commitments.Single(c => c.DemandId == "d1");
        Assert.Equal(("base:b", 1.0), (d1.PartyId, d1.Progress));
        Assert.InRange(d1.Expected, 0.0, 1.0);
        Assert.NotNull(p.Ultimatums);
    }

    [Fact]
    public void SwingPartyReactsFaster()
    {
        double Decay(bool swing) { var g = Coalition(swingB: swing); Run(g, 2); return Loyalty(g); }
        Assert.True(Decay(true) < Decay(false));
    }
}
