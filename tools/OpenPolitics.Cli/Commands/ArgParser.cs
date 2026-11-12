namespace OpenPolitics.Cli.Commands;

/// <summary>Minimal "--flag value [value...]" parser. Values run until the next token starting with "--".</summary>
internal sealed class ArgParser
{
    private readonly Dictionary<string, List<string>> _flags = new(StringComparer.Ordinal);
    public List<string> Errors { get; } = new();

    public ArgParser(string[] args)
    {
        List<string>? current = null;
        foreach (var a in args)
        {
            if (a.StartsWith("--", StringComparison.Ordinal)) { current = new List<string>(); _flags[a] = current; }
            else if (current != null) current.Add(a);
            else Errors.Add($"Unexpected argument '{a}'.");
        }
    }

    public bool Has(string flag) => _flags.ContainsKey(flag);
    public IReadOnlyList<string> Values(string flag) => _flags.TryGetValue(flag, out var v) ? v : Array.Empty<string>();
    public string? One(string flag) => Values(flag).Count == 1 ? Values(flag)[0] : null;

    public int Int(string flag, int fallback)
    {
        if (!Has(flag)) return fallback;
        if (int.TryParse(One(flag), out var v) && v >= 0) return v;
        Errors.Add($"{flag} needs a non-negative integer.");
        return fallback;
    }

    public ulong ULong(string flag, ulong fallback)
    {
        if (!Has(flag)) return fallback;
        if (ulong.TryParse(One(flag), out var v)) return v;
        Errors.Add($"{flag} needs a non-negative integer.");
        return fallback;
    }
}
