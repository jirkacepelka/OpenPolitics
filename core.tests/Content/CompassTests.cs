using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Content;

public class CompassTests
{
    private static readonly Core.Packs.PackSet Packs = BasePackTests.LoadBase().Packs!;

    private static IReadOnlyDictionary<string, (double X, double Y)> Compass() =>
        Simulation.NewGame(Packs, "base:cz_2015", 1).Compass;

    [Fact]
    public void EveryPartyHasAPositionInsideTheSquare()
    {
        var c = Compass();
        Assert.Equal(Packs.Parties.Count, c.Count);
        foreach (var (id, (x, y)) in c)
        {
            Assert.InRange(x, -1.0, 1.0);
            Assert.InRange(y, -1.0, 1.0);
        }
    }

    [Fact]
    public void SocialAlliancesSitsLeftOfTheCivicRight()
    {
        var c = Compass();
        Assert.True(c["base:social_alliance"].X < c["base:civic_right"].X - 0.3);
    }

    [Fact]
    public void TheHomelandPartyIsMoreAuthoritarianThanTheOpenNet()
    {
        var c = Compass();
        Assert.True(c["base:homeland_first"].Y > c["base:open_net"].Y + 0.3);
    }

    [Fact]
    public void ShiftingThePlatformMovesThePlayersPoint()
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015", 1);
        double before = g.Compass["base:social_alliance"].X;
        for (int i = 0; i < 5; i++) g.Apply(new ShiftPlatform("base:income_tax", 45));
        Assert.True(g.Compass["base:social_alliance"].X < before);
    }

    [Fact]
    public void PositionsAreDeterministic()
    {
        Assert.Equal(Compass(), Compass());
    }
}
