using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class LoyaltyModelTests
{
    [Fact]
    public void TargetExamples()
    {
        Assert.Equal(100.0, LoyaltyModel.Target(1, 0, 1, false)); // clamped
        Assert.Equal(0.0, LoyaltyModel.Target(0, 1, -1, true));
        Assert.Equal(50.0, LoyaltyModel.Target(0, 0, 0, false));
        Assert.Equal(50 + 40 * 0.5 - 60 * 0.25 + 20 * 0.5 - 20, LoyaltyModel.Target(0.5, 0.25, 0.5, true), 12);
    }

    [Fact]
    public void StepApproachesTheTargetMonotonicallyAndStaysInRange()
    {
        double l = 70;
        double previous = l;
        for (int i = 0; i < 200; i++)
        {
            l = LoyaltyModel.Step(l, 10, 1.0, 0.25);
            Assert.True(l <= previous && l >= 10);
            previous = l;
        }
        Assert.InRange(LoyaltyModel.Step(99, 500, 1, 10), 0, 100);
        Assert.InRange(LoyaltyModel.Step(1, -500, 1, 10), 0, 100);
    }

    [Fact]
    public void SwingLambdaDoublesTheRate()
    {
        double slow = LoyaltyModel.Step(70, 20, 1.0, 0.25), fast = LoyaltyModel.Step(70, 20, 2.0, 0.25);
        Assert.Equal(2 * (70 - slow), 70 - fast, 12);
    }

    [Fact]
    public void RedLineDirections()
    {
        Assert.True(LoyaltyModel.RedLineViolated(new RedLineDef("t:tax", ">=", 40), 40));
        Assert.False(LoyaltyModel.RedLineViolated(new RedLineDef("t:tax", ">=", 40), 39.9));
        Assert.True(LoyaltyModel.RedLineViolated(new RedLineDef("t:tax", "<=", 8), 8));
        Assert.False(LoyaltyModel.RedLineViolated(new RedLineDef("t:tax", "<=", 8), 8.1));
    }

    private static VoterGroupDef G(string id, double size) => new(id, size, 1, new Dictionary<string, double>(),
        new Dictionary<string, double>(), new Dictionary<string, double>(), "x");

    [Fact]
    public void BaseMoodWeightsGroupsBySizeAndAffinity()
    {
        var party = new PartyDef("p", "n", new Dictionary<string, PlatformPosition>(),
            new Dictionary<string, double> { ["big"] = 0.0, ["fan"] = 1.0 }, false, new List<DemandDef>(), new List<RedLineDef>(), 0, new List<FactionDef>(), "x");
        var groups = new[] { G("big", 0.5), G("fan", 0.5) };
        double mood = LoyaltyModel.BaseMood(party, groups, new Dictionary<string, double> { ["big"] = -1, ["fan"] = 1 });
        Assert.InRange(mood, -1, 1);
        Assert.True(mood > 0, "the fan group counts more because the party's base leans on it");
        Assert.Equal(0.0, LoyaltyModel.BaseMood(party, groups, new Dictionary<string, double>()), 12);
    }
}
