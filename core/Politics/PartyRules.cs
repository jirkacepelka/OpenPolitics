namespace OpenPolitics.Core.Politics;

/// <summary>
/// The numbers behind the player's party stats and actions (decision 0014 f). The engine uses exactly these constants, and the in-game explanations
/// are built from them, so what the UI says is what the game does.
/// </summary>
public static class PartyRules
{
    // organisation: 0..1
    public const double OrgDecay = 0.01, OrgBuildCost = 4, OrgBuildGain = 0.04, OrgIncome = 8.0, OrgTurnout = 0.2;
    // credibility: 0..1
    public const double CredibilityRecoveryPerYear = 0.1;
    // relations: 0..1, drift back toward 0.3
    public const double RelationDrift = 0.02, CourtCost = 3, CourtGain = 0.1;
    // platform
    /// <summary>The programme holds at least <see cref="PlatformMin"/> and at most <see cref="PlatformMax"/> laws; a law added starts with weight <see cref="AddWeight"/>.</summary>
    public const int PlatformMin = 2, PlatformMax = 30;
    public const double AddCost = 1, AddWeight = 0.4, RemoveCost = 2, RemoveCredibility = 0.1;
    public const double ShiftCost = 1, ShiftMaxOfRange = 0.1, ShiftCredibility = 0.5, PriorityCost = 1, PriorityStep = 0.2, PriorityCredibility = 0.1;
    // promises
    public const double PromiseCost = 2, PromiseMomentum = 0.03, PromiseKeptCredibility = 0.1, PromiseBrokenCredibility = 0.2;
    // campaigning and opposition work
    public const double CampaignCostPerLevel = 3, CampaignMomentumPerLevel = 0.06, CampaignElectionBoost = 2.0;
    public const int CampaignElectionTurns = 4;
    public const double CriticizeCost = 2, CriticizeMomentum = 0.15, CriticizePressure = 0.10, CriticizeBackfire = 0.10;
    public const double StanceCost = 1, StanceMomentum = 0.10;
    public const double NoConfidenceCost = 10, DismissCost = 4, LeaveCredibility = 0.15;
    /// <summary>Momentum and opposition pressure halve every this many quarters.</summary>
    public const int MomentumHalfLifeTurns = 4;
}
