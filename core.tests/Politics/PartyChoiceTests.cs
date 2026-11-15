using OpenPolitics.Core.Batch;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Content;
using Xunit;
using Xunit.Abstractions;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>The player picks any party of a political scenario at the start (decision 0013).</summary>
public class PartyChoiceTests(ITestOutputHelper output)
{
    private static readonly PackSet Packs = BasePackTests.LoadBase().Packs!;
    private const string Scenario = "base:cz_2015";

    private static CountryPolitics Pol(Game g) => g.State.Countries.Values.Single().Politics!;

    [Theory]
    [InlineData("base:social_alliance", Role.Leader)]
    [InlineData("base:citizens_movement", Role.Partner)]
    [InlineData("base:peoples_union", Role.Partner)]
    [InlineData("base:civic_right", Role.Opposition)]
    [InlineData("base:open_net", Role.Opposition)]
    public void TheChosenPartyIsThePlayerWithItsStartingRole(string party, Role role)
    {
        var g = Simulation.NewGame(Packs, Scenario, 1, party);
        Assert.Equal(party, Pol(g).PlayerParty);
        Assert.Equal(role, Pol(g).PlayerRole);
        Assert.Equal("base:social_alliance", Pol(g).Leader);
        Assert.DoesNotContain(party, Pol(g).Party!.Relations.Keys);
        Assert.Equal(Packs.Parties.Count - 1, Pol(g).Party!.Relations.Count);
    }

    [Fact]
    public void WithoutAChoiceTheScenarioPartyPlays()
    {
        Assert.Equal("base:social_alliance", Pol(Simulation.NewGame(Packs, Scenario, 1)).PlayerParty);
        Assert.Equal("base:social_alliance", Pol(Simulation.NewGame(Packs, Scenario, 1, null)).PlayerParty);
    }

    [Fact]
    public void UnknownPartyOrAPartyWithoutPoliticsIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Simulation.NewGame(Packs, Scenario, 1, "base:nobody"));
        Assert.Throws<ArgumentException>(() => Simulation.NewGame(Packs, "base:cz_2015_sandbox", 1, "base:civic_right"));
    }

    [Fact]
    public void TheChoiceDoesNotChangeTheStartingPollOrSeats()
    {
        var a = Pol(Simulation.NewGame(Packs, Scenario, 3));
        var b = Pol(Simulation.NewGame(Packs, Scenario, 3, "base:green_wave"));
        foreach (var (id, share) in a.Support) Assert.Equal(share, b.Support[id], 6);
        Assert.Equal(a.Seats, b.Seats);
    }

    [Fact]
    public void SaveRecordsTheChosenPartyAndReplays()
    {
        var g = Simulation.NewGame(Packs, Scenario, 5, "base:civic_right");
        for (int t = 0; t < 6; t++)
        {
            if (t == 2) Assert.True(g.Apply(new Campaign("base:middle_class", 1)).Accepted);
            if (t == 3) Assert.True(g.Apply(new Court("base:homeland_first")).Accepted);
            g.EndTurn();
        }
        var save = g.Save();
        Assert.Equal("base:civic_right", save.PlayerParty);
        var yaml = save.ToYaml();
        Assert.Contains("player_party: base:civic_right", yaml);
        var back = SaveData.FromYaml(yaml);
        Assert.Equal("base:civic_right", back.PlayerParty);
        var loaded = Simulation.Load(Packs, back);
        Assert.Equal("base:civic_right", Pol(loaded).PlayerParty);
        Assert.Equal(Pol(g).Party!.PoliticalCapital, Pol(loaded).Party!.PoliticalCapital, 9);
        for (int i = 0; i < 8; i++) { g.EndTurn(); loaded.EndTurn(); }
        Assert.Equal(Pol(g).Support, Pol(loaded).Support);
    }

    [Fact]
    public void ASaveWithoutAPartyFallsBackToTheScenarioParty()
    {
        var yaml = Simulation.NewGame(Packs, Scenario, 5).Save().ToYaml();
        var old = string.Join("\n", yaml.Split('\n').Where(l => !l.StartsWith("player_party", StringComparison.Ordinal)));
        var back = SaveData.FromYaml(old);
        Assert.Null(back.PlayerParty);
        Assert.Equal("base:social_alliance", Pol(Simulation.Load(Packs, back)).PlayerParty);
    }

    [Fact]
    public void PartyOptionsListEveryPartyWithItsStartingRole()
    {
        var options = PartyChoice.Options(Packs, Scenario);
        Assert.Equal(Packs.Parties.Count, options.Count);
        Assert.Equal(Role.Leader, options.Single(o => o.PartyId == "base:social_alliance").Role);
        Assert.Equal(Role.Partner, options.Single(o => o.PartyId == "base:peoples_union").Role);
        Assert.Equal(Role.Opposition, options.Single(o => o.PartyId == "base:workers_left").Role);
        Assert.Equal(200, options.Sum(o => o.Seats));
        Assert.Equal(1.0, options.Sum(o => o.Support), 6);
        Assert.True(options.Single(o => o.PartyId == "base:civic_right").X > options.Single(o => o.PartyId == "base:workers_left").X);
        Assert.Equal(options.OrderByDescending(o => o.Support).Select(o => o.PartyId), options.Select(o => o.PartyId));
        Assert.Empty(PartyChoice.Options(Packs, "base:cz_2015_sandbox"));
    }

    [Fact]
    public void APlayerPartnerIsNeverLedOutOfTheGovernmentByItsOwnLoyalty()
    {
        // The player decides when to leave: its party has no loyalty meter and never "leaves" on its own.
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var g = Simulation.NewGame(Packs, Scenario, seed, "base:peoples_union");
            for (int t = 0; t < 12; t++)
            {
                if (Pol(g).PlayerRole == Role.Partner) Assert.False(Pol(g).Loyalty!.ContainsKey("base:peoples_union"));
                var r = g.EndTurn();
                Assert.DoesNotContain(r.PoliticalEvents.OfType<PartnerLeft>(), e => e.PartyId == "base:peoples_union");
                Assert.DoesNotContain(r.PoliticalEvents.OfType<Ultimatum>(), e => e.PartyId == "base:peoples_union");
            }
        }
    }

    [Fact]
    public void EveryPartyCanBePlayedForTwentyYears()
    {
        foreach (var party in Packs.Parties.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal))
            foreach (var strategy in new[] { PoliticsStrategy.Keeper, PoliticsStrategy.Activist })
            {
                var r = PoliticsBatch.Run(Packs, Scenario, 6, 80, strategy, 1, party);
                output.WriteLine($"{party} {strategy}: falls {r.Falls}, governing {r.GoverningShare:P0}, leads {r.PlayerLeadShare:P0}, poll {r.MeanPlayerSupportOverRun:P1}");
                Assert.Equal(0, r.StuckGames);
                Assert.Equal(0, r.NegativeCapitalGames);
                Assert.Equal(r.FallsWithNewMajority, r.Falls);
            }
    }
}
