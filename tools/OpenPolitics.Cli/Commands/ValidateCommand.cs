using OpenPolitics.Core.Packs;

namespace OpenPolitics.Cli.Commands;

public static class ValidateCommand
{
    /// <summary>validate &lt;pack dir&gt;... Returns 0 if no errors, 1 if errors, 2 on usage problems.</summary>
    public static int Execute(string[] args, TextWriter output)
    {
        if (args.Length == 0)
        {
            output.WriteLine("Usage: validate <pack dir>...  (packs are loaded in the order given)");
            return 2;
        }
        foreach (var a in args)
            if (!Directory.Exists(a))
            {
                output.WriteLine($"Pack directory not found: {a}");
                return 2;
            }

        var result = PackLoader.Load(args.Select(a => (IPackSource)new DirectoryPackSource(a)).ToList());
        foreach (var d in result.Diagnostics) output.WriteLine(d);
        int errors = result.Diagnostics.Count(d => d.Severity == Severity.Error);
        int warnings = result.Diagnostics.Count - errors;
        output.WriteLine(errors == 0
            ? $"OK: {args.Length} pack(s) valid ({warnings} warning(s))."
            : $"FAILED: {errors} error(s), {warnings} warning(s).");
        return errors == 0 ? 0 : 1;
    }
}
