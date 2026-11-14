using Godot;

namespace OpenPolitics.Client;

/// <summary>The look of the whole client: one dark palette, rounded cards, readable type. Built in code so there are no asset files to keep in step.</summary>
public static class AppTheme
{
    public static readonly Color Bg = new("070b14");
    public static readonly Color Header = new("0b1120");
    public static readonly Color Surface = new("101933");
    public static readonly Color Card = new("16213d");
    public static readonly Color CardHover = new("1f2e55");
    public static readonly Color Border = new("2b3d68");
    public static readonly Color Text = new("eaf0ff");
    public static readonly Color Muted = new("8a9bbd");
    public static readonly Color Accent = new("38bdf8");
    public static readonly Color AccentDim = new("155e86");
    public static readonly Color Good = new("34d399");
    public static readonly Color Bad = new("fb7185");
    public static readonly Color Warn = new("fbbf24");

    public static readonly Color Tax = new("fbbf24"), Spend = new("2dd4bf"), Regulation = new("a78bfa"), ValueNode = new("60a5fa");

    /// <summary>A frosted-glass panel floating over the map, with a soft shadow.</summary>
    public static StyleBoxFlat Glass(float alpha = 0.9f, int radius = 16)
    {
        var s = Box(new Color(Header.R, Header.G, Header.B, alpha), radius, new Color(Border, 0.9f), 1, 0);
        s.ShadowColor = new Color(0, 0, 0, 0.55f); s.ShadowSize = 22; s.ShadowOffset = new Vector2(0, 6);
        return s;
    }

    private static SystemFont? _bold;
    /// <summary>The bold face used for headings and big numbers.</summary>
    public static SystemFont Bold => _bold ??= new SystemFont
    {
        FontNames = new[] { "Inter", "Noto Sans", "Segoe UI", "Helvetica Neue", "DejaVu Sans", "sans-serif" }, FontWeight = 700,
        Antialiasing = TextServer.FontAntialiasing.Lcd, Hinting = TextServer.Hinting.Light, SubpixelPositioning = TextServer.SubpixelPositioning.Auto,
    };

    public static StyleBoxFlat Box(Color fill, int radius = 8, Color? border = null, int borderWidth = 0, int pad = 0)
    {
        var s = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        s.SetCornerRadiusAll(radius);
        if (border != null) { s.BorderColor = border.Value; s.SetBorderWidthAll(Math.Max(1, borderWidth)); }
        if (pad > 0) s.SetContentMarginAll(pad);
        return s;
    }

    private static StyleBoxFlat Pad(StyleBoxFlat s, float h, float v)
    {
        s.ContentMarginLeft = h; s.ContentMarginRight = h; s.ContentMarginTop = v; s.ContentMarginBottom = v;
        return s;
    }

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 16 };
        t.DefaultFont = new SystemFont
        {
            FontNames = new[] { "Inter", "Noto Sans", "Segoe UI", "Helvetica Neue", "DejaVu Sans", "sans-serif" },
            Antialiasing = TextServer.FontAntialiasing.Lcd, Hinting = TextServer.Hinting.Light, SubpixelPositioning = TextServer.SubpixelPositioning.Auto,
        };

        foreach (var type in new[] { "Label", "CheckBox", "RichTextLabel" }) t.SetColor("font_color", type, Text);

        // Buttons
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            t.SetStylebox("normal", type, Pad(Box(Card, 8, Border, 1), 14, 8));
            t.SetStylebox("hover", type, Pad(Box(CardHover, 8, AccentDim, 1), 14, 8));
            t.SetStylebox("pressed", type, Pad(Box(AccentDim, 8, Accent, 1), 14, 8));
            t.SetStylebox("disabled", type, Pad(Box(new Color(Card, 0.45f), 8, new Color(Border, 0.5f), 1), 14, 8));
            t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), 8, Accent, 2));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, new Color(Muted, 0.6f));
        }

        // Tabs
        t.SetStylebox("panel", "TabContainer", Box(Bg, 0));
        t.SetStylebox("tab_selected", "TabContainer", Pad(Box(Surface, 8, Border, 1), 16, 9));
        t.SetStylebox("tab_unselected", "TabContainer", Pad(Box(new Color(Header, 1f), 8), 16, 9));
        t.SetStylebox("tab_hovered", "TabContainer", Pad(Box(CardHover, 8), 16, 9));
        t.SetStylebox("tab_focus", "TabContainer", Box(new Color(0, 0, 0, 0), 8));
        t.SetColor("font_selected_color", "TabContainer", Colors.White);
        t.SetColor("font_unselected_color", "TabContainer", Muted);
        t.SetColor("font_hovered_color", "TabContainer", Text);
        t.SetStylebox("tabbar_background", "TabContainer", Box(Header, 0));

        // Panels, popups, dialogs
        t.SetStylebox("panel", "PanelContainer", Box(Card, 12, Border, 1, 14));
        t.SetStylebox("panel", "PopupPanel", Box(Surface, 10, Border, 1, 10));
        t.SetStylebox("panel", "PopupMenu", Box(Surface, 8, Border, 1, 6));
        t.SetStylebox("hover", "PopupMenu", Box(AccentDim, 6));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Colors.White);
        t.SetStylebox("panel", "TooltipPanel", Box(new Color("0b0f19"), 6, Border, 1, 8));
        t.SetColor("font_color", "TooltipLabel", Text);
        var win = Box(Surface, 14, Border, 1, 18);
        t.SetStylebox("embedded_border", "Window", win);
        t.SetStylebox("embedded_unfocused_border", "Window", win);
        t.SetColor("title_color", "Window", Text);
        t.SetStylebox("panel", "AcceptDialog", win);

        // Inputs
        foreach (var type in new[] { "LineEdit", "SpinBox" })
        {
            t.SetStylebox("normal", type, Pad(Box(Header, 8, Border, 1), 10, 7));
            t.SetStylebox("focus", type, Pad(Box(Header, 8, Accent, 2), 10, 7));
            t.SetColor("font_color", type, Text);
        }

        // Bars and sliders
        t.SetStylebox("background", "ProgressBar", Box(new Color("0b0f19"), 6));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, 6));
        var track = Box(new Color("0b0f19"), 4, Border, 1); track.ContentMarginTop = 4; track.ContentMarginBottom = 4;
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", Box(Accent, 4));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Accent.Lightened(0.2f), 4));
        t.SetConstant("separation", "VBoxContainer", 8);

        // Separators and scrolling
        var line = new StyleBoxLine { Color = Border, Thickness = 1 };
        t.SetStylebox("separator", "HSeparator", line);
        t.SetConstant("separation", "HSeparator", 12);
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(0, 0, 0, 0), 4));
        t.SetStylebox("grabber", "VScrollBar", Box(Border, 4));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(AccentDim, 4));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Accent, 4));
        return t;
    }
}
