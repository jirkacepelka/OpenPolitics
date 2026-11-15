using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>One key figure in the top bar: a small chrome label over a bold value with its unit and a small trend.</summary>
public partial class FigureCell : Control
{
    public string Label { get; private set; } = "";
    public string Value { get; private set; } = "";
    public string Unit { get; private set; } = "";
    public string Delta { get; private set; } = "";
    public Color DeltaColor { get; private set; } = AppTheme.Ink3;
    public bool Alert { get; private set; }
    private bool _hover;
    public event Action? Pressed;

    public FigureCell() { MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand; SizeFlagsVertical = SizeFlags.Fill; }

    public void Set(string label, string value, string unit, string delta, Color deltaColor, bool alert, string tooltip)
    {
        Label = label; Value = value; Unit = unit; Delta = delta; DeltaColor = deltaColor; Alert = alert; TooltipText = tooltip;
        UpdateMinimumSize(); QueueRedraw();
    }

    private static int ValueSize => Layout.Compact ? 16 : 19;

    public override Vector2 _GetMinimumSize()
    {
        float l = AppTheme.Face(700, false, 1).GetStringSize(Label.ToUpperInvariant(), HorizontalAlignment.Left, -1, 11).X + (Alert ? 12 : 0);
        float v = AppTheme.FigBold.GetStringSize(Value, HorizontalAlignment.Left, -1, ValueSize).X
                  + (Unit.Length > 0 ? 4 + AppTheme.Semi.GetStringSize(Unit, HorizontalAlignment.Left, -1, 12).X : 0)
                  + (Delta.Length > 0 ? 6 + AppTheme.FigSemi.GetStringSize(Delta, HorizontalAlignment.Left, -1, 11).X : 0);
        return new Vector2(Math.Max(l, v) + 28, 40);
    }

    public override void _Draw()
    {
        if (_hover) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.04f));
        float x = 14, cy = Size.Y / 2;
        float lx = x;
        if (Alert) { DrawCircle(new Vector2(x + 4, cy - 11), 4, AppTheme.Bad); lx += 12; }
        DrawString(AppTheme.Face(700, false, 1), new Vector2(lx, cy - 7), Label.ToUpperInvariant(), HorizontalAlignment.Left, -1, 11, AppTheme.Chrome);
        int vs = ValueSize;
        DrawString(AppTheme.FigBold, new Vector2(x, cy + 15), Value, HorizontalAlignment.Left, -1, vs, AppTheme.Ink);
        float vx = x + AppTheme.FigBold.GetStringSize(Value, HorizontalAlignment.Left, -1, vs).X;
        if (Unit.Length > 0) { DrawString(AppTheme.Semi, new Vector2(vx + 4, cy + 15), Unit, HorizontalAlignment.Left, -1, 12, AppTheme.Ink3); vx += 4 + AppTheme.Semi.GetStringSize(Unit, HorizontalAlignment.Left, -1, 12).X; }
        if (Delta.Length > 0) DrawString(AppTheme.FigSemi, new Vector2(vx + 6, cy + 14), Delta, HorizontalAlignment.Left, -1, 11, DeltaColor);
        DrawRect(new Rect2(Size.X - 1, 14, 1, Size.Y - 28), AppTheme.Hairline);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Pressed?.Invoke(); AcceptEvent(); }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
    }
}

/// <summary>The key figures in priority order; those that do not fit go behind a "+N" button that lists them in a popup.</summary>
public partial class FigureStrip : Container
{
    private readonly Button _more = new() { ThemeTypeVariation = "ButtonGhost", FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
    private readonly PopupPanel _popup = new();
    private readonly VBoxContainer _popupBody = Ui.VBox(2);
    public List<FigureCell> Cells { get; } = new();

    public FigureStrip()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        _more.AddThemeFontOverride("font", AppTheme.Bold);
        _more.AddThemeFontSizeOverride("font_size", 15);
        _more.Pressed += ShowMore;
        AddChild(_more);
        _popup.AddChild(_popupBody);
        AddChild(_popup);
    }

    public void Add(FigureCell cell) { Cells.Add(cell); AddChild(cell); MoveChild(_more, -1); }

    public override Vector2 _GetMinimumSize() => new(Cells.Count > 0 ? Math.Min(Cells[0].GetCombinedMinimumSize().X, 140) + 44 : 0, 40);

