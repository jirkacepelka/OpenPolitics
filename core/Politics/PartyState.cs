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
    IReadOnlyList<string>? Promises = null,
    IReadOnlyList<PromiseInfo>? PromiseDetails = null);

/// <summary>
/// A promise to voters (decision 0014): made in quarter MadeTurn, judged at the next election. QuartersElapsed quarters have passed since, the party sat
/// in the government in QuartersInGovernment of them (it answers for the promise when that is at least half); Met is the state right now.
/// </summary>
public sealed record PromiseInfo(string DemandId, int MadeTurn, int QuartersElapsed, int QuartersInGovernment, bool Met)
{
    /// <summary>Whether a broken promise would cost the party if the election were now.</summary>
    public bool CountsAgainst => QuartersElapsed > 0 ? 2 * QuartersInGovernment >= QuartersElapsed : true;
}
