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

    /// <summary>
    /// Decision 0015 (role-aware texts): the variant of a text for the player's role. Keys are written for the leader (or neutrally); a key with a
    /// suffix <c>_opposition</c> or <c>_partner</c> replaces it for that role when it exists. Every text that addresses the player as the
    /// government must either have an <c>_opposition</c> variant or be listed in <see cref="RoleTexts.LeaderOnly"/> (enforced by a test).
    /// </summary>
    public string RoleKey(string key, Role role) => role switch
    {
        Role.Opposition when _t.Has(key + "_opposition") => key + "_opposition",
        Role.Partner when _t.Has(key + "_partner") => key + "_partner",
        _ => key,
    };

    /// <summary>The text of <paramref name="key"/> for the player's role (see <see cref="RoleKey"/>).</summary>
    public string ForRole(string key, Role role, params object[] args) => _t.T(RoleKey(key, role), args);

    /// <summary>One line for a political event as the leader of the government reads it (see the overload with the role).</summary>
    public string? Event(PoliticalEvent e) => Event(e, Role.Leader);

    /// <summary>One line for a political event, worded for the player's role; null for events that are shown elsewhere (elections).</summary>
    public string? Event(PoliticalEvent e, Role role) => e switch
    {
        FormationStarted f => _t.T("ui.event.formation_started", _t.Name(f.Formateur)),
        FormationFailed f => _t.T("ui.event.formation_failed", _t.Name(f.Formateur)),
        OfferMade o => _t.T("ui.event.offer_made", _t.Name(o.Proposal.Formateur)),
        GovernmentFormed g => _t.T("ui.event.government_formed", string.Join(", ", g.Government.Parties.Select(_t.Name)), _t.Name(g.Government.Leader)),
        EarlyElectionsCalled => _t.T("ui.event.early_elections"),
        LoyaltyWarning w => w.DemandId is { } wd && DemandOf(w.PartyId, wd) is { } wdef
            ? ForRole("ui.event.loyalty_warning_demand", role, _t.Name(w.PartyId), Demand(wdef))
            : ForRole("ui.event.loyalty_warning", role, _t.Name(w.PartyId)),
        Ultimatum u => u.DemandId is { } ud && DemandOf(u.PartyId, ud) is { } udef
            ? ForRole("ui.event.ultimatum_demand", role, _t.Name(u.PartyId), u.TurnsLeft, Demand(udef))
            : ForRole("ui.event.ultimatum", role, _t.Name(u.PartyId), u.TurnsLeft),
        PartnerLeft l => _t.T("ui.event.partner_left", _t.Name(l.PartyId)),
        GovernmentFell => _t.T("ui.event.government_fell"),
        DemandRaised r when DemandOf(r.PartyId, r.DemandId) is { } rd =>
            ForRole("ui.event.demand_raised", role, _t.Name(r.PartyId), Demand(rd), r.DueTurns),
        DemandMet m when DemandOf(m.PartyId, m.DemandId) is { } md => _t.T("ui.event.demand_met", _t.Name(m.PartyId), Demand(md)),
        DemandFailed f when DemandOf(f.PartyId, f.DemandId) is { } fd => ForRole("ui.event.demand_failed", role, _t.Name(f.PartyId), Demand(fd)),
        PromiseKept k when DemandOf(k.PartyId, k.DemandId) is { } kd => _t.T(k.Responsible ? "ui.event.promise_kept" : "ui.event.promise_kept_opposition", Demand(kd)),
        PromiseBroken b when DemandOf(b.PartyId, b.DemandId) is { } bd => _t.T(b.Responsible ? "ui.event.promise_broken" : "ui.event.promise_broken_opposition", Demand(bd)),
        PromiseCarried c when DemandOf(c.PartyId, c.DemandId) is { } cd => _t.T("ui.event.promise_carried", Demand(cd), c.JudgedIn),
        SupportMoved m => _t.T("ui.event.support_moved", _t.Name(m.PartyId),
            string.Join(", ", m.Moves.Select(x => _t.T("ui.support.group_move", _t.Name(x.GroupId), Fmt.Number(100 * x.After, "0.0"), Points(100 * (x.After - x.Before)))))),
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
        OfferLapsed => ForRole("ui.event.offer_lapsed", role),
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

    /// <summary>"+2.1" / "−0.4": percentage points with a sign.</summary>
    public static string Points(double points) => (points >= 0 ? "+" : "−") + Fmt.Number(Math.Abs(points), "0.0");

    /// <summary>
    /// "+2.1 pts among Middle class, −0.4 pts among Pensioners" (decision 0015): how far the player's support moved per voter group, biggest first, at
    /// most <paramref name="max"/> groups and only moves of at least 0.05 points; null when nothing moved.
    /// </summary>
    public string? SupportChange(IReadOnlyDictionary<string, double>? before, IReadOnlyDictionary<string, double>? after, int max = 3)
    {
        if (before == null || after == null) return null;
        var points = after.Select(kv => (Group: kv.Key, Points: 100 * (kv.Value - before.GetValueOrDefault(kv.Key, kv.Value)))).ToList();
        return SupportPoints(points.ToDictionary(p => p.Group, p => p.Points), max);
    }

    /// <summary>The same from points per group (as in <see cref="ProposalPreview.SupportPoints"/>); null when nothing moves by 0.05 points.</summary>
    public string? SupportPoints(IReadOnlyDictionary<string, double> points, int max = 3)
    {
        var shown = points.Where(kv => Math.Abs(kv.Value) >= 0.05).OrderByDescending(kv => Math.Abs(kv.Value)).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(max)
            .Select(kv => _t.T("ui.support.points_among", Points(kv.Value), _t.Name(kv.Key))).ToList();
        return shown.Count == 0 ? null : string.Join(", ", shown);
    }

    /// <summary>
    /// What a policy proposal would do (decision 0015), one line each: who decides (the leader's likely answer, or public and maybe adopted), how the
    /// voters' support moves, the credibility cost of going against the programme.
    /// </summary>
    public IReadOnlyList<string> ProposalPreviewLines(ProposalPreview p)
    {
        var lines = new List<string>();
        if (p.ToLeader) lines.Add(_t.T(p.LeaderAgrees == true ? "ui.propose.leader_yes" : p.LeaderReason == "red_line" ? "ui.propose.leader_red_line" : "ui.propose.leader_no"));
        else lines.Add(_t.T(p.Reaction.MayBeAdopted ? "ui.announce.adoptable" : "ui.propose.public"));
        if (SupportPoints(p.SupportPoints) is { } support) lines.Add(_t.T("ui.propose.support", support));
        else if (p.Reaction.Groups.Count == 0) lines.Add(_t.T("ui.announce.nobody"));
        if (p.Reaction.Credibility < 0) lines.Add(_t.T("ui.announce.credibility", Fmt.Number(p.Reaction.Credibility * 100, "0") + " %"));
        return lines;
    }

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
            N(FiscalBlame.MaxPenalty), N(FiscalBlame.CredibilityPerPoint * 100) + " %", N(FiscalBlame.MinShed), N(FiscalBlame.SeverityPerPoint), N(FiscalBlame.SeverityMax)),
        "campaign" => _t.T("ui.explain.campaign", N(PartyRules.CampaignCostPerLevel), N(PartyRules.CampaignMomentumPerLevel), PartyRules.CampaignElectionTurns, PartyRules.MomentumHalfLifeTurns),
        "organise" => _t.T("ui.explain.organise", N(PartyRules.OrgBuildCost), N(PartyRules.OrgBuildGain * 100), N(PartyRules.OrgDecay * 100)),
        "court" => _t.T("ui.explain.court", N(PartyRules.CourtCost), N(PartyRules.CourtGain)),
        "criticize" => _t.T("ui.explain.criticize", N(PartyRules.CriticizeCost), N(PartyRules.CriticizeMomentum), N(PartyRules.CriticizePressure), N(PartyRules.CriticizeBackfire)),
        "stance" => _t.T("ui.explain.stance", N(PartyRules.StanceCost), N(PartyRules.StanceMomentum)),
        "promise" => _t.T("ui.explain.promise", PoliticsLimits.PromiseWindowTurns, PoliticsLimits.MaxPromises, N(PartyRules.PromiseCost), N(PartyRules.PromiseMomentum),
            N(PartyRules.PromiseKeptCredibility * 100) + " %", N(PartyRules.PromiseBrokenCredibility * 100) + " %"),
        "priority" => _t.T("ui.explain.priority", N(PartyRules.PriorityCost), N(PartyRules.PriorityStep), N(PartyRules.PriorityCredibility)),
        "shift" => _t.T("ui.explain.shift", N(PartyRules.ShiftCost), N(PartyRules.ShiftMaxOfRange * 100), N(PartyRules.ShiftCredibility)),
        "propose" or "announce" => _t.T("ui.explain.propose", N(PoliticsLimits.ProposeToLeaderCost), N(PoliticsLimits.AnnounceCost), PoliticsLimits.AnnounceCooldownTurns,
            N(PoliticsLimits.AnnounceAdoptChance * 100), N(PoliticsLimits.AnnounceWarmth), N(PoliticsLimits.AnnounceCredibilityLoss * 100) + " %"),
        "proposals" => _t.T("ui.explain.proposals", N(PoliticsLimits.ProposalCredibilityGain * 100) + " %", N(PoliticsLimits.ProposalCredibilityLoss * 100) + " %",
            N(PoliticsLimits.ProposalRelationSupport), N(PoliticsLimits.ProposalRelationOppose), N(PoliticsLimits.ProposalPartnerStrain), N(PoliticsLimits.ProposalWarmth),
            N(PoliticsLimits.ProposalBlockedHit), PoliticsLimits.ProposalCooldownTurns + 1),
        "reshuffle" => _t.T("ui.explain.reshuffle", N(PoliticsLimits.ReshuffleBaseCost), N(PoliticsLimits.ReshufflePerParty), PoliticsLimits.ReshuffleCooldownTurns, PoliticsLimits.ReshuffleRefusedCooldownTurns),
        "exits" => _t.T("ui.explain.exits", N(PartyRules.NoConfidenceCost), N(PartyRules.DismissCost), N(PartyRules.LeaveCredibility * 100) + " %"),
        _ => topic,
    };

    /// <summary>The heading over the party's goals on the overview: plain "Goals" in government, "Opposition goals" outside it.</summary>
    public string GoalsHeading(Role role) => _t.T(role == Role.Opposition ? "ui.party.goals" : "ui.party.goals_government");

    /// <summary>
    /// The texts the clients show for a role outside events and explanations (decision 0015), so a test can check all of them: role pill, welcome
    /// title and text, the coalition-fragility label, the tips, the headings over the policies. Keys are base keys; <see cref="RoleKey"/> picks the variant.
    /// </summary>
    public static readonly IReadOnlyList<string> RoleScreenKeys = new[]
    {
        "ui.goal.fragility", "ui.client.welcome_politics", "ui.brief.welcome_title", "ui.heading.policies", "ui.client.policies_hint2",
        "ui.tip.1", "ui.tip.2", "ui.tip.3", "ui.tip.4", "ui.tip.5", "ui.rail.government", "ui.pt.government_play", "ui.party.measures",
    };

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
