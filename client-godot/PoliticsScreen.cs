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

        _body.AddChild(Ui.Heading(t.T("ui.heading.politics")));
        _body.AddChild(Ui.Text(t.T("ui.line.next_election") + ": " +
            (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.politics.election_in", pol.TurnsToElection))));
        Polls(s, pol);
        _body.AddChild(Ui.Rule());
        Government(s, pol);
        Formation(s, pol);
        Coalition(s, pol);
    }

    private void Polls(Session s, CountryPolitics pol)
    {
        var t = s.T;
        _body.AddChild(Ui.Text(t.T("ui.politics.polls"), Ui.Accent, 17));
        double max = Math.Max(0.35, pol.Support.Values.Max());
        foreach (var (id, share) in pol.Support.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var row = Ui.HBox(10);
            bool gov = pol.GovernmentParties.Contains(id);
            row.AddChild(Ui.Text(gov ? "★" : " ", Ui.Warn));
            var name = Ui.Text(t.Name(id) + (id == pol.PlayerParty ? "  (" + t.T("ui.politics.you") + ")" : ""), id == pol.PlayerParty ? Ui.Accent : null);
            name.CustomMinimumSize = new Vector2(260, 0);
            row.AddChild(name);
            row.AddChild(Ui.Bar(share, 0, max, gov ? Ui.Warn : Ui.Accent, 260));
            var pct = Ui.Text(Fmt.Number(share * 100) + " %");
            pct.CustomMinimumSize = new Vector2(70, 0);
            row.AddChild(pct);
            row.AddChild(Ui.Text(t.T("ui.politics.seats_of", pol.Seats.GetValueOrDefault(id), pol.Seats.Values.Sum()), Ui.Muted));
            _body.AddChild(row);
        }
    }

    private void Government(Session s, CountryPolitics pol)
    {
        var t = s.T;
        int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p));
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        bool majority = held > total * rules.MajorityFraction;
        _body.AddChild(Ui.Text($"{t.T("ui.politics.government")}: {t.T("ui.politics.seats_of", held, total)} ({t.T(majority ? "ui.politics.majority" : "ui.politics.no_majority")})",
            majority ? Ui.Good : Ui.Bad, 17));
        if (pol.Leader != null) _body.AddChild(Ui.Text($"{t.T("ui.politics.leader")}: {t.Name(pol.Leader)}"));
        _body.AddChild(Ui.Text(string.Join(", ", pol.GovernmentParties.Select(t.Name)), Ui.Muted));
    }

    // ---- formation ------------------------------------------------------------

    private void Formation(Session s, CountryPolitics pol)
    {
        var t = s.T;
        if (pol.Phase == Phase.Caretaker)
        {
            _body.AddChild(Ui.Rule());
            _body.AddChild(Ui.Text(t.T("ui.formation.caretaker"), Ui.Warn, 17, true));
            return;
        }
        if (pol.Phase != Phase.Formation || pol.Formation == null) return;
        var f = pol.Formation;
        _body.AddChild(Ui.Rule());
        _body.AddChild(Ui.Heading(t.T("ui.formation.heading")));
        if (f.Offer != null) { Offer(s, f.Offer); return; }
        if (!f.PlayerIsFormateur)
        {
            _body.AddChild(Ui.Text(t.T("ui.formation.ai_forms", t.Name(f.Formateur), f.Attempt), null, 0, true));
            return;
        }
        _body.AddChild(Ui.Text(t.T("ui.formation.you_form", f.TurnsLeft), Ui.Warn, 17, true));
        var auto = Ui.Btn(t.T("ui.client.auto_proposal"), () =>
        {
            if (s.Game.SuggestCoalition() is { } sug) s.Do(new ProposeCoalition(sug.Partners, sug.Agreements), t.T("ui.formation.proposed"));
        }, t.T("ui.client.auto_hint"));
        _body.AddChild(auto);
        Draft(s, pol);
    }

    private void Offer(Session s, CoalitionProposal offer)
    {
        var t = s.T;
        _body.AddChild(Ui.Text(t.T("ui.formation.offer", t.Name(offer.Formateur), string.Join(", ", offer.Partners.Select(t.Name))), Ui.Warn, 17, true));
        foreach (var a in offer.Agreements)
            if (s.Texts.DemandOf(a.PartyId, a.DemandId) is { } d) _body.AddChild(Ui.Text($"    {t.Name(a.PartyId)}: {s.Texts.Demand(d)}"));
        var row = Ui.HBox();
        row.AddChild(Ui.Btn(t.T("ui.client.accept"), () => s.Do(new AcceptOffer(), t.T("ui.formation.joined"))));
        row.AddChild(Ui.Btn(t.T("ui.client.reject"), () => s.Do(new RejectOffer(), t.T("ui.formation.rejected"))));
        _body.AddChild(row);
        _body.AddChild(Ui.Text(t.T("ui.formation.offer_hint"), Ui.Muted, 0, true));
    }

    private void Draft(Session s, CountryPolitics pol)
    {
        var t = s.T;
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        int total = pol.Seats.Values.Sum();
        int held = pol.Seats.GetValueOrDefault(pol.PlayerParty) + _invited.Sum(p => pol.Seats.GetValueOrDefault(p));
        _body.AddChild(Ui.Text(t.T("ui.formation.majority_needed", (int)Math.Floor(rules.MajorityFraction * total), held, rules.ProgramSlots),
            held > total * rules.MajorityFraction ? Ui.Good : Ui.Muted, 0, true));
        foreach (var party in s.Packs.Parties.Where(p => p.Id != pol.PlayerParty && pol.Seats.GetValueOrDefault(p.Id) > 0).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var invite = new CheckBox
            {
                Text = $"{t.Name(party.Id)}  —  {t.T("ui.formation.party_line", pol.Seats[party.Id], Fmt.Number(CoalitionRules.RequiredWeight(party, pol.Party?.Relations.GetValueOrDefault(party.Id, 0.3) ?? 0.3)))}",
                ButtonPressed = _invited.Contains(party.Id),
            };
            invite.Toggled += on =>
            {
                if (on) _invited.Add(party.Id); else { _invited.Remove(party.Id); _agreed.RemoveWhere(a => a.Party == party.Id); }
                Rebuild();
            };
            _body.AddChild(invite);
            if (!_invited.Contains(party.Id)) continue;
            foreach (var d in party.Demands)
            {
                var box = new CheckBox
                {
                    Text = $"{s.Texts.Demand(d)}   ({t.T("ui.formation.weight", Fmt.Number(d.Weight))})",
                    ButtonPressed = _agreed.Contains((party.Id, d.Id)),
                };
                var margin = Ui.Margin(box, 0);
                margin.AddThemeConstantOverride("margin_left", 36);
                box.Toggled += on => { if (on) _agreed.Add((party.Id, d.Id)); else _agreed.Remove((party.Id, d.Id)); Rebuild(); };
                _body.AddChild(margin);
            }
        }
        _body.AddChild(Ui.Btn(t.T("ui.client.submit"), () =>
            s.Do(new ProposeCoalition(_invited.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                _agreed.OrderBy(a => a.Party, StringComparer.Ordinal).ThenBy(a => a.Demand, StringComparer.Ordinal)
                    .Select(a => new AgreedDemand(a.Party, a.Demand)).ToList()), t.T("ui.formation.proposed"))));
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
        _body.AddChild(Ui.Rule());
        _body.AddChild(Ui.Heading(t.T("ui.heading.coalition")));
        foreach (var (party, loyalty) in pol.Loyalty)
        {
            bool ult = pol.Ultimatums?.Any(u => u.PartyId == party) == true;
            var row = Ui.HBox(10);
            var name = Ui.Text(t.Name(party));
            name.CustomMinimumSize = new Vector2(220, 0);
            row.AddChild(name);
            row.AddChild(Ui.Bar(loyalty, 0, 100, ult ? Ui.Bad : loyalty < 60 ? Ui.Warn : Ui.Good, 200));
            row.AddChild(Ui.Text(t.T("ui.coalition.loyalty", Fmt.Number(loyalty, "0")) + "  " +
                t.T(ult ? "ui.loyalty.ultimatum" : loyalty < 60 ? "ui.loyalty.uneasy" : "ui.loyalty.stable")));
            _body.AddChild(row);
            if (pol.Ultimatums?.FirstOrDefault(u => u.PartyId == party) is { } u)
            {
                var d = u.DemandId == null ? null : s.Texts.DemandOf(party, u.DemandId);
                _body.AddChild(Ui.Text("      " + t.T("ui.coalition.ultimatum", u.TurnsLeft, d == null ? "" : s.Texts.Demand(d)), Ui.Bad));
            }
            var mine = pol.Commitments?.Where(c => c.PartyId == party).ToList() ?? new();
            if (mine.Count == 0) _body.AddChild(Ui.Text("      " + t.T("ui.coalition.no_commitments"), Ui.Muted));
            foreach (var c in mine)
            {
                var d = s.Texts.DemandOf(party, c.DemandId);
                string status = c.Progress >= 0.999 ? "ui.commitment.done" : c.Progress + 0.05 >= c.Expected ? "ui.commitment.on_track" : "ui.commitment.late";
                var color = status == "ui.commitment.late" ? Ui.Bad : status == "ui.commitment.done" ? Ui.Good : Ui.Muted;
                _body.AddChild(Ui.Text($"      [{t.T(status)}] {(d == null ? c.DemandId : s.Texts.Demand(d))}  ({Fmt.Number(c.Progress * 100, "0")} % / {Fmt.Number(c.Expected * 100, "0")} %)", color));
            }
        }
        if (pol.PlayerRole == Role.Leader) EarlyElectionButton(s);
    }

    private void EarlyElectionButton(Session s) =>
        _body.AddChild(Ui.Btn(s.T.T("ui.client.early_election"), () => s.Do(new CallEarlyElection(), s.T.T("ui.formation.early_called"))));
}
