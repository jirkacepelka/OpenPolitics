using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>One running game plus everything the screens share: texts, news history, last report, messages.</summary>
public sealed class Session
{
    /// <summary>The game has no end: the world goes on until the player stops (decision 0015). Kept for the CLI's optional turn limit.</summary>
    public const int HorizonTurns = int.MaxValue;

    public PackSet Packs { get; }
    public Game Game { get; private set; }
    public string ScenarioId { get; }
    public ulong Seed { get; }
    public string Language { get; }
    public Localizer T { get; }
    public Texts Texts { get; }
    public TurnReport? Last { get; private set; }
    public List<(int Turn, string Text)> News { get; } = new();
    public string Message { get; private set; } = "";
    public double StartGdp { get; }
    public History History { get; }
    public IReadOnlyList<double> GdpHistory => History.Series(History.ValueKey("base:gdp"));
    /// <summary>News and decisions collected since the last start-of-turn popup was closed.</summary>
    public List<string> Digest { get; } = new();

    /// <summary>Raised after every change of the game (end of quarter, action, load).</summary>
    public event Action? Changed;

    /// <summary>Raised after a quarter ends, once the screens have been told (the start-of-quarter briefing hangs on this).</summary>
    public event Action? QuarterEnded;

    public Session(PackSet packs, string scenarioId, ulong seed, string language, string? party = null)
    {
        Packs = packs; ScenarioId = scenarioId; Seed = seed; Language = language;
        T = new Localizer(packs, language) { NominalGdpSource = () => Country.Values.GetValueOrDefault(OpenPolitics.Core.Economy.MacroModelV1.Ids.GdpNominal) };
        Texts = new Texts(packs, T);
        Game = Simulation.NewGame(packs, scenarioId, seed, party);
        StartGdp = Country.Values.GetValueOrDefault("base:gdp");
        History = new History(Game, packs);
        History.Record();
    }

    public Figures Figures => Figures.From(Country, GdpHistory, Packs.VoterGroups);

    public static string DefaultPacksDir()
    {
        var env = System.Environment.GetEnvironmentVariable("OPENPOLITICS_PACKS");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data", "base"));
    }

    public static LoadResult LoadPacks(string dir) =>
        PackLoader.Load(new IPackSource[] { new DirectoryPackSource(dir) });

    public CountryState Country => Game.State.Countries.Values.Single();
    public CountryPolitics? Politics => Country.Politics;
    public bool HasPolitics => Politics != null;
    public int Turn => Game.State.Turn;
    public bool Finished => false;

    public string DateText() => DateAt(Turn);

    /// <summary>A short date for chart axes, e.g. "2016 Q2".</summary>
    public string ShortDateAt(int turn)
    {
        var sc = Packs.GetScenario(ScenarioId);
        int tpy = sc.TurnsPerYear;
        return $"{sc.StartYear + turn / tpy} Q{turn % tpy + 1}";
    }

    /// <summary>The calendar date of a turn, e.g. "Year 2016, quarter 2".</summary>
    public string DateAt(int turn)
    {
        var sc = Packs.GetScenario(ScenarioId);
        int tpy = sc.TurnsPerYear;
        return T.T("ui.date", sc.StartYear + turn / tpy, turn % tpy + 1);
    }

    public void EndQuarter()
    {
        Last = Game.EndTurn();
        History.Record();
        foreach (var id in Last.FiredEventIds)
            if (Packs.Events.FirstOrDefault(e => e.Id == id) is { } ev) AddNews(T.T(ev.TextKey));
        foreach (var e in Last.PoliticalEvents)
        {
            if (e is ElectionHeld held) AddNews(ElectionText(held));
            else if (Texts.Event(e) is { } text) AddNews(text);
        }
        Message = "";
        Changed?.Invoke();
        QuarterEnded?.Invoke();
    }

    private void AddNews(string text)
    {
        News.Add((Last!.Turn, text));
        Digest.Add(text);
    }

    private string ElectionText(ElectionHeld e)
    {
        var parts = e.Seats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{T.Name(kv.Key)} {kv.Value}");
        return T.T("ui.politics.result_heading") + ": " + string.Join(", ", parts);
    }

    /// <summary>Asks every screen to redraw (UI-only changes such as a different selection).</summary>
    public void Touch() => Changed?.Invoke();

    /// <summary>Applies a player action; a refusal becomes the status message.</summary>
    public bool Do(PlayerAction action, string? okMessage = null)
    {
        var r = Game.Apply(action);
        Message = r.Accepted ? okMessage ?? "" : Texts.Error(r);
        Changed?.Invoke();
        return r.Accepted;
    }

    // ---- saving ---------------------------------------------------------------

    public static string SaveDir => ProjectSettings.GlobalizePath("user://saves");

    public string Save(string name)
    {
        System.IO.Directory.CreateDirectory(SaveDir);
        string file = System.IO.Path.Combine(SaveDir, name + ".save.yaml");
        System.IO.File.WriteAllText(file, Game.Save().ToYaml());
        Message = T.T("ui.saved", file);
        Changed?.Invoke();
        return file;
    }

    public bool Load(string file)
    {
        try
        {
            Game = Simulation.Load(Packs, SaveData.FromYaml(System.IO.File.ReadAllText(file)));
            Last = null;
            History.Reset(Game);
            Digest.Clear();
            Message = T.T("ui.loaded", file);
            Changed?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Message = T.T("ui.err.file", file, e.Message);
            Changed?.Invoke();
            return false;
        }
    }

    public static IEnumerable<string> SaveDirFiles() =>
        System.IO.Directory.Exists(SaveDir)
            ? System.IO.Directory.GetFiles(SaveDir, "*.save.yaml").OrderBy(f => f, StringComparer.Ordinal)
            : Array.Empty<string>();
}
