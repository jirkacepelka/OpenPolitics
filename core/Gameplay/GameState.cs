namespace OpenPolitics.Core.Gameplay;

/// <summary>Immutable per-country snapshot. Dictionaries iterate in ordinal key order.</summary>
public sealed record CountryState(
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, double> Policies,
    IReadOnlyDictionary<string, double> Satisfaction);

/// <summary>Immutable snapshot of the world (country id to state; one country in M1).</summary>
public sealed record GameState(int Turn, double Year, IReadOnlyDictionary<string, CountryState> Countries);
