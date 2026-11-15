using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The window. A running game is laid out like Democracy 4: a white top bar (key figures, term clock, square tabs, green Play), the
/// chrome Popularity panel on the left and the map filling the rest. Policies, indicators and every other screen open as dialogs over
/// the washed-out map. Talks to the core only through <see cref="Session"/>.
/// </summary>
public partial class Main : Control
{
    public PackSet? Packs { get; private set; }
    public Session? Session { get; private set; }
    public List<Screen> Screens { get; } = new();
    public WorldMap? Map { get; private set; }
    public Screen? Current { get; private set; }

    private string _language = "en";
    private bool _endShown, _batch, _inGame;
    private TopBar? _top;
    private Sidebar? _sidebar, _drawer;
    private Control _mapHost = new();
    private Control _dialogs = new();
    private PanelContainer _toast = new();
    private Label _toastText = new();
    private PanelContainer? _lensChip;
    private string _lastMessage = "";
    private int _newsSeen;
    private (string Kind, string Id)? _detail;
    private Dialog? _detailDialog, _screenDialog;

    /// <summary>The start-of-quarter briefing; the smoke test turns it off so it never blocks a scripted run.</summary>
    public bool PopupsEnabled { get; set; } = true;
    /// <summary>Animations; off for scripted runs so screenshots never catch a half-drawn frame.</summary>
    public static bool Animations { get; set; } = true;
    public string Language => _language;

