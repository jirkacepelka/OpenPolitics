using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class OppositionTests
{
    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 3, Tax: 31),
        new Spec("c", 0.22, Price: 3, Tax: 45), new Spec("z", 0.03),
    };

    /// <summary>Player z watches the AI government (a + b) form; the governor then raises spend toward b's demand.</summary>
    private static Game Watching(ulong seed = 1, int turns = 2)
    {
        var g = New(Load(Parties(), "z", interval: 60, extraGroups: true), seed);
        Run(g, turns);
        return g;
    }

    private static PartyStatus St(Game g) => Pol(g).Party!;

    [Fact]
    public void CriticizingAnImprovingValueMakesCaringGroupsThatLikeItBacklash()
    {
        var g = Watching(turns: 5);
        double pc = St(g).PoliticalCapital;
        Assert.True(g.Apply(new Criticize("base:gdp")).Accepted);
        Assert.Equal(pc - 2, St(g).PoliticalCapital, 12);
        Assert.Equal(-0.10, St(g).Momentum["base:workers"], 12); // w = 5: gdp growth is good for them -> backlash
    }

    [Fact]
    public void CriticizingAWorseningValueGivesMomentumAndPressure()
    {
        var g = Watching(turns: 5);
        g.Apply(new Criticize("base:gdp"));
        // contrarians (w = -2, max |w| = 5): GDP growth is bad for them
        Assert.Equal(0.15 * 2 / 5, St(g).Momentum["base:contrarians"], 12);
        Assert.Equal(0.10 * 2 / 5, St(g).Pressure["base:contrarians"], 12);
    }

    [Fact]
    public void OnlyCaringGroupsReact()
    {
        var g = Watching(turns: 5);
        g.Apply(new Criticize("base:gdp"));
        Assert.Equal(0.0, St(g).Momentum["base:bystanders"]);
        Assert.Equal(0.0, St(g).Pressure["base:bystanders"]);
    }

    private static (double share, double loyalty) Outcome(bool criticize)
    {
        var g = Watching(turns: 2);
        for (int i = 0; i < 3; i++)
        {
            if (criticize) Assert.True(g.Apply(new Criticize("base:gdp")).Accepted);
            g.EndTurn();
        }
        return (Pol(g).Support["base:a"], Pol(g).Loyalty!["base:b"]);
    }

    [Fact]
    public void PressureLowersTheGovernmentPartiesPoll() =>
        Assert.True(Outcome(true).share < Outcome(false).share);

    [Fact]
    public void PressureMakesPartnersLessLoyal() =>
        Assert.True(Outcome(true).loyalty < Outcome(false).loyalty);

    [Fact]
    public void LeaderCannotCriticizeOrTakeStances()
    {
        var g = New(Load(Parties(), "a", interval: 60, extraGroups: true));
        Assert.Equal("error.not_opposition", g.Apply(new Criticize("base:gdp")).ErrorKey);
        Assert.Equal("error.not_opposition", g.Apply(new TakeStance("1:base:spend", Stance.Support)).ErrorKey);
        Assert.Equal(6.0, St(g).PoliticalCapital);
    }

    [Fact]
    public void MeasuresAreRecordedFromTheGovernor()
    {
        var g = Watching();
        var m = Assert.Single(St(g).Measures);
        Assert.Equal(("2:base:spend", "base:spend", 40.0, 41.0, false), (m.Id, m.PolicyId, m.From, m.To, m.StanceTaken));
    }

    [Fact]
    public void StanceOnAnApprovedMeasure()
    {
        // bystanders prefer spend 45: 40 -> 41 is a step they like (share of electorate 1/3)
        var support = Watching();
        string id = St(support).Measures.Single().Id;
        Assert.True(support.Apply(new TakeStance(id, Stance.Support)).Accepted);
        Assert.Equal(0.10 / 3, St(support).Momentum["base:bystanders"], 12);
        Assert.True(St(support).Measures.Single().StanceTaken);

        var oppose = Watching();
        Assert.True(oppose.Apply(new TakeStance(id, Stance.Oppose)).Accepted);
        Assert.Equal(-0.10 / 3, St(oppose).Momentum["base:bystanders"], 12);
    }

    [Fact]
    public void OneStancePerMeasure()
    {
        var g = Watching();
        string id = St(g).Measures.Single().Id;
        g.Apply(new TakeStance(id, Stance.Support));
        double pc = St(g).PoliticalCapital;
        Assert.Equal("error.already_taken", g.Apply(new TakeStance(id, Stance.Oppose)).ErrorKey);
        Assert.Equal(pc, St(g).PoliticalCapital);
    }

    [Fact]
    public void UnknownValueOrMeasureIsAnError()
    {
        var g = Watching();
        Assert.Equal("error.unknown_value", g.Apply(new Criticize("base:ghost")).ErrorKey);
        Assert.Equal("error.unknown_measure", g.Apply(new TakeStance("99:base:spend", Stance.Support)).ErrorKey);
        Assert.Equal("error.no_effect", g.Apply(new Criticize("base:inflation")).ErrorKey); // no group cares about it
    }

    [Fact]
    public void SaveAndLoadReplayOppositionActions()
    {
        var packs = Load(Parties(), "z", interval: 60, extraGroups: true);
        var g = New(packs, 4);
        Run(g, 5);
        Assert.True(g.Apply(new Criticize("base:gdp")).Accepted);
        Assert.True(g.Apply(new TakeStance(St(g).Measures.First().Id, Stance.Support)).Accepted);
        Run(g, 3);
        var h = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(St(g).Momentum, St(h).Momentum);
        Assert.Equal(St(g).Pressure, St(h).Pressure);
        Assert.Equal(Pol(g).Support, Pol(h).Support);
        Assert.Equal(St(g).Measures, St(h).Measures);
    }
}
