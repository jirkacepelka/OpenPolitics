using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenPolitics.Core.Packs;

/// <summary>Content of one pack before merging.</summary>
public sealed class RawPack
{
    public required PackManifest Manifest { get; init; }
    public List<ValueDef> Values { get; } = new();
    public List<PolicyDef> Policies { get; } = new();
    public List<EdgeDef> Edges { get; } = new();
    public List<VoterGroupDef> VoterGroups { get; } = new();
    public List<EventDef> Events { get; } = new();
    public List<CountryDef> Countries { get; } = new();
    public List<ScenarioDef> Scenarios { get; } = new();
    public Dictionary<string, Dictionary<string, string>> Localization { get; } = new();
    /// <summary>Ids whose YAML entry carries <c>override: true</c>.</summary>
    public HashSet<string> Overrides { get; } = new();
    public string Hash { get; set; } = "";
    /// <summary>Definition (by reference) -> file and line where it was declared.</summary>
    public Dictionary<object, (string File, int Line)> Origins { get; } = new(ReferenceEqualityComparer.Instance);
}

/// <summary>Reads one pack from a source into definitions; problems become diagnostics with file and line.</summary>
public static class PackReader
{
    private static readonly string[] KnownFolders =
        { "values", "policies", "edges", "voter_groups", "events", "countries", "scenarios", "localization", "electoral_systems" };

    public static (RawPack? Pack, IReadOnlyList<Diagnostic> Diagnostics) Read(IPackSource source)
    {
        var diags = new List<Diagnostic>();
        var files = source.ListFiles().ToList();
        var texts = new Dictionary<string, string>();
        foreach (var f in files) texts[f] = Normalize(source.ReadAllText(f));

        if (!texts.TryGetValue("manifest.yaml", out var manifestText))
        {
            diags.Add(new Diagnostic(Severity.Error, "E002", "Pack has no manifest.yaml.", "manifest.yaml", 0));
            return (null, diags);
        }

        var manifestRoot = Parse("manifest.yaml", manifestText, diags);
        var manifest = manifestRoot is YamlMappingNode mm ? ReadManifest(mm, diags) : null;
        if (manifest == null)
        {
            if (manifestRoot != null && manifestRoot is not YamlMappingNode)
                diags.Add(new Diagnostic(Severity.Error, "E002", "manifest.yaml must be a mapping.", "manifest.yaml", (int)manifestRoot.Start.Line));
            return (null, diags);
        }

        var pack = new RawPack { Manifest = manifest, Hash = ComputeHash(files, texts) };
        foreach (var path in files)
        {
            if (path == "manifest.yaml") continue;
            if (!(path.EndsWith(".yaml", StringComparison.Ordinal) || path.EndsWith(".yml", StringComparison.Ordinal))) continue;
            int slash = path.IndexOf('/');
            string folder = slash < 0 ? "" : path[..slash];
            if (Array.IndexOf(KnownFolders, folder) < 0)
            {
                diags.Add(new Diagnostic(Severity.Warning, "W001", $"File is not in a known pack folder and is ignored.", path, 0));
                continue;
            }
            var root = Parse(path, texts[path], diags);
            if (root == null) continue;
            switch (folder)
            {
                case "values": Entries(path, root, diags, pack, ReadValue, pack.Values, v => v.Id); break;
                case "policies": Entries(path, root, diags, pack, ReadPolicy, pack.Policies, v => v.Id); break;
                case "edges": Entries(path, root, diags, pack, ReadEdge, pack.Edges, v => v.Id); break;
                case "voter_groups": Entries(path, root, diags, pack, ReadVoterGroup, pack.VoterGroups, v => v.Id); break;
                case "events": Entries(path, root, diags, pack, ReadEvent, pack.Events, v => v.Id); break;
                case "countries": Entries(path, root, diags, pack, ReadCountry, pack.Countries, v => v.Id); break;
                case "scenarios": Entries(path, root, diags, pack, ReadScenario, pack.Scenarios, v => v.Id); break;
                case "localization": ReadLocalization(path, root, diags, pack); break;
                // electoral_systems: reserved for M2, intentionally not read yet.
            }
        }
        return (pack, diags);
    }

