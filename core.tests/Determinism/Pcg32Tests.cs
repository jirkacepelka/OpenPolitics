using OpenPolitics.Core.Determinism;

namespace OpenPolitics.Core.Tests.Determinism;

public class Pcg32Tests
{
    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new Pcg32(123);
        var b = new Pcg32(123);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void DifferentSeedsDiffer()
    {
        var a = new Pcg32(1);
        var b = new Pcg32(2);
        bool anyDifferent = false;
        for (int i = 0; i < 16; i++) anyDifferent |= a.NextUInt() != b.NextUInt();
        Assert.True(anyDifferent);
    }

    [Fact]
    public void ReferenceVector()
    {
        // From the PCG reference demo: pcg32_srandom(42, 54).
        var rng = new Pcg32(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (var e in expected) Assert.Equal(e, rng.NextUInt());
    }

    [Fact]
    public void NextDoubleInRange()
    {
        var rng = new Pcg32(5);
        for (int i = 0; i < 10_000; i++)
        {
            double d = rng.NextDouble();
            Assert.InRange(d, 0.0, 1.0);
            Assert.True(d < 1.0);
        }
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    public void ExtremeSeeds(ulong seed)
    {
        var rng = new Pcg32(seed);
        for (int i = 0; i < 1000; i++)
        {
            rng.NextUInt();
            Assert.InRange(rng.NextInt(7), 0, 6);
        }
    }

    [Fact]
    public void CloneContinuesIdentically()
    {
        var a = new Pcg32(9);
        a.NextUInt();
        var b = a.Clone();
        for (int i = 0; i < 50; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
    }
}
