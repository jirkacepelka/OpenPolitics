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

/// <summary>As opposition: call a vote of no confidence. Costs 10 capital; passes with enough willing parties and unhappy partners.</summary>
public sealed record NoConfidence : PoliticsAction;

/// <summary>As formateur: offer a coalition. Partners join and the agreements become the government's commitments.</summary>
public sealed record ProposeCoalition(IReadOnlyList<string> Partners, IReadOnlyList<AgreedDemand> Agreements) : PoliticsAction;

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

public enum Stance { Support, Oppose }

/// <summary>As opposition: publicly criticise how a value developed over the last four turns.</summary>
public sealed record Criticize(string ValueId) : PoliticsAction;

/// <summary>As opposition: support or oppose a measure the government took (id from the party panel).</summary>
public sealed record TakeStance(string MeasureId, Stance Stance) : PoliticsAction;

/// <param name="ErrorKey">Localization key such as error.unknown_policy; null when accepted.</param>
public sealed record ActionResult(bool Accepted, string? ErrorKey, string? Detail)
{
    public static ActionResult Ok { get; } = new(true, null, null);
}

/// <summary>A policy change that was accepted but has not taken effect yet.</summary>
public sealed record PendingPolicy(string PolicyId, double Value, int EffectiveTurn);
