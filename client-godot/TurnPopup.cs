using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Client;

/// <summary>The start-of-quarter briefing: what happened, and what needs a decision now.</summary>
public partial class TurnPopup : Window
{
    private readonly Session _s;
    private readonly Action? _openPolitics;
    private readonly VBoxContainer _body = Ui.VBox(8);

    public TurnPopup(Session s, Action? openPolitics)
    {
        _s = s; _openPolitics = openPolitics;
        Title = s.T.T("ui.popup.title", s.DateText());
        Size = new Vector2I(680, 560);
        Exclusive = true; Transient = true;
        CloseRequested += Close;
        var root = Ui.VBox(10);
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 360);
        scroll.AddChild(_body);
        root.AddChild(scroll);
        var done = Ui.Btn(s.T.T("ui.popup.continue"), Close);
        done.CustomMinimumSize = new Vector2(0, 42);
        root.AddChild(done);
        Ui.FillWindow(this, root);
        Fill();
    }

    /// <summary>True when there is anything to tell or decide.</summary>
    public static bool HasContent(Session s) => s.Digest.Count > 0 || Decisions(s).Count > 0;

    private static List<string> Decisions(Session s)
    {
        var list = new List<string>();
        if (s.Politics is not { } pol) return list;
        if (pol.Formation is { } f && (f.Offer != null || f.PlayerIsFormateur)) list.Add("formation");
        if (pol.Ultimatums is { Count: > 0 }) list.Add("ultimatums");
        return list;
    }

    private void Fill()
    {
        var t = _s.T;
        Ui.Clear(_body);
        _body.AddChild(Ui.Text(_s.DateText(), Colors.White, 22));
        if (_s.Digest.Count > 0)
        {
            _body.AddChild(Ui.Section(t.T("ui.popup.news")));
            foreach (var line in _s.Digest) _body.AddChild(Ui.Card(Ui.Text("• " + line, null, 15, true)));
        }
        var pol = _s.Politics;
        if (pol == null) return;
        var decisions = Decisions(_s);
        if (decisions.Count > 0) _body.AddChild(Ui.Section(t.T("ui.popup.decisions")));
        if (pol.Formation is { Offer: { } offer })
        {
            var box = Ui.VBox(8);
            box.AddChild(Ui.Text(t.T("ui.event.offer_made", t.Name(offer.Formateur)), null, 16, true));
            box.AddChild(Ui.Text(string.Join(", ", new[] { offer.Formateur }.Concat(offer.Partners).Select(t.Name)), Ui.Muted, 14, true));
            foreach (var a in offer.Agreements)
                if (_s.Texts.DemandOf(a.PartyId, a.DemandId) is { } d) box.AddChild(Ui.Text($"{t.Name(a.PartyId)}: {_s.Texts.Demand(d)}", Ui.Muted, 14, true));
            var buttons = Ui.HBox();
            buttons.AddChild(Ui.Btn(t.T("ui.client.accept"), () => { _s.Do(new AcceptOffer(), t.T("ui.formation.joined")); Close(); }));
            buttons.AddChild(Ui.Btn(t.T("ui.client.reject"), () => { _s.Do(new RejectOffer(), t.T("ui.formation.rejected")); Close(); }));
            box.AddChild(buttons);
            _body.AddChild(Ui.Card(box, AppTheme.CardHover));
        }
        else if (pol.Formation is { PlayerIsFormateur: true })
        {
            var box = Ui.VBox(8);
            box.AddChild(Ui.Text(t.T("ui.popup.you_form", pol.Formation.TurnsLeft), null, 16, true));
            box.AddChild(Ui.Btn(t.T("ui.popup.open_politics"), () => { Close(); _openPolitics?.Invoke(); }));
            _body.AddChild(Ui.Card(box, AppTheme.CardHover));
        }
        if (pol.Ultimatums is { Count: > 0 } ultimatums)
            foreach (var u in ultimatums)
            {
                string text = u.DemandId != null && _s.Texts.DemandOf(u.PartyId, u.DemandId) is { } d
                    ? t.T("ui.event.ultimatum_demand", t.Name(u.PartyId), u.TurnsLeft, _s.Texts.Demand(d))
                    : t.T("ui.event.ultimatum", t.Name(u.PartyId), u.TurnsLeft);
                _body.AddChild(Ui.Card(Ui.Text("⚠ " + text, Ui.Warn, 15, true)));
            }
    }

    private void Close()
    {
        _s.Digest.Clear();
        QueueFree();
    }
}
