using OpenPolitics.Core.Determinism;

namespace OpenPolitics.Core.Politics;

/// <summary>
/// Decision 0014 (e): voters remember who ran up the deficit. Every quarter the blame of every party fades (half-life <see cref="HalfLifeTurns"/>
/// quarters); while the deficit (% of GDP) rises, the government's leader takes <see cref="LeaderShare"/> blame per point of the rise and each
/// partner <see cref="PartnerShare"/>; while it falls, they shed blame at the same rates (never below zero), much faster than it would fade.
/// Opposition parties neither gain nor shed blame, so the party that caused a deficit keeps paying after it leaves and the party that cleans it
/// up carries none. Blame costs a party <see cref="Penalty"/> in its voters' warmth (the poll), and the player's party also credibility.
/// </summary>
public static class FiscalBlame
{
    public const double LeaderShare = 1.0, PartnerShare = 0.5, HalfLifeTurns = 12, PenaltyPerPoint = 0.012, MaxPenalty = 0.15, CredibilityPerPoint = 0.01;
    private static readonly double Keep = DetMath.Pow(0.5, 1.0 / HalfLifeTurns);

    /// <summary>One quarter: fade, then add (deficit up) or shed (deficit down) for the government's parties. Leader null: no government.</summary>
    public static void Step(SortedDictionary<string, double> blame, string? leader, IEnumerable<string> partners, double deficitChange)
    {
        foreach (var k in blame.Keys.ToList())
        {
            double b = blame[k] * Keep;
            if (b < 1e-4) blame.Remove(k); else blame[k] = b;
        }
        if (leader == null || !double.IsFinite(deficitChange) || deficitChange == 0) return;
        void Apply(string party, double share)
        {
            double next = Math.Max(0.0, blame.GetValueOrDefault(party) + share * deficitChange);
            if (next < 1e-4) blame.Remove(party); else blame[party] = next;
        }
        Apply(leader, LeaderShare);
        foreach (var p in partners.Where(p => p != leader).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal)) Apply(p, PartnerShare);
    }

    /// <summary>How much of its voters' warmth the blame costs a party (a utility penalty in the poll).</summary>
    public static double Penalty(double blame) => Math.Min(MaxPenalty, PenaltyPerPoint * Math.Max(0.0, blame));
}
