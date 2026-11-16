using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>
/// The cards that answer each open decision (decision 0015) - used by the start-of-quarter briefing and the Politics screen, so every decision can
/// be made inline with real buttons. <c>refresh</c> redraws the host after an answer.
/// </summary>
public static class DecisionCards
{
    // The player's own coalition draft, shared by the briefing and the Politics screen.
    public static readonly HashSet<string> Invited = new(StringComparer.Ordinal);
    public static readonly HashSet<(string Party, string Demand)> Agreed = new();
    /// <summary>Decision 0016: counter values offered for negotiable demands, portfolios given (portfolio → party) and concessions (party → units).</summary>
    public static readonly Dictionary<(string Party, string Demand), double> Targets = new();
    public static readonly Dictionary<string, string> Portfolios = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, int> Concessions = new(StringComparer.Ordinal);
    private static bool _adjust;
    /// <summary>The counter-offer picks (index into the party's open demands / the free portfolios, 0 = nothing); public for the smoke test.</summary>
    public static int _counterDemand, _counterPortfolio;

    private static void ClearDraft() { Invited.Clear(); Agreed.Clear(); Targets.Clear(); Portfolios.Clear(); Concessions.Clear(); }

    /// <summary>The player's draft as a coalition proposal.</summary>
    public static CoalitionProposal DraftProposal(string player) => new(player, Invited.OrderBy(x => x, StringComparer.Ordinal).ToList(),
        Agreed.OrderBy(a => a.Party, StringComparer.Ordinal).ThenBy(a => a.Demand, StringComparer.Ordinal)
            .Select(a => new AgreedDemand(a.Party, a.Demand, Targets.TryGetValue(a, out var t) ? t : null)).ToList(),
        Portfolios.Where(kv => Invited.Contains(kv.Value)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new PortfolioGrant(kv.Value, kv.Key)).ToList(),
        Concessions.Where(kv => kv.Value > 0 && Invited.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new Concession(kv.Key, kv.Value)).ToList());

    /// <summary>Puts a whole proposal (the AI's suggestion or a partner's counter terms) into the draft.</summary>
    public static void Load(CoalitionProposal p)
    {
        ClearDraft();
        Invited.UnionWith(p.Partners);
        foreach (var a in p.Agreements) { Agreed.Add((a.PartyId, a.DemandId)); if (a.Target is { } t) Targets[(a.PartyId, a.DemandId)] = t; }
        foreach (var g in p.PortfolioList) Portfolios[g.PortfolioId] = g.PartyId;
        foreach (var c in p.ConcessionList) Concessions[c.PartyId] = c.Units;
    }

    private static void TakeCounter(string party, CounterTerms c)
    {
        Agreed.RemoveWhere(a => a.Party == party);
        foreach (var k in Targets.Keys.Where(k => k.Party == party).ToList()) Targets.Remove(k);
        foreach (var k in Portfolios.Where(kv => kv.Value == party).Select(kv => kv.Key).ToList()) Portfolios.Remove(k);
        foreach (var a in c.Agreements) { Agreed.Add((a.PartyId, a.DemandId)); if (a.Target is { } t) Targets[(a.PartyId, a.DemandId)] = t; }
        foreach (var g in c.Portfolios) Portfolios[g.PortfolioId] = g.PartyId;
    }

    /// <summary>The card for one decision, or null when the decision has no card here (the caller shows it otherwise).</summary>
    public static Control? For(Main main, Session s, Decision d, Action refresh) => d.Kind switch
    {
        DecisionKind.Offer => Offer(s, refresh),
        DecisionKind.Formation => Formation(main, s, refresh),
        DecisionKind.Proposal when s.Politics?.Proposals?.FirstOrDefault(p => p.Id == d.Id) is { } p => TurnPopup.ProposalCard(s, p, refresh),
        DecisionKind.Ultimatum when s.Politics?.Ultimatums?.FirstOrDefault(u => u.PartyId == d.PartyId) is { } u => Ultimatum(main, s, u, refresh),
        DecisionKind.Promises => Promises(s, refresh),
        DecisionKind.OwnUltimatum => OwnUltimatum(main, s, refresh),
        _ => null,
    };

