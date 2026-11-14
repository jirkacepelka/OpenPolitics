using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class AttributionTests
{
    private static readonly PolicyDef Tax = new("t:tax", "percent", 0, 40, 20, null, 0, 0, null, "x");

    private static VoterGroupDef Group(string id, double size) => new(
        id, size, 1, new Dictionary<string, double>(),
        new Dictionary<string, double> { ["t:tax"] = 20 }, new Dictionary<string, double> { ["t:tax"] = 1 }, "x");

    private static PartyDef Party(string id) => new(
        id, "name." + id,
        new Dictionary<string, PlatformPosition> { ["t:tax"] = new(20, 1) },
        new Dictionary<string, double>(), false,
        new List<DemandDef>(), new List<RedLineDef>(), 1, new List<FactionDef>(), "x");

    private static SupportModel Model() => new(new[] { Party("a"), Party("b") }, new[] { Group("g", 100) }, new[] { Tax });

    private static SupportInput Input(double s, IReadOnlyDictionary<string, double>? attribution) => new(
        new Dictionary<string, double> { ["g"] = s }, new HashSet<string> { "a" }, new Dictionary<string, double>(),
        Attribution: attribution);

    [Fact]
    public void BlameFormulaMatchesOldAtExtremes()
    {
        var m = Model();
        var old = m.Compute(Input(-0.5, null), null);
        var explicitExtremes = m.Compute(Input(-0.5, new Dictionary<string, double> { ["a"] = 1, ["b"] = 0 }), null);
        Assert.Equal(old["a"], explicitExtremes["a"], 12);
        Assert.Equal(old["b"], explicitExtremes["b"], 12);
    }

    [Fact]
    public void HalfAttributionIsBetween()
    {
        var m = Model();
        double none = m.Compute(Input(-0.5, new Dictionary<string, double> { ["a"] = 0, ["b"] = 0 }), null)["a"];
        double half = m.Compute(Input(-0.5, new Dictionary<string, double> { ["a"] = 0.5, ["b"] = 0 }), null)["a"];
        double full = m.Compute(Input(-0.5, new Dictionary<string, double> { ["a"] = 1, ["b"] = 0 }), null)["a"];
        Assert.True(none > half && half > full, $"{none} {half} {full}");
    }

    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 0, Tax: 31),
        new Spec("c", 0.22, Price: 0, Tax: 45), new Spec("z", 0.03),
    };

    [Fact]
    public void EnteringGovernmentDoesNotCollapsePoll()
    {
        // b governs alone at the start and a large group is deeply unhappy; the election after turn 1 makes the
        // player's a the formateur. Entering government must not wipe out a's poll within two quarters.
        var g = New(Load(Parties(), "a", government: new[] { "b" }, interval: 60, grumpy: true), 3);
        g.EndTurn();
        Assert.True(Pol(g).Formation is { PlayerIsFormateur: true });
        double pollBefore = Pol(g).Support["base:a"];
        Assert.True(g.Apply(new ProposeCoalition(new[] { "base:b" }, Array.Empty<AgreedDemand>())).Accepted);
        g.EndTurn();
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        g.EndTurn();
        double pollAfter = Pol(g).Support["base:a"];
        Assert.True(pollAfter >= 0.6 * pollBefore, $"{pollBefore} -> {pollAfter}");
    }
}
