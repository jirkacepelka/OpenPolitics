namespace OpenPolitics.Core.Packs;

/// <summary>Semantic checks on a merged pack set. Codes: E003-E007 (E008 for expressions, E009 in the loader).</summary>
public static class PackValidator
{
    private static readonly string[] RequiredLanguages = { "cs", "en" };
    private static readonly HashSet<string> FnTypes = new() { "linear", "power", "tanh", "threshold" };
    private static readonly HashSet<string> DelayTypes = new() { "none", "lag", "smooth" };

    public static IReadOnlyList<Diagnostic> Validate(PackSet set)
    {
        var diags = new List<Diagnostic>();
        var valueIds = new HashSet<string>(set.Values.Select(v => v.Id), StringComparer.Ordinal);
        var policyIds = new HashSet<string>(set.Policies.Select(v => v.Id), StringComparer.Ordinal);
        var countryIds = new HashSet<string>(set.Countries.Select(v => v.Id), StringComparer.Ordinal);

        void Err(string code, string kind, string id, string message)
        {
            var (file, line) = set.Where(kind, id);
            diags.Add(new Diagnostic(Severity.Error, code, message, file, line));
        }

        void Ref(string kind, string id, string what, string target, ISet<string> allowed, string targetKind)
        {
            if (!allowed.Contains(target)) Err("E004", kind, id, $"{what} refers to unknown {targetKind} '{target}'.");
        }

        // E003: a value and a policy must not share an id (edge sources would be ambiguous).
        foreach (var p in set.Policies)
            if (valueIds.Contains(p.Id)) Err("E003", "policy", p.Id, $"Policy id '{p.Id}' is also used by a value.");

        foreach (var v in set.Values)
        {
            if (!(v.Scale > 0)) Err("E007", "value", v.Id, $"Value '{v.Id}' needs scale > 0.");
        }
        foreach (var p in set.Policies)
        {
            if (p.Min > p.Max) Err("E007", "policy", p.Id, $"Policy '{p.Id}' has min greater than max.");
            else if (p.Default < p.Min || p.Default > p.Max)
                Err("E007", "policy", p.Id, $"Policy '{p.Id}' default {p.Default} is outside [{p.Min}, {p.Max}].");
            if (p.DelayTurns < 0) Err("E007", "policy", p.Id, $"Policy '{p.Id}' has negative delay_turns.");
            if (p.RevenueBaseValueId != null) Ref("policy", p.Id, $"Policy '{p.Id}' revenue_base", p.RevenueBaseValueId, valueIds, "value");
            if (p.CostValueId != null) Ref("policy", p.Id, $"Policy '{p.Id}' cost_value", p.CostValueId, valueIds, "value");
        }
        foreach (var e in set.Edges)
        {
            if (!valueIds.Contains(e.From) && !policyIds.Contains(e.From))
                Err("E004", "edge", e.Id, $"Edge '{e.Id}' refers to unknown source '{e.From}'.");
            Ref("edge", e.Id, $"Edge '{e.Id}' target", e.To, valueIds, "value");
            if (!FnTypes.Contains(e.Fn.Type)) Err("E007", "edge", e.Id, $"Edge '{e.Id}' has unknown function type '{e.Fn.Type}'.");
            if (!DelayTypes.Contains(e.Delay.Type)) Err("E007", "edge", e.Id, $"Edge '{e.Id}' has unknown delay type '{e.Delay.Type}'.");
            else if (e.Delay.Turns < 0) Err("E007", "edge", e.Id, $"Edge '{e.Id}' has negative delay turns.");
        }
        foreach (var g in set.VoterGroups)
        {
            foreach (var k in g.ValueWeights.Keys) Ref("voter_group", g.Id, $"Voter group '{g.Id}' value_weights", k, valueIds, "value");
            foreach (var k in g.PolicyPrefs.Keys) Ref("voter_group", g.Id, $"Voter group '{g.Id}' policy_prefs", k, policyIds, "policy");
            foreach (var k in g.PolicyWeights.Keys) Ref("voter_group", g.Id, $"Voter group '{g.Id}' policy_weights", k, policyIds, "policy");
        }
        foreach (var ev in set.Events)
        {
            if (!(ev.Probability >= 0 && ev.Probability <= 1))
                Err("E007", "event", ev.Id, $"Event '{ev.Id}' probability {ev.Probability} is outside [0, 1].");
            foreach (var fx in ev.Effects) Ref("event", ev.Id, $"Event '{ev.Id}' effect", fx.ValueId, valueIds, "value");
        }
        foreach (var s in set.Scenarios)
        {
            Ref("scenario", s.Id, $"Scenario '{s.Id}' country", s.CountryId, countryIds, "country");
            if (s.TurnsPerYear <= 0) Err("E007", "scenario", s.Id, $"Scenario '{s.Id}' needs turns_per_year > 0.");
            foreach (var k in s.StartValues.Keys) Ref("scenario", s.Id, $"Scenario '{s.Id}' start_values", k, valueIds, "value");
            foreach (var (k, val) in s.StartPolicies)
            {
                Ref("scenario", s.Id, $"Scenario '{s.Id}' start_policies", k, policyIds, "policy");
                var pol = set.Policies.FirstOrDefault(p => p.Id == k);
                if (pol != null && (val < pol.Min || val > pol.Max))
                    Err("E007", "scenario", s.Id, $"Scenario '{s.Id}' start policy '{k}' = {val} is outside [{pol.Min}, {pol.Max}].");
            }
        }

        CheckZeroDelayCycles(set, Err);
        CheckLocalization(set, diags);
        return diags;
    }

