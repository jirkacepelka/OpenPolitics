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

    /// <summary>Capital cannot be banked without limit: a party that never acts does not stockpile an unbeatable campaign.</summary>
    public const double MaxCapital = 30.0;

    /// <summary>
    /// Political capital per quarter: every party gets BaseIncome (+ 8 x organisation x poll share); the leader of the government LeaderIncome on
    /// top; a coalition is worth up to +-CoalitionIncome (leader: partners' mean loyalty, partner: relation with the leader). Decision 0013 raised the
    /// base (was 2) and cut the governing extras (were 2 and 3) because policy changes now cost capital.
    /// </summary>
    public const double BaseIncome = 3.5, LeaderIncome = 1.0, CoalitionIncome = 2.0;

    /// <summary>
    /// Coalition reshuffle (decision 0013): costs ReshuffleBaseCost + ReshufflePerParty per party dropped or added, paid when the leader asks; a refusal
    /// refunds all but ReshuffleRefusalCost. After a reshuffle the next one waits ReshuffleCooldownTurns quarters (ReshuffleRefusedCooldownTurns after
    /// a refusal). A dropped party resents the leader by ReshuffleGrudge (it refuses to sit with it while that is above the grudge veto) and its
    /// relation with the player falls by ReshuffleRelationHit; partners that stay lose ReshuffleLoyaltyHit loyalty.
    /// </summary>
    public const double ReshuffleBaseCost = 3, ReshufflePerParty = 2, ReshuffleRefusalCost = 2;
    public const int ReshuffleCooldownTurns = 8, ReshuffleRefusedCooldownTurns = 2;
    public const double ReshuffleGrudge = 0.9, ReshuffleRelationHit = 0.2, ReshuffleLoyaltyHit = 5;

    /// <summary>The chance that an AI leader patches a majority lost to a walk-out with outside parties (when such a patch exists) instead of falling.</summary>
    public const double AiPatchChance = 0.5;
}
