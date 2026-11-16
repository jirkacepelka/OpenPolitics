using OpenPolitics.Core.Determinism;

namespace OpenPolitics.Core.Politics;

/// <summary>
/// Decision 0014 (e): voters remember who ran up the deficit. Every quarter the blame of every party fades (half-life <see cref="HalfLifeTurns"/>
/// quarters); measured from the deficit a party inherited when it entered the government (a surplus counts as 0), the leader takes
/// <see cref="LeaderShare"/> blame per point the deficit rises above that level and each partner <see cref="PartnerShare"/>; while it falls or is at
/// or below that level, they shed blame (never below zero). Decision 0015 (7).
/// Opposition parties neither gain nor shed blame, so the party that caused a deficit keeps paying after it leaves and the party that cleans it
/// up carries none. Blame costs a party <see cref="Penalty"/> in its voters' warmth (the poll), and the player's party also credibility.
/// </summary>
public static class FiscalBlame
{
    public const double LeaderShare = 1.0, PartnerShare = 0.5, HalfLifeTurns = 12, PenaltyPerPoint = 0.012, MaxPenalty = 0.15, CredibilityPerPoint = 0.01;
    private static readonly double Keep = DetMath.Pow(0.5, 1.0 / HalfLifeTurns);

    /// <summary>Decision 0015 (7): blame a government party sheds per quarter at least while the deficit is at or below its baseline (partners half).</summary>
    public const double MinShed = 0.25;

    /// <summary>
    /// Decision 0015 (7): a rise weighs more the higher the deficit already is - severity <c>min(SeverityMax, 1 + max(deficit, 0) / SeverityPerPoint)</c>.
    /// From -3 % to +1 %: 1 point × 1.33; from +10 % to +12 %: 2 points × 4 = 8.
    /// </summary>
    public const double SeverityPerPoint = 3.0, SeverityMax = 4.0;

    public static double Severity(double deficit) => Math.Min(SeverityMax, 1.0 + Math.Max(0.0, deficit) / SeverityPerPoint);

    /// <summary>Old form: the deficit changed by <paramref name="deficitChange"/> points above the baseline; its level is unknown, so no severity.</summary>
    public static void Step(SortedDictionary<string, double> blame, string? leader, IEnumerable<string> partners, double deficitChange) =>
        Step(blame, leader, partners, new Dictionary<string, double>(), 10.0, 10.0 + (double.IsFinite(deficitChange) ? deficitChange : 0.0), severity: 1.0);

    /// <summary>
    /// One quarter (decision 0015 (7), replacing 0014's rule): every blame fades; then each government party is measured from its baseline - the
    /// deficit when it entered this government, a surplus counting as 0 (<paramref name="baselines"/>, missing = 0). It gains blame only for the rise
    /// of the excess <c>max(0, deficit - baseline)</c>, times <see cref="Severity"/> (leader share 1, partners 0.5): nothing while the budget is in surplus, at or below the inherited
    /// level, or falling. It sheds blame when the deficit falls (by the fall) and while it is at or below the baseline (at least <see cref="MinShed"/>).
    /// Leader null: no government.
    /// </summary>
    public static void Step(SortedDictionary<string, double> blame, string? leader, IEnumerable<string> partners,
        IReadOnlyDictionary<string, double> baselines, double deficitPrev, double deficitNow) =>
        Step(blame, leader, partners, baselines, deficitPrev, deficitNow, null);