    /// <summary>An AI formateur's offer: who, with whom, on which demands; Accept / Decline.</summary>
    public static Control? Offer(Session s, Action refresh)
    {
        var t = s.T;
        if (s.Politics?.Formation?.Offer is not { } offer) return null;
        var box = Ui.VBox(8);
        box.AddChild(Ui.L(t.T("ui.formation.offer", t.Name(offer.Formateur), string.Join(", ", offer.Partners.Select(t.Name))), Tx.BodyStrong, AppTheme.Ink, true));
        foreach (var a in offer.Agreements)
            if (s.Texts.DemandOf(a.PartyId, a.DemandId) is { } dm) box.AddChild(Ui.PartyName(s, a.PartyId, $"{t.Name(a.PartyId)}: {Ui.Fix(s.Texts.Demand(dm))}", Tx.Body));
        var row = Ui.HBox(1);
        var acc = Ui.Btn(t.T("ui.client.accept"), () => { s.Do(new AcceptOffer(), t.T("ui.formation.joined")); refresh(); }, "", IconKind.Check, null, false, 40);
        var rej = Ui.Btn(t.T("ui.client.reject"), () => { s.Do(new RejectOffer(), t.T("ui.formation.rejected")); refresh(); }, "ButtonSecondary", IconKind.Close, null, false, 40);
        acc.SizeFlagsHorizontal = rej.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(acc); row.AddChild(rej);
        box.AddChild(row);
        box.AddChild(Ui.L(t.T("ui.formation.offer_hint_client"), Tx.Small, AppTheme.Ink3, true));
        CounterSection(s, offer, box, refresh);
        return Ui.Panel(box, "Inset");
    }

    /// <summary>Decision 0016: the invited party's counter-offer - one of its demands and/or a portfolio, the formateur's likely answer, Join on these terms.</summary>
    private static void CounterSection(Session s, CoalitionProposal offer, VBoxContainer box, Action refresh)
    {
        var t = s.T;
        var pol = s.Politics!;
        box.AddChild(Ui.Section(t.T("ui.neg.counter_title")));
        if (pol.Formation?.CounterUsed == true)
        {
            box.AddChild(Ui.L(Ui.Fix(pol.Formation.CounterAnswer == "accepted" ? t.T("ui.neg.counter_sent_yes") : t.T("ui.neg.counter_no", t.Name(offer.Formateur), s.Texts.CounterReason(pol.Formation.CounterAnswer))),
                Tx.Body, AppTheme.WarnText, true));
            return;
        }
        box.AddChild(Ui.L(t.T("ui.neg.counter_hint"), Tx.Small, AppTheme.Ink3, true));
        var own = s.Packs.Parties.First(p => p.Id == pol.PlayerParty);
        var demands = own.Demands.Where(d => !Texts.IsMet(d, s.Country) && !offer.Agreements.Any(a => a.PartyId == own.Id && a.DemandId == d.Id)).ToList();
        var portfolios = s.Game.Portfolios.Where(pf => offer.PortfolioList.All(g => g.PortfolioId != pf.Id)).ToList();
        var dbox = new OptionButton { FocusMode = FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = "CounterDemand" };
        dbox.AddItem(t.T("ui.neg.counter_none"));
        foreach (var d in demands) dbox.AddItem(Ui.Fix(s.Texts.Demand(d)));
        _counterDemand = Math.Clamp(_counterDemand, 0, demands.Count);
        dbox.Selected = _counterDemand;
        var pbox = new OptionButton { FocusMode = FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = "CounterPortfolio" };
        pbox.AddItem(t.T("ui.neg.counter_none"));
        foreach (var pf in portfolios) pbox.AddItem(t.Name(pf.Id));
        _counterPortfolio = Math.Clamp(_counterPortfolio, 0, portfolios.Count);
        pbox.Selected = _counterPortfolio;
        dbox.ItemSelected += i => { _counterDemand = (int)i; refresh(); };
        pbox.ItemSelected += i => { _counterPortfolio = (int)i; refresh(); };
        var pick = Ui.HBox(8); pick.AddChild(dbox); pick.AddChild(pbox);
        box.AddChild(pick);
        var extra = _counterDemand > 0 ? new[] { new AgreedDemand(own.Id, demands[_counterDemand - 1].Id) } : Array.Empty<AgreedDemand>();
        var grants = _counterPortfolio > 0 ? new[] { new PortfolioGrant(own.Id, portfolios[_counterPortfolio - 1].Id) } : Array.Empty<PortfolioGrant>();
        if (extra.Length + grants.Length == 0) return;
        var preview = s.Game.PreviewCounter(extra, grants);
        if (preview != null) box.AddChild(Ui.L(Ui.Fix(s.Texts.CounterPreviewText(preview, offer.Formateur)), Tx.Small, preview.Accepts ? AppTheme.GoodText : AppTheme.WarnText, true));
        var go = Ui.Btn(t.T("ui.neg.counter_go"), () =>
        {
            if (s.Do(new CounterOffer(extra, grants), t.T(preview?.Accepts == true ? "ui.neg.counter_sent_yes" : "ui.neg.counter_sent_no"))) { _counterDemand = 0; _counterPortfolio = 0; }
            refresh();
        }, "ButtonSecondary", IconKind.Arrows, null, preview?.ErrorKey != null, 36);
        go.Name = "CounterGo";
        go.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        box.AddChild(go);
    }

