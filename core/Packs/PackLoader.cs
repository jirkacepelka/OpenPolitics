namespace OpenPolitics.Core.Packs;

public sealed record LoadResult(PackSet? Packs, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Ok => Packs != null && Diagnostics.All(d => d.Severity != Severity.Error);
}

/// <summary>Loads packs in the given order and merges them: a later pack wins, but only with <c>override: true</c>.</summary>
public static class PackLoader
{
    public static LoadResult Load(IReadOnlyList<IPackSource> sources)
    {
        var diags = new List<Diagnostic>();
        var loadedIds = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var loc = new Dictionary<string, (string File, int Line)>();
        var values = new Dictionary<string, ValueDef>(); var policies = new Dictionary<string, PolicyDef>();
        var edges = new Dictionary<string, EdgeDef>(); var groups = new Dictionary<string, VoterGroupDef>();
        var events = new Dictionary<string, EventDef>(); var countries = new Dictionary<string, CountryDef>();
        var scenarios = new Dictionary<string, ScenarioDef>();
        var parties = new Dictionary<string, PartyDef>(); var electoral = new Dictionary<string, ElectoralSystemDef>();
        var owner = new Dictionary<string, string>(); // kind/id -> pack id
        var localization = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        int packs = 0;

        foreach (var source in sources)
        {
            var (pack, d) = PackReader.Read(source);
            diags.AddRange(d);
            if (pack == null) continue;
            packs++;
            string pid = pack.Manifest.Id;
            foreach (var dep in pack.Manifest.Dependencies)
                if (!loadedIds.Contains(dep))
                    diags.Add(new Diagnostic(Severity.Error, "E009",
                        $"Pack '{pid}' depends on '{dep}', which is not loaded before it.", "manifest.yaml", 0));
            if (!loadedIds.Add(pid))
                diags.Add(new Diagnostic(Severity.Error, "E003", $"Pack id '{pid}' is loaded twice.", "manifest.yaml", 0));
            hashes[pid] = pack.Hash;

            Merge("value", pack, pack.Values, v => v.Id, values, owner, loc, diags);
            Merge("policy", pack, pack.Policies, v => v.Id, policies, owner, loc, diags);
            Merge("edge", pack, pack.Edges, v => v.Id, edges, owner, loc, diags);
            Merge("voter_group", pack, pack.VoterGroups, v => v.Id, groups, owner, loc, diags);
            Merge("event", pack, pack.Events, v => v.Id, events, owner, loc, diags);
            Merge("country", pack, pack.Countries, v => v.Id, countries, owner, loc, diags);
            Merge("scenario", pack, pack.Scenarios, v => v.Id, scenarios, owner, loc, diags);
            Merge("party", pack, pack.Parties, v => v.Id, parties, owner, loc, diags);
            Merge("electoral_system", pack, pack.ElectoralSystems, v => v.Id, electoral, owner, loc, diags);
            foreach (var (lang, table) in pack.Localization)
            {
                if (!localization.TryGetValue(lang, out var merged))
                    localization[lang] = merged = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (k, v) in table) merged[k] = v; // later pack wins for text
            }
        }

        if (packs == 0) return new LoadResult(null, diags);

        var loc2 = localization.ToDictionary(kv => kv.Key,
            kv => (IReadOnlyDictionary<string, string>)kv.Value, StringComparer.Ordinal);
        var set = new PackSet(values.Values, policies.Values, edges.Values, groups.Values, events.Values,
            countries.Values, scenarios.Values, loc2, hashes, loc, parties.Values, electoral.Values);
        diags.AddRange(PackValidator.Validate(set));
        return new LoadResult(set, diags);
    }

    private static void Merge<T>(string kind, RawPack pack, List<T> incoming, Func<T, string> idOf,
        Dictionary<string, T> into, Dictionary<string, string> owner,
        Dictionary<string, (string File, int Line)> loc, List<Diagnostic> diags) where T : class
    {
        foreach (var def in incoming)
        {
            string id = idOf(def), key = kind + "/" + id;
            var origin = pack.Origins[def];
            if (into.ContainsKey(key == "" ? id : id))
            {
                bool samePack = owner[key] == pack.Manifest.Id;
                if (samePack || !pack.Overrides.Contains(id))
                {
                    var first = loc[key];
                    diags.Add(new Diagnostic(Severity.Error, "E003",
                        $"Duplicate {kind} id '{id}' (first defined at {first.File}:{first.Line}); " +
                        (samePack ? "ids must be unique within a pack." : "set 'override: true' to replace it."),
                        origin.File, origin.Line));
                    continue;
                }
            }
            into[id] = def;
            owner[key] = pack.Manifest.Id;
            loc[key] = origin;
        }
    }
}
