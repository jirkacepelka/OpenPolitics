using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class CommitmentsTests
{
    private static Commitment C(string id, string op, double target, double baseline, double weight = 1, int due = 8, string party = "p") =>
        new(party, new DemandDef(id, "policy", "t:tax", op, target, weight, due), baseline, due);

    [Fact]
    public void ProgressRunsFromBaselineToTarget()
    {
        var c = C("d", "<=", 20, 30);
        Assert.Equal(0.0, CommitmentMath.Progress(c, 30));
        Assert.Equal(0.5, CommitmentMath.Progress(c, 25), 12);
        Assert.Equal(1.0, CommitmentMath.Progress(c, 20));
        Assert.Equal(1.0, CommitmentMath.Progress(c, 10)); // beyond the target is still done
        Assert.Equal(0.0, CommitmentMath.Progress(c, 35)); // wrong direction is clamped
        var up = C("u", ">=", 40, 30);
        Assert.Equal(0.3, CommitmentMath.Progress(up, 33), 12);
    }

    [Fact]
    public void AlreadySatisfiedAtFormationIsDone()
    {
        Assert.Equal(1.0, CommitmentMath.Progress(C("d", "<=", 20, 15), 15));
        Assert.Equal(1.0, CommitmentMath.Progress(C("d", ">=", 40, 45), 45));
    }

    [Fact]
    public void TargetEqualToBaselineGivesNoNaN()
    {
        var c = C("d", ">=", 40, 40);
        Assert.Equal(1.0, CommitmentMath.Progress(c, 40));
        var worse = C("d", "<=", 40, 40);
        Assert.Equal(1.0, CommitmentMath.Progress(worse, 40));
        Assert.Equal(0.0, CommitmentMath.Progress(worse, 41));
    }

    [Fact]
    public void ExpectedGrowsLinearlyAndCapsAtOne()
    {
        var c = C("d", "<=", 20, 30, due: 8);
        Assert.Equal(0.0, CommitmentMath.Expected(c, 5, 5));
        Assert.Equal(0.5, CommitmentMath.Expected(c, 9, 5), 12);
        Assert.Equal(1.0, CommitmentMath.Expected(c, 40, 5));
    }

    [Fact]
    public void FulfilAndShortfallAreWeightAveraged()
    {
        var mine = new[] { C("a", "<=", 20, 30, weight: 3), C("b", "<=", 20, 30, weight: 1) };
        double Cur(Commitment c) => c.Demand.Id == "a" ? 20 : 30; // a done, b untouched
        Assert.Equal(0.75, CommitmentMath.Fulfil(mine, Cur, 8, 0), 12);
        Assert.Equal(0.25, CommitmentMath.Shortfall(mine, Cur, 8, 0), 12); // b expected 1, progress 0, weight 1/4
    }

    [Fact]
    public void NoCommitmentsMeansNeutralFulfilAndNoShortfall()
    {
        Assert.Equal(0.5, CommitmentMath.Fulfil(Array.Empty<Commitment>(), _ => 0, 5, 0));
        Assert.Equal(0.0, CommitmentMath.Shortfall(Array.Empty<Commitment>(), _ => 0, 5, 0));
        Assert.Null(CommitmentMath.MostLate(Array.Empty<Commitment>(), _ => 0, 5, 0));
    }

    [Fact]
    public void MostLatePicksTheLargestWeightedGapAndBreaksTiesById()
    {
        var mine = new[] { C("b", "<=", 20, 30, weight: 2), C("a", "<=", 20, 30, weight: 2), C("c", "<=", 20, 30, weight: 1) };
        Assert.Equal("a", CommitmentMath.MostLate(mine, c => 30, 8, 0)!.Demand.Id);
        Assert.Equal("c", CommitmentMath.MostLate(mine, c => c.Demand.Id == "c" ? 30 : 20, 8, 0)!.Demand.Id);
        Assert.Null(CommitmentMath.MostLate(mine, c => 20, 8, 0));
    }
}
