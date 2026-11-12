using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class SupportMomentumTests
{
    private static readonly PolicyDef Tax = new("t:tax", "percent", 0, 40, 20, null, 0, 0, null, "x");

    private static VoterGroupDef Group(string id, double size, double taxPref) => new(
        id, size, 1, new Dictionary<string, double>(), new Dictionary<string, double> { ["t:tax"] = taxPref },
        new Dictionary<string, double> { ["t:tax"] = 1 }, "x");

    private static PartyDef Party(string id, double tax) => new(
        id, "name." + id, new Dictionary<string, PlatformPosition> { ["t:tax"] = new(tax, 1) },
        new Dictionary<string, double>(), false, new List<DemandDef>(), new List<RedLineDef>(), 0, new List<FactionDef>(), "x");

    private static SupportModel Model() => new(
        new[] { Party("a", 10), Party("b", 20), Party("c", 30) }, new[] { Group("g1", 60, 10), Group("g2", 40, 30) }, new[] { Tax });

    private static SupportInput Input(IReadOnlyDictionary<string, double>? momentum = null, string? party = null,
        IReadOnlyDictionary<string, double>? boost = null) => new(
        new Dictionary<string, double> { ["g1"] = 0, ["g2"] = 0 }, new HashSet<string>(), new Dictionary<string, double>(),
        momentum, party, boost);

    [Fact]
    public void MomentumRaisesTheShareOfItsPartyOnly()
    {
        var m = Model();
        var plain = m.Compute(Input(), null);
        var pushed = m.Compute(Input(new Dictionary<string, double> { ["g1"] = 0.3 }, "b"), null);
        Assert.True(pushed["b"] > plain["b"]);
        Assert.True(pushed["a"] < plain["a"] && pushed["c"] < plain["c"]);
        Assert.Equal(1.0, pushed.Values.Sum(), 12);
    }

    [Fact]
    public void NegativeMomentumLowersIt()
    {
        var m = Model();
        Assert.True(m.Compute(Input(new Dictionary<string, double> { ["g1"] = -0.3, ["g2"] = -0.3 }, "b"), null)["b"]
            < m.Compute(Input(), null)["b"]);
    }

    [Fact]
    public void TurnoutBoostRaisesTheShare()
    {
        var m = Model();
        var boosted = m.Compute(Input(boost: new Dictionary<string, double> { ["b"] = 1.2 }), null);
        Assert.True(boosted["b"] > m.Compute(Input(), null)["b"]);
        Assert.Equal(1.0, boosted.Values.Sum(), 12);
    }

    [Fact]
    public void NoMomentumEqualsOldBehaviour()
    {
        var plain = Model().Compute(Input(), new Pcg32(3));
        var explicitNone = Model().Compute(Input(new Dictionary<string, double>(), "b", new Dictionary<string, double>()), new Pcg32(3));
        foreach (var k in plain.Keys) Assert.Equal(plain[k], explicitNone[k]);
    }

    [Fact]
    public void UpdatePartyChangesDistances()
    {
        var m = Model();
        double before = m.Compute(Input(), null)["c"];
        m.UpdateParty(Party("c", 10)); // moves next to group g1's preference
        Assert.True(m.Compute(Input(), null)["c"] > before);
    }

    [Fact]
    public void UpdateUnknownPartyThrows() =>
        Assert.Throws<ArgumentException>(() => Model().UpdateParty(Party("ghost", 10)));

    [Fact]
    public void ExtremeMomentumStaysFinite()
    {
        var m = Model();
        foreach (var mom in new[] { -5.0, 5.0 })
            foreach (var boost in new[] { 0.0, 3.0 })
            {
                var r = m.Compute(Input(new Dictionary<string, double> { ["g1"] = mom, ["g2"] = mom }, "b",
                    new Dictionary<string, double> { ["b"] = boost }), new Pcg32(1));
                Assert.All(r.Values, v => Assert.True(double.IsFinite(v) && v >= 0));
                Assert.Equal(1.0, r.Values.Sum(), 9);
            }
    }
}