    /// <summary>
    /// The player forms the government: quarters left, the coalition the AI would build with its agreements and seats (Form this coalition), and
    /// Adjust, which opens the draft - invite each seated party, tick its demands, see the majority, send the proposal.
    /// </summary>
    public static Control? Formation(Main main, Session s, Action refresh)
    {
        var t = s.T;
        if (s.Politics is not { Formation: { PlayerIsFormateur: true } f } pol) { ClearDraft(); _adjust = false; return null; }
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        int total = pol.Seats.Values.Sum(), needed = (int)Math.Floor(rules.MajorityFraction * total) + 1;
        var box = Ui.VBox(8);
        box.AddChild(Ui.L(t.T("ui.popup.you_form", f.TurnsLeft), Tx.BodyStrong, AppTheme.WarnText, true));
        var sug = s.Game.SuggestCoalition();
        if (sug != null)
        {
            var parties = new[] { sug.Formateur }.Concat(sug.Partners).ToList();
            int seats = parties.Sum(p => pol.Seats.GetValueOrDefault(p));
            box.AddChild(Ui.L(Ui.Fix(t.T("ui.formation.suggested", string.Join(", ", parties.Select(t.Name)), seats, needed)), Tx.Body, AppTheme.Ink, true));
            foreach (var a in sug.Agreements)
                if (s.Texts.DemandOf(a.PartyId, a.DemandId) is { } dm) box.AddChild(Ui.PartyName(s, a.PartyId, $"{t.Name(a.PartyId)}: {Ui.Fix(s.Texts.Demand(dm))}", Tx.Small));
            foreach (var g in sug.PortfolioList) box.AddChild(Ui.PartyName(s, g.PartyId, t.T("ui.formation.portfolio_line", t.Name(g.PartyId), t.Name(g.PortfolioId)), Tx.Small));
            if (sug.Agreements.Count == 0) box.AddChild(Ui.L(t.T("ui.formation.no_agreements"), Tx.Small, AppTheme.Ink3, true));
        }
        else box.AddChild(Ui.L(t.T("ui.formation.no_suggestion"), Tx.Body, AppTheme.WarnText, true));
        var row = Ui.HBox(1);
        if (sug != null)
        {
            var form = Ui.Btn(t.T("ui.formation.form_suggested"), () => { s.Do(ProposeCoalition.From(sug), t.T("ui.formation.proposed")); refresh(); }, "", IconKind.Check, null, false, 40);
            form.Name = "FormSuggested";
            form.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(form);
        }
        var adjust = Ui.Btn(t.T(_adjust || sug == null ? "ui.formation.adjust_hide" : "ui.formation.adjust"), () =>
        {
            _adjust = !_adjust;
            if (_adjust && Invited.Count == 0 && sug != null) Load(sug);
            refresh();
        }, "ButtonSecondary", IconKind.Gear, null, sug == null, 40);
        adjust.Name = "Adjust";
        adjust.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(adjust);
        box.AddChild(row);
        if (_adjust || sug == null) Draft(s, pol, rules, box, refresh);
        return Ui.Panel(box, "Inset");
    }

