namespace OpenPolitics.Core.Politics;

/// <summary>Rules of the political game that clients also need to show (when a button is available).</summary>
public static class PoliticsLimits
{
    /// <summary>Promises can be made this many turns before an election; each is judged at the election after the next term (decision 0015).</summary>
    public const int PromiseWindowTurns = 8;

    /// <summary>Policy ids for the electoral rules (percent of the vote; 0 = D'Hondt, 1 = Sainte-Lague, 2 = Hare quota).</summary>
    public const string ThresholdPolicy = "base:electoral_threshold", MethodPolicy = "base:electoral_method";

    public static readonly string[] Methods = { "dhondt", "sainte_lague", "hare" };

    /// <summary>At most this many promises in the window before each election (promises carried over from the previous one do not count; decision 0015).</summary>
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

    /// <summary>
    /// Proposals (decision 0014): after one is settled, the next may come ProposalCooldownTurns quarters later at the earliest. Answering moves
    /// credibility by up to ProposalCredibilityGain (agreeing with the own programme) or ProposalCredibilityLoss (against it), scaled by the fit; the
    /// relation with the proposer by ProposalRelationSupport / ProposalRelationOppose (a partner who opposes its own government: also
    /// ProposalPartnerStrain; a measure blocked by the player's seats: ProposalBlockedHit); groups that like or dislike the change by
    /// ProposalWarmth (scaled by their size). A partner whose request the leading player supports gains ProposalLoyaltyGain loyalty, one turned
    /// down loses ProposalLoyaltyLoss.
    /// </summary>
    public const int ProposalCooldownTurns = 2;
    public const double ProposalCredibilityGain = 0.05, ProposalCredibilityLoss = 0.08;
    public const double ProposalRelationSupport = 0.05, ProposalRelationOppose = 0.03, ProposalPartnerStrain = 0.05, ProposalBlockedHit = 0.1;
    public const double ProposalWarmth = 0.04, ProposalLoyaltyGain = 4, ProposalLoyaltyLoss = 3;

    /// <summary>
    /// Announced policy proposals (decision 0014 g): cost AnnounceCost; each voter group that cares about the policy moves by up to AnnounceWarmth
    /// momentum (full effect for a change that brings the policy a tenth of its range closer to or further from the group's wish, scaled by how much the
    /// group cares relative to the group that cares most); against the party's own programme credibility falls by AnnounceCredibilityLoss times the
    /// platform weight. One announcement a quarter; the same policy again after AnnounceCooldownTurns quarters. When the size-weighted approval is above
    /// AnnouncePopularity and the government is AI-led, it adopts the proposal at the end of the quarter with probability AnnounceAdoptChance (at most a
    /// quarter of the range at once, never against a strong position of the leader or across a member's red line).
    /// </summary>
    /// <summary>Decision 0015: a partner's proposal to the leader costs this (an opposition party's public proposal costs <see cref="AnnounceCost"/>).</summary>
    public const double ProposeToLeaderCost = 3;

    public const double AnnounceCost = 2, AnnounceWarmth = 0.06, AnnounceCredibilityLoss = 0.05, AnnouncePopularity = 0.02, AnnounceAdoptChance = 0.2;
    public const int AnnounceCooldownTurns = 4;

    /// <summary>Decision 0015: the quarter's news reports the groups where the player's support moved by at least this many points.</summary>
    public const double SupportNewsPoints = 0.5;

    /// <summary>The chance that an AI leader patches a majority lost to a walk-out with outside parties (when such a patch exists) instead of falling.</summary>
    public const double AiPatchChance = 0.5;
}