    private static void Step(SortedDictionary<string, double> blame, string? leader, IEnumerable<string> partners,
        IReadOnlyDictionary<string, double> baselines, double deficitPrev, double deficitNow, double? severity)
    {
        foreach (var k in blame.Keys.ToList())
        {
            double b = blame[k] * Keep;
            if (b < 1e-4) blame.Remove(k); else blame[k] = b;
        }
        if (leader == null || !double.IsFinite(deficitPrev) || !double.IsFinite(deficitNow)) return;
        void Apply(string party, double share)
        {
            double baseline = Math.Max(0.0, baselines.GetValueOrDefault(party));
            double gain = Math.Max(0.0, Math.Max(0.0, deficitNow - baseline) - Math.Max(0.0, deficitPrev - baseline)) * (severity ?? Severity(deficitNow));
            double shed = Math.Max(Math.Max(0.0, deficitPrev - deficitNow), deficitNow <= baseline ? MinShed : 0.0);
            double delta = gain > 0 ? gain : -shed;
            if (delta == 0) return;
            double next = Math.Max(0.0, blame.GetValueOrDefault(party) + share * delta);
            if (next < 1e-4) blame.Remove(party); else blame[party] = next;
        }
        Apply(leader, LeaderShare);
        foreach (var p in partners.Where(p => p != leader).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal)) Apply(p, PartnerShare);
    }

    /// <summary>How much of its voters' warmth the blame costs a party (a utility penalty in the poll).</summary>
    public static double Penalty(double blame) => Math.Min(MaxPenalty, PenaltyPerPoint * Math.Max(0.0, blame));

    // ---- fiscal credit (decision 0016, item 3) ------------------------------------------------------------------

    /// <summary>
    /// Fiscal credit, the gentle counterpart of blame: a quarter's cut of a deficit earns <see cref="CreditRatio"/> times the blame an equal rise
    /// would have cost at that level. The leader takes it in full, partners <see cref="PartnerShare"/>; a cut smaller than <see cref="CreditMinCut"/>
    /// points earns nothing. Each credit point is worth <see cref="CreditCapitalPerPoint"/> political capital to the player's party (at most
    /// <see cref="CreditCapitalMax"/> a quarter), the same poll warmth per point as blame costs (at most <see cref="CreditMaxBonus"/>) and the same
    /// credibility per point. Credit fades like blame.
    /// </summary>
    public const double CreditRatio = 0.35, CreditMinCut = 0.05, CreditCapitalPerPoint = 0.4, CreditCapitalMax = 1.0, CreditMaxBonus = 0.05;

    /// <summary>
    /// Credit for one quarter's change of the deficit (leader share): only a real cut of a deficit counts - the part of the fall above balance
    /// (<c>prev - max(now, 0)</c>), weighed by the <see cref="Severity"/> of the deficit it started from. A surplus earns nothing, a rise nothing.
    /// </summary>
    public static double Credit(double deficitPrev, double deficitNow)
    {
        if (!double.IsFinite(deficitPrev) || !double.IsFinite(deficitNow) || deficitPrev <= 0) return 0.0;
        double cut = deficitPrev - Math.Max(0.0, deficitNow);
        return cut < CreditMinCut ? 0.0 : CreditRatio * cut * Severity(deficitPrev);
    }

    /// <summary>
    /// One quarter of fiscal credit: every party's credit fades (half-life <see cref="HalfLifeTurns"/>), then the government's parties gain credit
    /// for a cut (<see cref="Credit"/>; leader full, partners half). Leader null: no government, nothing gained. Returns what each party gained.
    /// </summary>
    public static IReadOnlyDictionary<string, double> CreditStep(SortedDictionary<string, double> credit, string? leader, IEnumerable<string> partners,
        double deficitPrev, double deficitNow)
    {
        foreach (var k in credit.Keys.ToList())
        {
            double c = credit[k] * Keep;
            if (c < 1e-4) credit.Remove(k); else credit[k] = c;
        }
        var gained = new SortedDictionary<string, double>(StringComparer.Ordinal);
        double points = leader == null ? 0.0 : Credit(deficitPrev, deficitNow);
        if (points <= 0) return gained;
        void Add(string party, double share)
        {
            gained[party] = share * points;
            credit[party] = credit.GetValueOrDefault(party) + share * points;
        }
        Add(leader!, LeaderShare);
        foreach (var p in partners.Where(p => p != leader).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal)) Add(p, PartnerShare);
        return gained;
    }

    /// <summary>Political capital the player's party earns for credit gained this quarter (a few tenths, at most <see cref="CreditCapitalMax"/>).</summary>
    public static double CreditCapital(double gained) => Math.Min(CreditCapitalMax, CreditCapitalPerPoint * Math.Max(0.0, gained));

    /// <summary>How much warmth the credit gives a party in the poll (a utility bonus, at most <see cref="CreditMaxBonus"/>).</summary>
    public static double Bonus(double credit) => Math.Min(CreditMaxBonus, PenaltyPerPoint * Math.Max(0.0, credit));
}
