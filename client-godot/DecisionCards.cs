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
    private static bool _adjust;

    /// <summary>The card for one decision, or null when the decision has no card here (the caller shows it otherwise).</summary>
    public static Control? For(Main main, Session s, Decision d, Action refresh) => d.Kind switch
    {
        DecisionKind.Offer => Offer(s, refresh),
        DecisionKind.Formation => Formation(main, s, refresh),
        DecisionKind.Proposal when s.Politics?.Proposals?.FirstOrDefault(p => p.Id == d.Id) is { } p => TurnPopup.ProposalCard(s, p, refresh),
        DecisionKind.Ultimatum when s.Politics?.Ultimatums?.FirstOrDefault(u => u.PartyId == d.PartyId) is { } u => Ultimatum(main, s, u, refresh),
        DecisionKind.Promises => Promises(s, refresh),
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
        return Ui.Panel(box, "Inset");
    }

    /// <summary>
    /// The player forms the government: quarters left, the coalition the AI would build with its agreements and seats (Form this coalition), and
    /// Adjust, which opens the draft - invite each seated party, tick its demands, see the majority, send the proposal.
    /// </summary>
    public static Control? Formation(Main main, Session s, Action refresh)
    {
        var t = s.T;
        if (s.Politics is not { Formation: { PlayerIsFormateur: true } f } pol) { Invited.Clear(); Agreed.Clear(); _adjust = false; return null; }
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
            if (sug.Agreements.Count == 0) box.AddChild(Ui.L(t.T("ui.formation.no_agreements"), Tx.Small, AppTheme.Ink3, true));
        }
        else box.AddChild(Ui.L(t.T("ui.formation.no_suggestion"), Tx.Body, AppTheme.WarnText, true));
        var row = Ui.HBox(1);
        if (sug != null)
        {
            var form = Ui.Btn(t.T("ui.formation.form_suggested"), () => { s.Do(new ProposeCoalition(sug.Partners, sug.Agreements), t.T("ui.formation.proposed")); refresh(); }, "", IconKind.Check, null, false, 40);
            form.Name = "FormSuggested";
            form.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(form);
        }
        var adjust = Ui.Btn(t.T(_adjust || sug == null ? "ui.formation.adjust_hide" : "ui.formation.adjust"), () =>
        {
            _adjust = !_adjust;
            if (_adjust && Invited.Count == 0 && sug != null) { Invited.UnionWith(sug.Partners); Agreed.UnionWith(sug.Agreements.Select(a => (a.PartyId, a.DemandId))); }
            refresh();
        }, "ButtonSecondary", IconKind.Gear, null, sug == null, 40);
        adjust.Name = "Adjust";
        adjust.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(adjust);
        box.AddChild(row);
        if (_adjust || sug == null) Draft(s, pol, rules, box, refresh);
        return Ui.Panel(box, "Inset");
    }

    private static void Draft(Session s, CountryPolitics pol, CountryPoliticsDef rules, VBoxContainer card, Action refresh)
    {
        var t = s.T;
        int total = pol.Seats.Values.Sum();
        int held = pol.Seats.GetValueOrDefault(pol.PlayerParty) + Invited.Sum(p => pol.Seats.GetValueOrDefault(p));
        card.AddChild(Ui.L(t.T("ui.formation.majority_needed", (int)Math.Floor(rules.MajorityFraction * total), held, rules.ProgramSlots),
            Tx.BodyStrong, held > total * rules.MajorityFraction ? AppTheme.GoodText : AppTheme.Ink2, true));
        foreach (var party in s.Packs.Parties.Where(p => p.Id != pol.PlayerParty && pol.Seats.GetValueOrDefault(p.Id) > 0).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var line = Ui.HBox(8);
            line.AddChild(new ColorDot(Ui.PartyColor(s, party.Id)));
            var invite = new CheckBox
            {
                Text = $"{t.Name(party.Id)}  ·  {Ui.Fix(t.T("ui.formation.party_line", pol.Seats[party.Id], Fmt.Number(CoalitionRules.RequiredWeight(party, pol.Party?.Relations.GetValueOrDefault(party.Id, 0.3) ?? 0.3))))}",
                ButtonPressed = Invited.Contains(party.Id), FocusMode = FocusModeEnum.None,
            };
            string pid = party.Id;
            invite.Toggled += on => { if (on) Invited.Add(pid); else { Invited.Remove(pid); Agreed.RemoveWhere(a => a.Party == pid); } refresh(); };
            line.AddChild(invite);
            card.AddChild(line);
            if (!Invited.Contains(party.Id)) continue;
            foreach (var dm in party.Demands)
            {
                string did = dm.Id;
                var box = new CheckBox
                {
                    Text = $"{Ui.Fix(s.Texts.Demand(dm))}   ({Ui.Fix(t.T("ui.formation.weight", Fmt.Number(dm.Weight)))})",
                    ButtonPressed = Agreed.Contains((pid, did)), FocusMode = FocusModeEnum.None,
                };
                box.AddThemeColorOverride("font_color", AppTheme.Ink2);
                box.Toggled += on => { if (on) Agreed.Add((pid, did)); else Agreed.Remove((pid, did)); refresh(); };
                var indent = Ui.Margin(box, 0, 0);
                indent.AddThemeConstantOverride("margin_left", 36);
                card.AddChild(indent);
            }
        }
        var submit = Ui.Btn(t.T("ui.client.submit"), () =>
        {
            if (s.Do(new ProposeCoalition(Invited.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    Agreed.OrderBy(a => a.Party, StringComparer.Ordinal).ThenBy(a => a.Demand, StringComparer.Ordinal).Select(a => new AgreedDemand(a.Party, a.Demand)).ToList()),
                    t.T("ui.formation.proposed")))
            { Invited.Clear(); Agreed.Clear(); _adjust = false; }
            refresh();
        }, "", IconKind.Check);
        submit.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        card.AddChild(submit);
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