    private int _shown;

    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren) return;
        float avail = Size.X, x = 0;
        var visible = Cells.Where(c => c.Value.Length > 0).ToList();
        int shown = 0;
        for (int i = 0; i < visible.Count; i++)
        {
            float w = visible[i].GetCombinedMinimumSize().X;
            float reserve = i < visible.Count - 1 ? 48 : 0;
            if (x + w + reserve > avail) break;
            x += w; shown++;
        }
        _shown = shown;
        x = 0;
        foreach (var c in Cells) c.Visible = false;
        for (int i = 0; i < visible.Count; i++)
        {
            var c = visible[i];
            c.Visible = i < shown;
            if (!c.Visible) continue;
            float w = c.GetCombinedMinimumSize().X;
            FitChildInRect(c, new Rect2(x, 0, w, Size.Y));
            x += w;
        }
        int hidden = visible.Count - shown;
        _more.Visible = hidden > 0;
        _more.Text = "+" + hidden;
        if (hidden > 0) FitChildInRect(_more, new Rect2(x + 4, (Size.Y - 36) / 2, 42, 36));
    }

    private void ShowMore()
    {
        Ui.Clear(_popupBody);
        var visible = Cells.Where(c => c.Value.Length > 0).ToList();
        foreach (var c in visible.Skip(_shown))
        {
            var row = Ui.HBox(10);
            var name = Ui.L(c.Label, Tx.Overline, AppTheme.Chrome);
            name.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(name);
            row.AddChild(Ui.L(c.Value + (c.Unit.Length > 0 ? " " + c.Unit : ""), Tx.Data));
            row.AddChild(Ui.L(c.Delta, Tx.Micro, c.DeltaColor));
            var b = new Button { ThemeTypeVariation = "ButtonGhost", CustomMinimumSize = new Vector2(280, 34), FocusMode = FocusModeEnum.None, TooltipText = c.TooltipText };
            b.AddChild(Ui.Margin(row, 8, 4));
            row.MouseFilter = MouseFilterEnum.Ignore;
            foreach (var ch in row.GetChildren()) ((Control)ch).MouseFilter = MouseFilterEnum.Ignore;
            var cell = c;
            b.Pressed += () => { _popup.Hide(); cell.EmitPress(); };
            _popupBody.AddChild(b);
        }
        var rect = GetGlobalRect();
        _popup.Popup(new Rect2I((int)(rect.Position.X + _more.Position.X), (int)rect.End.Y, 300, 10));
    }
}

public partial class FigureCell
{
    public void EmitPress() => Pressed?.Invoke();
}

/// <summary>The clock and a wide progress bar towards the next election (or the end of the 20-year horizon).</summary>
public partial class TermProgress : Control
{
    private double _frac;
    public TermProgress() { CustomMinimumSize = new Vector2(150, 40); MouseFilter = MouseFilterEnum.Stop; }
    public void Set(double frac, string tooltip) { _frac = Math.Clamp(frac, 0, 1); TooltipText = tooltip; QueueRedraw(); }
    public override void _Draw()
    {
        float cy = Size.Y / 2, icon = 34;
        DrawArc(new Vector2(18 + icon / 2 - 14, cy), icon / 2 - 1.5f, 0, Mathf.Tau, 48, AppTheme.Chrome, 3f, true);
        var c = new Vector2(18 + icon / 2 - 14, cy);
        DrawLine(c, c + new Vector2(0, -10), AppTheme.Chrome, 3f, true);
        DrawLine(c, c + new Vector2(7, 4), AppTheme.Chrome, 3f, true);
        float x0 = icon + 14, x1 = Size.X - 16, h = 16;
        if (x1 - x0 < 30) return;
        DrawStyleBox(AppTheme.Box(AppTheme.Panel2, 8), new Rect2(x0, cy - h / 2, x1 - x0, h));
        float fw = (float)((x1 - x0) * _frac);
        if (fw > 0) DrawStyleBox(AppTheme.Box(AppTheme.Chrome, 8), new Rect2(x0, cy - h / 2, Math.Max(fw, h), h));
    }
}

/// <summary>
/// The top bar (Democracy 4 layout): key figures on the left, the term clock and progress bar, a row of square chrome tabs and the
/// green Play (End quarter) with a narrow x4 beside it.
/// </summary>
public partial class TopBar : PanelContainer
{
    private readonly Main _main;
    private readonly FigureStrip _figures = new();
    private readonly TermProgress _progress = new();
    private readonly HBoxContainer _tabs = Ui.HBox(1);
    private readonly Dictionary<string, SquareTab> _tabById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FigureCell> _cells = new(StringComparer.Ordinal);
    private SquareTab _play = null!, _four = null!;

