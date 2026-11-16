using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>A cabinet portfolio given to a coalition partner in formation talks (decision 0016).</summary>
public sealed record PortfolioGrant(string PartyId, string PortfolioId);

/// <summary>Relation-building concessions the formateur pays political capital for, worth <see cref="Negotiation.ConcessionWorth"/> each toward the partner's price.</summary>
public sealed record Concession(string PartyId, int Units);

/// <summary>
/// The terms of coalition talks that depend on who is talking (decision 0016): the price multiplier of a partner toward a formateur (ideological
/// distance, strength, alternatives), whether they are too far apart to sit together at all, and the cabinet portfolios that can be offered.
/// Null in <see cref="CoalitionRules.Evaluate"/> and <see cref="CoalitionBuilder"/> means the plain rules of decision 0003 (factor 1, no veto, no portfolios).
/// </summary>
public sealed record Talks(Func<string, string, double> Factor, Func<string, string, bool> Veto, IReadOnlyList<PortfolioDef> Portfolios, bool AllowPortfolios = true)
{
    public PortfolioDef? Portfolio(string id) => Portfolios.FirstOrDefault(p => p.Id == id);
}

/// <summary>
/// How a partner sees one of its demands in the formateur's offer: "acceptable" (the formateur can give it), "negotiable" (the formateur's own
/// platform pulls the other way, so a half-way value can be offered: HalfWay, between Anchor - the formateur's position - and the demand's target),
/// "red_line" (it crosses a red line of RedLineOf, a coalition member), "met" (a value goal that already holds: worth nothing). Worth is what it pays
/// toward the price as offered (AgreedTarget: the counter value offered, null for the full demand).
/// </summary>
public sealed record DemandView(string DemandId, string Status, double Weight, double Worth, double? Anchor, double? HalfWay, bool Agreed, double? AgreedTarget,
    string? RedLineOf);

/// <summary>A portfolio as a partner sees it: Worth toward its price (more for a partner whose programme stresses its policies), HeldBy in this offer.</summary>
public sealed record PortfolioView(string PortfolioId, double Worth, string? HeldBy);

/// <summary>
/// What would change a partner's answer: "add_demand" (DemandId, Amount = its worth), "half_way" (DemandId, Amount = the half-way value),
/// "give_portfolio" (PortfolioId, Amount = its worth), "concede" (Amount = units), "drop_demand" (DemandId of PartyId crossing the partner's red
/// line), "court" (raise the relation), "wait" (a grudge must fade).
/// </summary>
public sealed record Fix(string Kind, string? PartyId = null, string? DemandId = null, string? PortfolioId = null, double Amount = 0);

/// <summary>A partner's counter-offer: the agreements and portfolios it asks for itself instead of what was offered.</summary>
public sealed record CounterTerms(IReadOnlyList<AgreedDemand> Agreements, IReadOnlyList<PortfolioGrant> Portfolios);

/// <summary>
/// One partner in the talks (decision 0016): its price (BasePrice from the pack, Required after relation, grudge and the talk factors), what the
/// offer pays (Offered; Satisfaction = Offered / Required), its answer ("accept", "counter", "refuse"), why (Reason: price, red_line, grudge,
/// distance, contradiction) with the Shortfall in price points, the factors behind the price, its demands and portfolios as it sees them, the
/// counter-offer it would make and what would fix a refusal.
/// </summary>
public sealed record PartnerView(
    string PartyId, int Seats, double BasePrice, double Required, double Offered, double Satisfaction, string Verdict, string? Reason, string? ReasonDemand,
    double Shortfall, double Distance, double DistanceFactor, double StrengthFactor, double AlternativesFactor, bool HasAlternative, double Relation, double Grudge,
    IReadOnlyList<DemandView> Demands, IReadOnlyList<PortfolioView> Portfolios, int Concessions, CounterTerms? Counter, IReadOnlyList<Fix> Fixes);

