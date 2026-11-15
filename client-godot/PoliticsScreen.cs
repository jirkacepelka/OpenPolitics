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

    private readonly HashSet<string> _invited = new(StringComparer.Ordinal);
    private readonly HashSet<(string Party, string Demand)> _agreed = new();
    private Session? _s; private Dialog? _d; private Main? _main;

    private void Rebuild() { if (_s != null && _d != null && _main != null && GodotObject.IsInstanceValid(_d)) { Ui.Clear(_d.Body); Build(_s, _d, _main); } }

    private static Control Card(string title, Control body)
    {
        var box = Ui.VBox(10);
        box.AddChild(Ui.L(title, Tx.Overline, AppTheme.Chrome));
        box.AddChild(body);
        return Ui.Panel(box, "Card");
    }

    public override void Build(Session s, Dialog d, Main main)
    {
        _s = s; _d = d; _main = main;
        var t = s.T;
        var pol = s.Politics;
        if (pol == null) { d.Body.AddChild(Ui.L(t.T("ui.err.no_politics"), Tx.Body)); return; }
        if (pol.Formation is not { PlayerIsFormateur: true }) { _invited.Clear(); _agreed.Clear(); }
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p));
        bool majority = held > total * rules.MajorityFraction;

        Formation(s, pol, d.Body, main);

        var grid = new FlowGrid { MinColumn = 330, MaxColumns = 3, Gap = 12 };
        // Parliament
        var parl = Ui.VBox(8);
        var hemi = new Hemicycle { CustomMinimumSize = new Vector2(260, 160) };
        var order = s.Packs.Parties.Select(p => p.Id).OrderBy(id => s.Game.Compass.TryGetValue(id, out var c) ? c.X : 0).ThenBy(id => id, StringComparer.Ordinal);
        hemi.Set(order.Select(id => (Ui.PartyColor(s, id), pol.GovernmentParties.Contains(id), pol.Seats.GetValueOrDefault(id), t.Name(id))), (float)rules.MajorityFraction,
            $"/ {total} · " + t.T(majority ? "ui.politics.majority" : "ui.politics.no_majority"));
        parl.AddChild(hemi);
        var status = Ui.HBox(8);
        status.AddChild(Ui.Pill(t.T(majority ? "ui.politics.majority" : "ui.politics.no_majority"), majority ? AppTheme.Good : AppTheme.Bad));
        status.AddChild(Ui.Pill(t.T("ui.line.next_election") + ": " + (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.politics.election_in", pol.TurnsToElection)), AppTheme.Chrome));
        parl.AddChild(status);
        if (pol.Leader != null) parl.AddChild(Ui.L($"{t.T("ui.politics.leader")}: {t.Name(pol.Leader)}", Tx.Body, AppTheme.Ink));
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
        if (pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader)
        {
            var b = Ui.Btn(t.T("ui.client.early_election"), () => s.Do(new CallEarlyElection(), t.T("ui.formation.early_called")), "ButtonDanger", IconKind.Ballot);
            b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            d.Body.AddChild(b);
        }
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
        if (f.Offer != null)
        {
            box.AddChild(Ui.L(t.T("ui.formation.offer", t.Name(f.Offer.Formateur), string.Join(", ", f.Offer.Partners.Select(t.Name))), Tx.BodyStrong, AppTheme.Ink, true));
            foreach (var a in f.Offer.Agreements)
                if (s.Texts.DemandOf(a.PartyId, a.DemandId) is { } dm) box.AddChild(Ui.PartyName(s, a.PartyId, $"{t.Name(a.PartyId)}: {Ui.Fix(s.Texts.Demand(dm))}", Tx.Body));
            var row = Ui.HBox(1);
            row.AddChild(Ui.Btn(t.T("ui.client.accept"), () => s.Do(new AcceptOffer(), t.T("ui.formation.joined")), "", IconKind.Check));
            row.AddChild(Ui.Btn(t.T("ui.client.reject"), () => s.Do(new RejectOffer(), t.T("ui.formation.rejected")), "ButtonSecondary", IconKind.Close));
            box.AddChild(row);
            box.AddChild(Ui.L(t.T("ui.formation.offer_hint"), Tx.Small, AppTheme.Ink3, true));
        }
        else if (!f.PlayerIsFormateur) box.AddChild(Ui.L(t.T("ui.formation.ai_forms", t.Name(f.Formateur), f.Attempt), Tx.Body, AppTheme.Ink, true));
        else
        {
            var head = Ui.HBox(12);
            head.AddChild(Ui.L(t.T("ui.formation.you_form", f.TurnsLeft), Tx.BodyStrong, AppTheme.WarnText, true));
            head.AddChild(Ui.Btn(t.T("ui.client.auto_proposal"), () =>
            {
                if (s.Game.SuggestCoalition() is { } sug) s.Do(new ProposeCoalition(sug.Partners, sug.Agreements), t.T("ui.formation.proposed"));
            }, "", IconKind.Check, t.T("ui.client.auto_hint")));
            box.AddChild(head);
            Draft(s, pol, box);
        }
        body.AddChild(Ui.Panel(box, "Inset"));
    }

    private void Draft(Session s, CountryPolitics pol, VBoxContainer card)
    {
        var t = s.T;
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        int total = pol.Seats.Values.Sum();
        int held = pol.Seats.GetValueOrDefault(pol.PlayerParty) + _invited.Sum(p => pol.Seats.GetValueOrDefault(p));
        card.AddChild(Ui.L(t.T("ui.formation.majority_needed", (int)Math.Floor(rules.MajorityFraction * total), held, rules.ProgramSlots),
            Tx.BodyStrong, held > total * rules.MajorityFraction ? AppTheme.GoodText : AppTheme.Ink2, true));
        foreach (var party in s.Packs.Parties.Where(p => p.Id != pol.PlayerParty && pol.Seats.GetValueOrDefault(p.Id) > 0).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var line = Ui.HBox(8);
            line.AddChild(new ColorDot(Ui.PartyColor(s, party.Id)));
            var invite = new CheckBox
            {
                Text = $"{t.Name(party.Id)}  ·  {Ui.Fix(t.T("ui.formation.party_line", pol.Seats[party.Id], Fmt.Number(CoalitionRules.RequiredWeight(party, pol.Party?.Relations.GetValueOrDefault(party.Id, 0.3) ?? 0.3))))}",
                ButtonPressed = _invited.Contains(party.Id), FocusMode = Control.FocusModeEnum.None,
            };
            invite.Toggled += on =>
            {
                if (on) _invited.Add(party.Id); else { _invited.Remove(party.Id); _agreed.RemoveWhere(a => a.Party == party.Id); }
                Rebuild();
            };
            line.AddChild(invite);
            card.AddChild(line);
            if (!_invited.Contains(party.Id)) continue;
            foreach (var dm in party.Demands)
            {
                var box = new CheckBox
                {
                    Text = $"{Ui.Fix(s.Texts.Demand(dm))}   ({Ui.Fix(t.T("ui.formation.weight", Fmt.Number(dm.Weight)))})",
                    ButtonPressed = _agreed.Contains((party.Id, dm.Id)), FocusMode = Control.FocusModeEnum.None,
                };
                box.AddThemeColorOverride("font_color", AppTheme.Ink2);
                box.Toggled += on => { if (on) _agreed.Add((party.Id, dm.Id)); else _agreed.Remove((party.Id, dm.Id)); Rebuild(); };
                var indent = Ui.Margin(box, 0, 0);
                indent.AddThemeConstantOverride("margin_left", 36);
                card.AddChild(indent);
            }
        }
        var submit = Ui.Btn(t.T("ui.client.submit"), () =>
            s.Do(new ProposeCoalition(_invited.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                _agreed.OrderBy(a => a.Party, StringComparer.Ordinal).ThenBy(a => a.Demand, StringComparer.Ordinal)
                    .Select(a => new AgreedDemand(a.Party, a.Demand)).ToList()), t.T("ui.formation.proposed")), "", IconKind.Check);
        submit.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        card.AddChild(submit);
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
