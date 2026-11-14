using Godot;

namespace OpenPolitics.Client;

/// <summary>Small helpers that build the UI in code, so the scenes stay tiny. They carry the type scale and spacing of <see cref="AppTheme"/>.</summary>
public static class Ui
{
    public static readonly Color Good = AppTheme.Good;
    public static readonly Color Bad = AppTheme.Bad;
    public static readonly Color Warn = AppTheme.Warn;
    public static readonly Color Muted = AppTheme.Muted;
    public static readonly Color Accent = AppTheme.Accent;

    public static Label Text(string text, Color? color = null, int size = 0, bool wrap = false, Font? font = null)
    {
        var l = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        if (size > 0) l.AddThemeFontSizeOverride("font_size", size);
        if (font != null) l.AddThemeFontOverride("font", font);
        if (wrap) { l.CustomMinimumSize = new Vector2(120, 0); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        return l;
    }

    /// <summary>A page title (kept for screens that still need one inside their panel).</summary>
    public static Label Heading(string text) => Text(text, Colors.White, AppTheme.SectionSize, false, AppTheme.Bold);

    /// <summary>A big number or key word in the bold face.</summary>
    public static Label Big(string text, Color? color = null, int size = 22) => Text(text, color ?? Colors.White, size, false, AppTheme.Bold);

    /// <summary>A section title: sentence case, semibold, white; an optional muted line under it.</summary>
    public static Control Section(string text, string? subtitle = null)
    {
        var box = VBox(2);
        var spacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
        box.AddChild(spacer);
        box.AddChild(Text(text, Colors.White, AppTheme.Subhead, false, AppTheme.Semi));
        if (!string.IsNullOrEmpty(subtitle)) box.AddChild(Text(subtitle, Muted, AppTheme.Caption, true));
        return box;
    }

    /// <summary>A small muted caption above a value.</summary>
    public static Label Caption(string text, Color? color = null) => Text(text, color ?? Muted, AppTheme.Caption, false, AppTheme.Medium);

    /// <summary>A rounded card around some content. The card is a PanelContainer: the child fills it.</summary>
    public static PanelContainer Card(Control child, Color? fill = null, bool expand = true, int pad = 16, Color? border = null)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", AppTheme.Box(fill ?? AppTheme.Card, 12, border ?? AppTheme.Border, 1, pad));
        if (expand) card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        card.AddChild(child);
        return card;
    }

