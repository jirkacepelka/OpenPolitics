using Godot;

namespace OpenPolitics.Client;

/// <summary>
/// How the game fills the screen. The window is freely resizable; the interface is laid out for a reference size of 1280 x 720 and scaled
/// to the window (<c>canvas_items</c> with <c>expand</c>), so a wider or taller window shows more room rather than black bars. On top of
/// that the player may make everything larger or smaller and go full screen (F11). Saved in <c>user://display.cfg</c>.
/// </summary>
public static class DisplaySettings
{
    public const float MinScale = 0.8f, MaxScale = 1.4f;
    public static readonly Vector2I Reference = new(1280, 720);
    public static readonly Vector2I MinWindow = new(960, 540);
    private const string File = "user://display.cfg";

    public static float UiScale { get; private set; } = 1.0f;
    public static bool Fullscreen { get; private set; }
    public static event Action? Changed;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        UiScale = Mathf.Clamp((float)(double)cfg.GetValue("display", "ui_scale", 1.0), MinScale, MaxScale);
        Fullscreen = (bool)cfg.GetValue("display", "fullscreen", false);
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display", "ui_scale", (double)UiScale);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.Save(File);
    }

    /// <summary>Applies the saved choices to the window. On first start a window that fits the screen is opened.</summary>
    public static void Apply(Window window, bool sizeWindow)
    {
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        window.ContentScaleSize = Reference;
        window.ContentScaleFactor = UiScale;
        window.MinSize = MinWindow;
        if (!sizeWindow) return;
        if (Fullscreen) { window.Mode = Window.ModeEnum.ExclusiveFullscreen; return; }
        var usable = DisplayServer.ScreenGetUsableRect();
        var size = new Vector2I(Math.Min(1600, (int)(usable.Size.X * 0.92)), Math.Min(900, (int)(usable.Size.Y * 0.92)));
        size = new Vector2I(Math.Max(size.X, MinWindow.X), Math.Max(size.Y, MinWindow.Y));
        window.Size = size;
        window.Position = usable.Position + (usable.Size - size) / 2;
    }

    public static void SetScale(Window window, float scale)
    {
        UiScale = Mathf.Clamp(scale, MinScale, MaxScale);
        window.ContentScaleFactor = UiScale;
        Save(); Changed?.Invoke();
    }

    public static void ToggleFullscreen(Window window, bool? on = null)
    {
        Fullscreen = on ?? !Fullscreen;
        window.Mode = Fullscreen ? Window.ModeEnum.ExclusiveFullscreen : Window.ModeEnum.Windowed;
        Save(); Changed?.Invoke();
    }
}
