using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Client;

/// <summary>
/// `godot --headless --path client-godot -- --smoke` plays scripted quarters through every screen of every scenario and exits
/// with 0 or 1. With `--screenshots DIR` (windowed) it also writes a PNG of every tab.
/// </summary>
public static class Smoke
{
    public static async Task Run(Main main, string? screenshots)
    {
        int code = 0;
        try
        {
            if (screenshots != null)
            {
                System.IO.Directory.CreateDirectory(screenshots);
                main.ShowStart();
                await Frame(main); await Frame(main);
                main.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(screenshots, "start.png"));
            }
            foreach (var scenario in main.Packs!.Scenarios.OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                foreach (var language in new[] { "en", "cs" })
                {
                    if (language == "cs" && scenario.Politics == null) continue;
                    main.StartGame(scenario.Id, 7, language);
                    await Play(main, scenario.Id, language, screenshots);
                }
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

    private static async Task Play(Main main, string scenarioId, string language, string? shots)
    {
        var s = main.Session!;
        int quarters = scenarioId.Contains("sandbox") ? 8 : 30;
        for (int q = 0; q < quarters; q++)
        {
            var pol = s.Politics;
            // Answer formation questions like a sensible player; otherwise tinker a little.
            if (pol?.Formation is { PlayerIsFormateur: true } && s.Game.SuggestCoalition() is { } sug)
                Check(s.Do(new ProposeCoalition(sug.Partners, sug.Agreements)), "propose");
            else if (pol?.Formation?.Offer != null) Check(s.Do(new AcceptOffer()), "accept");
            if (q == 1) s.Do(new SetPolicy("base:research", 2.0));
            if (pol is { Phase: Phase.Governing }) { s.Do(new Campaign("base:middle_class", 1)); s.Do(new BuildOrganization()); }
            s.Do(new Court("base:green_wave"));
            s.Do(new Criticize("base:gdp"));
            s.EndQuarter();
            main.Tabs!.CurrentTab = q % main.Screens.Count;
            await Frame(main);
            if (q % 7 == 0)
                foreach (var g in main.Screens.OfType<GraphScreen>()) g.SelectedId = q % 2 == 0 ? "base:gdp" : "base:income_tax";
        }
        foreach (var screen in main.Screens)
            if (screen.GetChildCount() == 0) throw new Exception($"{screen.GetType().Name} is empty");
        foreach (var policies in main.Screens.OfType<PoliciesScreen>()) policies.Focus(s, "base:pensions", 13);
        foreach (var chart in Charts.Available(s)) _ = Charts.Build(s, chart, 8);
        if (shots != null)
        {
            if (s.Packs.Policies.Count > 0) foreach (var policies in main.Screens.OfType<PoliciesScreen>()) policies.Focus(s, "base:minimum_wage", 7);
            await Frame(main);
            main.Tabs!.CurrentTab = main.Screens.FindIndex(x => x is PoliciesScreen);
            await Frame(main); await Frame(main);
            main.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shots, $"{scenarioId.Replace(':', '_')}-{language}-effects.png"));
            if (Charts.Available(s).FirstOrDefault(c => c.Id == (s.HasPolitics ? "polls" : "gdp")) is { } spec)
            {
                ChartDialog.Open(main, s, spec);
                await Frame(main); await Frame(main);
                main.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shots, $"{scenarioId.Replace(':', '_')}-{language}-chart.png"));
                foreach (var d in main.GetTree().Root.GetChildren().OfType<ChartDialog>()) d.QueueFree();
                await Frame(main);
            }
            main.PopupsEnabled = true;
            if (s.Digest.Count == 0) s.Digest.Add("Smoke: a sample line of news.");
            main.ShowBriefing();
            await Frame(main); await Frame(main);
            main.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shots, $"{scenarioId.Replace(':', '_')}-{language}-popup.png"));
            main.PopupsEnabled = false;
            foreach (var w in main.GetChildren().OfType<TurnPopup>()) w.QueueFree();
            await Frame(main);
        }
        if (shots != null)
            for (int i = 0; i < main.Screens.Count; i++)
            {
                main.Tabs!.CurrentTab = i;
                await Frame(main); await Frame(main);
                var img = main.GetViewport().GetTexture().GetImage();
                img.SavePng(System.IO.Path.Combine(shots, $"{scenarioId.Replace(':', '_')}-{language}-{i}-{main.Screens[i].GetType().Name}.png"));
            }
        GD.Print($"smoke: {scenarioId} [{language}] ok, turn {s.Turn}");
    }

    private static void Check(bool ok, string what) { if (!ok) GD.Print("smoke: note: action refused: " + what); }
}