    public TopBar(Main main, Session s)
    {
        _main = main;
        ThemeTypeVariation = "TopBar";
        var row = Ui.HBox(0);
        row.AddChild(_figures);
        row.AddChild(_progress);
        _tabs.SizeFlagsVertical = SizeFlags.Fill;
        row.AddChild(_tabs);
        AddChild(row);
        var t = s.T;

        foreach (var (id, key) in new[] { ("revenue", "ui.fig.income"), ("spending", "ui.fig.expenditure"), ("deficit", "ui.fig.deficit"), ("debt", "ui.fig.debt"),
                     ("gdp", "ui.hud.gdp"), ("growth", "ui.hud.growth"), ("unemployment", "ui.hud.unemployment"), ("inflation", "ui.hud.inflation"), ("mood", "ui.hud.mood") })
        {
            var cell = new FigureCell();
            string chart = id == "spending" ? "revenue" : id;
            cell.Pressed += () => { if (Charts.All.FirstOrDefault(c => c.Id == chart) is { } spec) _main.OpenChart(spec); };
            _cells[id] = cell;
            _figures.Add(cell);
        }

        void Tab(string id, IconKind icon, string tip, Action press)
        {
            var tab = new SquareTab(icon, tip);
            tab.Pressed += press;
            _tabById[id] = tab;
            _tabs.AddChild(tab);
        }
        Tab("map", IconKind.Map, t.T("ui.tab.map"), () => _main.Open(null));
        Tab("voters", IconKind.Voters, t.T("ui.client.tab.voters"), () => _main.OpenScreen<VotersScreen>());
        Tab("charts", IconKind.Chart, t.T("ui.client.charts"), () => _main.OpenScreen<ChartsScreen>());
        Tab("budget", IconKind.Coin, t.T("ui.tab.budget"), () => _main.OpenScreen<BudgetScreen>());
        if (s.HasPolitics)
        {
            Tab("politics", IconKind.Politics, t.T("ui.client.tab.politics"), () => _main.OpenScreen<PoliticsScreen>());
            Tab("party", IconKind.Party, t.T("ui.client.tab.party"), () => _main.OpenScreen<PartyScreen>());
        }
        Tab("news", IconKind.News, t.T("ui.client.tab.news"), () => _main.OpenScreen<NewsScreen>());
        Tab("menu", IconKind.Gear, t.T("ui.client.menu"), () => _main.ShowMenu(_tabById["menu"]));
        _four = new SquareTab(IconKind.FastForward, t.T("ui.client.four_quarters")) { Fill = AppTheme.PlayDark, HoverFill = AppTheme.PlayDark.Darkened(0.12f), Caption = "×4" };
        _four.Pressed += _main.PlayFour;
        _tabs.AddChild(_four);
        _play = new SquareTab(IconKind.Play, t.T("ui.client.end_quarter") + "\n" + t.T("ui.client.end_quarter_hint")) { Fill = AppTheme.Play, HoverFill = AppTheme.PlayDark };
        _play.Pressed += _main.EndQuarterPressed;
        _tabs.AddChild(_play);
        Relayout();
    }

    public void Relayout()
    {
        float h = Layout.TopBar;
        CustomMinimumSize = new Vector2(0, h);
        float tw = Layout.Compact ? 50 : Layout.Wide ? h : 64;
        foreach (var tab in _tabById.Values) tab.CustomMinimumSize = new Vector2(tw, h);
        _four.CustomMinimumSize = new Vector2(Layout.Compact ? 36 : h * 0.55f, h);
        _play.CustomMinimumSize = new Vector2(Layout.Compact ? 60 : h * 1.15f, h);
        _progress.Visible = Layout.Width != WidthClass.Compact;
        _progress.CustomMinimumSize = new Vector2(Layout.Wide ? 260 : 150, 40);
        _progress.SizeFlagsHorizontal = Layout.Wide ? SizeFlags.ExpandFill : SizeFlags.Fill;
        _progress.SizeFlagsStretchRatio = 0.35f;
        foreach (var c in _figures.Cells) c.UpdateMinimumSize();
        _figures.QueueSort();
    }

