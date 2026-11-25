using Godot;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Client;

/// <summary>
/// A data pack read through Godot's virtual file system, so a pack bundled into the exported game's .pck
/// (<c>res://data/base</c>, staged by <c>scripts/export.sh</c>) loads the same way as one on disk.
/// </summary>
public sealed class GodotPackSource : IPackSource
{
    private readonly string _root;

    public GodotPackSource(string resPath) { _root = resPath.TrimEnd('/'); }

    public string Name => _root;

    public static bool Exists(string resPath) => DirAccess.DirExistsAbsolute(resPath);

    public IEnumerable<string> ListFiles()
    {
        var files = new List<string>();
        Walk("", files);
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private void Walk(string rel, List<string> files)
    {
        using var dir = DirAccess.Open(rel.Length == 0 ? _root : _root + "/" + rel);
        if (dir == null) return;
        foreach (var f in dir.GetFiles())
        {
            // Exported resources may carry a ".remap" suffix; the packs are plain text and do not, but be tolerant.
            var name = f.EndsWith(".remap") ? f[..^6] : f;
            files.Add(rel.Length == 0 ? name : rel + "/" + name);
        }
        foreach (var d in dir.GetDirectories())
            Walk(rel.Length == 0 ? d : rel + "/" + d, files);
    }

    public string ReadAllText(string relativePath)
    {
        using var f = Godot.FileAccess.Open(_root + "/" + relativePath, Godot.FileAccess.ModeFlags.Read)
            ?? throw new FileNotFoundException(_root + "/" + relativePath);
        return f.GetAsText();
    }
}
