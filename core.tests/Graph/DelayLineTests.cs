using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Graph;

public class DelayLineTests
{
    private static double[] Run(DelayLine line, params double[] inputs) => inputs.Select(line.Push).ToArray();

    [Fact]
    public void NoneIsIdentity() =>
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, Run(new DelayLine(new DelayDef("none", 0)), 1, 2, 3));

    [Fact]
    public void LagReturnsInputFromNTurnsAgo() =>
        Assert.Equal(new[] { 1.0, 1.0, 1.0, 2.0 }, Run(new DelayLine(new DelayDef("lag", 2)), 1, 2, 3, 4));

    [Fact]
    public void LagOfZeroTurnsIsIdentity() =>
        Assert.Equal(new[] { 5.0, 6.0 }, Run(new DelayLine(new DelayDef("lag", 0)), 5, 6));

    [Fact]
    public void SmoothApproachesInputMonotonically()
    {
        var line = new DelayLine(new DelayDef("smooth", 4));
        Assert.Equal(0.0, line.Push(0));
        double prev = 0;
        double last = 0;
        for (int i = 0; i < 40; i++)
        {
            last = line.Push(1);
            Assert.True(last > prev && last <= 1.0);
            prev = last;
        }
        Assert.True(1.0 - last < 1e-3);
    }

    [Theory]
    [InlineData("none", 0)]
    [InlineData("lag", 3)]
    [InlineData("smooth", 5)]
    public void SnapshotRoundTrips(string type, double turns)
    {
        var def = new DelayDef(type, turns);
        var a = new DelayLine(def);
        foreach (var x in new[] { 1.0, 4.0, 2.0, 8.0, 3.0 }) a.Push(x);
        var b = DelayLine.FromSnapshot(def, a.Snapshot());
        foreach (var x in new[] { 7.0, 1.0, 9.0, 2.0 }) Assert.Equal(a.Push(x), b.Push(x));
    }
}
