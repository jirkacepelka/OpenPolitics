using Godot;

namespace OpenPolitics.Client;

/// <summary>
/// The look of the whole client, built in code so there are no asset files to keep in step: a dark ink palette with lighter
/// floating surfaces, one accent for things you can press, category colours for policies, and a Fira Sans type scale.
/// </summary>
public static class AppTheme
{
    // Surfaces, from the map up to raised cards.
    public static readonly Color Bg = new("0b111c");
    public static readonly Color MapGlow = new("15233f");
    public static readonly Color Header = new("0f1726");
    public static readonly Color Surface = new("121a2a");
    public static readonly Color Card = new("18223a");
    public static readonly Color CardHover = new("1f2c49");
    public static readonly Color Border = new("26334f");
    public static readonly Color BorderStrong = new("3a4e78");

    // Ink.
    public static readonly Color Text = new("eef2fa");
    public static readonly Color Text2 = new("b4c0d8");
    public static readonly Color Muted = new("8d9ab5");

    // Meaning.
    public static readonly Color Accent = new("4cc2ff");
    public static readonly Color AccentDim = new("1e6fa3");
    public static readonly Color AccentInk = new("04121f");
    public static readonly Color Good = new("3ddc97");
    public static readonly Color Bad = new("ff6b81");
    public static readonly Color Warn = new("ffc145");

    // Categories of the map.
    public static readonly Color Tax = new("f2b544"), Spend = new("2dd4bf"), Regulation = new("a78bfa"), ValueNode = new("6aa8ff");

    // Type scale.
    public const int Caption = 13, Body = 15, Subhead = 17, SectionSize = 20, Title = 26, Display = 32;

    private static readonly string[] Families = { "Fira Sans", "Inter", "Noto Sans", "Segoe UI", "Helvetica Neue", "DejaVu Sans", "sans-serif" };

    private static SystemFont Face(int weight) => new()
    {
        FontNames = Families, FontWeight = weight,
        Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.Light, SubpixelPositioning = TextServer.SubpixelPositioning.Auto,
        MultichannelSignedDistanceField = false,
    };

    private static SystemFont? _regular, _medium, _semi, _bold;
    public static SystemFont Regular => _regular ??= Face(400);
    public static SystemFont Medium => _medium ??= Face(500);
    public static SystemFont Semi => _semi ??= Face(600);
    /// <summary>The bold face used for titles and big numbers.</summary>
    public static SystemFont Bold => _bold ??= Face(700);

    /// <summary>A floating panel over the map: a lighter surface, a hairline and a soft deep shadow.</summary>
    public static StyleBoxFlat Glass(float alpha = 0.97f, int radius = 16)
    {
        var s = Box(new Color(Surface, alpha), radius, new Color(BorderStrong, 0.55f), 1, 0);
        s.ShadowColor = new Color(0, 0, 0, 0.5f); s.ShadowSize = 26; s.ShadowOffset = new Vector2(0, 8);
        return s;
    }

    public static StyleBoxFlat Box(Color fill, int radius = 8, Color? border = null, int borderWidth = 0, int pad = 0)
    {
        var s = new StyleBoxFlat { BgColor = fill, AntiAliasing = true, AntiAliasingSize = 0.8f };
        s.SetCornerRadiusAll(radius);
        if (border != null) { s.BorderColor = border.Value; s.SetBorderWidthAll(Math.Max(1, borderWidth)); }
        if (pad > 0) s.SetContentMarginAll(pad);
        return s;
    }

    public static StyleBoxFlat Pad(StyleBoxFlat s, float h, float v)
    {
        s.ContentMarginLeft = h; s.ContentMarginRight = h; s.ContentMarginTop = v; s.ContentMarginBottom = v;
        return s;
    }

    /// <summary>A tinted fill for a colour: the background of chips, selected tiles and category badges.</summary>
    public static Color Tint(Color c, float a = 0.16f) => new(c, a);

    // ---- generated textures (no asset files) -------------------------------------------------------------

