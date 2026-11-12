using System.Text.Json;
using OpenPolitics.Core.Batch;

namespace OpenPolitics.Cli.Commands;

public static class BatchCommand
{
    public const string Usage =
        "Usage: batch --packs <dir>... [--scenario <id>] [--games <n>] [--strategy passive|random|extreme] [--turns <n>] [--json <file>]\n" +
        "       batch --politics --packs <dir>... [--scenario <id>] [--games <n>] [--strategy ignorer|keeper|random|extreme|activist] [--turns <n>]";

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
            p.Errors.Add($"Unknown politics strategy '{strategyName}' (use ignorer, keeper, random, extreme or activist).");
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
        var report = PoliticsBatch.Run(loaded.Packs, scenario, games, turns, strategy);
        output.Write(report.ToText());
        output.WriteLine(report.StuckGames == 0 ? "Result: Stable" : "Result: NOT stable (games got stuck outside Governing)");
        return report.StuckGames == 0 ? 0 : 1;
    }
}
