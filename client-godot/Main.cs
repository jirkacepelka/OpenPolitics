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
        Theme = AppTheme.Build();
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
            PopupsEnabled = false;
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
                Gradient = new Gradient { Colors = new[] { new Color("14224a"), new Color("0a1226"), AppTheme.Bg }, Offsets = new[] { 0f, 0.55f, 1f } },
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
        var column = Ui.VBox(18);
        column.CustomMinimumSize = new Vector2(580, 0);
        var title = Ui.Big("OpenPolitics", Colors.White, 60);
        column.AddChild(title);
        column.AddChild(Ui.Text(t.T("ui.client.tagline"), AppTheme.Muted, 18, true));

        var form = Ui.VBox(14);
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

        var packThreshold = Packs.ElectoralSystems.FirstOrDefault()?.Threshold ?? 0.05;
        var threshold = new SpinBox { MinValue = 0, MaxValue = GameOptions.MaxThreshold * 100, Step = 0.5, Value = packThreshold * 100, Suffix = "%", TooltipText = t.T("ui.client.threshold_hint") };
        Field(form, t.T("ui.client.threshold"), threshold);
        form.AddChild(Ui.Text(t.T("ui.client.threshold_hint"), AppTheme.Muted, 13, true));

        var start = new IconButton(IconKind.Play, t.T("ui.client.start"), "", new Vector2(0, 56)) { Primary = true, Round = true };
        start.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        start.Pressed += () =>
        {
            var scenario = scenarios[scenarioBox.Selected];
            double pct = threshold.Value;
            var options = scenario.Politics != null && Math.Abs(pct / 100.0 - packThreshold) > 1e-9 ? new GameOptions(pct / 100.0) : null;
            StartGame(scenario.Id, (ulong)seed.Value, _language, options);
        };
        form.AddChild(start);
        var formCard = Ui.Card(form);
        formCard.AddThemeStyleboxOverride("panel", AppTheme.Glass(0.92f, 18));
        column.AddChild(formCard);

        var saves = Session.SaveDirFiles().ToList();
        if (saves.Count > 0)
        {
            var saveBox = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var f in saves) saveBox.AddItem(System.IO.Path.GetFileName(f));
            var row = Ui.HBox(); row.AddChild(saveBox);
            row.AddChild(Ui.Btn(t.T("ui.client.load"), () => LoadGame(saves[saveBox.Selected])));
            var loadCard = Ui.VBox(8);
            loadCard.AddChild(Ui.Text(t.T("ui.client.saved_games"), AppTheme.Muted));
            loadCard.AddChild(row);
            column.AddChild(Ui.Card(loadCard));
        }
        column.AddChild(Ui.Text(t.T("ui.client.illustrative"), AppTheme.Warn, 13, true));
        center.AddChild(column);
        AddChild(center);
    }

    private static void Field(VBoxContainer form, string label, Control input)
    {
        var row = Ui.HBox(12);
        var l = Ui.Text(label, AppTheme.Muted);
        l.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(l);
        input.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(input);
        form.AddChild(row);
    }

    // ---- game -------------------------------------------------------------------------------------------

    public void StartGame(string scenarioId, ulong seed, string language, GameOptions? options = null)
    {
        _language = language;
        Session = new Session(Packs!, scenarioId, seed, language, options);
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

    private void BuildPanel(Session s)
    {
        _panel = new PanelContainer { Visible = false };
        _panel.AddThemeStyleboxOverride("panel", AppTheme.Glass(0.975f, 18));
        _panel.SetAnchorsPreset(LayoutPreset.LeftWide);
        _panel.OffsetLeft = 18; _panel.OffsetTop = 100; _panel.OffsetBottom = -108;
        var column = Ui.VBox(6);
        var head = Ui.HBox(8);
        _panelTitle = Ui.Big("", Colors.White, 20);
        _panelTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(Ui.Margin(_panelTitle, 6));
        var close = Ui.Btn("✕", () => Open(null), s.T.T("ui.client.close"));
        close.CustomMinimumSize = new Vector2(40, 36);
        head.AddChild(close);
        column.AddChild(head);
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
        float width = Math.Clamp(Size.X * (Current?.PreferredWidth ?? 0.62f), 560f, 1500f);
        _panel.OffsetRight = _panel.OffsetLeft + width;
        Map?.SetInset(_panel.Visible && (Current?.PreferredWidth ?? 1f) <= 0.65f ? _panel.OffsetRight : 0);
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
        Current = screen;
        if (screen != null) Map?.Select(null);
        foreach (var sc in Screens) sc.Visible = sc == screen;
        _panel.Visible = screen != null;
        if (screen != null && Session != null) { _panelTitle.Text = Session.T.T(screen.TitleKey); screen.Refresh(Session); }
        foreach (var (sc, button) in _dock) { button.Active = sc == screen; button.QueueRedraw(); }
        ResizePanel();
    }

    /// <summary>Shows a panel without toggling (used by scripted runs).</summary>
    public void Show(Screen screen) { if (Current == screen) { if (Session != null) screen.Refresh(Session); } else Open(screen); }

    public void OpenScreen<T>() where T : Screen => Open(Screens.OfType<T>().FirstOrDefault() is { } sc && sc != Current ? sc : null);

    private void BuildHud(Session s)
    {
        var t = s.T;
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", AppTheme.Glass(0.9f, 18));
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.OffsetLeft = 14; bar.OffsetRight = -14; bar.OffsetTop = 12; bar.OffsetBottom = 84;
        var row = Ui.HBox(8);
        var menu = new IconButton(IconKind.Menu, "", t.T("ui.client.menu"), new Vector2(42, 50)) { Round = true };
        menu.Pressed += ShowStart;
        row.AddChild(menu);
        var save = new IconButton(IconKind.Save, "", t.T("ui.client.save"), new Vector2(42, 50)) { Round = true };
        save.Pressed += () => s.Save("quicksave");
        row.AddChild(save);

        var dateBox = Ui.VBox(0);
        _date = Ui.Big("", Colors.White, 22);
        _turnInfo = Ui.Text("", AppTheme.Muted, 12);
        dateBox.AddChild(_date); dateBox.AddChild(_turnInfo);
        var dateMargin = Ui.Margin(dateBox, 4); dateMargin.CustomMinimumSize = new Vector2(88, 0);
        row.AddChild(dateMargin);

        foreach (var (id, chart) in new[] { ("gdp", "gdp"), ("growth", "growth"), ("unemployment", "unemployment"), ("inflation", "inflation"), ("deficit", "deficit"), ("mood", "mood") })
        {
            var chip = new HudChip();
            var spec = Charts.All.First(c => c.Id == chart);
            chip.Pressed += () => ChartDialog.Open(this, s, spec);
            _chips[id] = chip;
                        row.AddChild(chip);
        }
        row.AddChild(Ui.Spacer());
        if (s.HasPolitics)
        {
            _capital = new IconButton(IconKind.Coin, "", t.T("ui.party.capital"), new Vector2(124, 50)) { Round = true };
            _capital.Pressed += () => OpenScreen<PartyScreen>();
            row.AddChild(_capital);
        }
        var four = new IconButton(IconKind.FastForward, "", t.T("ui.client.four_quarters"), new Vector2(48, 50)) { Round = true };
        four.Pressed += () =>
        {
            _batch = true;
            for (int i = 0; i < 4 && !s.Finished; i++) s.EndQuarter();
            _batch = false;
            ShowBriefing();
        };
        row.AddChild(four);
        var end = new IconButton(IconKind.Play, t.T("ui.client.end_quarter"), t.T("ui.client.end_quarter_hint"), new Vector2(156, 50)) { Primary = true, Round = true };
        end.Pressed += s.EndQuarter;
        row.AddChild(end);
        bar.AddChild(Ui.Margin(row, 6));
        _hud = bar;
        AddChild(bar);
    }

    private void BuildDock(Session s)
    {
        var dock = new PanelContainer();
        dock.AddThemeStyleboxOverride("panel", AppTheme.Glass(0.92f, 22));
        dock.SetAnchorsPreset(LayoutPreset.CenterBottom);
        var row = Ui.HBox(8);
        IconKind Icon(Screen sc) => sc switch
        {
            OverviewScreen => IconKind.Overview, PoliciesScreen => IconKind.Policies, VotersScreen => IconKind.Voters,
            PoliticsScreen => IconKind.Politics, PartyScreen => IconKind.Party, _ => IconKind.News,
        };
        foreach (var sc in Screens)
        {
            var captured = sc;
            var button = new IconButton(Icon(sc), s.T.T(sc.TitleKey), s.T.T(sc.TitleKey));
            button.Pressed += () => Open(captured);
            _dock[sc] = button;
            row.AddChild(button);
        }
        dock.AddChild(Ui.Margin(row, 8));
        AddChild(dock);
        dock.GrowHorizontal = GrowDirection.Both; dock.GrowVertical = GrowDirection.Begin;
        dock.OffsetBottom = -14; dock.OffsetTop = -96;
    }

    private void BuildToast()
    {
        _toastBox = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _toastBox.AddThemeStyleboxOverride("panel", AppTheme.Box(new Color(AppTheme.Header, 0.95f), 12, AppTheme.Warn, 1, 12));
        _toast = Ui.Text("", AppTheme.Warn, 15, true);
        _toast.CustomMinimumSize = new Vector2(420, 0);
        _toastBox.AddChild(_toast);
        _toastBox.SetAnchorsPreset(LayoutPreset.CenterTop);
        _toastBox.GrowHorizontal = GrowDirection.Both;
        _toastBox.OffsetTop = 96;
        AddChild(_toastBox);
    }

    private static string Delta(double v, string fmt) => Math.Abs(v) < 1e-9 ? "" : (v > 0 ? "▲ " : "▼ ") + Math.Abs(v).ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

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
            _capital.Caption = $"{Fmt.Number(ps.PoliticalCapital)} {Fmt.Signed(ps.Income)}";
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

    public override void _Draw()
    {
        for (int i = 0; i < _nodes.Count; i++)
            for (int j = i + 1; j < _nodes.Count; j++)
            {
                var a = _nodes[i] * Size; var b = _nodes[j] * Size;
                float d = a.DistanceTo(b);
                if (d < Size.X * 0.2f) DrawLine(a, b, new Color(AppTheme.Accent, 0.12f * (1 - d / (Size.X * 0.2f))), 1.2f, true);
            }
        for (int i = 0; i < _nodes.Count; i++)
        {
            var p = _nodes[i] * Size;
            var color = i % 3 == 0 ? AppTheme.Tax : i % 3 == 1 ? AppTheme.Spend : AppTheme.Regulation;
            DrawCircle(p, 6f, new Color(color, 0.10f)); DrawCircle(p, 3f, new Color(color, 0.45f));
        }
    }

    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
}
