namespace OpenPolitics.Core.Packs;

public sealed record PackManifest(
    string Id, string Version,
    IReadOnlyList<string> Dependencies, IReadOnlyList<string> LoadAfter,
    string License, bool Illustrative);

/// <summary>A simulated quantity in real units. Owner is null or the id of the economy model that steps it.</summary>
public sealed record ValueDef(
    string Id, string Unit, double Initial, double Reference, double Scale, string? Owner, string Source);

/// <summary>A player-controlled lever. Min/Max bound the lever, not any simulated value.</summary>
public sealed record PolicyDef(
    string Id, string Unit, double Min, double Max, double Default,
    string? CostValueId, double CostPerUnit, int DelayTurns, string? RevenueBaseValueId, string Source);

public enum EdgeKind { Additive, Relative }

public sealed record FnDef(string Type, double P, double Scale, double At, double Width);

public sealed record DelayDef(string Type, double Turns);

/// <summary>Influence of a source (value or policy) on the rate of change of a target value.</summary>
public sealed record EdgeDef(
    string Id, string From, string To, EdgeKind Kind, FnDef Fn, double Strength, DelayDef Delay, string Source);

public sealed record VoterGroupDef(
    string Id, double Size, double Weight,
    IReadOnlyDictionary<string, double> ValueWeights,
    IReadOnlyDictionary<string, double> PolicyPrefs,
    IReadOnlyDictionary<string, double> PolicyWeights,
    string Source);

public sealed record EffectDef(string ValueId, double Delta);

public sealed record EventDef(
    string Id, string Condition, double Probability, IReadOnlyList<EffectDef> Effects, string TextKey, string Source);

/// <summary>Parameters are model parameters (e.g. alpha, delta) for the country's economy model.</summary>
public sealed record CountryDef(
    string Id, string NameKey, IReadOnlyDictionary<string, double> Parameters, string Source);

public sealed record ScenarioDef(
    string Id, string CountryId, int StartYear, int TurnsPerYear,
    IReadOnlyDictionary<string, double> StartValues,
    IReadOnlyDictionary<string, double> StartPolicies,
    string Source);
