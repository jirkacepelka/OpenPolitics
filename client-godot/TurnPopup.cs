using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>A key indicator as a rounded grey pill: chrome disc with a white pictogram, uppercase label, traffic bar and value.</summary>
public partial class IndicatorPill : Control
{
    private readonly IconKind _icon; private readonly string _label, _value; private readonly double _good;
    public IndicatorPill(IconKind icon, string label, string value, double goodness, string tooltip)
    {
        _icon = icon; _label = label.ToUpperInvariant(); _value = value; _good = goodness; TooltipText = tooltip;
        CustomMinimumSize = new Vector2(220, 70); SizeFlagsHorizontal = SizeFlags.ExpandFill; MouseFilter = MouseFilterEnum.Pass;
    }
    public override void _Draw()
    {
        var tile = new Rect2(0, 4, Size.X, Size.Y - 8);
        DrawStyleBox(AppTheme.Box(AppTheme.Panel, 14), tile);
        float d = Math.Min(40, tile.Size.Y - 20);
        var c = new Vector2(tile.Position.X + 14 + d / 2, Size.Y / 2);
        DrawCircle(c, d / 2, AppTheme.ChromeTint);
        Icons.Draw(this, _icon, c, d * 0.55f, AppTheme.Chrome);
        float x = c.X + d / 2 + 14, w = tile.End.X - x - 16;
        DrawString(AppTheme.Face(600, false, 1), new Vector2(x, Size.Y / 2 - 6), _label, HorizontalAlignment.Left, w - 70, 11, AppTheme.Ink3);
        DrawString(AppTheme.FigSemi, new Vector2(x, Size.Y / 2 - 5), _value, HorizontalAlignment.Right, w, 14, AppTheme.Ink);
        var bar = new Rect2(x, Size.Y / 2 + 7, w, 5);
        DrawStyleBox(AppTheme.Box(AppTheme.TrackLight, 3), bar);
        DrawStyleBox(AppTheme.Box(AppTheme.Traffic(_good), 3), new Rect2(bar.Position, new Vector2(Math.Max(5, w * (float)Math.Clamp(_good, 0, 1)), 5)));
    }

    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
}

/// <summary>
/// The start-of-quarter briefing in the style of Democracy 4's intro modal: big centred title, a paragraph, the decisions, a grid of
/// key indicator pills, the news, and two full-width buttons.
/// </summary>
public static class TurnPopup
{
    /// <summary>True when there is anything to tell or decide.</summary>
    public static bool HasContent(Session s) => s.Digest.Count > 0 || DecisionCount(s) > 0;

    /// <summary>Decisions that wait: an offer to answer or a government to form, plus running ultimatums.</summary>
    public static int DecisionCount(Session s)
    {
        if (s.Politics is not { } pol) return 0;
        int n = 0;
        if (pol.Formation is { } f && (f.Offer != null || f.PlayerIsFormateur)) n++;
        n += pol.Ultimatums?.Count ?? 0;
        return n;
    }

    public static bool HasDecisions(Session s) => DecisionCount(s) > 0;

    private static Label Centred(Label l) { l.HorizontalAlignment = HorizontalAlignment.Center; return l; }

