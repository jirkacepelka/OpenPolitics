using OpenPolitics.Core.Gameplay;
using Xunit;

namespace OpenPolitics.Core.Tests.Gameplay;

public class PolicyPreviewTests
{
    private static Game NewGame() => Simulation.NewGame(GameFixture.Load(), "base:test", 1);

    [Fact]
    public void MoreSpendingPushesHealthUpAndWidensTheDeficit()
    {
        var p = NewGame().PreviewPolicy("base:spend", 50)!;
        Assert.Equal((40.0, 50.0), (p.From, p.To));
        var health = Assert.Single(p.Values, v => v.ValueId == "base:health");
        Assert.Equal(0.25, health.Additive, 9); // strength 2 x (50 - 40) / range 80
        Assert.Equal(0.0, health.Relative);
        Assert.Equal(10.0, p.BudgetPercentOfGdp, 9);
    }

    [Fact]
    public void ARevenuePolicyNarrowsTheDeficitAndVotersMindIt()
    {
        var p = NewGame().PreviewPolicy("base:tax", 50)!;
        Assert.Equal(-10.0, p.BudgetPercentOfGdp, 9);
        var workers = Assert.Single(p.Groups);
        Assert.Equal("base:workers", workers.GroupId);
        Assert.Equal(-0.5 * 10 / 60, workers.Pressure, 9);
    }

    [Fact]
    public void NoChangeMeansNoEffects()
    {
        var p = NewGame().PreviewPolicy("base:spend", 40)!;
        Assert.All(p.Values, v => Assert.Equal(0.0, v.Additive));
        Assert.Equal(0.0, p.BudgetPercentOfGdp);
    }

    [Fact]
    public void PreviewStartsFromTheValueAlreadyQueued()
    {
        var g = NewGame();
        g.Apply(new SetPolicy("base:spend", 60));
        var p = g.PreviewPolicy("base:spend", 50)!;
        Assert.Equal(60.0, p.From);
        Assert.True(p.BudgetPercentOfGdp < 0);
    }

    [Fact]
    public void UnknownPolicyOrValueOutsideItsRangeGivesNothing()
    {
        var g = NewGame();
        Assert.Null(g.PreviewPolicy("base:nope", 1));
        Assert.Null(g.PreviewPolicy("base:spend", 999));
        Assert.Null(g.PreviewPolicy("base:spend", double.NaN));
    }

    [Fact]
    public void PreviewDoesNotChangeTheGame()
    {
        var a = NewGame();
        var b = NewGame();
        a.PreviewPolicy("base:spend", 70);
        a.EndTurn(); b.EndTurn();
        Assert.Equal(GameTests.Fingerprint(a), GameTests.Fingerprint(b));
        Assert.Empty(a.Save().Actions);
    }

    [Fact]
    public void AGroupWithAPreferenceButNoWeightIsNotListed()
    {
        var g = Simulation.NewGame(GameFixture.Load(f =>
        {
            f["voter_groups/g.yaml"] += "- id: base:silent\n  size: 1\n  weight: 1\n  policy_prefs: {base:tax: 10}\n";
            foreach (var lang in new[] { "cs", "en" }) f[$"localization/{lang}.yaml"] += "name.base:silent: s\n";
        }), "base:test", 1);
        Assert.DoesNotContain(g.PreviewPolicy("base:tax", 50)!.Groups, x => x.GroupId == "base:silent");
    }
}
