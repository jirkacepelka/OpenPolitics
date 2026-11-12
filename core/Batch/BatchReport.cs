namespace OpenPolitics.Core.Batch;

/// <summary>Limits used to flag implausible runs. They are detection thresholds, not model ceilings.</summary>
public sealed record StabilityThresholds(
    double MaxAnnualGdpGrowth = 0.20,
    double CollapseRatio = 0.01,
    double ExplosionRatio = 100.0,
    double PercentMin = -50.0,
    double PercentMax = 200.0,
    double DebtSpiralRatio = 3.0)
{
    /// <summary>
    /// Net positions that may legitimately be repaid or turn into net assets (public debt after long surpluses).
    /// They are exempt from the collapse check, and their explosion check compares |value| to GDP instead of
    /// to the initial value. Debt above <see cref="DebtSpiralRatio"/> x GDP is counted as a debt spiral.
    /// </summary>
    public IReadOnlySet<string> NetPositions { get; init; } = new HashSet<string>(StringComparer.Ordinal) { "base:debt" };
}

/// <summary>
/// DebtSpiralGames does not make random or extreme batches unstable (they abuse the model on purpose), but a spiral
/// in passive play, where nobody acted, is a defect of the data or the model and does fail the batch. Informational otherwise: a debt spiral is a limit that arises from the
/// model (interest on growing debt), which random and extreme strategies are expected to trigger.
/// </summary>
public sealed record BatchReport(
    int Games, int Turns, string Strategy,
    int NanOrInfGames, int ExtremeRateGames, int CollapseGames, int ExplosionGames, int DebtSpiralGames,
    bool CeilingDetected, IReadOnlyList<string> Notes)
{
    public bool Stable =>
        NanOrInfGames == 0 && ExtremeRateGames == 0 && CollapseGames == 0 && ExplosionGames == 0 && !CeilingDetected
        && (Strategy != "passive" || DebtSpiralGames == 0);
}
