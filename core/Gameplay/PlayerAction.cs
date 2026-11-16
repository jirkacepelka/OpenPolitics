using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Gameplay;

public abstract record PlayerAction;

/// <summary>Actions of the political layer (formation, party management); refused when the scenario has no politics.</summary>
public abstract record PoliticsAction : PlayerAction;

/// <summary>Set a policy to a value in its own unit. Takes effect after the policy's delay.</summary>
public sealed record SetPolicy(string PolicyId, double Value) : PlayerAction;

/// <summary>Choose how a spending policy follows the economy: "gdp" (share of GDP, default), "inflation" (fixed real amount) or "none" (fixed nominal amount).</summary>
public sealed record SetIndexation(string PolicyId, string Mode) : PlayerAction;

/// <summary>As a governing partner (not the leader): ask the leader to move a policy toward Target. The leader decides next turn.</summary>
public sealed record ProposePolicy(string PolicyId, double Target) : PoliticsAction;

/// <summary>As a governing partner: leave the government at the end of the turn.</summary>
public sealed record LeaveCoalition : PoliticsAction;

/// <summary>As government leader: dismiss a partner (allowed while the rest keep a majority). Costs 4 capital.</summary>
public sealed record DismissPartner(string PartyId) : PoliticsAction;

/// <summary>
/// As government leader: reshuffle the coalition (decision 0013) - drop partners, bring in seated outside parties with the agreements they ask
/// for, or both, as long as the new coalition keeps a majority. Paid when accepted (<see cref="Politics.PoliticsLimits.ReshuffleBaseCost"/> plus
/// <see cref="Politics.PoliticsLimits.ReshufflePerParty"/> per party changed); the parties answer at the end of the quarter by the formation rules.
/// A refusal refunds all but <see cref="Politics.PoliticsLimits.ReshuffleRefusalCost"/>. Once per <see cref="Politics.PoliticsLimits.ReshuffleCooldownTurns"/> quarters.
/// </summary>
public sealed record ReshuffleCoalition(IReadOnlyList<string> Drop, IReadOnlyList<string> Add, IReadOnlyList<AgreedDemand> Agreements) : PoliticsAction;

/// <summary>As opposition: call a vote of no confidence. Costs 10 capital; passes with enough willing parties and unhappy partners.</summary>
public sealed record NoConfidence : PoliticsAction;

/// <summary>
/// As formateur: offer a coalition and form it at once if every party accepts (otherwise refused with the first reason). Partners join, the
/// agreements become the government's commitments, the portfolios go to the partners and the concessions are paid (decision 0016).
/// </summary>
public sealed record ProposeCoalition(IReadOnlyList<string> Partners, IReadOnlyList<AgreedDemand> Agreements,
    IReadOnlyList<PortfolioGrant>? Portfolios = null, IReadOnlyList<Concession>? Concessions = null) : PoliticsAction
{
    /// <summary>Forms exactly the given coalition (for example the one <see cref="Game.SuggestCoalition"/> proposes).</summary>
    public static ProposeCoalition From(CoalitionProposal p) => new(p.Partners, p.Agreements, p.Portfolios, p.Concessions);
}

/// <summary>
/// As formateur, one round of coalition talks (decision 0016): every invited party answers at once - accept, counter (the terms it would take) or
/// refuse with a reason. All accept: the government forms. Otherwise the answers are shown and the formateur can improve the offer;
/// <see cref="Politics.PoliticsLimits.NegotiationRoundsPerQuarter"/> rounds a quarter.
/// </summary>
public sealed record NegotiateCoalition(IReadOnlyList<string> Partners, IReadOnlyList<AgreedDemand> Agreements,
    IReadOnlyList<PortfolioGrant>? Portfolios = null, IReadOnlyList<Concession>? Concessions = null) : PoliticsAction;

/// <summary>
/// As a party invited into an AI formateur's coalition (decision 0016): "we join if you also add this" - extra demands of the player's party and/or
/// portfolios for it. The formateur answers at once: accepted, the government forms on these terms; refused, the offer stands. One per offer.
/// </summary>
public sealed record CounterOffer(IReadOnlyList<AgreedDemand> Agreements, IReadOnlyList<PortfolioGrant> Portfolios) : PoliticsAction;

