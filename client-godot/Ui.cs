using System.Globalization;
using System.Text.RegularExpressions;
using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>Small helpers that build the UI in code. Text roles map to the theme type variations of <see cref="AppTheme"/>.</summary>
public static class Ui
{
    /// <summary>Czech number formatting (decimal comma, true minus) when the game runs in Czech.</summary>
    public static bool Czech { get; set; }

    private static readonly Regex Decimal = new(@"(?<=\d)\.(?=\d)", RegexOptions.Compiled);
    private static readonly Regex Minus = new(@"(?<![\w])-(?=\d)", RegexOptions.Compiled);

    /// <summary>Localises the numbers inside a formatted string: decimal comma in Czech, U+2212 for minus.</summary>
    public static string Fix(string s)
    {
        if (Czech)
        {
            s = Decimal.Replace(s, ",");
            s = s.Replace(" tn CZK", " bil.\u00a0Kč").Replace(" bn CZK", " mld.\u00a0Kč").Replace(" m CZK", " mil.\u00a0Kč");
        }
        s = s.Replace(">=", "\u2265").Replace("<=", "\u2264");
        return Minus.Replace(s, "−");
    }

    /// <summary>A CZK amount as (figure, unit): "1,84" + "bil. Kč" / "1.84" + "tn CZK".</summary>
    public static (string, string) Money(OpenPolitics.Core.Presentation.Localizer t, double v) =>
        Math.Abs(v) >= 1e12 ? (Num(v / 1e12, "0.00"), t.T("ui.unit.tn")) : (Num(v / 1e9, "0"), t.T("ui.unit.bn"));

    public static string Num(double v, string fmt = "0.0") => double.IsFinite(v) ? Fix(v.ToString(fmt, CultureInfo.InvariantCulture)) : "–";
    public static string Signed(double v, string fmt = "0.0") => double.IsFinite(v) ? (v > 0 ? "+" : "") + Num(v, fmt) : "–";

    /// <summary>Splits "5.42 tn CZK" into ("5,42", "tn CZK"); text without a leading number stays whole.</summary>
    public static (string Figure, string Unit) Split(string formatted)
    {
        formatted = Fix(formatted);
        int sp = formatted.IndexOf(' ');
        if (sp <= 0 || !(char.IsDigit(formatted[0]) || formatted[0] == '−' || formatted[0] == '+')) return (formatted, "");
        return (formatted[..sp], formatted[(sp + 1)..]);
    }

