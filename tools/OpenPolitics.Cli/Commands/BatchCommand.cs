using System.Text.Json;
using OpenPolitics.Core.Batch;
using OpenPolitics.Core.Gameplay;

namespace OpenPolitics.Cli.Commands;

public static class BatchCommand
{
    public const string Usage =
        "Usage: batch --packs <dir>... [--scenario <id>] [--games <n>] [--strategy passive|random|extreme] [--turns <n>] [--json <file>]\n" +
        "       batch --politics --packs <dir>... [--scenario <id>] [--games <n>] [--strategy ignorer|keeper|random|extreme|activist|reformer|challenger] [--turns <n>] [--party <id>|all]";

    /// <summary>Returns 0 if the batch is stable, 1 if not, 2 on usage or pack problems.</summary>
    public static int Execute(string[] args, TextWriter output)
    {
        var p = new ArgParser(args);
        int games = p.Int("--games", 100), turns = p.Int("--turns", 80);
        bool politics = p.Has("--politics");
        string strategyName = p.One("--strategy") ?? (politics ? "ignorer" : "passive");
        if (politics) return ExecutePolitics(p, games, turns, strategyName, output);
        var strategy = Strategies.ByName(strategyName);
        if (strategy == null) p.Errors.Add($"Unknown strategy '{strategyName}' (use passive, random or extreme).");
        if (games == 0) p.Errors.Add("--games must be at least 1.");
        if (p.Errors.Count > 0 || p.Has("--help"))
        {
            foreach (var e in p.Errors) output.WriteLine(e);
            output.WriteLine(Usage);
            return 2;
        }
        var loaded = RunCommand.LoadPacks(p, output);
        if (loaded == null) { output.WriteLine(Usage); return 2; }

        string? scenario = p.One("--scenario") ?? (loaded.Packs!.Scenarios.FirstOrDefault(s => s.Politics == null) ?? loaded.Packs!.Scenarios.FirstOrDefault())?.Id;
        if (scenario == null || loaded.Packs!.Scenarios.All(s => s.Id != scenario))
        {
            output.WriteLine($"Unknown scenario '{scenario}'.");
            return 2;
        }

        var report = BatchRunner.Run(loaded.Packs, scenario, games, turns, strategy!);
        output.WriteLine($"Batch: {report.Games} game(s) x {report.Turns} turns, strategy '{report.Strategy}', scenario '{scenario}'");
        output.WriteLine($"  NaN/Inf games:        {report.NanOrInfGames}");
        output.WriteLine($"  Extreme-rate games:   {report.ExtremeRateGames}");
        output.WriteLine($"  Collapse games:       {report.CollapseGames}");
        output.WriteLine($"  Explosion games:      {report.ExplosionGames}");
        output.WriteLine($"  Debt-spiral games:    {report.DebtSpiralGames} (informational)");
        output.WriteLine($"  Ceiling detected:     {report.CeilingDetected}");
        foreach (var n in report.Notes) output.WriteLine($"  note: {n}");
        output.WriteLine(report.Stable ? "Result: Stable" : "Result: NOT stable");

        var json = p.One("--json");
        if (json != null) File.WriteAllText(json, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return report.Stable ? 0 : 1;
    }

    private static int ExecutePolitics(ArgParser p, int games, int turns, string strategyName, TextWriter output)
    {
        if (!Enum.TryParse<PoliticsStrategy>(strategyName, ignoreCase: true, out var strategy))
            p.Errors.Add($"Unknown politics strategy '{strategyName}' (use ignorer, keeper, random, extreme, activist, reformer or challenger).");
        if (games == 0) p.Errors.Add("--games must be at least 1.");
        if (p.Errors.Count > 0)
        {
            foreach (var e in p.Errors) output.WriteLine(e);
            output.WriteLine(Usage);
            return 2;
        }
        var loaded = RunCommand.LoadPacks(p, output);
        if (loaded == null) { output.WriteLine(Usage); return 2; }
        string? scenario = p.One("--scenario") ?? loaded.Packs!.Scenarios.FirstOrDefault(s => s.Politics != null)?.Id;
        if (scenario == null || loaded.Packs!.Scenarios.All(s => s.Id != scenario) || loaded.Packs.GetScenario(scenario).Politics == null)
        {
            output.WriteLine($"Scenario '{scenario}' does not exist or has no politics.");
            return 2;
        }
        if (p.One("--party") == "all")
        {
            // One batch per party: the table of decision 0013 (falls, governing share, how often the player leads, poll).
            output.WriteLine($"Politics batch per player party: {games} game(s) x {turns} turns, strategy '{strategy.ToString().ToLowerInvariant()}'");
            output.WriteLine("party                      role        falls  stuck  governing  leads  lost-lead  poll   capital  cap.lead  changes/y  refused/y");
            var options = PartyChoice.Options(loaded.Packs, scenario);
            int stuckAll = 0;
            foreach (var o in options)
            {
                var r = PoliticsBatch.Run(loaded.Packs, scenario, games, turns, strategy, 1, o.PartyId);
                stuckAll += r.StuckGames;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                output.WriteLine(string.Format(inv, "{0,-26} {1,-11} {2,5}  {3,5}  {4,8:0.0} %  {5,4:0} %  {6,9}  {7,4:0.0} %  {8,7:0.0}  {11,8:0.0}  {9,9:0.00}  {10,9:0.00}",
                    o.PartyId, o.Role.ToString().ToLowerInvariant(), r.Falls, r.StuckGames, 100 * r.GoverningShare, 100 * r.PlayerLeadShare,
                    r.GamesLosingLead, 100 * r.MeanPlayerSupportOverRun, r.MeanCapital, r.PolicyChangesPerYear, r.PolicyRefusalsPerYear, r.MeanCapitalLeading));
            }
            output.WriteLine(stuckAll == 0 ? "Result: Stable" : "Result: NOT stable (games got stuck outside Governing)");
            return stuckAll == 0 ? 0 : 1;
        }
        string? party = RunCommand.ResolveParty(p, loaded.Packs, scenario);
        if (p.Errors.Count > 0) { foreach (var e in p.Errors) output.WriteLine(e); return 2; }
        var report = PoliticsBatch.Run(loaded.Packs, scenario, games, turns, strategy, 1, party);
        if (party != null) output.WriteLine($"Player party: {party}");
        output.Write(report.ToText());
        output.WriteLine(report.StuckGames == 0 ? "Result: Stable" : "Result: NOT stable (games got stuck outside Governing)");
        return report.StuckGames == 0 ? 0 : 1;
    }
}
