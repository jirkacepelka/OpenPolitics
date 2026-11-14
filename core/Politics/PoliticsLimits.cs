namespace OpenPolitics.Core.Politics;

/// <summary>Rules of the political game that clients also need to show (when a button is available).</summary>
public static class PoliticsLimits
{
    /// <summary>Promises can be made this many turns before an election.</summary>
    public const int PromiseWindowTurns = 8;

    /// <summary>Policy ids for the electoral rules (percent of the vote; 0 = D'Hondt, 1 = Sainte-Lague, 2 = Hare quota).</summary>
    public const string ThresholdPolicy = "base:electoral_threshold", MethodPolicy = "base:electoral_method";

    public static readonly string[] Methods = { "dhondt", "sainte_lague", "hare" };

    /// <summary>At most this many promises per term.</summary>
    public const int MaxPromises = 2;
}
