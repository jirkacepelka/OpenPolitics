using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Presentation;

/// <summary>What the game waits for the player to decide (decision 0015).</summary>
public enum DecisionKind
{
    /// <summary>An AI formateur offers the player's party a place in its coalition (AcceptOffer / RejectOffer); blocks the end of the quarter.</summary>
    Offer,
    /// <summary>The player's party forms the government (ProposeCoalition) - after an election, a fall or a lost majority.</summary>
    Formation,
    /// <summary>A measure put to parliament or a partner's request waits for support or opposition (SupportProposal / OpposeProposal).</summary>
    Proposal,
    /// <summary>A partner of the player's government gave an ultimatum: deliver its demand, court it, reshuffle or dismiss it (leader only).</summary>
    Ultimatum,
    /// <summary>The window before an election is open and the party may still promise (MakePromise).</summary>
    Promises,
}

/// <summary>One open decision. PartyId: the partner of an ultimatum or the formateur of an offer; Id: the proposal id. Urgent decisions stop fast-forward.</summary>
public sealed record Decision(DecisionKind Kind, string? PartyId = null, string? Id = null, bool Urgent = true);

/// <summary>
/// Every decision the game waits for, in the order the briefing offers them (decision 0015). The client's decision count, the Politics badge and the
/// start-of-quarter briefing all use this list, so what is counted is what is offered.
/// </summary>
public static class Decisions
{
    public static IReadOnlyList<Decision> Open(CountryState country, PackSet packs)
    {
        var list = new List<Decision>();
        if (country.Politics is not { } pol) return list;
        if (pol.Formation is { Offer: { } offer }) list.Add(new Decision(DecisionKind.Offer, offer.Formateur));
        else if (pol.Formation is { PlayerIsFormateur: true }) list.Add(new Decision(DecisionKind.Formation, pol.PlayerParty));
        foreach (var p in pol.Proposals ?? Array.Empty<ProposalInfo>())
            if (p.Answer == null) list.Add(new Decision(DecisionKind.Proposal, p.Proposer, p.Id));
        // ultimatums are given to the leader of the government; for anyone else they are news about the government, not a decision
        if (pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader)
            foreach (var u in pol.Ultimatums ?? Array.Empty<UltimatumInfo>()) list.Add(new Decision(DecisionKind.Ultimatum, u.PartyId));
        if (Promisable(country, packs).Count > 0)
            list.Add(new Decision(DecisionKind.Promises, pol.PlayerParty, Urgent: pol.TurnsToElection == PoliticsLimits.PromiseWindowTurns));
        return list;
    }

    /// <summary>The party's demands it could promise right now: in the window, not met, not promised, a free slot and the capital to pay.</summary>
    public static IReadOnlyList<DemandDef> Promisable(CountryState country, PackSet packs)
    {
        if (country.Politics is not { Party: { } ps } pol || pol.Phase == Phase.Formation || pol.TurnsToElection > PoliticsLimits.PromiseWindowTurns) return Array.Empty<DemandDef>();
        if ((ps.PromiseDetails?.Count(p => !p.Carried) ?? 0) >= PoliticsLimits.MaxPromises || ps.PoliticalCapital + 1e-9 < PartyRules.PromiseCost) return Array.Empty<DemandDef>();
        var party = packs.Parties.FirstOrDefault(p => p.Id == pol.PlayerParty);
        if (party == null) return Array.Empty<DemandDef>();
        return party.Demands.Where(d => !Texts.IsMet(d, country) && !(ps.Promises?.Contains(d.Id) ?? false)).ToList();
    }

    public static bool Any(CountryState country, PackSet packs) => Open(country, packs).Count > 0;

    /// <summary>A decision that should stop fast-forward (all but a promise window that was already open last quarter).</summary>
    public static bool AnyUrgent(CountryState country, PackSet packs) => Open(country, packs).Any(d => d.Urgent);
}
