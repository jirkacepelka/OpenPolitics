namespace OpenPolitics.Core.Packs;

/// <summary>Merged, immutable content of all loaded packs. All lists are ordinal-sorted by id.</summary>
public sealed class PackSet
{
    public IReadOnlyList<ValueDef> Values { get; }
    public IReadOnlyList<PolicyDef> Policies { get; }
    public IReadOnlyList<EdgeDef> Edges { get; }
    public IReadOnlyList<VoterGroupDef> VoterGroups { get; }
    public IReadOnlyList<EventDef> Events { get; }
    public IReadOnlyList<CountryDef> Countries { get; }
    public IReadOnlyList<ScenarioDef> Scenarios { get; }
    public IReadOnlyList<PartyDef> Parties { get; }
    public IReadOnlyList<ElectoralSystemDef> ElectoralSystems { get; }
    /// <summary>language -> key -> text</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Localization { get; }
    /// <summary>pack id -> SHA-256 hex of its content</summary>
    public IReadOnlyDictionary<string, string> PackHashes { get; }
    private readonly IReadOnlyDictionary<string, (string File, int Line)> _locations;

    /// <summary>Where a definition was declared. Kinds: value, policy, edge, voter_group, event, country, scenario, party, electoral_system.</summary>
    public (string? File, int Line) Where(string kind, string id) =>
        _locations.TryGetValue(kind + "/" + id, out var l) ? (l.File, l.Line) : (null, 0);

    public PackSet(
        IEnumerable<ValueDef> values, IEnumerable<PolicyDef> policies, IEnumerable<EdgeDef> edges,
        IEnumerable<VoterGroupDef> voterGroups, IEnumerable<EventDef> events,
        IEnumerable<CountryDef> countries, IEnumerable<ScenarioDef> scenarios,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> localization,
        IReadOnlyDictionary<string, string> packHashes,
        IReadOnlyDictionary<string, (string File, int Line)>? locations = null,
        IEnumerable<PartyDef>? parties = null, IEnumerable<ElectoralSystemDef>? electoralSystems = null)
    {
        Parties = Sort(parties ?? Array.Empty<PartyDef>(), v => v.Id);
        ElectoralSystems = Sort(electoralSystems ?? Array.Empty<ElectoralSystemDef>(), v => v.Id);
        _locations = locations ?? new Dictionary<string, (string File, int Line)>();
        Values = Sort(values, v => v.Id);
        Policies = Sort(policies, v => v.Id);
        Edges = Sort(edges, v => v.Id);
        VoterGroups = Sort(voterGroups, v => v.Id);
        Events = Sort(events, v => v.Id);
        Countries = Sort(countries, v => v.Id);
        Scenarios = Sort(scenarios, v => v.Id);
        Localization = localization;
        PackHashes = packHashes;
    }

    private static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, Func<T, string> id) =>
        items.OrderBy(id, StringComparer.Ordinal).ToList();

    public ScenarioDef GetScenario(string id) =>
        Scenarios.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"Unknown scenario '{id}'.");
}