    public static Label L(string text, Tx role = Tx.Body, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = role == Tx.Overline ? text.ToUpperInvariant() : text, ThemeTypeVariation = "T" + role };
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.CustomMinimumSize = new Vector2(60, 0); }
        if (Czech) l.Language = "cs";
        return l;
    }

    public static Label Right(Label l) { l.HorizontalAlignment = HorizontalAlignment.Right; return l; }

    public static IconTextButton Btn(string text, Action onPress, string variation = "", IconKind? icon = null, string? tooltip = null, bool disabled = false, float height = 34)
    {
        var b = new IconTextButton(icon) { Text = text, Disabled = disabled, TooltipText = tooltip ?? "", ThemeTypeVariation = variation, FocusMode = Control.FocusModeEnum.None };
        b.CustomMinimumSize = new Vector2(0, height);
        b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        b.MouseDefaultCursorShape = disabled ? Control.CursorShape.Forbidden : Control.CursorShape.PointingHand;
        b.Pressed += onPress;
        return b;
    }

    /// <summary>A button with the political-capital cost after the label.</summary>
    public static IconTextButton Costed(string text, int cost, Action onPress, string? tooltip = null, bool disabled = false, string variation = "") =>
        Btn(cost > 0 ? $"{text} · {cost}" : text, onPress, variation, cost > 0 ? IconKind.Coin : null, tooltip, disabled);

    public static VBoxContainer VBox(int sep = 8) { var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", sep); return v; }
    public static HBoxContainer HBox(int sep = 8) { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", sep); return h; }

    public static MarginContainer Margin(Control child, int h, int v = -1, int? top = null, int? bottom = null)
    {
        if (v < 0) v = h;
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", h); m.AddThemeConstantOverride("margin_right", h);
        m.AddThemeConstantOverride("margin_top", top ?? v); m.AddThemeConstantOverride("margin_bottom", bottom ?? v);
        m.AddChild(child);
        return m;
    }

    public static PanelContainer Panel(Control child, string variation, bool expand = true)
    {
        var p = new PanelContainer { ThemeTypeVariation = variation };
        if (expand) p.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        p.AddChild(child);
        return p;
    }

    /// <summary>A section heading: an uppercase small label with a hairline after it.</summary>
    public static Control Section(string text, Control? right = null)
    {
        var row = HBox(10);
        row.AddChild(L(text.TrimEnd(':', ' '), Tx.Overline, AppTheme.Ink2));
        var line = new HairLine { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(line);
        if (right != null) row.AddChild(right);
        return row;
    }

    /// <summary>A rounded status label (traffic colour).</summary>
    public static Control Pill(string text, Color color)
    {
        var p = new PanelContainer();
        var box = AppTheme.Box(new Color(color, 0.16f), 11, null, 1, 9, 2);
        p.AddThemeStyleboxOverride("panel", box);
        p.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        p.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var l = L(text, Tx.Small, color == AppTheme.Good ? AppTheme.GoodText : color == AppTheme.Bad ? AppTheme.BadText : color == AppTheme.Warn ? AppTheme.WarnText : color);
        l.AddThemeFontOverride("font", AppTheme.Semi);
        p.AddChild(l);
        return p;
    }

    public static Control Spacer() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
    public static Control Gap(int px) => new() { CustomMinimumSize = new Vector2(px, px), MouseFilter = Control.MouseFilterEnum.Ignore };
    public static Control Rule() => new HairLine { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    public static void Clear(Node n) { foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); } }

    /// <summary>A scroll container that never scrolls sideways, with a VBox body.</summary>
    public static (ScrollContainer Scroll, VBoxContainer Body) Scroller(int sep = 12)
    {
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = VBox(sep);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(body);
        return (scroll, body);
    }

    /// <summary>A row of toggle buttons (one pressed) in the flat grey/chrome segment look.</summary>
    public static HBoxContainer Segmented(IReadOnlyList<string> labels, int selected, Action<int> onSelect, float height = 30)
    {
        var row = HBox(2);
        var group = new ButtonGroup();
        for (int i = 0; i < labels.Count; i++)
        {
            int k = i;
            var b = new Button { Text = labels[i], ToggleMode = true, ButtonGroup = group, ButtonPressed = i == selected, ThemeTypeVariation = "ButtonSeg",
                FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, height), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            b.Pressed += () => onSelect(k);
            row.AddChild(b);
        }
        return row;
    }

    // ---- parties -------------------------------------------------------------------------------------------
    private static readonly Color[] Palette =
    {
        new("1d3f9e"), new("d9730d"), new("0b9a5e"), new("c860b0"), new("86561c"), new("5a2e7a"), new("3f7fe0"), new("7e1f33"),
    };
    public static Color PaletteColor(int i) => Palette[((i % Palette.Length) + Palette.Length) % Palette.Length];

    private static readonly Dictionary<string, Color> Parties = new(StringComparer.Ordinal)
    {
        ["base:citizens_movement"] = new("3f7fe0"), ["base:social_alliance"] = new("d9730d"), ["base:civic_right"] = new("1d3f9e"),
        ["base:peoples_union"] = new("86561c"), ["base:workers_left"] = new("7e1f33"), ["base:homeland_first"] = new("5a2e7a"),
        ["base:green_wave"] = new("0b9a5e"), ["base:open_net"] = new("c860b0"),
    };

    /// <summary>The colour of a party, fixed by id (the same in charts, compass, parliament and polls).</summary>
    public static Color PartyColor(Session s, string id) => PartyColor(s.Packs, id);

    public static Color PartyColor(OpenPolitics.Core.Packs.PackSet packs, string id)
    {
        if (Parties.TryGetValue(id, out var c)) return c;
        var ids = packs.Parties.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return PaletteColor(ids.IndexOf(id));
    }

    public static Control PartyName(Session s, string id, string? text = null, Tx role = Tx.BodyStrong)
    {
        var row = HBox(8);
        row.AddChild(new ColorDot(PartyColor(s, id)));
        var l = L(text ?? s.T.Name(id), role);
        row.AddChild(l);
        return row;
    }

    /// <summary>How much voters like a value going up (positive), down (negative) or not at all (0).</summary>
    public static double VoterLike(Session s, string valueId) =>
        s.Groups.Sum(g => g.Size * g.Weight * (g.ValueWeights.GetValueOrDefault(valueId) + (g.LevelWeights?.GetValueOrDefault(valueId) ?? 0)));

    /// <summary>
    /// How good an indicator is now compared with the start of the game, 0..1 (0.5 = unchanged, or nobody cares), from the direction
    /// voters prefer and the value's own scale.
    /// </summary>
    public static double Goodness(Session s, string valueId)
    {
        var def = s.Packs.Values.FirstOrDefault(v => v.Id == valueId);
        if (def == null) return 0.5;
        double like = Math.Sign(VoterLike(s, valueId));
        if (like == 0) return 0.5;
        var series = s.History.Series(History.ValueKey(valueId));
        double start = series.Count > 0 && double.IsFinite(series[0]) ? series[0] : def.Initial;
        double now = s.Country.Values.GetValueOrDefault(valueId, start);
        double scale = Math.Max(Math.Abs(def.Scale), 1e-9);
        return 0.5 + 0.5 * Math.Tanh(like * (now - start) / scale * 1.2);
    }

    /// <summary>A group's opinion of a policy's current setting, -1..1 (0 when it has none).</summary>
    public static double Opinion(Session s, OpenPolitics.Core.Packs.VoterGroupDef g, OpenPolitics.Core.Packs.PolicyDef p, double? value = null)
    {
        if (!g.PolicyPrefs.TryGetValue(p.Id, out var pref) || !g.PolicyWeights.TryGetValue(p.Id, out var weight) || weight == 0) return 0;
        double v = value ?? s.Country.Policies.GetValueOrDefault(p.Id, p.Default);
        double range = Math.Max(p.Max - p.Min, 1e-9);
        return Math.Clamp(1 - 2.5 * Math.Abs(v - pref) / range, -1, 1) * Math.Min(1, Math.Abs(weight) * 2);
    }

    /// <summary>Popularity of a policy's setting with all voters, 0..1.</summary>
    public static double Popularity(Session s, OpenPolitics.Core.Packs.PolicyDef p, double? value = null)
    {
        double sum = 0, w = 0;
        foreach (var g in s.Groups)
        {
            if (!g.PolicyWeights.ContainsKey(p.Id)) continue;
            sum += g.Size * Opinion(s, g, p, value); w += g.Size;
        }
        return w <= 0 ? 0.5 : Math.Clamp(0.5 + 0.5 * sum / w, 0, 1);
    }
}

/// <summary>A 1 px hairline that fills its width.</summary>
public partial class HairLine : Control
{
    public Color Color { get; set; } = AppTheme.Hairline;
    public HairLine() { CustomMinimumSize = new Vector2(0, 9); SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw() => DrawRect(new Rect2(0, Size.Y / 2, Size.X, 1), Color);
}

/// <summary>A Button with a code-drawn pictogram in the icon slot (tinted like the label).</summary>
public partial class IconTextButton : Button
{
    private readonly IconKind? _kind;
    public float IconSize { get; set; } = 16;
    public IconTextButton(IconKind? kind = null)
    {
        _kind = kind;
        if (kind != null) Icon = AppTheme.Blank(16);
    }

    public override void _Draw()
    {
        if (_kind == null) return;
        var color = Disabled ? GetThemeColor("font_disabled_color") : GetThemeColor(ButtonPressed && ToggleMode ? "font_pressed_color" : "font_color");
        var box = GetThemeStylebox(Disabled ? "disabled" : "normal");
        float x = box.ContentMarginLeft;
        if (Text.Length == 0) x = Size.X / 2 - IconSize / 2;
        else if (Alignment == HorizontalAlignment.Center)
        {
            var font = GetThemeFont("font"); int fs = GetThemeFontSize("font_size");
            float w = 16 + GetThemeConstant("h_separation") + font.GetStringSize(Text, HorizontalAlignment.Left, -1, fs).X;
            x = Math.Max(box.ContentMarginLeft, (Size.X - w) / 2);
        }
        Icons.Draw(this, _kind.Value, new Vector2(x + 8, Size.Y / 2), IconSize, color);
    }
}

public enum TabStyle { Ghost, Filled, Dark }

/// <summary>
/// A square-ish icon tile: Ghost (top-bar tabs: no fill, a soft tile on hover, signature tint when active), Filled (the primary
/// End-quarter action) or Dark (on the graphite voter panel). Hover and press ease over ~120 ms. Optional count badge and caption.
/// </summary>
public partial class SquareTab : Control
{
    private readonly IconKind _kind;
    private bool _hover, _down;
    private float _h; // eased hover 0..1
    public bool Active { get; set; }
    public int Badge { get; set; }
    public TabStyle Style { get; set; } = TabStyle.Ghost;
    public Color Fill { get; set; } = AppTheme.Chrome;
    public Color HoverFill { get; set; } = AppTheme.ChromeDark;
    public string Caption { get; set; } = "";
    public float Inset { get; set; } = 6;
    public float IconScale { get; set; } = 0.36f;
    /// <summary>Draw the tile as a centred square (tabs) rather than filling the control (the wide Play button).</summary>
    public bool Square { get; set; } = true;
    public event Action? Pressed;

    public SquareTab(IconKind kind, string tooltip)
    {
        _kind = kind; TooltipText = tooltip;
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
        SizeFlagsVertical = SizeFlags.Fill;
    }

    public override void _Process(double delta)
    {
        float target = _hover ? 1 : 0;
        if (Math.Abs(_h - target) < 0.01f) { if (_h != target) { _h = target; QueueRedraw(); } SetProcess(false); return; }
        _h = Main.Animations ? Mathf.MoveToward(_h, target, (float)delta * 8f) : target;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size).Grow(-Inset);
        if (Square) { float side = Math.Min(r.Size.X, r.Size.Y); r = new Rect2(r.GetCenter() - new Vector2(side, side) / 2, new Vector2(side, side)); }
        int radius = (int)Math.Min(12, r.Size.Y / 2);
        Color ink;
        switch (Style)
        {
            case TabStyle.Filled:
                DrawStyleBox(AppTheme.Box(Fill.Lerp(HoverFill, _h).Darkened(_down ? 0.08f : 0), radius), r);
                ink = Colors.White;
                break;
            case TabStyle.Dark:
                var dark = Active ? AppTheme.Graphite3 : new Color(1, 1, 1, 0.07f * _h);
                DrawStyleBox(AppTheme.Box(dark, radius), r);
                ink = new Color(1, 1, 1, Active ? 1f : 0.62f + 0.38f * _h);
                break;
            default:
                var fill = Active ? AppTheme.ChromeTint : new Color(AppTheme.Ink, 0.05f * _h + (_down ? 0.04f : 0));
                DrawStyleBox(AppTheme.Box(fill, radius), r);
                ink = Active ? AppTheme.Chrome : AppTheme.Ink2.Lerp(AppTheme.Ink, _h);
                break;
        }
        float icon = Math.Min(r.Size.X, r.Size.Y) * IconScale * (Caption.Length > 0 ? 0.9f : 1f) + 4;
        var c = new Vector2(Size.X / 2, Caption.Length > 0 ? r.Position.Y + r.Size.Y * 0.4f : Size.Y / 2);
        Icons.Draw(this, _kind, c, icon, ink);
        if (Caption.Length > 0)
            DrawString(AppTheme.Semi, new Vector2(r.Position.X, r.End.Y - 7), Caption, HorizontalAlignment.Center, r.Size.X, 11, ink);
        if (Badge > 0)
        {
            var p = new Vector2(r.End.X - 6, r.Position.Y + 6);
            DrawCircle(p, 8.5f, AppTheme.Surface);
            DrawCircle(p, 7, AppTheme.Bad);
            DrawString(AppTheme.FigBold, new Vector2(p.X - 8, p.Y + 3.5f), Badge.ToString(CultureInfo.InvariantCulture), HorizontalAlignment.Center, 16, 10, Colors.White);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed) _down = true;
            else { if (_down && new Rect2(Vector2.Zero, Size).HasPoint(mb.Position)) Pressed?.Invoke(); _down = false; }
            QueueRedraw();
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; SetProcess(true); }
        else if (what == NotificationMouseExit) { _hover = false; _down = false; SetProcess(true); }
    }
}

