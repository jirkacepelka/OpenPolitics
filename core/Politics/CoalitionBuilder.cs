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
        Func<string, string, double>? grudge = null, Func<DemandDef, bool>? alreadyMet = null, Talks? talks = null)
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
            if (talks != null && c.Partners.Any(p => talks.Veto(formateur, p))) continue;
            var deal = Allocate(formateur, c.Partners, parties, programSlots, relation, grudge, alreadyMet: alreadyMet, talks: talks, seats: seats);
            if (deal == null) continue;
            var proposal = new CoalitionProposal(formateur, c.Partners, deal.Value.Agreements, deal.Value.Portfolios.Count > 0 ? deal.Value.Portfolios : null);
            if (CoalitionRules.Evaluate(proposal, parties, seats, fraction, programSlots, relation, grudge, alreadyMet: alreadyMet, talks: talks).Count == 0) return proposal;
        }
        return null;
    }

    /// <summary>
    /// How attractive a formateur's coalition is on the formation market, 0..1:
    /// 0.9 * coherence (1 - mean distance to the partners) + 0.1 * the formateur's share of the coalition's seats.
    /// (The first guess of 0.6 / 0.4 plus 0.1 for the largest party made the largest party lead every single time.)
    /// </summary>
    public static double MarketScore(
        CoalitionProposal proposal, IReadOnlyDictionary<string, PartyDef> parties, IReadOnlyDictionary<string, int> seats,
        IReadOnlyDictionary<string, double> policyRange)
    {
        var lead = parties[proposal.Formateur];
        double coherence = proposal.Partners.Count == 0 ? 1.0
            : 1.0 - proposal.Partners.Average(p => PartyMath.Distance(lead, parties[p], policyRange));
        double coalitionSeats = seats.GetValueOrDefault(proposal.Formateur) + proposal.Partners.Sum(p => seats.GetValueOrDefault(p));
        double share = coalitionSeats > 0 ? seats.GetValueOrDefault(proposal.Formateur) / coalitionSeats : 0.0;
        return 0.9 * coherence + 0.1 * share;
    }

    /// <summary>
    /// The agreements a leader would offer the parties it brings into a sitting government (decision 0013): the same allocation as at formation,
    /// avoiding the red lines of everybody who will sit in it and contradictions with what is already agreed. Null when a price cannot be paid.
    /// </summary>
    public static IReadOnlyList<AgreedDemand>? AllocateFor(
        string leader, IReadOnlyList<string> added, IReadOnlyList<string> kept, IReadOnlyList<AgreedDemand> existing,
        IReadOnlyDictionary<string, PartyDef> parties, int programSlots, Func<string, string, double> relation, Func<string, string, double>? grudge = null,
        Func<DemandDef, bool>? alreadyMet = null, Talks? talks = null)
    {
        grudge ??= (_, _) => 0.0;
        if (added.Any(a => !parties.ContainsKey(a)) || kept.Any(k => !parties.ContainsKey(k)) || !parties.ContainsKey(leader)) return null;
        var demands = existing.Select(a => parties.GetValueOrDefault(a.PartyId)?.Demands.FirstOrDefault(d => d.Id == a.DemandId) is { } d ? Negotiation.Effective(d, a) : null)
            .Where(d => d != null).Select(d => d!).ToList();
        // a reshuffle or a patch-up pays with agreements only: portfolios are handed out at formation
        var noPortfolios = talks == null ? null : talks with { AllowPortfolios = false };
        return Allocate(leader, added.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(), parties, programSlots, relation, grudge, kept, demands,
            alreadyMet, noPortfolios)?.Agreements;
    }

    /// <summary>
    /// The AI leader's way out when a partner walks out and the majority is gone (decision 0013): the ideologically closest smallest set of seated
    /// outside parties (none excluded, none whose red line is crossed right now) that restores the majority and accepts the sitting coalition,
    /// with the agreements it asks for. Null when there is none.
    /// </summary>
    public static CoalitionProposal? Complete(
        string leader, IReadOnlyList<string> kept, IReadOnlyList<AgreedDemand> existing,
        IReadOnlyDictionary<string, PartyDef> parties, IReadOnlyDictionary<string, int> seats, IReadOnlyDictionary<string, double> policyRange,
        double fraction, int programSlots, Func<string, string, double> relation, ISet<string> excluded, Func<string, string, double>? grudge = null,
        Func<DemandDef, bool>? alreadyMet = null, Talks? talks = null)
    {
        grudge ??= (_, _) => 0.0;
        if (!parties.TryGetValue(leader, out var lead)) return null;
        var noPortfolios = talks == null ? null : talks with { AllowPortfolios = false };
        var sitting = new SittingCoalition(kept, existing);
        var eligible = parties.Keys
            .Where(id => id != leader && !kept.Contains(id) && !excluded.Contains(id) && seats.GetValueOrDefault(id) > 0)
            .OrderByDescending(id => seats[id]).ThenBy(id => id, StringComparer.Ordinal).Take(MaxCandidates)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        var stay = new List<string> { leader }; stay.AddRange(kept);
        var candidates = new List<(double Score, int Size, string Key, List<string> Added)>();
        for (long mask = 1; mask < (1L << eligible.Count); mask++)
        {
            var added = new List<string>();
            for (int i = 0; i < eligible.Count; i++) if ((mask >> i & 1) != 0) added.Add(eligible[i]);
            if (added.Count > 2) continue; // a patch-up, not a new government
            var all = stay.Concat(added).ToList();
            if (!CoalitionRules.HasMajority(all, seats, fraction)) continue;
            if (added.Any(p => CoalitionRules.HasMajority(all.Where(x => x != p), seats, fraction))) continue; // not minimal
            double score = added.Average(p => PartyMath.Distance(lead, parties[p], policyRange) - 0.2 * relation(leader, p));
            candidates.Add((score, added.Count, string.Join(",", added), added));
        }
        foreach (var c in candidates.OrderBy(c => c.Size).ThenBy(c => c.Score).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            if (noPortfolios != null && c.Added.Any(p => noPortfolios.Veto(leader, p))) continue;
            var agreements = AllocateFor(leader, c.Added, kept, existing, parties, programSlots, relation, grudge, alreadyMet, noPortfolios);
            if (agreements == null || existing.Count + agreements.Count > programSlots) continue;
            var proposal = new CoalitionProposal(leader, c.Added, agreements);
            if (CoalitionRules.Evaluate(proposal, parties, seats, fraction, programSlots, relation, grudge, sitting, alreadyMet, noPortfolios).Count == 0) return proposal;
        }
        return null;
    }

    /// <summary>
    /// What a party gets for joining a government: ministries in proportion to its seats in the coalition (at least one, at most
    /// <see cref="Negotiation.MaxPortfoliosPerPartner"/>), picked in rounds from the biggest partner down, each taking the portfolio it values most
    /// that is still free. The leader keeps the rest. Ministries come on top of the price in policy demands, which still has to be paid.
    /// </summary>
    /// <summary>The ministries a government's partners hold, as the AI formateur would hand them out (a government that was not formed in talks).</summary>
    public static IReadOnlyList<PortfolioGrant> Ministries(string leader, IReadOnlyList<string> partners, IReadOnlyDictionary<string, PartyDef> parties,
        IReadOnlyList<PortfolioDef> portfolios, IReadOnlyDictionary<string, int> seats)
    {
        var grants = new List<PortfolioGrant>();
        if (partners.Count == 0 || portfolios.Count == 0) return grants;
        var talks = new Talks((_, _) => 1.0, (_, _) => false, portfolios);
        OfferMinistries(leader, partners.ToList(), parties, talks, seats, Negotiation.MaxPartnerPortfolios(portfolios.Count), grants);
        return grants;
    }

    private static void OfferMinistries(string formateur, List<string> partners, IReadOnlyDictionary<string, PartyDef> parties, Talks talks,
        IReadOnlyDictionary<string, int> seats, int maxGrants, List<PortfolioGrant> grants)
    {
        double total = seats.GetValueOrDefault(formateur) + partners.Sum(p => seats.GetValueOrDefault(p));
        var order = partners.OrderByDescending(p => seats.GetValueOrDefault(p)).ThenBy(p => p, StringComparer.Ordinal).ToList();
        var quota = order.ToDictionary(p => p, p => total > 0
            ? Math.Clamp((int)Math.Round(talks.Portfolios.Count * seats.GetValueOrDefault(p) / total, MidpointRounding.AwayFromZero), 1, Negotiation.MaxPortfoliosPerPartner) : 1,
            StringComparer.Ordinal);
        for (int round = 0; round < Negotiation.MaxPortfoliosPerPartner; round++)
            foreach (var partner in order)
            {
                if (grants.Count >= maxGrants || grants.Count(g => g.PartyId == partner) >= quota[partner]) continue;
                var pf = talks.Portfolios.Where(x => grants.All(g => g.PortfolioId != x.Id))
                    .OrderByDescending(x => Negotiation.PortfolioWorth(parties[partner], x)).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (pf != null) grants.Add(new PortfolioGrant(partner, pf.Id));
            }
    }

    /// <summary>How much the party stresses what a demand is about: its platform weight for that policy, 0.5 when it has none.</summary>
    public static double Priority(PartyDef party, DemandDef demand) =>
        demand.Kind == "policy" && party.Platform.TryGetValue(demand.Subject, out var pos) ? pos.Weight : 0.5;

    /// <summary>
    /// The AI formateur's offer to each partner (ordinal order): its demands by weight × (0.5 + priority), skipping any that cross a member's red line
    /// or contradict what is chosen, until the price (with the talk factor, decision 0016) is paid; when the program slots or the demands run out,
    /// cabinet portfolios the partner values most (when the talks allow them). Null when a price cannot be paid.
    /// </summary>
    private static (List<AgreedDemand> Agreements, List<PortfolioGrant> Portfolios)? Allocate(
        string formateur, List<string> partners, IReadOnlyDictionary<string, PartyDef> parties, int slots,
        Func<string, string, double> relation, Func<string, string, double> grudge,
        IReadOnlyList<string>? kept = null, IReadOnlyList<DemandDef>? existing = null, Func<DemandDef, bool>? alreadyMet = null, Talks? talks = null,
        IReadOnlyDictionary<string, int>? seats = null)
    {
        var members = new List<string> { formateur }; members.AddRange(partners);
        if (kept != null) members.AddRange(kept);
        var redLines = members.SelectMany(m => parties[m].RedLines).ToList();
        var chosen = new List<(string Party, DemandDef Demand)>();
        var grants = new List<PortfolioGrant>();
        var already = existing ?? Array.Empty<DemandDef>();
        bool portfolios = talks is { AllowPortfolios: true } && talks.Portfolios.Count > 0;
        int maxGrants = portfolios ? Negotiation.MaxPartnerPortfolios(talks!.Portfolios.Count) : 0;
        if (portfolios && seats != null) OfferMinistries(formateur, partners, parties, talks!, seats, maxGrants, grants);
        foreach (var partner in partners) // ordinal order
        {
            // the ministries given above are a share of power, not the price: the policy demands still have to be met
            double need = CoalitionRules.RequiredWeight(parties[partner], relation(formateur, partner), grudge(partner, formateur), talks?.Factor(formateur, partner) ?? 1.0), got = 0;
            foreach (var d in parties[partner].Demands.OrderByDescending(d => d.Weight * (0.5 + Priority(parties[partner], d))).ThenBy(d => d.Id, StringComparer.Ordinal))
            {
                if (got + 1e-12 >= need) break;
                if (chosen.Count >= slots) { if (portfolios) break; return null; }
                if (redLines.Any(r => CoalitionRules.DemandConflictsWithRedLine(d, r))) continue;
                if (chosen.Any(c => CoalitionRules.DemandsContradict(c.Demand, d)) || already.Any(e => CoalitionRules.DemandsContradict(e, d))) continue;
                if (CoalitionRules.Worth(d, alreadyMet) <= 0) continue; // a goal that is already met is no concession
                chosen.Add((partner, d));
                got += d.Weight;
            }
            if (portfolios && got + 1e-12 < need)
            {
                // when the demands run out, the ministries it already got (and more) pay the rest
                got += grants.Where(g => g.PartyId == partner).Sum(g => talks!.Portfolio(g.PortfolioId) is { } own ? Negotiation.PortfolioWorth(parties[partner], own) : 0.0);
                foreach (var pf in talks!.Portfolios.Where(pf => grants.All(g => g.PortfolioId != pf.Id))
                             .OrderByDescending(pf => Negotiation.PortfolioWorth(parties[partner], pf)).ThenBy(pf => pf.Id, StringComparer.Ordinal))
                {
                    if (got + 1e-12 >= need || grants.Count >= maxGrants || grants.Count(g => g.PartyId == partner) >= Negotiation.MaxPortfoliosPerPartner) break;
                    grants.Add(new PortfolioGrant(partner, pf.Id));
                    got += Negotiation.PortfolioWorth(parties[partner], pf);
                }
            }
            if (got + 1e-12 < need) return null;
        }
        return (chosen.Select(c => new AgreedDemand(c.Party, c.Demand.Id)).ToList(), grants);
    }
}
