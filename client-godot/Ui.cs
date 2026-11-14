using Godot;

namespace OpenPolitics.Client;

/// <summary>Small helpers that build the UI in code, so the scenes stay tiny.</summary>
public static class Ui
{
    public static readonly Color Good = AppTheme.Good;
    public static readonly Color Bad = AppTheme.Bad;
    public static readonly Color Warn = AppTheme.Warn;
    public static readonly Color Muted = AppTheme.Muted;
    public static readonly Color Accent = AppTheme.Accent;

    public static Label Text(string text, Color? color = null, int size = 0, bool wrap = false)
    {
        var l = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        if (size > 0) l.AddThemeFontSizeOverride("font_size", size);
        if (wrap) l.CustomMinimumSize = new Vector2(300, 0);
        return l;
    }

    public static Label Heading(string text)
    {
        var l = Text(text, Colors.White, 24);
        l.AddThemeFontOverride("font", AppTheme.Bold);
        return l;
    }

    /// <summary>A big number or key word in the bold face.</summary>
    public static Label Big(string text, Color? color = null, int size = 22)
    {
        var l = Text(text, color ?? Colors.White, size);
        l.AddThemeFontOverride("font", AppTheme.Bold);
        return l;
    }

    /// <summary>A smaller section title inside a page or card.</summary>
    public static Label Section(string text)
    {
        var l = Text(text.ToUpperInvariant(), Accent, 14);
        l.AddThemeFontOverride("font", AppTheme.Bold);
        return l;
    }

    /// <summary>A rounded card around some content. The card is a PanelContainer: the child fills it.</summary>
    public static PanelContainer Card(Control child, Color? fill = null, bool expand = true)
    {
        var card = new PanelContainer();
        if (fill != null) card.AddThemeStyleboxOverride("panel", AppTheme.Box(fill.Value, 12, AppTheme.Border, 1, 14));
        if (expand) card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        card.AddChild(child);
        return card;
    }

    /// <summary>A small coloured label such as a role or a status.</summary>
    public static Control Pill(string text, Color color)
    {
        var p = new PanelContainer();
        var box = AppTheme.Box(new Color(color, 0.18f), 10, new Color(color, 0.6f), 1);
        box.ContentMarginLeft = 10; box.ContentMarginRight = 10; box.ContentMarginTop = 2; box.ContentMarginBottom = 2;
        p.AddThemeStyleboxOverride("panel", box);
        p.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        p.AddChild(Text(text, color, 13));
        return p;
    }

    public static Button Btn(string text, Action onPress, string? tooltip = null, bool disabled = false)
    {
        var b = new Button { Text = text, Disabled = disabled, TooltipText = tooltip ?? "" };
        b.Pressed += onPress;
        return b;
    }

    public static VBoxContainer VBox(int separation = 6)
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

    /// <summary>A scrollable page: returns the scroll container and the VBox to fill.</summary>
    public static (ScrollContainer Scroll, VBoxContainer Body) Page()
    {
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var body = VBox(8);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(Margin(body));
        ((MarginContainer)body.GetParent()).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return (scroll, body);
    }

    /// <summary>A filled bar for a value between min and max (e.g. a poll share or loyalty).</summary>
    public static ProgressBar Bar(double value, double min, double max, Color? color = null, int width = 180)
    {
        var b = new ProgressBar { MinValue = min, MaxValue = max, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(width, 18) };
        if (color != null)
        {
            var fill = new StyleBoxFlat { BgColor = color.Value };
            b.AddThemeStyleboxOverride("fill", fill);
        }
        return b;
    }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    /// <summary>Puts content into a window on a solid, themed surface (a bare Window is transparent grey and does not lay out its child).</summary>
    public static void FillWindow(Window window, Control content, int margin = 16)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", AppTheme.Box(AppTheme.Surface, 0));
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(Margin(content, margin));
        window.AddChild(panel);
    }

    public static Control Spacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    public static HSeparator Rule() => new();

    /// <summary>Colour for a signed effect: good, bad or neutral.</summary>
    public static Color Signed(double v, double eps = 1e-9) => v > eps ? Good : v < -eps ? Bad : Muted;
}