    // ---- text and hashing -------------------------------------------------

    private static string Normalize(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string ComputeHash(List<string> files, Dictionary<string, string> texts)
    {
        var sb = new StringBuilder();
        foreach (var f in files.OrderBy(x => x, StringComparer.Ordinal))
            sb.Append(f).Append('\n').Append(texts[f]).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static YamlNode? Parse(string file, string text, List<Diagnostic> diags)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
        }
        catch (YamlException e)
        {
            diags.Add(new Diagnostic(Severity.Error, "E001", $"YAML syntax error: {e.Message}", file, (int)e.Start.Line));
            return null;
        }
    }

    // ---- generic field access ---------------------------------------------

    private sealed class Fields
    {
        private readonly YamlMappingNode _node;
        private readonly string _file;
        private readonly List<Diagnostic> _diags;
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        public bool Failed { get; private set; }
        public string File => _file;
        public int Line => (int)_node.Start.Line;

        public Fields(YamlMappingNode node, string file, List<Diagnostic> diags)
        { _node = node; _file = file; _diags = diags; }

        public void Report(Diagnostic d)
        {
            if (d.Severity == Severity.Error) Failed = true;
            _diags.Add(d);
        }

        public void Error(YamlNode at, string message)
        {
            Failed = true;
            _diags.Add(new Diagnostic(Severity.Error, "E002", message, _file, (int)at.Start.Line));
        }

        public YamlNode? Get(string key)
        {
            _used.Add(key);
            foreach (var kv in _node.Children)
                if (kv.Key is YamlScalarNode s && s.Value == key) return kv.Value;
            return null;
        }

        public string Str(string key, string? def = null)
        {
            var n = Get(key);
            if (n == null)
            {
                if (def != null) return def;
                Failed = true;
                _diags.Add(new Diagnostic(Severity.Error, "E002", $"Missing required field '{key}'.", _file, Line));
                return "";
            }
            if (n is YamlScalarNode s && s.Value != null) return s.Value;
            Error(n, $"Field '{key}' must be a text value.");
            return "";
        }

        public string? OptStr(string key)
        {
            var n = Get(key);
            if (n == null) return null;
            if (n is YamlScalarNode s && s.Value != null) return s.Value;
            Error(n, $"Field '{key}' must be a text value.");
            return null;
        }

        public double Num(string key, double? def = null)
        {
            var n = Get(key);
            if (n == null)
            {
                if (def.HasValue) return def.Value;
                Failed = true;
                _diags.Add(new Diagnostic(Severity.Error, "E002", $"Missing required field '{key}'.", _file, Line));
                return 0;
            }
            return NumFrom(n, key);
        }

        public double? OptNum(string key)
        {
            var n = Get(key);
            return n == null ? null : NumFrom(n, key);
        }

        private double NumFrom(YamlNode n, string key)
        {
            if (n is YamlScalarNode s && double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && double.IsFinite(v)) return v;
            Error(n, $"Field '{key}' must be a finite number.");
            return 0;
        }

        public bool Bool(string key, bool def)
        {
            var n = Get(key);
            if (n == null) return def;
            if (n is YamlScalarNode s && bool.TryParse(s.Value, out var b)) return b;
            Error(n, $"Field '{key}' must be true or false.");
            return def;
        }

        public int Int(string key, int? def = null)
        {
            double d = Num(key, def);
            if (d != Math.Floor(d) || Math.Abs(d) > int.MaxValue) { Failed = true; _diags.Add(new Diagnostic(Severity.Error, "E002", $"Field '{key}' must be an integer.", _file, Line)); return 0; }
            return (int)d;
        }