/// <summary>A small coloured disc (party colour, legend).</summary>
public partial class ColorDot : Control
{
    private readonly Color _color; private readonly float _r;
    public ColorDot(Color color, float radius = 5.5f) { _color = color; _r = radius; CustomMinimumSize = new Vector2(radius * 2 + 2, radius * 2 + 2); SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw() => DrawCircle(Size / 2, _r, _color);
}

/// <summary>A bare pictogram as a control.</summary>
public partial class IconView : Control
{
    private readonly IconKind _kind; private readonly float _size; private readonly Color _color;
    public IconView(IconKind kind, float size, Color color)
    {
        _kind = kind; _size = size; _color = color;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore;
    }
    public override void _Draw() => Icons.Draw(this, _kind, Size / 2, _size, _color);
}

/// <summary>A pictogram on a filled disc (dialog headers, briefing pills): grey disc + dark icon, or chrome disc + white icon.</summary>
public partial class PictoDisc : Control
{
    private readonly IconKind _kind; private readonly Color _fill, _ink; private readonly float _d;
    public PictoDisc(IconKind kind, float diameter, Color? fill = null, Color? ink = null)
    {
        _kind = kind; _d = diameter; _fill = fill ?? AppTheme.ChromeTint; _ink = ink ?? AppTheme.Chrome;
        CustomMinimumSize = new Vector2(diameter, diameter);
        SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore;
    }
    public override void _Draw()
    {
        DrawCircle(Size / 2, _d / 2, _fill);
        Icons.Draw(this, _kind, Size / 2, _d * 0.54f, _ink);
    }
}

/// <summary>
/// A thin rounded bar. Plain: fill from the left (traffic, signature or party colour). Diverging: the fill grows from the centre, green to
/// the right and red to the left (effect rows). The fill eases in when the bar appears.
/// </summary>
public partial class Meter : Control
{
    private double _value, _min, _max;
    private Color _color;
    private readonly bool _diverging;
    private readonly float _h;
    private float _grow = 1;
    public Color TrackColor { get; set; } = AppTheme.TrackLight;
    /// <summary>Optional marker (0..1) drawn as an ink tick, e.g. the majority line.</summary>
    public float? Mark { get; set; }
    public bool Animate { get; set; } = true;
    public Color MarkColor { get; set; } = AppTheme.Ink;