    /// <summary>
    /// The negotiation view (decision 0016): seats and majority, slots, portfolios and rounds left; per seated party an invite box and, when invited,
    /// its answer (accepts / counters / refuses with the reason and numbers), a satisfaction bar, the price breakdown, its demands with their status
    /// (acceptable / negotiable / red line / met), a half-way slider for negotiable ones, portfolios, concessions, the fixes and its counter terms;
    /// Send the offer runs one round of talks.
    /// </summary>
    private static void Draft(Session s, CountryPolitics pol, CountryPoliticsDef rules, VBoxContainer card, Action refresh)
    {
        var t = s.T;
        var view = s.Game.AssessCoalition(DraftProposal(pol.PlayerParty));
        if (view == null) return;
        card.AddChild(Ui.Section(t.T("ui.neg.title")));
        card.AddChild(Ui.L(t.T("ui.neg.hint"), Tx.Small, AppTheme.Ink3, true));
        card.AddChild(Ui.L(t.T("ui.reshuffle.seats", view.SeatsHeld, view.SeatsNeeded, t.T(view.SeatsHeld >= view.SeatsNeeded ? "ui.politics.majority" : "ui.politics.no_majority")),
            Tx.BodyStrong, view.SeatsHeld >= view.SeatsNeeded ? AppTheme.GoodText : AppTheme.Ink2, true));
        card.AddChild(Ui.L(t.T("ui.neg.rounds", view.RoundsLeft, view.SlotsUsed, view.Slots, view.PortfoliosGiven, view.PortfoliosMax), Tx.Small, AppTheme.Ink2, true));
        if (view.HardError != null && view.HardError != "error.refused.majority")
            card.AddChild(Ui.L(Ui.Fix(s.Texts.Error(new ActionResult(false, view.HardError, view.HardDetail))), Tx.Small, AppTheme.WarnText, true));
        var replies = pol.Formation?.Replies ?? Array.Empty<PartnerReply>();
        foreach (var party in s.Packs.Parties.Where(p => p.Id != pol.PlayerParty && pol.Seats.GetValueOrDefault(p.Id) > 0).OrderByDescending(p => pol.Seats[p.Id]).ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            string pid = party.Id;
            var line = Ui.HBox(8);
            line.AddChild(new ColorDot(Ui.PartyColor(s, pid)));
            var invite = new CheckBox { Text = $"{t.Name(pid)}  ·  {pol.Seats[pid]}", ButtonPressed = Invited.Contains(pid), FocusMode = FocusModeEnum.None, Name = "Invite_" + pid.Split(':').Last() };
            invite.Toggled += on =>
            {
                if (on) Invited.Add(pid);
                else { Invited.Remove(pid); Agreed.RemoveWhere(a => a.Party == pid); foreach (var k in Portfolios.Where(kv => kv.Value == pid).Select(kv => kv.Key).ToList()) Portfolios.Remove(k); Concessions.Remove(pid); }
                refresh();
            };
            line.AddChild(invite);
            var v = view.Partners.FirstOrDefault(x => x.PartyId == pid);
            if (v != null)
            {
                line.AddChild(new Meter(Math.Min(v.Satisfaction, 1.5), 0, 1.5, v.Satisfaction >= 1 ? AppTheme.Good : v.Satisfaction >= 0.7 ? AppTheme.Warn : AppTheme.Bad, 110, false, 8)
                    { TooltipText = t.T("ui.neg.satisfaction", Ui.Num(v.Satisfaction * 100, "0")), SizeFlagsVertical = SizeFlags.ShrinkCenter });
                line.AddChild(Ui.Pill(s.Texts.Verdict(v.Verdict), v.Verdict == "accept" ? AppTheme.Good : v.Verdict == "counter" ? AppTheme.Warn : AppTheme.Bad));
            }
            card.AddChild(line);
            if (v == null) continue;
            var inner = Ui.VBox(4);
            inner.AddChild(Ui.L(Ui.Fix(s.Texts.NegotiationReason(v)), Tx.Body, v.Verdict == "accept" ? AppTheme.GoodText : v.Verdict == "counter" ? AppTheme.WarnText : AppTheme.BadText, true));
            inner.AddChild(Ui.L(Ui.Fix(s.Texts.PriceBreakdown(v)), Tx.Small, AppTheme.Ink3, true));
            if (replies.FirstOrDefault(r => r.PartyId == pid) is { } last)
                inner.AddChild(Ui.L(t.T("ui.neg.last_replies") + ": " + s.Texts.Verdict(last.Verdict), Tx.Small, AppTheme.Ink2, true));
            foreach (var dm in party.Demands)
            {
                var dv = v.Demands.First(x => x.DemandId == dm.Id);
                string did = dm.Id;
                var drow = Ui.HBox(8);
                var box = new CheckBox { Text = $"{Ui.Fix(s.Texts.Demand(dm))}   (+{Fmt.Number(dv.Worth, "0.0")})", ButtonPressed = dv.Agreed, FocusMode = FocusModeEnum.None, Disabled = dv.Status == "met" };
                box.AddThemeColorOverride("font_color", AppTheme.Ink2);
                box.Toggled += on => { if (on) Agreed.Add((pid, did)); else { Agreed.Remove((pid, did)); Targets.Remove((pid, did)); } refresh(); };
                box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                drow.AddChild(box);
                drow.AddChild(Ui.Pill(s.Texts.DemandStatus(dv.Status), dv.Status switch { "red_line" => AppTheme.Bad, "negotiable" => AppTheme.Warn, "met" => AppTheme.Ink3, _ => AppTheme.Good }));
                var indent = Ui.Margin(drow, 0, 0);
                indent.AddThemeConstantOverride("margin_left", 30);
                inner.AddChild(indent);
                // a counter value for a negotiable demand: between the player's own position and the partner's target
                if (dv.Agreed && dv.Anchor is { } anchor && s.Packs.Policies.FirstOrDefault(p => p.Id == dm.Subject) is { } pdef)
                {
                    var srow = Ui.HBox(8);
                    double lo = Math.Min(anchor, dm.Target), hi = Math.Max(anchor, dm.Target);
                    double cur = Targets.TryGetValue((pid, did), out var tv) ? tv : dm.Target;
                    var slider = new PolicySlider(compact: true) { GovMarker = anchor };
                    slider.Configure(lo, hi, Math.Max((hi - lo) / 100.0, 1e-3), dm.Target, null, cur);
                    var value = Ui.Right(Ui.L(Ui.Fix(Fmt.Policy(pdef, cur, t)), Tx.Data));
                    value.CustomMinimumSize = new Vector2(90, 0);
                    slider.TargetChanged += x => { value.Text = Ui.Fix(Fmt.Policy(pdef, x, t)); if (Math.Abs(x - dm.Target) < 1e-9) Targets.Remove((pid, did)); else Targets[(pid, did)] = x; };
                    srow.AddChild(slider); srow.AddChild(value);
                    srow.AddChild(Ui.Btn(t.T("ui.neg.half_way"), () => { Targets[(pid, did)] = Negotiation.HalfWay(dm, anchor); refresh(); }, "ButtonGhost", null, null, false, 28));
                    var si = Ui.Margin(srow, 0, 0);
                    si.AddThemeConstantOverride("margin_left", 60);
                    inner.AddChild(si);
                }
            }
            // portfolios (the four this party values most, and any it holds) and concessions
            var pfFlow = new HFlowContainer();
            pfFlow.AddThemeConstantOverride("h_separation", 10);
            pfFlow.AddChild(Ui.L(t.T("ui.neg.portfolios") + ":", Tx.Label));
            foreach (var pv in v.Portfolios.Where(x => x.HeldBy == null || x.HeldBy == pid).OrderByDescending(x => x.HeldBy == pid).ThenByDescending(x => x.Worth).Take(4))
            {
                string pfid = pv.PortfolioId;
                var cb = new CheckBox { Text = $"{t.Name(pfid)} (+{Fmt.Number(pv.Worth, "0.0")})", ButtonPressed = pv.HeldBy == pid, FocusMode = FocusModeEnum.None, Name = "Portfolio_" + pfid.Split(':').Last() };
                cb.Toggled += on => { if (on) Portfolios[pfid] = pid; else Portfolios.Remove(pfid); refresh(); };
                pfFlow.AddChild(cb);
            }
            inner.AddChild(pfFlow);
            var crow = Ui.HBox(8);
            crow.AddChild(Ui.L(t.T("ui.neg.concessions") + ":", Tx.Label));
            crow.AddChild(Ui.Segmented(Enumerable.Range(0, Negotiation.MaxConcessions + 1).Select(u => u == 0 ? "0" : $"{u} · {u * (int)Negotiation.ConcessionCost}").ToArray(),
                Concessions.GetValueOrDefault(pid), i => { Concessions[pid] = i; refresh(); }));
            inner.AddChild(crow);
            foreach (var f in v.Fixes.Take(5)) inner.AddChild(Ui.L("• " + Ui.Fix(s.Texts.FixText(f)), Tx.Small, AppTheme.Ink2, true));
            if (v.Counter is { } counter)
            {
                var cr = Ui.HBox(8);
                var ct = Ui.L(Ui.Fix(s.Texts.CounterText(counter)), Tx.Small, AppTheme.WarnText, true);
                ct.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                cr.AddChild(ct);
                var take = Ui.Btn(t.T("ui.neg.use_counter"), () => { TakeCounter(pid, counter); refresh(); }, "ButtonSecondary", IconKind.Check, null, false, 30);
                take.Name = "TakeCounter_" + pid.Split(':').Last();
                cr.AddChild(take);
                inner.AddChild(cr);
            }
            var wrap = Ui.Margin(inner, 0, 0);
            wrap.AddThemeConstantOverride("margin_left", 28);
            card.AddChild(wrap);
        }
        var buttons = Ui.HBox(8);
        var send = Ui.Btn(t.T("ui.neg.send") + (view.ConcessionCost > 0 ? $" · {view.ConcessionCost:0}" : ""), () =>
        {
            if (s.Do(new NegotiateCoalition(DraftProposal(pol.PlayerParty).Partners, DraftProposal(pol.PlayerParty).Agreements, DraftProposal(pol.PlayerParty).Portfolios,
                    DraftProposal(pol.PlayerParty).Concessions), t.T(view.AllAccept ? "ui.neg.formed" : "ui.neg.replies")) && view.AllAccept)
            { ClearDraft(); _adjust = false; }
            refresh();
        }, "", IconKind.Check, s.Texts.Explain("negotiation"), view.RoundsLeft <= 0 || view.HardError != null);
        send.Name = "SendOffer";
        buttons.AddChild(send);
        card.AddChild(buttons);
    }

