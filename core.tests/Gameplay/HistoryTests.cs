using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Presentation;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Gameplay;

public class HistoryTests
{
    private static readonly Spec[] Parties = { new("a", 0.5), new("b", 0.3), new("c", 0.2) };

    private static (Game Game, History History) Played(int turns)
    {
        var packs = Load(Parties, "a", government: new[] { "a", "b" }, leader: "a", turnsToElection: 40);
        var g = New(packs);
        var h = new History(g, packs);
        h.Record();
        for (int i = 0; i < turns; i++) { g.EndTurn(); h.Record(); }
        return (g, h);
    }

    [Fact]
    public void InterestPaidIsTheRealInterestOnTheDebtEveryTurn()
    {
        var (g, h) = Played(3);
        var v = g.State.Countries.Single().Value.Values;
        double expected = v["base:debt"] * (v["base:interest_rate"] - v["base:inflation"]) / 100.0;
        Assert.Equal(4, h.Series(History.InterestPaid).Count);
        Assert.Equal(expected, h.Series(History.InterestPaid)[^1], 3);
    }

    [Fact]
    public void EverySeriesHasOnePointPerRecordedTurn()
    {
        var (_, h) = Played(6);
        Assert.Equal(7, h.Turns.Count);
        Assert.Equal(Enumerable.Range(0, 7), h.Turns);
        foreach (var key in h.Keys) Assert.Equal(7, h.Series(key).Count);
    }

    [Fact]
    public void ValueSeriesFollowTheGame()
    {
        var (g, h) = Played(5);
        Assert.Equal(g.State.Countries.Values.Single().Values["base:gdp"], h.Series(History.ValueKey("base:gdp"))[^1]);
    }

    [Fact]
    public void ExtraSeriesExist()
    {
        var (g, h) = Played(5);
        Assert.Contains(History.Mood, h.Keys);
        Assert.Contains(History.DebtRatio, h.Keys);
        Assert.Contains(History.PollKey("base:b"), h.Keys);
        double debt = g.State.Countries.Values.Single().Values["base:debt"], gdp = g.State.Countries.Values.Single().Values["base:gdp"];
        Assert.Equal(debt / gdp * 100, h.Series(History.DebtRatio)[^1], 9);
    }

    [Fact]
    public void LoyaltyOnlyExistsWhileAPartnerIsInGovernment()
    {
        var (_, h) = Played(4);
        var loyalty = h.Series(History.LoyaltyKey("base:b"));
        Assert.True(loyalty.All(double.IsFinite));
        Assert.DoesNotContain(History.LoyaltyKey("base:a"), h.Keys); // the leader has no loyalty
    }

    [Fact]
    public void ARangeCutsTheLastTurnsOnly()
    {
        var (_, h) = Played(10);
        var (turns, values) = h.Window(History.Mood, 4);
        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, turns);
        Assert.Equal(5, values.Count);
        var (allTurns, _) = h.Window(History.Mood, 0);
        Assert.Equal(11, allTurns.Count);
    }
}
