namespace OpenPolitics.Core.Packs;

public enum Severity { Error, Warning }

/// <summary>A problem found while reading, merging or validating data packs.</summary>
public sealed record Diagnostic(Severity Severity, string Code, string Message, string? File, int Line)
{
    public override string ToString() =>
        File is null
            ? $"{Severity} {Code}: {Message}"
            : $"{File}:{Line}: {Severity} {Code}: {Message}";
}