    public void SetActive(string id) { foreach (var (k, tab) in _tabById) { tab.Active = k == id; tab.QueueRedraw(); } }

    public void SetBadge(string id, int count) { if (_tabById.TryGetValue(id, out var tab)) { tab.Badge = count; tab.QueueRedraw(); } }

    public void SetPlayBlocked(bool blocked, string tip)
    {
        _play.Fill = blocked ? AppTheme.Warn : AppTheme.Play;
        _play.HoverFill = blocked ? AppTheme.Warn.Darkened(0.12f) : AppTheme.PlayDark;
        _play.TooltipText = tip;
        _play.QueueRedraw();
    }

    public void Update(Session s)
    {
        var t = s.T;
        double Last(string key) { var x = s.History.Series(key); return x.Count > 0 ? x[^1] : double.NaN; }
        double Prev(string key) { var x = s.History.Series(key); return x.Count > 1 ? x[^2] : Last(key); }
        void Cell(string id, string labelKey, string key, Func<double, (string V, string U)> fmt, bool higherIsGood, Func<double, bool>? alert = null, string fmtDelta = "0.0", double deltaScale = 1)
        {
            var cell = _cells[id];
            double v = Last(key), d = v - Prev(key);
            if (!double.IsFinite(v)) { cell.Set(t.T(labelKey), "", "", "", AppTheme.Ink3, false, ""); return; }
            var (val, unit) = fmt(v);
            bool showNumber = Layout.Wide;
            int decimals = fmtDelta.Contains('.') ? fmtDelta.Length - fmtDelta.IndexOf('.') - 1 : 0;
            string delta = Math.Abs(d * deltaScale) < 0.5 * Math.Pow(10, -decimals) ? "" : (d > 0 ? "▲ " : "▼ ") + Ui.Num(Math.Abs(d * deltaScale), fmtDelta);
            var color = Math.Abs(d) < 1e-12 ? AppTheme.Ink3 : (d > 0) == higherIsGood ? AppTheme.GoodText : AppTheme.BadText;
            string full = delta;
            if (!showNumber && delta.Length > 0) delta = delta[..1];
            cell.Set(t.T(labelKey), val, unit, delta, color, alert?.Invoke(v) ?? false, t.T(labelKey) + ": " + val + " " + unit + (full.Length > 0 ? "  (" + full + ")" : ""));
        }
        (string, string) Money(double v) => Ui.Money(t, v);
        Cell("revenue", "ui.fig.income", History.ValueKey("base:revenue"), Money, true, null, "0.00", 1e-12);
        Cell("spending", "ui.fig.expenditure", History.ValueKey("base:spending"), Money, false, null, "0.00", 1e-12);
        Cell("deficit", "ui.fig.deficit", History.ValueKey("base:deficit_pct"), v => (Ui.Num(v), t.T("ui.unit.gdp_short")), false, v => v > 3);
        Cell("debt", "ui.fig.debt", History.DebtRatio, v => (Ui.Num(v, "0"), t.T("ui.unit.gdp_short")), false, v => v > 60, "0");
        Cell("gdp", "ui.hud.gdp", History.ValueKey("base:gdp"), Money, true, null, "0.00", 1e-12);
        Cell("growth", "ui.hud.growth", History.ValueKey("base:real_growth"), v => (Ui.Num(v), "%"), true, v => v < 0);
        Cell("unemployment", "ui.hud.unemployment", History.ValueKey("base:unemployment"), v => (Ui.Num(v), "%"), false, v => v > 8);
        Cell("inflation", "ui.hud.inflation", History.ValueKey("base:inflation"), v => (Ui.Num(v), "%"), false, v => v > 4);
        Cell("mood", "ui.hud.mood", History.Mood, v => (Ui.Num(v, "0.00"), ""), true, v => v < -0.3, "0.00");
        _figures.QueueSort();

        if (s.Politics is { } pol)
        {
            int interval = s.Packs.Countries.FirstOrDefault(c => c.Politics != null)?.Politics?.ElectionIntervalTurns ?? 16;
            _progress.Set(1 - pol.TurnsToElection / (double)Math.Max(1, interval),
                t.T("ui.line.next_election") + ": " + (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.politics.election_in", pol.TurnsToElection)) + "\n" + s.DateText());
        }
        else _progress.Set(s.Turn / (double)Session.HorizonTurns, s.DateText() + $"\n{s.Turn} / {Session.HorizonTurns}");
    }
}
