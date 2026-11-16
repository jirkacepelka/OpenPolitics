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

    /// <summary>What an agreed demand is worth toward a partner's price: its weight, or nothing for a value goal that is already met.</summary>
    public static double Worth(DemandDef d, Func<DemandDef, bool>? alreadyMet) =>
        d.Kind == "value" && alreadyMet != null && alreadyMet(d) ? 0.0 : d.Weight;

    /// <summary>Price a partner asks for, reduced by good relations.</summary>
    public static double RequiredWeight(PartyDef partner, double relation, double grudgeToFormateur = 0.0) =>
        partner.Price * (1.0 - 0.5 * relation) * (1.0 + Math.Max(0.0, grudgeToFormateur));

    /// <summary>Decision 0016: the price with the talk factor (ideological distance, strength, alternatives) applied.</summary>
    public static double RequiredWeight(PartyDef partner, double relation, double grudgeToFormateur, double factor) =>
        RequiredWeight(partner, relation, grudgeToFormateur) * factor;

    /// <summary>A party that resents another this much refuses to sit in a government with it.</summary>
    public const double GrudgeVeto = 0.5;

    /// <summary>
    /// What one agreement pays toward its owner's price: the demand's weight (nothing for a value goal already met), a part of it for a counter
    /// value (decision 0016). Null when the agreement is not valid: an unknown demand, or a counter value on a demand that cannot be countered or
    /// outside the range between the formateur's position and the demand's target.
    /// </summary>
    public static (DemandDef Demand, double Worth)? AgreementWorth(AgreedDemand a, PartyDef owner, PartyDef? formateur, Func<DemandDef, bool>? alreadyMet)
    {
        if (owner.Demands.FirstOrDefault(d => d.Id == a.DemandId) is not { } demand) return null;
        double worth = Worth(demand, alreadyMet);
        if (a.Target is not { } t) return (demand, worth);
        if (formateur == null || Negotiation.Anchor(formateur, demand) is not { } anchor || !Negotiation.ValidCounter(demand, anchor, t)) return null;
        return (Negotiation.Effective(demand, a), worth * Negotiation.CounterFraction(demand, anchor, t));
    }

    /// <param name="sitting">
    /// For a reshuffle of a sitting government (decision 0013): the partners that stay and the agreements in force. The proposal's partners and
    /// agreements are then only the newcomers and their new agreements; the kept partners count for the majority and their red lines and
    /// agreements are checked against what is new, but their price is not asked again and grudges among those already sitting are ignored.
    /// </param>
    /// <param name="talks">
    /// Decision 0016: the talk factor on each partner's price, the distance veto and the portfolios that may be given. Null: the plain rules
    /// (factor 1, no veto; any portfolio or concession in the proposal is unknown).
    /// </param>
    public static IReadOnlyList<Refusal> Evaluate(
        CoalitionProposal p, IReadOnlyDictionary<string, PartyDef> parties, IReadOnlyDictionary<string, int> seats,
        double fraction, int programSlots, Func<string, string, double> relation,
        Func<string, string, double>? grudge = null, SittingCoalition? sitting = null, Func<DemandDef, bool>? alreadyMet = null, Talks? talks = null)
    {
        grudge ??= (_, _) => 0.0;
        var refusals = new List<Refusal>();
        var members = new List<string> { p.Formateur };
        members.AddRange(p.Partners);
        if (sitting != null) members.AddRange(sitting.Kept.Where(k => !members.Contains(k)));
        foreach (var m in members.Where(m => !parties.ContainsKey(m)).Distinct(StringComparer.Ordinal))
            refusals.Add(new Refusal("unknown", m, null));
        var known = members.Where(parties.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
        bool IsNew(string id) => sitting == null || p.Partners.Contains(id);
        var formateur = parties.GetValueOrDefault(p.Formateur);

        var agreed = new List<(AgreedDemand A, DemandDef D, double Worth)>();
        var distinctAgreements = p.Agreements.Distinct().ToList(); // a repeated agreement never counts twice
        var seenDemands = new HashSet<(string, string)>();
        foreach (var a in distinctAgreements)
        {
            if (!p.Partners.Contains(a.PartyId) || !parties.TryGetValue(a.PartyId, out var owner)
                || AgreementWorth(a, owner, formateur, alreadyMet) is not { } aw || !seenDemands.Add((a.PartyId, a.DemandId)))
            {
                refusals.Add(new Refusal("unknown", a.PartyId, a.DemandId));
                continue;
            }
            agreed.Add((a, aw.Demand, aw.Worth));
        }
        var existing = (sitting?.Existing ?? Array.Empty<AgreedDemand>())
            .Select(a => parties.TryGetValue(a.PartyId, out var owner) && owner.Demands.FirstOrDefault(d => d.Id == a.DemandId) is { } d ? Negotiation.Effective(d, a) : null)
            .Where(d => d != null).Select(d => d!).ToList();

        if (distinctAgreements.Count > programSlots) refusals.Add(new Refusal("slots", null, null));
        if (!CoalitionRules.HasMajority(known, seats, fraction)) refusals.Add(new Refusal("majority", null, null));

        // portfolios and concessions (decision 0016)
        var portfolioWorth = new Dictionary<string, double>(StringComparer.Ordinal);
        var grants = p.PortfolioList.ToList();
        if (grants.Count > 0)
        {
            var usedPortfolios = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in grants)
            {
                if (talks == null || !talks.AllowPortfolios || talks.Portfolio(g.PortfolioId) is not { } pf || !p.Partners.Contains(g.PartyId)
                    || !parties.TryGetValue(g.PartyId, out var holder) || !usedPortfolios.Add(g.PortfolioId))
                { refusals.Add(new Refusal("unknown", g.PartyId, null)); continue; }
                portfolioWorth[g.PartyId] = portfolioWorth.GetValueOrDefault(g.PartyId) + Negotiation.PortfolioWorth(holder, pf);
            }
            if (talks != null && (grants.GroupBy(g => g.PartyId).Any(gr => gr.Count() > Negotiation.MaxPortfoliosPerPartner)
                    || grants.Count > Negotiation.MaxPartnerPortfolios(talks.Portfolios.Count)))
                refusals.Add(new Refusal("portfolios", null, null));
        }
        var concessions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in p.ConcessionList)
        {
            if (talks == null || !p.Partners.Contains(c.PartyId) || c.Units < 0 || c.Units > Negotiation.MaxConcessions || concessions.ContainsKey(c.PartyId))
            { refusals.Add(new Refusal("unknown", c.PartyId, null)); continue; }
            concessions[c.PartyId] = c.Units;
        }

        foreach (var m in known.OrderBy(x => x, StringComparer.Ordinal))
            if (known.Any(o => o != m && (IsNew(m) || IsNew(o)) && grudge(m, o) >= GrudgeVeto)) refusals.Add(new Refusal("grudge", m, null));
        if (talks != null)
            foreach (var partner in p.Partners.Where(parties.ContainsKey).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
                if (talks.Veto(p.Formateur, partner)) refusals.Add(new Refusal("distance", partner, null));

        foreach (var (a, d, _) in agreed)
            foreach (var m in known)
                foreach (var line in parties[m].RedLines)
                    if (DemandConflictsWithRedLine(d, line)) refusals.Add(new Refusal("red_line", a.PartyId, a.DemandId));
        if (sitting != null) // what the sitting government already agreed must not cross a newcomer's red line
            foreach (var d in existing)
                foreach (var m in known.Where(m => p.Partners.Contains(m)))
                    if (parties[m].RedLines.Any(line => DemandConflictsWithRedLine(d, line))) refusals.Add(new Refusal("red_line", m, null));

        for (int i = 0; i < agreed.Count; i++)
        {
            for (int j = i + 1; j < agreed.Count; j++)
                if (DemandsContradict(agreed[i].D, agreed[j].D))
                    refusals.Add(new Refusal("contradiction", agreed[j].A.PartyId, agreed[j].A.DemandId));
            if (existing.Any(e => DemandsContradict(e, agreed[i].D)))
                refusals.Add(new Refusal("contradiction", agreed[i].A.PartyId, agreed[i].A.DemandId));
        }

        foreach (var partner in p.Partners.Where(parties.ContainsKey).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            // a value goal that is already met is no concession (decision 0014): it pays nothing toward the price
            double weight = agreed.Where(x => x.A.PartyId == partner).Sum(x => x.Worth) + portfolioWorth.GetValueOrDefault(partner)
                + Negotiation.ConcessionWorth * concessions.GetValueOrDefault(partner);
            double factor = talks?.Factor(p.Formateur, partner) ?? 1.0;
            if (weight + 1e-12 < RequiredWeight(parties[partner], relation(p.Formateur, partner), grudge(partner, p.Formateur), factor))
                refusals.Add(new Refusal("price", partner, null));
        }
        return refusals;
    }
}
