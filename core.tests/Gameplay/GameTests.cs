using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;

namespace OpenPolitics.Core.Tests.Gameplay;

public class GameTests
{
    private const string Scenario = "base:test";
    private const string Country = "base:czechia";

    private static Game New(ulong seed = 1, Action<Dictionary<string, string>>? mutate = null) =>
        Simulation.NewGame(GameFixture.Load(mutate), Scenario, seed);

    private static CountryState C(Game g) => g.State.Countries[Country];

    [Fact]
    public void NewGameStartsAtScenarioYearAndValues()
    {
        var g = New();
        Assert.Equal(0, g.State.Turn);
        Assert.Equal(2015.0, g.State.Year);
        Assert.Equal(65.0, C(g).Values["base:health"]);          // scenario override
        Assert.Equal(4.6e12, C(g).Values[MacroModelV1.Ids.Gdp], 1.0);
        Assert.Equal(40.0, C(g).Policies["base:tax"]);            // policy default
        Assert.Equal(0.0, C(g).Satisfaction["base:workers"]);
    }

    [Fact]
    public void EndTurnAdvancesTurnAndYearByQuarter()
    {
        var g = New();
        var report = g.EndTurn();
        Assert.Equal(1, g.State.Turn);
        Assert.Equal(2015.25, g.State.Year);
        Assert.Equal(1, report.Turn);
        Assert.Equal(2015.25, report.Year);
        g.EndTurn(); g.EndTurn(); g.EndTurn();
        Assert.Equal(2016.0, g.State.Year);
    }

    [Fact]
    public void ApplyRejectsUnknownPolicy()
    {
        var g = New();
        var before = Snapshot(g);
        var r = g.Apply(new SetPolicy("base:nope", 1));
        Assert.False(r.Accepted);
        Assert.Equal("error.unknown_policy", r.ErrorKey);
        Assert.Equal(before, Snapshot(g));
        g.EndTurn();
        Assert.Equal(40.0, C(g).Policies["base:tax"]);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(60.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ApplyRejectsOutOfRange(double value)
    {
        var g = New();
        var before = Snapshot(g);
        var r = g.Apply(new SetPolicy("base:tax", value));
        Assert.False(r.Accepted);
        Assert.Equal("error.value_out_of_range", r.ErrorKey);
        Assert.Equal(before, Snapshot(g));
        Assert.Empty(g.Save().Actions);
    }

    [Fact]
    public void PolicyWithoutDelayTakesEffectInTheNextTurn()
    {
        var g = New();
        Assert.True(g.Apply(new SetPolicy("base:tax", 30)).Accepted);
        Assert.Equal(40.0, C(g).Policies["base:tax"]);   // queued, not yet effective
        g.EndTurn();
        Assert.Equal(30.0, C(g).Policies["base:tax"]);
    }

    [Fact]
    public void PolicyChangeTakesEffectAfterDelay()
    {
        var g = New();
        g.Apply(new SetPolicy("base:slow", 5));            // delay_turns: 2
        g.EndTurn(); Assert.Equal(1.0, C(g).Policies["base:slow"]);
        g.EndTurn(); Assert.Equal(1.0, C(g).Policies["base:slow"]);
        g.EndTurn(); Assert.Equal(5.0, C(g).Policies["base:slow"]);
    }

    [Fact]
    public void TurnReportExplainsChange()
    {
        var g = New();
        g.Apply(new SetPolicy("base:spend", 60));
        var report = g.EndTurn();
        var change = report.Changes.Single(c => c.ValueId == "base:health");
        double rate = change.Contributions.Sum(c => c.Amount);
        Assert.NotEmpty(change.Contributions);
        Assert.Equal(rate * 0.25, change.After - change.Before, 9);
        Assert.Contains(change.Contributions, c => c.EdgeId == "base:spend_health" && c.FromId == "base:spend");
    }

    [Fact]
    public void ReportListsEveryValue()
    {
        var report = New().EndTurn();
        Assert.Equal(11, report.Changes.Count);
        Assert.Equal(report.Changes.Select(c => c.ValueId).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                     report.Changes.Select(c => c.ValueId).ToArray());
    }

    [Fact]
    public void SameSeedSameActionsSameState()
    {
        var a = Run(7);
        var b = Run(7);
        Assert.Equal(Fingerprint(a), Fingerprint(b));
        Assert.Equal(80, a.State.Turn);
    }

    private static Game Run(ulong seed)
    {
        var g = New(seed);
        for (int t = 0; t < 80; t++)
        {
            if (t == 5) g.Apply(new SetPolicy("base:tax", 45));
            if (t == 20) g.Apply(new SetPolicy("base:spend", 50));
            g.EndTurn();
        }
        return g;
    }

    internal static string Snapshot(Game g) => Fingerprint(g);

    internal static string Fingerprint(Game g)
    {
        var s = g.State;
        var parts = new List<string> { $"turn={s.Turn}", $"year={GameFixture.Bits(s.Year)}" };
        foreach (var (cid, c) in s.Countries)
        {
            parts.AddRange(c.Values.Select(kv => $"{cid}/v/{kv.Key}={GameFixture.Bits(kv.Value)}"));
            parts.AddRange(c.Policies.Select(kv => $"{cid}/p/{kv.Key}={GameFixture.Bits(kv.Value)}"));
            parts.AddRange(c.Satisfaction.Select(kv => $"{cid}/s/{kv.Key}={GameFixture.Bits(kv.Value)}"));
        }
        return string.Join("|", parts);
    }

    private static Dictionary<string, double> SatisfactionAfterOneTurn(Action<Dictionary<string, string>> mutate)
    {
        var g = New(1, mutate);
        g.EndTurn();
        return new Dictionary<string, double>(C(g).Satisfaction);
    }

    private static void AddGroup(Dictionary<string, string> f, string weights)
    {
        f["voter_groups/g.yaml"] += $"- id: base:elders\n  size: 1\n  weight: 1\n  value_weights: {{{weights}}}\n";
        f["localization/cs.yaml"] += "name.base:elders: cs\n";
        f["localization/en.yaml"] += "name.base:elders: en\n";
    }

    [Fact]
    public void ValueNearZeroDoesNotSaturateVoterSatisfaction()
    {
        var s = SatisfactionAfterOneTurn(f =>
        {
            AddGroup(f, "base:inflation: -1.5");
            f["scenarios/s.yaml"] = f["scenarios/s.yaml"].Replace("{base:health: 65}", "{base:health: 65, base:inflation: 0.001}");
        });
        Assert.InRange(s["base:elders"], -0.5, 0.5);
    }

    [Fact]
    public void EventShocksReachVoters()
    {
        var s = SatisfactionAfterOneTurn(f =>
        {
            AddGroup(f, "base:health: 2.0");
            f["events/ev.yaml"] = "- id: base:epidemic\n  condition: \"base:gdp > 0\"\n  probability: 1\n  text_key: event.epidemic\n  effects:\n    - {value: base:health, delta: -20}\n";
            f["localization/cs.yaml"] += "event.epidemic: cs\n";
            f["localization/en.yaml"] += "event.epidemic: en\n";
        });
        Assert.True(s["base:elders"] < -0.1, $"satisfaction {s["base:elders"]}");
    }
}
