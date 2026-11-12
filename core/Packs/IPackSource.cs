namespace OpenPolitics.Core.Packs;

/// <summary>Abstraction over where a pack's files come from (directory, memory, ...).</summary>
public interface IPackSource
{
    string Name { get; }
    /// <summary>Relative paths with forward slashes, ordinal-sorted.</summary>
    IEnumerable<string> ListFiles();
    string ReadAllText(string relativePath);
}

public sealed class DirectoryPackSource : IPackSource
{
    private readonly string _root;
    public DirectoryPackSource(string path) { _root = path; }
    public string Name => _root;

    public IEnumerable<string> ListFiles() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    public string ReadAllText(string relativePath) =>
        File.ReadAllText(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}

public sealed class InMemoryPackSource : IPackSource
{
    private readonly IDictionary<string, string> _files;
    public InMemoryPackSource(string name, IDictionary<string, string> files) { Name = name; _files = files; }
    public string Name { get; }
    public IEnumerable<string> ListFiles() => _files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    public string ReadAllText(string relativePath) => _files[relativePath];
}
