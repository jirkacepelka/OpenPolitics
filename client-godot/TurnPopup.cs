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
        Size = new Vector2I(680, 580);
        Exclusive = true; Transient = true; Borderless = true; Transparent = true; TransparentBg = true;
        Theme = AppTheme.Instance;
        CloseRequested += Close;
        var root = Ui.VBox(14);
        root.AddChild(Ui.DialogHeader(IconKind.News, s.T.T("ui.popup.heading"), s.DateText(), Close, s.T.T("ui.client.close")));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var inner = Ui.Margin(_body, 0); inner.AddThemeConstantOverride("margin_right", 12);
        inner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(inner);
        root.AddChild(scroll);
        var done = Ui.Primary(s.T.T("ui.popup.continue"), Close);
        done.CustomMinimumSize = new Vector2(200, 44);
        done.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        root.AddChild(done);
        Ui.FillWindow(this, root, 22);
        Fill();
    }

    /// <summary>True when there is anything to tell or decide.</summary>
    public static bool HasContent(Session s) => s.Digest.Count > 0 || Decisions(s).Count > 0;

    /// <summary>True when the player must decide something (an offer to answer, a government to form, an ultimatum).</summary>
    public static bool HasDecisions(Session s) => Decisions(s).Count > 0;

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
        if (_s.Digest.Count > 0)
        {
            _body.AddChild(Ui.Caption(t.T("ui.popup.news")));
            var list = Ui.VBox(10);
            bool first = true;
            foreach (var line in _s.Digest)
            {
                if (!first) list.AddChild(Ui.Rule());
                first = false;
                var row = Ui.HBox(12);
                var icon = new IconView(IconKind.News, 16, AppTheme.Accent) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
                row.AddChild(icon);
                row.AddChild(Ui.Text(line, AppTheme.Text, 15, true));
                list.AddChild(row);
            }
            _body.AddChild(Ui.Card(list, AppTheme.Card, true, 16));
        }
        var pol = _s.Politics;
        if (pol == null) return;
        var decisions = Decisions(_s);
        if (decisions.Count > 0) { _body.AddChild(Ui.Gap(4)); _body.AddChild(Ui.Caption(t.T("ui.popup.decisions"))); }
        if (pol.Formation is { Offer: { } offer })
        {
            var box = Ui.VBox(8);
            box.AddChild(Ui.Text(t.T("ui.event.offer_made", t.Name(offer.Formateur)), Colors.White, 16, true, AppTheme.Semi));
            box.AddChild(Ui.Text(string.Join(", ", new[] { offer.Formateur }.Concat(offer.Partners).Select(t.Name)), AppTheme.Text2, 14, true));
            foreach (var a in offer.Agreements)
                if (_s.Texts.DemandOf(a.PartyId, a.DemandId) is { } d) box.AddChild(Ui.Text($"{t.Name(a.PartyId)}: {_s.Texts.Demand(d)}", Ui.Muted, 14, true));
            var buttons = Ui.HBox();
            buttons.AddChild(Ui.Primary(t.T("ui.client.accept"), () => { _s.Do(new AcceptOffer(), t.T("ui.formation.joined")); Close(); }));
            buttons.AddChild(Ui.Btn(t.T("ui.client.reject"), () => { _s.Do(new RejectOffer(), t.T("ui.formation.rejected")); Close(); }));
            box.AddChild(buttons);
            _body.AddChild(Ui.Callout(box, AppTheme.Accent));
        }
        else if (pol.Formation is { PlayerIsFormateur: true })
        {
            var box = Ui.VBox(8);
            box.AddChild(Ui.Text(t.T("ui.popup.you_form", pol.Formation.TurnsLeft), Colors.White, 16, true, AppTheme.Semi));
            var open = Ui.Primary(t.T("ui.popup.open_politics"), () => { Close(); _openPolitics?.Invoke(); });
            open.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            box.AddChild(open);
            _body.AddChild(Ui.Callout(box, AppTheme.Accent));
        }
        if (pol.Ultimatums is { Count: > 0 } ultimatums)
            foreach (var u in ultimatums)
            {
                string text = u.DemandId != null && _s.Texts.DemandOf(u.PartyId, u.DemandId) is { } d
                    ? t.T("ui.event.ultimatum_demand", t.Name(u.PartyId), u.TurnsLeft, _s.Texts.Demand(d))
                    : t.T("ui.event.ultimatum", t.Name(u.PartyId), u.TurnsLeft);
                var row = Ui.HBox(10);
                row.AddChild(new IconView(IconKind.Alert, 18, Ui.Warn) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
                row.AddChild(Ui.Text(text, AppTheme.Text, 15, true));
                _body.AddChild(Ui.Callout(row, Ui.Warn));
            }
    }

    private void Close()
    {
        _s.Digest.Clear();
        QueueFree();
    }
}