    /// <summary>A card with a coloured stripe on the left (a callout: warning, decision, notice).</summary>
    public static PanelContainer Callout(Control child, Color color)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var box = AppTheme.Box(AppTheme.Tint(color, 0.10f), 10, new Color(color, 0.35f), 1, 14);
        box.BorderWidthLeft = 4; box.ContentMarginLeft = 18;
        card.AddThemeStyleboxOverride("panel", box);
        card.AddChild(child);
        return card;
    }

    /// <summary>A small coloured label such as a role or a status.</summary>
    public static Control Pill(string text, Color color)
    {
        var p = new PanelContainer();
        var box = AppTheme.Box(AppTheme.Tint(color, 0.16f), 11, new Color(color, 0.45f), 1);
        box.ContentMarginLeft = 10; box.ContentMarginRight = 10; box.ContentMarginTop = 2; box.ContentMarginBottom = 3;
        p.AddThemeStyleboxOverride("panel", box);
        p.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        p.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        p.AddChild(Text(text, color, AppTheme.Caption, false, AppTheme.Semi));
        return p;
    }

    public static Button Btn(string text, Action onPress, string? tooltip = null, bool disabled = false)
    {
        var b = new Button { Text = text, Disabled = disabled, TooltipText = tooltip ?? "", CustomMinimumSize = new Vector2(0, 36), FocusMode = Control.FocusModeEnum.None };
        b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        b.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        b.Pressed += onPress;
        return b;
    }

    public static Button Primary(string text, Action onPress, string? tooltip = null, bool disabled = false)
    {
        var b = Btn(text, onPress, tooltip, disabled);
        b.ThemeTypeVariation = "Primary";
        return b;
    }

    /// <summary>An action button with its political-capital cost and a coin.</summary>
    public static Button Costed(string text, int cost, System.Action onPress, string? tooltip = null, bool disabled = false)
    {
        var b = Btn(cost > 0 ? $"{text}   {cost}" : text, onPress, tooltip, disabled);
        if (cost > 0) { b.Icon = AppTheme.Coin; b.IconAlignment = HorizontalAlignment.Right; }
        return b;
    }

    public static VBoxContainer VBox(int separation = 8)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    public static HBoxContainer HBox(int separation = 8)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", separation);
        return h;
    }

    public static MarginContainer Margin(Control child, int px = 12)
    {
        var m = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) m.AddThemeConstantOverride(side, px);
        m.AddChild(child);
        return m;
    }

    public static MarginContainer Margin(Control child, int h, int v)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", h); m.AddThemeConstantOverride("margin_right", h);
        m.AddThemeConstantOverride("margin_top", v); m.AddThemeConstantOverride("margin_bottom", v);
        m.AddChild(child);
        return m;
    }

    /// <summary>A scrollable page: returns the scroll container and the VBox to fill.</summary>
    public static (ScrollContainer Scroll, VBoxContainer Body) Page()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var body = VBox(12);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 24); margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 4); margin.AddThemeConstantOverride("margin_bottom", 24);
        margin.AddChild(body);
        scroll.AddChild(margin);
        return (scroll, body);
    }

    /// <summary>A filled rounded bar for a value between min and max (e.g. a poll share or loyalty).</summary>
    public static Control Bar(double value, double min, double max, Color? color = null, int width = 180) =>
        new Meter(value, min, max, color ?? Accent, width);

    /// <summary>A bar for a -1..1 mood that fills from the middle.</summary>
    public static Control MoodBar(double value, int width = 180) =>
        new Meter(value, -1, 1, MoodColor(value), width, diverging: true);

    public static Color MoodColor(double v) => v < -0.2 ? Bad : v > 0.2 ? Good : Warn;

    /// <summary>A key figure: caption, big value, optional coloured sub-line, in a card.</summary>
    public static PanelContainer Stat(string caption, string value, Color? valueColor = null, string? sub = null, Color? subColor = null, IconKind? icon = null)
    {
        var v = VBox(2);
        var head = HBox(6);
        if (icon != null) head.AddChild(new IconView(icon.Value, 16, Muted));
        head.AddChild(Caption(caption));
        v.AddChild(head);
        v.AddChild(Big(value, valueColor ?? Colors.White, AppTheme.Title));
        if (sub != null) v.AddChild(Text(sub, subColor ?? Muted, AppTheme.Caption, true));
        var card = Card(v, AppTheme.Card, true, 14);
        card.SizeFlagsVertical = Control.SizeFlags.Fill;
        return card;
    }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    /// <summary>Puts content into a window on a solid, themed surface (a bare Window is transparent grey and does not lay out its child).</summary>
    public static void FillWindow(Window window, Control content, int margin = 20)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", AppTheme.Box(AppTheme.Surface, 16, new Color(AppTheme.BorderStrong, 0.6f), 1));
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(Margin(content, margin));
        window.AddChild(panel);
    }

    /// <summary>The header of a dialog or panel: pictogram in a tinted square, title, optional subtitle, close button.</summary>
    public static HBoxContainer DialogHeader(IconKind icon, string title, string? subtitle, Action onClose, string closeTip, Color? tint = null)
    {
        var row = HBox(12);
        row.AddChild(new IconBadge(icon, tint ?? Accent, 40));
        var titles = VBox(0);
        titles.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titles.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titles.AddChild(Text(title, Colors.White, 22, false, AppTheme.Bold));
        if (!string.IsNullOrEmpty(subtitle)) titles.AddChild(Text(subtitle, Muted, AppTheme.Caption));
        row.AddChild(titles);
        var close = new IconButton(IconKind.Close, "", closeTip, new Vector2(36, 36));
        close.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        close.Pressed += onClose;
        row.AddChild(close);
        return row;
    }

    public static Control Spacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    public static Control Gap(int px) => new Control { CustomMinimumSize = new Vector2(px, px), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static HSeparator Rule() => new();

    /// <summary>The colour of a party, the same in charts, the compass and the parliament.</summary>
    public static Color PartyColor(Session s, string id)
    {
        var ids = s.Packs.Parties.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return Charts.PaletteColor(ids.IndexOf(id));
    }

    /// <summary>A party name with its colour dot.</summary>
    public static Control PartyName(Session s, string id, string? text = null, Color? color = null, int size = 15)
    {
        var row = HBox(8);
        var dot = new ColorDot(PartyColor(s, id));
        row.AddChild(dot);
        row.AddChild(Text(text ?? s.T.Name(id), color ?? AppTheme.Text, size));
        return row;
    }

    /// <summary>Colour for a signed effect: good, bad or neutral.</summary>
    public static Color Signed(double v, double eps = 1e-9) => v > eps ? Good : v < -eps ? Bad : Muted;
}

/// <summary>A small coloured disc (party colour, legend).</summary>
public partial class ColorDot : Control
{
    private readonly Color _color;
    public ColorDot(Color color) { _color = color; CustomMinimumSize = new Vector2(12, 12); SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw() => DrawCircle(Size / 2, 5.5f, _color);
}

/// <summary>A bare pictogram as a control.</summary>
public partial class IconView : Control
{
    private readonly IconKind _kind; private readonly float _size; private readonly Color _color;
    public IconView(IconKind kind, float size, Color color)
    {
        _kind = kind; _size = size; _color = color;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }
    public override void _Draw() => Icons.Draw(this, _kind, Size / 2, _size, _color);
}

/// <summary>A pictogram in a rounded square tinted with a colour (category badges, panel headers).</summary>
public partial class IconBadge : Control
{
    private readonly IconKind _kind; private readonly Color _color; private readonly float _size;
    public bool Circle { get; set; }
    public IconBadge(IconKind kind, Color color, float size = 36)
    {
        _kind = kind; _color = color; _size = size;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }
    public override void _Draw()
    {
        DrawStyleBox(AppTheme.Box(AppTheme.Tint(_color, 0.18f), Circle ? (int)(_size / 2) : (int)(_size * 0.28f), new Color(_color, 0.35f), 1), new Rect2(Vector2.Zero, Size));
        Icons.Draw(this, _kind, Size / 2, _size * 0.55f, _color);
    }
}
