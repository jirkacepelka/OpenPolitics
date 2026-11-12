using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>One running game plus everything the screens share: texts, news history, last report, messages.</summary>
public sealed class Session
{
    public const int HorizonTurns = 80; // twenty years: the end-of-campaign summary

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
    public List<double> GdpHistory { get; } = new();
    public List<double> MoodHistory { get; } = new();
    public List<double> UnemploymentHistory { get; } = new();
    public List<double> InflationHistory { get; } = new();

    /// <summary>Raised after every change of the game (end of quarter, action, load).</summary>
    public event Action? Changed;

    public Session(PackSet packs, string scenarioId, ulong seed, string language)
    {
        Packs = packs; ScenarioId = scenarioId; Seed = seed; Language = language;
        T = new Localizer(packs, language);
        Texts = new Texts(packs, T);
        Game = Simulation.NewGame(packs, scenarioId, seed);
        StartGdp = Country.Values.GetValueOrDefault("base:gdp");
        RecordHistory();
    }

    private void RecordHistory()
    {
        var f = Figures;
        GdpHistory.Add(f.Gdp ?? 0); MoodHistory.Add(f.Mood);
        UnemploymentHistory.Add(f.Unemployment ?? 0); InflationHistory.Add(f.Inflation ?? 0);
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
    public bool Finished => Turn >= HorizonTurns;

    public string DateText()
    {
        var sc = Packs.GetScenario(ScenarioId);
        int tpy = sc.TurnsPerYear;
        return T.T("ui.date", sc.StartYear + Turn / tpy, Turn % tpy + 1);
    }

    public void EndQuarter()
    {
        Last = Game.EndTurn();
        RecordHistory();
        foreach (var id in Last.FiredEventIds)
            if (Packs.Events.FirstOrDefault(e => e.Id == id) is { } ev) News.Add((Last.Turn, T.T(ev.TextKey)));
        foreach (var e in Last.PoliticalEvents)
        {
            if (e is ElectionHeld held) News.Add((Last.Turn, ElectionText(held)));
            else if (Texts.Event(e) is { } text) News.Add((Last.Turn, text));
        }
        Message = "";
        Changed?.Invoke();
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
            GdpHistory.Clear(); MoodHistory.Clear(); UnemploymentHistory.Clear(); InflationHistory.Clear();
            RecordHistory();
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
