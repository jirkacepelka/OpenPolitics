using OpenPolitics.Core.Determinism;

namespace OpenPolitics.Core.Tests.Determinism;

public class DetMathTests
{
    private static void Close(double expected, double actual, double tol = 1e-12)
    {
        double scale = Math.Max(1.0, Math.Abs(expected));
        Assert.True(Math.Abs(expected - actual) <= tol * scale, $"expected {expected:R}, got {actual:R}");
    }

    private static void RelClose(double expected, double actual, double tol = 1e-12)
    {
        Assert.True(Math.Abs(expected - actual) / Math.Abs(expected) < tol, $"expected {expected:R}, got {actual:R}");
    }

    [Fact]
    public void PowMatchesMathPow()
    {
        for (double x = 0.001; x <= 1000; x *= 1.7)
            for (double y = -3; y <= 3; y += 0.25)
                RelClose(Math.Pow(x, y), DetMath.Pow(x, y));
    }

    [Fact]
    public void PowWithZeroExponentIsOne()
    {
        foreach (var x in new[] { 0.001, 1.0, 3.7, 1000.0 }) Assert.Equal(1.0, DetMath.Pow(x, 0));
    }

    [Fact]
    public void ExpMatchesMathExp()
    {
        for (double x = -50; x <= 50; x += 0.37) RelClose(Math.Exp(x), DetMath.Exp(x));
    }

    [Fact]
    public void LogMatchesMathLog()
    {
        for (double x = 1e-6; x <= 1e6; x *= 1.9) Close(Math.Log(x), DetMath.Log(x));
        Assert.Equal(0.0, DetMath.Log(1.0));
    }

    [Fact]
    public void TanhMatchesMathTanh()
    {
        for (double x = -20; x <= 20; x += 0.173) Close(Math.Tanh(x), DetMath.Tanh(x));
        Close(Math.Tanh(1e-6), DetMath.Tanh(1e-6));
    }

    [Fact]
    public void ExtremeArgumentsDoNotProduceNaN()
    {
        Assert.Equal(double.PositiveInfinity, DetMath.Exp(1000));
        Assert.Equal(0.0, DetMath.Exp(-1000));
        Assert.Equal(1.0, DetMath.Tanh(1e9));
        Assert.Equal(-1.0, DetMath.Tanh(-1e9));
        Assert.Equal(double.NegativeInfinity, DetMath.Log(0));
    }
}