    /// <summary>
    /// A partner's ultimatum to the player's government: what it wants and by when, and what the player can do about it right here - deliver the
    /// demand (opens the policy or indicator), court the partner, reshuffle it out, dismiss it - or let it walk out.
    /// </summary>
    public static Control Ultimatum(Main main, Session s, UltimatumInfo u, Action refresh)
    {
        var t = s.T;
        var box = Ui.VBox(8);
        var head = Ui.HBox(10);
        head.AddChild(new IconView(IconKind.Alert, 18, AppTheme.WarnText));
        var dm = u.DemandId != null ? s.Texts.DemandOf(u.PartyId, u.DemandId) : null;
        string text = dm != null ? t.T("ui.event.ultimatum_demand", t.Name(u.PartyId), u.TurnsLeft, s.Texts.Demand(dm)) : t.T("ui.event.ultimatum", t.Name(u.PartyId), u.TurnsLeft);
        head.AddChild(Ui.L(Ui.Fix(text), Tx.BodyStrong, AppTheme.WarnText, true));
        box.AddChild(head);
        box.AddChild(Ui.L(t.T("ui.ultimatum.options"), Tx.Small, AppTheme.Ink2, true));
        var row = Ui.HBox(6);
        if (dm != null)
        {
            string kind = dm.Kind == "policy" ? "policy" : "value";
            var deliver = Ui.Btn(t.T("ui.ultimatum.deliver", t.Name(dm.Subject)), () => main.OpenDetail(kind, dm.Subject), "", IconKind.Policies, null, false, 36);
            deliver.Name = "UltimatumDeliver";
            row.AddChild(deliver);
        }
        string pid = u.PartyId;
        var court = Ui.Costed(t.T("ui.ultimatum.court", t.Name(pid)), (int)PartyRules.CourtCost, () => { s.Do(new Court(pid), t.T("ui.party.court_ok", t.Name(pid))); refresh(); }, null, false, "ButtonSecondary");
        court.Name = "UltimatumCourt";
        row.AddChild(court);
        row.AddChild(Ui.Btn(t.T("ui.client.reshuffle"), () => { main.CloseDialogs(); var sc = main.Screens.OfType<PoliticsScreen>().First(); main.Open(sc); Callable.From(sc.ScrollToReshuffle).CallDeferred(); },
            "ButtonSecondary", IconKind.Arrows, null, false, 36));
        bool canDismiss = s.Politics is { } pol && CoalitionRules.HasMajority(pol.GovernmentParties.Where(p => p != pid), pol.Seats,
            s.Packs.Countries.First(c => c.Politics != null).Politics!.MajorityFraction);
        row.AddChild(Ui.Costed(t.T("ui.client.dismiss"), (int)PartyRules.DismissCost, () => { s.Do(new DismissPartner(pid), t.T("ui.exit.dismiss_ok", t.Name(pid))); refresh(); },
            canDismiss ? null : t.T("ui.err.would_lose_majority"), !canDismiss, "ButtonDanger"));
        var flow = new HFlowContainer();
        foreach (var c in row.GetChildren().OfType<Control>().ToList()) { row.RemoveChild(c); flow.AddChild(c); }
        flow.AddThemeConstantOverride("h_separation", 6); flow.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(flow);
        return Ui.Panel(box, "Warn");
    }