/// <summary>Accept the AI formateur's offer to join its coalition.</summary>
public sealed record AcceptOffer : PoliticsAction;

/// <summary>Decline the AI formateur's offer; it continues without the player's party.</summary>
public sealed record RejectOffer : PoliticsAction;

/// <summary>As government leader: bring the next election forward to the end of this turn.</summary>
public sealed record CallEarlyElection : PoliticsAction;

/// <summary>Spend capital to build the party's organisation (+0.04).</summary>
public sealed record BuildOrganization : PoliticsAction;

/// <summary>Spend capital to improve relations with another party (+0.1).</summary>
public sealed record Court(string PartyId) : PoliticsAction;

/// <summary>Move the party's position on a policy toward Target by at most 10 % of the policy's range.</summary>
public sealed record ShiftPlatform(string PolicyId, double Target) : PoliticsAction;

/// <summary>Win over a voter group; Effort is 1 to 3 and costs 3 per level.</summary>
public sealed record Campaign(string GroupId, int Effort) : PoliticsAction;

/// <summary>Change how much the party stresses a policy of its platform (weight 0 to 1, by at most 0.2 at a time; costs 1 capital).</summary>
public sealed record SetPriority(string PolicyId, double Weight) : PoliticsAction;

/// <summary>Promise voters one of the party's own demands before an election (within 8 turns of it; at most two; costs 2 capital).</summary>
public sealed record MakePromise(string DemandId) : PoliticsAction;

public enum Stance { Support, Oppose }

/// <summary>As opposition: publicly criticise how a value developed over the last four turns.</summary>
public sealed record Criticize(string ValueId) : PoliticsAction;

/// <summary>As opposition: support or oppose a measure the government took (id from the party panel).</summary>
public sealed record TakeStance(string MeasureId, Stance Stance) : PoliticsAction;

/// <summary>Support the open proposal (decision 0014; id from the briefing or the politics screen). Free; once per proposal.</summary>
public sealed record SupportProposal(string ProposalId) : PoliticsAction;

/// <summary>Oppose the open proposal (decision 0014). Free; once per proposal. A pivotal partner's no makes a measure fail.</summary>
public sealed record OpposeProposal(string ProposalId) : PoliticsAction;

/// <summary>
/// Decision 0014 (g): any party that does not lead the government announces publicly what it would do with a policy. Costs
/// <see cref="Politics.PoliticsLimits.AnnounceCost"/>; voter groups that would like the change warm to the party, the others cool; contradicting the
/// party's own programme costs credibility. One a quarter, the same policy again after a cooldown. An AI-led government may adopt a popular one.
/// </summary>
public sealed record AnnouncePolicy(string PolicyId, double Target) : PoliticsAction;

/// <summary>
/// Decision 0016 (1): as a junior partner, give the leader an ultimatum - deliver within <paramref name="Deadline"/> quarters
/// (<see cref="Politics.PoliticsLimits.UltimatumMinDeadline"/>..<see cref="Politics.PoliticsLimits.UltimatumMaxDeadline"/>) or the party leaves the
/// government. Kind "demand": SubjectId is one of the party's own demands (agreed or not; a value goal is asked as a move of the policy that drives
/// it most); kind "policy": SubjectId is a policy of the party's programme (asked to move toward it by at most a quarter of its range). Costs
/// <see cref="Politics.PoliticsLimits.UltimatumCost"/>; one at a time; the leader answers at the end of the quarter.
/// </summary>
public sealed record IssueUltimatum(string Kind, string SubjectId, int Deadline) : PoliticsAction;

/// <summary>Withdraw the party's open ultimatum (decision 0016): costs <see cref="Politics.PoliticsLimits.UltimatumWithdrawCredibility"/> credibility.</summary>
public sealed record WithdrawUltimatum : PoliticsAction;

/// <param name="ErrorKey">Localization key such as error.unknown_policy; null when accepted.</param>
public sealed record ActionResult(bool Accepted, string? ErrorKey, string? Detail)
{
    public static ActionResult Ok { get; } = new(true, null, null);
}

/// <summary>A policy change that was accepted but has not taken effect yet.</summary>
public sealed record PendingPolicy(string PolicyId, double Value, int EffectiveTurn);
