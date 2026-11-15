using Godot;

namespace OpenPolitics.Client;

public enum WindowMode { Windowed, Borderless, Fullscreen }

/// <summary>
/// How the game fills the screen. The window is freely resizable; the interface is laid out in logical pixels for a 1280 x 720 reference
/// and scaled to the window (<c>canvas_items</c>, <c>expand</c>), so other shapes show more room instead of bars. On top of that:
/// an automatic factor that keeps text readable in small windows (the logical viewport shrinks toward 960 x 540 instead of the glyphs
/// shrinking), the player's UI scale (80-140 %), the window mode and "reduce motion". Saved in <c>user://display.cfg</c>.
/// Formula: docs/design/layout.md 1.2.
/// </summary>
public static class DisplaySettings
{
    public const float MinScale = 0.8f, MaxScale = 1.4f;
    public static readonly Vector2I Reference = new(1280, 720);
    public static readonly Vector2I MinLogical = new(960, 540);
    public static readonly Vector2I MinWindow = new(960, 540);
    private const string File = "user://display.cfg";

    public static float UiScale { get; private set; } = 1.0f;
    public static bool AutoScale { get; private set; } = true;
    public static bool ReduceMotion { get; private set; }
    public static WindowMode Mode { get; private set; } = WindowMode.Windowed;
    public static bool Fullscreen => Mode == WindowMode.Fullscreen;
    public static event Action? Changed;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        UiScale = Mathf.Clamp((float)(double)cfg.GetValue("display", "ui_scale", 1.0), MinScale, MaxScale);
        AutoScale = (bool)cfg.GetValue("display", "auto_scale", true);
        ReduceMotion = (bool)cfg.GetValue("display", "reduce_motion", false);
        Mode = (WindowMode)(int)cfg.GetValue("display", "window_mode", (bool)cfg.GetValue("display", "fullscreen", false) ? 2 : 0);
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display", "ui_scale", (double)UiScale);
        cfg.SetValue("display", "auto_scale", AutoScale);
        cfg.SetValue("display", "reduce_motion", ReduceMotion);
        cfg.SetValue("display", "window_mode", (int)Mode);
        cfg.Save(File);
    }

    /// <summary>The content scale factor for a window size (layout.md 1.2).</summary>
    public static float Factor(Vector2 win)
    {
        float baseFit = Math.Min(win.X / Reference.X, win.Y / Reference.Y);
        if (baseFit <= 0) return 1;
        float auto = AutoScale ? Mathf.Clamp(Math.Min(Math.Min(win.X / MinLogical.X, win.Y / MinLogical.Y), 1f) / baseFit, 1f, 1.4f) : 1f;
        float f = auto * UiScale;
        float fMax = Math.Min(win.X / MinLogical.X, win.Y / MinLogical.Y) / baseFit;
        return Mathf.Clamp(f, 0.5f, Math.Max(1f, fMax));
    }

    /// <summary>The logical size the layout sees for a window size.</summary>
    public static Vector2 LogicalSize(Vector2 win)
    {
        float baseFit = Math.Min(win.X / Reference.X, win.Y / Reference.Y);
        return win / Math.Max(baseFit * Factor(win), 0.01f);
    }

    /// <summary>Applies the saved choices to the window. On first start a window that fits the screen is opened.</summary>
    public static void Apply(Window window, bool sizeWindow)
    {
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        window.ContentScaleSize = Reference;
        window.MinSize = MinWindow;
        if (sizeWindow)
        {
            ApplyMode(window);
            if (Mode == WindowMode.Windowed)
            {
                var usable = DisplayServer.ScreenGetUsableRect();
                var size = new Vector2I(Math.Min(1600, (int)(usable.Size.X * 0.92)), Math.Min(900, (int)(usable.Size.Y * 0.92)));
                size = new Vector2I(Math.Max(size.X, MinWindow.X), Math.Max(size.Y, MinWindow.Y));
                window.Size = size;
                window.Position = usable.Position + (usable.Size - size) / 2;
            }
        }
        Refit(window);
    }

    /// <summary>Recomputes the scale factor for the current window size (called on every resize).</summary>
    public static void Refit(Window window)
    {
        float f = Factor(window.Size);
        if (Math.Abs(window.ContentScaleFactor - f) > 1e-4f) window.ContentScaleFactor = f;
    }

    private static void ApplyMode(Window window)
    {
        switch (Mode)
        {
            case WindowMode.Fullscreen: window.Mode = Window.ModeEnum.ExclusiveFullscreen; break;
            case WindowMode.Borderless:
                window.Mode = Window.ModeEnum.Windowed; window.Borderless = true;
                var usable = new Rect2I(DisplayServer.ScreenGetPosition(), DisplayServer.ScreenGetSize());
                window.Position = usable.Position; window.Size = usable.Size;
                break;
            default:
                if (window.Mode != Window.ModeEnum.Windowed) window.Mode = Window.ModeEnum.Windowed;
                window.Borderless = false;
                break;
        }
    }

    public static void SetScale(Window window, float scale, bool save = true)
    {
        UiScale = Mathf.Clamp(scale, MinScale, MaxScale);
        Refit(window);
        if (save) Save();
        Changed?.Invoke();
    }

    public static void SetAuto(Window window, bool on) { AutoScale = on; Refit(window); Save(); Changed?.Invoke(); }
    public static void SetReduceMotion(bool on) { ReduceMotion = on; Save(); Changed?.Invoke(); }

    public static void SetMode(Window window, WindowMode mode)
    {
        bool wasBorderless = Mode == WindowMode.Borderless;
        Mode = mode;
        ApplyMode(window);
        if (mode == WindowMode.Windowed && wasBorderless)
        {
            var usable = DisplayServer.ScreenGetUsableRect();
            var size = new Vector2I(Math.Min(1600, (int)(usable.Size.X * 0.85)), Math.Min(900, (int)(usable.Size.Y * 0.85)));
            window.Size = size; window.Position = usable.Position + (usable.Size - size) / 2;
        }
        Refit(window);
        Save(); Changed?.Invoke();
    }

    public static void ToggleFullscreen(Window window) => SetMode(window, Fullscreen ? WindowMode.Windowed : WindowMode.Fullscreen);
}

