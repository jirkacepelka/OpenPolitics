using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>The player's role in the current government.</summary>
public enum Role { Leader, Partner, Opposition }

/// <summary>
/// A demand of <see cref="PartyId"/> written into the coalition agreement. Target, when set, is a counter value the formateur offered instead of the
/// demand's own target (decision 0016: between its own platform position and the demand's target; it pays part of the demand's weight).
/// </summary>
public sealed record AgreedDemand(string PartyId, string DemandId, double? Target = null);

/// <summary>
/// A formateur's offer: who joins and which demands are agreed with them; since decision 0016 also the cabinet portfolios given to partners and the
/// relation-building concessions the formateur pays for.
/// </summary>
public sealed record CoalitionProposal(string Formateur, IReadOnlyList<string> Partners, IReadOnlyList<AgreedDemand> Agreements,
    IReadOnlyList<PortfolioGrant>? Portfolios = null, IReadOnlyList<Concession>? Concessions = null)
{
    public IReadOnlyList<PortfolioGrant> PortfolioList => Portfolios ?? Array.Empty<PortfolioGrant>();
    public IReadOnlyList<Concession> ConcessionList => Concessions ?? Array.Empty<Concession>();
}

/// <summary>Why a coalition proposal does not work. Reason is one of slots, majority, red_line, contradiction, price, unknown, grudge, distance, portfolios.</summary>
public sealed record Refusal(string Reason, string? PartyId, string? DemandId);

/// <summary>
/// An agreed demand with the value it started from and the turn by which it is expected to be delivered. StartTurn is -1 for demands agreed at
/// formation, else the turn it was agreed later (a mid-term demand, or the agreement with a partner brought in by a reshuffle). MidTerm marks
/// mid-term demands, which have their own reward and penalty instead of counting toward the partner's gradual loyalty.
/// </summary>
public sealed record Commitment(string PartyId, DemandDef Demand, double Baseline, int DueTurn, int StartTurn = -1, bool MidTerm = false);

/// <summary>
/// The partners that stay in a government being reshuffled and the agreements already in force. With it, <see cref="CoalitionRules.Evaluate"/>
/// judges only what is new: the newcomers' price, and grudges, red lines and contradictions that involve a newcomer or a new agreement.
/// </summary>
public sealed record SittingCoalition(IReadOnlyList<string> Kept, IReadOnlyList<AgreedDemand> Existing);

/// <summary>
/// A formed government. Parties are ordinal-sorted and include the leader. Portfolios: the cabinet portfolios held by partners (portfolio id → party,
/// decision 0016); the leader holds every other one.
/// </summary>
public sealed record Government(string Leader, IReadOnlyList<string> Parties, IReadOnlyList<Commitment> Commitments, int FormedTurn,
    IReadOnlyDictionary<string, string>? Portfolios = null)
{
    public IReadOnlyDictionary<string, string> PortfolioHolders => Portfolios ?? new Dictionary<string, string>();
}
