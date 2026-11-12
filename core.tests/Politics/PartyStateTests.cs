using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class PartyStateTests
{
    private static readonly Dictionary<string, double> Range = new() { ["t:tax"] = 40, ["t:spend"] = 20 };

    private static PartyDef Party(double tax, params FactionDef[] factions) => new(
        "p", "n", new Dictionary<string, PlatformPosition> { ["t:tax"] = new(tax, 1) },
        new Dictionary<string, double>(), false, new List<DemandDef>(), new List<RedLineDef>(), 0, factions, "x");

    private static FactionDef F(double weight, double tax) =>
        new("f", weight, new Dictionary<string, double> { ["t:tax"] = tax });

    [Fact]
    public void NoFactionsMeansFullCohesion() => Assert.Equal(1.0, PartyMath.Cohesion(Party(20), Range));

    [Fact]
    public void IdenticalPlatformMeansFullCohesion() =>
        Assert.Equal(1.0, PartyMath.Cohesion(Party(20, F(0.6, 20), F(0.4, 20)), Range), 12);

    [Fact]
    public void FarFactionsLowerCohesionInProportionToWeight()
    {
        // factions at 0 and 40 around a platform at 20: both at distance 0.5
        Assert.Equal(0.5, PartyMath.Cohesion(Party(20, F(0.5, 0), F(0.5, 40)), Range), 12);
        // only the heavy faction is far
        Assert.Equal(1 - 0.75 * 0.5, PartyMath.Cohesion(Party(20, F(3, 0), F(1, 20)), Range), 12);
    }

    [Fact]
    public void CohesionIsClampedAndIgnoresUnknownPolicies()
    {
        var wild = new FactionDef("f", 1, new Dictionary<string, double> { ["t:tax"] = 9999, ["t:nope"] = 1 });
        Assert.InRange(PartyMath.Cohesion(Party(20, wild), Range), 0.0, 1.0);
        var empty = new FactionDef("e", 1, new Dictionary<string, double>());
        Assert.Equal(1.0, PartyMath.Cohesion(Party(20, empty), Range));
    }
}