public enum WidthClass { Compact, Regular, Wide }
public enum HeightClass { Short, Normal, Tall }

/// <summary>
/// The size classes of the logical viewport: compact &lt; 1100 &lt;= regular &lt; 1600 &lt;= wide; short &lt; 640, tall &gt;= 860.
/// <see cref="Main"/> feeds it on every resize; <see cref="Changed"/> fires when a class flips.
/// </summary>
public static class Layout
{
    public static Vector2 Size { get; private set; } = new(1280, 720);
    public static WidthClass Width { get; private set; } = WidthClass.Regular;
    public static HeightClass Height { get; private set; } = HeightClass.Normal;
    public static bool Compact => Width == WidthClass.Compact;
    public static bool Wide => Width == WidthClass.Wide;
    public static bool Short => Height == HeightClass.Short;
    public static bool Tall => Height == HeightClass.Tall;
    public static event Action? Changed;

    public static WidthClass WidthOf(float w) => w < 1100 ? WidthClass.Compact : w < 1600 ? WidthClass.Regular : WidthClass.Wide;
    public static HeightClass HeightOf(float h) => h < 640 ? HeightClass.Short : h < 860 ? HeightClass.Normal : HeightClass.Tall;

    public static void Update(Vector2 size)
    {
        Size = size;
        var w = WidthOf(size.X); var h = HeightOf(size.Y);
        if (w == Width && h == Height) return;
        Width = w; Height = h;
        Changed?.Invoke();
    }

    /// <summary>Height of the top bar for the current class.</summary>
    public static float TopBar => Compact || Short ? 60 : 76;
    public static float Sidebar => Compact ? 56 : Wide ? 288 : 248;
}
