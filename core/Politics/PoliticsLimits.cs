namespace OpenPolitics.Core.Politics;

/// <summary>Rules of the political game that clients also need to show (when a button is available).</summary>
public static class PoliticsLimits
{
    /// <summary>Promises can be made this many turns before an election; each is judged at the election after the next term (decision 0015).</summary>
    public const int PromiseWindowTurns = 16;

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

    /// <summary>
    /// The partner's ultimatum to the leader (decision 0016): costs UltimatumCost, a deadline of UltimatumMinDeadline..UltimatumMaxDeadline quarters,
    /// one at a time, the next UltimatumCooldownTurns quarters after one is settled. A policy ultimatum asks at most UltimatumMaxMoveOfRange of the
    /// range toward the programme; a value goal is asked as a move of UltimatumGoalStepOfRange on its strongest lever. The leader's score (relation,
    /// fit with its platform, how much it needs the party, the party's weight, the size of the move, its grudge) delivers at UltimatumDeliverAt,
    /// refuses below UltimatumRefuseBelow, otherwise stalls and gains UltimatumStallGain a quarter as the deadline nears. Delivered: credibility
    /// +UltimatumDeliveredCredibility, the party's voters +UltimatumVoterMomentum, the relation with the leader -UltimatumRelationDelivered. Refused:
    /// relation -UltimatumRelationRefused. Carried out: the party leaves (as a walk-out) and loses UltimatumCarriedOutCredibility (less than an
    /// unannounced exit). Withdrawn: -UltimatumWithdrawCredibility.
    /// </summary>
    public const double UltimatumCost = 4, UltimatumMaxMoveOfRange = 0.25, UltimatumGoalStepOfRange = 0.2;
    public const int UltimatumMinDeadline = 2, UltimatumMaxDeadline = 6, UltimatumCooldownTurns = 8;
    public const double UltimatumDeliverAt = 0.55, UltimatumRefuseBelow = 0.3, UltimatumStallGain = 0.08;
    public const double UltimatumDeliveredCredibility = 0.05, UltimatumVoterMomentum = 0.03, UltimatumRelationDelivered = 0.05, UltimatumRelationRefused = 0.05;
    public const double UltimatumCarriedOutCredibility = 0.05, UltimatumWithdrawCredibility = 0.03;

    /// <summary>A partner that holds the portfolio of a policy is heard more on it: the leader's score for its proposals and ultimatums rises by this.</summary>
    public const double PortfolioProposalBonus = 0.15;

    /// <summary>An AI partner's loyalty target rises by PortfolioLoyalty per cabinet portfolio it holds (at most PortfolioLoyaltyMax).</summary>
    public const double PortfolioLoyalty = 2, PortfolioLoyaltyMax = 4;

    /// <summary>
    /// Formation talks (decision 0016): the player as formateur has NegotiationRoundsPerQuarter offers a quarter; a partner that refuses an offer
    /// loses NegotiationPatience relation (impatience); a concession unit also raises the relation by ConcessionRelation when the government forms.
    /// A partner's counter-offer is accepted while what it gets stays under its price × (1 + CounterSlack), plus CounterPivotalBonus when the
    /// formateur has no coalition without it; never against a formateur position weighted CounterPlatformWeight or more.
    /// </summary>
    public const int NegotiationRoundsPerQuarter = 3;
    public const double NegotiationPatience = 0.02, ConcessionRelation = 0.05;
    public const double CounterSlack = 0.5, CounterPivotalBonus = 2, CounterPlatformWeight = 0.5;

    /// <summary>The chance that an AI leader patches a majority lost to a walk-out with outside parties (when such a patch exists) instead of falling.</summary>
    public const double AiPatchChance = 0.5;
}