    private static void CheckZeroDelayCycles(PackSet set, Action<string, string, string, string> err)
    {
        var edges = set.Edges.Where(e => e.Delay.Type == "none").ToList(); // already id-sorted
        var adj = new SortedDictionary<string, List<EdgeDef>>(StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (!adj.TryGetValue(e.From, out var l)) adj[e.From] = l = new List<EdgeDef>();
            l.Add(e);
            adj.TryAdd(e.To, new List<EdgeDef>());
        }

        // Tarjan SCC over nodes in ordinal order.
        int index = 0;
        var idx = new Dictionary<string, int>(); var low = new Dictionary<string, int>();
        var onStack = new HashSet<string>(); var stack = new Stack<string>();
        var sccs = new List<List<string>>();

        void Strong(string v)
        {
            idx[v] = low[v] = index++;
            stack.Push(v); onStack.Add(v);
            foreach (var e in adj[v])
            {
                if (!idx.ContainsKey(e.To)) { Strong(e.To); low[v] = Math.Min(low[v], low[e.To]); }
                else if (onStack.Contains(e.To)) low[v] = Math.Min(low[v], idx[e.To]);
            }
            if (low[v] == idx[v])
            {
                var comp = new List<string>();
                string w;
                do { w = stack.Pop(); onStack.Remove(w); comp.Add(w); } while (w != v);
                sccs.Add(comp);
            }
        }
        foreach (var n in adj.Keys.ToList()) if (!idx.ContainsKey(n)) Strong(n);

        foreach (var comp in sccs)
        {
            var members = new HashSet<string>(comp);
            var inside = edges.Where(e => members.Contains(e.From) && members.Contains(e.To)).ToList();
            if (inside.Count == 0) continue;
            if (comp.Count == 1 && !inside.Any(e => e.From == e.To)) continue;
            var ids = inside.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
            err("E005", "edge", ids[0],
                $"Edges {string.Join(", ", ids.Select(i => "'" + i + "'"))} form a cycle without delay; give at least one a lag or smooth delay.");
        }
    }

    private static void CheckLocalization(PackSet set, List<Diagnostic> diags)
    {
        var needs = new List<(string Kind, string Id, string Key)>();
        foreach (var v in set.Values) needs.Add(("value", v.Id, "name." + v.Id));
        foreach (var p in set.Policies) needs.Add(("policy", p.Id, "name." + p.Id));
        foreach (var g in set.VoterGroups) needs.Add(("voter_group", g.Id, "name." + g.Id));
        foreach (var c in set.Countries) needs.Add(("country", c.Id, c.NameKey));
        foreach (var e in set.Events) needs.Add(("event", e.Id, e.TextKey));

        foreach (var lang in RequiredLanguages)
        {
            if (!set.Localization.TryGetValue(lang, out var table))
            {
                diags.Add(new Diagnostic(Severity.Error, "E006", $"Localization for language '{lang}' is missing (localization/{lang}.yaml).", null, 0));
                continue;
            }
            foreach (var (kind, id, key) in needs)
                if (!table.ContainsKey(key))
                {
                    var (file, line) = set.Where(kind, id);
                    diags.Add(new Diagnostic(Severity.Error, "E006", $"Missing localization key '{key}' for language '{lang}'.", file, line));
                }
        }
    }
}
