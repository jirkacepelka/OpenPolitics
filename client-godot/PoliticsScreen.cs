using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>Parliament, polls, the compass, government formation (as formateur or when offered a place) and the coalition.</summary>
public sealed class PoliticsScreen : Screen
{
    public override string TitleKey => "ui.client.tab.politics";
    public override IconKind Icon => IconKind.Politics;
    public override string TabId => "politics";
    public override float Width => 1200;

    private readonly HashSet<string> _rsDrop = new(StringComparer.Ordinal), _rsAdd = new(StringComparer.Ordinal);
    private readonly HashSet<(string Party, string Demand)> _rsAgreed = new();
    private Control? _rsCard;

    /// <summary>Scrolls the dialog so the reshuffle card is in view.</summary>
    public void ScrollToReshuffle()
    {
        if (_rsCard == null || !GodotObject.IsInstanceValid(_rsCard)) return;
        for (Node? n = _rsCard.GetParent(); n != null; n = n.GetParent())
            if (n is ScrollContainer sc) { sc.EnsureControlVisible(_rsCard); return; }
    }
    private Session? _s; private Dialog? _d; private Main? _main;

    private void Rebuild() { if (_s != null && _d != null && _main != null && GodotObject.IsInstanceValid(_d)) { Ui.Clear(_d.Body); Build(_s, _d, _main); } }

    private static Control Card(string title, Control body)
    {
        var box = Ui.VBox(10);
        box.AddChild(Ui.L(title, Tx.Overline, AppTheme.Ink3));
        box.AddChild(body);
        return Ui.Panel(box, "Card");
    }

