namespace OpenPolitics.Core.Politics;

/// <summary>Rules of the political game that clients also need to show (when a button is available).</summary>
public static class PoliticsLimits
{
    /// <summary>Promises can be made this many turns before an election.</summary>
    public const int PromiseWindowTurns = 8;

    /// <summary>At most this many promises per term.</summary>
    public const int MaxPromises = 2;
}
