namespace OpenPolitics.Core.Gameplay;

/// <summary>Choices made when a game starts. They are saved with the game.</summary>
/// <param name="ElectoralThreshold">Share of the vote a party needs for seats, 0 to 0.10; null keeps the country's own rule.</param>
public sealed record GameOptions(double? ElectoralThreshold = null)
{
    public const double MaxThreshold = 0.10;

    public static double Validated(double threshold) =>
        double.IsFinite(threshold) && threshold >= 0.0 && threshold <= MaxThreshold
            ? threshold
            : throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "The electoral threshold must be between 0 and 0.10.");
}
