using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>Polls, the government, government formation (as formateur or when offered a place) and the coalition panel.</summary>
public partial class PoliticsScreen : Screen
{
    public override string TitleKey => "ui.client.tab.politics";
    public override float PreferredWidth => 0.86f;
    private readonly VBoxContainer _body;
    private Session? _s;
    private readonly HashSet<string> _invited = new(StringComparer.Ordinal);
    private readonly HashSet<(string Party, string Demand)> _agreed = new();

    public PoliticsScreen()
    {
        var (scroll, body) = Ui.Page();
        _body = body;
        AddChild(scroll);
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
    }

    private void Rebuild() { if (_s != null) Refresh(_s); }

    public override void Refresh(Session s)
    {
        _s = s;
        Ui.Clear(_body);
        var t = s.T;
        var pol = s.Politics;
        if (pol == null) { _body.AddChild(Ui.Text(t.T("ui.err.no_politics"), Ui.Muted)); return; }
        if (pol.Formation is not { PlayerIsFormateur: true }) { _invited.Clear(); _agreed.Clear(); }

        int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p));
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        bool majority = held > total * rules.MajorityFraction;

        // Parliament and government, next to the compass.
        var top = Ui.HBox(12);
        var gov = Ui.VBox(10);
        var head = Ui.HBox(12);
        var titles = Ui.VBox(2);
        titles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titles.AddChild(Ui.Caption(t.T("ui.politics.government")));
        titles.AddChild(Ui.Big($"{held} / {total}", majority ? Ui.Good : Ui.Bad, AppTheme.Title));
        head.AddChild(titles);
        var side = Ui.VBox(6);
        side.AddChild(Ui.Pill(t.T(majority ? "ui.politics.majority" : "ui.politics.no_majority"), majority ? Ui.Good : Ui.Bad));
        side.AddChild(Ui.Pill(t.T("ui.line.next_election") + ": " + (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.politics.election_in", pol.TurnsToElection)), AppTheme.Accent));
        head.AddChild(side);
        gov.AddChild(head);
        var hemi = new Hemicycle { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 170) };
        hemi.Set(s.Packs.Parties.Select(p => p.Id).OrderBy(id => s.Game.Compass.TryGetValue(id, out var c) ? c.X : 0).ThenBy(id => id, StringComparer.Ordinal)
            .Select(id => (Ui.PartyColor(s, id), pol.GovernmentParties.Contains(id), pol.Seats.GetValueOrDefault(id))), (float)rules.MajorityFraction);
        gov.AddChild(hemi);
        if (pol.Leader != null) gov.AddChild(Ui.Text($"{t.T("ui.politics.leader")}: {t.Name(pol.Leader)}", AppTheme.Text, 14));
        var members = new HFlowContainer();
        members.AddThemeConstantOverride("h_separation", 14);
        foreach (var id in pol.GovernmentParties) members.AddChild(Ui.PartyName(s, id, null, null, 14));
        gov.AddChild(members);
        top.AddChild(Ui.Card(gov));
        var compassCard = Ui.VBox(8);
        compassCard.AddChild(Ui.Caption(t.T("ui.client.compass")));
        var compass = new CompassView();
        compass.Set(s, pol.PlayerParty);
        compassCard.AddChild(compass);
        top.AddChild(Ui.Card(compassCard, null, false));
        _body.AddChild(top);

        var pollBox = Ui.VBox(8);
        Polls(s, pol, pollBox);
        _body.AddChild(Ui.Card(pollBox));
        Formation(s, pol);
        Coalition(s, pol);
    }

    private void Polls(Session s, CountryPolitics pol, VBoxContainer parent)
    {
        var t = s.T;
        parent.AddChild(Ui.Text(t.T("ui.politics.polls"), Colors.White, AppTheme.Subhead, false, AppTheme.Semi));
        double max = Math.Max(0.35, pol.Support.Values.Max());
        int total = pol.Seats.Values.Sum();
        foreach (var (id, share) in pol.Support.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var row = Ui.HBox(12);
            bool gov = pol.GovernmentParties.Contains(id), me = id == pol.PlayerParty;
            var name = Ui.PartyName(s, id, t.Name(id) + (me ? "  (" + t.T("ui.politics.you") + ")" : ""), me ? AppTheme.Accent : null);
            name.CustomMinimumSize = new Vector2(250, 0);
            row.AddChild(name);
            var star = Ui.Text(gov ? "★" : "", Ui.Warn, 14);
            star.CustomMinimumSize = new Vector2(16, 0);
            star.TooltipText = gov ? t.T("ui.politics.government") : "";
            row.AddChild(star);
            var bar = new Meter(share, 0, max, Ui.PartyColor(s, id), 160, false, 10) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(bar);
            var pct = Ui.Text(Fmt.Number(share * 100) + " %", Colors.White, 15, false, AppTheme.Semi);
            pct.CustomMinimumSize = new Vector2(64, 0); pct.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(pct);
            var seats = Ui.Text(t.T("ui.politics.seats_of", pol.Seats.GetValueOrDefault(id), total), Ui.Muted, AppTheme.Caption);
            seats.CustomMinimumSize = new Vector2(120, 0); seats.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(seats);
            parent.AddChild(row);
        }
    }

    // ---- formation ------------------------------------------------------------

    private void Formation(Session s, CountryPolitics pol)
    {
        var t = s.T;
        if (pol.Phase == Phase.Caretaker)
        {
            _body.AddChild(Ui.Callout(Ui.Text(t.T("ui.formation.caretaker"), Ui.Warn, 15, true), Ui.Warn));
            return;
        }
        if (pol.Phase != Phase.Formation || pol.Formation == null) return;
        var f = pol.Formation;
        _body.AddChild(Ui.Section(t.T("ui.formation.heading")));
        if (f.Offer != null) { Offer(s, f.Offer); return; }
        if (!f.PlayerIsFormateur)
        {
            _body.AddChild(Ui.Callout(Ui.Text(t.T("ui.formation.ai_forms", t.Name(f.Formateur), f.Attempt), AppTheme.Text, 14, true), AppTheme.Accent));
            return;
        }
        var head = Ui.HBox(12);
        head.AddChild(Ui.Text(t.T("ui.formation.you_form", f.TurnsLeft), Ui.Warn, 15, true));
        var auto = Ui.Btn(t.T("ui.client.auto_proposal"), () =>
        {
            if (s.Game.SuggestCoalition() is { } sug) s.Do(new ProposeCoalition(sug.Partners, sug.Agreements), t.T("ui.formation.proposed"));
        }, t.T("ui.client.auto_hint"));
        head.AddChild(auto);
        _body.AddChild(Ui.Callout(head, Ui.Warn));
        Draft(s, pol);
    }

    private void Offer(Session s, CoalitionProposal offer)
    {
        var t = s.T;
        var box = Ui.VBox(10);
        box.AddChild(Ui.Text(t.T("ui.formation.offer", t.Name(offer.Formateur), string.Join(", ", offer.Partners.Select(t.Name))), Colors.White, 15, true, AppTheme.Semi));
        foreach (var a in offer.Agreements)
            if (s.Texts.DemandOf(a.PartyId, a.DemandId) is { } d) box.AddChild(Ui.PartyName(s, a.PartyId, $"{t.Name(a.PartyId)}: {s.Texts.Demand(d)}", AppTheme.Text2, 14));
        var row = Ui.HBox();
        row.AddChild(Ui.Primary(t.T("ui.client.accept"), () => s.Do(new AcceptOffer(), t.T("ui.formation.joined"))));
        row.AddChild(Ui.Btn(t.T("ui.client.reject"), () => s.Do(new RejectOffer(), t.T("ui.formation.rejected"))));
        box.AddChild(row);
        box.AddChild(Ui.Text(t.T("ui.formation.offer_hint"), Ui.Muted, AppTheme.Caption, true));
        _body.AddChild(Ui.Callout(box, Ui.Warn));
    }

    private void Draft(Session s, CountryPolitics pol)
    {
        var t = s.T;
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        int total = pol.Seats.Values.Sum();
        int held = pol.Seats.GetValueOrDefault(pol.PlayerParty) + _invited.Sum(p => pol.Seats.GetValueOrDefault(p));
        var card = Ui.VBox(6);
        card.AddChild(Ui.Text(t.T("ui.formation.majority_needed", (int)Math.Floor(rules.MajorityFraction * total), held, rules.ProgramSlots),
            held > total * rules.MajorityFraction ? Ui.Good : AppTheme.Text2, 14, true));
        foreach (var party in s.Packs.Parties.Where(p => p.Id != pol.PlayerParty && pol.Seats.GetValueOrDefault(p.Id) > 0).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var line = Ui.HBox(8);
            line.AddChild(new ColorDot(Ui.PartyColor(s, party.Id)));
            var invite = new CheckBox
            {
                Text = $"{t.Name(party.Id)}  ·  {t.T("ui.formation.party_line", pol.Seats[party.Id], Fmt.Number(CoalitionRules.RequiredWeight(party, pol.Party?.Relations.GetValueOrDefault(party.Id, 0.3) ?? 0.3)))}",
                ButtonPressed = _invited.Contains(party.Id), FocusMode = FocusModeEnum.None,
            };
            invite.Toggled += on =>
            {
                if (on) _invited.Add(party.Id); else { _invited.Remove(party.Id); _agreed.RemoveWhere(a => a.Party == party.Id); }
                Rebuild();
            };
            line.AddChild(invite);
            card.AddChild(line);
            if (!_invited.Contains(party.Id)) continue;
            foreach (var d in party.Demands)
            {
                var box = new CheckBox
                {
                    Text = $"{s.Texts.Demand(d)}   ({t.T("ui.formation.weight", Fmt.Number(d.Weight))})",
                    ButtonPressed = _agreed.Contains((party.Id, d.Id)), FocusMode = FocusModeEnum.None,
                };
                box.AddThemeColorOverride("font_color", AppTheme.Text2);
                var margin = Ui.Margin(box, 0);
                margin.AddThemeConstantOverride("margin_left", 40);
                box.Toggled += on => { if (on) _agreed.Add((party.Id, d.Id)); else _agreed.Remove((party.Id, d.Id)); Rebuild(); };
                card.AddChild(margin);
            }
        }
        var submit = Ui.Primary(t.T("ui.client.submit"), () =>
            s.Do(new ProposeCoalition(_invited.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                _agreed.OrderBy(a => a.Party, StringComparer.Ordinal).ThenBy(a => a.Demand, StringComparer.Ordinal)
                    .Select(a => new AgreedDemand(a.Party, a.Demand)).ToList()), t.T("ui.formation.proposed")));
        submit.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        card.AddChild(Ui.Gap(4));
        card.AddChild(submit);
        _body.AddChild(Ui.Card(card));
    }

    // ---- coalition -------------------------------------------------------------

    private void Coalition(Session s, CountryPolitics pol)
    {
        var t = s.T;
        if (pol.Phase != Phase.Governing || pol.Loyalty is not { Count: > 0 })
        {
            if (pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader) EarlyElectionButton(s);
            return;
        }
        _body.AddChild(Ui.Section(t.T("ui.heading.coalition")));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var (party, loyalty) in pol.Loyalty)
        {
            bool ult = pol.Ultimatums?.Any(u => u.PartyId == party) == true;
            var color = ult ? Ui.Bad : loyalty < 60 ? Ui.Warn : Ui.Good;
            var box = Ui.VBox(8);
            var head = Ui.HBox(10);
            var name = Ui.PartyName(s, party, null, Colors.White, 16);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(name);
            head.AddChild(Ui.Pill(t.T(ult ? "ui.loyalty.ultimatum" : loyalty < 60 ? "ui.loyalty.uneasy" : "ui.loyalty.stable"), color));
            box.AddChild(head);
            var meter = Ui.HBox(10);
            var m = new Meter(loyalty, 0, 100, color, 160) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            meter.AddChild(m);
            meter.AddChild(Ui.Text(t.T("ui.coalition.loyalty", Fmt.Number(loyalty, "0")), AppTheme.Text2, AppTheme.Caption));
            box.AddChild(meter);
            if (pol.Ultimatums?.FirstOrDefault(u => u.PartyId == party) is { } u)
            {
                var d = u.DemandId == null ? null : s.Texts.DemandOf(party, u.DemandId);
                box.AddChild(Ui.Text("⚠ " + t.T("ui.coalition.ultimatum", u.TurnsLeft, d == null ? "" : s.Texts.Demand(d)), Ui.Bad, 14, true));
            }
            var mine = pol.Commitments?.Where(c => c.PartyId == party).ToList() ?? new();
            if (mine.Count == 0) box.AddChild(Ui.Text(t.T("ui.coalition.no_commitments"), Ui.Muted, AppTheme.Caption, true));
            foreach (var c in mine)
            {
                var d = s.Texts.DemandOf(party, c.DemandId);
                string status = c.Progress >= 0.999 ? "ui.commitment.done" : c.Progress + 0.05 >= c.Expected ? "ui.commitment.on_track" : "ui.commitment.late";
                var sc = status == "ui.commitment.late" ? Ui.Bad : status == "ui.commitment.done" ? Ui.Good : AppTheme.Accent;
                var line = Ui.HBox(8);
                line.AddChild(Ui.Pill(t.T(status), sc));
                line.AddChild(Ui.Text($"{(d == null ? c.DemandId : s.Texts.Demand(d))}  ({Fmt.Number(c.Progress * 100 + 0.0, "0")} % / {Fmt.Number(c.Expected * 100, "0")} %)", AppTheme.Text2, AppTheme.Caption, true));
                box.AddChild(line);
            }
            grid.AddChild(Ui.Card(box, null, true, 14));
        }
        _body.AddChild(grid);
        if (pol.PlayerRole == Role.Leader) EarlyElectionButton(s);
    }

    private void EarlyElectionButton(Session s)
    {
        var b = Ui.Btn(s.T.T("ui.client.early_election"), () => s.Do(new CallEarlyElection(), s.T.T("ui.formation.early_called")));
        b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _body.AddChild(b);
    }
}
