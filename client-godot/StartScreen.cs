using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>A washed-out field of node discs behind the start screen, echoing the map.</summary>
public partial class StartBackdrop : Control
{
    public StartBackdrop() { MouseFilter = MouseFilterEnum.Ignore; TextureRepeat = TextureRepeatEnum.Enabled; }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), AppTheme.MapBg);
        DrawTextureRect(AppTheme.GridDot, new Rect2(Vector2.Zero, Size), true);
        var rng = new RandomNumberGenerator { Seed = 11 };
        float cell = 96;
        for (float y = cell / 2; y < Size.Y + cell; y += cell)
            for (float x = cell / 2; x < Size.X + cell; x += cell)
            {
                var p = new Vector2(x + rng.RandfRange(-26, 26), y + rng.RandfRange(-26, 26));
                float r = rng.RandfRange(12, 34);
                bool policy = rng.Randf() < 0.4f;
                DrawCircle(p, r, policy ? new Color(AppTheme.ChromeTint, 0.8f) : new Color(1, 1, 1, 0.7f));
                DrawArc(p, r - 2, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * rng.RandfRange(0.3f, 1f), 40, policy ? new Color(AppTheme.Chrome, 0.25f) : new Color("dcd7ce"), 2, true);
            }
        var c = Size / 2;
        for (int i = 0; i < 3; i++)
        {
            float a = -Mathf.Pi / 2 + Mathf.Tau * (i + 0.5f) / 3;
            var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            for (float d = 80; d < Size.Length(); d += 12) DrawLine(c + u * d, c + u * (d + 5), new Color("d6d0c6"), 1.2f, true);
        }
    }
    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
}

/// <summary>A small compass square with one party's dot (start screen party cards).</summary>
public partial class MiniCompass : Control
{
    private readonly Vector2 _pos; private readonly Color _color;
    public MiniCompass(double x, double y, Color color, float zoom)
    {
        _pos = new Vector2((float)Math.Clamp(x * zoom, -1, 1), (float)Math.Clamp(y * zoom, -1, 1)); _color = color;
        CustomMinimumSize = new Vector2(40, 40); MouseFilter = MouseFilterEnum.Ignore; SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }
    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(AppTheme.Box(AppTheme.Panel, 6), r);
        DrawLine(new Vector2(r.Size.X / 2, 3), new Vector2(r.Size.X / 2, r.Size.Y - 3), new Color("c9c4bb"), 1);
        DrawLine(new Vector2(3, r.Size.Y / 2), new Vector2(r.Size.X - 3, r.Size.Y / 2), new Color("c9c4bb"), 1);
        var p = new Vector2(r.Size.X * (_pos.X + 1) / 2, r.Size.Y * (1 - (_pos.Y + 1) / 2));
        DrawCircle(p, 5.5f, new Color("ffffff"));
        DrawCircle(p, 4.5f, _color);
    }
}

