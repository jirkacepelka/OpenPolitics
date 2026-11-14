using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Content;

public class PoliticsContentTests
{
    private const string Scenario = "base:cz_2015";
    private static readonly Core.Packs.PackSet Packs = BasePackTests.LoadBase().Packs!;
    private static CountryPolitics Pol(Game g) => g.State.Countries.Values.Single().Politics!;

    [Fact]
    public void BasePackWithPoliticsHasNoDiagnostics()
    {
        var r = BasePackTests.LoadBase();
        Assert.Empty(r.Diagnostics);
        Assert.Equal(8, r.Packs!.Parties.Count);
        Assert.Contains(r.Packs.ElectoralSystems, e => e.Id == "base:czech_pr" && e.Seats == 200);
    }

    [Fact]
    public void EightPartiesStartingSupportSumsToOne()
    {
        var sc = Packs.GetScenario(Scenario).Politics!;
        Assert.Equal(8, sc.Support.Count);
        Assert.Equal(1.0, sc.Support.Values.Sum(), 12);
        foreach (var p in Packs.Parties)
        {
            Assert.InRange(p.Platform.Count, 3, 14);
            Assert.Equal(Packs.VoterGroups.Count, p.Affinity.Count);
            Assert.InRange(p.Demands.Count, 3, 12);
            Assert.InRange(p.RedLines.Count, 1, 3);
        }
    }

    [Fact]
    public void StartingGovernmentHasASeatMajority()
    {
        var p = Pol(Simulation.NewGame(Packs, Scenario, 1));
        int gov = p.GovernmentParties.Sum(id => p.Seats[id]);
        Assert.Equal(3, p.GovernmentParties.Count);
        Assert.True(gov > 100, $"government has {gov} seats");
    }

    [Fact]
    public void SwingPartyIsNearTheThreshold()
    {
        Assert.True(Packs.Parties.Single(p => p.Id == "base:open_net").Swing);
        var share = Pol(Simulation.NewGame(Packs, Scenario, 1)).Support["base:open_net"];
        Assert.InRange(share, 0.05, 0.07);
    }

    private static List<ElectionHeld> Elections(ulong seed)
    {
        var g = Simulation.NewGame(Packs, Scenario, seed);
        var result = new List<ElectionHeld>();
        for (int i = 0; i < 12; i++) result.AddRange(g.EndTurn().PoliticalEvents.OfType<ElectionHeld>());
        return result;
    }

    [Fact]
    public void ThousandElectionsAreConsistent()
    {
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            var elections = Elections(seed);
            var e = Assert.Single(elections);
            Assert.Equal(200, e.Seats.Values.Sum());
            Assert.Equal(1.0, e.Votes.Values.Sum(), 9);
            foreach (var (id, votes) in e.Votes)
                if (votes < 0.05) Assert.Equal(0, e.Seats[id]);
            if (seed % 100 == 0) Assert.Equal(e, Elections(seed).Single(), new ElectionComparer());
        }
    }

    private sealed class ElectionComparer : IEqualityComparer<ElectionHeld>
    {
        public bool Equals(ElectionHeld? a, ElectionHeld? b) =>
            a!.Turn == b!.Turn && a.Votes.SequenceEqual(b.Votes) && a.Seats.SequenceEqual(b.Seats);
        public int GetHashCode(ElectionHeld e) => e.Turn;
    }

    [Fact]
    public void PollsDriftOnlyModeratelyInPassivePlay()
    {
        int ok = 0, seeds = 200;
        for (ulong seed = 1; seed <= (ulong)seeds; seed++)
        {
            var g = Simulation.NewGame(Packs, Scenario, seed);
            var start = Pol(g).Support;
            for (int i = 0; i < 6; i++) g.EndTurn(); // before an idle player's coalition starts losing partners (that changes who is blamed)
            var end = Pol(g).Support;
            if (start.Keys.All(k => Math.Abs(end[k] - start[k]) <= 0.08)) ok++;
        }
        Assert.True(ok >= seeds * 0.95, $"{ok}/{seeds} seeds stayed within 8 points in 6 quarters");
    }
}
