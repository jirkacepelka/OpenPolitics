namespace OpenPolitics.Core.Politics;

/// <summary>Governing; Formation (formateur attempts, old government caretaking); Caretaker (attempts failed, early elections ahead).</summary>
public enum Phase { Governing, Formation, Caretaker }

/// <summary>Immutable political snapshot: latest poll, current seats, the government and the player's party.</summary>
public sealed record CountryPolitics(
    Phase Phase, int TurnsToElection,
    IReadOnlyDictionary<string, double> Support,
    IReadOnlyDictionary<string, int> Seats,
    IReadOnlyList<string> GovernmentParties,
    string? Leader, string PlayerParty,
    Government? Government = null, FormationInfo? Formation = null, Role PlayerRole = Role.Opposition,
    IReadOnlyDictionary<string, double>? Loyalty = null, IReadOnlyList<CommitmentStatus>? Commitments = null,
    IReadOnlyList<UltimatumInfo>? Ultimatums = null);

/// <summary>How far an agreed demand is delivered (Progress) against how far it should be by now (Expected), both 0..1.</summary>
public sealed record CommitmentStatus(string PartyId, string DemandId, double Progress, double Expected);

/// <summary>A partner's deadline: deliver the named demand (or win back its trust) before TurnsLeft runs out.</summary>
public sealed record UltimatumInfo(string PartyId, string? DemandId, int TurnsLeft);

/// <summary>An attempt to form a government. Offer is set while the AI's coalition waits for the player's answer.</summary>
public sealed record FormationInfo(string Formateur, int Attempt, int TurnsLeft, bool PlayerIsFormateur, CoalitionProposal? Offer);

public abstract record PoliticalEvent(int Turn);

/// <summary>An election took place: the votes (shares summing to 1) and the resulting seats.</summary>
public sealed record ElectionHeld(
    int Turn, IReadOnlyDictionary<string, double> Votes, IReadOnlyDictionary<string, int> Seats) : PoliticalEvent(Turn);

public sealed record FormationStarted(int Turn, string Formateur) : PoliticalEvent(Turn);
public sealed record FormationFailed(int Turn, string Formateur) : PoliticalEvent(Turn);
public sealed record OfferMade(int Turn, CoalitionProposal Proposal) : PoliticalEvent(Turn);
public sealed record OfferLapsed(int Turn) : PoliticalEvent(Turn);
public sealed record GovernmentFormed(int Turn, Government Government) : PoliticalEvent(Turn);
public sealed record EarlyElectionsCalled(int Turn) : PoliticalEvent(Turn);
public sealed record LoyaltyWarning(int Turn, string PartyId, string? DemandId) : PoliticalEvent(Turn);
public sealed record Ultimatum(int Turn, string PartyId, string? DemandId, int TurnsLeft) : PoliticalEvent(Turn);
public sealed record PartnerLeft(int Turn, string PartyId) : PoliticalEvent(Turn);
