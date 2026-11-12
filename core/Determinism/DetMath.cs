namespace OpenPolitics.Core.Determinism;

/// <summary>
/// Transcendental functions built only from IEEE-754 basic operations (+, -, *, /), so results are
/// identical on every platform (unlike Math.Exp/Log/Pow, which may call platform libm).
/// Accuracy is about 1e-15 relative, far more than the simulation needs.
/// </summary>
public static class DetMath
{
    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;
    private const double InvLn2 = 1.44269504088896338700e+00;

    public static double Exp(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x > 709.78) return double.PositiveInfinity;
        if (x < -745.0) return 0.0;
        double kd = Math.Floor(x * InvLn2 + 0.5);
        int k = (int)kd;
        double r = (x - kd * Ln2Hi) - kd * Ln2Lo;
        double p = ExpTaylor(r);
        return ScaleByPow2(p, k);
    }

    private static double ExpTaylor(double r)
    {
        // Horner form of sum r^n / n!, |r| <= ~0.35
        double sum = 1.0;
        for (int n = 16; n >= 1; n--) sum = 1.0 + sum * r / n;
        return sum;
    }

    private static double ScaleByPow2(double v, int k)
    {
        // Multiplication by a power of two is exact except for subnormal results (still deterministic).
        while (k > 1000) { v *= Pow2(1000); k -= 1000; }
        while (k < -1000) { v *= Pow2(-1000); k += 1000; }
        return v * Pow2(k);
    }

    private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

    public static double Log(double x)
    {
        if (double.IsNaN(x) || x < 0) return double.NaN;
        if (x == 0) return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x)) return x;
        int e = 0;
        if (x < 2.2250738585072014e-308) { x *= Pow2(54); e = -54; } // subnormal
        long bits = BitConverter.DoubleToInt64Bits(x);
        e += (int)((bits >> 52) & 0x7FF) - 1023;
        double m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L); // [1,2)
        if (m > 1.4142135623730951) { m *= 0.5; e += 1; }
        double s = (m - 1.0) / (m + 1.0);
        double s2 = s * s;
        double sum = 0.0;
        for (int n = 15; n >= 0; n--) sum = 1.0 / (2 * n + 1) + sum * s2;
        double logm = 2.0 * s * sum;
        return (e * Ln2Hi + logm) + e * Ln2Lo;
    }

    /// <summary>x^y for x &gt; 0. Pow(0, y) follows the usual limits; negative x yields NaN.</summary>
    public static double Pow(double x, double y)
    {
        if (y == 0) return 1.0;
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (x < 0) return double.NaN;
        if (x == 0) return y > 0 ? 0.0 : double.PositiveInfinity;
        return Exp(y * Log(x));
    }

    public static double Tanh(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        double a = Math.Abs(x);
        if (a > 20.0) return x < 0 ? -1.0 : 1.0;
        double t = 2.0 * a;
        double em1 = t < 1.0 ? Expm1Small(t) : Exp(t) - 1.0;
        double r = em1 / (em1 + 2.0);
        return x < 0 ? -r : r;
    }

    private static double Expm1Small(double t)
    {
        // sum_{n>=1} t^n / n!, accurate for small t (no cancellation)
        double sum = 0.0;
        for (int n = 20; n >= 1; n--) sum = (1.0 + sum) * t / n;
        return sum;
    }
}