    /// <summary>Decision 0016: the party's own ultimatum to the leader - the countdown and the leader's answer, Withdraw, and a link to the Actions tab.</summary>
    public static Control? OwnUltimatum(Main main, Session s, Action refresh)
    {
        var t = s.T;
        if (s.Politics is not { PlayerUltimatum: { } u, Leader: { } leader }) return null;
        var box = Ui.VBox(8);
        var head = Ui.HBox(10);
        head.AddChild(new IconView(IconKind.Alert, 18, u.Answer == "refuse" ? AppTheme.BadText : AppTheme.WarnText));
        head.AddChild(Ui.L(Ui.Fix(s.Texts.UltimatumStatus(u, leader)), Tx.BodyStrong, u.Answer == "refuse" ? AppTheme.BadText : AppTheme.WarnText, true));
        box.AddChild(head);
        var row = Ui.HBox(6);
        var withdraw = Ui.Btn(t.T("ui.myult.withdraw"), () => { s.Do(new WithdrawUltimatum(), t.T("ui.myult.withdrawn")); refresh(); }, "ButtonSecondary", IconKind.Back, s.Texts.Explain("ultimatum"), false, 36);
        withdraw.Name = "BriefUltimatumWithdraw";
        row.AddChild(withdraw);
        row.AddChild(Ui.Btn(t.T("ui.client.tab.party"), () => { main.CloseDialogs(); main.OpenScreen<PartyScreen>(); }, "ButtonGhost", IconKind.Party, null, false, 36));
        box.AddChild(row);
        return Ui.Panel(box, "Warn");
    }

