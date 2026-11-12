using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Gameplay;

public abstract record PlayerAction;

/// <summary>Set a policy to a value in its own unit. Takes effect after the policy's delay.</summary>
public sealed record SetPolicy(string PolicyId, double Value) : PlayerAction;

/// <summary>As formateur: offer a coalition. Partners join and the agreements become the government's commitments.</summary>
public sealed record ProposeCoalition(IReadOnlyList<string> Partners, IReadOnlyList<AgreedDemand> Agreements) : PlayerAction;

/// <summary>Accept the AI formateur's offer to join its coalition.</summary>
public sealed record AcceptOffer : PlayerAction;

/// <summary>Decline the AI formateur's offer; it continues without the player's party.</summary>
public sealed record RejectOffer : PlayerAction;

/// <summary>As government leader: bring the next election forward to the end of this turn.</summary>
public sealed record CallEarlyElection : PlayerAction;

/// <param name="ErrorKey">Localization key such as error.unknown_policy; null when accepted.</param>
public sealed record ActionResult(bool Accepted, string? ErrorKey, string? Detail)
{
    public static ActionResult Ok { get; } = new(true, null, null);
}

/// <summary>A policy change that was accepted but has not taken effect yet.</summary>
public sealed record PendingPolicy(string PolicyId, double Value, int EffectiveTurn);