    public override void _Ready()
    {
        Theme = AppTheme.Instance;
        GetTree().Root.Theme = AppTheme.Instance;
        _language = OS.GetLocale().StartsWith("cs", StringComparison.OrdinalIgnoreCase) ? "cs" : "en";
        Ui.Czech = _language == "cs";
        var loaded = Session.LoadPacks(Session.DefaultPacksDir());
        if (!loaded.Ok)
        {
            ShowFatal("Packs could not be loaded from " + Session.DefaultPacksDir() + ":\n" + string.Join("\n", loaded.Diagnostics));
            return;
        }
        Packs = loaded.Packs;
        var args = OS.GetCmdlineUserArgs();
        DisplaySettings.Load();
        var scaleArg = Array.IndexOf(args, "--ui-scale");
        if (scaleArg >= 0 && scaleArg + 1 < args.Length && float.TryParse(args[scaleArg + 1], System.Globalization.CultureInfo.InvariantCulture, out var forced))
            DisplaySettings.SetScale(GetWindow(), forced, save: false);
        else if (args.Contains("--smoke")) DisplaySettings.SetScale(GetWindow(), 1.0f, save: false);
        DisplaySettings.Apply(GetWindow(), sizeWindow: !args.Contains("--smoke"));
        GetTree().Root.SizeChanged += () => DisplaySettings.Refit(GetWindow());
        Layout.Update(Size);
        Layout.Changed += OnLayoutClass;
        DisplaySettings.Changed += () => { if (!_inGame) return; Session?.Touch(); };
        if (args.Contains("--smoke"))
        {
            PopupsEnabled = false; Animations = false;
            var resArg = Array.IndexOf(args, "--res");
            if (resArg >= 0 && resArg + 1 < args.Length && args[resArg + 1].Split('x') is { Length: 2 } wh && int.TryParse(wh[0], out var rw) && int.TryParse(wh[1], out var rh))
            {
                // A tiling window manager may resize the real window, so screenshots render into an offscreen viewport of exactly the
                // requested window size, scaled to its logical size the same way the window would be (canvas_items + the UI factor).
                var req = new Vector2I(rw, rh);
                var logical = DisplaySettings.LogicalSize(req);
                var vp = new SubViewport
                {
                    Size = req, Size2DOverride = new Vector2I((int)Math.Round(logical.X), (int)Math.Round(logical.Y)), Size2DOverrideStretch = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always, GuiEmbedSubwindows = true, TransparentBg = false,
                };
                GetTree().Root.CallDeferred(Node.MethodName.AddChild, vp);
                Callable.From(() => { GetParent().RemoveChild(this); vp.AddChild(this); SetAnchorsPreset(LayoutPreset.FullRect); Size = vp.Size2DOverride; }).CallDeferred();
                Smoke.RequestedSize = req;
            }
            var shots = Array.IndexOf(args, "--screenshots");
            _ = Smoke.Run(this, shots >= 0 && shots + 1 < args.Length ? args[shots + 1] : null);
            return;
        }
        ShowStart();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) Layout.Update(Size);
        else if (what == NotificationWMCloseRequest && !OS.GetCmdlineUserArgs().Contains("--smoke")) DisplaySettings.RememberWindow(GetWindow());
    }

    private void OnLayoutClass()
    {
        if (_inGame && Session != null)
        {
            var current = Current; var detail = _detail;
            BuildGameUi();
            if (current != null) Open(current);
            if (detail is { } d) OpenDetail(d.Kind, d.Id);
        }
        else if (!_inGame && Packs != null && GetChildCount() > 0 && _startShown) ShowStart();
    }

    private bool _startShown;

    private void Reset()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        Screens.Clear();
        Map = null; Current = null; _top = null; _sidebar = null; _drawer = null; _detail = null; _detailDialog = null; _screenDialog = null; _lensChip = null;
        var bg = new ColorRect { Color = AppTheme.MapBg, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
    }

    private void ShowFatal(string text)
    {
        Reset();
        var l = Ui.L(text, Tx.Body, AppTheme.BadText, true);
        AddChild(Ui.Margin(l, 24));
        GD.PrintErr(text);
    }

    // ---- start ----------------------------------------------------------------------------------------

    public void ShowStart()
    {
        _inGame = false;
        Reset();
        _startShown = true;
        Ui.Czech = _language == "cs";
        StartScreen.Build(this, Packs!, _language, lang => { _language = lang; ShowStart(); });
        _dialogs = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _dialogs.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_dialogs);
    }

    public void StartGame(string scenarioId, ulong seed, string language)
    {
        _language = language;
        Ui.Czech = language == "cs";
        Session = new Session(Packs!, scenarioId, seed, language);
        _newsSeen = 0;
        _lastMessage = "";
        BuildGameUi();
        if (PopupsEnabled) TurnPopup.Open(this, Session, welcome: true);
    }

    public void LoadGame(string file)
    {
        try
        {
            var save = SaveData.FromYaml(System.IO.File.ReadAllText(file));
            Session = new Session(Packs!, save.ScenarioId, save.Seed, _language);
            if (!Session.Load(file)) { ShowStart(); return; }
            _newsSeen = Session.News.Count;
            BuildGameUi();
        }
        catch (Exception e) { GD.PrintErr(e.ToString()); ShowStart(); }
    }

    // ---- the frame ----------------------------------------------------------------------------------------

    private void BuildGameUi()
    {
        var s = Session!;
        if (_inGame) s.Changed -= OnChanged;
        Reset();
        _inGame = true; _startShown = false;
        _endShown = s.Finished;

        Screens.Add(new VotersScreen());
        Screens.Add(new ChartsScreen());
        Screens.Add(new BudgetScreen());
        if (s.HasPolitics) { Screens.Add(new PoliticsScreen()); Screens.Add(new PartyScreen()); }
        Screens.Add(new NewsScreen());

        var column = Ui.VBox(0);
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        _top = new TopBar(this, s);
        column.AddChild(_top);
        var body = Ui.HBox(0);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        _sidebar = new Sidebar(this, Layout.Compact) { CustomMinimumSize = new Vector2(Layout.Sidebar, 0) };
        body.AddChild(_sidebar);
        _mapHost = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        Map = new WorldMap();
        Map.SetAnchorsPreset(LayoutPreset.FullRect);
        Map.NodeClicked += id => OpenDetail(s.Packs.Policies.Any(p => p.Id == id) ? "policy" : "value", id);
        Map.HubClicked += () => { if (s.HasPolitics) OpenScreen<PartyScreen>(); };
        _mapHost.AddChild(Map);
        Map.Setup(s);
        BuildToast();
        body.AddChild(_mapHost);
        column.AddChild(body);
        AddChild(column);

        _dialogs = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _dialogs.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_dialogs);

        s.Changed += OnChanged;
        s.QuarterEnded -= OnQuarterEnded;
        s.QuarterEnded += OnQuarterEnded;
        OnChanged();
    }

    private void OnQuarterEnded() { if (!_batch) ShowBriefing(); }

    private void BuildToast()
    {
        _toast = new PanelContainer { ThemeTypeVariation = "Toast", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _toastText = Ui.L("", Tx.Label, Colors.White, false);
        _toastText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _toastText.CustomMinimumSize = new Vector2(Layout.Compact ? 320 : 460, 0);
        _toast.AddChild(_toastText);
        _toast.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _toast.GrowHorizontal = GrowDirection.Both; _toast.GrowVertical = GrowDirection.Begin;
        _toast.OffsetBottom = -20;
        _mapHost.AddChild(_toast);
    }

    private SceneTreeTimer? _toastTimer;

    private void ShowToast(string text)
    {
        _toastText.Text = text;
        _toast.Visible = text.Length > 0;
        _toast.ResetSize();
        _toast.OffsetTop = _toast.OffsetBottom - _toast.GetCombinedMinimumSize().Y;
        if (text.Length == 0) return;
        _toastTimer = GetTree().CreateTimer(4.5);
        var mine = _toastTimer;
        mine.Timeout += () => { if (mine == _toastTimer && IsInstanceValid(_toast)) _toast.Visible = false; };
    }

    public void RefreshSidebars()
    {
        if (Session == null) return;
        _sidebar?.Update(Session);
        if (_drawer != null && IsInstanceValid(_drawer)) _drawer.Update(Session);
    }

    public void ToggleSidebarDrawer()
    {
        if (_drawer != null && IsInstanceValid(_drawer)) { _drawer.GetParent()?.QueueFree(); _drawer = null; return; }
        var holder = new Control { MouseFilter = MouseFilterEnum.Stop };
        holder.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) ToggleSidebarDrawer(); };
        _drawer = new Sidebar(this, false);
        _drawer.SetAnchorsPreset(LayoutPreset.LeftWide);
        _drawer.OffsetRight = 260;
        holder.AddChild(_drawer);
        _mapHost.AddChild(holder);
        _drawer.Update(Session!);
    }

    /// <summary>Turns the map into a voter group's lens (null = off).</summary>
    public void SetLens(string? groupId)
    {
        if (Map == null || Session == null) return;
        Map.SetLens(groupId);
        _lensChip?.QueueFree(); _lensChip = null;
        if (groupId != null)
        {
            var t = Session.T;
            var row = Ui.HBox(10);
            row.AddChild(new IconView(Icons.For(groupId), 18, Colors.White));
            row.AddChild(Ui.L(t.T("ui.map.lens", t.Name(groupId)), Tx.Label, Colors.White));
            var details = Ui.Btn(t.T("ui.map.details"), () => OpenDetail("group", groupId), "ButtonSecondary", null, null, false, 28);
            row.AddChild(details);
            var close = Ui.Btn("", () => SetLens(null), "", IconKind.Close, t.T("ui.client.close"), false, 28);
            close.AddThemeStyleboxOverride("normal", AppTheme.Box(AppTheme.ChromeDark, 0, null, 1, 6, 4));
            row.AddChild(close);
            _lensChip = Ui.Panel(row, "ChromeBox", false);
            _lensChip.Position = new Vector2(12, 12);
            _mapHost.AddChild(_lensChip);
        }
        RefreshSidebars();
    }

    // ---- dialogs ------------------------------------------------------------------------------------------

    public Dialog NewDialog(string title, IconKind? icon, float width)
    {
        var d = new Dialog(title, icon, Session?.T.T("ui.client.close") ?? "Close") { PreferredWidth = width };
        _dialogs.AddChild(d);
        return d;
    }

    public void CloseDialogs() { foreach (var c in _dialogs.GetChildren()) if (c is Dialog d) d.Close(); }

    /// <summary>Opens a screen as a large dialog (null closes everything and shows the map).</summary>
    public void Open(Screen? screen)
    {
        if (Session == null) return;
        CloseDialogs();
        Current = screen;
        _top?.SetActive(screen?.TabId ?? "map");
        if (screen == null) return;
        var d = NewDialog(Session.T.T(screen.TitleKey), screen.Icon, screen.Width);
        d.FillHeight = screen.Fill;
        d.Key = "screen";
        _screenDialog = d;
        d.Closed += () => { if (Current == screen) { Current = null; _top?.SetActive("map"); } };
        screen.Build(Session, d, this);
        d.Refit();
        if (screen is NewsScreen) _newsSeen = Session.News.Count;
        UpdateBadges();
    }

    /// <summary>Shows a screen without toggling (scripted runs): rebuilds it when it is already open.</summary>
    public void Show(Screen screen) { if (Current == screen) RebuildScreen(); else Open(screen); }

    public void OpenScreen<T>() where T : Screen { if (Screens.OfType<T>().FirstOrDefault() is { } sc) { if (sc == Current) Open(null); else Open(sc); } }

    private void RebuildScreen()
    {
        if (Current == null || _screenDialog == null || !IsInstanceValid(_screenDialog) || Session == null) return;
        var d = _screenDialog;
        var scroll = d.Body.GetParent()?.GetParent() as ScrollContainer;
        int v = scroll?.ScrollVertical ?? 0;
        Ui.Clear(d.Body); Ui.Clear(d.Fixed); Ui.Clear(d.HeaderExtra); d.ClearActions();
        Current.Build(Session, d, this);
        if (scroll != null) Callable.From(() => { if (IsInstanceValid(scroll)) scroll.ScrollVertical = v; }).CallDeferred();
    }

    /// <summary>Opens the dialog of a policy, indicator, voter group or party.</summary>
    public void OpenDetail(string kind, string id)
    {
        if (Session == null) return;
        if (_detailDialog != null && IsInstanceValid(_detailDialog)) _detailDialog.Close();
        _detail = (kind, id);
        Dialog d = kind switch
        {
            "policy" => PolicyDialog.Open(this, Session, id),
            "value" => ValueDialog.Open(this, Session, id),
            "group" => GroupDialog.Open(this, Session, id),
            _ => PartyDialog.Open(this, Session, id),
        };
        _detailDialog = d;
        d.Closed += () => { if (_detailDialog == d) { _detail = null; _detailDialog = null; Map?.Select(null); } };
    }

    public void OpenChart(ChartSpec spec) { if (Session != null) ChartDialog.Open(this, Session, spec); }

    public void ShowMenu(Control anchor)
    {
        var s = Session; if (s == null) return;
        var t = s.T;
        var menu = new PopupMenu();
        menu.AddItem(t.T("ui.client.save") + "   Ctrl+S", 0);
        var saves = Session.SaveDirFiles().ToList();
        var load = new PopupMenu { Name = "load" };
        for (int i = 0; i < saves.Count; i++) load.AddItem(System.IO.Path.GetFileName(saves[i]).Replace(".save.yaml", ""), i);
        load.IdPressed += i => LoadGame(saves[(int)i]);
        menu.AddChild(load);
        menu.AddSubmenuNodeItem(t.T("ui.client.load"), load, 1);
        if (saves.Count == 0) menu.SetItemDisabled(1, true);
        menu.AddItem(t.T("ui.start.display"), 2);
        menu.AddItem(t.T("ui.menu.fullscreen") + "   F11", 3);
        menu.AddSeparator();
        menu.AddItem(t.T("ui.menu.quit"), 4);
        menu.IdPressed += id =>
        {
            switch (id)
            {
                case 0: s.Save("quicksave"); break;
                case 2: ShowDisplaySettings(); break;
                case 3: DisplaySettings.ToggleFullscreen(GetWindow()); break;
                case 4: ShowStart(); break;
            }
        };
        AddChild(menu);
        var r = anchor.GetGlobalRect();
        menu.Popup(new Rect2I((int)(r.End.X - 260), (int)r.End.Y, 260, 10));
        menu.PopupHide += () => menu.QueueFree();
    }

    public void ShowDisplaySettings()
    {
        var t = Session?.T ?? new Localizer(Packs!, _language);
        var d = new Dialog(t.T("ui.start.display"), IconKind.Gear, t.T("ui.client.close")) { PreferredWidth = 520 };
        _dialogs.AddChild(d);
        d.Body.AddChild(StartScreen.DisplayCard(this, t, false));
        d.Refit();
    }

    // ---- turn ---------------------------------------------------------------------------------------------

    /// <summary>A decision that must be answered before time moves on (an offer to join a coalition).</summary>
    public static bool Blocking(Session s) => s.Politics?.Formation?.Offer != null;

    public void EndQuarterPressed()
    {
        var s = Session; if (s == null || s.Finished) return;
        if (Blocking(s)) { ShowBriefing(force: true); return; }
        CloseDialogs();
        s.EndQuarter();
    }

    public void PlayFour()
    {
        var s = Session; if (s == null) return;
        if (Blocking(s)) { ShowBriefing(force: true); return; }
        CloseDialogs();
        _batch = true;
        for (int i = 0; i < 4 && !s.Finished && !Blocking(s); i++) s.EndQuarter();
        _batch = false;
        ShowBriefing();
    }

    private void UpdateBadges()
    {
        var s = Session; if (s == null || _top == null) return;
        _top.SetBadge("politics", TurnPopup.DecisionCount(s));
        _top.SetBadge("news", Math.Max(0, s.News.Count - _newsSeen));
        _top.SetPlayBlocked(Blocking(s), Blocking(s) ? s.T.T("ui.hud.decide_first") : s.T.T("ui.client.end_quarter") + "\n" + s.T.T("ui.client.end_quarter_hint"));
    }

    private void OnChanged()
    {
        var s = Session;
        if (s == null || Map == null) return;
        _top?.Update(s);
        RefreshSidebars();
        Map.Setup(s);
        if (s.Message != _lastMessage) { _lastMessage = s.Message; ShowToast(s.Message); }
        RebuildScreen();
        UpdateBadges();
        if (s.Finished && !_endShown) { _endShown = true; ShowEnd(s); }
    }

    /// <summary>The start-of-quarter briefing (news, decisions, key indicators).</summary>
    public void ShowBriefing(bool force = false)
    {
        var s = Session;
        if (s == null || Map == null || s.Finished) return;
        if (!PopupsEnabled && !force) return;
        foreach (var c in _dialogs.GetChildren()) if (c is Dialog { Key: "briefing" } old) old.Close();
        if (!force && !TurnPopup.HasContent(s)) return;
        TurnPopup.Open(this, s);
    }

    private void ShowEnd(Session s)
    {
        var t = s.T;
        var f = s.Figures;
        double ratio = s.StartGdp > 0 && f.Gdp is { } g ? g / s.StartGdp : 1.0;
        var d = NewDialog(t.T("ui.end.heading"), IconKind.Star, 620);
        d.Body.AddChild(Ui.L(t.T("ui.end.summary", Session.HorizonTurns / s.Packs.GetScenario(s.ScenarioId).TurnsPerYear, t.T(Moods.Key(f.Mood)),
            Ui.Num(ratio, "0.0"), Ui.Num(f.DebtPercentOfGdp ?? 0, "0") + " %"), Tx.Body, AppTheme.Ink, true));
        d.Body.AddChild(Ui.L(t.T(f.Mood >= 0.2 ? "ui.end.good" : f.Mood >= -0.3 ? "ui.end.ok" : "ui.end.bad"), Tx.BodyStrong, AppTheme.Ink, true));
        d.AddAction(t.T("ui.menu.quit"), IconKind.Back, ShowStart);
        d.AddAction(t.T("ui.brief.to_map"), IconKind.Map, d.Close, false, "");
        d.Refit();
    }

    // ---- keyboard -----------------------------------------------------------------------------------------

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.F11) { DisplaySettings.ToggleFullscreen(GetWindow()); GetViewport().SetInputAsHandled(); return; }
        if (!_inGame || Session == null) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;
        var top = _dialogs.GetChildren().OfType<Dialog>().LastOrDefault(d => !d.IsQueuedForDeletion());
        switch (k.Keycode)
        {
            case Key.Escape:
                if (top != null) { if (!top.Blocking) top.Close(); }
                else if (Map?.LensGroup != null) SetLens(null);
                else Map?.Select(null);
                break;
            case Key.S when k.CtrlPressed: Session.Save("quicksave"); break;
            case Key.M: Open(null); break;
            case Key.Space when top == null: EndQuarterPressed(); break;
            case >= Key.Key1 and <= Key.Key9:
                int i = (int)(k.Keycode - Key.Key1);
                if (i < Screens.Count) Open(Screens[i]);
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
}

/// <summary>A screen that opens as a large dialog from a top-bar tab. Rebuilt from the session whenever the game changes.</summary>
public abstract class Screen
{
    public abstract string TitleKey { get; }
    public abstract IconKind Icon { get; }
    public abstract string TabId { get; }
    public virtual float Width => 1040;
    public virtual bool Fill => true;
    public abstract void Build(Session s, Dialog d, Main main);
}
