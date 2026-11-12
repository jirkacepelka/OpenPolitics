namespace OpenPolitics.Core.Gameplay;

public abstract record PlayerAction;

/// <summary>Set a policy to a value in its own unit. Takes effect after the policy's delay.</summary>
public sealed record SetPolicy(string PolicyId, double Value) : PlayerAction;

/// <param name="ErrorKey">Localization key such as error.unknown_policy; null when accepted.</param>
public sealed record ActionResult(bool Accepted, string? ErrorKey, string? Detail)
{
    public static ActionResult Ok { get; } = new(true, null, null);
}

/// <summary>A policy change that was accepted but has not taken effect yet.</summary>
public sealed record PendingPolicy(string PolicyId, double Value, int EffectiveTurn);
