using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Client;

/// <summary>
/// `godot --headless --path client-godot -- --smoke` plays scripted quarters through every screen and dialog of every scenario and exits
/// with 0 or 1. With `--screenshots DIR` (windowed) it also writes PNGs of the start screen, the map (plain, hovered, group lens), the policy,
/// indicator and group dialogs, every screen, a chart and the briefing, at whatever window size it runs in.
/// </summary>
public static class Smoke
{
    public static Vector2I? RequestedSize { get; set; }

    public static async Task Run(Main main, string? screenshots)
    {
        int code = 0;
        try
        {
            await Frame(main);
            await Frame(main);
            if (RequestedSize is { } want) GD.Print($"smoke: window {want}, logical {main.Size}, class {Layout.Width}/{Layout.Height}");
            if (screenshots != null)
            {
                System.IO.Directory.CreateDirectory(screenshots);
                main.ShowStart();
                await Frames(main, 4);
                Shot(main, screenshots, "start");
            }
            foreach (var scenario in main.Packs!.Scenarios.OrderBy(s => s.Id, StringComparer.Ordinal))
                foreach (var language in new[] { "en", "cs" })
                {
                    if (language == "cs" && scenario.Politics == null) continue;
                    main.StartGame(scenario.Id, 7, language);
                    await Play(main, scenario.Id, language, screenshots);
                }
            GD.Print("SMOKE OK");
        }
        catch (Exception e)
        {
            GD.PrintErr("SMOKE FAIL: " + e);
            code = 1;
        }
        main.GetTree().Quit(code);
    }

    private static async Task Frame(Main main) => await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
    private static async Task Frames(Main main, int n) { for (int i = 0; i < n; i++) await Frame(main); }

    private static void Shot(Main main, string dir, string name) =>
        main.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name + ".png"));

    private static T? Find<T>(Node root) where T : class
    {
        foreach (var c in root.GetChildren())
        {
            if (c is T hit) return hit;
            if (Find<T>(c) is { } deeper) return deeper;
        }
        return null;
    }

    private static async Task Play(Main main, string scenarioId, string language, string? shots)
    {
        var s = main.Session!;
        string prefix = $"{scenarioId.Replace(':', '_')}-{language}";
        int quarters = scenarioId.Contains("sandbox") ? 8 : 30;
        for (int q = 0; q < quarters; q++)
        {
            var pol = s.Politics;
            if (pol?.Formation is { PlayerIsFormateur: true } && s.Game.SuggestCoalition() is { } sug)
                Check(s.Do(new ProposeCoalition(sug.Partners, sug.Agreements)), "propose");
            else if (pol?.Formation?.Offer != null) Check(s.Do(new AcceptOffer()), "accept");
            if (q == 1) s.Do(new SetPolicy("base:research", 2.0));
            if (pol is { Phase: Phase.Governing }) { s.Do(new Campaign("base:middle_class", 1)); s.Do(new BuildOrganization()); }
            s.Do(new Court("base:green_wave"));
            s.Do(new Criticize("base:gdp"));
            s.EndQuarter();
            main.Show(main.Screens[q % main.Screens.Count]);
            await Frame(main);
            if (q % 7 == 0)
            {
                main.OpenDetail(q % 2 == 0 ? "value" : "policy", q % 2 == 0 ? "base:gdp" : "base:income_tax");
                await Frame(main);
            }
        }
        foreach (var screen in main.Screens)
        {
            main.Show(screen);
            await Frame(main);
            var dlg = Find<Dialog>(main);
            if (dlg == null || dlg.Body.GetChildCount() == 0) throw new Exception($"{screen.GetType().Name} is empty");
        }
        main.Open(null);
        foreach (var g in s.Packs.VoterGroups.Take(2)) { main.OpenDetail("group", g.Id); await Frame(main); }
        if (s.HasPolitics) { main.OpenDetail("party", s.Politics!.PlayerParty); await Frame(main); }
        main.OpenDetail("policy", "base:pensions");
        await Frame(main);
        if (Find<PolicySlider>(main) is { } ps) ps.SetTarget(13);
        foreach (var chart in Charts.Available(s)) _ = Charts.Build(s, chart, 8);
        main.CloseDialogs();
        await Frame(main);

        if (shots != null)
        {
            bool full = language == "en" || scenarioId.Contains("cz");
            await Frames(main, 3);
            Shot(main, shots, $"{prefix}-map");
            if (main.Map!.PositionOf("base:unemployment") != null)
            {
                main.Map.HoverForTest("base:unemployment");
                await Frames(main, 2);
                Shot(main, shots, $"{prefix}-map-hover");
                main.Map.HoverForTest(null);
            }
            var group = s.Packs.VoterGroups.OrderBy(g => g.Id, StringComparer.Ordinal).First().Id;
            main.SetLens(group);
            await Frames(main, 2);
            Shot(main, shots, $"{prefix}-map-lens");
            main.SetLens(null);

            if (s.Packs.Policies.Any(p => p.Id == "base:minimum_wage"))
            {
                main.OpenDetail("policy", "base:minimum_wage");
                await Frames(main, 2);
                if (Find<PolicySlider>(main) is { } slider) slider.SetTarget(slider.InForce + (slider.Max - slider.Min) * 0.2);
                await Frames(main, 4);
                Shot(main, shots, $"{prefix}-policy");
                main.CloseDialogs();
            }
            main.OpenDetail("value", "base:unemployment");
            await Frames(main, 4);
            Shot(main, shots, $"{prefix}-value");
            main.CloseDialogs();
            if (full)
            {
                main.OpenDetail("group", group);
                await Frames(main, 4);
                Shot(main, shots, $"{prefix}-group");
                main.CloseDialogs();
            }
            for (int i = 0; i < main.Screens.Count; i++)
            {
                main.Open(main.Screens[i]);
                await Frames(main, 4);
                Shot(main, shots, $"{prefix}-{i}-{main.Screens[i].GetType().Name}");
            }
            if (s.HasPolitics && main.Screens.OfType<PartyScreen>().FirstOrDefault() is { } party)
            {
                // the other tabs of the party screen
                foreach (var tab in new[] { 1, 4 })
                {
                    typeof(PartyScreen).GetField("_tab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(party, tab);
                    main.Open(party);
                    await Frames(main, 4);
                    Shot(main, shots, $"{prefix}-party-tab{tab}");
                }
            }
            main.Open(null);
            if (Charts.Available(s).FirstOrDefault(c => c.Id == (s.HasPolitics ? "polls" : "gdp")) is { } spec)
            {
                main.OpenChart(spec);
                await Frames(main, 4);
                Shot(main, shots, $"{prefix}-chart");
                main.CloseDialogs();
            }
            main.PopupsEnabled = true;
            if (s.Digest.Count == 0) s.Digest.Add("Smoke: a sample line of news.");
            main.ShowBriefing();
            await Frames(main, 4);
            Shot(main, shots, $"{prefix}-briefing");
            main.PopupsEnabled = false;
            main.CloseDialogs();
            await Frame(main);
        }
        GD.Print($"smoke: {scenarioId} [{language}] ok, turn {s.Turn}");
    }

    private static void Check(bool ok, string what) { if (!ok) GD.Print("smoke: note: action refused: " + what); }
}
