using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Graph;

/// <summary>Maps a source's deviation from its reference to a dimensionless influence.</summary>
public static class EdgeFunctions
{
    /// <summary>Numeric safety bound for a function output (not a bound on any simulated value).</summary>
    private const double Limit = 1e100;

    /// <summary>
    /// d = (x - reference) / scale, then divided by fn.Scale (an optional shaping constant, default 1).
    /// A non-positive scale yields d = 0. The result is always finite.
    /// </summary>
    public static double Apply(FnDef fn, double x, double reference, double scale)
    {
        double d = scale > 0 ? (x - reference) / scale : 0.0;
        if (fn.Scale > 0) d /= fn.Scale;
        if (double.IsNaN(d)) return 0.0;
        d = Clamp(d);

        double r = fn.Type switch
        {
            "linear" => d,
            "power" => d == 0 ? 0.0 : Math.Sign(d) * DetMath.Pow(Math.Abs(d), fn.P),
            "tanh" => DetMath.Tanh(d),
            "threshold" => Threshold(fn, d),
            _ => 0.0,
        };
        return double.IsNaN(r) ? 0.0 : Clamp(r);
    }

    private static double Threshold(FnDef fn, double d)
    {
        if (fn.Width <= 0) return d >= fn.At ? 1.0 : 0.0;
        double lo = fn.At - fn.Width / 2, hi = fn.At + fn.Width / 2;
        if (d <= lo) return 0.0;
        if (d >= hi) return 1.0;
        return (d - lo) / fn.Width;
    }

    private static double Clamp(double v) => v > Limit ? Limit : v < -Limit ? -Limit : v;
}
