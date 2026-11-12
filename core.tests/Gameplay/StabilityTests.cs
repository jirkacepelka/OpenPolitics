using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Tests.Content;

namespace OpenPolitics.Core.Tests.Gameplay;

public class StabilityTests
{
    private static Game NewGame(ulong seed) =>
        Simulation.NewGame(BasePackTests.LoadBase().Packs!, "base:cz_2015_sandbox", seed);

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    public void ExtremeSeedsAreDeterministic(ulong seed)
    {
        string Run()
        {
            var g = NewGame(seed);
            for (int i = 0; i < 40; i++) g.EndTurn();
            return GameTests.Fingerprint(g);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void TenThousandTurnsNoNaNOrException()
    {
        var g = NewGame(7);
        for (int i = 0; i < 10_000; i++)
        {
            var report = g.EndTurn();
            Assert.Empty(report.Warnings);
            foreach (var (id, v) in g.State.Countries["base:czechia"].Values)
                Assert.True(double.IsFinite(v), $"{id} at turn {i}: {v}");
        }
        Assert.Equal(10_000, g.State.Turn);
    }
}
