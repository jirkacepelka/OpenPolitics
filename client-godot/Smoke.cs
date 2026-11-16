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
            if (screenshots != null) System.IO.Directory.CreateDirectory(screenshots);
            await StartScreenPartyPicker(main, screenshots);
            foreach (var scenario in main.Packs!.Scenarios.OrderBy(s => s.Id, StringComparer.Ordinal))
                foreach (var language in new[] { "en", "cs" })
                {
                    if (language == "cs" && scenario.Politics == null) continue;
                    main.StartGame(scenario.Id, 7, language);
                    await Play(main, scenario.Id, language, screenshots);
                }
            // decision 0013: an opposition party from the very start, a costed policy change and a coalition reshuffle
            if (main.Packs.Scenarios.Any(sc => sc.Id == "base:cz_2015") && main.Packs.Parties.Any(p => p.Id == "base:civic_right"))
            {
                main.StartGame("base:cz_2015", 7, "en", "base:civic_right");
                if (main.Session!.Politics!.PlayerRole != Role.Opposition) throw new Exception("civic right should start in opposition");
                await Play(main, "base:cz_2015", "en", screenshots, "opposition");
                main.StartGame("base:cz_2015", 11, "en");
                await CostAndReshuffle(main, screenshots);
                main.StartGame("base:cz_2015", 7, "en", "base:civic_right");
                await ProposalPromiseProgram(main, screenshots);
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

    private static List<T> FindAll<T>(Node root, List<T>? into = null) where T : class
    {
        into ??= new List<T>();
        foreach (var c in root.GetChildren())
        {
            if (c is T hit) into.Add(hit);
            FindAll(c, into);
        }
        return into;
    }

    /// <summary>The start screen shows one card per party for the political scenario; choosing one rebuilds the cards with it selected.</summary>
    private static async Task StartScreenPartyPicker(Main main, string? shots)
    {
        main.ShowStart();
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "start");
        if (main.Packs!.Scenarios.All(sc => sc.Politics == null)) return;
        var cards = FindAll<Button>(main).Where(b => b.ThemeTypeVariation == "ButtonCard" && b.TooltipText.Contains(" · ")).ToList();
        if (cards.Count != main.Packs.Parties.Count) throw new Exception($"start screen shows {cards.Count} party cards, expected {main.Packs.Parties.Count}");
        cards[2].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        var after = FindAll<Button>(main).Where(b => b.ThemeTypeVariation == "ButtonCard" && b.TooltipText.Contains(" · ")).ToList();
        if (after.Count(b => b.ButtonPressed) != 1) throw new Exception("exactly one party card should be selected");
        if (shots != null) Shot(main, shots, "start-party");
        GD.Print($"smoke: start screen party picker ok ({after.Count} parties)");
    }

    /// <summary>The policy dialog refuses a change the capital cannot pay and charges an affordable one; then a reshuffle is shown and asked.</summary>
    private static async Task CostAndReshuffle(Main main, string? shots)
    {
        var s = main.Session!;
        s.EndQuarter();
        if (s.Politics is not { PlayerRole: Role.Leader, Phase: Phase.Governing }) { GD.Print("smoke: note: not leading, cost check skipped"); return; }
        main.OpenDetail("policy", "base:pensions");
        await Frames(main, 3);
        var slider = Find<PolicySlider>(main) ?? throw new Exception("no policy slider");
        if (slider.Affordable is not { } band) throw new Exception("the slider shows no affordable band");
        IconTextButton Apply() => FindAll<IconTextButton>(main).First(b => b.Text.StartsWith(s.T.T("ui.pd.apply"), StringComparison.Ordinal));
        slider.SetTarget(slider.InForce > (slider.Min + slider.Max) / 2 ? slider.Min : slider.Max);
        await Frames(main, 2);
        if (!Apply().Disabled) throw new Exception("an unaffordable change can be applied");
        if (shots != null) Shot(main, shots, "policy-cost-unaffordable");
        slider.SetTarget(slider.InForce + (band.Hi - slider.InForce) * 0.6);
        await Frames(main, 2);
        if (shots != null) Shot(main, shots, "policy-cost");
        double before = s.Politics!.Party!.PoliticalCapital;
        if (Apply().Disabled) throw new Exception("an affordable change cannot be applied");
        Apply().EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 2);
        if (!(s.Politics!.Party!.PoliticalCapital < before)) throw new Exception("the policy change cost nothing");
        main.CloseDialogs();
        await Frame(main);
        for (int i = 0; i < 2; i++) s.EndQuarter(); // capital for a swap

        var pol = s.Politics!;
        // the first swap of one partner for one outside party that keeps a majority and that the parties accept
        var swaps = (from d in pol.GovernmentParties.Where(p => p != pol.PlayerParty).OrderBy(p => pol.Seats.GetValueOrDefault(p))
                     from a in pol.Seats.Where(kv => kv.Value > 0 && !pol.GovernmentParties.Contains(kv.Key)).OrderByDescending(kv => kv.Value).Select(kv => kv.Key)
                     select (Drop: new List<string> { d }, Add: new List<string> { a })).ToList();
        var pick = swaps.FirstOrDefault(sw => s.Game.SuggestReshuffle(sw.Drop, sw.Add) is { } dl && s.Game.PreviewReshuffle(sw.Drop, sw.Add, dl) is { ErrorKey: null, Refusals.Count: 0 });
        if (pick.Drop == null) pick = swaps.First();
        var (drop, add) = pick;
        var screen = main.Screens.OfType<PoliticsScreen>().First();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        ((HashSet<string>)typeof(PoliticsScreen).GetField("_rsDrop", flags)!.GetValue(screen)!).UnionWith(drop);
        ((HashSet<string>)typeof(PoliticsScreen).GetField("_rsAdd", flags)!.GetValue(screen)!).UnionWith(add);
        var deal = s.Game.SuggestReshuffle(drop, add) ?? Array.Empty<AgreedDemand>();
        ((HashSet<(string, string)>)typeof(PoliticsScreen).GetField("_rsAgreed", flags)!.GetValue(screen)!).UnionWith(deal.Select(a => (a.PartyId, a.DemandId)));
        main.Open(screen);
        await Frames(main, 4);
        screen.ScrollToReshuffle();
        await Frames(main, 3);
        if (shots != null) Shot(main, shots, "reshuffle");
        var preview = s.Game.PreviewReshuffle(drop, add, deal);
        if (preview.ErrorKey == null && preview.Affordable)
        {
            Check(s.Do(new ReshuffleCoalition(drop, add, deal)), "reshuffle");
            s.EndQuarter();
            await Frames(main, 2);
            var outcome = s.Last!.PoliticalEvents.FirstOrDefault(e => e is CoalitionReshuffled or ReshuffleRefused);
            GD.Print($"smoke: reshuffle {string.Join(",", drop)} -> {string.Join(",", add)}: {outcome?.GetType().Name ?? "no answer"}");
            if (outcome == null) throw new Exception("the reshuffle got no answer");
            main.Open(screen);
            await Frames(main, 3);
            if (shots != null) Shot(main, shots, "reshuffle-after");
        }
        else GD.Print($"smoke: note: reshuffle not possible ({preview.ErrorKey}, affordable {preview.Affordable})");
        main.Open(null);
    }

    private static async Task Play(Main main, string scenarioId, string language, string? shots, string tag = "")
    {
        var s = main.Session!;
        string prefix = $"{scenarioId.Replace(':', '_')}-{language}{(tag.Length > 0 ? "-" + tag : "")}";
        int quarters = scenarioId.Contains("sandbox") ? 8 : 30;
        for (int q = 0; q < quarters; q++)
        {
            var pol = s.Politics;
            if (pol?.Formation is { PlayerIsFormateur: true } && s.Game.SuggestCoalition() is { } sug)
                Check(s.Do(new ProposeCoalition(sug.Partners, sug.Agreements)), "propose");
            else if (pol?.Formation?.Offer != null) Check(s.Do(new AcceptOffer()), "accept");
            if (q == 1) s.Do(new SetPolicy("base:research", 2.0));
            if (pol is { Phase: Phase.Governing }) { s.Do(new Campaign("base:middle_class", 1)); s.Do(new BuildOrganization()); }
            if (pol is { PlayerRole: Role.Partner, Phase: Phase.Governing } && q % 5 == 2) s.Do(new ProposePolicy("base:research", 2.0));
            if (pol is { PlayerRole: Role.Opposition, Phase: Phase.Governing } && q == 20) s.Do(new NoConfidence());
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
            foreach (var model in new[] { "base:debt", "base:interest_rate", "base:capital" })
            {
                main.OpenDetail("value", model);
                await Frames(main, 4);
                Shot(main, shots, $"{prefix}-value-{model.Replace("base:", "")}");
                main.CloseDialogs();
            }
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

    /// <summary>
    /// Decision 0014: as the opposition, wait for a proposal, answer it with the Support button in the briefing, make a promise once the election is
    /// near, announce a policy proposal, and walk through the programme, promises and actions tabs of the party screen.
    /// </summary>
    private static async Task ProposalPromiseProgram(Main main, string? shots)
    {
        var s = main.Session!;
        var t = s.T;
        ProposalInfo? open = null;
        for (int q = 0; q < 16 && open == null; q++)
        {
            s.EndQuarter();
            main.CloseDialogs();
            await Frame(main);
            open = s.Politics?.Proposals?.FirstOrDefault(p => p.Answer == null);
        }
        if (open == null) throw new Exception("no proposal reached the opposition in 16 quarters");
        if (TurnPopup.DecisionCount(s) < 1) throw new Exception("the open proposal is not counted as a decision");
        main.PopupsEnabled = true;
        main.ShowBriefing(force: true);
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "proposal-briefing");
        var dlg = FindAll<Dialog>(main).LastOrDefault(d => d.Key == "briefing") ?? throw new Exception("no briefing");
        var support = FindAll<IconTextButton>(dlg).FirstOrDefault(b => b.Text == t.T("ui.client.support")) ?? throw new Exception("no Support button in the briefing");
        support.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        if (s.Politics?.Proposals?.FirstOrDefault(p => p.Id == open.Id)?.Answer != Stance.Support) throw new Exception("the proposal was not answered from the briefing");
        if (shots != null) Shot(main, shots, "proposal-answered");
        main.PopupsEnabled = false;
        main.CloseDialogs();
        main.Show(main.Screens.OfType<PoliticsScreen>().First());
        await Frames(main, 2);
        // a promise once the election is within reach
        while (s.Politics!.TurnsToElection > PoliticsLimits.PromiseWindowTurns && !s.Finished) { s.EndQuarter(); main.CloseDialogs(); await Frame(main); }
        var own = s.Packs.Parties.First(p => p.Id == s.Politics.PlayerParty);
        var goal = own.Demands.FirstOrDefault(d => d.Kind == "value" && !OpenPolitics.Core.Presentation.Texts.IsMet(d, s.Country))
            ?? own.Demands.First(d => !OpenPolitics.Core.Presentation.Texts.IsMet(d, s.Country));
        if (!s.Do(new MakePromise(goal.Id))) throw new Exception("promise refused: " + s.Message);
        if (s.Politics.Party!.PromiseDetails is not { Count: 1 }) throw new Exception("promise not shown");
        if (!s.Do(new AnnouncePolicy("base:income_tax", 19))) GD.Print("smoke: note: announcement refused: " + s.Message);
        var party = main.Screens.OfType<PartyScreen>().First();
        foreach (var tab in new[] { 0, 1, 2, 4 })
        {
            typeof(PartyScreen).GetField("_tab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(party, tab);
            main.Open(party);
            await Frames(main, 4);
            if (Find<Dialog>(main) is not { } d || d.Body.GetChildCount() == 0) throw new Exception($"party tab {tab} is empty");
            if (shots != null) Shot(main, shots, $"party0014-tab{tab}");
        }
        main.Open(null);
        GD.Print($"smoke: proposal answered, promise '{goal.Id}' made, turn {s.Turn}");
    }

    private static void Check(bool ok, string what) { if (!ok) GD.Print("smoke: note: action refused: " + what); }
}
