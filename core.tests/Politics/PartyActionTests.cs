using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class PartyActionTests
{
    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 2.4, Tax: 31),
        new Spec("c", 0.22, Price: 3, Tax: 45), new Spec("z", 0.03),
    };

    private static Game As(string player, int turnsToElection = 40, ulong seed = 1) =>
        New(Load(Parties(), player, interval: 60, turnsToElection: turnsToElection), seed);

    private static PartyStatus St(Game g) => Pol(g).Party!;

    [Fact]
    public void StartingResources()
    {
        var s = St(As("z"));
        Assert.Equal((6.0, 0.2, 1.0), (s.PoliticalCapital, s.Organization, s.Credibility));
        Assert.Equal(1.0, s.Cohesion);
        Assert.DoesNotContain("base:z", s.Relations.Keys);
        Assert.Equal(0.3, s.Relations["base:b"]);
    }

    [Fact]
    public void IncomeFollowsTheFormula()
    {
        var g = As("z"); // opposition: no leader bonus
        double share = Pol(g).Support["base:z"];
        g.EndTurn();
        Assert.Equal(6 + PoliticsLimits.BaseIncome + 8 * 0.19 * share, St(g).PoliticalCapital, 9);

        var leader = As("a"); // leader bonus
        double leaderShare = Pol(leader).Support["base:a"];
        leader.EndTurn();
        Assert.Equal(6 + PoliticsLimits.BaseIncome + 8 * 0.19 * leaderShare + PoliticsLimits.LeaderIncome, St(leader).PoliticalCapital, 9);
    }

    [Fact]
    public void OrganizationDecaysAndBuildingAddsFourHundredths()
    {
        var g = As("z");
        g.EndTurn();
        Assert.Equal(0.19, St(g).Organization, 12);
        double pc = St(g).PoliticalCapital;
        Assert.True(g.Apply(new BuildOrganization()).Accepted);
        Assert.Equal(0.23, St(g).Organization, 12);
        Assert.Equal(pc - 4, St(g).PoliticalCapital, 12);
    }

    [Fact]
    public void CourtRaisesRelationAndItDecaysTowardDefault()
    {
        var g = As("z");
        Assert.True(g.Apply(new Court("base:b")).Accepted);
        Assert.Equal(0.4, St(g).Relations["base:b"], 12);
        Assert.Equal(3.0, St(g).PoliticalCapital, 12);
        g.EndTurn();
        Assert.Equal(0.38, St(g).Relations["base:b"], 12);
        Run(g, 20);
        Assert.Equal(0.3, St(g).Relations["base:b"], 12); // never below the default
    }

    [Fact]
    public void CourtedPartyIsCheaperInCoalitionTalks()
    {
        Game Formateur() { var g = New(Load(Parties(), "a", interval: 60, turnsToElection: 1)); g.EndTurn(); return g; }
        var draft = new CoalitionProposal("base:a", new[] { "base:b" }, new[] { new AgreedDemand("base:b", "d2") });

        var plain = Formateur();
        double before = plain.AssessCoalition(draft)!.Partners.Single().Required;
        var courted = Formateur();
        Assert.True(courted.Apply(new Court("base:b")).Accepted);
        Assert.True(courted.Apply(new Court("base:b")).Accepted); // relation 0.3 -> 0.5
        double after = courted.AssessCoalition(draft)!.Partners.Single().Required;
        // decision 0016 keeps the relation term of the price: (1 - 0.5 x relation), now times the talk factor
        Assert.Equal((1 - 0.5 * 0.5) / (1 - 0.5 * 0.3), after / before, 9);
    }

    [Fact]
    public void ShiftPlatformMovesAtMostTenPercentOfTheRange()
    {
        var g = As("a");
        Assert.Equal(30.0, St(g).Platform["base:tax"]);
        Assert.True(g.Apply(new ShiftPlatform("base:tax", 50)).Accepted);
        Assert.Equal(36.0, St(g).Platform["base:tax"], 12); // range of base:tax is 60, so 6 per call
        Assert.True(g.Apply(new ShiftPlatform("base:tax", 20)).Accepted);
        Assert.Equal(30.0, St(g).Platform["base:tax"], 12);
    }

    [Fact]
    public void ShiftToCurrentPositionDoesNothingBad()
    {
        var g = As("a");
        Assert.True(g.Apply(new ShiftPlatform("base:tax", 30)).Accepted);
        Assert.Equal(1.0, St(g).Credibility);
        Assert.Equal(6.0 - PartyRules.ShiftCost, St(g).PoliticalCapital);
        for (int i = 0; i < 6; i++) g.Apply(new ShiftPlatform("base:tax", 60));
        Assert.InRange(St(g).Credibility, 0.0, 1.0);
        Assert.True(double.IsFinite(St(g).Platform["base:tax"]));
    }

    [Fact]
    public void ShiftLowersCredibilityAndItRecovers()
    {
        var g = As("a");
        g.Apply(new ShiftPlatform("base:tax", 60)); // 6 of 60 = 0.1, credibility falls by half of that
        Assert.Equal(0.95, St(g).Credibility, 12);
        Run(g, 3);
        Assert.Equal(1.0, St(g).Credibility, 12); // +0.025 per quarter, capped
    }

    [Fact]
    public void CampaignGivesMomentumDoubledBeforeAnElection()
    {
        var far = As("z", turnsToElection: 10);
        Assert.True(far.Apply(new Campaign("base:workers", 1)).Accepted);
        Assert.Equal(0.06, St(far).Momentum["base:workers"], 12);
        Assert.Equal(3.0, St(far).PoliticalCapital, 12);

        var near = As("z", turnsToElection: 3);
        Assert.True(near.Apply(new Campaign("base:workers", 1)).Accepted);
        Assert.Equal(0.12, St(near).Momentum["base:workers"], 12);
    }

    [Fact]
    public void MomentumDecaysWithAHalfLifeOfFourTurns()
    {
        var g = As("z", turnsToElection: 40);
        g.Apply(new Campaign("base:workers", 1));
        Run(g, 4);
        Assert.Equal(0.03, St(g).Momentum["base:workers"], 9);
    }

    [Fact]
    public void NotEnoughCapitalRefusesWithoutSpending()
    {
        var g = As("z");
        Assert.True(g.Apply(new Court("base:a")).Accepted);
        Assert.True(g.Apply(new Court("base:b")).Accepted);
        Assert.Equal(0.0, St(g).PoliticalCapital, 12);
        var r = g.Apply(new Court("base:c"));
        Assert.Equal("error.not_enough_capital", r.ErrorKey);
        Assert.Equal(0.0, St(g).PoliticalCapital, 12);
        Assert.Equal(0.3, St(g).Relations["base:c"]);
    }

    [Fact]
    public void BadArgumentsAreErrorsNotExceptions()
    {
        var g = As("a");
        Assert.Equal("error.unknown_party", g.Apply(new Court("base:ghost")).ErrorKey);
        Assert.Equal("error.unknown_party", g.Apply(new Court("base:a")).ErrorKey);
        Assert.Equal("error.unknown_group", g.Apply(new Campaign("base:ghost", 1)).ErrorKey);
        Assert.Equal("error.bad_effort", g.Apply(new Campaign("base:workers", 0)).ErrorKey);
        Assert.Equal("error.bad_effort", g.Apply(new Campaign("base:workers", 4)).ErrorKey);
        Assert.Equal("error.unknown_policy", g.Apply(new ShiftPlatform("base:ghost", 1)).ErrorKey);
        Assert.Equal("error.not_in_platform", g.Apply(new ShiftPlatform("base:spend", 1)).ErrorKey);
        Assert.Equal("error.value_out_of_range", g.Apply(new ShiftPlatform("base:tax", double.NaN)).ErrorKey);
        Assert.Equal(6.0, St(g).PoliticalCapital);
        Assert.Empty(g.Save().Actions);
    }

    [Fact]
    public void SaveAndLoadReplayPartyActions()
    {
        var packs = Load(Parties(), "a", interval: 60, turnsToElection: 40);
        var g = New(packs, 3);
        g.Apply(new Court("base:b")); g.Apply(new ShiftPlatform("base:tax", 40));
        Run(g, 3);
        g.Apply(new Campaign("base:workers", 1)); g.Apply(new BuildOrganization());
        Run(g, 2);
        var h = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        var (x, y) = (St(g), St(h));
        Assert.Equal((x.PoliticalCapital, x.Organization, x.Credibility), (y.PoliticalCapital, y.Organization, y.Credibility));
        Assert.Equal(x.Relations, y.Relations);
        Assert.Equal(x.Platform, y.Platform);
        Assert.Equal(x.Momentum, y.Momentum);
        Assert.Equal(Pol(g).Support, Pol(h).Support);
    }

    [Fact]
    public void CapitalIsCappedSoItCannotBeStockpiledForever()
    {
        var g = As("z");
        Run(g, 60);
        Assert.Equal(30.0, St(g).PoliticalCapital, 9);
    }

    [Fact]
    public void CampaignRaisesTheSupportOfThePlayersPartyAmongThatGroup()
    {
        double Share(bool campaign)
        {
            var g = As("z", turnsToElection: 40);
            for (int i = 0; i < 6; i++)
            {
                if (campaign) g.Apply(new Campaign("base:workers", 1));
                g.EndTurn();
            }
            return Pol(g).Support["base:z"];
        }
        Assert.True(Share(true) > Share(false));
    }
}
