namespace OpenPolitics.Core.Packs;

public sealed record PackManifest(
    string Id, string Version,
    IReadOnlyList<string> Dependencies, IReadOnlyList<string> LoadAfter,
    string License, bool Illustrative);

/// <summary>A simulated quantity in real units. Owner is null or the id of the economy model that steps it.</summary>
public sealed record ValueDef(
    string Id, string Unit, double Initial, double Reference, double Scale, string? Owner, string Source, bool Hidden = false, double? Floor = null);

/// <summary>A player-controlled lever. AxisX/AxisY (-1..1) say how raising it moves a party on the compass: economic left (-) to right (+), social liberal (-) to authoritarian (+). Min/Max bound the lever, not any simulated value. Unit "tax_rate" needs BaseShare (the share of GDP the tax is levied on): the parameter is revenue as percent of GDP, shown to players as the rate on that base; unit "nominal_spending" shows the parameter (percent of GDP) as a yearly amount of money. CapitalCost (0..10, default 1) weighs how much political capital a change of the lever costs (see Politics.PolicyCost).</summary>
public sealed record PolicyDef(
    string Id, string Unit, double Min, double Max, double Default,
    string? CostValueId, double CostPerUnit, int DelayTurns, string? RevenueBaseValueId, string Source,
    double AxisX = 0.0, double AxisY = 0.0, double BaseShare = 0.0, double CapitalCost = 1.0,
    bool StartActive = true, bool Abolishable = true, double? IntroValue = null, string? CostDriver = null);

/// <summary>
/// Additive: a rate in value units per year. Relative: a growth rate in percent per year. Level (decision 0021): a one-off shift of the target's
/// LEVEL, in percent, that the edge's delay spreads out; it ends when the source stops moving (a tax raises the price level once, then inflation settles).
/// </summary>
public enum EdgeKind { Additive, Relative, Level }

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
    string Source,
    IReadOnlyDictionary<string, double>? LevelWeights = null,
    string? SizeValue = null, double SizeFactor = 1.0, double SizeOffset = 0.0)
{
    /// <summary>The group's share of the voters now: the value it follows (times a factor, plus an offset) when the pack names one, else the fixed size.</summary>
    public double SizeNow(IReadOnlyDictionary<string, double>? values) =>
        SizeValue != null && values != null && values.TryGetValue(SizeValue, out var v) && double.IsFinite(v) ? Math.Max(0.0, SizeOffset + SizeFactor * v) : Size;
}

public sealed record EffectDef(string ValueId, double Delta);

public sealed record EventDef(
    string Id, string Condition, double Probability, IReadOnlyList<EffectDef> Effects, string TextKey, string Source);

public sealed record PlatformPosition(double Target, double Weight);

/// <summary>A coalition demand. Kind is "policy" or "value"; Subject is that policy or value id; Op is ">=" or "<=".</summary>
public sealed record DemandDef(string Id, string Kind, string Subject, string Op, double Target, double Weight, int DueTurns);

public sealed record RedLineDef(string Policy, string Op, double Threshold);

public sealed record FactionDef(string Id, double Weight, IReadOnlyDictionary<string, double> Platform);

/// <summary>A fictional party: platform, voter-group affinities, coalition demands, red lines and factions.</summary>
public sealed record PartyDef(
    string Id, string NameKey,
    IReadOnlyDictionary<string, PlatformPosition> Platform,
    IReadOnlyDictionary<string, double> Affinity,
    bool Swing,
    IReadOnlyList<DemandDef> Demands, IReadOnlyList<RedLineDef> RedLines,
    double Price, IReadOnlyList<FactionDef> Factions, string Source);

/// <summary>
/// Type "proportional" (Seats, Method, Threshold) or the majority systems "fptp" (single-member districts, plurality) and "two_round" (a runoff
/// when nobody has more than half). For those Seats is the number of single-member districts; the districts are simulated inside the core
/// (decision 0017): Spread is how much a party's local vote varies from its national share, Strategic the share of voters who desert a hopeless
/// candidate, RunoffTop how many candidates reach the second round (RunoffMinShare: each needs this much of the first-round vote), Abstain the share
/// of an eliminated candidate's voters who stay home in the second round.
/// </summary>
public sealed record ElectoralSystemDef(string Id, string Type, int Seats, string Method, double Threshold, string Source,
    double Spread = 0.35, double Strategic = 0.0, int RunoffTop = 2, double RunoffMinShare = 0.0, double Abstain = 0.10);

public sealed record CountryPoliticsDef(
    string ElectoralSystem, int ElectionIntervalTurns, double MajorityFraction,
    int MaxFormationTurns, int CaretakerTurns, int ProgramSlots,
    IReadOnlyList<PortfolioDef>? Portfolios = null);

/// <summary>
/// A cabinet portfolio (ministry) of a country (decision 0016): coalition partners can be given it in formation talks. Value is what it is worth
/// toward a partner's price (more for a partner whose programme stresses its policies); Policies are the policy areas it is responsible for.
/// </summary>
public sealed record PortfolioDef(string Id, double Value, IReadOnlyList<string> Policies);

/// <summary>A demand already written into the starting coalition agreement (party id, demand id).</summary>
public sealed record AgreementDef(string Party, string Demand);

public sealed record ScenarioPoliticsDef(
    string PlayerParty, IReadOnlyDictionary<string, double> Support,
    IReadOnlyList<string> Government, string? Leader, int TurnsToElection,
    IReadOnlyList<AgreementDef>? Agreements = null, double StartNoise = 0);

/// <summary>Parameters are model parameters (e.g. alpha, delta) for the country's economy model.</summary>
public sealed record CountryDef(
    string Id, string NameKey, IReadOnlyDictionary<string, double> Parameters, string Source,
    CountryPoliticsDef? Politics = null);

public sealed record ScenarioDef(
    string Id, string CountryId, int StartYear, int TurnsPerYear,
    IReadOnlyDictionary<string, double> StartValues,
    IReadOnlyDictionary<string, double> StartPolicies,
    string Source,
    ScenarioPoliticsDef? Politics = null);
