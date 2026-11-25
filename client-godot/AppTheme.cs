using Godot;

namespace OpenPolitics.Client;

/// <summary>Text roles of the type scale (logical px at the 1280 x 720 reference).</summary>
public enum Tx { Display, Title, Heading, Body, BodyStrong, Label, Small, Overline, Figure, FigureXl, Data, Micro }

/// <summary>
/// The look of the client, built in code (docs/design/addendum-democracy4.md): white and light-grey surfaces, one saturated flat
/// <see cref="Chrome"/> colour for everything you can press, green for End quarter, traffic lights for good/bad, square corners,
/// no shadows. Fonts: bundled Inter when present, otherwise a system sans.
/// </summary>
public static class AppTheme
{
    // ---- colour tokens -------------------------------------------------------------------------------------
    /// <summary>The signature colour (violet ink): primary action, selection, policy rings, the slider. Swap it here.</summary>
    public static readonly Color Chrome = new("5b45e0");
    public static readonly Color ChromeDark = new("4632bf");
    public static readonly Color ChromeLight = new("a497f4");
    public static readonly Color ChromeTint = new("eeebfd");
    /// <summary>Graphite: the voter panel, the hub, tooltips, the active tab icon.</summary>
    public static readonly Color Graphite = new("1d1c23");
    public static readonly Color Graphite2 = new("2b2a33");
    public static readonly Color Graphite3 = new("3a3944");
    public static readonly Color ActiveTab = new("eeebfd");
    public static readonly Color Play = new("5b45e0");
    public static readonly Color PlayDark = new("4632bf");
    public static readonly Color MapBg = new("edeae4");
    public static readonly Color Surface = new("fbfaf8");
    public static readonly Color Panel = new("f2f0ec");
    public static readonly Color Panel2 = new("e8e5df");
    public static readonly Color Track = new("e2ded7");
    public static readonly Color TrackLight = new("e6e2db");
    public static readonly Color Border = new("d8d3ca");
    public static readonly Color Hairline = new("e6e2db");
    public static readonly Color Ink = new("17161c");
    public static readonly Color Ink2 = new("4b4952");
    public static readonly Color Ink3 = new("75727b");
    public static readonly Color InkDisabled = new("aeaab2");
    public static readonly Color Good = new("2f9e66");
    public static readonly Color Warn = new("e0892c");
    public static readonly Color Bad = new("dc4b3e");
    public static readonly Color GoodText = new("23784c");
    public static readonly Color WarnText = new("a35c12");
    public static readonly Color BadText = new("b73a2f");
    public static readonly Color Veil = new(0.09f, 0.086f, 0.11f, 0.34f);
    public const int Radius = 10, RadiusLg = 16;

    /// <summary>Traffic-light colour for a goodness in 0..1.</summary>
    public static Color Traffic(double goodness) => goodness >= 0.6 ? Good : goodness >= 0.4 ? Warn : Bad;
    public static Color TrafficText(double goodness) => goodness >= 0.6 ? GoodText : goodness >= 0.4 ? WarnText : BadText;
    /// <summary>Text colour for a signed effect (positive = good).</summary>
    public static Color Signed(double v, double eps = 1e-9) => v > eps ? GoodText : v < -eps ? BadText : Ink3;

    // ---- fonts ---------------------------------------------------------------------------------------------
    public const string FontDir = "res://assets/fonts/";
    private static readonly string[] Fallbacks = { "Inter", "Noto Sans", "Open Sans", "Fira Sans", "DejaVu Sans", "sans-serif" };
    private static FontFile? _variable;
    private static bool _probed;
    /// <summary>Which font is in use, for the README check and the display card.</summary>
    public static string FontSource { get; private set; } = "system";

    private static FontFile? LoadFile(string name)
    {
        // Imported resource first (editor and exported builds), the raw file second (a fresh checkout before the first import).
        if (ResourceLoader.Exists(FontDir + name) && ResourceLoader.Load(FontDir + name) is FontFile imported)
        {
            imported.Antialiasing = TextServer.FontAntialiasing.Gray; imported.Hinting = TextServer.Hinting.Light;
            imported.SubpixelPositioning = TextServer.SubpixelPositioning.Auto; imported.MultichannelSignedDistanceField = false;
            return imported;
        }
        string path = ProjectSettings.GlobalizePath(FontDir + name);
        if (!System.IO.File.Exists(path)) return null;
        var f = new FontFile { Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.Light, SubpixelPositioning = TextServer.SubpixelPositioning.Auto };
        return f.LoadDynamicFont(path) == Error.Ok ? f : null;
    }