        public IReadOnlyDictionary<string, double> NumMap(string key)
        {
            var result = new SortedDictionary<string, double>(StringComparer.Ordinal);
            var n = Get(key);
            if (n == null) return result;
            if (n is not YamlMappingNode m) { Error(n, $"Field '{key}' must be a mapping of id to number."); return result; }
            foreach (var kv in m.Children)
            {
                if (kv.Key is YamlScalarNode k && kv.Value is YamlScalarNode v
                    && double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
                    result[k.Value ?? ""] = d;
                else Error(kv.Key, $"Entries of '{key}' must map an id to a finite number.");
            }
            return result;
        }

        public IReadOnlyList<string> StrList(string key)
        {
            var result = new List<string>();
            var n = Get(key);
            if (n == null) return result;
            if (n is not YamlSequenceNode seq) { Error(n, $"Field '{key}' must be a list."); return result; }
            foreach (var item in seq.Children)
            {
                if (item is YamlScalarNode s && s.Value != null) result.Add(s.Value);
                else Error(item, $"Items of '{key}' must be text.");
            }
            return result;
        }

        public YamlMappingNode? Map(string key)
        {
            var n = Get(key);
            if (n == null) return null;
            if (n is YamlMappingNode m) return m;
            Error(n, $"Field '{key}' must be a mapping.");
            return null;
        }

        public IEnumerable<YamlNode> Seq(string key)
        {
            var n = Get(key);
            if (n == null) return Array.Empty<YamlNode>();
            if (n is YamlSequenceNode s) return s.Children;
            Error(n, $"Field '{key}' must be a list.");
            return Array.Empty<YamlNode>();
        }

        public void ReportUnknown()
        {
            foreach (var kv in _node.Children)
                if (kv.Key is YamlScalarNode s && s.Value != null && !_used.Contains(s.Value))
                    Error(kv.Key, $"Unknown field '{s.Value}'.");
        }
    }

    private static void Entries<T>(string file, YamlNode root, List<Diagnostic> diags, RawPack pack,
        Func<Fields, string, T?> read, List<T> into, Func<T, string> idOf) where T : class
    {
        if (root is not YamlSequenceNode seq)
        {
            diags.Add(new Diagnostic(Severity.Error, "E002", "File must contain a list of entries.", file, (int)root.Start.Line));
            return;
        }
        foreach (var item in seq.Children)
        {
            if (item is not YamlMappingNode map)
            {
                diags.Add(new Diagnostic(Severity.Error, "E002", "Each entry must be a mapping.", file, (int)item.Start.Line));
                continue;
            }
            var f = new Fields(map, file, diags);
            bool ov = f.Bool("override", false);
            var def = read(f, file);
            f.ReportUnknown();
            if (def != null && !f.Failed)
            {
                into.Add(def);
                pack.Origins[def] = (file, (int)map.Start.Line);
                if (ov) pack.Overrides.Add(idOf(def));
            }
        }
    }

    // ---- readers ------------------------------------------------------------

    private static PackManifest? ReadManifest(YamlMappingNode map, List<Diagnostic> diags)
    {
        var f = new Fields(map, "manifest.yaml", diags);
        var m = new PackManifest(f.Str("id"), f.Str("version"), f.StrList("dependencies"), f.StrList("load_after"),
            f.Str("license", ""), f.Bool("illustrative", false));
        f.ReportUnknown();
        return f.Failed ? null : m;
    }

    private static ValueDef? ReadValue(Fields f, string file)
    {
        string id = f.Str("id");
        double initial = f.Num("initial");
        double reference = f.Num("reference", initial);
        double defScale = Math.Abs(reference) * 0.1;
        double scale = f.Num("scale", defScale > 0 ? defScale : 1.0);
        return new ValueDef(id, f.Str("unit", ""), initial, reference, scale, f.OptStr("owner"), file);
    }

    private static PolicyDef? ReadPolicy(Fields f, string file) =>
        new(f.Str("id"), f.Str("unit", ""), f.Num("min"), f.Num("max"), f.Num("default"),
            f.OptStr("cost_value"), f.Num("cost_per_unit", 0), f.Int("delay_turns", 0), f.OptStr("revenue_base"), file);

