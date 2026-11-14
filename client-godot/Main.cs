using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The window. A running game is a full-screen map of the country with a HUD on top (date, key indicators, the next-quarter button),
/// a dock of icons at the bottom and one floating panel at a time. Talks to the core only through <see cref="Session"/>.
/// </summary>
public partial class Main : Control
{
    public PackSet? Packs { get; private set; }
    public Session? Session { get; private set; }
    public List<Screen> Screens { get; } = new();
    public WorldMap? Map { get; private set; }
    public Screen? Current { get; private set; }

    private string _language = "en";
    private bool _endShown, _batch;
    private TurnPopup? _popup;
    private PanelContainer? _panel;
    private Label _panelTitle = new();
    private Control? _panelBody;
    private readonly Dictionary<Screen, IconButton> _dock = new();
    private readonly Dictionary<string, HudChip> _chips = new();
    private Label _date = new(), _turnInfo = new(), _toast = new();
    private PanelContainer _toastBox = new();
    private IconButton _capital = new(IconKind.Coin, "");
    private Control _hud = new();

    /// <summary>The start-of-quarter briefing; the smoke test turns it off so it never blocks a scripted run.</summary>
    public bool PopupsEnabled { get; set; } = true;

    public override void _Ready()
    {
        Theme = AppTheme.Instance;
        _language = OS.GetLocale().StartsWith("cs", StringComparison.OrdinalIgnoreCase) ? "cs" : "en";
        var loaded = Session.LoadPacks(Session.DefaultPacksDir());
        if (!loaded.Ok)
        {
            ShowFatal("Packs could not be loaded from " + Session.DefaultPacksDir() + ":\n" + string.Join("\n", loaded.Diagnostics));
            return;
        }
        Packs = loaded.Packs;
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--smoke"))
        {
            PopupsEnabled = false; Animations = false;
            var shots = Array.IndexOf(args, "--screenshots");
            _ = Smoke.Run(this, shots >= 0 && shots + 1 < args.Length ? args[shots + 1] : null);
            return;
        }
        ShowStart();
    }

