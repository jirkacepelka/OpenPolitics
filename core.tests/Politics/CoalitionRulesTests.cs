using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class CoalitionRulesTests
{
    internal static DemandDef D(string id, string policy, string op, double target, double weight = 1) =>
        new(id, "policy", policy, op, target, weight, 8);

    internal static PartyDef P(string id, double price = 0, double taxTarget = 20, DemandDef[]? demands = null, RedLineDef[]? red = null) => new(
        id, "name." + id,
        new Dictionary<string, PlatformPosition> { ["t:tax"] = new(taxTarget, 1) },
        new Dictionary<string, double>(), false,
        demands ?? Array.Empty<DemandDef>(), red ?? Array.Empty<RedLineDef>(), price, new List<FactionDef>(), "x");

    internal static Dictionary<string, PartyDef> Parties(params PartyDef[] ps) => ps.ToDictionary(p => p.Id);
    internal static Dictionary<string, int> Seats(params (string, int)[] xs) => xs.ToDictionary(x => x.Item1, x => x.Item2);
    internal static double Rel(string a, string b) => 0.3;

    private static IReadOnlyList<Refusal> Eval(CoalitionProposal p, Dictionary<string, PartyDef> parties, Dictionary<string, int> seats, int slots = 6) =>
        CoalitionRules.Evaluate(p, parties, seats, 0.5, slots, Rel);

    [Fact]
    public void HasMajorityNeedsMoreThanHalf()
    {
        Assert.False(CoalitionRules.HasMajority(new[] { "a" }, Seats(("a", 100), ("b", 100)), 0.5));
        Assert.True(CoalitionRules.HasMajority(new[] { "a" }, Seats(("a", 101), ("b", 99)), 0.5));
        Assert.True(CoalitionRules.HasMajority(new[] { "a", "c" }, Seats(("a", 60), ("b", 99), ("c", 41)), 0.5));
    }

    [Fact]
    public void RedLineConflictForbiddenRange()
    {
        var upper = new RedLineDef("t:tax", ">=", 40);
        Assert.False(CoalitionRules.DemandConflictsWithRedLine(D("d", "t:tax", "<=", 22), upper));
        Assert.True(CoalitionRules.DemandConflictsWithRedLine(D("d", "t:tax", ">=", 45), upper));
        Assert.False(CoalitionRules.DemandConflictsWithRedLine(D("d", "t:other", ">=", 45), upper));
        Assert.True(CoalitionRules.DemandConflictsWithRedLine(D("d", "t:tax", "<=", 5), new RedLineDef("t:tax", "<=", 8)));
    }

    [Fact]
    public void OppositeDemandsContradict()
    {
        Assert.True(CoalitionRules.DemandsContradict(D("a", "t:tax", ">=", 30), D("b", "t:tax", "<=", 20)));
        Assert.False(CoalitionRules.DemandsContradict(D("a", "t:tax", ">=", 20), D("b", "t:tax", "<=", 30)));
    }

    [Fact]
    public void SameDirectionDemandsDoNot()
    {
        Assert.False(CoalitionRules.DemandsContradict(D("a", "t:tax", ">=", 30), D("b", "t:tax", ">=", 20)));
        Assert.False(CoalitionRules.DemandsContradict(D("a", "t:tax", ">=", 30), D("b", "t:other", "<=", 1)));
    }

    private static readonly Dictionary<string, PartyDef> Trio = Parties(
        P("a"),
        P("b", price: 4, demands: new[] { D("d1", "t:tax", "<=", 22, 3), D("d2", "t:tax", "<=", 25, 0.4) }),
        P("c", price: 2, red: new[] { new RedLineDef("t:tax", "<=", 10) }));

    [Fact]
    public void AcceptsAValidCoalition()
    {
        var p = new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "d1"), new AgreedDemand("b", "d2") });
        Assert.Empty(Eval(p, Trio, Seats(("a", 90), ("b", 40), ("c", 70))));
    }

    [Fact]
    public void RefusesWithoutMajority()
    {
        var p = new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "d1"), new AgreedDemand("b", "d2") });
        Assert.Contains(Eval(p, Trio, Seats(("a", 60), ("b", 40), ("c", 100))), r => r.Reason == "majority");
    }

    [Fact]
    public void RefusesWhenDemandHitsAPartnersRedLine()
    {
        var parties = Parties(P("a"), P("b", demands: new[] { D("low", "t:tax", "<=", 8, 5) }),
            P("c", red: new[] { new RedLineDef("t:tax", "<=", 10) }));
        var p = new CoalitionProposal("a", new[] { "b", "c" }, new[] { new AgreedDemand("b", "low") });
        var r = Assert.Single(Eval(p, parties, Seats(("a", 80), ("b", 60), ("c", 60))), x => x.Reason == "red_line");
        Assert.Equal(("b", "low"), (r.PartyId, r.DemandId));
    }

    [Fact]
    public void RefusesWhenPriceNotMet()
    {
        var parties = Parties(P("a"), P("b", price: 4, demands: new[] { D("x", "t:tax", "<=", 22, 3), D("y", "t:tax", "<=", 23, 3.5) }));
        var seats = Seats(("a", 90), ("b", 90));
        var low = new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "x") });
        var high = new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "y") });
        Assert.Contains(Eval(low, parties, seats), r => r.Reason == "price" && r.PartyId == "b");
        Assert.Empty(Eval(high, parties, seats));
    }

    [Fact]
    public void RefusesTooManyAgreements()
    {
        var p = new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "d1"), new AgreedDemand("b", "d2") });
        Assert.Contains(Eval(p, Trio, Seats(("a", 90), ("b", 40), ("c", 70)), slots: 1), r => r.Reason == "slots");
    }

    [Fact]
    public void UnknownPartyOrDemandIsRefusedNotThrown()
    {
        var seats = Seats(("a", 90), ("b", 40), ("c", 70));
        Assert.Contains(Eval(new CoalitionProposal("a", new[] { "ghost" }, Array.Empty<AgreedDemand>()), Trio, seats), r => r.Reason == "unknown");
        Assert.Contains(Eval(new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "nope") }), Trio, seats), r => r.Reason == "unknown");
        Assert.Contains(Eval(new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("c", "d1") }), Trio, seats), r => r.Reason == "unknown");
    }

    [Fact]
    public void AllFindingsAreReported()
    {
        var p = new CoalitionProposal("a", new[] { "b" }, Array.Empty<AgreedDemand>());
        var reasons = Eval(p, Trio, Seats(("a", 40), ("b", 40), ("c", 120))).Select(r => r.Reason).ToList();
        Assert.Contains("majority", reasons);
        Assert.Contains("price", reasons);
    }

    [Fact]
    public void ContradictingAgreedDemandsAreRefused()
    {
        var parties = Parties(P("a"), P("b", demands: new[] { D("up", "t:tax", ">=", 30, 5) }), P("c", demands: new[] { D("down", "t:tax", "<=", 20, 5) }));
        var p = new CoalitionProposal("a", new[] { "b", "c" }, new[] { new AgreedDemand("b", "up"), new AgreedDemand("c", "down") });
        Assert.Contains(Eval(p, parties, Seats(("a", 40), ("b", 80), ("c", 80))), r => r.Reason == "contradiction");
    }

    [Fact]
    public void DistanceIsZeroForIdenticalPlatformsAndHalfWhenNothingShared()
    {
        var range = new Dictionary<string, double> { ["t:tax"] = 40 };
        Assert.Equal(0.0, PartyMath.Distance(P("a", taxTarget: 20), P("b", taxTarget: 20), range));
        Assert.Equal(0.25, PartyMath.Distance(P("a", taxTarget: 20), P("b", taxTarget: 30), range), 12);
        var other = new PartyDef("c", "n", new Dictionary<string, PlatformPosition> { ["t:x"] = new(1, 1) },
            new Dictionary<string, double>(), false, new List<DemandDef>(), new List<RedLineDef>(), 0, new List<FactionDef>(), "x");
        Assert.Equal(0.5, PartyMath.Distance(P("a"), other, range));
    }
}