    private static ImageTexture Paint(int w, int h, Func<float, float, Color> pixel)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) img.SetPixel(x, y, pixel(x + 0.5f, y + 0.5f));
        return ImageTexture.CreateFromImage(img);
    }

    private static float Cover(float d) => Math.Clamp(0.5f - d, 0f, 1f); // signed distance (px, negative inside) to coverage

    private static Color Over(Color under, Color over, float a)
    {
        a *= over.A;
        float outA = a + under.A * (1 - a);
        if (outA <= 0) return new Color(0, 0, 0, 0);
        return new Color((over.R * a + under.R * under.A * (1 - a)) / outA, (over.G * a + under.G * under.A * (1 - a)) / outA,
            (over.B * a + under.B * under.A * (1 - a)) / outA, outA);
    }

    /// <summary>A round slider grabber: a white disc with a coloured rim.</summary>
    public static ImageTexture Grabber(Color rim, int size = 22) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f, d = new Vector2(x - c, y - c).Length();
        var col = new Color(0, 0, 0, 0);
        col = Over(col, new Color(0, 0, 0, 0.35f), Cover(d - (c - 0.2f)));
        col = Over(col, rim, Cover(d - (c - 1.5f)));
        col = Over(col, Colors.White, Cover(d - (c - 5f)));
        return col;
    });

    /// <summary>A check box: a rounded square, filled with a tick when on.</summary>
    public static ImageTexture CheckIcon(bool on, bool disabled = false, int size = 22) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f, half = c - 2f, r = 5f;
        var q = new Vector2(Math.Abs(x - c), Math.Abs(y - c)) - new Vector2(half - r, half - r);
        float d = new Vector2(Math.Max(q.X, 0), Math.Max(q.Y, 0)).Length() + Math.Min(Math.Max(q.X, q.Y), 0) - r;
        var col = new Color(0, 0, 0, 0);
        float k = disabled ? 0.45f : 1f;
        if (on)
        {
            col = Over(col, new Color(Accent, k), Cover(d));
            // tick: two segments
            float s1 = SegDist(new Vector2(x, y), new Vector2(c - 5, c), new Vector2(c - 1.5f, c + 4));
            float s2 = SegDist(new Vector2(x, y), new Vector2(c - 1.5f, c + 4), new Vector2(c + 5.5f, c - 4.5f));
            col = Over(col, new Color(AccentInk, k), Cover(Math.Min(s1, s2) - 1.4f));
        }
        else
        {
            col = Over(col, new Color(Header, k), Cover(d));
            col = Over(col, new Color(BorderStrong, k), Cover(Math.Abs(d + 0.75f) - 0.75f));
        }
        return col;
    });

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var pa = p - a; var ba = b - a;
        float h = Math.Clamp(pa.Dot(ba) / ba.Dot(ba), 0, 1);
        return (pa - ba * h).Length();
    }

    /// <summary>A small downward chevron for drop-downs.</summary>
    public static ImageTexture Chevron(Color color, int size = 16) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f;
        float d = Math.Min(SegDist(new Vector2(x, y), new Vector2(c - 4, c - 2), new Vector2(c, c + 2)),
                           SegDist(new Vector2(x, y), new Vector2(c, c + 2), new Vector2(c + 4, c - 2)));
        return new Color(color, Cover(d - 1f));
    });

    /// <summary>A thin cross for close buttons.</summary>
    public static ImageTexture Cross(Color color, int size = 16) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f;
        float d = Math.Min(SegDist(new Vector2(x, y), new Vector2(c - 4.5f, c - 4.5f), new Vector2(c + 4.5f, c + 4.5f)),
                           SegDist(new Vector2(x, y), new Vector2(c - 4.5f, c + 4.5f), new Vector2(c + 4.5f, c - 4.5f)));
        return new Color(color, Cover(d - 0.9f));
    });

    private static ImageTexture? _coin;
    /// <summary>The political-capital coin shown next to the cost of an action.</summary>
    public static ImageTexture Coin => _coin ??= Paint(16, 16, (x, y) =>
    {
        float d = new Vector2(x - 8, y - 8).Length();
        var col = new Color(0, 0, 0, 0);
        col = Over(col, Warn, Cover(d - 7f));
        col = Over(col, new Color("3a2a05"), Cover(Math.Abs(d - 4.2f) - 0.8f));
        return col;
    });

    private static readonly ImageTexture Empty = Paint(1, 1, (_, _) => new Color(0, 0, 0, 0));

    // ---- the theme -------------------------------------------------------------------------------------------

    private static Theme? _instance;
    /// <summary>The one theme, shared by the main window and dialogs that live outside it (they do not inherit it).</summary>
    public static Theme Instance => _instance ??= Build();

    /// <summary>A small upward chevron (spin boxes).</summary>
    public static ImageTexture ChevronUp(Color color, int size = 16) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f;
        float d = Math.Min(SegDist(new Vector2(x, y), new Vector2(c - 4, c + 2), new Vector2(c, c - 2)),
                           SegDist(new Vector2(x, y), new Vector2(c, c - 2), new Vector2(c + 4, c + 2)));
        return new Color(color, Cover(d - 1f));
    });

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = Body, DefaultFont = Regular };

        foreach (var type in new[] { "Label", "CheckBox", "CheckButton", "RichTextLabel" }) t.SetColor("font_color", type, Text);
        t.SetColor("default_color", "RichTextLabel", Text);

        // Buttons: secondary by default (the primary look is a type variation).
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            t.SetStylebox("normal", type, Pad(Box(Card, 8, Border, 1), 14, 7));
            t.SetStylebox("hover", type, Pad(Box(CardHover, 8, new Color(Accent, 0.55f), 1), 14, 7));
            t.SetStylebox("pressed", type, Pad(Box(new Color(AccentDim, 0.55f), 8, Accent, 1), 14, 7));
            t.SetStylebox("hover_pressed", type, Pad(Box(new Color(AccentDim, 0.7f), 8, Accent, 1), 14, 7));
            t.SetStylebox("disabled", type, Pad(Box(new Color(Card, 0.4f), 8, new Color(Border, 0.5f), 1), 14, 7));
            t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), 8, new Color(Accent, 0.8f), 2));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_hover_pressed_color", type, Colors.White);
            t.SetColor("font_focus_color", type, Text);
            t.SetColor("font_disabled_color", type, new Color(Muted, 0.55f));
            t.SetFont("font", type, Medium);
        }
        t.SetIcon("arrow", "OptionButton", Chevron(Text2));
        t.SetConstant("arrow_margin", "OptionButton", 10);
        t.SetConstant("h_separation", "Button", 8);

        // Primary call to action.
        t.SetTypeVariation("Primary", "Button");
        t.SetStylebox("normal", "Primary", Pad(Box(Accent, 8), 18, 8));
        t.SetStylebox("hover", "Primary", Pad(Box(Accent.Lightened(0.18f), 8), 18, 8));
        t.SetStylebox("pressed", "Primary", Pad(Box(Accent.Darkened(0.15f), 8), 18, 8));
        t.SetStylebox("disabled", "Primary", Pad(Box(new Color(Accent, 0.25f), 8), 18, 8));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) t.SetColor(c, "Primary", AccentInk);
        t.SetColor("font_disabled_color", "Primary", new Color(AccentInk, 0.6f));
        t.SetFont("font", "Primary", Bold);

        // Quiet button: no chrome until hovered (segmented controls, chart ranges, icon buttons).
        t.SetTypeVariation("Ghost", "Button");
        t.SetStylebox("normal", "Ghost", new StyleBoxEmpty { ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 });
        t.SetStylebox("hover", "Ghost", Pad(Box(CardHover, 8), 12, 6));
        t.SetStylebox("pressed", "Ghost", Pad(Box(Tint(Accent, 0.22f), 8, new Color(Accent, 0.7f), 1), 12, 6));
        t.SetStylebox("hover_pressed", "Ghost", Pad(Box(Tint(Accent, 0.28f), 8, Accent, 1), 12, 6));
        t.SetStylebox("disabled", "Ghost", new StyleBoxEmpty { ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 });
        t.SetStylebox("focus", "Ghost", new StyleBoxEmpty());
        t.SetColor("font_color", "Ghost", Text2);
        t.SetColor("font_pressed_color", "Ghost", Colors.White);
        t.SetColor("font_hover_pressed_color", "Ghost", Colors.White);

        // Check boxes.
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            var none = Pad(Box(new Color(0, 0, 0, 0), 8), 6, 6);
            t.SetStylebox("normal", type, none); t.SetStylebox("pressed", type, none);
            t.SetStylebox("hover", type, Pad(Box(CardHover, 8), 6, 6)); t.SetStylebox("hover_pressed", type, Pad(Box(CardHover, 8), 6, 6));
            t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), 8));
            t.SetStylebox("disabled", type, none);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Text);
            t.SetColor("font_hover_pressed_color", type, Colors.White);
            t.SetConstant("h_separation", type, 10);
        }
        t.SetIcon("checked", "CheckBox", CheckIcon(true)); t.SetIcon("unchecked", "CheckBox", CheckIcon(false));
        t.SetIcon("checked_disabled", "CheckBox", CheckIcon(true, true)); t.SetIcon("unchecked_disabled", "CheckBox", CheckIcon(false, true));

        // Panels, popups, tooltips.
        t.SetStylebox("panel", "PanelContainer", Box(Card, 12, Border, 1, 16));
        t.SetStylebox("panel", "PopupPanel", Box(Surface, 10, BorderStrong, 1, 8));
        var menu = Box(Surface, 10, BorderStrong, 1, 6); menu.ShadowColor = new Color(0, 0, 0, 0.45f); menu.ShadowSize = 14;
        t.SetStylebox("panel", "PopupMenu", menu);
        t.SetStylebox("hover", "PopupMenu", Box(Tint(Accent, 0.2f), 6));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Colors.White);
        t.SetFont("font", "PopupMenu", Regular);
        t.SetFontSize("font_size", "PopupMenu", Body);
        t.SetConstant("v_separation", "PopupMenu", 8);
        t.SetIcon("radio_checked", "PopupMenu", CheckIcon(true, false, 16));
        t.SetIcon("radio_unchecked", "PopupMenu", Empty);
        var tip = Box(new Color("0a101b", 0.98f), 8, BorderStrong, 1, 10); tip.ShadowColor = new Color(0, 0, 0, 0.4f); tip.ShadowSize = 10;
        t.SetStylebox("panel", "TooltipPanel", tip);
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetFontSize("font_size", "TooltipLabel", 14);

        // Windows: we draw our own headers, so the embedded frame is only a shadowed edge.
        var win = Box(Surface, 16, new Color(BorderStrong, 0.6f), 1, 0);
        win.ShadowColor = new Color(0, 0, 0, 0.55f); win.ShadowSize = 32; win.ShadowOffset = new Vector2(0, 10);
        win.ExpandMarginLeft = 1; win.ExpandMarginRight = 1; win.ExpandMarginTop = 1; win.ExpandMarginBottom = 1;
        t.SetStylebox("embedded_border", "Window", win);
        t.SetStylebox("embedded_unfocused_border", "Window", win);
        t.SetColor("title_color", "Window", Text);
        t.SetFont("title_font", "Window", Semi);
        t.SetIcon("close", "Window", Cross(Text2)); t.SetIcon("close_pressed", "Window", Cross(Colors.White));
        t.SetStylebox("panel", "AcceptDialog", Box(Surface, 0, null, 0, 20));

        // Inputs.
        foreach (var type in new[] { "LineEdit", "SpinBox" })
        {
            t.SetStylebox("normal", type, Pad(Box(Header, 8, Border, 1), 12, 7));
            t.SetStylebox("focus", type, Pad(Box(Header, 8, Accent, 2), 12, 7));
            t.SetColor("font_color", type, Text);
        }

        foreach (var (name, color) in new[] { ("", Text2), ("_hover", Colors.White), ("_pressed", Accent), ("_disabled", Muted) })
        {
            t.SetIcon("up" + name, "SpinBox", ChevronUp(color));
            t.SetIcon("down" + name, "SpinBox", Chevron(color));
        }

        // Bars and sliders.
        t.SetStylebox("background", "ProgressBar", Box(new Color(Bg, 0.9f), 5));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, 5));
        var track = Box(new Color(Bg, 0.95f), 4, Border, 1); track.ContentMarginTop = 4; track.ContentMarginBottom = 4;
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", Box(Accent, 4));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Accent.Lightened(0.15f), 4));
        t.SetIcon("grabber", "HSlider", Grabber(Accent));
        t.SetIcon("grabber_highlight", "HSlider", Grabber(Accent.Lightened(0.3f)));
        t.SetIcon("grabber_disabled", "HSlider", Grabber(BorderStrong));
        t.SetIcon("tick", "HSlider", Empty);
        t.SetConstant("center_grabber", "HSlider", 0);

        t.SetConstant("separation", "VBoxContainer", 8);
        t.SetConstant("separation", "HBoxContainer", 8);

        // Separators and scrolling.
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Border, Thickness = 1 });
        t.SetConstant("separation", "HSeparator", 16);
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            var scroll = Box(new Color(0, 0, 0, 0), 4); scroll.ContentMarginLeft = 3; scroll.ContentMarginRight = 3;
            t.SetStylebox("scroll", bar, scroll);
            t.SetStylebox("grabber", bar, Box(new Color(BorderStrong, 0.8f), 4));
            t.SetStylebox("grabber_highlight", bar, Box(new Color(Accent, 0.6f), 4));
            t.SetStylebox("grabber_pressed", bar, Box(Accent, 4));
        }
        t.SetStylebox("split_bar_background", "HSplitContainer", Box(new Color(0, 0, 0, 0), 0));
        t.SetIcon("grabber", "HSplitContainer", Empty);
        return t;
    }
}
