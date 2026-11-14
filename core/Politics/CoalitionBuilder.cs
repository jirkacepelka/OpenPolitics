using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>The AI formateur: the ideologically closest minimal winning coalition whose partners' prices can be paid.</summary>
public static class CoalitionBuilder
{
    private const int MaxCandidates = 14;

    public static CoalitionProposal? Build(
        string formateur, IReadOnlyDictionary<string, PartyDef> parties, IReadOnlyDictionary<string, int> seats,
        IReadOnlyDictionary<string, double> policyRange, double fraction, int programSlots,
        Func<string, string, double> relation, ISet<string> excluded,
        Func<string, string, double>? grudge = null)
    {
        grudge ??= (_, _) => 0.0;
        if (!parties.TryGetValue(formateur, out var lead)) return null;
        var eligible = parties.Keys
            .Where(id => id != formateur && !excluded.Contains(id) && seats.GetValueOrDefault(id) > 0)
            .OrderByDescending(id => seats[id]).ThenBy(id => id, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();

        var candidates = new List<(double Score, int Size, string Key, List<string> Partners)>();
        for (long mask = 0; mask < (1L << eligible.Count); mask++)
        {
            var partners = new List<string>();
            for (int i = 0; i < eligible.Count; i++) if ((mask >> i & 1) != 0) partners.Add(eligible[i]);
            var all = new List<string> { formateur }; all.AddRange(partners);
            if (!CoalitionRules.HasMajority(all, seats, fraction)) continue;
            if (partners.Any(p => CoalitionRules.HasMajority(all.Where(x => x != p), seats, fraction))) continue; // not minimal
            double score = partners.Count == 0 ? 0
                : partners.Average(p => PartyMath.Distance(lead, parties[p], policyRange) - 0.2 * relation(formateur, p));
            candidates.Add((score, partners.Count, string.Join(",", partners), partners));
        }

        foreach (var c in candidates.OrderBy(c => c.Score).ThenBy(c => c.Size).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            var agreements = Allocate(formateur, c.Partners, parties, programSlots, relation, grudge);
            if (agreements == null) continue;
            var proposal = new CoalitionProposal(formateur, c.Partners, agreements);
            if (CoalitionRules.Evaluate(proposal, parties, seats, fraction, programSlots, relation, grudge).Count == 0) return proposal;
        }
        return null;
    }

    private static List<AgreedDemand>? Allocate(
        string formateur, List<string> partners, IReadOnlyDictionary<string, PartyDef> parties, int slots,
        Func<string, string, double> relation, Func<string, string, double> grudge)
    {
        var members = new List<string> { formateur }; members.AddRange(partners);
        var redLines = members.SelectMany(m => parties[m].RedLines).ToList();
        var chosen = new List<(string Party, DemandDef Demand)>();
        foreach (var partner in partners) // ordinal order
        {
            double need = CoalitionRules.RequiredWeight(parties[partner], relation(formateur, partner), grudge(partner, formateur)), got = 0;
            foreach (var d in parties[partner].Demands.OrderByDescending(d => d.Weight).ThenBy(d => d.Id, StringComparer.Ordinal))
            {
                if (got + 1e-12 >= need) break;
                if (chosen.Count >= slots) return null;
                if (redLines.Any(r => CoalitionRules.DemandConflictsWithRedLine(d, r))) continue;
                if (chosen.Any(c => CoalitionRules.DemandsContradict(c.Demand, d))) continue;
                chosen.Add((partner, d));
                got += d.Weight;
            }
            if (got + 1e-12 < need) return null;
        }
        return chosen.Select(c => new AgreedDemand(c.Party, c.Demand.Id)).ToList();
    }
}
