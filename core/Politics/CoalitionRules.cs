using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>Pure rules deciding whether a coalition proposal works. Same rules for the AI and the player.</summary>
public static class CoalitionRules
{
    public static bool HasMajority(IEnumerable<string> parties, IReadOnlyDictionary<string, int> seats, double fraction)
    {
        int total = seats.Values.Sum();
        int held = parties.Distinct(StringComparer.Ordinal).Sum(p => seats.GetValueOrDefault(p));
        return held > fraction * total;
    }

    /// <summary>A policy demand conflicts with a red line when its target lies in the forbidden range.</summary>
    public static bool DemandConflictsWithRedLine(DemandDef d, RedLineDef r)
    {
        if (d.Kind != "policy" || d.Subject != r.Policy) return false;
        return r.Op == ">=" ? d.Target >= r.Threshold : d.Target <= r.Threshold;
    }

    /// <summary>Two demands on the same policy contradict when no value satisfies both.</summary>
    public static bool DemandsContradict(DemandDef a, DemandDef b)
    {
        if (a.Kind != b.Kind || a.Subject != b.Subject) return false;
        if (a.Op == b.Op) return false;
        var lower = a.Op == ">=" ? a : b; // value must be >= lower.Target
        var upper = a.Op == ">=" ? b : a; // and <= upper.Target
        return lower.Target > upper.Target;
    }

    /// <summary>Price a partner asks for, reduced by good relations.</summary>
    public static double RequiredWeight(PartyDef partner, double relation) => partner.Price * (1.0 - 0.5 * relation);

    public static IReadOnlyList<Refusal> Evaluate(
        CoalitionProposal p, IReadOnlyDictionary<string, PartyDef> parties, IReadOnlyDictionary<string, int> seats,
        double fraction, int programSlots, Func<string, string, double> relation)
    {
        var refusals = new List<Refusal>();
        var members = new List<string> { p.Formateur };
        members.AddRange(p.Partners);
        foreach (var m in members.Where(m => !parties.ContainsKey(m)).Distinct(StringComparer.Ordinal))
            refusals.Add(new Refusal("unknown", m, null));
        var known = members.Where(parties.ContainsKey).Distinct(StringComparer.Ordinal).ToList();

        var agreed = new List<(AgreedDemand A, DemandDef D)>();
        foreach (var a in p.Agreements)
        {
            if (!p.Partners.Contains(a.PartyId) || !parties.TryGetValue(a.PartyId, out var owner)
                || owner.Demands.FirstOrDefault(d => d.Id == a.DemandId) is not { } demand)
            {
                refusals.Add(new Refusal("unknown", a.PartyId, a.DemandId));
                continue;
            }
            agreed.Add((a, demand));
        }

        if (p.Agreements.Count > programSlots) refusals.Add(new Refusal("slots", null, null));
        if (!CoalitionRules.HasMajority(known, seats, fraction)) refusals.Add(new Refusal("majority", null, null));

        foreach (var (a, d) in agreed)
            foreach (var m in known)
                foreach (var line in parties[m].RedLines)
                    if (DemandConflictsWithRedLine(d, line)) refusals.Add(new Refusal("red_line", a.PartyId, a.DemandId));

        for (int i = 0; i < agreed.Count; i++)
            for (int j = i + 1; j < agreed.Count; j++)
                if (DemandsContradict(agreed[i].D, agreed[j].D))
                    refusals.Add(new Refusal("contradiction", agreed[j].A.PartyId, agreed[j].A.DemandId));

        foreach (var partner in p.Partners.Where(parties.ContainsKey).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            double weight = agreed.Where(x => x.A.PartyId == partner).Sum(x => x.D.Weight);
            if (weight + 1e-12 < RequiredWeight(parties[partner], relation(p.Formateur, partner)))
                refusals.Add(new Refusal("price", partner, null));
        }
        return refusals;
    }
}
