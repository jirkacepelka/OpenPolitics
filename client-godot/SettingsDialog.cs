using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The settings dialog, opened from the main menu and from the in-game menu: language (main menu only, a running game keeps its language),
/// window mode and size, interface size and motion. Everything applies at once and is remembered in <c>user://display.cfg</c>.
/// </summary>
public static class SettingsDialog
{
    public static Dialog Open(Main main, Localizer t, bool allowLanguage, Action<string> setLanguage)
    {
        bool compact = Layout.Compact;
        var d = new Dialog(t.T("ui.menu.settings"), IconKind.Gear, t.T("ui.client.close")) { PreferredWidth = 760, Key = "settings" };
        var window = main.GetWindow();

        // ---- general: language, window
        var general = Ui.VBox(12);
        general.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        general.AddChild(Ui.Section(t.T("ui.settings.general")));
        if (allowLanguage)
        {
            general.AddChild(Ui.L(t.T("ui.client.language"), Tx.Small, AppTheme.Ink3));
            general.AddChild(Ui.Segmented(new[] { "Čeština", "English" }, main.Language == "cs" ? 0 : 1, i => setLanguage(i == 0 ? "cs" : "en"), 34));
        }
        general.AddChild(Ui.L(t.T("ui.start.window_mode"), Tx.Small, AppTheme.Ink3));
        general.AddChild(Ui.Segmented(new[] { t.T("ui.start.mode.windowed"), t.T("ui.start.mode.borderless"), t.T("ui.start.mode.fullscreen") }, (int)DisplaySettings.Mode,
            i => DisplaySettings.SetMode(window, (WindowMode)i), 34));
        general.AddChild(Ui.L(t.T("ui.start.resolution"), Tx.Small, AppTheme.Ink3));
        var res = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = t.T("ui.start.resolution_hint") };
        var choices = new List<string> { "fit" };
        res.AddItem(t.T("ui.start.fit"));
        foreach (var c in DisplaySettings.Choices(window)) { choices.Add($"{c.X}x{c.Y}"); res.AddItem($"{c.X} × {c.Y}"); }
        int sel = choices.IndexOf(DisplaySettings.Resolution);
        res.Selected = sel >= 0 ? sel : 0;
        res.ItemSelected += i => DisplaySettings.SetResolution(window, choices[(int)i]);
        general.AddChild(res);
        var screen = DisplayServer.ScreenGetSize(window.CurrentScreen);
        general.AddChild(Ui.L(t.T("ui.start.screen_info", screen.X, screen.Y, window.Size.X, window.Size.Y), Tx.Small, AppTheme.Ink3, true));

        // ---- interface: size, motion
        var face = Ui.VBox(12);
        face.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        face.AddChild(Ui.Section(t.T("ui.settings.interface")));
        var scaleHead = Ui.HBox(8);
        scaleHead.AddChild(Ui.L(t.T("ui.start.ui_scale"), Tx.Small, AppTheme.Ink3));
        scaleHead.AddChild(Ui.Spacer());
        var value = Ui.L(Ui.Num(DisplaySettings.UiScale * 100, "0") + " %", Tx.Figure);
        scaleHead.AddChild(value);
        face.AddChild(scaleHead);
        var slider = new HSlider { MinValue = 30, MaxValue = 150, Step = 5, Value = DisplaySettings.UiScale * 100, CustomMinimumSize = new Vector2(200, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += _ => { dragging = false; DisplaySettings.SetScale(window, (float)slider.Value / 100f); };
        slider.ValueChanged += v => { value.Text = Ui.Num(v, "0") + " %"; if (!dragging) DisplaySettings.SetScale(window, (float)v / 100f); };
        face.AddChild(slider);
        var quick = Ui.HBox(8);
        quick.AddChild(Ui.Btn(t.T("ui.start.scale_default"), () => { slider.Value = 100; DisplaySettings.SetScale(window, 1f); }, "ButtonSecondary", null, null, false, 30));
        var native = Ui.Btn(t.T("ui.start.scale_native"), () => { float f = DisplaySettings.NativeScale(window); slider.Value = Math.Round(f * 100 / 5) * 5; DisplaySettings.SetScale(window, f); }, "ButtonSecondary", null, null, false, 30);
        native.TooltipText = t.T("ui.start.scale_native_hint");
        quick.AddChild(native);
        face.AddChild(quick);
        var auto = new CheckBox { Text = t.T("ui.start.auto_scale"), ButtonPressed = DisplaySettings.AutoScale, FocusMode = Control.FocusModeEnum.None, TooltipText = t.T("ui.start.auto_hint") };
        auto.Toggled += on => DisplaySettings.SetAuto(window, on);
        face.AddChild(auto);
        var motion = new CheckBox { Text = t.T("ui.start.reduce_motion"), ButtonPressed = DisplaySettings.ReduceMotion, FocusMode = Control.FocusModeEnum.None };
        motion.Toggled += on => DisplaySettings.SetReduceMotion(on);
        face.AddChild(motion);
        var preview = Ui.L("", Tx.Small, AppTheme.Ink2, true);
        void Update()
        {
            if (!GodotObject.IsInstanceValid(preview)) return;
            var size = main.Size;
            string cls = t.T("ui.start.class." + Layout.WidthOf(size.X).ToString().ToLowerInvariant());
            preview.Text = t.T("ui.start.preview", (int)Math.Round(size.X), (int)Math.Round(size.Y), cls, AppTheme.FontSource);
        }
        Update();
        main.Resized += Update;
        preview.TreeExiting += () => main.Resized -= Update;
        face.AddChild(Ui.Panel(preview, "Inset"));

        if (compact) { d.Body.AddChild(general); d.Body.AddChild(face); }
        else
        {
            var cols = Ui.HBox(24);
            cols.AddChild(general); cols.AddChild(Detail.VLine()); cols.AddChild(face);
            d.Body.AddChild(cols);
        }
        d.Refit();
        return d;
    }
}
