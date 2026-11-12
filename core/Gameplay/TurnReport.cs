using OpenPolitics.Core.Graph;

namespace OpenPolitics.Core.Gameplay;

/// <summary>A value's change over one turn with the per-edge contributions that explain it.</summary>
public sealed record ValueChange(string ValueId, double Before, double After, IReadOnlyList<Contribution> Contributions);

public sealed record TurnReport(
    int Turn, double Year,
    IReadOnlyList<ValueChange> Changes,
    IReadOnlyList<string> FiredEventIds,
    IReadOnlyList<string> Warnings);
