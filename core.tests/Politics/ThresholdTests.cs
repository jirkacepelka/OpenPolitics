using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>The threshold and the seat allocation method are policies: the leader sets them, they apply at the next election.</summary>
public class ThresholdTests
{
    private static readonly Spec[] Parties =
    {
        new("a", 0.50, Tax: 30), new("b", 0.32, Tax: 31), new("y", 0.08, Tax: 45), new("z", 0.03, Tax: 20), new("c", 0.07, Tax: 50),
    };

    private static Core.Packs.PackSet Packs() => Load(Parties, "a", mutate: f =>
    {
        foreach (var lang in new[] { "cs", "en" }) f[$"localization/{lang}.yaml"] += "name.base:electoral_threshold: t\nname.base:electoral_method: m\n";
        f["policies/e.yaml"] =
        "- id: base:electoral_threshold\n  unit: percent\n  min: 0\n  max: 10\n  default: 5\n" +
        "- id: base:electoral_method\n  unit: electoral_method\n  min: 0\n  max: 2\n  default: 0\n";
        GameFixture.FreePolicies(f); // these tests are about the election rules, not about what changing them costs
    });

    private static IReadOnlyDictionary<string, int> SeatsAfterElection(double? threshold, double? method = null)
    {
        var g = New(Packs());
        if (threshold != null) Assert.True(g.Apply(new SetPolicy("base:electoral_threshold", threshold.Value)).Accepted);
        if (method != null) Assert.True(g.Apply(new SetPolicy("base:electoral_method", method.Value)).Accepted);
        g.EndTurn();
        return Pol(g).Seats;
    }

    [Fact]
    public void TheDefaultPolicyIsTheCountrysThreshold()
    {
        var seats = SeatsAfterElection(null);
        Assert.True(seats["base:y"] > 0);
        Assert.Equal(0, seats["base:z"]);
        Assert.Equal(0.05, New(Packs()).ElectoralThreshold);
    }

    [Fact]
    public void LoweringThePolicySeatsEvenTheSmallestParty()
    {
        Assert.True(SeatsAfterElection(0.0)["base:z"] > 0);
    }

    [Fact]
    public void RaisingThePolicyLocksOutAnEightPercentParty()
    {
        var seats = SeatsAfterElection(10.0);
        Assert.Equal(0, seats["base:y"]);
        Assert.Equal(0, seats["base:z"]);
        Assert.True(seats["base:a"] > 0);
    }

    [Fact]
    public void TheThresholdPolicyIsReadAtTheElectionNotWhenItIsSet()
    {
        var g = New(Packs(), 1);
        g.Apply(new SetPolicy("base:electoral_threshold", 10.0));
        Assert.Equal(0.05, g.ElectoralThreshold); // not yet in effect: policies take a turn
        g.EndTurn();
        Assert.Equal(0.10, g.ElectoralThreshold);
    }

    private static readonly Dictionary<string, double> Votes = new()
    { ["a"] = 0.50, ["b"] = 0.32, ["c"] = 0.07, ["y"] = 0.08, ["z"] = 0.03 };

    [Theory]
    [InlineData("dhondt", 104, 16)]
    [InlineData("sainte_lague", 103, 17)]
    [InlineData("hare", 103, 17)]
    public void EachMethodSplitsTheSeatsItsOwnWay(string method, int biggest, int smallEligible)
    {
        var seats = new Core.Politics.ProportionalSystem(200, 0.05, method).Allocate(Votes);
        Assert.Equal(200, seats.Values.Sum());
        Assert.Equal(biggest, seats["a"]);
        Assert.Equal(smallEligible, seats["y"]);
        Assert.Equal(0, seats["z"]);
    }

    [Fact]
    public void TheMethodPolicyIsUsedAtTheElection()
    {
        // Same votes, so any difference in seats comes from the allocation method alone; the methods are all valid and fill the house.
        foreach (double method in new[] { 0.0, 1.0, 2.0 })
            Assert.Equal(200, SeatsAfterElection(null, method).Values.Sum());
        Assert.Equal(0.05, New(Packs()).ElectoralThreshold);
    }

    [Fact]
    public void WithoutThePoliciesThePackRuleApplies()
    {
        var g = New(Load(Parties, "a"));
        Assert.Equal(0.05, g.ElectoralThreshold);
        g.EndTurn();
        Assert.Equal(0, Pol(g).Seats["base:z"]);
    }

    [Fact]
    public void WithoutPoliticsThereIsNoThreshold()
    {
        Assert.Null(Simulation.NewGame(Load(Parties, "a"), "base:test", 1).ElectoralThreshold);
    }
}