/// <summary>The start screen: title, new game (scenario cards, party choice, language, seed), the display card, load and start.</summary>
public static class StartScreen
{
    public static void Build(Main main, PackSet packs, string language, Action<string> setLanguage)
    {
        var t = new Localizer(packs, language);
        var backdrop = new StartBackdrop();
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        main.AddChild(backdrop);

        bool compact = Layout.Compact || Layout.Short;
        var veil = new ColorRect { Color = new Color(0.93f, 0.92f, 0.89f, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        main.AddChild(veil);

        var panel = new PanelContainer { ThemeTypeVariation = compact ? "Sheet" : "Dialog" };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = Ui.VBox(18);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(Ui.Margin(body, compact ? 20 : 34, compact ? 18 : 28));
        ((Control)scroll.GetChild(0)).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(scroll);

        var title = Ui.L("OpenPolitics", Tx.Display);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", compact ? 28 : 36);
        body.AddChild(title);
        var tag = Ui.L(t.T("ui.client.tagline"), Tx.Body, AppTheme.Ink2, true);
        tag.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(tag);

        // New game.
        var game = Ui.VBox(12);
        game.AddChild(Ui.Section(t.T("ui.client.new_game")));
        game.AddChild(Ui.L(t.T("ui.client.scenario"), Tx.Small, AppTheme.Ink3));
        var scenarios = packs.Scenarios.OrderByDescending(s => s.Politics != null).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        int chosen = 0;
        string? party = scenarios.Count > 0 ? scenarios[0].Politics?.PlayerParty : null;
        var partyBox = Ui.VBox(10);
        void BuildParties() => PartyPicker(partyBox, packs, t, scenarios[chosen], party, id => { party = id; BuildParties(); });
        var cards = Ui.VBox(8);
        var group = new ButtonGroup();
        for (int i = 0; i < scenarios.Count; i++)
        {
            int k = i;
            var b = new Button { ToggleMode = true, ButtonGroup = group, ButtonPressed = i == 0, ThemeTypeVariation = "ButtonCard", FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            var col = Ui.VBox(2);
            col.AddChild(Ui.L(t.T("name." + scenarios[i].Id), Tx.Heading, null, true));
            if (t.Description(scenarios[i].Id) is { } desc) col.AddChild(Ui.L(desc, Tx.Small, AppTheme.Ink2, true));
            else col.AddChild(Ui.L(t.T(scenarios[i].Politics != null ? "ui.start.politics_desc" : "ui.start.sandbox_desc"), Tx.Small, AppTheme.Ink2, true));
            var m = Ui.Margin(col, 14, 10);
            m.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            Detail.SetIgnore(m);
            b.AddChild(m);
            b.CustomMinimumSize = new Vector2(0, 64);
            b.Pressed += () => { if (chosen == k) return; chosen = k; party = scenarios[k].Politics?.PlayerParty; BuildParties(); };
            cards.AddChild(b);
        }
        game.AddChild(cards);
        game.AddChild(partyBox);
        if (scenarios.Count > 0) BuildParties();
        var opts = compact ? (BoxContainer)Ui.VBox(12) : Ui.HBox(24);
        var langCol = Ui.VBox(6);
        langCol.AddChild(Ui.L(t.T("ui.client.language"), Tx.Small, AppTheme.Ink3));
        langCol.AddChild(Ui.Segmented(new[] { "Čeština", "English" }, language == "cs" ? 0 : 1, i => setLanguage(i == 0 ? "cs" : "en"), 32));
        opts.AddChild(langCol);
        var seedCol = Ui.VBox(6);
        seedCol.AddChild(Ui.L(t.T("ui.client.seed"), Tx.Small, AppTheme.Ink3));
        var seedRow = Ui.HBox(4);
        var seed = new LineEdit { Text = ((Time.GetTicksMsec() % 99999) + 1).ToString(), CustomMinimumSize = new Vector2(120, 32), MaxLength = 9 };
        seedRow.AddChild(seed);
        seedRow.AddChild(Ui.Btn("", () => seed.Text = (Random.Shared.Next(1, 999999)).ToString(), "ButtonSecondary", IconKind.Arrows, t.T("ui.start.reroll"), false, 32));
        seedCol.AddChild(seedRow);
        opts.AddChild(seedCol);
        game.AddChild(opts);

        var display = DisplayCard(main, t, true);
        Control columns;
        if (compact) { var v = Ui.VBox(20); v.AddChild(game); v.AddChild(display); columns = v; }
        else
        {
            var h = Ui.HBox(28);
            game.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; game.SizeFlagsStretchRatio = 1.5f;
            display.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            h.AddChild(game); h.AddChild(Detail.VLine()); h.AddChild(display);
            columns = h;
        }
        body.AddChild(columns);

        var buttons = Ui.HBox(16);
        var saves = Session.SaveDirFiles().ToList();
        var load = Ui.Btn(t.T("ui.start.load_game"), () => { }, "", IconKind.Save, saves.Count == 0 ? t.T("ui.start.no_saves") : null, saves.Count == 0, 44);
        load.Pressed += () =>
        {
            var menu = new PopupMenu();
            for (int i = 0; i < saves.Count; i++) menu.AddItem(System.IO.Path.GetFileName(saves[i]).Replace(".save.yaml", ""), i);
            menu.IdPressed += i => main.LoadGame(saves[(int)i]);
            main.AddChild(menu);
            var r = load.GetGlobalRect();
            menu.Popup(new Rect2I((int)r.Position.X, (int)r.End.Y, (int)r.Size.X, 10));
            menu.PopupHide += () => menu.QueueFree();
        };
        var start = Ui.Btn(t.T("ui.client.start"), () =>
        {
            ulong value = ulong.TryParse(seed.Text, out var v) && v > 0 ? v : 1;
            main.StartGame(scenarios[chosen].Id, value, language, scenarios[chosen].Politics != null ? party : null);
        }, "ButtonPlay", IconKind.Play, null, false, 44);
        start.AddThemeFontOverride("font", AppTheme.Bold); start.AddThemeFontSizeOverride("font_size", 15);
        load.AddThemeFontOverride("font", AppTheme.Bold);
        load.SizeFlagsHorizontal = start.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buttons.AddChild(load); buttons.AddChild(start);
        body.AddChild(buttons);
        var foot = Ui.HBox(8);
        foot.AddChild(Ui.L(t.T("ui.client.illustrative"), Tx.Small, AppTheme.Ink3, true));
        foot.AddChild(Ui.Right(Ui.L("F11 · " + t.T("ui.menu.fullscreen"), Tx.Small, AppTheme.Ink3)));
        body.AddChild(foot);

        main.AddChild(panel);
        void Place()
        {
            if (!GodotObject.IsInstanceValid(panel)) return;
            var size = main.Size;
            if (compact) { panel.Position = Vector2.Zero; panel.Size = size; return; }
            float w = Math.Min(1080, size.X - 80);
            float h = Math.Min(size.Y - 60, body.GetCombinedMinimumSize().Y + 60);
            panel.Size = new Vector2(w, h);
            panel.Position = (size - panel.Size) / 2;
        }
        main.Resized += Place;
        panel.TreeExiting += () => main.Resized -= Place;
        Callable.From(Place).CallDeferred();
        main.GetTree().CreateTimer(0.05).Timeout += Place;
    }

    /// <summary>
    /// The party choice for a political scenario (decision 0013): one card per party with its colour, name, starting role, poll and seats and its
    /// place on the compass, largest first; under the cards what the chosen role means. Empty for a scenario without politics.
    /// </summary>
    public static void PartyPicker(VBoxContainer box, PackSet packs, Localizer t, ScenarioDef scenario, string? chosen, Action<string> choose)
    {
        Ui.Clear(box);
        var options = PartyChoice.Options(packs, scenario.Id);
        if (options.Count == 0) return;
        box.AddChild(Ui.L(t.T("ui.start.your_party"), Tx.Small, AppTheme.Ink3));
        float far = (float)options.Max(o => Math.Max(Math.Abs(o.X), Math.Abs(o.Y)));
        float zoom = far > 0.02f ? Math.Min(4f, 0.85f / far) : 1f;
        var grid = new FlowGrid { MinColumn = 250, MaxColumns = 3, Gap = 8 };
        var group = new ButtonGroup();
        foreach (var o in options)
        {
            string id = o.PartyId;
            var b = new Button { ToggleMode = true, ButtonGroup = group, ButtonPressed = id == chosen, ThemeTypeVariation = "ButtonCard", FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 104),
                TooltipText = t.Name(id) + " · " + t.T("ui.start.role." + o.Role.ToString().ToLowerInvariant()) };
            var row = Ui.HBox(10);
            var col = Ui.VBox(3);
            col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var head = Ui.HBox(8);
            head.AddChild(new ColorDot(Ui.PartyColor(packs, id), 6.5f));
            var name = Ui.L(t.Name(id), Tx.BodyStrong);
            name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(name);
            col.AddChild(head);
            var roleColor = o.Role == Role.Leader ? AppTheme.Chrome : o.Role == Role.Partner ? AppTheme.Good : AppTheme.Ink3;
            col.AddChild(Ui.Pill(t.T("ui.start.role." + o.Role.ToString().ToLowerInvariant()), roleColor));
            col.AddChild(Ui.L(Ui.Fix(t.T("ui.start.party_line", Ui.Num(o.Support * 100), o.Seats)), Tx.Small, AppTheme.Ink2));
            col.AddChild(Ui.L(CompassWords(t, o), Tx.Small, AppTheme.Ink3));
            row.AddChild(col);
            row.AddChild(new MiniCompass(o.X, o.Y, Ui.PartyColor(packs, id), zoom));
            var m = Ui.Margin(row, 12, 9);
            m.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            Detail.SetIgnore(m);
            b.AddChild(m);
            b.Pressed += () => choose(id);
            grid.AddChild(b);
        }
        box.AddChild(grid);
        var pick = options.FirstOrDefault(o => o.PartyId == chosen) ?? options[0];
        var hint = Ui.HBox(10);
        hint.AddChild(new IconView(pick.Role == Role.Opposition ? IconKind.Alert : IconKind.Ballot, 18, AppTheme.Chrome));
        hint.AddChild(Ui.L(t.T("ui.start.role_hint." + pick.Role.ToString().ToLowerInvariant(), t.Name(pick.PartyId)), Tx.Body, AppTheme.Ink2, true));
        box.AddChild(Ui.Panel(hint, "Inset"));
    }

    private static string CompassWords(Localizer t, PartyOption o) =>
        t.T(o.X < -0.15 ? "ui.start.econ.left" : o.X > 0.15 ? "ui.start.econ.right" : "ui.start.econ.centre") + ", " +
        t.T(o.Y < -0.15 ? "ui.start.social.liberal" : o.Y > 0.15 ? "ui.start.social.authoritarian" : "ui.start.social.centre");

    /// <summary>Window mode, UI scale, automatic scaling, reduced motion and the resulting logical size. Applies live.</summary>
    public static Control DisplayCard(Main main, Localizer t, bool withHeading)
    {
        var box = Ui.VBox(12);
        if (withHeading) box.AddChild(Ui.Section(t.T("ui.start.display")));
        box.AddChild(Ui.L(t.T("ui.start.window_mode"), Tx.Small, AppTheme.Ink3));
        var window = main.GetWindow();
        box.AddChild(Ui.Segmented(new[] { t.T("ui.start.mode.windowed"), t.T("ui.start.mode.borderless"), t.T("ui.start.mode.fullscreen") }, (int)DisplaySettings.Mode,
            i => DisplaySettings.SetMode(window, (WindowMode)i), 32));
        box.AddChild(Ui.L(t.T("ui.start.resolution"), Tx.Small, AppTheme.Ink3));
        var res = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = t.T("ui.start.resolution_hint") };
        var choices = new List<string> { "fit" };
        res.AddItem(t.T("ui.start.fit"));
        foreach (var c in DisplaySettings.Choices(window)) { choices.Add($"{c.X}x{c.Y}"); res.AddItem($"{c.X} \u00d7 {c.Y}"); }
        int sel = choices.IndexOf(DisplaySettings.Resolution);
        res.Selected = sel >= 0 ? sel : 0;
        res.ItemSelected += i => DisplaySettings.SetResolution(window, choices[(int)i]);
        box.AddChild(res);
        var screen = DisplayServer.ScreenGetSize(window.CurrentScreen);
        box.AddChild(Ui.L(t.T("ui.start.screen_info", screen.X, screen.Y, window.Size.X, window.Size.Y), Tx.Small, AppTheme.Ink3, true));
        var scaleHead = Ui.HBox(8);
        scaleHead.AddChild(Ui.L(t.T("ui.start.ui_scale"), Tx.Small, AppTheme.Ink3));
        scaleHead.AddChild(Ui.Spacer());
        var value = Ui.L(Ui.Num(DisplaySettings.UiScale * 100, "0") + " %", Tx.Figure);
        scaleHead.AddChild(value);
        box.AddChild(scaleHead);
        var slider = new HSlider { MinValue = 80, MaxValue = 140, Step = 5, Value = DisplaySettings.UiScale * 100, CustomMinimumSize = new Vector2(200, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += _ => { dragging = false; DisplaySettings.SetScale(window, (float)slider.Value / 100f); };
        slider.ValueChanged += v => { value.Text = Ui.Num(v, "0") + " %"; if (!dragging) DisplaySettings.SetScale(window, (float)v / 100f); };
        box.AddChild(slider);
        var ends = Ui.HBox(); ends.AddChild(Ui.L("80 %", Tx.Small)); ends.AddChild(Ui.Spacer()); ends.AddChild(Ui.L("140 %", Tx.Small));
        box.AddChild(ends);
        var auto = new CheckBox { Text = t.T("ui.start.auto_scale"), ButtonPressed = DisplaySettings.AutoScale, FocusMode = Control.FocusModeEnum.None, TooltipText = t.T("ui.start.auto_hint") };
        auto.Toggled += on => DisplaySettings.SetAuto(window, on);
        box.AddChild(auto);
        var motion = new CheckBox { Text = t.T("ui.start.reduce_motion"), ButtonPressed = DisplaySettings.ReduceMotion, FocusMode = Control.FocusModeEnum.None };
        motion.Toggled += on => DisplaySettings.SetReduceMotion(on);
        box.AddChild(motion);
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
        box.AddChild(Ui.Panel(preview, "Inset"));
        return box;
    }
}
