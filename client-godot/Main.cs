using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>The window: start screen, then header + tabs for a running game. Talks to the core only through <see cref="Session"/>.</summary>
public partial class Main : Control
{
    public PackSet? Packs { get; private set; }
    public Session? Session { get; private set; }
    public TabContainer? Tabs { get; private set; }
    public List<Screen> Screens { get; } = new();
    private Label _date = new(), _status = new();
    private Button _end = new(), _four = new();
    private string _language = "en";
    private bool _endShown;

    public override void _Ready()
    {
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
            var shots = Array.IndexOf(args, "--screenshots");
            _ = Smoke.Run(this, shots >= 0 && shots + 1 < args.Length ? args[shots + 1] : null);
            return;
        }
        ShowStart();
    }

    private void Reset()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        Screens.Clear();
        Tabs = null;
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
        var center = new CenterContainer(); center.SetAnchorsPreset(LayoutPreset.FullRect);
        var box = Ui.VBox(10);
        box.CustomMinimumSize = new Vector2(520, 0);
        box.AddChild(Ui.Text("OpenPolitics", Ui.Accent, 40));
        box.AddChild(Ui.Text(t.T("ui.client.tagline"), Ui.Muted, 16, true));
        box.AddChild(Ui.Text(t.T("ui.client.illustrative"), Ui.Warn, 14, true));

        var lang = new OptionButton();
        lang.AddItem("Čeština", 0); lang.AddItem("English", 1);
        lang.Selected = _language == "cs" ? 0 : 1;
        lang.ItemSelected += i => { _language = i == 0 ? "cs" : "en"; ShowStart(); };
        var langRow = Ui.HBox(); langRow.AddChild(Ui.Text(t.T("ui.client.language"))); langRow.AddChild(lang);
        box.AddChild(langRow);

        var scenarios = Packs!.Scenarios.OrderByDescending(s => s.Politics != null).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        var scenarioBox = new OptionButton();
        foreach (var s in scenarios) scenarioBox.AddItem(t.T("name." + s.Id));
        var scRow = Ui.HBox(); scRow.AddChild(Ui.Text(t.T("ui.client.scenario"))); scRow.AddChild(scenarioBox);
        box.AddChild(scRow);

        var seed = new SpinBox { MinValue = 1, MaxValue = 999999, Step = 1, Value = (Time.GetTicksMsec() % 99999) + 1 };
        var seedRow = Ui.HBox(); seedRow.AddChild(Ui.Text(t.T("ui.client.seed"))); seedRow.AddChild(seed);
        box.AddChild(seedRow);

        var start = Ui.Btn(t.T("ui.client.start"), () => StartGame(scenarios[scenarioBox.Selected].Id, (ulong)seed.Value, _language));
        start.CustomMinimumSize = new Vector2(0, 44);
        box.AddChild(start);

        var saves = Session.SaveDirFiles().ToList();
        if (saves.Count > 0)
        {
            var saveBox = new OptionButton();
            foreach (var f in saves) saveBox.AddItem(System.IO.Path.GetFileName(f));
            var row = Ui.HBox(); row.AddChild(saveBox);
            row.AddChild(Ui.Btn(t.T("ui.client.load"), () => LoadGame(saves[saveBox.Selected])));
            box.AddChild(Ui.Text(t.T("ui.client.saved_games"), Ui.Muted));
            box.AddChild(row);
        }
        center.AddChild(box);
        AddChild(center);
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
        var root = Ui.VBox(0); root.SetAnchorsPreset(LayoutPreset.FullRect);

        var header = Ui.HBox(12);
        _date = Ui.Text("", null, 20);
        header.AddChild(Ui.Margin(_date, 8));
        _status = Ui.Text("", Ui.Warn, 15, true);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.CustomMinimumSize = new Vector2(100, 0);
        header.AddChild(_status);
        header.AddChild(Ui.Btn(s.T.T("ui.client.save"), () => s.Save("quicksave")));
        header.AddChild(Ui.Btn(s.T.T("ui.client.menu"), ShowStart));
        _four = Ui.Btn(s.T.T("ui.client.four_quarters"), () => { for (int i = 0; i < 4 && !s.Finished; i++) s.EndQuarter(); });
        _end = Ui.Btn(s.T.T("ui.client.end_quarter"), s.EndQuarter, s.T.T("ui.client.end_quarter_hint"));
        _end.CustomMinimumSize = new Vector2(190, 38);
        header.AddChild(_four);
        header.AddChild(Ui.Margin(_end, 8));
        root.AddChild(header);

        Tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Screens.Add(new OverviewScreen());
        Screens.Add(new PoliciesScreen());
        Screens.Add(new VotersScreen());
        if (s.HasPolitics) { Screens.Add(new PoliticsScreen()); Screens.Add(new PartyScreen()); }
        Screens.Add(new GraphScreen());
        Screens.Add(new NewsScreen());
        foreach (var sc in Screens) Tabs.AddChild(sc);
        for (int i = 0; i < Screens.Count; i++) Tabs.SetTabTitle(i, s.T.T(Screens[i].TitleKey));
        root.AddChild(Tabs);
        AddChild(root);

        s.Changed += OnChanged;
        OnChanged();
    }

    private void OnChanged()
    {
        var s = Session;
        if (s == null || Tabs == null) return;
        _date.Text = $"{s.DateText()}   ({s.Turn}/{Session.HorizonTurns})";
        _status.Text = s.Message;
        foreach (var screen in Screens) screen.Refresh(s);
        if (s.Finished && !_endShown) { _endShown = true; ShowEnd(s); }
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
