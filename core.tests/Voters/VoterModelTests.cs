using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Voters;

namespace OpenPolitics.Core.Tests.Voters;

public class VoterModelTests
{
    private const double Dt = 0.25;

    private static VoterGroupDef Group(double valueWeight = 1, double policyWeight = 0.5, double pref = 20) =>
        new("t:g", 1, 1,
            new Dictionary<string, double> { ["t:gdp"] = valueWeight },
            new Dictionary<string, double> { ["t:tax"] = pref },
            new Dictionary<string, double> { ["t:tax"] = policyWeight }, "f");

    private static ValueDef Value() => new("t:gdp", "u", 1, 1, 1, null, "f");
    private static PolicyDef Policy() => new("t:tax", "percent", 0, 100, 20, null, 0, 0, null, "f");

    private static VoterModel Model(VoterGroupDef? g = null, double lambda = 0.5) =>
        new(new[] { g ?? Group() }, new[] { Value() }, new[] { Policy() }, lambda);

    private static double Next(VoterModel m, double s, double gdpChange, double tax)
    {
        var r = m.Step(new Dictionary<string, double> { ["t:g"] = s },
            new Dictionary<string, double> { ["t:gdp"] = gdpChange },
            new Dictionary<string, double> { ["t:tax"] = tax }, Dt);
        return r["t:g"];
    }

    [Theory]
    [InlineData(1e9, 1e9, 1e9)]
    [InlineData(-1e9, -1e9, -1e9)]
    [InlineData(1e9, 0, 0)]
    [InlineData(-1e9, 0, 100)]
    public void SatisfactionStaysInRange(double change, double start, double tax)
    {
        var s = Next(Model(Group(valueWeight: 1e6)), Math.Clamp(start, -1, 1), change, tax);
        Assert.InRange(s, -1.0, 1.0);
    }

    [Fact]
    public void ImprovingPreferredValueRaisesSatisfaction()
    {
        var m = Model();
        Assert.True(Next(m, 0, 0.05, 20) > 0);
        Assert.True(Next(m, 0, -0.05, 20) < 0);
    }

    [Fact]
    public void DecaysTowardZeroWithoutInput()
    {
        var m = Model(Group(policyWeight: 0));
        Assert.Equal(0.8 - Dt * 0.5 * 0.8, Next(m, 0.8, 0, 20), 12);
        Assert.Equal(-0.8 + Dt * 0.5 * 0.8, Next(m, -0.8, 0, 20), 12);
    }

    [Fact]
    public void PolicyAtPreferenceIsNeutralAndDeviationHurts()
    {
        var m = Model(Group(valueWeight: 0));
        Assert.Equal(0.0, Next(m, 0, 0, 20), 12);
        Assert.True(Next(m, 0, 0, 40) < 0);
        Assert.True(Next(m, 0, 0, 0) < 0);
    }

    [Fact]
    public void UnknownKeysAreTreatedAsZeroAndAllGroupsReturned()
    {
        var m = Model();
        var r = m.Step(new Dictionary<string, double>(), new Dictionary<string, double>(), new Dictionary<string, double>(), Dt);
        Assert.Equal(new[] { "t:g" }, r.Keys.ToArray());
        Assert.True(double.IsFinite(r["t:g"]));
    }
}
