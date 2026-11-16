using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0014 (g): any party that does not lead can announce a policy proposal; voters react, an AI government may adopt it.</summary>
public class AnnouncePolicyTests
{
    private static Spec[] World(double playerTax = 30) =>
        new[] { new Spec("a", 0.45, Tax: 40), new Spec("b", 0.25, Tax: 40), new Spec("c", 0.30, Tax: playerTax) };

    private static Core.Packs.PackSet Packs(string player = "c", string[]? government = null, double playerTax = 30) =>
        Load(World(playerTax), player, government: government ?? new[] { "a", "b" }, leader: (government ?? new[] { "a" })[0], turnsToElection: 40, interval: 40,
            extraGroups: true, mutate: GameFixture.FreePolicies);

    [Fact]
    public void VotersWhoLikeTheProposalWarmUpAndOthersCool()
    {
        var g = New(Packs());
        double capital = Pol(g).Party!.PoliticalCapital;
        Assert.True(g.Apply(new AnnouncePolicy("base:spend", 45)).Accepted);
        var m = Pol(g).Party!.Momentum;
        Assert.True(m["base:bystanders"] > 0); // they want spending at 45
        Assert.Equal(capital - PoliticsLimits.AnnounceCost, Pol(g).Party!.PoliticalCapital, 9);
        Assert.True(g.Apply(new AnnouncePolicy("base:tax", 30)).ErrorKey == "error.announce_limit"); // one a quarter
        g.EndTurn();
        Assert.True(g.Apply(new AnnouncePolicy("base:tax", 30)).Accepted);
        Assert.True(Pol(g).Party!.Momentum["base:workers"] < 0); // workers want the tax at 40
    }

    [Fact]
    public void TheSamePolicyWaitsForItsCooldownAndTheLeaderUsesThePolicyDialogInstead()
    {
        var g = New(Packs());
        Assert.True(g.Apply(new AnnouncePolicy("base:spend", 45)).Accepted);
        g.EndTurn();
        Assert.Equal("error.announce_cooldown", g.Apply(new AnnouncePolicy("base:spend", 50)).ErrorKey);
        var lead = New(Packs("a", new[] { "a", "b" }));
        Assert.Equal("error.not_for_leader", lead.Apply(new AnnouncePolicy("base:spend", 45)).ErrorKey);
        Assert.Equal("error.unknown_policy", g.Apply(new AnnouncePolicy("base:nope", 1)).ErrorKey);
    }

    [Fact]
    public void ContradictingTheOwnProgrammeCostsCredibility()
    {
        var g = New(Packs());
        g.Apply(new ShiftPlatform("base:tax", 25)); // below the credibility cap
        double before = Pol(g).Party!.Credibility;
        Assert.True(g.Apply(new AnnouncePolicy("base:tax", 45)).Accepted); // c wants tax 25-30
        Assert.True(Pol(g).Party!.Credibility < before);
    }

    [Fact]
    public void AnAiGovernmentSometimesAdoptsAPopularProposal()
    {
        int adopted = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var g = New(Packs(), seed);
            g.Apply(new AnnouncePolicy("base:spend", 45));
            var r = g.EndTurn();
            if (r.PoliticalEvents.OfType<AnnouncementAdopted>().Any())
            {
                adopted++;
                Assert.Contains(r.PoliticalEvents.OfType<GovernmentMeasure>(), e => e.PolicyId == "base:spend" && e.To > e.From);
            }
        }
        Assert.InRange(adopted, 2, 15);
    }

    [Fact]
    public void AnnouncementsSurviveSaveAndReplay()
    {
        var packs = Packs();
        var live = New(packs);
        live.Apply(new AnnouncePolicy("base:spend", 45));
        live.EndTurn();
        var loaded = Simulation.Load(packs, SaveData.FromYaml(live.Save().ToYaml()));
        Assert.Equal(Pol(live).Party!.Momentum, Pol(loaded).Party!.Momentum);
        Assert.Equal(live.PendingPolicies, loaded.PendingPolicies);
    }
}
