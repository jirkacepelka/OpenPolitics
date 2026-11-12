using OpenPolitics.Core.Graph;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Graph;

public class GraphEvaluatorTests
{
    private static ValueDef V(string id, double reference = 10, double scale = 5) =>
        new(id, "u", reference, reference, scale, null, "f");

    private static PolicyDef P(string id) => new(id, "percent", 0, 100, 20, null, 0, 0, null, "f");

    private static EdgeDef E(string id, string from, string to, double strength = 1,
        EdgeKind kind = EdgeKind.Additive, string delay = "none", double turns = 0) =>
        new(id, from, to, kind, new FnDef("linear", 1, 1, 0, 0), strength, new DelayDef(delay, turns), "f");

    private static Dictionary<string, double> Vals(double a, double b = 0) => new() { ["t:a"] = a, ["t:b"] = b };

    private static readonly Dictionary<string, double> NoPolicies = new();

    [Fact]
    public void AdditiveEdgeProducesRate()
    {
        var g = new GraphEvaluator(new[] { E("t:e", "t:a", "t:b", 2) }, new[] { V("t:a"), V("t:b") }, Array.Empty<PolicyDef>());
        var r = g.Evaluate(Vals(15), NoPolicies);
        Assert.Equal(2.0, r.Rates["t:b"].Additive, 12);
        Assert.Equal(0.0, r.Rates["t:b"].Relative, 12);
    }

    [Fact]
    public void RelativeEdgeFillsRelativeAsFractionPerYear()
    {
        var g = new GraphEvaluator(new[] { E("t:e", "t:a", "t:b", 2, EdgeKind.Relative) }, new[] { V("t:a"), V("t:b") }, Array.Empty<PolicyDef>());
        var r = g.Evaluate(Vals(15), NoPolicies);
        Assert.Equal(0.02, r.Rates["t:b"].Relative, 12);   // strength 2 percent/year
        Assert.Equal(0.0, r.Rates["t:b"].Additive, 12);
        Assert.Equal(2.0, r.Contributions["t:b"].Single().Amount, 12); // contributions stay in edge units
    }

    [Fact]
    public void ContributionsSumToRateAndAreInEdgeIdOrder()
    {
        var edges = new[] { E("t:z", "t:a", "t:b", 3), E("t:m", "t:a", "t:b", -1), E("t:c", "t:a", "t:b", 0.5) };
        var g = new GraphEvaluator(edges, new[] { V("t:a"), V("t:b") }, Array.Empty<PolicyDef>());
        var r = g.Evaluate(Vals(20), NoPolicies);
        var c = r.Contributions["t:b"];
        Assert.Equal(new[] { "t:c", "t:m", "t:z" }, c.Select(x => x.EdgeId).ToArray());
        Assert.All(c, x => Assert.Equal("t:a", x.FromId));
        Assert.Equal(r.Rates["t:b"].Additive, c.Sum(x => x.Amount), 12);
    }

    [Fact]
    public void PolicySourceUsesPolicyDefaultAsReference()
    {
        var g = new GraphEvaluator(new[] { E("t:e", "t:tax", "t:b") }, new[] { V("t:b") }, new[] { P("t:tax") });
        var r = g.Evaluate(new Dictionary<string, double> { ["t:b"] = 0 }, new Dictionary<string, double> { ["t:tax"] = 30 });
        Assert.Equal(0.1, r.Rates["t:b"].Additive, 12); // (30-20)/(100-0)
    }

    [Fact]
    public void LagDelaysEffect()
    {
        var g = new GraphEvaluator(new[] { E("t:e", "t:a", "t:b", 1, EdgeKind.Additive, "lag", 1) }, new[] { V("t:a"), V("t:b") }, Array.Empty<PolicyDef>());
        double Step(double a) => g.Evaluate(Vals(a), NoPolicies).Rates["t:b"].Additive;
        Assert.Equal(0.0, Step(10), 12);  // deviation 0
        Assert.Equal(0.0, Step(15), 12);  // still the old influence
        Assert.Equal(1.0, Step(15), 12);  // now the new one
    }

    [Fact]
    public void EvaluationDoesNotDependOnEdgeListOrder()
    {
        var edges = new[] { E("t:e1", "t:a", "t:b", 3), E("t:e2", "t:b", "t:a", -1, EdgeKind.Relative, "lag", 1), E("t:e3", "t:a", "t:b", 0.7) };
        var values = new[] { V("t:a"), V("t:b") };
        var g1 = new GraphEvaluator(edges, values, Array.Empty<PolicyDef>());
        var g2 = new GraphEvaluator(edges.Reverse().ToArray(), values.Reverse().ToArray(), Array.Empty<PolicyDef>());
        for (int i = 0; i < 4; i++)
        {
            var a = g1.Evaluate(Vals(12 + i, 9 - i), NoPolicies);
            var b = g2.Evaluate(Vals(12 + i, 9 - i), NoPolicies);
            Assert.Equal(a.Rates.OrderBy(k => k.Key).ToArray(), b.Rates.OrderBy(k => k.Key).ToArray());
            Assert.Equal(a.Contributions["t:b"], b.Contributions["t:b"]);
        }
    }

    [Fact]
    public void SnapshotRestoreContinuesIdentically()
    {
        var edges = new[] { E("t:e", "t:a", "t:b", 1, EdgeKind.Additive, "lag", 2), E("t:s", "t:b", "t:a", 1, EdgeKind.Additive, "smooth", 3) };
        var values = new[] { V("t:a"), V("t:b") };
        var g1 = new GraphEvaluator(edges, values, Array.Empty<PolicyDef>());
        for (int i = 0; i < 5; i++) g1.Evaluate(Vals(10 + i, 5 + i), NoPolicies);
        var g2 = new GraphEvaluator(edges, values, Array.Empty<PolicyDef>());
        g2.Restore(g1.Snapshot());
        for (int i = 0; i < 5; i++)
        {
            var a = g1.Evaluate(Vals(20 - i, 3 * i), NoPolicies);
            var b = g2.Evaluate(Vals(20 - i, 3 * i), NoPolicies);
            Assert.Equal(a.Rates.OrderBy(k => k.Key).ToArray(), b.Rates.OrderBy(k => k.Key).ToArray());
        }
    }
}
