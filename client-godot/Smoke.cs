using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Client;

/// <summary>
/// `godot --headless --path client-godot -- --smoke` plays scripted quarters through every screen and dialog of every scenario and exits
/// with 0 or 1. With `--screenshots DIR` (windowed) it also writes PNGs of the main menu, settings, new-game and load screens, the map (plain, hovered, group lens), the policy,
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
            if (screenshots != null) await IconSheet(main, screenshots);
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
                // the programme is a choice of laws: a minus per law, "add program" lists the rest, both change the programme
                main.StartGame("base:cz_2015", 7, "en", "base:civic_right");
                await ProgrammeEditing(main, screenshots);
                // decision 0019: a joint list in the districts, proposed from the politics screen
                if (main.Packs.Scenarios.Any(sc => sc.Id == "base:cz_2015_districts"))
                {
                    main.StartGame("base:cz_2015_districts", 7, "en", "base:open_net");
                    await AllianceFlow(main, screenshots);
                }
                // decision 0015: every decision answered in the briefing - forming the government as formateur, an ultimatum, promises
                main.StartGame("base:cz_2015", 7, "en", "base:citizens_movement");
                await DecisionsInBriefing(main, screenshots);
                // decision 0016: a junior partner's ultimatum, and a counter-offer as an invited party
                main.StartGame("base:cz_2015", 7, "en", "base:peoples_union");
                await PartnerUltimatumAndCounter(main, screenshots);
            }
            // decision 0021: a law that is not in force is brought in from the Laws tab and repealed from its own dialog
            main.StartGame("base:cz_2015_sandbox", 5, "en");
            await LawsFlow(main, screenshots);
            // decision 0020: a random start plays like any other, whoever governs and whatever the party's role
            foreach (ulong seed in new ulong[] { 21, 22, 23 })
            {
                main.StartGame("base:cz_2015", seed, "en", null, randomStart: true);
                for (int q = 0; q < 6; q++) main.Session!.EndQuarter();
                await Frames(main, 2);
                GD.Print($"smoke: random start seed {seed}: {main.Session!.Politics!.Leader} leads, {main.Session.Politics.PlayerRole}");
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

    /// <summary>
    /// The main menu offers its entries; Settings opens as a dialog and switching the language rebuilds the menu and keeps the dialog; the
    /// new-game screen shows one row per party for the political scenario and choosing one selects exactly that row; a saved game appears in
    /// the load list and loads from it.
    /// </summary>
    private static async Task StartScreenPartyPicker(Main main, string? shots)
    {
        main.ShowStart();
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "menu");
        var entries = FindAll<IconTextButton>(main).Where(b => b.ThemeTypeVariation == "ButtonMenu" || b.ThemeTypeVariation == "ButtonPlay").ToList();
        if (entries.Count < 4) throw new Exception($"main menu shows {entries.Count} entries, expected at least 4");

        main.ShowSettings();
        await Frames(main, 4);
        if (FindAll<Dialog>(main).Count != 1) throw new Exception("settings should open as one dialog");
        if (shots != null) Shot(main, shots, "settings");
        main.SetLanguage("cs", persist: false);
        await Frames(main, 4);
        if (main.Language != "cs" || FindAll<Dialog>(main).Count != 1) throw new Exception("switching the language should rebuild the menu and keep the settings dialog");
        if (shots != null) Shot(main, shots, "settings-cs");
        main.SetLanguage("en", persist: false);
        await Frames(main, 2);
        main.CloseDialogs();
        await Frame(main);

        main.ShowNewGame();
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "start");
        if (main.Packs!.Scenarios.Any(sc => sc.Politics != null))
        {
            var cards = FindAll<Button>(main).Where(b => b.TooltipText.Contains(" \u00b7 ")).ToList();
            if (cards.Count != main.Packs.Parties.Count) throw new Exception($"new-game screen shows {cards.Count} party rows, expected {main.Packs.Parties.Count}");
            cards[2].EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(main, 3);
            var after = FindAll<Button>(main).Where(b => b.TooltipText.Contains(" \u00b7 ")).ToList();
            if (after.Count(b => b.ButtonPressed) != 1) throw new Exception("exactly one party row should be selected");
            if (shots != null) Shot(main, shots, "start-party");
            GD.Print($"smoke: new-game screen party list ok ({after.Count} parties)");
        }

        // a saved game shows up in the load list (a temp file: the player's own saves are never touched) and loads from it
        var scenarioId = main.Packs.Scenarios.OrderBy(sc => sc.Id, StringComparer.Ordinal).First().Id;
        var probe = new Session(main.Packs, scenarioId, 7, "en");
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "openpolitics-smoke.save.yaml");
        try
        {
            System.IO.File.WriteAllText(file, probe.Game.Save().ToYaml());
            main.ShowStart();
            main.ShowLoad(new[] { file });
            await Frames(main, 4);
            if (shots != null) Shot(main, shots, "load");
            var row = FindAll<Button>(main).FirstOrDefault(b => b.TooltipText == file) ?? throw new Exception("the saved game is not in the load list");
            if (row.Disabled) throw new Exception("a readable saved game is disabled in the load list");
            row.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(main, 3);
            if (main.Session == null || main.Session.ScenarioId != scenarioId) throw new Exception("loading from the list did not open the saved game");
            GD.Print("smoke: main menu, settings, new game and load ok");
        }
        finally { if (System.IO.File.Exists(file)) System.IO.File.Delete(file); }
    }

    /// <summary>Every pictogram on one sheet (a quick look for duplicates and badly drawn ones), and every law and indicator's icon must be its own.</summary>
    private static async Task IconSheet(Main main, string shots)
    {
        main.ShowStart();
        var bg = new ColorRect { Color = AppTheme.Surface };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var grid = new GridContainer { Columns = 14 };
        grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10);
        foreach (var kind in Enum.GetValues<IconKind>()) grid.AddChild(new PictoDisc(kind, 40));
        bg.AddChild(Ui.Margin(grid, 16));
        main.AddChild(bg);
        await Frames(main, 3);
        Shot(main, shots, "icons");
        bg.QueueFree();
        var shared = main.Packs!.Policies.Select(p => p.Id).GroupBy(Icons.For).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g)}").ToList();
        if (shared.Count > 0) GD.Print("smoke: note: laws sharing an icon: " + string.Join(" | ", shared));
        if (main.Packs.Policies.Any(p => Icons.For(p.Id) == IconKind.Gauge)) GD.Print("smoke: note: a law has the fallback icon");
    }

    private static async Task LawsFlow(Main main, string? shots)
    {
        var s = main.Session!;
        var t = s.T;
        var idle = s.Packs.Policies.Where(p => !s.Game.IsActive(p.Id)).ToList();
        if (idle.Count == 0) throw new Exception("every law is in force at the start");
        var laws = main.Screens.OfType<LawsScreen>().First();
        main.Open(laws);
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "laws");
        var introduce = FindAll<IconTextButton>(main).Where(b => b.Text.StartsWith(t.T("ui.law.introduce"), StringComparison.Ordinal)).ToList();
        if (introduce.Count != idle.Count) throw new Exception($"{introduce.Count} bring-in buttons for {idle.Count} laws");
        int before = s.Game.ActivePolicies.Count;
        introduce[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        if (s.Game.ActivePolicies.Count != before + 1) throw new Exception("bringing a law in did not put it in force");
        var inForce = s.Game.ActivePolicies.Except(s.Packs.Policies.Where(p => p.StartActive).Select(p => p.Id)).First();
        main.OpenDetail("policy", inForce);
        await Frames(main, 4);
        var repeal = FindAll<IconTextButton>(main).FirstOrDefault(b => b.Text.StartsWith(t.T("ui.law.abolish"), StringComparison.Ordinal)) ?? throw new Exception("no repeal button");
        repeal.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        if (s.Game.IsActive(inForce)) throw new Exception("repealing a law left it in force");
        main.CloseDialogs();
        GD.Print($"smoke: laws in force {before}, brought in and repealed {inForce}");

        // The private pillar: its dialog and the population screen both say what the funds do and where they lead.
        main.OpenDetail("policy", "base:pension_subsidy");
        await Frames(main, 4);
        if (!FindAll<Label>(main).Any(l => string.Equals(l.Text, t.T("ui.pop.fund_open"), StringComparison.OrdinalIgnoreCase))) throw new Exception("the idle pillar law shows no pension card");
        main.CloseDialogs();
        if (!s.Do(new IntroducePolicy("base:pension_subsidy"), t.T("ui.law.introduced", t.Name("base:pension_subsidy")))) throw new Exception("the state contribution cannot be brought in");
        main.OpenDetail("policy", "base:pension_subsidy");
        await Frames(main, 4);
        if (!FindAll<Label>(main).Any(l => string.Equals(l.Text, t.T("ui.pop.fund_open"), StringComparison.OrdinalIgnoreCase))) throw new Exception("the pillar law shows no pension card");
        foreach (var sc in FindAll<ScrollContainer>(main)) sc.ScrollVertical = 420;
        await Frames(main, 3);
        if (shots != null) Shot(main, shots, "pension-policy");
        main.CloseDialogs();
        main.Open(main.Screens.OfType<PopulationScreen>().First());
        await Frames(main, 4);
        foreach (var sc in FindAll<ScrollContainer>(main)) sc.ScrollVertical = 100000;
        await Frames(main, 3);
        if (!FindAll<Label>(main).Any(l => string.Equals(l.Text, t.T("ui.pop.fund_open"), StringComparison.OrdinalIgnoreCase))) throw new Exception("the population screen shows no pension card");
        if (shots != null) Shot(main, shots, "pension-population");
        main.CloseDialogs();
    }

    private static async Task AllianceFlow(Main main, string? shots)
    {
        var s = main.Session!;
        var t = s.T;
        for (int i = 0; i < 4; i++) s.EndQuarter(); // the window opens eight quarters before the election
        var screen = main.Screens.OfType<PoliticsScreen>().First();
        main.Open(screen);
        await Frames(main, 4);
        string partner = "base:social_alliance";
        var chip = FindAll<Button>(main).FirstOrDefault(b => b.ThemeTypeVariation == "ButtonSeg" && b.Text.StartsWith(t.Name(partner), StringComparison.Ordinal))
            ?? throw new Exception("no party to choose for a joint list");
        chip.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 4);
        if (shots != null)
        {
            foreach (var sc in FindAll<ScrollContainer>(main)) sc.ScrollVertical = 100000; // the card sits below the parliament and polls
            await Frames(main, 3);
            Shot(main, shots, "alliance");
        }
        var form = FindAll<IconTextButton>(main).FirstOrDefault(b => b.Text.StartsWith(t.T("ui.alliance.form"), StringComparison.Ordinal)) ?? throw new Exception("no button to form the list");
        if (form.Disabled) throw new Exception("the list the partner would join cannot be formed");
        form.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        if (s.Politics!.Alliances is not { Count: > 0 } lists || !lists.Any(a => a.Members.Contains(partner) && a.Members.Contains(s.Politics.PlayerParty)))
            throw new Exception("forming the joint list did not register");
        if (shots != null) Shot(main, shots, "alliance-formed");
        main.CloseDialogs();
        GD.Print("smoke: joint list ok");
    }

    private static async Task ProgrammeEditing(Main main, string? shots)
    {
        var s = main.Session!;
        var t = s.T;
        var party = main.Screens.OfType<PartyScreen>().First();
        typeof(PartyScreen).GetField("_tab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(party, 1);
        main.Open(party);
        await Frames(main, 4);
        int Count() => s.Politics!.Party!.Platform.Count;
        int before = Count();
        if (before >= s.Packs.Policies.Count) throw new Exception("the programme should not hold every law");
        var minus = FindAll<IconTextButton>(main).Where(b => b.ThemeTypeVariation == "ButtonDanger").ToList();
        if (minus.Count != before) throw new Exception($"{minus.Count} minus buttons for {before} laws in the programme");
        var add = FindAll<IconTextButton>(main).First(b => b.Text == t.T("ui.program.add"));
        add.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        if (shots != null) Shot(main, shots, "programme-add");
        string hint = t.T("ui.program.add_hint", PartyRules.AddCost);
        var plus = FindAll<IconTextButton>(main).Where(b => b.TooltipText == hint && b.Text.Length == 0).ToList();
        if (plus.Count != s.Packs.Policies.Count - before) throw new Exception($"{plus.Count} laws offered, expected {s.Packs.Policies.Count - before}");
        plus[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        if (Count() != before + 1) throw new Exception("adding a law did not put it into the programme");
        FindAll<IconTextButton>(main).First(b => b.Text == t.T("ui.program.done")).EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        FindAll<IconTextButton>(main).First(b => b.ThemeTypeVariation == "ButtonDanger" && !b.Disabled).EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        if (Count() != before) throw new Exception("taking a law out did not shrink the programme");
        if (shots != null) Shot(main, shots, "programme");
        main.CloseDialogs();
        GD.Print($"smoke: programme editing ok ({before} of {s.Packs.Policies.Count} laws)");
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
                Check(s.Do(ProposeCoalition.From(sug)), "propose");
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
                if (main.Screens[i] is PoliticsScreen)
                {
                    foreach (var sc in FindAll<ScrollContainer>(main)) sc.ScrollVertical = 380; // the lower cards: cabinet, coalition
                    await Frames(main, 3);
                    Shot(main, shots, $"{prefix}-politics-lower");
                }
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
        if (!s.Do(new ProposePolicy("base:income_tax", 19))) GD.Print("smoke: note: public proposal refused: " + s.Message);
        // the voter panel: Support for the party by default in opposition, Mood one click away (decision 0015)
        if (!Sidebar.ShowsSupport(s)) throw new Exception("the sidebar should show support for the party in opposition");
        // a compact window has the narrow rail without the toggle: the full voter panel opens as a drawer
        if (!FindAll<Button>(main).Any(b => b.Name == "ViewMood")) { main.ToggleSidebarDrawer(); await Frames(main, 3); }
        var moodButton = FindAll<Button>(main).FirstOrDefault(b => b.Name == "ViewMood") ?? throw new Exception("no Mood / Support toggle in the sidebar");
        moodButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 2);
        if (Sidebar.ShowsSupport(s)) throw new Exception("the Mood toggle did not switch the sidebar");
        Sidebar.SupportView = null;
        main.RefreshSidebars();
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

    /// <summary>Presses the first button of the briefing whose name starts with <paramref name="name"/>; false when there is none.</summary>
    private static async Task<bool> PressInBriefing(Main main, string name)
    {
        var dlg = FindAll<Dialog>(main).LastOrDefault(d => d.Key == "briefing" && !d.IsQueuedForDeletion()) ?? throw new Exception("no briefing open");
        var b = FindAll<BaseButton>(dlg).FirstOrDefault(x => x.Name.ToString().StartsWith(name, StringComparison.Ordinal) && !x.Disabled);
        if (b == null) return false;
        b.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(main, 3);
        return true;
    }

    /// <summary>
    /// Decision 0015: as Citizens' Movement, wait for the party's own formation, open the briefing, look at the draft (Adjust) and form the suggested
    /// coalition from the briefing; then lead until a partner gives an ultimatum and answer it from the briefing (Court); then promise from the
    /// briefing when the window before the next election opens. The decision count always matches what the briefing offers.
    /// </summary>
    private static async Task DecisionsInBriefing(Main main, string? shots)
    {
        var s = main.Session!;
        main.PopupsEnabled = false;
        for (int q = 0; q < 30 && s.Politics!.Formation is not { PlayerIsFormateur: true }; q++)
        {
            if (s.Politics.Formation?.Offer != null) s.Do(new RejectOffer());
            s.EndQuarter(); main.CloseDialogs(); await Frame(main);
        }
        if (s.Politics!.Formation is not { PlayerIsFormateur: true }) throw new Exception("citizens' movement never got to form a government in 30 quarters");
        if (OpenPolitics.Core.Presentation.Decisions.Open(s.Country, s.Packs).All(d => d.Kind != OpenPolitics.Core.Presentation.DecisionKind.Formation))
            throw new Exception("the formation is not listed as a decision");
        main.PopupsEnabled = true;
        main.ShowBriefing(force: true);
        await Frames(main, 4);
        if (!await PressInBriefing(main, "Adjust")) GD.Print("smoke: note: no Adjust button (no suggestion)");
        await Frames(main, 2);
        var briefing = FindAll<Dialog>(main).Last(d => d.Key == "briefing");
        if (FindAll<CheckBox>(briefing).Count == 0) GD.Print("smoke: note: the draft shows no invitations");
        if (shots != null) Shot(main, shots, "briefing-formation");
        // decision 0016: one round of talks with a thinner offer plus a portfolio; the parties answer (or accept and the government forms)
        var draftPartner = DecisionCards.Invited.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (draftPartner != null)
        {
            DecisionCards.Agreed.RemoveWhere(a => a.Party == draftPartner);
            if (s.Game.Portfolios.FirstOrDefault() is { } pf) DecisionCards.Portfolios[pf.Id] = draftPartner;
            main.CloseDialogs();
            main.ShowBriefing(force: true);
            await Frames(main, 4);
            if (!await PressInBriefing(main, "SendOffer")) throw new Exception("no Send the offer button in the negotiation view");
            if (s.Politics is { Phase: Phase.Formation, Formation: { Replies.Count: 0 } }) throw new Exception("the parties gave no answer to the offer");
            if (s.Politics?.Formation is { PlayerIsFormateur: true })
            {
                main.CloseDialogs();
                main.ShowBriefing(force: true);
                await Frames(main, 4);
                bool took = await PressInBriefing(main, "TakeCounter_");
                await Frames(main, 2);
                if (shots != null) Shot(main, shots, "negotiation");
                GD.Print($"smoke: negotiation round answered ({string.Join(", ", s.Politics.Formation!.Replies!.Select(r => r.PartyId + "=" + r.Verdict))}), counter taken: {took}");
            }
            else GD.Print("smoke: the offer was accepted at once");
        }
        if (s.Politics?.Formation is { PlayerIsFormateur: true } && !await PressInBriefing(main, "FormSuggested")) throw new Exception("no 'Form this coalition' button in the briefing");
        if (s.Politics is not { Phase: Phase.Governing, PlayerRole: Role.Leader }) throw new Exception("the coalition was not formed from the briefing");
        main.CloseDialogs();
        main.PopupsEnabled = false;
        GD.Print($"smoke: formed the government from the briefing, turn {s.Turn}");

        // lead without delivering anything until a partner gives an ultimatum
        for (int q = 0; q < 40 && !(s.Politics is { PlayerRole: Role.Leader, Ultimatums.Count: > 0 }); q++)
        {
            if (s.Politics?.Formation is { PlayerIsFormateur: true } && s.Game.SuggestCoalition() is { } sug) s.Do(ProposeCoalition.From(sug));
            s.EndQuarter(); main.CloseDialogs(); await Frame(main);
        }
        if (s.Politics is not { PlayerRole: Role.Leader, Ultimatums.Count: > 0 } pol) throw new Exception("no ultimatum reached the leading player in 40 quarters");
        var u = pol.Ultimatums![0];
        int count = TurnPopup.DecisionCount(s);
        if (count < 1) throw new Exception("the ultimatum is not counted as a decision");
        main.PopupsEnabled = true;
        main.ShowBriefing(force: true);
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "briefing-ultimatum");
        double relation = s.Politics.Party!.Relations.GetValueOrDefault(u.PartyId);
        while (s.Politics.Party.PoliticalCapital < PartyRules.CourtCost) { main.CloseDialogs(); s.EndQuarter(); await Frame(main); main.ShowBriefing(force: true); await Frames(main, 3); }
        relation = s.Politics.Party!.Relations.GetValueOrDefault(u.PartyId);
        if (s.Politics.Ultimatums?.Any(x => x.PartyId == u.PartyId) == true)
        {
            if (!await PressInBriefing(main, "UltimatumCourt")) throw new Exception("no Court button for the ultimatum in the briefing");
            if (!(s.Politics.Party!.Relations.GetValueOrDefault(u.PartyId) > relation)) throw new Exception("courting from the briefing did nothing");
        }
        else GD.Print("smoke: note: the ultimatum resolved while saving capital");
        main.CloseDialogs();
        main.PopupsEnabled = false;
        GD.Print($"smoke: answered an ultimatum of {u.PartyId} from the briefing, turn {s.Turn}");

        // the window for promises: the briefing offers a Promise button per goal
        for (int q = 0; q < 20 && OpenPolitics.Core.Presentation.Decisions.Promisable(s.Country, s.Packs).Count == 0; q++)
        {
            if (s.Politics?.Formation is { PlayerIsFormateur: true } && s.Game.SuggestCoalition() is { } sug) s.Do(ProposeCoalition.From(sug));
            else if (s.Politics?.Formation?.Offer != null) s.Do(new AcceptOffer());
            s.EndQuarter(); main.CloseDialogs(); await Frame(main);
        }
        if (OpenPolitics.Core.Presentation.Decisions.Promisable(s.Country, s.Packs).Count > 0)
        {
            main.PopupsEnabled = true;
            main.ShowBriefing(force: true);
            await Frames(main, 4);
            int before = s.Politics!.Party!.PromiseDetails?.Count ?? 0;
            if (!await PressInBriefing(main, "Promise_")) throw new Exception("no Promise button in the briefing while the window is open");
            if ((s.Politics.Party!.PromiseDetails?.Count ?? 0) != before + 1) throw new Exception("the promise from the briefing was not made");
            if (shots != null) Shot(main, shots, "briefing-promise");
            main.CloseDialogs();
            main.PopupsEnabled = false;
            GD.Print($"smoke: promised from the briefing, turn {s.Turn}");
        }
        else GD.Print("smoke: note: no promise window reached");
        // fast-forward stops at a decision
        int start = s.Turn;
        main.PopupsEnabled = true;
        main.PlayFour();
        await Frames(main, 2);
        if (s.Turn - start < 1 || s.Turn - start > 4) throw new Exception("x4 moved " + (s.Turn - start) + " quarters");
        if (s.Turn - start < 4 && !OpenPolitics.Core.Presentation.Decisions.AnyUrgent(s.Country, s.Packs) && !Main.Blocking(s)) throw new Exception("x4 stopped without a decision");
        main.CloseDialogs();
        main.PopupsEnabled = false;
    }

    /// <summary>
    /// Decision 0016: as People's Union (a junior partner at the start) open the Actions tab with the Ultimatum card, give an ultimatum, see its
    /// countdown and the leader's answer in the briefing, withdraw it if it still runs; then, after an election, make a counter-offer to an offer.
    /// </summary>
    private static async Task PartnerUltimatumAndCounter(Main main, string? shots)
    {
        var s = main.Session!;
        main.PopupsEnabled = false;
        if (s.Politics!.PlayerRole != Role.Partner) throw new Exception("people's union should start as a junior partner");
        s.EndQuarter(); main.CloseDialogs(); await Frame(main);
        var party = main.Screens.OfType<PartyScreen>().First();
        typeof(PartyScreen).GetField("_tab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(party, 4);
        main.Open(party);
        await Frames(main, 4);
        var go = FindAll<BaseButton>(main).FirstOrDefault(b => b.Name == "UltimatumGo") ?? throw new Exception("no Ultimatum card in the partner's Actions tab");
        if (shots != null) Shot(main, shots, "ultimatum-card");
        if (!go.Disabled) { go.EmitSignal(BaseButton.SignalName.Pressed); await Frames(main, 3); }
        if (s.Politics!.PlayerUltimatum == null) throw new Exception("the ultimatum was not given from the card");
        main.Open(party);
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "ultimatum-card-open");
        main.Open(null);
        s.EndQuarter(); main.CloseDialogs(); await Frame(main);
        GD.Print($"smoke: ultimatum given; answer {s.Politics?.PlayerUltimatum?.Answer ?? "settled"}");
        if (s.Politics?.PlayerUltimatum != null)
        {
            main.PopupsEnabled = true;
            main.ShowBriefing(force: true);
            await Frames(main, 4);
            if (shots != null) Shot(main, shots, "briefing-own-ultimatum");
            if (!await PressInBriefing(main, "BriefUltimatumWithdraw")) throw new Exception("no Withdraw button for the own ultimatum in the briefing");
            if (s.Politics?.PlayerUltimatum != null) throw new Exception("withdrawing from the briefing did nothing");
            main.CloseDialogs();
            main.PopupsEnabled = false;
        }
        // an offer from an AI formateur: counter it with one of the party's demands
        for (int q = 0; q < 60 && s.Politics?.Formation?.Offer == null; q++) { s.EndQuarter(); main.CloseDialogs(); await Frame(main); }
        if (s.Politics?.Formation?.Offer == null) { GD.Print("smoke: note: no offer to counter in 60 quarters"); return; }
        DecisionCards._counterDemand = 1; DecisionCards._counterPortfolio = 0;
        main.PopupsEnabled = true;
        main.ShowBriefing(force: true);
        await Frames(main, 4);
        if (shots != null) Shot(main, shots, "briefing-counter");
        if (!await PressInBriefing(main, "CounterGo")) GD.Print("smoke: note: no counter button (nothing to ask)");
        else if (s.Politics?.Formation is { CounterUsed: false } && s.Politics.Phase == Phase.Formation) throw new Exception("the counter-offer was not sent");
        GD.Print($"smoke: counter-offer answered: {s.Politics?.Formation?.CounterAnswer ?? (s.Politics?.Phase == Phase.Governing ? "accepted (formed)" : "-")}");
        if (s.Politics?.Formation?.Offer != null) s.Do(new AcceptOffer());
        main.CloseDialogs();
        main.PopupsEnabled = false;
    }

    private static void Check(bool ok, string what) { if (!ok) GD.Print("smoke: note: action refused: " + what); }
}