    public static Dialog Open(Main main, Session s, bool welcome = false)
    {
        var t = s.T;
        var pol = s.Politics;
        string title = welcome ? t.T("ui.brief.welcome_title") : pol?.Formation?.Offer is { } o ? t.T("ui.event.offer_made", t.Name(o.Formateur))
            : s.Digest.Count > 0 ? Shorten(s.Digest[0]) : t.T("ui.brief.quiet_title");
        var d = main.NewDialog("", null, 900);
        d.Key = "briefing";
        d.Closed += () => s.Digest.Clear();
        d.Blocking = Main.Blocking(s);
        d.CloseEnabled = !d.Blocking;
        d.Body.AddChild(Centred(Ui.L(t.T("ui.brief.overline", s.DateText()), Tx.Overline, AppTheme.Ink3)));
        var head = Centred(Ui.L(Ui.Fix(title), Tx.Title, AppTheme.Ink, true));
        head.AddThemeFontSizeOverride("font_size", 26);
        d.Body.AddChild(head);
        if (welcome) d.Body.AddChild(Ui.L(t.T(s.HasPolitics ? "ui.client.welcome_politics" : "ui.client.welcome"), Tx.Body, AppTheme.Ink, true));

        // Decisions first.
        if (pol != null)
        {
            if (pol.Formation is { Offer: { } offer })
            {
                var box = Ui.VBox(8);
                box.AddChild(Ui.L(t.T("ui.formation.offer", t.Name(offer.Formateur), string.Join(", ", offer.Partners.Select(t.Name))), Tx.BodyStrong, AppTheme.Ink, true));
                foreach (var a in offer.Agreements)
                    if (s.Texts.DemandOf(a.PartyId, a.DemandId) is { } dm) box.AddChild(Ui.PartyName(s, a.PartyId, $"{t.Name(a.PartyId)}: {Ui.Fix(s.Texts.Demand(dm))}", Tx.Body));
                var row = Ui.HBox(1);
                var acc = Ui.Btn(t.T("ui.client.accept"), () => { s.Do(new AcceptOffer(), t.T("ui.formation.joined")); Done(); }, "", IconKind.Check, null, false, 40);
                var rej = Ui.Btn(t.T("ui.client.reject"), () => { s.Do(new RejectOffer(), t.T("ui.formation.rejected")); Done(); }, "ButtonSecondary", IconKind.Close, null, false, 40);
                acc.SizeFlagsHorizontal = rej.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(acc); row.AddChild(rej);
                box.AddChild(row);
                d.Body.AddChild(Ui.Section(t.T("ui.popup.decisions")));
                d.Body.AddChild(Ui.Panel(box, "Inset"));
            }
            else if (pol.Formation is { PlayerIsFormateur: true })
            {
                var box = Ui.VBox(8);
                box.AddChild(Ui.L(t.T("ui.popup.you_form", pol.Formation.TurnsLeft), Tx.BodyStrong, AppTheme.Ink, true));
                var open = Ui.Btn(t.T("ui.popup.open_politics"), () => { Done(); main.OpenScreen<PoliticsScreen>(); }, "", IconKind.Politics, null, false, 38);
                open.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                box.AddChild(open);
                d.Body.AddChild(Ui.Section(t.T("ui.popup.decisions")));
                d.Body.AddChild(Ui.Panel(box, "Inset"));
            }
            if (pol.Ultimatums is { Count: > 0 } ultimatums)
                foreach (var u in ultimatums)
                {
                    string text = u.DemandId != null && s.Texts.DemandOf(u.PartyId, u.DemandId) is { } dm
                        ? t.T("ui.event.ultimatum_demand", t.Name(u.PartyId), u.TurnsLeft, s.Texts.Demand(dm))
                        : t.T("ui.event.ultimatum", t.Name(u.PartyId), u.TurnsLeft);
                    var row = Ui.HBox(10);
                    row.AddChild(new IconView(IconKind.Alert, 18, AppTheme.WarnText));
                    row.AddChild(Ui.L(Ui.Fix(text), Tx.BodyStrong, AppTheme.WarnText, true));
                    d.Body.AddChild(Ui.Panel(row, "Warn"));
                }
        }

        // Key indicators as pills.
        var grid = new FlowGrid { MinColumn = 230, MaxColumns = 3, Gap = 14 };
        foreach (var (id, key) in new[] { ("base:gdp", "ui.hud.gdp"), ("base:unemployment", "ui.hud.unemployment"), ("base:inflation", "ui.hud.inflation"),
                     ("base:deficit_pct", "ui.hud.deficit"), ("base:debt", "ui.fig.debt"), ("base:crime", "") })
        {
            var def = s.Packs.Values.FirstOrDefault(v => v.Id == id);
            if (def == null) continue;
            string label = key.Length > 0 ? t.T(key) : t.Name(id);
            string value = Ui.Fix(Fmt.Value(def, s.Country.Values.GetValueOrDefault(id)));
            if (id == "base:debt" && s.Figures.DebtPercentOfGdp is { } dr) value = Ui.Num(dr, "0") + " " + t.T("ui.unit.gdp_short");
            var change = s.Last?.Changes.FirstOrDefault(c => c.ValueId == id);
            string tip = label + ": " + value + (change != null ? "  (" + Ui.Fix(Fmt.Value(def, change.Before)) + " → " + Ui.Fix(Fmt.Value(def, change.After)) + ")" : "");
            grid.AddChild(new IndicatorPill(Icons.For(id), label, value, Ui.Goodness(s, id), tip));
        }
        d.Body.AddChild(grid);

        // News.
        if (s.Digest.Count > 0)
        {
            d.Body.AddChild(Ui.Section(t.T("ui.popup.news")));
            foreach (var line in s.Digest.Take(5))
            {
                var row = Ui.HBox(12);
                bool alert = line.Contains("ultim", StringComparison.OrdinalIgnoreCase);
                row.AddChild(new PictoDisc(alert ? IconKind.Alert : IconKind.News, 26, alert ? new Color(AppTheme.Warn, 0.16f) : AppTheme.Panel, alert ? AppTheme.WarnText : AppTheme.Ink2) { SizeFlagsVertical = SizeFlags.ShrinkBegin });
                row.AddChild(Ui.L(Ui.Fix(line), Tx.Body, AppTheme.Ink, true));
                d.Body.AddChild(row);
            }
            if (s.Digest.Count > 5)
            {
                var more = Ui.Btn(t.T("ui.brief.show_all", s.Digest.Count), () => { Done(); main.OpenScreen<NewsScreen>(); }, "ButtonGhost");
                more.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                d.Body.AddChild(more);
            }
        }

        var buttons = Ui.HBox(16);
        var left = Ui.Btn(t.T(s.HasPolitics ? "ui.popup.open_politics" : "ui.brief.charts"), () => { Done(); if (s.HasPolitics) main.OpenScreen<PoliticsScreen>(); else main.OpenScreen<ChartsScreen>(); }, "", null, null, false, 40);
        var right = Ui.Btn(t.T(welcome ? "ui.brief.begin" : "ui.popup.continue"), Done, "", null, null, d.Blocking, 40);
        left.SizeFlagsHorizontal = right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.AddThemeFontOverride("font", AppTheme.Bold); right.AddThemeFontOverride("font", AppTheme.Bold);
        buttons.AddChild(left); buttons.AddChild(right);
        d.Body.AddChild(Ui.Gap(4));
        d.Body.AddChild(buttons);
        if (d.Blocking) d.Body.AddChild(Centred(Ui.L(t.T("ui.brief.decide_hint"), Tx.Small, AppTheme.WarnText, true)));
        d.Refit();
        return d;

        void Done() { s.Digest.Clear(); d.Close(); }
    }

    private static string Shorten(string s)
    {
        int dot = s.IndexOfAny(new[] { '.', ':' });
        string head = dot > 20 && dot < 90 ? s[..dot] : s;
        return head.Length > 90 ? head[..87] + "…" : head;
    }
}
