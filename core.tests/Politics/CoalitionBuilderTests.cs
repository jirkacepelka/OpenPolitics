using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.CoalitionRulesTests;

namespace OpenPolitics.Core.Tests.Politics;

public class CoalitionBuilderTests
{
    private static readonly Dictionary<string, double> Range = new() { ["t:tax"] = 40 };
    private static readonly HashSet<string> None = new();

    private static CoalitionProposal? Build(string formateur, Dictionary<string, PartyDef> parties, Dictionary<string, int> seats,
        int slots = 6, ISet<string>? excluded = null) =>
        CoalitionBuilder.Build(formateur, parties, seats, Range, 0.5, slots, Rel, excluded ?? None);

    [Fact]
    public void FormateurWithAMajorityGetsASoloGovernment()
    {
        var r = Build("a", Parties(P("a"), P("b")), Seats(("a", 120), ("b", 80)))!;
        Assert.Empty(r.Partners);
        Assert.Empty(r.Agreements);
    }

    [Fact]
    public void PicksTheIdeologicallyClosestMinimalCoalition()
    {
        var parties = Parties(P("a", taxTarget: 20), P("near", taxTarget: 22), P("far", taxTarget: 40));
        var r = Build("a", parties, Seats(("a", 90), ("near", 60), ("far", 60)))!;
        Assert.Equal(new[] { "near" }, r.Partners);
    }

    [Fact]
    public void ResultIsMinimalWinning()
    {
        var parties = Parties(P("a", taxTarget: 20), P("b", taxTarget: 21), P("c", taxTarget: 22), P("d", taxTarget: 23));
        var seats = Seats(("a", 70), ("b", 50), ("c", 40), ("d", 40));
        var r = Build("a", parties, seats)!;
        var all = new[] { "a" }.Concat(r.Partners).ToList();
        Assert.True(CoalitionRules.HasMajority(all, seats, 0.5));
        foreach (var p in r.Partners)
            Assert.False(CoalitionRules.HasMajority(all.Where(x => x != p), seats, 0.5), $"{p} is not needed");
    }

    [Fact]
    public void AllocatesDemandsToMeetEachPrice()
    {
        var parties = Parties(P("a"), P("b", price: 4, demands: new[] { D("x", "t:tax", "<=", 22, 3), D("y", "t:tax", "<=", 25, 2), D("z", "t:tax", "<=", 30, 1) }));
        var r = Build("a", parties, Seats(("a", 90), ("b", 90)))!;
        double w = r.Agreements.Sum(a => parties["b"].Demands.Single(d => d.Id == a.DemandId).Weight);
        Assert.True(w >= 4 * 0.85);
        Assert.Equal(new[] { "x", "y" }, r.Agreements.Select(a => a.DemandId).ToArray()); // highest weight first, stop once met
    }

    [Fact]
    public void SkipsDemandsThatHitRedLines()
    {
        var parties = Parties(
            P("a", red: new[] { new RedLineDef("t:tax", "<=", 10) }),
            P("b", price: 2, demands: new[] { D("low", "t:tax", "<=", 8, 5), D("ok", "t:tax", "<=", 20, 2.5) }));
        var r = Build("a", parties, Seats(("a", 90), ("b", 90)))!;
        Assert.Equal(new[] { "ok" }, r.Agreements.Select(x => x.DemandId).ToArray());
    }

    [Fact]
    public void FailsWhenProgramSlotsAreTooFew()
    {
        var parties = Parties(P("a"), P("b", price: 4, demands: new[] { D("x", "t:tax", "<=", 22, 1), D("y", "t:tax", "<=", 25, 1), D("z", "t:tax", "<=", 30, 1), D("w", "t:tax", "<=", 31, 1) }));
        Assert.Null(Build("a", parties, Seats(("a", 90), ("b", 90)), slots: 2));
    }

    [Fact]
    public void ExcludedPartiesAreNeverChosen()
    {
        var parties = Parties(P("a", taxTarget: 20), P("near", taxTarget: 21), P("far", taxTarget: 40));
        var r = Build("a", parties, Seats(("a", 90), ("near", 60), ("far", 60)), excluded: new HashSet<string> { "near" })!;
        Assert.Equal(new[] { "far" }, r.Partners);
    }

    [Fact]
    public void ReturnsNullWhenNoMajorityIsPossible()
    {
        var parties = Parties(P("a"), P("b"), P("c"));
        Assert.Null(Build("a", parties, Seats(("a", 90), ("b", 60), ("c", 50)), excluded: new HashSet<string> { "b", "c" }));
    }

    private static (Dictionary<string, PartyDef>, Dictionary<string, int>) Random(Pcg32 rng)
    {
        int n = 3 + rng.NextInt(7);
        var parties = new List<PartyDef>();
        var seats = new Dictionary<string, int>();
        int left = 200;
        for (int i = 0; i < n; i++)
        {
            string id = $"p{i}";
            var demands = Enumerable.Range(0, 3).Select(k => D($"d{k}", "t:tax", rng.NextInt(2) == 0 ? "<=" : ">=", 5 + rng.NextInt(40), 0.5 + 3.5 * rng.NextDouble())).ToArray();
            var red = rng.NextInt(2) == 0 ? new[] { new RedLineDef("t:tax", rng.NextInt(2) == 0 ? "<=" : ">=", 5 + rng.NextInt(40)) } : Array.Empty<RedLineDef>();
            parties.Add(P(id, price: 6 * rng.NextDouble(), taxTarget: 5 + rng.NextInt(40), demands: demands, red: red));
            int s = i == n - 1 ? left : Math.Min(left, rng.NextInt(1 + 2 * left / (n - i)));
            seats[id] = s; left -= s;
        }
        return (parties.ToDictionary(p => p.Id), seats);
    }

    [Fact]
    public void OutputAlwaysPassesEvaluate()
    {
        var rng = new Pcg32(2024);
        int built = 0;
        for (int i = 0; i < 1000; i++)
        {
            var (parties, seats) = Random(rng);
            foreach (var f in parties.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var r = Build(f, parties, seats);
                if (r is null) continue;
                built++;
                Assert.Empty(CoalitionRules.Evaluate(r, parties, seats, 0.5, 6, Rel));
            }
        }
        Assert.True(built > 1000, $"only {built} proposals built");
    }

    [Fact]
    public void IsDeterministic()
    {
        var (parties, seats) = Random(new Pcg32(5));
        var f = parties.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
        var a = Build(f, parties, seats);
        var b = Build(f, parties, seats);
        Assert.Equal(a?.Partners, b?.Partners);
        Assert.Equal(a?.Agreements, b?.Agreements);
    }
}
