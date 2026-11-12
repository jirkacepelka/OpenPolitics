using Godot;

namespace OpenPolitics.Client;

/// <summary>Small helpers that build the UI in code, so the scenes stay tiny.</summary>
public static class Ui
{
    public static readonly Color Good = new(0.35f, 0.75f, 0.45f);
    public static readonly Color Bad = new(0.9f, 0.4f, 0.35f);
    public static readonly Color Warn = new(0.95f, 0.75f, 0.3f);
    public static readonly Color Muted = new(0.65f, 0.67f, 0.72f);
    public static readonly Color Accent = new(0.45f, 0.65f, 0.95f);

    public static Label Text(string text, Color? color = null, int size = 0, bool wrap = false)
    {
        var l = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        if (size > 0) l.AddThemeFontSizeOverride("font_size", size);
        if (wrap) l.CustomMinimumSize = new Vector2(300, 0);
        return l;
    }

    public static Label Heading(string text) => Text(text, Accent, 20);

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

    public static Control Spacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    public static HSeparator Rule() => new();
}
