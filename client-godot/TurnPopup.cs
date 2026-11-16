using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
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

    /// <summary>
    /// Decisions that wait (decision 0015, <see cref="Decisions.Open"/>): an offer, the player's own formation, unanswered proposals, ultimatums to the
    /// player's government, the window for promises. The briefing offers exactly these, each inline.
    /// </summary>
    public static int DecisionCount(Session s) => Decisions.Open(s.Country, s.Packs).Count;

    public static bool HasDecisions(Session s) => DecisionCount(s) > 0;

    private static Label Centred(Label l) { l.HorizontalAlignment = HorizontalAlignment.Center; return l; }

    public static Dialog Open(Main main, Session s, bool welcome = false)
    {
        var t = s.T;
        var pol = s.Politics;
        string role = pol?.PlayerRole switch { Role.Partner => "_partner", Role.Opposition => "_opposition", _ => "" };
        string title = welcome ? t.T("ui.brief.welcome_title" + role) : pol?.Formation?.Offer is { } o ? t.T("ui.event.offer_made", t.Name(o.Formateur))
            : pol?.Formation is { PlayerIsFormateur: true } ? t.T("ui.brief.title_formateur")
            : s.Digest.Count > 0 ? Shorten(s.Digest[0]) : t.T("ui.brief.quiet_title");
        var d = main.NewDialog("", null, 900);
        Label hint = null!;
        IconTextButton? cont = null;
        d.Key = "briefing";
        d.Closed += () => s.Digest.Clear();
        d.Blocking = Main.Blocking(s);
        d.CloseEnabled = !d.Blocking;
        d.Body.AddChild(Centred(Ui.L(t.T("ui.brief.overline", s.DateText()), Tx.Overline, AppTheme.Ink3)));
        var head = Centred(Ui.L(Ui.Fix(title), Tx.Title, AppTheme.Ink, true));
        head.AddThemeFontSizeOverride("font_size", 26);
        d.Body.AddChild(head);
        if (welcome) d.Body.AddChild(Ui.L(s.HasPolitics ? t.T("ui.client.welcome_politics" + role, t.Name(pol!.PlayerParty)) : t.T("ui.client.welcome"), Tx.Body, AppTheme.Ink, true));

        // Decisions first, each answered inline (decision 0015); the box redraws itself after an answer.
        var decisions = Ui.VBox(10);
        d.Body.AddChild(decisions);
        void FillDecisions()
        {
            if (!GodotObject.IsInstanceValid(decisions)) return;
            Ui.Clear(decisions);
            var open = Decisions.Open(s.Country, s.Packs);
            if (open.Count > 0) decisions.AddChild(Ui.Section(t.T("ui.popup.decisions") + (open.Count > 1 ? $" ({open.Count})" : "")));
            foreach (var dec in open)
                if (DecisionCards.For(main, s, dec, FillDecisions) is { } card) decisions.AddChild(card);
            d.Blocking = Main.Blocking(s);
            d.CloseEnabled = !d.Blocking;
            if (cont != null && GodotObject.IsInstanceValid(cont)) cont.Disabled = d.Blocking;
            if (GodotObject.IsInstanceValid(hint)) hint.Visible = d.Blocking;
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
            if (id == "base:deficit_pct" && s.Country.Values.GetValueOrDefault(id) < 0) { label = t.T("ui.fig.surplus"); value = Ui.Fix(Fmt.Value(def, -s.Country.Values[id])); }
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

        // The two answers live in the dialog's own footer, so they stay in view however long the news is.
        d.AddAction(t.T(s.HasPolitics ? "ui.popup.open_politics" : "ui.brief.charts"), s.HasPolitics ? IconKind.Politics : IconKind.Chart,
            () => { Done(); if (s.HasPolitics) main.OpenScreen<PoliticsScreen>(); else main.OpenScreen<ChartsScreen>(); });
        cont = d.AddAction(t.T(welcome ? "ui.brief.begin" : "ui.popup.continue"), IconKind.Play, Done, d.Blocking, "");
        hint = Centred(Ui.L(t.T("ui.brief.decide_hint"), Tx.Small, AppTheme.WarnText, true));
        hint.Visible = d.Blocking;
        d.Body.AddChild(hint);
        FillDecisions();
        d.Refit();
        return d;

        void Done() { s.Digest.Clear(); d.Close(); }
    }

    /// <summary>
    /// A proposal to support or oppose (decision 0014): who proposes what in shown units, what an answer means (fits / goes against the programme,
    /// whether the player's seats decide), and Support / Oppose buttons that answer inline. <paramref name="refresh"/> redraws the host afterwards.
    /// </summary>
    public static Control ProposalCard(Session s, ProposalInfo p, Action? refresh = null)
    {
        var box = Ui.VBox(8);
        Fill(s, p, box, refresh);
        return Ui.Panel(box, "Inset");
    }

    private static void Fill(Session s, ProposalInfo p, VBoxContainer box, Action? refresh)
    {
        var t = s.T;
        Ui.Clear(box);
        var head = Ui.HBox(10);
        head.AddChild(new PictoDisc(Icons.For(p.PolicyId), 30));
        var (who, what) = s.Texts.Proposal(p);
        var titles = Ui.VBox(2);
        titles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titles.AddChild(Ui.PartyName(s, p.Proposer, who, Tx.Label));
        titles.AddChild(Ui.L(Ui.Fix(what), Tx.BodyStrong, AppTheme.Ink, true));
        head.AddChild(titles);
        head.AddChild(Ui.Pill(t.T("ui.proposal.heading"), p.Fit > 1e-9 ? AppTheme.Good : p.Fit < -1e-9 ? AppTheme.Bad : AppTheme.Chrome));
        box.AddChild(head);
        var hints = s.Texts.ProposalHints(p);
        for (int i = 0; i < hints.Count; i++)
            box.AddChild(Ui.L(hints[i], i == 0 ? Tx.Body : Tx.Small, i == 0 ? (p.Fit > 1e-9 ? AppTheme.GoodText : p.Fit < -1e-9 ? AppTheme.BadText : AppTheme.Ink2) : AppTheme.Ink3, true));
        if (p.Answer is { } answer)
        {
            box.AddChild(Ui.L(t.T(answer == Stance.Support ? "ui.proposal.answered_support" : "ui.proposal.answered_oppose"), Tx.BodyStrong, AppTheme.Ink2));
            return;
        }
        void Answer(PlayerAction action, string ok)
        {
            if (!s.Do(action, ok)) return;
            var now = s.Politics?.Proposals?.FirstOrDefault(x => x.Id == p.Id);
            if (refresh != null) refresh();
            else if (now != null && GodotObject.IsInstanceValid(box)) Fill(s, now, box, null);
        }
        var row = Ui.HBox(1);
        var yes = Ui.Btn(t.T("ui.client.support"), () => Answer(new SupportProposal(p.Id), t.T("ui.proposal.ok_support", t.Name(p.PolicyId))), "", IconKind.Check, s.Texts.Explain("proposals"), false, 40);
        var no = Ui.Btn(t.T("ui.client.oppose"), () => Answer(new OpposeProposal(p.Id), t.T("ui.proposal.ok_oppose", t.Name(p.PolicyId))), "ButtonSecondary", IconKind.Close, s.Texts.Explain("proposals"), false, 40);
        yes.SizeFlagsHorizontal = no.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(yes); row.AddChild(no);
        box.AddChild(row);
    }

    private static string Shorten(string s)
    {
        int dot = s.IndexOfAny(new[] { '.', ':' });
        string head = dot > 20 && dot < 90 ? s[..dot] : s;
        return head.Length > 90 ? head[..87] + "…" : head;
    }
}