    private static Font Base(int weight)
    {
        if (!_probed)
        {
            _probed = true;
            _variable = LoadFile("InterVariable.ttf");
            if (_variable != null) FontSource = "InterVariable.ttf";
        }
        if (_variable != null) return _variable;
        string? stat = weight >= 700 ? "Inter-Bold.ttf" : weight >= 600 ? "Inter-SemiBold.ttf" : weight >= 500 ? "Inter-Medium.ttf" : "Inter-Regular.ttf";
        if (LoadFile(stat) is { } file) { FontSource = "Inter static"; return file; }
        return new SystemFont
        {
            FontNames = Fallbacks, FontWeight = weight,
            Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.Light, SubpixelPositioning = TextServer.SubpixelPositioning.Auto,
        };
    }

    private static long Tag(string name) => TextServerManager.GetPrimaryInterface().NameToTag(name);

    private static FontVariation Make(int weight, bool figures, int spacing = 0)
    {
        var baseFont = Base(weight);
        var v = new FontVariation { BaseFont = baseFont, SpacingGlyph = spacing };
        if (baseFont == _variable && _variable != null) v.VariationOpentype = new Godot.Collections.Dictionary { { Tag("wght"), weight } };
        var features = new Godot.Collections.Dictionary { { Tag("cv05"), 1 }, { Tag("cv08"), 1 } };
        if (figures) features[Tag("tnum")] = 1;
        v.OpentypeFeatures = features;
        return v;
    }

    private static readonly Dictionary<(int, bool, int), FontVariation> Cache = new();
    public static Font Face(int weight, bool figures = false, int spacing = 0)
    {
        if (!Cache.TryGetValue((weight, figures, spacing), out var f)) Cache[(weight, figures, spacing)] = f = Make(weight, figures, spacing);
        return f;
    }

    public static Font Regular => Face(400);
    public static Font Semi => Face(600);
    public static Font Bold => Face(700);
    public static Font FigBold => Face(700, true);
    public static Font FigSemi => Face(600, true);

    public static Font FontOf(Tx r) => r switch
    {
        Tx.Display or Tx.Title or Tx.Heading => Bold,
        Tx.Body or Tx.Small => Regular,
        Tx.BodyStrong or Tx.Label => Semi,
        Tx.Overline => Face(700, false, 1),
        Tx.Figure or Tx.FigureXl => FigBold,
        Tx.Data or Tx.Micro => FigSemi,
        _ => Regular,
    };

    public static int SizeOf(Tx r) => r switch
    {
        Tx.Display => 30, Tx.Title => 22, Tx.Heading => 16, Tx.Body => 14, Tx.BodyStrong => 14, Tx.Label => 13,
        Tx.Small => 12, Tx.Overline => 11, Tx.Figure => 18, Tx.FigureXl => 28, Tx.Data => 13, Tx.Micro => 11, _ => 14,
    };

    // ---- style boxes ---------------------------------------------------------------------------------------
    public static StyleBoxFlat Box(Color fill, int radius = 0, Color? border = null, int borderWidth = 1, float padH = 0, float padV = -1)
    {
        var s = new StyleBoxFlat { BgColor = fill, AntiAliasing = radius > 0, AntiAliasingSize = 0.7f };
        s.SetCornerRadiusAll(radius);
        if (border != null) { s.BorderColor = border.Value; s.SetBorderWidthAll(borderWidth); }
        if (padV < 0) padV = padH;
        s.ContentMarginLeft = s.ContentMarginRight = padH; s.ContentMarginTop = s.ContentMarginBottom = padV;
        return s;
    }

    private static StyleBoxEmpty Empty(float h = 0, float v = 0) => new() { ContentMarginLeft = h, ContentMarginRight = h, ContentMarginTop = v, ContentMarginBottom = v };