    public Meter(double value, double min, double max, Color color, float width = 120, bool diverging = false, float height = 6)
    {
        _value = value; _min = min; _max = max; _color = color; _diverging = diverging; _h = Math.Min(height, 8);
        CustomMinimumSize = new Vector2(width, Math.Max(_h, 12));
        SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore;
        if (diverging) TrackColor = AppTheme.Track;
    }

    public override void _Ready()
    {
        if (Animate && Main.Animations && !DisplaySettings.ReduceMotion) { _grow = 0; SetProcess(true); }
        else SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _grow = Math.Min(1, _grow + (float)delta / 0.35f);
        QueueRedraw();
        if (_grow >= 1) SetProcess(false);
    }

    public void Set(double value, Color color) { _value = value; _color = color; QueueRedraw(); }

    public override void _Draw()
    {
        float y = (Size.Y - _h) / 2;
        int r = (int)Math.Ceiling(_h / 2);
        float ease = 1 - (1 - _grow) * (1 - _grow) * (1 - _grow);
        DrawStyleBox(AppTheme.Box(TrackColor, r), new Rect2(0, y, Size.X, _h));
        double span = Math.Max(_max - _min, 1e-12);
        float f = (float)Math.Clamp((_value - _min) / span, 0, 1);
        if (_diverging)
        {
            float mid = Size.X / 2, x = Size.X * f;
            float w = Math.Abs(x - mid) * ease;
            if (w >= 0.5f || Math.Abs(_value) > 1e-9)
            {
                w = Math.Max(w, _h);
                var rect = x >= mid ? new Rect2(mid, y, w, _h) : new Rect2(mid - w, y, w, _h);
                DrawStyleBox(AppTheme.Box(_color, r), rect);
            }
            DrawRect(new Rect2(mid - 0.5f, y - 3, 1, _h + 6), new Color(AppTheme.Ink, 0.35f));
        }
        else if (f > 0) DrawStyleBox(AppTheme.Box(_color, r), new Rect2(0, y, Math.Max(Size.X * f * ease, _h), _h));
        if (Mark is { } m) DrawRect(new Rect2(Size.X * m - 1, y - 4, 2, _h + 8), MarkColor);
    }
}

/// <summary>Lays children out in as many equal columns as fit (each at least <see cref="MinColumn"/> wide, at most <see cref="MaxColumns"/>). A child's "span" meta spans columns.</summary>
public partial class FlowGrid : Container
{
    public float MinColumn { get; set; } = 260;
    public int MaxColumns { get; set; } = 3;
    public int Gap { get; set; } = 12;
    public int Columns { get; private set; } = 1;