    private void Reset()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        Screens.Clear(); _dock.Clear(); _chips.Clear();
        Map = null; Current = null; _popup = null; _panel = null; _panelBody = null;
        var bg = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.45f), FillTo = new Vector2(1.05f, 1.0f), Width = 256, Height = 256,
                Gradient = new Gradient { Colors = new[] { AppTheme.MapGlow, new Color("0e1830"), AppTheme.Bg }, Offsets = new[] { 0f, 0.55f, 1f } },
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
    }

    private void ShowFatal(string text)
    {
        Reset();
        var box = Ui.VBox(); box.SetAnchorsPreset(LayoutPreset.FullRect);
        box.AddChild(Ui.Margin(Ui.Text(text, Ui.Bad, 16, true), 24));
        AddChild(box);
        GD.PrintErr(text);
    }

    // ---- start screen ----------------------------------------------------------------------------------

    public void ShowStart()
    {
        Reset();
        var t = new Localizer(Packs!, _language);
        var backdrop = new StartBackdrop();
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);
        var center = new CenterContainer(); center.SetAnchorsPreset(LayoutPreset.FullRect);
        var columns = Ui.HBox(48);

        // Left: the name and what the game is.
        var hero = Ui.VBox(16);
        hero.CustomMinimumSize = new Vector2(420, 0);
        hero.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var mark = Ui.HBox(14);
        mark.AddChild(new IconBadge(IconKind.Politics, AppTheme.Accent, 56));
        mark.AddChild(Ui.Big("OpenPolitics", Colors.White, 52));
        hero.AddChild(mark);
        hero.AddChild(Ui.Text(t.T("ui.client.tagline"), AppTheme.Text2, 20, true));
        var legend = Ui.HBox(16);
        foreach (var (icon, color, key) in new[] { (IconKind.Percent, AppTheme.Tax, "ui.map.taxes"), (IconKind.CoinOut, AppTheme.Spend, "ui.map.spending"), (IconKind.Scale, AppTheme.Regulation, "ui.map.regulation") })
        {
            var item = Ui.HBox(8);
            var badge = new IconBadge(icon, color, 32) { Circle = true };
            item.AddChild(badge);
            item.AddChild(Ui.Text(t.T(key), AppTheme.Text2, AppTheme.Body));
            legend.AddChild(item);
        }
        hero.AddChild(Ui.Gap(4));
        hero.AddChild(legend);
        hero.AddChild(Ui.Gap(4));
        hero.AddChild(Ui.Text(t.T("ui.client.illustrative"), AppTheme.Muted, AppTheme.Caption, true));
        columns.AddChild(hero);

        // Right: the new-game card.
        var form = Ui.VBox(16);
        form.CustomMinimumSize = new Vector2(400, 0);
        form.AddChild(Ui.Text(t.T("ui.client.new_game"), Colors.White, AppTheme.SectionSize, false, AppTheme.Bold));
        var lang = new OptionButton();
        lang.AddItem("Čeština", 0); lang.AddItem("English", 1);
        lang.Selected = _language == "cs" ? 0 : 1;
        lang.ItemSelected += i => { _language = i == 0 ? "cs" : "en"; ShowStart(); };
        Field(form, t.T("ui.client.language"), lang);

        var scenarios = Packs!.Scenarios.OrderByDescending(s => s.Politics != null).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        var scenarioBox = new OptionButton();
        foreach (var s in scenarios) scenarioBox.AddItem(t.T("name." + s.Id));
        Field(form, t.T("ui.client.scenario"), scenarioBox);

        var seed = new SpinBox { MinValue = 1, MaxValue = 999999, Step = 1, Value = (Time.GetTicksMsec() % 99999) + 1 };
        Field(form, t.T("ui.client.seed"), seed);

        form.AddChild(Ui.Gap(4));
        var start = new IconButton(IconKind.Play, t.T("ui.client.start"), "", new Vector2(0, 52)) { Primary = true, Round = true };
        start.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        start.Pressed += () => StartGame(scenarios[scenarioBox.Selected].Id, (ulong)seed.Value, _language);
        form.AddChild(start);

        var saves = Session.SaveDirFiles().ToList();
        if (saves.Count > 0)
        {
            form.AddChild(Ui.Rule());
            form.AddChild(Ui.Caption(t.T("ui.client.saved_games")));
            var saveBox = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            foreach (var f in saves) saveBox.AddItem(System.IO.Path.GetFileName(f));
            var row = Ui.HBox(); row.AddChild(saveBox);
            row.AddChild(Ui.Btn(t.T("ui.client.load"), () => LoadGame(saves[saveBox.Selected])));
            form.AddChild(row);
        }
        var formCard = Ui.Card(Ui.Margin(form, 12), AppTheme.Surface);
        formCard.AddThemeStyleboxOverride("panel", AppTheme.Glass(0.94f, 18));
        formCard.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        columns.AddChild(formCard);
        center.AddChild(columns);
        AddChild(center);
    }

    private static void Field(VBoxContainer form, string label, Control input)
    {
        var col = Ui.VBox(6);
        col.AddChild(Ui.Caption(label));
        input.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        input.CustomMinimumSize = new Vector2(0, 40);
        col.AddChild(input);
        form.AddChild(col);
    }

    // ---- game -------------------------------------------------------------------------------------------

    public void StartGame(string scenarioId, ulong seed, string language)
    {
        _language = language;
        Session = new Session(Packs!, scenarioId, seed, language);
        BuildGameUi();
    }

    public void LoadGame(string file)
    {
        try
        {
            var save = SaveData.FromYaml(System.IO.File.ReadAllText(file));
            Session = new Session(Packs!, save.ScenarioId, save.Seed, _language);
            if (!Session.Load(file)) { ShowStart(); return; }
            BuildGameUi();
        }
        catch (Exception e) { GD.PrintErr(e.ToString()); ShowStart(); }
    }

    private void BuildGameUi()
    {
        Reset();
        _endShown = false;
        var s = Session!;

        Map = new WorldMap();
        Map.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(Map);
        Map.Setup(s);

        // Screens live inside the floating panel.
        Screens.Add(new OverviewScreen());
        Screens.Add(new PoliciesScreen());
        Screens.Add(new VotersScreen());
        if (s.HasPolitics) { Screens.Add(new PoliticsScreen()); Screens.Add(new PartyScreen()); }
        Screens.Add(new NewsScreen());
        BuildPanel(s);
        BuildHud(s);
        BuildDock(s);
        BuildToast();

        s.Changed += OnChanged;
        s.QuarterEnded += () => { if (!_batch) ShowBriefing(); };
        OnChanged();
        Open(Screens[0]);
    }

    private const float TopBar = 64, PanelTop = 76, PanelBottom = 100;
    private IconBadge? _panelBadge;
    private HBoxContainer? _panelHead;

    /// <summary>Panel and button animations; off for scripted runs so screenshots never catch a half-faded panel.</summary>
    public static bool Animations { get; set; } = true;

    public static IconKind IconOf(Screen sc) => sc switch
    {
        OverviewScreen => IconKind.Overview, PoliciesScreen => IconKind.Policies, VotersScreen => IconKind.Voters,
        PoliticsScreen => IconKind.Politics, PartyScreen => IconKind.Party, _ => IconKind.News,
    };

    private void BuildPanel(Session s)
    {
        _panel = new PanelContainer { Visible = false };
        _panel.AddThemeStyleboxOverride("panel", AppTheme.Glass(0.985f, 16));
        _panel.SetAnchorsPreset(LayoutPreset.LeftWide);
        _panel.OffsetLeft = 16; _panel.OffsetTop = PanelTop; _panel.OffsetBottom = -PanelBottom;
        var column = Ui.VBox(8);
        _panelHead = Ui.HBox(12);
        _panelBadge = new IconBadge(IconKind.Overview, AppTheme.Accent, 36);
        _panelHead.AddChild(_panelBadge);
        _panelTitle = Ui.Big("", Colors.White, 22);
        _panelTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _panelTitle.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _panelHead.AddChild(_panelTitle);
        var close = new IconButton(IconKind.Close, "", s.T.T("ui.client.close"), new Vector2(36, 36));
        close.Pressed += () => Open(null);
        close.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _panelHead.AddChild(close);
        column.AddChild(Ui.Margin(_panelHead, 20, 14));
        var body = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        _panelBody = body;
        foreach (var sc in Screens) { sc.Visible = false; body.AddChild(sc); sc.SetAnchorsPreset(LayoutPreset.FullRect); }
        column.AddChild(body);
        _panel.AddChild(column);
        AddChild(_panel);
        ResizePanel();
    }

    private void ResizePanel()
    {
        if (_panel == null) return;
        float width = Math.Clamp(Size.X * (Current?.PreferredWidth ?? 0.62f), 560f, Math.Max(560f, Size.X - 32));
        _panel.OffsetRight = _panel.OffsetLeft + width;
        Map?.SetInset(_panel.Visible ? _panel.OffsetRight : 0);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) ResizePanel();
    }

    /// <summary>Shows a panel (or hides the current one when <paramref name="screen"/> is null or already open).</summary>
    public void Open(Screen? screen)
    {
        if (_panel == null) return;
        if (screen == Current) screen = null;
        bool wasOpen = _panel.Visible;
        Current = screen;
        if (screen != null) Map?.Select(null);
        foreach (var sc in Screens) sc.Visible = sc == screen;
        _panel.Visible = screen != null;
        if (screen != null && Session != null)
        {
            _panelTitle.Text = Session.T.T(screen.TitleKey);
            _panelBadge?.QueueFree();
            _panelBadge = new IconBadge(IconOf(screen), AppTheme.Accent, 36);
            _panelHead?.AddChild(_panelBadge);
            _panelHead?.MoveChild(_panelBadge, 0);
            screen.Refresh(Session);
            if (Animations && !wasOpen)
            {
                _panel.Modulate = new Color(1, 1, 1, 0);
                var tw = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
                tw.TweenProperty(_panel, "modulate:a", 1f, 0.16f);
                tw.TweenProperty(_panel, "offset_left", 16f, 0.2f).From(-8f);
            }
            else { _panel.Modulate = Colors.White; _panel.OffsetLeft = 16; }
        }
        foreach (var (sc, button) in _dock) { button.Active = sc == screen; button.QueueRedraw(); }
        ResizePanel();
    }

    /// <summary>Shows a panel without toggling (used by scripted runs).</summary>
    public void Show(Screen screen) { if (Current == screen) { if (Session != null) screen.Refresh(Session); } else Open(screen); }

    public void OpenScreen<T>() where T : Screen => Open(Screens.OfType<T>().FirstOrDefault() is { } sc && sc != Current ? sc : null);

    private static Control Divider()
    {
        var d = new ColorRect { Color = new Color(AppTheme.Border, 0.9f), CustomMinimumSize = new Vector2(1, 30), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        return d;
    }

    private void BuildHud(Session s)
    {
        var t = s.T;
        var bar = new PanelContainer();
        var style = AppTheme.Box(new Color(AppTheme.Header, 0.97f), 0);
        style.BorderColor = AppTheme.Border; style.BorderWidthBottom = 1;
        style.ShadowColor = new Color(0, 0, 0, 0.35f); style.ShadowSize = 16; style.ShadowOffset = new Vector2(0, 4);
        bar.AddThemeStyleboxOverride("panel", style);
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.OffsetBottom = TopBar;
        var row = Ui.HBox(8);
        var menu = new IconButton(IconKind.Menu, "", t.T("ui.client.menu"), new Vector2(40, 40));
        menu.Pressed += ShowStart;
        menu.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(menu);
        var save = new IconButton(IconKind.Save, "", t.T("ui.client.save"), new Vector2(40, 40));
        save.Pressed += () => s.Save("quicksave");
        save.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(save);
        row.AddChild(Ui.Gap(4));

        var dateBox = Ui.VBox(0);
        dateBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _date = Ui.Big("", Colors.White, 20);
        _turnInfo = Ui.Text("", AppTheme.Muted, AppTheme.Caption);
        dateBox.AddChild(_date); dateBox.AddChild(_turnInfo);
        dateBox.CustomMinimumSize = new Vector2(84, 0);
        row.AddChild(dateBox);
        row.AddChild(Divider());

        var chips = Ui.HBox(2);
        chips.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bool first = true;
        foreach (var (id, chart) in new[] { ("gdp", "gdp"), ("growth", "growth"), ("unemployment", "unemployment"), ("inflation", "inflation"), ("deficit", "deficit"), ("mood", "mood") })
        {
            if (!first) chips.AddChild(Divider());
            first = false;
            var chip = new HudChip { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var spec = Charts.All.First(c => c.Id == chart);
            chip.Pressed += () => ChartDialog.Open(this, s, spec);
            _chips[id] = chip;
            chips.AddChild(chip);
        }
        row.AddChild(chips);
        row.AddChild(Divider());
        if (s.HasPolitics)
        {
            _capital = new IconButton(IconKind.Coin, "", t.T("ui.party.capital"), new Vector2(100, 44)) { Round = true, IconColor = AppTheme.Warn };
            _capital.Pressed += () => OpenScreen<PartyScreen>();
            _capital.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(_capital);
        }
        var four = new IconButton(IconKind.FastForward, "", t.T("ui.client.four_quarters"), new Vector2(44, 44));
        four.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        four.Pressed += () =>
        {
            _batch = true;
            for (int i = 0; i < 4 && !s.Finished; i++) s.EndQuarter();
            _batch = false;
            ShowBriefing();
        };
        row.AddChild(four);
        var end = new IconButton(IconKind.Play, t.T("ui.client.end_quarter"), t.T("ui.client.end_quarter_hint"), new Vector2(120, 44)) { Primary = true, Round = true };
        end.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        end.Pressed += s.EndQuarter;
        row.AddChild(end);
        bar.AddChild(Ui.Margin(row, 12, 10));
        _hud = bar;
        AddChild(bar);
    }

    private void BuildDock(Session s)
    {
        var dock = new PanelContainer();
        var style = AppTheme.Glass(0.97f, 18);
        dock.AddThemeStyleboxOverride("panel", style);
        dock.SetAnchorsPreset(LayoutPreset.CenterBottom);
        var row = Ui.HBox(4);
        foreach (var sc in Screens)
        {
            var captured = sc;
            var button = new IconButton(IconOf(sc), s.T.T(sc.TitleKey), s.T.T(sc.TitleKey), new Vector2(92, 62));
            button.Pressed += () => Open(captured);
            _dock[sc] = button;
            row.AddChild(button);
        }
        dock.AddChild(Ui.Margin(row, 6));
        AddChild(dock);
        dock.GrowHorizontal = GrowDirection.Both; dock.GrowVertical = GrowDirection.Begin;
        dock.OffsetBottom = -14; dock.OffsetTop = -88;
    }

    private void BuildToast()
    {
        _toastBox = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        var box = AppTheme.Box(new Color(AppTheme.Surface, 0.98f), 12, new Color(AppTheme.Warn, 0.5f), 1, 12);
        box.BorderWidthLeft = 4; box.ContentMarginLeft = 18;
        box.ShadowColor = new Color(0, 0, 0, 0.4f); box.ShadowSize = 16;
        _toastBox.AddThemeStyleboxOverride("panel", box);
        _toast = Ui.Text("", AppTheme.Text, AppTheme.Body, true);
        _toast.CustomMinimumSize = new Vector2(420, 0);
        _toastBox.AddChild(_toast);
        _toastBox.SetAnchorsPreset(LayoutPreset.CenterTop);
        _toastBox.GrowHorizontal = GrowDirection.Both;
        _toastBox.OffsetTop = TopBar + 12;
        AddChild(_toastBox);
    }

    private static string Delta(double v, string fmt)
    {
        string s = Math.Abs(v).ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
        return s.Trim('0', '.').Length == 0 ? "" : (v > 0 ? "▲" : "▼") + s;
    }

    private void UpdateHud(Session s)
    {
        var t = s.T;
        _date.Text = s.ShortDateAt(s.Turn);
        _turnInfo.Text = $"{s.Turn} / {Session.HorizonTurns}";
        double Last(string key) { var x = s.History.Series(key); return x.Count > 0 ? x[^1] : double.NaN; }
        double Prev(string key) { var x = s.History.Series(key); return x.Count > 1 ? x[^2] : Last(key); }
        void Chip(string id, string captionKey, string key, double scale, string fmt, bool higherIsGood, Func<double, bool>? ok = null, string unit = "")
        {
            if (!_chips.TryGetValue(id, out var chip)) return;
            double v = Last(key), d = v - Prev(key);
            if (!double.IsFinite(v)) { chip.Visible = false; return; }
            bool good = ok?.Invoke(v) ?? (Math.Abs(d) < 1e-9 || (d > 0) == higherIsGood);
            chip.Set(t.T(captionKey), (v * scale).ToString(fmt, System.Globalization.CultureInfo.InvariantCulture) + unit, Delta(d * scale, fmt), good ? AppTheme.Good : AppTheme.Bad, t.T(captionKey));
        }
        Chip("gdp", "ui.hud.gdp", History.ValueKey("base:gdp"), 1e-12, "0.00", true, null, " tn");
        Chip("growth", "ui.hud.growth", History.ValueKey("base:real_growth"), 1, "0.0", true, v => v >= 0, " %");
        Chip("unemployment", "ui.hud.unemployment", History.ValueKey("base:unemployment"), 1, "0.0", false, null, " %");
        Chip("inflation", "ui.hud.inflation", History.ValueKey("base:inflation"), 1, "0.0", false, v => v <= 4, " %");
        Chip("deficit", "ui.hud.deficit", History.ValueKey("base:deficit_pct"), 1, "0.0", false, v => v <= 3, " %");
        Chip("mood", "ui.hud.mood", History.Mood, 1, "0.00", true, v => v >= 0);
        if (s.HasPolitics && s.Politics?.Party is { } ps)
        {
            _capital.Visible = true;
            _capital.SubCaption = $"{Fmt.Signed(ps.Income)} {t.T("ui.party.per_quarter")}";
            _capital.Caption = Fmt.Number(ps.PoliticalCapital);
            _capital.TooltipText = $"{t.T("ui.party.capital")}: {Fmt.Number(ps.PoliticalCapital)} ({Fmt.Signed(ps.Income)} {t.T("ui.party.per_quarter")})";
            _capital.QueueRedraw();
        }
    }

    private void OnChanged()
    {
        var s = Session;
        if (s == null || Map == null) return;
        UpdateHud(s);
        _toast.Text = s.Message;
        _toastBox.Visible = s.Message.Length > 0;
        Map.Setup(s);
        foreach (var screen in Screens) if (screen == Current) screen.Refresh(s);
        if (s.HasPolitics && Screens.OfType<PoliticsScreen>().FirstOrDefault() is { } pol && _dock.TryGetValue(pol, out var b)) { b.Badge = TurnPopup.HasDecisions(s); b.QueueRedraw(); }
        if (s.Finished && !_endShown) { _endShown = true; ShowEnd(s); }
    }

    private void OpenPoliticsTab() => OpenScreen<PoliticsScreen>();

    /// <summary>The start-of-quarter briefing: news and open decisions.</summary>
    public void ShowBriefing()
    {
        var s = Session;
        if (!PopupsEnabled || s == null || Map == null || s.Finished) return;
        if (_popup != null && IsInstanceValid(_popup)) _popup.QueueFree();
        _popup = null;
        if (!TurnPopup.HasContent(s)) return;
        _popup = new TurnPopup(s, OpenPoliticsTab);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        _popup.TreeExiting += () => { if (IsInstanceValid(dim)) dim.QueueFree(); };
        AddChild(_popup);
        _popup.PopupCentered();
    }

    private void ShowEnd(Session s)
    {
        var t = s.T;
        var f = s.Figures;
        double ratio = s.StartGdp > 0 && f.Gdp is { } g ? g / s.StartGdp : 1.0;
        var dialog = new AcceptDialog { Title = t.T("ui.end.heading") };
        dialog.DialogText = t.T("ui.end.summary", Session.HorizonTurns / s.Packs.GetScenario(s.ScenarioId).TurnsPerYear, t.T(Moods.Key(f.Mood)),
                Fmt.Number(ratio, "0.0"), Fmt.Number(f.DebtPercentOfGdp ?? 0, "0") + " %")
            + "\n\n" + t.T(f.Mood >= 0.2 ? "ui.end.good" : f.Mood >= -0.3 ? "ui.end.ok" : "ui.end.bad");
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(560, 240));
    }
}

