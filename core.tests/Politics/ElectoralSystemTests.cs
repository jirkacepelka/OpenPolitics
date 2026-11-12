using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class ElectoralSystemTests
{
    private static Dictionary<string, double> V(params (string, double)[] xs) => xs.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void DHondtKnownExample()
    {
        var r = new ProportionalSystem(8, 0).Allocate(V(("a", 100), ("b", 80), ("c", 30), ("d", 20)));
        Assert.Equal(4, r["a"]);
        Assert.Equal(3, r["b"]);
        Assert.Equal(1, r["c"]);
        Assert.Equal(0, r["d"]);
    }

    [Fact]
    public void ThresholdExcludesSmallParties()
    {
        var r = new ProportionalSystem(10, 0.05).Allocate(V(("a", .5), ("b", .3), ("c", .17), ("d", .03)));
        Assert.Equal(0, r["d"]);
        Assert.Equal(10, r.Values.Sum());
    }

    [Fact]
    public void TieGoesToLowerId()
    {
        var r = new ProportionalSystem(1, 0).Allocate(V(("b", 100), ("a", 100)));
        Assert.Equal(1, r["a"]);
        Assert.Equal(0, r["b"]);
    }

    [Fact]
    public void NobodyAboveThresholdStillAllocatesAllSeats()
    {
        var r = new ProportionalSystem(20, 0.3).Allocate(V(("a", .2), ("b", .2), ("c", .2), ("d", .2), ("e", .2)));
        Assert.Equal(20, r.Values.Sum());
    }

    [Fact]
    public void SeatsAlwaysSumToTotal()
    {
        var rng = new Pcg32(7);
        var sys = new ProportionalSystem(200, 0.05);
        for (int i = 0; i < 1000; i++)
        {
            int n = 2 + rng.NextInt(11);
            var votes = new Dictionary<string, double>();
            for (int p = 0; p < n; p++) votes[$"p{p:00}"] = rng.NextDouble() + 0.001;
            var r = sys.Allocate(votes);
            Assert.Equal(200, r.Values.Sum());
            Assert.Equal(n, r.Count);
            Assert.Equal(r.Keys.OrderBy(k => k, StringComparer.Ordinal), r.Keys);
        }
    }

    [Fact]
    public void SingleParty()
    {
        var r = new ProportionalSystem(200, 0.05).Allocate(V(("only", 1)));
        Assert.Equal(200, r["only"]);
    }

    [Fact]
    public void AllZeroVotesThrows()
    {
        Assert.Throws<ArgumentException>(() => new ProportionalSystem(10, 0).Allocate(V(("a", 0), ("b", 0))));
    }

    [Fact]
    public void FactoryCreatesProportionalAndRejectsUnknownType()
    {
        var ok = ElectoralSystems.Create(new ElectoralSystemDef("t:pr", "proportional", 200, "dhondt", 0.05, "x"));
        Assert.Equal(200, ok.Seats);
        Assert.Throws<InvalidOperationException>(() =>
            ElectoralSystems.Create(new ElectoralSystemDef("t:x", "majoritarian", 200, "x", 0, "x")));
    }
}