/// <summary>
/// The whole offer as the parties see it: per partner, seats held and needed, program slots and portfolios used, the capital the concessions cost,
/// a hard error that stops it before anyone answers (HardError: not formateur, majority, slots, portfolios, unknown, no rounds left), whether every
/// partner accepts and how many negotiation rounds are left this quarter.
/// </summary>
public sealed record NegotiationView(IReadOnlyList<PartnerView> Partners, int SeatsHeld, int SeatsNeeded, int SlotsUsed, int Slots, int PortfoliosGiven,
    int PortfoliosMax, double ConcessionCost, string? HardError, string? HardDetail, bool AllAccept, int RoundsLeft);

/// <summary>A partner's answer in the last round of talks (shown in the formation snapshot).</summary>
public sealed record PartnerReply(string PartyId, string Verdict, string? Reason, string? ReasonDemand, double Shortfall, CounterTerms? Counter);

/// <summary>
/// The AI formateur's answer to the player's counter-offer as a partner ("I join if you also add X"): Accepts, or Reason ("red_line", "slots",
/// "contradiction", "platform" - against a strong position of the formateur -, "too_much" - more than it would pay -, "used" - one counter per offer).
/// Worth: what the player's party would get, Required: its price, Ceiling: the most the formateur pays.
/// </summary>
public sealed record CounterPreview(bool Accepts, string? Reason, double Worth, double Required, double Ceiling, string? ErrorKey);

/// <summary>
/// Formation talks that respect party differences (decision 0016, item 2): pure math shared by the AI formateur, the player's negotiation and the
/// client's preview. A partner's price is its pack price × (1 - relation / 2) × (1 + grudge) × <see cref="DistanceFactor"/> ×
/// <see cref="StrengthFactor"/> × <see cref="AlternativesFactor"/>; agreed demands pay their weight (a counter value part of it), portfolios their
/// <see cref="PortfolioWorth"/>, concessions <see cref="ConcessionWorth"/> each.
/// </summary>
public static class Negotiation
{
    /// <summary>
    /// Talk distance = <see cref="PlatformShare"/> × platform distance + (1 - PlatformShare) × compass distance / 2√2. The distance factor is
    /// <c>clamp(1 + DistanceSlope × (d - DistanceCentre), DistanceMin, DistanceMax)</c>; at <see cref="DistanceVeto"/> or more the parties refuse to
    /// sit together whatever is offered.
    /// </summary>
    public const double PlatformShare = 0.7, DistanceSlope = 1.5, DistanceCentre = 0.12, DistanceMin = 0.75, DistanceMax = 2.2, DistanceVeto = 0.42;

    /// <summary>A partner with more seats asks more: <c>clamp(StrengthBase + StrengthPerRatio × partner seats / formateur seats, StrengthMin, StrengthMax)</c>.</summary>
    public const double StrengthBase = 0.85, StrengthPerRatio = 0.25, StrengthMin = 0.85, StrengthMax = 1.25;

    /// <summary>A partner that could form another majority without the formateur asks more (×AlternativeYes), one that could not less (×AlternativeNo).</summary>
    public const double AlternativeYes = 1.05, AlternativeNo = 0.95, AlternativeDistance = 0.3;
    public const int AlternativeMaxParties = 4;

    /// <summary>Concessions: each unit costs the formateur ConcessionCost capital and pays ConcessionWorth toward the price; at most MaxConcessions per partner.</summary>
    public const double ConcessionCost = 3, ConcessionWorth = 1;
    public const int MaxConcessions = 2;

    /// <summary>Portfolios: at most MaxPortfoliosPerPartner per partner, and the partners together at most PartnerPortfolioShare of all portfolios.</summary>
    public const int MaxPortfoliosPerPartner = 3;
    public const double PartnerPortfolioShare = 2.0 / 3.0;

    public static double TalkDistance(PartyDef a, PartyDef b, IReadOnlyDictionary<string, double> ranges, IReadOnlyDictionary<string, PolicyDef> policies)
    {
        var ca = Compass.Position(a, policies);
        var cb = Compass.Position(b, policies);
        double compass = Math.Sqrt((ca.X - cb.X) * (ca.X - cb.X) + (ca.Y - cb.Y) * (ca.Y - cb.Y)) / (2 * Math.Sqrt(2));
        return PlatformShare * PartyMath.Distance(a, b, ranges) + (1 - PlatformShare) * compass;
    }