/// <summary>A faint web of nodes behind the start screen, echoing the map of the game.</summary>
public partial class StartBackdrop : Control
{
    private readonly List<Vector2> _nodes = new();

    public StartBackdrop()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < 46; i++) _nodes.Add(new Vector2(rng.Randf(), rng.Randf()));
    }

    /// <summary>Fades the web out towards the middle, where the title and the form sit.</summary>
    private float Fade(Vector2 unit)
    {
        var d = (unit - new Vector2(0.5f, 0.5f)) * new Vector2(1.0f, 1.3f);
        return Mathf.SmoothStep(0.28f, 0.62f, d.Length());
    }

    public override void _Draw()
    {
        for (int i = 0; i < _nodes.Count; i++)
            for (int j = i + 1; j < _nodes.Count; j++)
            {
                var a = _nodes[i] * Size; var b = _nodes[j] * Size;
                float d = a.DistanceTo(b);
                float f = Math.Min(Fade(_nodes[i]), Fade(_nodes[j]));
                if (d < Size.X * 0.2f && f > 0.01f) DrawLine(a, b, new Color(AppTheme.Accent, 0.16f * f * (1 - d / (Size.X * 0.2f))), 1.2f, true);
            }
        for (int i = 0; i < _nodes.Count; i++)
        {
            var p = _nodes[i] * Size;
            float f = Fade(_nodes[i]);
            if (f < 0.01f) continue;
            var color = i % 3 == 0 ? AppTheme.Tax : i % 3 == 1 ? AppTheme.Spend : AppTheme.Regulation;
            DrawCircle(p, 7f, new Color(color, 0.12f * f)); DrawCircle(p, 3.2f, new Color(color, 0.55f * f));
        }
    }

    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
}