    private static EdgeDef? ReadEdge(Fields f, string file)
    {
        string id = f.Str("id"), from = f.Str("from"), to = f.Str("to");
        string kindText = f.Str("kind", "additive");
        EdgeKind kind = EdgeKind.Additive;
        if (kindText == "relative") kind = EdgeKind.Relative;
        else if (kindText != "additive") f.Error(f.Get("kind") ?? new YamlScalarNode(kindText), $"Field 'kind' must be 'additive' or 'relative', not '{kindText}'.");
        var fnMap = f.Map("fn");
        var fn = new FnDef("linear", 1, 1, 0, 0);
        if (fnMap != null)
        {
            fn = ReadFn(fnMap, file, f);
        }
        var delayMap = f.Map("delay");
        var delay = new DelayDef("none", 0);
        if (delayMap != null) delay = ReadDelay(delayMap, file, f);
        return new EdgeDef(id, from, to, kind, fn, f.Num("strength", 1), delay, file);
    }

    private static FnDef ReadFn(YamlMappingNode map, string file, Fields parent)
    {
        var diags = new List<Diagnostic>();
        var g = new Fields(map, file, diags);
        var fn = new FnDef(g.Str("type", "linear"), g.Num("p", 1), g.Num("scale", 1), g.Num("at", 0), g.Num("width", 0));
        g.ReportUnknown();
        Forward(diags, parent);
        return fn;
    }

    private static DelayDef ReadDelay(YamlMappingNode map, string file, Fields parent)
    {
        var diags = new List<Diagnostic>();
        var g = new Fields(map, file, diags);
        var d = new DelayDef(g.Str("type", "none"), g.Num("turns", 0));
        g.ReportUnknown();
        Forward(diags, parent);
        return d;
    }

    private static void Forward(List<Diagnostic> nested, Fields parent)
    {
        foreach (var d in nested) parent.Report(d);
    }

    private static VoterGroupDef? ReadVoterGroup(Fields f, string file) =>
        new(f.Str("id"), f.Num("size", 1), f.Num("weight", 1), f.NumMap("value_weights"),
            f.NumMap("policy_prefs"), f.NumMap("policy_weights"), file);

    private static EventDef? ReadEvent(Fields f, string file)
    {
        string id = f.Str("id");
        string cond = f.Str("condition");
        double prob = f.Num("probability", 1);
        string text = f.Str("text_key");
        var effects = new List<EffectDef>();
        foreach (var item in f.Seq("effects"))
        {
            if (item is not YamlMappingNode m) { f.Error(item, "Each effect must be a mapping with 'value' and 'delta'."); continue; }
            var diags = new List<Diagnostic>();
            var g = new Fields(m, file, diags);
            var e = new EffectDef(g.Str("value"), g.Num("delta"));
            g.ReportUnknown();
            Forward(diags, f);
            effects.Add(e);
        }
        return new EventDef(id, cond, prob, effects, text, file);
    }

    private static CountryDef? ReadCountry(Fields f, string file) =>
        new(f.Str("id"), f.Str("name_key"), f.NumMap("parameters"), file);

    private static ScenarioDef? ReadScenario(Fields f, string file) =>
        new(f.Str("id"), f.Str("country"), f.Int("start_year"), f.Int("turns_per_year", 4),
            f.NumMap("start_values"), f.NumMap("start_policies"), file);

    private static void ReadLocalization(string path, YamlNode root, List<Diagnostic> diags, RawPack pack)
    {
        string lang = Path.GetFileNameWithoutExtension(path);
        if (root is not YamlMappingNode map)
        {
            diags.Add(new Diagnostic(Severity.Error, "E002", "Localization file must be a mapping of key to text.", path, (int)root.Start.Line));
            return;
        }
        if (!pack.Localization.TryGetValue(lang, out var table))
            pack.Localization[lang] = table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in map.Children)
        {
            if (kv.Key is YamlScalarNode k && kv.Value is YamlScalarNode v && k.Value != null && v.Value != null) table[k.Value] = v.Value;
            else diags.Add(new Diagnostic(Severity.Error, "E002", "Localization entries must map a key to text.", path, (int)kv.Key.Start.Line));
        }
    }
}
