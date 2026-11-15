using OpenPolitics.Cli.Play;
using OpenPolitics.Core.Gameplay;

namespace OpenPolitics.Cli.Commands;

public static class PlayCommand
{
    public const string Usage =
        "Usage: play --packs <dir>... [--scenario <id>] [--party <id>|list] [--seed <n>] [--lang cs|en] [--turns <n>] [--no-tips]";

    public static int Execute(string[] args, TextReader input, TextWriter output)
    {
        var p = new ArgParser(args);
        int turns = p.Int("--turns", 80);
        ulong seed = p.ULong("--seed", 1);
        string lang = p.One("--lang") ?? DefaultLanguage();
        if (p.Errors.Count > 0 || p.Has("--help"))
        {
            foreach (var e in p.Errors) output.WriteLine(e);
            output.WriteLine(Usage);
            return 2;
        }
        var loaded = RunCommand.LoadPacks(p, output);
        if (loaded == null) { output.WriteLine(Usage); return 2; }
        string? scenario = p.One("--scenario") ?? (loaded.Packs!.Scenarios.FirstOrDefault(s => s.Politics != null) ?? loaded.Packs!.Scenarios.FirstOrDefault())?.Id;
        if (scenario == null || loaded.Packs!.Scenarios.All(s => s.Id != scenario))
        {
            output.WriteLine($"Unknown scenario '{scenario}'.");
            return 2;
        }
        if (p.One("--party") == "list")
        {
            var t = new OpenPolitics.Core.Presentation.Localizer(loaded.Packs, lang);
            foreach (var o in PartyChoice.Options(loaded.Packs, scenario))
                output.WriteLine($"  {o.PartyId,-26}{t.Name(o.PartyId),-26}{o.Support * 100,5:0.0} %  {o.Seats,3}  {t.T("ui.role." + o.Role.ToString().ToLowerInvariant())}");
            return 0;
        }
        string? party = RunCommand.ResolveParty(p, loaded.Packs, scenario);
        if (p.Errors.Count > 0) { foreach (var e in p.Errors) output.WriteLine(e); output.WriteLine(Usage); return 2; }
        return new PlayLoop(loaded.Packs, scenario, seed, lang, turns, !p.Has("--no-tips"), input, output, party).Run();
    }

    private static string DefaultLanguage()
    {
        string? env = Environment.GetEnvironmentVariable("LC_ALL") ?? Environment.GetEnvironmentVariable("LANG");
        return env != null && env.StartsWith("cs", StringComparison.OrdinalIgnoreCase) ? "cs" : "en";
    }
}
