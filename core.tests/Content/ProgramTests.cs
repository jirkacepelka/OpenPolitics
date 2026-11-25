using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Content;

/// <summary>Decision 0014: every party has a broad programme (policy positions plus value goals such as a balanced budget).</summary>
public class ProgramTests
{
    private static readonly Core.Packs.PackSet Packs = BasePackTests.LoadBase().Packs!;

    [Fact]
    public void EveryPartyTakesAPositionOnFifteenToThirtyPolicies()
    {
        foreach (var p in Packs.Parties) Assert.InRange(p.Platform.Count, 15, 30); // a programme is a choice among ~90 laws, never all of them
    }

    [Fact]
    public void EveryPartyHasValueGoals()
    {
        foreach (var p in Packs.Parties)
            Assert.True(p.Demands.Count(d => d.Kind == "value") >= 2, $"{p.Id} has fewer than two value goals");
        var subjects = Packs.Parties.SelectMany(p => p.Demands).Where(d => d.Kind == "value").Select(d => d.Subject).Distinct().ToList();
        Assert.True(subjects.Count >= 7, $"only {subjects.Count} different value goals: {string.Join(", ", subjects)}");
    }

    [Theory]
    [InlineData("base:civic_right", "balanced_budget", "low_debt")]
    [InlineData("base:workers_left", "less_inequality", "low_unemployment")]
    [InlineData("base:green_wave", "greener", "environment")]
    [InlineData("base:homeland_first", "lower_crime", "closed_borders")]
    public void PartiesHaveGoalsThatFitThem(string party, string first, string second)
    {
        var demands = Packs.Parties.Single(p => p.Id == party).Demands.Select(d => d.Id).ToList();
        Assert.Contains(first, demands);
        Assert.Contains(second, demands);
    }

    [Fact]
    public void PlatformsAgreeWithTheirOwnDemandsAndRedLines()
    {
        foreach (var p in Packs.Parties)
        {
            foreach (var d in p.Demands.Where(d => d.Kind == "policy" && p.Platform.ContainsKey(d.Subject)))
                Assert.True(CommitmentMath.Satisfied(d, p.Platform[d.Subject].Target), $"{p.Id}: platform {d.Subject} contradicts its demand {d.Id}");
            foreach (var r in p.RedLines.Where(r => p.Platform.ContainsKey(r.Policy)))
                Assert.False(LoyaltyModel.RedLineViolated(r, p.Platform[r.Policy].Target), $"{p.Id}: platform {r.Policy} crosses its own red line");
        }
    }

    [Fact]
    public void ValueGoalsHaveATargetTheCountryCanReach()
    {
        var values = Packs.Values.ToDictionary(v => v.Id);
        foreach (var d in Packs.Parties.SelectMany(p => p.Demands).Where(d => d.Kind == "value"))
        {
            Assert.True(values.ContainsKey(d.Subject), d.Subject);
            Assert.InRange(d.DueTurns, 12, 20);
            Assert.InRange(d.Weight, 1.0, 3.0);
        }
    }

    [Fact]
    public void DebtAsAShareOfGdpIsTracked()
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015_sandbox", 1);
        var v0 = g.State.Countries.Single().Value.Values;
        Assert.Equal(40.0, v0["base:debt_pct"], 6);
        for (int i = 0; i < 8; i++) g.EndTurn();
        var v = g.State.Countries.Single().Value.Values;
        Assert.Equal(v[MacroModelV1.Ids.Debt] / v[MacroModelV1.Ids.Gdp] * 100.0, v["base:debt_pct"], 6);
    }

    [Fact]
    public void EveryValueGoalHasTextInBothLanguages()
    {
        foreach (var lang in new[] { "cs", "en" })
        {
            var t = new OpenPolitics.Core.Presentation.Localizer(Packs, lang);
            foreach (var d in Packs.Parties.SelectMany(p => p.Demands).Where(d => d.Kind == "value"))
                Assert.NotEqual("name." + d.Subject, t.Name(d.Subject));
        }
    }
}
