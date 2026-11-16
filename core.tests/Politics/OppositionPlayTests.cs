using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>
/// Decision 0015 (2, 3): one "propose a policy" for every party outside the lead (a partner's goes to the leader, anyone else's goes public, the
/// voters react either way), and the player's support in each voter group is visible and moves with campaigns.
/// </summary>
public class OppositionPlayTests
{
    private static Spec[] World() => new[] { new Spec("a", 0.45, Tax: 40), new Spec("b", 0.25, Tax: 40), new Spec("c", 0.30, Tax: 30) };

    /// <summary>a leads with b; c is in opposition. The extra groups include bystanders who want spending at 45.</summary>
    private static Game Make(string player) =>
        New(Load(World(), player, government: new[] { "a", "b" }, leader: "a", turnsToElection: 40, interval: 40, extraGroups: true, mutate: GameFixture.FreePolicies));

    private static double Capital(Game g) => Pol(g).Party!.PoliticalCapital;

    [Fact]
    public void APartnersProposalGoesToTheLeaderAndMovesTheVoters()
    {
        var g = Make("b");
        Assert.Equal(Role.Partner, Pol(g).PlayerRole);
        double capital = Capital(g);
        Assert.True(g.Apply(new ProposePolicy("base:spend", 45)).Accepted);
        Assert.Equal(capital - PoliticsLimits.ProposeToLeaderCost, Capital(g), 9);
        Assert.True(Pol(g).Party!.Momentum["base:bystanders"] > 0);
        var r = g.EndTurn();
        Assert.True(r.PoliticalEvents.OfType<PolicyProposalAccepted>().Any() || r.PoliticalEvents.OfType<PolicyProposalRejected>().Any());
    }

    [Fact]
    public void AnOppositionProposalGoesPublic()
    {
        var g = Make("c");
        double capital = Capital(g);
        Assert.True(g.Apply(new ProposePolicy("base:spend", 45)).Accepted);
        Assert.Equal(capital - PoliticsLimits.AnnounceCost, Capital(g), 9);
        Assert.True(Pol(g).Party!.Momentum["base:bystanders"] > 0);
        var r = g.EndTurn();
        Assert.Empty(r.PoliticalEvents.OfType<PolicyProposalAccepted>());
        Assert.Empty(r.PoliticalEvents.OfType<PolicyProposalRejected>());
    }

    [Theory]
    [InlineData("b")]
    [InlineData("c")]
    public void OneProposalAQuarterForEveryRole(string player)
    {
        var g = Make(player);
        Assert.True(g.Apply(new ProposePolicy("base:spend", 45)).Accepted);
        Assert.Equal("error.announce_limit", g.Apply(new ProposePolicy("base:tax", 35)).ErrorKey);
        Assert.Equal("error.announce_limit", g.Apply(new AnnouncePolicy("base:tax", 35)).ErrorKey);
    }

    [Fact]
    public void TheOldAnnounceVerbIsAnAliasOfPropose()
    {
        var g = Make("b");
        Assert.True(g.Apply(new AnnouncePolicy("base:spend", 45)).Accepted);
        var r = g.EndTurn();
        Assert.True(r.PoliticalEvents.OfType<PolicyProposalAccepted>().Any() || r.PoliticalEvents.OfType<PolicyProposalRejected>().Any());
    }

    [Fact]
    public void ThePreviewSaysWhoDecidesWhatItCostsAndHowSupportMoves()
    {
        var partner = Make("b").PreviewProposal("base:spend", 45)!;
        Assert.True(partner.ToLeader);
        Assert.Equal(PoliticsLimits.ProposeToLeaderCost, partner.Cost);
        Assert.NotNull(partner.LeaderAgrees);
        Assert.True(partner.SupportPoints.GetValueOrDefault("base:bystanders") > 0);
        var opposition = Make("c").PreviewProposal("base:spend", 45)!;
        Assert.False(opposition.ToLeader);
        Assert.Equal(PoliticsLimits.AnnounceCost, opposition.Cost);
        Assert.Null(opposition.LeaderAgrees);
        Assert.Null(opposition.ErrorKey);
        Assert.Equal("error.not_for_leader", Make("a").PreviewProposal("base:spend", 45)!.ErrorKey);
    }

    [Fact]
    public void TheSnapshotShowsTheSupportOfEveryGroupForThePlayersParty()
    {
        var g = Make("c");
        var support = Pol(g).Party!.GroupSupport!;
        Assert.Equal(g.State.Countries.Values.Single().Satisfaction.Keys.OrderBy(k => k, StringComparer.Ordinal), support.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(support.Values, v => Assert.InRange(v, 0.0, 1.0));
        Assert.NotNull(Pol(g).Party!.GroupSupportPrev);
    }

    [Fact]
    public void ACampaignMovesItsGroupByWhatThePreviewSaid()
    {
        var g = Make("c");
        double before = Pol(g).Party!.GroupSupport!["base:bystanders"];
        double preview = g.PreviewCampaign("base:bystanders", 2);
        Assert.True(preview > 1.0, $"a full campaign should move its group by more than a point, got {preview}, share {before}");
        Assert.True(g.Apply(new Campaign("base:bystanders", 2)).Accepted);
        double after = Pol(g).Party!.GroupSupport!["base:bystanders"];
        Assert.Equal(preview, 100 * (after - before), 6);
    }

    [Fact]
    public void TheNewsReportsWhereSupportMoved()
    {
        var g = Make("c");
        Assert.True(g.Apply(new Campaign("base:bystanders", 2)).Accepted);
        var moved = Assert.Single(g.EndTurn().PoliticalEvents.OfType<SupportMoved>());
        var m = Assert.Single(moved.Moves, x => x.GroupId == "base:bystanders");
        Assert.True(m.After > m.Before);
    }
}
