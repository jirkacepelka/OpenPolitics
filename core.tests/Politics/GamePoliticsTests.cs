using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class GamePoliticsTests
{
    private const string Scenario = "base:test_politics";

    internal static void AddPolitics(Dictionary<string, string> f, string support = "{base:pa: 0.45, base:pb: 0.35, base:pc: 0.20}", int turnsToElection = 3)
    {
        string Party(string id, double tax, string aff) =>
            $"- id: base:{id}\n  platform:\n    base:tax: {{target: {tax}, weight: 1}}\n  affinity: {{base:workers: {aff}}}\n";
        f["parties/p.yaml"] = Party("pa", 30, "0.2") + Party("pb", 40, "0") + Party("pc", 50, "-0.1");
        f["electoral_systems/pr.yaml"] = "- id: base:pr\n  type: proportional\n  seats: 200\n  method: dhondt\n  threshold: 0.05\n";
        f["countries/c.yaml"] = "- id: base:czechia\n  name_key: country.czechia\n  politics:\n    electoral_system: base:pr\n" +
            "    election_interval_turns: 4\n    majority_fraction: 0.5\n    max_formation_turns: 4\n    caretaker_turns: 2\n    program_slots: 6\n";
        f["scenarios/s.yaml"] =
            "- id: base:test\n  country: base:czechia\n  start_year: 2015\n  turns_per_year: 4\n  start_values: {base:health: 65}\n" +
            "- id: base:test_politics\n  country: base:czechia\n  start_year: 2015\n  turns_per_year: 4\n  start_values: {base:health: 65}\n" +
            "  politics:\n    player_party: base:pa\n    support: " + support + "\n    government: [base:pa]\n    leader: base:pa\n    turns_to_election: " + turnsToElection + "\n";
        foreach (var lang in new[] { "cs", "en" })
            f[$"localization/{lang}.yaml"] += string.Join("", new[] { "pa", "pb", "pc" }.Select(i => $"name.base:{i}: {lang}-{i}\n"));
    }

    private static PackSet Packs() => GameFixture.Load(f => AddPolitics(f));
    private static CountryPolitics Pol(Game g) => g.State.Countries.Values.Single().Politics!;

    [Fact]
    public void NoPoliticsWithoutAPoliticsBlock()
    {
        var g = Simulation.NewGame(Packs(), "base:test", 1);
        Assert.Null(Pol(g));
        Assert.Empty(g.EndTurn().PoliticalEvents);
        Assert.Null(Pol(g));
    }

    [Fact]
    public void StartPollMatchesScenarioSupport()
    {
        var p = Pol(Simulation.NewGame(Packs(), Scenario, 1));
        Assert.Equal(0.45, p.Support["base:pa"], 6);
        Assert.Equal(0.35, p.Support["base:pb"], 6);
        Assert.Equal(0.20, p.Support["base:pc"], 6);
        Assert.Equal(Phase.Governing, p.Phase);
        Assert.Equal(3, p.TurnsToElection);
        Assert.Equal(new[] { "base:pa" }, p.GovernmentParties);
        Assert.Equal("base:pa", p.PlayerParty);
    }

    [Fact]
    public void SeatsAtStartSumToTotal()
    {
        var p = Pol(Simulation.NewGame(Packs(), Scenario, 1));
        Assert.Equal(200, p.Seats.Values.Sum());
        Assert.Equal(3, p.Seats.Count);
    }

    [Fact]
    public void PollsSumToOneEveryTurn()
    {
        var g = Simulation.NewGame(Packs(), Scenario, 3);
        for (int i = 0; i < 20; i++)
        {
            g.EndTurn();
            Assert.Equal(1.0, Pol(g).Support.Values.Sum(), 9);
        }
    }

    [Fact]
    public void ElectionHappensOnTheScheduledTurn()
    {
        var g = Simulation.NewGame(Packs(), Scenario, 5);
        var electionTurns = new List<int>();
        for (int i = 0; i < 12; i++)
        {
            var r = g.EndTurn();
            foreach (var e in r.PoliticalEvents.OfType<ElectionHeld>())
            {
                electionTurns.Add(r.Turn);
                Assert.Equal(e.Turn, r.Turn);
                Assert.Equal(200, e.Seats.Values.Sum());
                Assert.Equal(1.0, e.Votes.Values.Sum(), 9);
                Assert.Equal(e.Seats, Pol(g).Seats);
            }
        }
        Assert.Equal(new[] { 3, 7, 11 }, electionTurns);
    }

    [Fact]
    public void PollsReactToUnhappyVoters()
    {
        double GovShare(bool bad)
        {
            // No election inside the window, so the government (and who gets blamed) stays the same.
            var g = Simulation.NewGame(GameFixture.Load(f => AddPolitics(f, turnsToElection: 40)), Scenario, 9);
            if (bad)
            {
                Assert.True(g.Apply(new SetPolicy("base:tax", 60)).Accepted);
                Assert.True(g.Apply(new SetPolicy("base:spend", 5)).Accepted);
            }
            for (int i = 0; i < 20; i++) g.EndTurn();
            return Pol(g).Support["base:pa"];
        }
        Assert.True(GovShare(true) < GovShare(false));
    }

    [Fact]
    public void PoliticsIsDeterministicPerSeed()
    {
        string Run(ulong seed)
        {
            var g = Simulation.NewGame(Packs(), Scenario, seed);
            for (int i = 0; i < 12; i++) g.EndTurn();
            var p = Pol(g);
            return string.Join(",", p.Support.Select(kv => kv.Value.ToString("R"))) + "|" + string.Join(",", p.Seats.Values);
        }
        Assert.Equal(Run(4), Run(4));
        Assert.NotEqual(Run(4), Run(5));
    }

    [Fact]
    public void ScenarioWithUnknownPartyIsRejectedByValidatorNotByNewGame()
    {
        var files = GameFixture.Files(f => AddPolitics(f, "{base:pa: 0.5, base:ghost: 0.5}"));
        var r = PackLoader.Load(new IPackSource[] { new InMemoryPackSource("base", files) });
        Assert.False(r.Ok);
        Assert.Contains(r.Diagnostics, d => d.Code == "E004" && d.Message.Contains("base:ghost"));
    }

    [Fact]
    public void SaveAndLoadReproducePolitics()
    {
        var packs = Packs();
        var g = Simulation.NewGame(packs, Scenario, 12);
        g.Apply(new SetPolicy("base:tax", 35));
        for (int i = 0; i < 9; i++) g.EndTurn();
        var h = Simulation.Load(packs, g.Save());
        Assert.Equal(Pol(g).Support, Pol(h).Support);
        Assert.Equal(Pol(g).Seats, Pol(h).Seats);
        Assert.Equal(Pol(g).TurnsToElection, Pol(h).TurnsToElection);
    }
}
