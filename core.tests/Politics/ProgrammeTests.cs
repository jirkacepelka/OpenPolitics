using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>The party's programme is a choice of laws: one can be added (at the setting in force) or taken out, at a price.</summary>
public class ProgrammeTests
{
    private static Game As(string player) =>
        New(Load(new[] { new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 2.4, Tax: 31), new Spec("z", 0.25) }, player, interval: 60, turnsToElection: 40, slowTarget: 3), 1);

    private static PartyStatus St(Game g) => Pol(g).Party!;
    private static double Setting(Game g, string policy) => g.State.Countries.Values.Single().Policies[policy];

    [Fact]
    public void AddedLawStartsAtTheSettingInForceWithAMiddlingPriority()
    {
        var g = As("z");
        Assert.DoesNotContain("base:spend", St(g).Platform.Keys);
        double pc = St(g).PoliticalCapital;
        Assert.True(g.Apply(new AddToPlatform("base:spend")).Accepted);
        Assert.Equal(Setting(g, "base:spend"), St(g).Platform["base:spend"], 9);
        Assert.Equal(PartyRules.AddWeight, St(g).Priorities!["base:spend"], 9);
        Assert.Equal(pc - PartyRules.AddCost, St(g).PoliticalCapital, 9);
    }

    [Fact]
    public void AddingTwiceOrAnUnknownLawIsRefused()
    {
        var g = As("z");
        Assert.True(g.Apply(new AddToPlatform("base:spend")).Accepted);
        Assert.Equal("error.already_in_platform", g.Apply(new AddToPlatform("base:spend")).ErrorKey);
        Assert.Equal("error.unknown_policy", g.Apply(new AddToPlatform("base:nope")).ErrorKey);
    }

    [Fact]
    public void RemovingCostsCapitalAndCredibilityAndKeepsAMinimum()
    {
        var g = As("z");
        Assert.True(g.Apply(new AddToPlatform("base:spend")).Accepted); // tax, slow, spend
        double pc = St(g).PoliticalCapital, cred = St(g).Credibility;
        Assert.True(g.Apply(new RemoveFromPlatform("base:tax")).Accepted);
        Assert.DoesNotContain("base:tax", St(g).Platform.Keys);
        Assert.Equal(pc - PartyRules.RemoveCost, St(g).PoliticalCapital, 9);
        Assert.Equal(cred - PartyRules.RemoveCredibility, St(g).Credibility, 9);
        Assert.Equal("error.not_in_platform", g.Apply(new RemoveFromPlatform("base:tax")).ErrorKey);
        // two laws left: the programme may not shrink below the minimum
        Assert.Equal("error.platform_min", g.Apply(new RemoveFromPlatform("base:spend")).ErrorKey);
    }

    [Fact]
    public void ARefusedChangeCostsNothing()
    {
        var g = As("z");
        double pc = St(g).PoliticalCapital;
        Assert.False(g.Apply(new RemoveFromPlatform("base:spend")).Accepted); // not in the programme
        Assert.Equal(pc, St(g).PoliticalCapital, 12);
    }

    [Fact]
    public void ProgrammeChangesReplayFromTheSave()
    {
        var packs = Load(new[] { new Spec("a", 0.42), new Spec("b", 0.33, Tax: 31), new Spec("z", 0.25) }, "z", interval: 60, turnsToElection: 40, slowTarget: 3);
        var g = New(packs, 1);
        Assert.True(g.Apply(new AddToPlatform("base:spend")).Accepted);
        Assert.True(g.Apply(new RemoveFromPlatform("base:tax")).Accepted);
        g.EndTurn();
        var loaded = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(St(g).Platform.Keys.ToArray(), St(loaded).Platform.Keys.ToArray());
        Assert.Equal(St(g).Platform["base:spend"], St(loaded).Platform["base:spend"], 12);
    }
}