    public FlowGrid() { SizeFlagsHorizontal = SizeFlags.ExpandFill; }

    /// <summary>The narrowest a column may be: MinColumn, or what its widest single-column child needs (a card is never squeezed over its neighbour).</summary>
    private float ColumnMin()
    {
        float m = MinColumn;
        foreach (var n in GetChildren()) if (n is Control c && c.Visible && !c.HasMeta("span")) m = Math.Max(m, c.GetCombinedMinimumSize().X);
        return m;
    }

    private int Cols(float width) { float min = ColumnMin(); return Math.Clamp((int)((width + Gap) / (min + Gap)), 1, MaxColumns); }
    private static int Span(Control c, int cols) => Math.Clamp(c.HasMeta("span") ? (int)c.GetMeta("span") : 1, 1, cols);

    private List<List<Control>> Rows(int cols)
    {
        var rows = new List<List<Control>>(); var row = new List<Control>(); int used = 0;
        foreach (var n in GetChildren())
        {
            if (n is not Control c || !c.Visible) continue;
            int sp = Span(c, cols);
            if (used + sp > cols && row.Count > 0) { rows.Add(row); row = new(); used = 0; }
            row.Add(c); used += sp;
        }
        if (row.Count > 0) rows.Add(row);
        return rows;
    }

