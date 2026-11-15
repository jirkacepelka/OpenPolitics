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
    IReadOnlyList<UltimatumInfo>? Ultimatums = null, PartyStatus? Party = null,
    IReadOnlyDictionary<string, double>? Grudge = null, ReshuffleInfo? Reshuffle = null);

/// <summary>When the player's government can be reshuffled again (0 = now) and whether a reshuffle waits for the end of this quarter.</summary>
public sealed record ReshuffleInfo(int CooldownLeft, bool Queued);

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
public sealed record GovernmentFell(int Turn) : PoliticalEvent(Turn);
/// <summary>A partner raised a new demand during the term; it must be delivered within DueTurns or loyalty suffers.</summary>
public sealed record DemandRaised(int Turn, string PartyId, string DemandId, int DueTurns) : PoliticalEvent(Turn);
public sealed record DemandMet(int Turn, string PartyId, string DemandId) : PoliticalEvent(Turn);
public sealed record DemandFailed(int Turn, string PartyId, string DemandId) : PoliticalEvent(Turn);
/// <summary>The player's party kept (or broke) a promise it made before the election.</summary>
public sealed record PromiseKept(int Turn, string PartyId, string DemandId) : PoliticalEvent(Turn);
public sealed record PromiseBroken(int Turn, string PartyId, string DemandId) : PoliticalEvent(Turn);
/// <summary>The player's party left the government.</summary>
public sealed record PlayerLeftCoalition(int Turn) : PoliticalEvent(Turn);
/// <summary>The player, as leader, dismissed a partner.</summary>
public sealed record PartnerDismissed(int Turn, string PartyId) : PoliticalEvent(Turn);
/// <summary>The government was reshuffled by its leader (the player or an AI leader): Dropped left it, Added joined with fresh agreements.</summary>
public sealed record CoalitionReshuffled(int Turn, string Leader, IReadOnlyList<string> Dropped, IReadOnlyList<string> Added) : PoliticalEvent(Turn);
/// <summary>The player's reshuffle was refused: Reason as in <see cref="Refusal"/> (price, grudge, red_line, contradiction, majority, ...).</summary>
public sealed record ReshuffleRefused(int Turn, string Reason, string? PartyId) : PoliticalEvent(Turn);
public sealed record NoConfidencePassed(int Turn) : PoliticalEvent(Turn);
public sealed record NoConfidenceFailed(int Turn) : PoliticalEvent(Turn);
/// <summary>The leader agreed to move a policy toward the player's proposal (the move itself follows as a government measure).</summary>
public sealed record PolicyProposalAccepted(int Turn, string PolicyId, double Target) : PoliticalEvent(Turn);
/// <summary>The leader turned the player's proposal down; Reason is "score" or "red_line".</summary>
public sealed record PolicyProposalRejected(int Turn, string PolicyId, string Reason) : PoliticalEvent(Turn);
/// <summary>A policy change made by the government's AI governor (not by the player).</summary>
public sealed record GovernmentMeasure(int Turn, string PolicyId, double From, double To) : PoliticalEvent(Turn);
