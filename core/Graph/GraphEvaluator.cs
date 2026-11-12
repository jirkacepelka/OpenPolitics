using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Graph;

/// <summary>
/// Net influence on a target. Additive is in value units per year. Relative is a growth rate as a FRACTION per year
/// (edge strengths of relative edges are written in percent per year and divided by 100 here).
/// </summary>
public readonly record struct Rate(double Additive, double Relative);

/// <summary>One edge's share of a target's change, in the edge's own units (units/year or percent/year).</summary>
public readonly record struct Contribution(string EdgeId, string FromId, double Amount);

public sealed record EdgeEvaluation(
    IReadOnlyDictionary<string, Rate> Rates,
    IReadOnlyDictionary<string, IReadOnlyList<Contribution>> Contributions);

/// <summary>
/// Evaluates all edges synchronously against the state at the start of a turn. Edges are processed in ordinal
/// edge-id order, so the result does not depend on the order of the input lists. Each edge owns a delay line.
/// </summary>
public sealed class GraphEvaluator
{
    private sealed class Runtime(EdgeDef def, DelayLine line, double reference, double scale)
    {
        public EdgeDef Def { get; } = def;
        public DelayLine Line { get; } = line;
        public double Reference { get; } = reference;
        public double Scale { get; } = scale;
    }

    private readonly List<Runtime> _edges;

    public GraphEvaluator(IReadOnlyList<EdgeDef> edges, IReadOnlyList<ValueDef> values, IReadOnlyList<PolicyDef> policies)
    {
        var valueDefs = values.ToDictionary(v => v.Id);
        var policyDefs = policies.ToDictionary(p => p.Id);
        _edges = edges.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e =>
        {
            double reference = 0, scale = 1;
            if (valueDefs.TryGetValue(e.From, out var v)) { reference = v.Reference; scale = v.Scale; }
            else if (policyDefs.TryGetValue(e.From, out var p)) { reference = p.Default; scale = p.Max - p.Min; }
            return new Runtime(e, new DelayLine(e.Delay), reference, scale);
        }).ToList();
    }

    public EdgeEvaluation Evaluate(IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, double> policyParams)
    {
        var add = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var rel = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var contribs = new SortedDictionary<string, List<Contribution>>(StringComparer.Ordinal);

        foreach (var e in _edges)
        {
            double x = values.TryGetValue(e.Def.From, out var vx) ? vx
                : policyParams.TryGetValue(e.Def.From, out var px) ? px
                : e.Reference; // unknown source: no influence
            double f = EdgeFunctions.Apply(e.Def.Fn, x, e.Reference, e.Scale);
            double amount = e.Line.Push(f) * e.Def.Strength;

            string to = e.Def.To;
            if (e.Def.Kind == EdgeKind.Additive) add[to] = add.GetValueOrDefault(to) + amount;
            else rel[to] = rel.GetValueOrDefault(to) + amount / 100.0;
            if (!add.ContainsKey(to)) add[to] = 0;
            if (!rel.ContainsKey(to)) rel[to] = 0;

            if (!contribs.TryGetValue(to, out var list)) contribs[to] = list = new List<Contribution>();
            list.Add(new Contribution(e.Def.Id, e.Def.From, amount));
        }

        var rates = new SortedDictionary<string, Rate>(StringComparer.Ordinal);
        foreach (var to in add.Keys) rates[to] = new Rate(add[to], rel[to]);
        return new EdgeEvaluation(rates,
            contribs.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Contribution>)kv.Value));
    }

    public (string EdgeId, double[] State)[] Snapshot() =>
        _edges.Select(e => (e.Def.Id, e.Line.Snapshot())).ToArray();

    public void Restore((string EdgeId, double[] State)[] snapshot)
    {
        var map = snapshot.ToDictionary(s => s.EdgeId, s => s.State);
        for (int i = 0; i < _edges.Count; i++)
        {
            var e = _edges[i];
            if (map.TryGetValue(e.Def.Id, out var state))
                _edges[i] = new Runtime(e.Def, DelayLine.FromSnapshot(e.Def.Delay, state), e.Reference, e.Scale);
        }
    }
}
