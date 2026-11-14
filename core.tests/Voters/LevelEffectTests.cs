using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Gameplay;
using OpenPolitics.Core.Voters;
using Xunit;

namespace OpenPolitics.Core.Tests.Voters;

public class LevelEffectTests
{
    private const double Dt = 0.25;

    private static VoterGroupDef Group(double level) => new(
        "t:g", 1, 1, new Dictionary<string, double>(), new Dictionary<string, double>(), new Dictionary<string, double>(), "f",
        new Dictionary<string, double> { ["t:gdp"] = level });

    private static VoterModel Model(double level = 1.0) => new(new[] { Group(level) },
        new[] { new ValueDef("t:gdp", "u", 100, 100, 10, null, "f") }, Array.Empty<PolicyDef>());

    private static double Run(VoterModel m, double growthPerYear, int turns)
    {
        double s = 0, v = 100;
        var none = new Dictionary<string, double>();
        for (int i = 0; i < turns; i++)
        {
            v *= Math.Pow(1 + growthPerYear, Dt);
            s = m.Step(new Dictionary<string, double> { ["t:g"] = s }, none, none, Dt,
                new Dictionary<string, double> { ["t:gdp"] = v })["t:g"];
        }
        return s;
    }

    [Fact]
    public void ConstantValueHasNoLevelEffect() => Assert.Equal(0.0, Run(Model(), 0.0, 80), 9);

    [Fact]
    public void SustainedGrowthRaisesSatisfaction()
    {
        double growth = Run(Model(), 0.03, 80), flat = Run(Model(), 0.0, 80);
        Assert.True(growth - flat >= 0.15, $"{growth} vs {flat}");
    }

    [Fact]
    public void ShrinkingEconomyLowersSatisfaction() => Assert.True(Run(Model(), -0.03, 80) < -0.1);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void HabitIsGuardedAgainstNonPositiveValues(double v)
    {
        var m = Model();
        var none = new Dictionary<string, double>();
        double s = 0;
        for (int i = 0; i < 20; i++)
            s = m.Step(new Dictionary<string, double> { ["t:g"] = s }, none, none, Dt, new Dictionary<string, double> { ["t:gdp"] = v })["t:g"];
        Assert.True(double.IsFinite(s));
    }

    [Fact]
    public void ValidatorRejectsUnknownLevelValue()
    {
        var r = PackLoader.Load(new IPackSource[] { new InMemoryPackSource("base", GameFixture.Files(f =>
            f["voter_groups/g.yaml"] = "- id: base:workers\n  size: 1\n  weight: 1\n  level_weights: {base:nope: 1}\n")) });
        Assert.False(r.Ok);
        Assert.Contains(r.Diagnostics, d => d.ToString()!.Contains("level_weights"));
    }
}
