using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0017: the electoral system is a policy; the majority systems turn the same votes into a different parliament.</summary>
public class ElectoralSystemPolicyTests
{
    private static readonly Core.Packs.PackSet Packs = Content.BasePackTests.LoadBase().Packs!;
    private static CountryPolitics Pol(Game g) => g.State.Countries.Values.Single().Politics!;

    [Theory]
    [InlineData("base:cz_2015", "proportional")]
    [InlineData("base:cz_2015_districts", "fptp")]
    [InlineData("base:cz_2015_runoff", "two_round")]
    public void TheScenarioPicksTheSystem(string scenario, string kind) =>
        Assert.Equal(kind, Simulation.NewGame(Packs, scenario, 1).ElectoralKind);

    [Fact]
    public void DistrictsGiveTheLeaderMoreSeatsThanProportionalRepresentation()
    {
        var pr = Pol(Simulation.NewGame(Packs, "base:cz_2015", 1));
        var fptp = Pol(Simulation.NewGame(Packs, "base:cz_2015_districts", 1));
        Assert.Equal(200, fptp.Seats.Values.Sum());
        string top = pr.Support.OrderByDescending(kv => kv.Value).First().Key;
        Assert.True(fptp.Seats[top] > pr.Seats[top], $"{top}: districts {fptp.Seats[top]}, proportional {pr.Seats[top]}");
        Assert.True(fptp.Seats.Count(kv => kv.Value > 0) < pr.Seats.Count(kv => kv.Value > 0));
    }

    [Theory]
    [InlineData("base:cz_2015_districts")]
    [InlineData("base:cz_2015_runoff")]
    public void AnElectionInDistrictsFillsTheParliament(string scenario)
    {
        var g = Simulation.NewGame(Packs, scenario, 3);
        ElectionHeld? held = null;
        for (int t = 0; t < 40 && held == null; t++)
            held = g.EndTurn().PoliticalEvents.OfType<ElectionHeld>().FirstOrDefault();
        Assert.NotNull(held);
        Assert.Equal(200, held!.Seats.Values.Sum());
        Assert.Equal(1.0, held.Votes.Values.Sum(), 9);
        Assert.Equal(200, Pol(g).Seats.Values.Sum());
    }

    [Fact]
    public void SmallPartiesRefuseTheReform()
    {
        foreach (var id in new[] { "base:peoples_union", "base:workers_left", "base:homeland_first", "base:green_wave", "base:open_net" })
            Assert.Contains(Packs.Parties.First(p => p.Id == id).RedLines, r => r.Policy == PoliticsLimits.SystemPolicy && r.Op == ">=");
    }
}
