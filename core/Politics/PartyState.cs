namespace OpenPolitics.Core.Politics;

/// <summary>A policy change made by the government (via the AI governor) that the opposition can take a stance on.</summary>
public sealed record MeasureInfo(string Id, int Turn, string PolicyId, double From, double To, bool StanceTaken);

/// <summary>
/// The player's own party: resources, how others see it, its current platform and active campaigns. GroupSupport is the share of each voter group
/// that would vote for the party now (decision 0015; GroupSupportPrev the same a quarter earlier, for the trend).
/// </summary>
public sealed record PartyStatus(
    double PoliticalCapital, double Organization, double Credibility, double Cohesion,
    IReadOnlyDictionary<string, double> Relations, IReadOnlyDictionary<string, double> Momentum,
    IReadOnlyDictionary<string, double> Pressure, IReadOnlyDictionary<string, double> Platform,
    IReadOnlyList<MeasureInfo> Measures,
    double Income = 0.0,
    IReadOnlyDictionary<string, double>? Priorities = null,
    IReadOnlyList<string>? Promises = null,
    IReadOnlyList<PromiseInfo>? PromiseDetails = null,
    double FiscalBlame = 0.0,
    IReadOnlyDictionary<string, double>? GroupSupport = null,
    IReadOnlyDictionary<string, double>? GroupSupportPrev = null,
    double FiscalCredit = 0.0, double FiscalCreditCapital = 0.0);

/// <summary>
/// A promise to voters (decisions 0014, 0015): "what we will do if you elect us", made in quarter MadeTurn in the window before an election. That
/// election carries it over (Carried) and it is judged at the next one, in JudgedIn quarters. Once carried, QuartersElapsed counts the quarters of
/// the new term and QuartersInGovernment those the party sat in the government (it answers for the promise when that is at least half); Met is
/// the state right now.
/// </summary>
public sealed record PromiseInfo(string DemandId, int MadeTurn, int QuartersElapsed, int QuartersInGovernment, bool Met, bool Carried = false, int JudgedIn = 0)
{
    /// <summary>Whether a broken promise would cost the party if it were judged now (before the carry-over: only if it governs half the next term).</summary>
    public bool CountsAgainst => Carried && QuartersElapsed > 0 ? 2 * QuartersInGovernment >= QuartersElapsed : Carried;
}