    public static double DistanceFactor(double distance) => Math.Clamp(1 + DistanceSlope * (distance - DistanceCentre), DistanceMin, DistanceMax);

    public static double StrengthFactor(int partnerSeats, int formateurSeats) =>
        formateurSeats <= 0 ? StrengthMax : Math.Clamp(StrengthBase + StrengthPerRatio * partnerSeats / (double)formateurSeats, StrengthMin, StrengthMax);

    public static double AlternativesFactor(bool hasAlternative) => hasAlternative ? AlternativeYes : AlternativeNo;

    /// <summary>
    /// Whether <paramref name="partner"/> could be part of another majority without <paramref name="formateur"/>: at most
    /// <see cref="AlternativeMaxParties"/> seated parties, each within <see cref="AlternativeDistance"/> talk distance of the partner.
    /// </summary>
    public static bool HasAlternative(string partner, string formateur, IReadOnlyDictionary<string, int> seats, double fraction, Func<string, string, double> distance)
    {
        var others = seats.Where(kv => kv.Value > 0 && kv.Key != partner && kv.Key != formateur && distance(partner, kv.Key) <= AlternativeDistance)
            .Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        int n = Math.Min(others.Count, 16);
        for (long mask = 0; mask < (1L << n); mask++)
        {
            var set = new List<string> { partner };
            for (int i = 0; i < n; i++) if ((mask >> i & 1) != 0) set.Add(others[i]);
            if (set.Count > AlternativeMaxParties) continue;
            if (CoalitionRules.HasMajority(set, seats, fraction)) return true;
        }
        return false;
    }

    /// <summary>How much a party's programme stresses a portfolio's policies: its largest platform weight among them (0..1).</summary>
    public static double PortfolioInterest(PartyDef party, PortfolioDef portfolio) =>
        Math.Clamp(portfolio.Policies.Select(p => party.Platform.TryGetValue(p, out var pos) ? pos.Weight : 0.0).DefaultIfEmpty(0.0).Max(), 0.0, 1.0);

    /// <summary>What a portfolio pays toward a partner's price: its value × (0.5 + 0.5 × the partner's interest in it).</summary>
    public static double PortfolioWorth(PartyDef party, PortfolioDef portfolio) => portfolio.Value * (0.5 + 0.5 * PortfolioInterest(party, portfolio));

    /// <summary>The most portfolios all partners together may hold.</summary>
    public static int MaxPartnerPortfolios(int total) => (int)Math.Floor(PartnerPortfolioShare * total + 1e-9);

    /// <summary>
    /// The formateur's own position on a policy demand when it pulls the other way (the demand is not met at that position), else null: only then
    /// is a counter value possible.
    /// </summary>
    public static double? Anchor(PartyDef formateur, DemandDef demand) =>
        demand.Kind == "policy" && formateur.Platform.TryGetValue(demand.Subject, out var pos) && !CommitmentMath.Satisfied(demand, pos.Target) ? pos.Target : null;

    public static double HalfWay(DemandDef demand, double anchor) => (anchor + demand.Target) / 2.0;

    /// <summary>Whether a counter value lies between the formateur's position and the demand's target (both included).</summary>
    public static bool ValidCounter(DemandDef demand, double anchor, double value) =>
        double.IsFinite(value) && value >= Math.Min(anchor, demand.Target) - 1e-9 && value <= Math.Max(anchor, demand.Target) + 1e-9;

    /// <summary>The share of the demand's worth a counter value pays: how far it goes from the formateur's position toward the target.</summary>
    public static double CounterFraction(DemandDef demand, double anchor, double value)
    {
        double span = demand.Target - anchor;
        return Math.Abs(span) < 1e-12 ? 1.0 : Math.Clamp((value - anchor) / span, 0.0, 1.0);
    }

    /// <summary>The demand as agreed: with the counter value as its target when one was offered.</summary>
    public static DemandDef Effective(DemandDef demand, AgreedDemand agreed) => agreed.Target is { } t ? demand with { Target = t } : demand;
}