    public override Vector2 _GetMinimumSize()
    {
        int cols = Cols(Size.X > 0 ? Size.X : MinColumn);
        float h = 0; float minW = 0;
        foreach (var row in Rows(cols)) { h += row.Max(c => c.GetCombinedMinimumSize().Y) + Gap; }
        foreach (var n in GetChildren()) if (n is Control c && c.Visible) minW = Math.Max(minW, Math.Min(c.GetCombinedMinimumSize().X, MinColumn));
        return new Vector2(minW, Math.Max(0, h - Gap));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationSortChildren)
        {
            int cols = Cols(Size.X);
            if (cols != Columns) { Columns = cols; CallDeferred(Control.MethodName.UpdateMinimumSize); }
            float colW = (Size.X - Gap * (cols - 1)) / cols;
            float y = 0;
            foreach (var row in Rows(cols))
            {
                float h = row.Max(c => c.GetCombinedMinimumSize().Y);
                float x = 0;
                foreach (var c in row)
                {
                    int sp = Span(c, cols);
                    float w = colW * sp + Gap * (sp - 1);
                    FitChildInRect(c, new Rect2(x, y, w, h));
                    x += w + Gap;
                }
                y += h + Gap;
            }
        }
        else if (what == NotificationResized) UpdateMinimumSize();
    }
}
