namespace OpenPolitics.Core.Batch;

public static class StabilityChecks
{
    /// <summary>
    /// True if a (passive, 400+ turn) GDP series looks capped: it does not grow over the last 80 turns (relative
    /// change under 1e-6) or is not above its turn-80 level at turn 400. Shorter series cannot be judged.
    /// </summary>
    public static bool DetectCeiling(IReadOnlyList<double> gdp)
    {
        if (gdp.Count < 401) return false;
        double g320 = gdp[320], g400 = gdp[400];
        if (!(g320 > 0)) return false;
        if (Math.Abs(g400 / g320 - 1.0) < 1e-6) return true;
        return !(g400 > gdp[80]);
    }
}
