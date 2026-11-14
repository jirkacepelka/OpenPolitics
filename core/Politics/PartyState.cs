namespace OpenPolitics.Core.Politics;

/// <summary>A policy change made by the government (via the AI governor) that the opposition can take a stance on.</summary>
public sealed record MeasureInfo(string Id, int Turn, string PolicyId, double From, double To, bool StanceTaken);

/// <summary>The player's own party: resources, how others see it, its current platform and active campaigns.</summary>
public sealed record PartyStatus(
    double PoliticalCapital, double Organization, double Credibility, double Cohesion,
    IReadOnlyDictionary<string, double> Relations, IReadOnlyDictionary<string, double> Momentum,
    IReadOnlyDictionary<string, double> Pressure, IReadOnlyDictionary<string, double> Platform,
    IReadOnlyList<MeasureInfo> Measures,
    double Income = 0.0,
    IReadOnlyDictionary<string, double>? Priorities = null,
    IReadOnlyList<string>? Promises = null);
