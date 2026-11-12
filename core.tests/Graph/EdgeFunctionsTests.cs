using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Graph;

public class EdgeFunctionsTests
{
    private static FnDef Fn(string type, double p = 1, double scale = 1, double at = 0, double width = 0) =>
        new(type, p, scale, at, width);

    [Fact]
    public void LinearIsNormalisedDeviation() =>
        Assert.Equal(1.0, EdgeFunctions.Apply(Fn("linear"), 15, 10, 5), 12);

    [Fact]
    public void PowerKeepsSignAndExponent()
    {
        Assert.Equal(4.0, EdgeFunctions.Apply(Fn("power", p: 2), 14, 10, 2), 9);
        Assert.Equal(-4.0, EdgeFunctions.Apply(Fn("power", p: 2), 6, 10, 2), 9);
    }

    [Fact]
    public void PowerOfNegativeIsOddSymmetric()
    {
        foreach (var x in new[] { 0.3, 1.0, 2.5, 7.0 })
            Assert.Equal(-EdgeFunctions.Apply(Fn("power", p: 1.5), 10 + x, 10, 1),
                          EdgeFunctions.Apply(Fn("power", p: 1.5), 10 - x, 10, 1), 12);
    }

    [Fact]
    public void TanhSaturatesAndHonoursFnScale()
    {
        Assert.Equal(Math.Tanh(0.5), EdgeFunctions.Apply(Fn("tanh"), 10.5, 10, 1), 12);
        Assert.Equal(Math.Tanh(0.5), EdgeFunctions.Apply(Fn("tanh", scale: 2), 11, 10, 1), 12);
        Assert.Equal(1.0, EdgeFunctions.Apply(Fn("tanh"), 1e9, 10, 1), 12);
    }

    [Fact]
    public void ThresholdRampsBetweenEdges()
    {
        var f = Fn("threshold", at: 0, width: 2);
        Assert.Equal(0.0, EdgeFunctions.Apply(f, -2, 0, 1), 12);
        Assert.Equal(0.5, EdgeFunctions.Apply(f, 0, 0, 1), 12);
        Assert.Equal(1.0, EdgeFunctions.Apply(f, 2, 0, 1), 12);
        var step = Fn("threshold", at: 1, width: 0);
        Assert.Equal(0.0, EdgeFunctions.Apply(step, 0.9, 0, 1), 12);
        Assert.Equal(1.0, EdgeFunctions.Apply(step, 1.0, 0, 1), 12);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("power")]
    [InlineData("tanh")]
    [InlineData("threshold")]
    public void ZeroScaleReturnsZeroNotNaN(string type)
    {
        double r = EdgeFunctions.Apply(Fn(type, p: 2, at: 0.5, width: 1), 123, 1, 0);
        Assert.True(double.IsFinite(r));
        Assert.Equal(0.0, r, 12);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("power")]
    [InlineData("tanh")]
    public void ValueEqualsReferenceIsZero(string type) =>
        Assert.Equal(0.0, EdgeFunctions.Apply(Fn(type, p: 1.7), 42, 42, 3), 12);

    [Theory]
    [InlineData("linear", 1e12)]
    [InlineData("linear", -1e12)]
    [InlineData("power", 1e12)]
    [InlineData("power", -1e12)]
    [InlineData("tanh", 1e12)]
    [InlineData("threshold", -1e12)]
    [InlineData("power", 0.0)]
    public void ExtremeInputsStayFinite(string type, double x)
    {
        foreach (double scale in new[] { 1e-300, 1.0, 1e300 })
        {
            double r = EdgeFunctions.Apply(Fn(type, p: 3, at: 0, width: 1), x, 0, scale);
            Assert.True(double.IsFinite(r), $"{type} x={x} scale={scale} -> {r}");
        }
    }
}