    public override void Build(Session s, Dialog d, Main main)
    {
        _s = s; _d = d; _main = main;
        var t = s.T;
        var pol = s.Politics;
        if (pol == null) { d.Body.AddChild(Ui.L(t.T("ui.err.no_politics"), Tx.Body)); return; }
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p));
        bool majority = held > total * rules.MajorityFraction;

        Formation(s, pol, d.Body, main);
        foreach (var p in pol.Proposals ?? Array.Empty<ProposalInfo>()) d.Body.AddChild(TurnPopup.ProposalCard(s, p, Rebuild));
        if (pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader)
            foreach (var u in pol.Ultimatums ?? Array.Empty<UltimatumInfo>()) d.Body.AddChild(DecisionCards.Ultimatum(main, s, u, Rebuild));

        var grid = new FlowGrid { MinColumn = 330, MaxColumns = 3, Gap = 12 };
        // Parliament
        var parl = Ui.VBox(8);
        var hemi = new Hemicycle { CustomMinimumSize = new Vector2(260, 160) };
        var order = s.Packs.Parties.Select(p => p.Id).OrderBy(id => s.Game.Compass.TryGetValue(id, out var c) ? c.X : 0).ThenBy(id => id, StringComparer.Ordinal);
        hemi.Set(order.Select(id => (Ui.PartyColor(s, id), pol.GovernmentParties.Contains(id), pol.Seats.GetValueOrDefault(id), t.Name(id))), (float)rules.MajorityFraction,
            $"/ {total} · " + t.T(majority ? "ui.rail.majority" : "ui.rail.no_majority"));
        parl.AddChild(hemi);
        var status = Ui.HBox(8);
        status.AddChild(Ui.Pill(t.T(majority ? "ui.rail.majority" : "ui.rail.no_majority"), majority ? AppTheme.Good : AppTheme.Bad));
        status.AddChild(Ui.Pill(t.T("ui.line.next_election") + ": " + (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.politics.election_in", pol.TurnsToElection)), AppTheme.Chrome));
        parl.AddChild(status);
        if (pol.Leader != null) parl.AddChild(Ui.L($"{t.T("ui.politics.leader")}: {t.Name(pol.Leader)}", Tx.Body, AppTheme.Ink));
        if (pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader)
        {
            // the reshuffle card sits below the grid: this jumps to it
            var jump = Ui.Btn(t.T("ui.client.reshuffle"), ScrollToReshuffle, "ButtonSecondary", IconKind.Arrows);
            jump.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            parl.AddChild(jump);
        }
        foreach (var id in pol.GovernmentParties.Concat(pol.Seats.Keys.Where(k => !pol.GovernmentParties.Contains(k) && pol.Seats[k] > 0).OrderByDescending(k => pol.Seats[k])))
        {
            var row = Ui.HBox(8);
            var pn = Ui.PartyName(s, id, t.Name(id) + (pol.GovernmentParties.Contains(id) ? "  ★" : ""), Tx.Label);
            pn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(pn);
            row.AddChild(Ui.L(pol.Seats.GetValueOrDefault(id).ToString(), Tx.Data));
            parl.AddChild(PartyButton(main, id, row));
        }
        grid.AddChild(Card(t.T("ui.pp.parliament"), parl));

        // Polls
        var polls = Ui.VBox(4);
        double max = Math.Max(0.3, pol.Support.Values.DefaultIfEmpty(0).Max());
        double threshold = s.Game.ElectoralThreshold ?? 0;
        foreach (var (id, share) in pol.Support.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var row = Ui.HBox(8);
            bool me = id == pol.PlayerParty;
            var name = Ui.PartyName(s, id, t.Name(id) + (me ? " (" + t.T("ui.politics.you") + ")" : ""), me ? Tx.BodyStrong : Tx.Label);
            name.CustomMinimumSize = new Vector2(Layout.Compact ? 130 : 170, 0);
            foreach (var l in name.GetChildren().OfType<Label>()) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.CustomMinimumSize = new Vector2(40, 0); }
            row.AddChild(name);
            var bar = new Meter(share, 0, max, Ui.PartyColor(s, id), 60, false, 12) { SizeFlagsHorizontal = SizeFlags.ExpandFill, Mark = threshold > 0 ? (float)(threshold / max) : null };
            row.AddChild(bar);
            var pct = Ui.Right(Ui.L(Ui.Num(share * 100) + " %", Tx.Data));
            pct.CustomMinimumSize = new Vector2(54, 0);
            row.AddChild(pct);
            var seats = Ui.Right(Ui.L(pol.Seats.GetValueOrDefault(id).ToString(), Tx.Data, AppTheme.Ink3));
            seats.CustomMinimumSize = new Vector2(30, 0);
            row.AddChild(seats);
            polls.AddChild(PartyButton(main, id, row));
        }
        if (threshold > 0) polls.AddChild(Ui.L(t.T("ui.pp.threshold", Ui.Num(threshold * 100, "0.#")), Tx.Small));
        if (Charts.All.FirstOrDefault(c => c.Id == "polls") is { } pspec)
        {
            var b = Ui.Btn(t.T("ui.map.open_chart"), () => main.OpenChart(pspec), "ButtonSecondary", IconKind.Chart);
            b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            polls.AddChild(b);
        }
        grid.AddChild(Card(t.T("ui.politics.polls"), polls));

        // Compass
        var compass = new CompassView { CustomMinimumSize = new Vector2(240, 260), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        compass.Set(s, pol.PlayerParty);
        grid.AddChild(Card(t.T("ui.client.compass"), compass));

        // Coalition
        var coal = Coalition(s, pol, main);
        if (coal != null) { coal.SetMeta("span", 2); grid.AddChild(coal); }
        d.Body.AddChild(grid);
        if (pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader) { _rsCard = Reshuffle(s, pol); d.Body.AddChild(_rsCard); }
        else { _rsDrop.Clear(); _rsAdd.Clear(); _rsAgreed.Clear(); }
        if (pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader)
        {
            var b = Ui.Btn(t.T("ui.client.early_election"), () => s.Do(new CallEarlyElection(), t.T("ui.formation.early_called")), "ButtonDanger", IconKind.Ballot);
            b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            d.Body.AddChild(b);
        }
    }

    /// <summary>
    /// The reshuffle card (decision 0013): partners to drop, outside parties to bring in with the demands they ask for, the seats the new
    /// coalition would hold, what the parties would answer, cost and cooldown, and the confirm button.
    /// </summary>
    private Control Reshuffle(Session s, CountryPolitics pol)
    {
        var t = s.T;
        var box = Ui.VBox(12);
        var head = Ui.HBox(10);
        head.AddChild(new PictoDisc(IconKind.Arrows, 34));
        head.AddChild(Ui.L(t.T("ui.client.reshuffle"), Tx.Heading));
        box.AddChild(head);
        box.AddChild(Ui.L(t.T("ui.client.reshuffle_hint") + " " + s.Texts.Explain("reshuffle"), Tx.Small, AppTheme.Ink2, true));
        var info = pol.Reshuffle;
        if (info is { Queued: true })
        {
            box.AddChild(Ui.Panel(Ui.L(t.T("ui.err.already_queued"), Tx.BodyStrong, AppTheme.WarnText, true), "Warn"));
            return Ui.Panel(box, "Card");
        }
        var partners = pol.GovernmentParties.Where(p => p != pol.PlayerParty).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var outside = pol.Seats.Where(kv => kv.Value > 0 && !pol.GovernmentParties.Contains(kv.Key)).OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToList();
        _rsDrop.RemoveWhere(p => !partners.Contains(p)); _rsAdd.RemoveWhere(p => !outside.Contains(p)); _rsAgreed.RemoveWhere(a => !_rsAdd.Contains(a.Party));

        var dropCol = Ui.VBox(6);
        dropCol.AddChild(Ui.L(t.T("ui.client.reshuffle_drop"), Tx.Overline, AppTheme.Ink3));
        foreach (var id in partners)
        {
            var line = Ui.HBox(8);
            line.AddChild(new ColorDot(Ui.PartyColor(s, id)));
            string loyalty = pol.Loyalty?.TryGetValue(id, out var l) == true ? " · " + Ui.Fix(t.T("ui.coalition.loyalty", Fmt.Number(l, "0"))) : "";
            var cb = new CheckBox { Text = $"{t.Name(id)} · {pol.Seats.GetValueOrDefault(id)}{loyalty}", ButtonPressed = _rsDrop.Contains(id), FocusMode = Control.FocusModeEnum.None };
            cb.Toggled += on => { if (on) _rsDrop.Add(id); else _rsDrop.Remove(id); Rebuild(); };
            line.AddChild(cb);
            dropCol.AddChild(line);
        }
        if (partners.Count == 0) dropCol.AddChild(Ui.L("–", Tx.Body, AppTheme.Ink3));

        var addCol = Ui.VBox(6);
        addCol.AddChild(Ui.L(t.T("ui.client.reshuffle_add"), Tx.Overline, AppTheme.Ink3));
        foreach (var id in outside)
        {
            var def = s.Packs.Parties.First(p => p.Id == id);
            var line = Ui.HBox(8);
            line.AddChild(new ColorDot(Ui.PartyColor(s, id)));
            double price = CoalitionRules.RequiredWeight(def, pol.Party?.Relations.GetValueOrDefault(id, 0.3) ?? 0.3,
                pol.Grudge?.GetValueOrDefault(id + "|" + pol.PlayerParty) ?? 0);
            var cb = new CheckBox { Text = $"{t.Name(id)}  ·  {Ui.Fix(t.T("ui.formation.party_line", pol.Seats[id], Fmt.Number(price)))}", ButtonPressed = _rsAdd.Contains(id), FocusMode = Control.FocusModeEnum.None };
            cb.Toggled += on =>
            {
                if (on)
                {
                    _rsAdd.Add(id);
                    foreach (var a in s.Game.SuggestReshuffle(_rsDrop.ToList(), new[] { id }) ?? Array.Empty<AgreedDemand>()) _rsAgreed.Add((a.PartyId, a.DemandId));
                }
                else { _rsAdd.Remove(id); _rsAgreed.RemoveWhere(a => a.Party == id); }
                Rebuild();
            };
            line.AddChild(cb);
            addCol.AddChild(line);
            if (!_rsAdd.Contains(id)) continue;
            foreach (var dm in def.Demands)
            {
                var dcb = new CheckBox { Text = $"{Ui.Fix(s.Texts.Demand(dm))}   ({Ui.Fix(t.T("ui.formation.weight", Fmt.Number(dm.Weight)))})", ButtonPressed = _rsAgreed.Contains((id, dm.Id)), FocusMode = Control.FocusModeEnum.None };
                dcb.AddThemeColorOverride("font_color", AppTheme.Ink2);
                dcb.Toggled += on => { if (on) _rsAgreed.Add((id, dm.Id)); else _rsAgreed.Remove((id, dm.Id)); Rebuild(); };
                var indent = Ui.Margin(dcb, 0, 0);
                indent.AddThemeConstantOverride("margin_left", 30);
                addCol.AddChild(indent);
            }
        }
        if (_rsAdd.Count > 0)
        {
            var auto = Ui.Btn(t.T("ui.client.reshuffle_auto"), () =>
            {
                _rsAgreed.Clear();
                foreach (var a in s.Game.SuggestReshuffle(_rsDrop.ToList(), _rsAdd.ToList()) ?? Array.Empty<AgreedDemand>()) _rsAgreed.Add((a.PartyId, a.DemandId));
                Rebuild();
            }, "ButtonSecondary", IconKind.Check);
            auto.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            addCol.AddChild(auto);
        }
        box.AddChild(Detail.Columns(dropCol, addCol, 1f));

        // What would happen.
        var drop = _rsDrop.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var add = _rsAdd.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var agreements = _rsAgreed.OrderBy(a => a.Party, StringComparer.Ordinal).ThenBy(a => a.Demand, StringComparer.Ordinal).Select(a => new AgreedDemand(a.Party, a.Demand)).ToList();
        var preview = s.Game.PreviewReshuffle(drop, add, agreements);
        var status = Ui.VBox(6);
        bool nothing = drop.Count == 0 && add.Count == 0;
        if (!nothing)
            status.AddChild(Ui.L(Ui.Fix(t.T("ui.reshuffle.seats", preview.SeatsHeld, preview.SeatsNeeded, t.T(preview.Majority ? "ui.politics.majority" : "ui.politics.no_majority"))),
                Tx.BodyStrong, preview.Majority ? AppTheme.GoodText : AppTheme.BadText, true));
        if (preview.ErrorKey != null && preview.ErrorKey != "error.no_change")
            status.AddChild(Ui.L(s.Texts.Error(new ActionResult(false, preview.ErrorKey, preview.ErrorDetail)), Tx.Body, AppTheme.BadText, true));
        else if (!nothing)
            status.AddChild(Ui.L(preview.Refusals.Count == 0 ? t.T("ui.reshuffle.agree")
                : t.T("ui.reshuffle.would_refuse", string.Join("; ", preview.Refusals.Select(r => (r.PartyId != null ? t.Name(r.PartyId) + ": " : "") + s.Texts.ReshuffleReason(r.Reason)))),
                Tx.Body, preview.Refusals.Count == 0 ? AppTheme.GoodText : AppTheme.WarnText, true));
        status.AddChild(Ui.L(Ui.Fix(t.T("ui.reshuffle.cost", Fmt.Number(preview.Cost, "0"), Fmt.Number(PoliticsLimits.ReshuffleRefusalCost, "0"), PoliticsLimits.ReshuffleCooldownTurns)), Tx.Small, AppTheme.Ink3, true));
        status.AddChild(Ui.L(info is { CooldownLeft: > 0 } ? t.T("ui.reshuffle.cooldown_left", info.CooldownLeft) : t.T("ui.reshuffle.cooldown_now"), Tx.Small,
            info is { CooldownLeft: > 0 } ? AppTheme.WarnText : AppTheme.Ink3, true));
        bool can = preview.ErrorKey == null && preview.Affordable;
        string? why = preview.ErrorKey != null ? s.Texts.Error(new ActionResult(false, preview.ErrorKey, preview.ErrorDetail)) : !preview.Affordable ? t.T("ui.err.not_enough_capital") : null;
        var go = Ui.Costed(t.T("ui.client.reshuffle_go"), (int)preview.Cost, () =>
        {
            if (s.Do(new ReshuffleCoalition(drop, add, agreements), t.T("ui.reshuffle.ok", Fmt.Number(preview.Cost, "0"))))
            { _rsDrop.Clear(); _rsAdd.Clear(); _rsAgreed.Clear(); }
        }, why, !can, preview.Refusals.Count > 0 ? "ButtonDanger" : "");
        go.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        status.AddChild(go);
        box.AddChild(Ui.Panel(status, "Inset"));
        return Ui.Panel(box, "Card");
    }

    private static Control PartyButton(Main main, string id, Control row)
    {
        var b = new Button { ThemeTypeVariation = "ButtonGhost", CustomMinimumSize = new Vector2(0, 28), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddChild(row); row.SetAnchorsPreset(Control.LayoutPreset.FullRect); Detail.SetIgnore(row);
        b.Pressed += () => main.OpenDetail("party", id);
        return b;
    }

    private void Formation(Session s, CountryPolitics pol, VBoxContainer body, Main main)
    {
        var t = s.T;
        if (pol.Phase == Phase.Caretaker)
        {
            body.AddChild(Ui.Panel(Ui.L(t.T("ui.formation.caretaker"), Tx.BodyStrong, AppTheme.WarnText, true), "Warn"));
            return;
        }
        if (pol.Phase != Phase.Formation || pol.Formation == null) return;
        var f = pol.Formation;
        var box = Ui.VBox(10);
        box.AddChild(Ui.L(t.T("ui.formation.heading"), Tx.Heading));
        // the same cards as in the briefing (decision 0015): the offer, or the player's own formation with the suggested coalition and the draft
        if (DecisionCards.Offer(s, Rebuild) is { } offer) box.AddChild(offer);
        else if (DecisionCards.Formation(main, s, Rebuild) is { } form) box.AddChild(form);
        else box.AddChild(Ui.L(t.T("ui.formation.ai_forms", t.Name(f.Formateur), f.Attempt), Tx.Body, AppTheme.Ink, true));
        body.AddChild(Ui.Panel(box, "Inset"));
    }

    private static Control? Coalition(Session s, CountryPolitics pol, Main main)
    {
        var t = s.T;
        if (pol.Phase != Phase.Governing || pol.Loyalty is not { Count: > 0 }) return null;
        var list = Ui.VBox(14);
        foreach (var (party, loyalty) in pol.Loyalty)
        {
            bool ult = pol.Ultimatums?.Any(u => u.PartyId == party) == true;
            var color = ult ? AppTheme.Bad : loyalty < 60 ? AppTheme.Warn : AppTheme.Good;
            var box = Ui.VBox(6);
            var head = Ui.HBox(10);
            var name = Ui.PartyName(s, party, null, Tx.Heading);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(name);
            head.AddChild(Ui.Pill(t.T(ult ? "ui.loyalty.ultimatum" : loyalty < 60 ? "ui.loyalty.uneasy" : "ui.loyalty.stable"), color));
            box.AddChild(head);
            var meter = Ui.HBox(10);
            meter.AddChild(new Meter(loyalty, 0, 100, color, 120, false, 12) { SizeFlagsHorizontal = SizeFlags.ExpandFill, TrackColor = AppTheme.TrackLight });
            meter.AddChild(Ui.L(Ui.Fix(t.T("ui.coalition.loyalty", Fmt.Number(loyalty, "0"))), Tx.Small));
            box.AddChild(meter);
            if (pol.Ultimatums?.FirstOrDefault(u => u.PartyId == party) is { } u)
            {
                var dm = u.DemandId == null ? null : s.Texts.DemandOf(party, u.DemandId);
                var w = Ui.HBox(8);
                w.AddChild(new IconView(IconKind.Alert, 16, AppTheme.BadText));
                w.AddChild(Ui.L(Ui.Fix(t.T("ui.coalition.ultimatum", u.TurnsLeft, dm == null ? "" : s.Texts.Demand(dm))), Tx.BodyStrong, AppTheme.BadText, true));
                box.AddChild(w);
            }
            var mine = pol.Commitments?.Where(c => c.PartyId == party).ToList() ?? new();
            if (mine.Count == 0) box.AddChild(Ui.L(t.T("ui.coalition.no_commitments"), Tx.Small, AppTheme.Ink3, true));
            foreach (var c in mine)
            {
                var dm = s.Texts.DemandOf(party, c.DemandId);
                string status = c.Progress >= 0.999 ? "ui.commitment.done" : c.Progress + 0.05 >= c.Expected ? "ui.commitment.on_track" : "ui.commitment.late";
                var sc = status == "ui.commitment.late" ? AppTheme.Bad : status == "ui.commitment.done" ? AppTheme.Good : AppTheme.Chrome;
                var line = Ui.HBox(8);
                line.AddChild(Ui.Pill(t.T(status), sc));
                line.AddChild(Ui.L(Ui.Fix($"{(dm == null ? c.DemandId : s.Texts.Demand(dm))}  ({Fmt.Number(c.Progress * 100, "0")} % / {Fmt.Number(c.Expected * 100, "0")} %)"), Tx.Small, AppTheme.Ink2, true));
                box.AddChild(line);
            }
            list.AddChild(box);
        }
        return Card(t.T("ui.heading.coalition"), list);
    }
}
