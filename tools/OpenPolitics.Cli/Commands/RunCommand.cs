using System.Globalization;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Cli.Commands;

public static class RunCommand
{
    public const string Usage = "Usage: run --packs <dir>... [--scenario <id>] [--seed <n>] [--turns <n>] [--party <id>]  (prints tab-separated values per turn)";

    /// <summary>
    /// The party named by <c>--party</c>: a full id ("base:civic_right") or the part after the colon ("civic_right"). Null when the flag
    /// is absent; on an unknown party or a scenario without politics an error is added and null returned.
    /// </summary>
    internal static string? ResolveParty(ArgParser p, PackSet packs, string scenarioId)
    {
        if (!p.Has("--party")) return null;
        string? token = p.One("--party");
        if (packs.GetScenario(scenarioId).Politics == null) { p.Errors.Add($"Scenario '{scenarioId}' has no politics, so --party cannot be used."); return null; }
        var hit = packs.Parties.FirstOrDefault(x => x.Id == token)
            ?? packs.Parties.FirstOrDefault(x => token != null && x.Id.Split(':').Last().Equals(token, StringComparison.OrdinalIgnoreCase));
        if (hit == null)
            p.Errors.Add($"Unknown party '{token}'. Parties: {string.Join(", ", packs.Parties.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal))}.");
        return hit?.Id;
    }

    internal static LoadResult? LoadPacks(ArgParser p, TextWriter output)
    {
        var dirs = p.Values("--packs");
        if (dirs.Count == 0) { output.WriteLine("--packs <dir>... is required."); return null; }
        foreach (var d in dirs)
            if (!Directory.Exists(d)) { output.WriteLine($"Pack directory not found: {d}"); return null; }
        var result = PackLoader.Load(dirs.Select(d => (IPackSource)new DirectoryPackSource(d)).ToList());
        if (!result.Ok)
        {
            foreach (var d in result.Diagnostics) output.WriteLine(d);
            output.WriteLine("Packs are invalid; run 'validate' for details.");
            return null;
        }
        return result;
    }

    /// <summary>Returns 0 on success, 2 on usage or pack problems.</summary>
    public static int Execute(string[] args, TextWriter output)
    {
        var p = new ArgParser(args);
        int turns = p.Int("--turns", 80);
        ulong seed = p.ULong("--seed", 1);
        if (p.Errors.Count > 0 || p.Has("--help"))
        {
            foreach (var e in p.Errors) output.WriteLine(e);
            output.WriteLine(Usage);
            return 2;
        }
        var loaded = LoadPacks(p, output);
        if (loaded == null) { output.WriteLine(Usage); return 2; }

        string? scenario = p.One("--scenario") ?? (loaded.Packs!.Scenarios.FirstOrDefault(s => s.Politics == null) ?? loaded.Packs!.Scenarios.FirstOrDefault())?.Id;
        if (scenario == null || loaded.Packs!.Scenarios.All(s => s.Id != scenario))
        {
            output.WriteLine($"Unknown scenario '{scenario}'. Available: {string.Join(", ", loaded.Packs!.Scenarios.Select(s => s.Id))}");
            return 2;
        }

        string? party = ResolveParty(p, loaded.Packs, scenario);
        if (p.Errors.Count > 0) { foreach (var e in p.Errors) output.WriteLine(e); return 2; }
        var game = Simulation.NewGame(loaded.Packs, scenario, seed, party);
        var ids = game.State.Countries.Single().Value.Values.Keys.ToList();
        output.WriteLine("turn\tyear\t" + string.Join("\t", ids));
        void Row()
        {
            var s = game.State;
            var values = s.Countries.Single().Value.Values;
            output.WriteLine($"{s.Turn}\t{s.Year.ToString("R", CultureInfo.InvariantCulture)}\t" +
                string.Join("\t", ids.Select(id => values[id].ToString("R", CultureInfo.InvariantCulture))));
        }
        Row();
        for (int t = 0; t < turns; t++) { game.EndTurn(); Row(); }
        return 0;
    }
}
