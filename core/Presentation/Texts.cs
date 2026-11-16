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
        PromiseKept k when DemandOf(k.PartyId, k.DemandId) is { } kd => _t.T(k.Responsible ? "ui.event.promise_kept" : "ui.event.promise_kept_opposition", Demand(kd)),
        PromiseBroken b when DemandOf(b.PartyId, b.DemandId) is { } bd => _t.T(b.Responsible ? "ui.event.promise_broken" : "ui.event.promise_broken_opposition", Demand(bd)),
        PromiseCarried c when DemandOf(c.PartyId, c.DemandId) is { } cd => _t.T("ui.event.promise_carried", Demand(cd), c.JudgedIn),
        PlayerLeftCoalition => _t.T("ui.event.player_left"),
        PartnerDismissed d => _t.T("ui.event.partner_dismissed", _t.Name(d.PartyId)),
        CoalitionReshuffled r => _t.T("ui.event.coalition_reshuffled", _t.Name(r.Leader), Names(r.Dropped), Names(r.Added)),
        ReshuffleRefused r => _t.T("ui.event.reshuffle_refused", ReshuffleReason(r.Reason)),
        NoConfidencePassed => _t.T("ui.event.no_confidence_passed"),
        NoConfidenceFailed => _t.T("ui.event.no_confidence_failed"),
        PolicyProposalAccepted a when _packs.Policies.FirstOrDefault(p => p.Id == a.PolicyId) is { } ap =>
            _t.T("ui.event.proposal_accepted", _t.Name(a.PolicyId), Fmt.Policy(ap, a.Target, _t)),
        PolicyProposalRejected r => _t.T("ui.event.proposal_rejected_" + r.Reason, _t.Name(r.PolicyId)),
        ProposalPut p when Pol(p.Proposal.PolicyId) is { } pp => _t.T(p.Proposal.Kind == ProposalKind.Measure ? "ui.event.proposal_put_measure" : "ui.event.proposal_put_request",
            _t.Name(p.Proposal.Proposer), _t.Name(pp.Id), Fmt.Policy(pp, p.Proposal.From, _t), Fmt.Policy(pp, p.Proposal.To, _t)),
        ProposalPassed p when Pol(p.PolicyId) is { } pp => _t.T(p.Kind == ProposalKind.Measure ? "ui.event.proposal_passed_measure" : "ui.event.proposal_passed_request",
            _t.Name(p.Proposer), _t.Name(pp.Id), Fmt.Policy(pp, p.From, _t), Fmt.Policy(pp, p.To, _t)),
        ProposalFailed f => _t.T(f.Kind == ProposalKind.Measure ? "ui.event.proposal_failed_measure" : "ui.event.proposal_failed_request", _t.Name(f.Proposer), _t.Name(f.PolicyId)),
        ProposalLapsed l => _t.T("ui.event.proposal_lapsed", _t.Name(l.Proposer), _t.Name(l.PolicyId)),
        AnnouncementAdopted a when Pol(a.PolicyId) is { } ap => _t.T("ui.event.announcement_adopted", _t.Name(a.Leader), _t.Name(a.PolicyId), Fmt.Policy(ap, a.Target, _t)),
        GovernmentMeasure m when _packs.Policies.FirstOrDefault(p => p.Id == m.PolicyId) is { } mp =>
            _t.T("ui.event.government_measure", _t.Name(m.PolicyId), Fmt.Policy(mp, m.From, _t), Fmt.Policy(mp, m.To, _t)),
        _ => null,
    };

    private PolicyDef? Pol(string id) => _packs.Policies.FirstOrDefault(p => p.Id == id);

    /// <summary>"Social Alliance asks you:" and "Pensions 9.0 % → 9.5 %": who proposes and what, in shown units (decision 0014).</summary>
    public (string Who, string What) Proposal(ProposalInfo p) =>
        (_t.T(p.Kind == ProposalKind.Measure ? "ui.proposal.measure" : "ui.proposal.request", _t.Name(p.Proposer)),
         Pol(p.PolicyId) is { } d ? $"{_t.Name(p.PolicyId)}: {Fmt.Policy(d, p.From, _t)} → {Fmt.Policy(d, p.To, _t)}" : p.PolicyId);

    /// <summary>What answering means: fit with the programme, whether the player's seats decide, what an answer or silence does.</summary>
    public IReadOnlyList<string> ProposalHints(ProposalInfo p)
    {
        var lines = new List<string> { _t.T(p.Fit > 1e-9 ? "ui.proposal.matches" : p.Fit < -1e-9 ? "ui.proposal.contradicts" : "ui.proposal.neutral") };
        if (p.Kind == ProposalKind.Request) lines.Add(_t.T("ui.proposal.request_hint"));
        else if (p.Pivotal) lines.Add(_t.T("ui.proposal.pivotal"));
        else lines.Add(_t.T("ui.proposal.not_pivotal"));
        lines.Add(_t.T("ui.proposal.expires"));
        return lines;
    }

    private static string N(double v, string format = "0.##") => Fmt.Number(v, format);

    /// <summary>
    /// The in-game explanation of a party stat or action (decision 0014 f), with the numbers the engine uses: capital, organisation, credibility,
    /// cohesion, relations, fiscal_blame; campaign, organise, court, criticize, stance, promise, priority, shift, propose, announce, proposals,
    /// reshuffle, exits. Unknown topics return the key.
    /// </summary>
    public string Explain(string topic) => topic switch
    {
        "capital" => _t.T("ui.explain.capital", N(PoliticsLimits.BaseIncome), N(PartyRules.OrgIncome), N(PoliticsLimits.LeaderIncome), N(PoliticsLimits.CoalitionIncome), N(PoliticsLimits.MaxCapital)),
        "organisation" => _t.T("ui.explain.organisation", N(PartyRules.OrgIncome), N(PartyRules.OrgTurnout * 100), N(PartyRules.OrgBuildGain * 100), N(PartyRules.OrgBuildCost), N(PartyRules.OrgDecay * 100)),
        "credibility" => _t.T("ui.explain.credibility", N(PartyRules.CredibilityRecoveryPerYear * 100), N(PartyRules.ShiftCredibility * 100), N(PartyRules.PromiseBrokenCredibility * 100),
            N(PartyRules.LeaveCredibility * 100), N(PartyRules.PromiseKeptCredibility * 100)),
        "cohesion" => _t.T("ui.explain.cohesion"),
        "relations" => _t.T("ui.explain.relations", N(0.3), N(PoliticsLimits.CoalitionIncome), N(PartyRules.CourtGain), N(PartyRules.CourtCost), N(PartyRules.RelationDrift)),
        "fiscal_blame" => _t.T("ui.explain.fiscal_blame", N(FiscalBlame.LeaderShare), N(FiscalBlame.PartnerShare), N(FiscalBlame.HalfLifeTurns), N(FiscalBlame.PenaltyPerPoint, "0.###"),
            N(FiscalBlame.MaxPenalty), N(FiscalBlame.CredibilityPerPoint * 100) + " %"),
        "campaign" => _t.T("ui.explain.campaign", N(PartyRules.CampaignCostPerLevel), N(PartyRules.CampaignMomentumPerLevel), PartyRules.CampaignElectionTurns, PartyRules.MomentumHalfLifeTurns),
        "organise" => _t.T("ui.explain.organise", N(PartyRules.OrgBuildCost), N(PartyRules.OrgBuildGain * 100), N(PartyRules.OrgDecay * 100)),
        "court" => _t.T("ui.explain.court", N(PartyRules.CourtCost), N(PartyRules.CourtGain)),
        "criticize" => _t.T("ui.explain.criticize", N(PartyRules.CriticizeCost), N(PartyRules.CriticizeMomentum), N(PartyRules.CriticizePressure), N(PartyRules.CriticizeBackfire)),
        "stance" => _t.T("ui.explain.stance", N(PartyRules.StanceCost), N(PartyRules.StanceMomentum)),
        "promise" => _t.T("ui.explain.promise", PoliticsLimits.PromiseWindowTurns, PoliticsLimits.MaxPromises, N(PartyRules.PromiseCost), N(PartyRules.PromiseMomentum),
            N(PartyRules.PromiseKeptCredibility * 100) + " %", N(PartyRules.PromiseBrokenCredibility * 100) + " %"),
        "priority" => _t.T("ui.explain.priority", N(PartyRules.PriorityCost), N(PartyRules.PriorityStep), N(PartyRules.PriorityCredibility)),
        "shift" => _t.T("ui.explain.shift", N(PartyRules.ShiftCost), N(PartyRules.ShiftMaxOfRange * 100), N(PartyRules.ShiftCredibility)),
        "propose" => _t.T("ui.explain.propose", N(PoliticsEngine.ProposalCost)),
        "announce" => _t.T("ui.explain.announce", N(PoliticsLimits.AnnounceCost), PoliticsLimits.AnnounceCooldownTurns, N(PoliticsLimits.AnnounceWarmth),
            N(PoliticsLimits.AnnounceCredibilityLoss * 100) + " %", N(PoliticsLimits.AnnounceAdoptChance * 100)),
        "proposals" => _t.T("ui.explain.proposals", N(PoliticsLimits.ProposalCredibilityGain * 100) + " %", N(PoliticsLimits.ProposalCredibilityLoss * 100) + " %",
            N(PoliticsLimits.ProposalRelationSupport), N(PoliticsLimits.ProposalRelationOppose), N(PoliticsLimits.ProposalPartnerStrain), N(PoliticsLimits.ProposalWarmth),
            N(PoliticsLimits.ProposalBlockedHit), PoliticsLimits.ProposalCooldownTurns + 1),
        "reshuffle" => _t.T("ui.explain.reshuffle", N(PoliticsLimits.ReshuffleBaseCost), N(PoliticsLimits.ReshufflePerParty), PoliticsLimits.ReshuffleCooldownTurns, PoliticsLimits.ReshuffleRefusedCooldownTurns),
        "exits" => _t.T("ui.explain.exits", N(PartyRules.NoConfidenceCost), N(PartyRules.DismissCost), N(PartyRules.LeaveCredibility * 100) + " %"),
        _ => topic,
    };

    /// <summary>The heading over the party's goals on the overview: plain "Goals" in government, "Opposition goals" outside it.</summary>
    public string GoalsHeading(Role role) => _t.T(role == Role.Opposition ? "ui.party.goals" : "ui.party.goals_government");

    /// <summary>Whether a demand or goal is met in the country right now (policy demands by the setting in force, value goals by the value).</summary>
    public static bool IsMet(DemandDef d, CountryState c) =>
        CommitmentMath.Satisfied(d, d.Kind == "policy" ? c.Policies.GetValueOrDefault(d.Subject) : c.Values.GetValueOrDefault(d.Subject));

    /// <summary>
    /// Two lines for a promise made (decision 0015): at which election it is judged (the one after the coming term, or once carried over the next
    /// one), in how many quarters and whether it is met now; whether breaking it would cost the party.
    /// </summary>
    public IReadOnlyList<string> PromiseStatus(PromiseInfo p) => new[]
    {
        _t.T(p.Carried ? "ui.party.promise_judged_next" : "ui.party.promise_judged_after", Math.Max(1, p.JudgedIn), _t.T(p.Met ? "ui.goal.met" : "ui.goal.unmet")),
        p.Carried ? _t.T(p.CountsAgainst ? "ui.party.promise_counts" : "ui.party.promise_free", p.QuartersInGovernment, p.QuartersElapsed) : _t.T("ui.party.promise_pending_rule"),
    };

    private string Names(IReadOnlyList<string> ids) => ids.Count == 0 ? _t.T("ui.reshuffle.nobody") : string.Join(", ", ids.Select(_t.Name));

    /// <summary>Why the parties turned a reshuffle down (a <see cref="Refusal"/> reason), in plain words.</summary>
    public string ReshuffleReason(string reason) =>
        _t.T(reason is "price" or "grudge" or "red_line" or "contradiction" ? "ui.reshuffle.reason." + reason : "ui.reshuffle.reason.other");

    /// <summary>Plain-language text for a refused action (error key + optional detail).</summary>
    public string Error(ActionResult r)
    {
        string key = "ui." + (r.ErrorKey ?? "error.unknown_action").Replace("error.", "err.");
        // a policy change the party cannot pay for: "cost|capital" in the detail (decision 0013)
        if (r.ErrorKey is "error.policy_cost" or "error.reshuffle_cooldown" or "error.announce_cooldown" && r.Detail != null) return _t.T(key, r.Detail.Split('|').Cast<object>().ToArray());
        string text = _t.T(key);
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
