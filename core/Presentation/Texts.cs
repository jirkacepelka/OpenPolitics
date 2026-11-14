using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Presentation;

/// <summary>Turns political events, demands and action errors into text, so every client says the same thing.</summary>
public sealed class Texts
{
    private readonly PackSet _packs;
    private readonly Localizer _t;

    public Texts(PackSet packs, Localizer localizer) { _packs = packs; _t = localizer; }

    public DemandDef? DemandOf(string partyId, string demandId) =>
        _packs.Parties.FirstOrDefault(p => p.Id == partyId)?.Demands.FirstOrDefault(d => d.Id == demandId);

    /// <summary>"Corporate tax &lt;= 4.0 % of GDP": the demand in real units.</summary>
    public string Demand(DemandDef d)
    {
        string target = d.Kind == "policy" && _packs.Policies.FirstOrDefault(p => p.Id == d.Subject) is { } pol ? Fmt.Policy(pol, d.Target, _t)
            : _packs.Values.FirstOrDefault(v => v.Id == d.Subject) is { } val ? Fmt.Value(val, d.Target) : Fmt.Number(d.Target);
        return $"{_t.Name(d.Subject)} {d.Op} {target}";
    }

    /// <summary>One line for a political event, or null for events that are shown elsewhere (elections).</summary>
    public string? Event(PoliticalEvent e) => e switch
    {
        FormationStarted f => _t.T("ui.event.formation_started", _t.Name(f.Formateur)),
        FormationFailed f => _t.T("ui.event.formation_failed", _t.Name(f.Formateur)),
        OfferMade o => _t.T("ui.event.offer_made", _t.Name(o.Proposal.Formateur)),
        OfferLapsed => _t.T("ui.event.offer_lapsed"),
        GovernmentFormed g => _t.T("ui.event.government_formed", string.Join(", ", g.Government.Parties.Select(_t.Name)), _t.Name(g.Government.Leader)),
        EarlyElectionsCalled => _t.T("ui.event.early_elections"),
        LoyaltyWarning w => w.DemandId is { } wd && DemandOf(w.PartyId, wd) is { } wdef
            ? _t.T("ui.event.loyalty_warning_demand", _t.Name(w.PartyId), Demand(wdef))
            : _t.T("ui.event.loyalty_warning", _t.Name(w.PartyId)),
        Ultimatum u => u.DemandId is { } ud && DemandOf(u.PartyId, ud) is { } udef
            ? _t.T("ui.event.ultimatum_demand", _t.Name(u.PartyId), u.TurnsLeft, Demand(udef))
            : _t.T("ui.event.ultimatum", _t.Name(u.PartyId), u.TurnsLeft),
        PartnerLeft l => _t.T("ui.event.partner_left", _t.Name(l.PartyId)),
        GovernmentFell => _t.T("ui.event.government_fell"),
        DemandRaised r when DemandOf(r.PartyId, r.DemandId) is { } rd =>
            _t.T("ui.event.demand_raised", _t.Name(r.PartyId), Demand(rd), r.DueTurns),
        DemandMet m when DemandOf(m.PartyId, m.DemandId) is { } md => _t.T("ui.event.demand_met", _t.Name(m.PartyId), Demand(md)),
        DemandFailed f when DemandOf(f.PartyId, f.DemandId) is { } fd => _t.T("ui.event.demand_failed", _t.Name(f.PartyId), Demand(fd)),
        PlayerLeftCoalition => _t.T("ui.event.player_left"),
        PartnerDismissed d => _t.T("ui.event.partner_dismissed", _t.Name(d.PartyId)),
        NoConfidencePassed => _t.T("ui.event.no_confidence_passed"),
        NoConfidenceFailed => _t.T("ui.event.no_confidence_failed"),
        PolicyProposalAccepted a when _packs.Policies.FirstOrDefault(p => p.Id == a.PolicyId) is { } ap =>
            _t.T("ui.event.proposal_accepted", _t.Name(a.PolicyId), Fmt.Policy(ap, a.Target, _t)),
        PolicyProposalRejected r => _t.T("ui.event.proposal_rejected_" + r.Reason, _t.Name(r.PolicyId)),
        GovernmentMeasure m when _packs.Policies.FirstOrDefault(p => p.Id == m.PolicyId) is { } mp =>
            _t.T("ui.event.government_measure", _t.Name(m.PolicyId), Fmt.Policy(mp, m.From, _t), Fmt.Policy(mp, m.To, _t)),
        _ => null,
    };

    /// <summary>Plain-language text for a refused action (error key + optional detail).</summary>
    public string Error(ActionResult r)
    {
        string text = _t.T("ui." + (r.ErrorKey ?? "error.unknown_action").Replace("error.", "err."));
        if (r.ErrorKey?.StartsWith("error.refused.", StringComparison.Ordinal) == true && r.Detail is { Length: > 1 } detail)
        {
            var split = detail.Split(':'); // "base:party:demand" or "base:party:"
            if (split.Length >= 3)
            {
                string partyId = split[0] + ":" + split[1];
                var d = DemandOf(partyId, split[2]);
                text += "\n" + _t.Name(partyId) + (d != null ? ": " + Demand(d) : "");
            }
        }
        return text;
    }
}