    /// <summary>The window before an election: each goal the party could promise, with a Promise button, and at which election it would be judged.</summary>
    public static Control? Promises(Session s, Action refresh)
    {
        var t = s.T;
        if (s.Politics is not { Party: { } ps } pol) return null;
        var list = Decisions.Promisable(s.Country, s.Packs);
        if (list.Count == 0) return null;
        int interval = s.Packs.Countries.FirstOrDefault(c => c.Politics != null)?.Politics?.ElectionIntervalTurns ?? 16;
        int fresh = ps.PromiseDetails?.Count(p => !p.Carried) ?? 0;
        var box = Ui.VBox(6);
        box.AddChild(Ui.L(t.T("ui.popup.promises", pol.TurnsToElection), Tx.BodyStrong, AppTheme.Ink, true));
        box.AddChild(Ui.L(t.T("ui.client.promise_when", pol.TurnsToElection + interval, fresh, PoliticsLimits.MaxPromises), Tx.Small, AppTheme.Ink2, true));
        foreach (var dm in list)
        {
            var row = Ui.HBox(10);
            row.AddChild(new PictoDisc(Icons.For(dm.Subject), 24));
            var l = Ui.L(Ui.Fix(s.Texts.Demand(dm)), Tx.Body, AppTheme.Ink, true);
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(l);
            string id = dm.Id;
            var b = Ui.Costed(t.T("ui.client.promise"), (int)PartyRules.PromiseCost, () => { s.Do(new MakePromise(id), t.T("ui.party.promise_ok", s.Texts.Demand(dm))); refresh(); }, null, false, "ButtonSecondary");
            b.Name = "Promise_" + id;
            row.AddChild(b);
            box.AddChild(row);
        }
        box.AddChild(Ui.L(t.T("ui.popup.promises_optional"), Tx.Small, AppTheme.Ink3, true));
        return Ui.Panel(box, "Inset");
    }
}
