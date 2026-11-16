using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Voters;
using Xunit;

namespace OpenPolitics.Core.Tests.Voters;

/// <summary>Decision 0014 (e): voters dislike austerity less when the deficit or the debt is high, but they never like it.</summary>
public class NecessityTests
{
    private const double Dt = 0.25;

    private static readonly VoterGroupDef Group = new("t:g", 1, 1, new Dictionary<string, double>(),
        new Dictionary<string, double> { ["t:spend"] = 10, ["t:tax"] = 20 },
        new Dictionary<string, double> { ["t:spend"] = 1, ["t:tax"] = 1 }, "f");

    private static readonly PolicyDef Spend = new("t:spend", "percent", 0, 20, 10, null, 1, 0, null, "f");
    private static readonly PolicyDef Tax = new("t:tax", "percent", 0, 40, 20, null, 0, 0, "t:gdp", "f");

    private static double After(double spend, double tax, double deficit, double debt)
    {
        var m = new VoterModel(new[] { Group }, Array.Empty<ValueDef>(), new[] { Spend, Tax });
        var r = m.Step(new Dictionary<string, double> { ["t:g"] = 0 }, new Dictionary<string, double>(),
            new Dictionary<string, double> { ["t:spend"] = spend, ["t:tax"] = tax }, Dt,
            new Dictionary<string, double> { [Necessity.DeficitId] = deficit, [Necessity.DebtId] = debt });
        return r["t:g"];
    }

    [Fact]
    public void ACutIsLessUnpopularWhenTheDeficitIsHighButStillUnpopular()
    {
        double calm = After(8, 20, 1, 40), crisis = After(8, 20, 9, 40);
        Assert.True(calm < 0 && crisis < 0);
        Assert.True(crisis > calm, $"{crisis} should be milder than {calm}");
    }

    [Fact]
    public void ATaxRiseIsLessUnpopularWhenTheDebtIsHigh()
    {
        double calm = After(10, 24, 1, 40), crisis = After(10, 24, 1, 110);
        Assert.True(crisis < 0 && crisis > calm);
    }

    [Fact]
    public void SpendingMoreOrTaxingLessIsNotSoftened()
    {
        Assert.Equal(After(12, 20, 1, 40), After(12, 20, 9, 120), 12);
        Assert.Equal(After(10, 16, 1, 40), After(10, 16, 9, 120), 12);
    }

    [Theory]
    [InlineData(0, 0, 1.0)]
    [InlineData(3, 60, 1.0)]
    [InlineData(8, 60, 0.6)]
    [InlineData(30, 300, 0.4)]
    public void TheFactorFallsWithDeficitAndDebtDownToAFloor(double deficit, double debt, double expected) =>
        Assert.Equal(expected, Necessity.Factor(deficit, debt), 9);
}