    // ---- generated textures ---------------------------------------------------------------------------------
    private static ImageTexture Paint(int w, int h, Func<float, float, Color> pixel)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) img.SetPixel(x, y, pixel(x + 0.5f, y + 0.5f));
        return ImageTexture.CreateFromImage(img);
    }
    private static float Cover(float d) => Math.Clamp(0.5f - d, 0f, 1f);
    private static float Seg(Vector2 p, Vector2 a, Vector2 b)
    {
        var pa = p - a; var ba = b - a;
        float h = Math.Clamp(pa.Dot(ba) / ba.Dot(ba), 0, 1);
        return (pa - ba * h).Length();
    }
    private static Color Over(Color under, Color over, float a)
    {
        a *= over.A; float o = a + under.A * (1 - a);
        if (o <= 0) return new Color(0, 0, 0, 0);
        return new Color((over.R * a + under.R * under.A * (1 - a)) / o, (over.G * a + under.G * under.A * (1 - a)) / o, (over.B * a + under.B * under.A * (1 - a)) / o, o);
    }

    /// <summary>A square check box, chrome when on.</summary>
    public static ImageTexture Check(bool on, bool disabled = false, int size = 18) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f, half = c - 1f;
        var q = new Vector2(Math.Abs(x - c), Math.Abs(y - c)) - new Vector2(half - 4, half - 4);
        float d = new Vector2(Math.Max(q.X, 0), Math.Max(q.Y, 0)).Length() + Math.Min(Math.Max(q.X, q.Y), 0) - 4;
        var col = new Color(0, 0, 0, 0);
        float k = disabled ? 0.4f : 1f;
        if (on)
        {
            col = Over(col, new Color(Chrome, k), Cover(d));
            float s = Math.Min(Seg(new(x, y), new(c - 4.5f, c), new(c - 1.2f, c + 3.5f)), Seg(new(x, y), new(c - 1.2f, c + 3.5f), new(c + 5f, c - 4f)));
            col = Over(col, Colors.White, Cover(s - 1.2f));
        }
        else
        {
            col = Over(col, Colors.White, Cover(d));
            col = Over(col, new Color(Ink3, k), Cover(Math.Abs(d + 0.75f) - 0.75f));
        }
        return col;
    });

    public static ImageTexture Chevron(Color color, int size = 14) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f;
        float d = Math.Min(Seg(new(x, y), new(c - 4, c - 2), new(c, c + 2)), Seg(new(x, y), new(c, c + 2), new(c + 4, c - 2)));
        return new Color(color, Cover(d - 1f));
    });

    public static ImageTexture Disc(Color fill, Color rim, int size = 20) => Paint(size, size, (x, y) =>
    {
        float c = size / 2f, d = new Vector2(x - c, y - c).Length();
        var col = Over(new Color(0, 0, 0, 0), rim, Cover(d - (c - 0.5f)));
        return Over(col, fill, Cover(d - (c - 3.5f)));
    });

    private static readonly Dictionary<int, ImageTexture> Blanks = new();
    /// <summary>A transparent texture that reserves icon room in a Button (the pictogram is drawn on top).</summary>
    public static ImageTexture Blank(int size)
    {
        if (!Blanks.TryGetValue(size, out var t)) Blanks[size] = t = Paint(size, size, (_, _) => new Color(0, 0, 0, 0));
        return t;
    }

    private static ImageTexture? _dot;
    /// <summary>One faint dot of the map grid, tiled.</summary>
    public static ImageTexture GridDot => _dot ??= Paint(24, 24, (x, y) => x < 1.5f && y < 1.5f ? new Color("dcd8d0") : new Color(0, 0, 0, 0));

    // ---- the theme ------------------------------------------------------------------------------------------
    private static Theme? _instance;
    public static Theme Instance => _instance ??= Build();

    private static void ButtonStyle(Theme t, string type, Color fill, Color hover, Color pressed, Color text, Color? border = null, float padH = 14, float padV = 8, Color? disabledFill = null, int radius = Radius)
    {
        t.SetStylebox("normal", type, Box(fill, radius, border, 1, padH, padV));
        t.SetStylebox("hover", type, Box(hover, radius, border, 1, padH, padV));
        t.SetStylebox("pressed", type, Box(pressed, radius, border, 1, padH, padV));
        t.SetStylebox("hover_pressed", type, Box(pressed, radius, border, 1, padH, padV));
        t.SetStylebox("disabled", type, Box(disabledFill ?? Panel, radius, border == null ? null : Hairline, 1, padH, padV));
        var focus = Box(new Color(0, 0, 0, 0), radius + 2, Chrome, 2); focus.SetExpandMarginAll(2);
        t.SetStylebox("focus", type, focus);
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) t.SetColor(c, type, text);
        t.SetColor("font_disabled_color", type, InkDisabled);
        t.SetColor("icon_normal_color", type, text);
        t.SetFont("font", type, Semi);
        t.SetFontSize("font_size", type, SizeOf(Tx.Label));
        t.SetConstant("h_separation", type, 8);
    }

    private static void Variation(Theme t, string name, string baseType) => t.SetTypeVariation(name, baseType);

    public static Theme Build()
    {
        var t = new Theme { DefaultFont = Regular, DefaultFontSize = SizeOf(Tx.Body) };

        foreach (var type in new[] { "Label", "CheckBox", "CheckButton", "RichTextLabel", "LinkButton" }) t.SetColor("font_color", type, Ink);
        t.SetColor("default_color", "RichTextLabel", Ink);
        t.SetConstant("line_spacing", "Label", 2);

        // Label roles as type variations.
        foreach (Tx r in Enum.GetValues<Tx>())
        {
            string name = "T" + r;
            Variation(t, name, "Label");
            t.SetFont("font", name, FontOf(r));
            t.SetFontSize("font_size", name, SizeOf(r));
            t.SetColor("font_color", name, r is Tx.Overline or Tx.Small or Tx.Micro ? Ink3 : r == Tx.Body ? Ink2 : Ink);
        }

        // Buttons. Default = chrome flat (D4); variations for the others.
        ButtonStyle(t, "Button", Chrome, ChromeDark, ChromeDark.Darkened(0.12f), Colors.White, null, 14, 8, Panel2);
        Variation(t, "ButtonSecondary", "Button");
        ButtonStyle(t, "ButtonSecondary", Surface, Panel, Panel2, Ink, Border, 14, 8, Surface);
        Variation(t, "ButtonGhost", "Button");
        ButtonStyle(t, "ButtonGhost", new Color(0, 0, 0, 0), new Color(Ink, 0.05f), new Color(Ink, 0.09f), Chrome, null, 8, 6, new Color(0, 0, 0, 0));
        Variation(t, "ButtonPlay", "Button");
        ButtonStyle(t, "ButtonPlay", Play, PlayDark, PlayDark.Darkened(0.1f), Colors.White, null, 16, 8, Panel2, 12);
        Variation(t, "ButtonDanger", "Button");
        ButtonStyle(t, "ButtonDanger", Surface, new Color("fbebe8"), new Color("f6d8d3"), BadText, new Color(Bad, 0.45f), 14, 8, Surface);
        // Segment / toggle buttons (sort strip, ranges, tabs inside dialogs): flat grey, chrome when pressed.
        Variation(t, "ButtonSeg", "Button");
        ButtonStyle(t, "ButtonSeg", Panel2, new Color("dedad3"), Surface, Ink2, null, 12, 6, Panel2, 8);
        var segOn = Box(Surface, 8, new Color(Ink, 0.06f), 1, 12, 6); segOn.ShadowColor = new Color(Ink, 0.10f); segOn.ShadowSize = 2; segOn.ShadowOffset = new Vector2(0, 1);
        t.SetStylebox("pressed", "ButtonSeg", segOn); t.SetStylebox("hover_pressed", "ButtonSeg", segOn);
        t.SetColor("font_pressed_color", "ButtonSeg", Ink);
        t.SetColor("font_hover_pressed_color", "ButtonSeg", Ink);
        // Selectable cards (scenario choice): white with a grey border, chrome border and tint when chosen.
        Variation(t, "ButtonCard", "Button");
        ButtonStyle(t, "ButtonCard", Surface, new Color("ffffff"), ChromeTint, Ink, Border, 14, 12, Surface, 12);
        t.SetStylebox("pressed", "ButtonCard", Box(ChromeTint, 12, Chrome, 2, 14, 12));
        t.SetStylebox("hover_pressed", "ButtonCard", Box(ChromeTint, 12, Chrome, 2, 14, 12));
        // Main-menu entries: big flat rows, a soft tint on hover.
        Variation(t, "ButtonMenu", "Button");
        ButtonStyle(t, "ButtonMenu", new Color(0, 0, 0, 0), ChromeTint, ChromeTint.Darkened(0.04f), Ink, null, 18, 12, new Color(0, 0, 0, 0), 10);
        t.SetFontSize("font_size", "ButtonMenu", 18); t.SetConstant("h_separation", "ButtonMenu", 14);
        // A table row you can choose (party list): flat, a hairline between rows, chrome tint when chosen.
        Variation(t, "ButtonRow", "Button");
        ButtonStyle(t, "ButtonRow", new Color(0, 0, 0, 0), Panel, ChromeTint, Ink, null, 12, 6, new Color(0, 0, 0, 0), 10);
        t.SetStylebox("pressed", "ButtonRow", Box(ChromeTint, 10, Chrome, 2, 12, 6));
        t.SetStylebox("hover_pressed", "ButtonRow", Box(ChromeTint, 10, Chrome, 2, 12, 6));
        // Tabs in a dialog: text with a chrome underline when pressed.
        Variation(t, "ButtonTab", "Button");
        ButtonStyle(t, "ButtonTab", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), Ink3, null, 2, 10, new Color(0, 0, 0, 0), 0);
        var under = Box(new Color(0, 0, 0, 0), 0, null, 1, 2, 10); under.BorderColor = Chrome; under.BorderWidthBottom = 2;
        t.SetStylebox("pressed", "ButtonTab", under); t.SetStylebox("hover_pressed", "ButtonTab", under);
        t.SetColor("font_pressed_color", "ButtonTab", Ink); t.SetColor("font_hover_pressed_color", "ButtonTab", Ink);
        t.SetFont("font", "ButtonTab", Semi); t.SetFontSize("font_size", "ButtonTab", 14);
        t.SetColor("font_color", "ButtonTab", Ink3); t.SetColor("font_hover_color", "ButtonTab", Ink);

        // Drop-downs.
        foreach (var type in new[] { "OptionButton", "MenuButton" })
        {
            ButtonStyle(t, type, Surface, Panel, Panel, Ink, Border, 12, 6, Surface, 8);
        }
        t.SetIcon("arrow", "OptionButton", Chevron(Ink2));
        t.SetConstant("arrow_margin", "OptionButton", 8);

        // Popups and tooltips.
        var pop = Box(Surface, 12, new Color(Ink, 0.08f), 1, 6); pop.ShadowColor = new Color(Ink, 0.14f); pop.ShadowSize = 16; pop.ShadowOffset = new Vector2(0, 6);
        t.SetStylebox("panel", "PopupMenu", pop);
        t.SetStylebox("hover", "PopupMenu", Box(ChromeTint, 8));
        t.SetStylebox("separator", "PopupMenu", new StyleBoxLine { Color = Hairline, Thickness = 1 });
        t.SetColor("font_color", "PopupMenu", Ink); t.SetColor("font_hover_color", "PopupMenu", Ink);
        t.SetColor("font_disabled_color", "PopupMenu", InkDisabled);
        t.SetFont("font", "PopupMenu", Semi); t.SetFontSize("font_size", "PopupMenu", 13);
        t.SetConstant("v_separation", "PopupMenu", 10); t.SetConstant("h_separation", "PopupMenu", 8);
        t.SetConstant("item_start_padding", "PopupMenu", 10); t.SetConstant("item_end_padding", "PopupMenu", 10);
        t.SetIcon("radio_checked", "PopupMenu", Disc(Chrome, Chrome, 12)); t.SetIcon("radio_unchecked", "PopupMenu", Blank(12));
        t.SetIcon("checked", "PopupMenu", Check(true, false, 14)); t.SetIcon("unchecked", "PopupMenu", Check(false, false, 14));
        t.SetStylebox("panel", "PopupPanel", pop);
        var tip = Box(Graphite, 8, null, 1, 10, 7); tip.ShadowColor = new Color(Ink, 0.2f); tip.ShadowSize = 8; tip.ShadowOffset = new Vector2(0, 3);
        t.SetStylebox("panel", "TooltipPanel", tip);
        t.SetColor("font_color", "TooltipLabel", Colors.White);
        t.SetFont("font", "TooltipLabel", Regular); t.SetFontSize("font_size", "TooltipLabel", 13);

        // Panels: default plain; variations for regions and dialogs.
        t.SetStylebox("panel", "PanelContainer", Empty());
        void PanelVar(string name, StyleBox box) { Variation(t, name, "PanelContainer"); t.SetStylebox("panel", name, box); }
        var top = Box(Surface, 0); top.BorderColor = Hairline; top.BorderWidthBottom = 1;
        top.ShadowColor = new Color(Ink, 0.05f); top.ShadowSize = 6; top.ShadowOffset = new Vector2(0, 2);
        PanelVar("TopBar", top);
        PanelVar("Sidebar", Box(Graphite, 0));
        var dialog = Box(Surface, RadiusLg, new Color(Ink, 0.06f), 1); dialog.ShadowColor = new Color(Ink, 0.20f); dialog.ShadowSize = 36; dialog.ShadowOffset = new Vector2(0, 14);
        PanelVar("Dialog", dialog);
        PanelVar("Sheet", Box(Surface, 0));
        PanelVar("Inset", Box(Panel, 12, null, 1, 16, 14));
        PanelVar("Card", Box(Surface, 14, Hairline, 1, 16, 14));
        PanelVar("Pill", Box(Panel, 40, null, 1, 0, 0));
        var toast = Box(Graphite, 12, null, 1, 18, 12); toast.ShadowColor = new Color(Ink, 0.2f); toast.ShadowSize = 12; toast.ShadowOffset = new Vector2(0, 4);
        PanelVar("Toast", toast);
        PanelVar("Warn", Box(new Color("fbf0e2"), 10, null, 1, 14, 10));
        var lens = Box(Graphite, 12, null, 1, 12, 8); lens.ShadowColor = new Color(Ink, 0.16f); lens.ShadowSize = 10; lens.ShadowOffset = new Vector2(0, 3);
        PanelVar("ChromeBox", lens);

        // Inputs.
        foreach (var type in new[] { "LineEdit", "SpinBox" })
        {
            t.SetStylebox("normal", type, Box(new Color("ffffff"), 8, Border, 1, 10, 6));
            t.SetStylebox("focus", type, Box(new Color("ffffff"), 8, Chrome, 2, 10, 6));
            t.SetStylebox("read_only", type, Box(Panel, 8, Hairline, 1, 10, 6));
            t.SetColor("font_color", type, Ink); t.SetColor("font_placeholder_color", type, Ink3);
            t.SetColor("caret_color", type, Ink); t.SetColor("selection_color", type, ChromeTint);
            t.SetFont("font", type, FigSemi); t.SetFontSize("font_size", type, 13);
        }

        // Check boxes.
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            var none = Empty(2, 4);
            foreach (var st in new[] { "normal", "pressed", "hover", "hover_pressed", "disabled", "focus" }) t.SetStylebox(st, type, none);
            t.SetColor("font_hover_color", type, Ink); t.SetColor("font_pressed_color", type, Ink); t.SetColor("font_hover_pressed_color", type, Ink);
            t.SetColor("font_focus_color", type, Ink);
            t.SetConstant("h_separation", type, 8);
            t.SetFont("font", type, Regular); t.SetFontSize("font_size", type, 13);
        }
        t.SetIcon("checked", "CheckBox", Check(true)); t.SetIcon("unchecked", "CheckBox", Check(false));
        t.SetIcon("checked_disabled", "CheckBox", Check(true, true)); t.SetIcon("unchecked_disabled", "CheckBox", Check(false, true));
        t.SetIcon("radio_checked", "CheckBox", Disc(Chrome, Chrome, 18)); t.SetIcon("radio_unchecked", "CheckBox", Disc(Surface, Ink3, 18));

        // Sliders (display card, priority) and progress.
        var track = Box(TrackLight, 2); track.ContentMarginTop = track.ContentMarginBottom = 2;
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", Box(Chrome, 2));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(ChromeDark, 2));
        t.SetIcon("grabber", "HSlider", Disc(new Color("ffffff"), Chrome, 20));
        t.SetIcon("grabber_highlight", "HSlider", Disc(ChromeTint, ChromeDark, 20));
        t.SetIcon("grabber_disabled", "HSlider", Disc(Panel2, Panel2, 20));
        t.SetIcon("tick", "HSlider", Blank(1));
        t.SetStylebox("background", "ProgressBar", Box(TrackLight, 4));
        t.SetStylebox("fill", "ProgressBar", Box(Chrome, 4));

        t.SetConstant("separation", "VBoxContainer", 8);
        t.SetConstant("separation", "HBoxContainer", 8);
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Hairline, Thickness = 1 });
        t.SetConstant("separation", "HSeparator", 12);
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            var scroll = Empty(3, 3);
            t.SetStylebox("scroll", bar, scroll);
            t.SetStylebox("scroll_focus", bar, scroll);
            t.SetStylebox("grabber", bar, Box(new Color(0, 0, 0, 0.22f), 3));
            t.SetStylebox("grabber_highlight", bar, Box(new Color(0, 0, 0, 0.36f), 3));
            t.SetStylebox("grabber_pressed", bar, Box(new Color(0, 0, 0, 0.5f), 3));
        }
        return t;
    }
}
