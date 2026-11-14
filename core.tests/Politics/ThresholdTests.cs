using OpenPolitics.Core.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class ThresholdTests
{
    private static readonly Spec[] Parties =
    {
        new("a", 0.50, Tax: 30), new("b", 0.32, Tax: 31), new("y", 0.08, Tax: 45), new("z", 0.03, Tax: 20), new("c", 0.07, Tax: 50),
    };

    private static IReadOnlyDictionary<string, int> SeatsAfterElection(double? threshold)
    {
        var g = Simulation.NewGame(Load(Parties, "a"), "base:f", 1, new GameOptions(threshold));
        g.EndTurn();
        return Pol(g).Seats;
    }

    [Fact]
    public void ThePackThresholdIsTheDefault()
    {
        var seats = SeatsAfterElection(null);
        Assert.True(seats["base:y"] > 0);
        Assert.Equal(0, seats["base:z"]);
    }

    [Fact]
    public void ZeroThresholdSeatsEvenTheSmallestParty()
    {
        Assert.True(SeatsAfterElection(0.0)["base:z"] > 0);
    }

    [Fact]
    public void TenPercentThresholdLocksOutAnEightPercentParty()
    {
        var seats = SeatsAfterElection(0.10);
        Assert.Equal(0, seats["base:y"]);
        Assert.Equal(0, seats["base:z"]);
        Assert.True(seats["base:a"] > 0);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.11)]
    [InlineData(double.NaN)]
    public void ThresholdOutsideZeroToTenPercentIsRejected(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Simulation.NewGame(Load(Parties, "a"), "base:f", 1, new GameOptions(threshold)));
    }

    [Fact]
    public void TheThresholdSurvivesSaveAndLoad()
    {
        var packs = Load(Parties, "a");
        var live = Simulation.NewGame(packs, "base:f", 1, new GameOptions(0.0));
        Assert.Equal(0.0, live.ElectoralThreshold);
        var back = SaveData.FromYaml(live.Save().ToYaml());
        Assert.Equal(0.0, back.Threshold);
        var loaded = Simulation.Load(packs, back);
        loaded.EndTurn();
        Assert.True(Pol(loaded).Seats["base:z"] > 0);
        Assert.Equal(0.0, loaded.ElectoralThreshold);
    }

    [Fact]
    public void WithoutAnOverrideTheSaveStaysFreeOfAThreshold()
    {
        var g = Simulation.NewGame(Load(Parties, "a"), "base:f", 1);
        Assert.Equal(0.05, g.ElectoralThreshold);
        Assert.Null(SaveData.FromYaml(g.Save().ToYaml()).Threshold);
    }
}
