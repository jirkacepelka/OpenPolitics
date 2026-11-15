using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Gameplay;

/// <summary>
/// What a coalition reshuffle would do (decision 0013). Majority: the new coalition holds at least SeatsNeeded seats. ErrorKey: why it cannot be
/// asked at all (no capital is charged then); otherwise Refusals are what the parties would answer at the end of the quarter (empty = they agree).
/// Cost is paid when asked; a refusal refunds all but <see cref="PoliticsLimits.ReshuffleRefusalCost"/>. CooldownLeft: quarters until the next
/// reshuffle may be asked.
/// </summary>
public sealed record ReshufflePreview(
    int SeatsHeld, int SeatsNeeded, bool Majority, double Cost, string? ErrorKey, string? ErrorDetail,
    IReadOnlyList<Refusal> Refusals, int CooldownLeft, bool Affordable);
