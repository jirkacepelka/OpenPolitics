using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0016 (1): a junior partner gives the leader an ultimatum - deliver within N quarters or we leave.</summary>
public class PartnerUltimatumTests
{
    // a leads; the player b is a partner. c and d ask a price nobody can pay (99), so an AI leader cannot patch a lost majority with them
    // unless the test says otherwise. b's demand d1 asks spend >= 41 (spend is 40 at the start).
    private static Spec[] Parties(double cPrice = 99) => new[]
    {
        new Spec("a", 0.42, Tax: 30), new Spec("b", 0.20, Tax: 50), new Spec("c", 0.15, Price: cPrice, Tax: 34), new Spec("d", 0.15, Price: 99, Tax: 36),
        new Spec("z", 0.08, Price: 99),
    };

    private static Game Make(string player = "b", string[]? government = null, string? redLines = null, double cPrice = 99) =>
        New(Load(Parties(cPrice), player, government: government ?? new[] { "a", "b" }, leader: "a", turnsToElection: 60, interval: 60, redLines: redLines), 1);

    private static IssueUltimatum Demand(string id = "d1", int deadline = 3) => new("demand", id, deadline);
    private static double Capital(Game g) => Pol(g).Party!.PoliticalCapital;
    private static double Credibility(Game g) => Pol(g).Party!.Credibility;
    private static double Spend(Game g) => g.State.Countries.Values.Single().Policies["base:spend"];

    [Fact]
    public void OnlyAJuniorPartnerMayGiveAnUltimatum()
    {
        Assert.Equal("error.not_partner", Make("a").Apply(Demand()).ErrorKey);
        Assert.Equal("error.not_partner", Make("c").Apply(Demand()).ErrorKey);
        var g = Make();
        Assert.Equal("error.bad_deadline", g.Apply(Demand(deadline: 1)).ErrorKey);
        Assert.Equal("error.bad_deadline", g.Apply(Demand(deadline: PoliticsLimits.UltimatumMaxDeadline + 1)).ErrorKey);
        Assert.Equal("error.unknown_demand", g.Apply(new IssueUltimatum("demand", "nope", 3)).ErrorKey);
        Assert.Equal("error.not_in_platform", g.Apply(new IssueUltimatum("policy", "base:spend", 3)).ErrorKey);
        Assert.Equal("error.unknown_action", g.Apply(new IssueUltimatum("bribe", "d1", 3)).ErrorKey);
        Assert.Equal("error.no_ultimatum", g.Apply(new WithdrawUltimatum()).ErrorKey);
    }

    [Fact]
    public void ItCostsCapitalAndOnlyOneCanBeOpen()
    {
        var g = Make();
        double before = Capital(g);
        Assert.True(g.Apply(Demand()).Accepted);
        Assert.Equal(before - PoliticsLimits.UltimatumCost, Capital(g), 9);
        Assert.Equal("error.ultimatum_open", g.Apply(Demand("d2")).ErrorKey);
        var u = Pol(g).PlayerUltimatum!;
        Assert.Equal(("demand", "d1", "base:spend", 40.0, 41.0, 3), (u.Kind, u.SubjectId, u.PolicyId, u.From, u.Target, u.TurnsLeft));
        Assert.Null(u.Answer); // answered at the end of the quarter
    }

    [Fact]
    public void APivotalPartnerWithoutAlternativesIsDelivered()
    {
        var g = Make();
        Assert.Equal("deliver", g.PreviewUltimatum("demand", "d1", 3)!.Answer);
        double cred = Credibility(g);
        Assert.True(g.Apply(Demand()).Accepted);
        var r = g.EndTurn();
        var answer = Assert.Single(r.PoliticalEvents.OfType<UltimatumAnswered>());
        Assert.Equal("deliver", answer.Answer);
        Assert.Single(r.PoliticalEvents.OfType<UltimatumDelivered>());
        Assert.Contains(r.PoliticalEvents.OfType<GovernmentMeasure>(), m => m.PolicyId == "base:spend" && m.To == 41.0);
        Assert.Null(Pol(g).PlayerUltimatum);
        Assert.True(Pol(g).UltimatumCooldown > 0);
        Assert.True(Credibility(g) > cred - 1e-9);
        Assert.True(Pol(g).Party!.Relations["base:a"] < 0.3); // the leader does not like being pushed
        g.EndTurn();
        Assert.Equal(41.0, Spend(g));
        Assert.Contains("base:b", Pol(g).GovernmentParties);
    }

    [Fact]
    public void AStallingLeaderMayGiveInBeforeTheDeadline()
    {
        // c is cheap now, so the leader could replace b: less pressure, it stalls first and gives in as the deadline comes closer
        var g = Make(cPrice: 0);
        var preview = g.PreviewUltimatum("demand", "d1", 4)!;
        Assert.Equal("stall", preview.Answer);
        Assert.NotNull(preview.DeliversAfter);
        Assert.True(g.Apply(Demand(deadline: 4)).Accepted);
        var reports = Run(g, 4);
        Assert.Equal("stall", Events<UltimatumAnswered>(reports).First().Answer);
        Assert.Single(Events<UltimatumDelivered>(reports));
        Assert.Empty(Events<UltimatumCarriedOut>(reports));
        Assert.Contains("base:b", Pol(g).GovernmentParties);
    }

    [Fact]
    public void ARefusedUltimatumThatRunsOutTakesThePartyOutAndTheGovernmentFalls()
    {
        // spend >= 41 crosses a red line every party has: the leader refuses at once
        var g = Make(redLines: "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 41}\n");
        Assert.Equal("refuse", g.PreviewUltimatum("demand", "d1", 2)!.Answer);
        double cred = Credibility(g);
        Assert.True(g.Apply(Demand(deadline: 2)).Accepted);
        var r1 = g.EndTurn();
        var answer = Assert.Single(r1.PoliticalEvents.OfType<UltimatumAnswered>());
        Assert.Equal(("refuse", "red_line"), (answer.Answer, answer.Reason));
        Assert.Equal("refuse", Pol(g).PlayerUltimatum!.Answer);
        Assert.Equal(1, Pol(g).PlayerUltimatum!.TurnsLeft);
        var r2 = g.EndTurn();
        Assert.Single(r2.PoliticalEvents.OfType<UltimatumCarriedOut>());
        Assert.Single(r2.PoliticalEvents.OfType<GovernmentFell>()); // a alone has no majority and nobody can patch it
        Assert.Equal(Role.Opposition, Pol(g).PlayerRole);
        Assert.Equal(Phase.Formation, Pol(g).Phase);
        Assert.True(Credibility(g) < cred);
        Assert.True(Pol(g).Grudge!.GetValueOrDefault("base:a|base:b") > 0); // the leader resents the walk-out as after an AI partner's
        Assert.Null(Pol(g).PlayerUltimatum);
    }

    [Fact]
    public void ACarriedOutUltimatumWithoutMajorityLossLeavesTheRestGoverning()
    {
        var g = Make(government: new[] { "a", "b", "c" }, redLines: "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 41}\n");
        Assert.False(g.PreviewUltimatum("demand", "d1", 2)!.Pivotal);
        Assert.True(g.Apply(Demand(deadline: 2)).Accepted);
        var reports = Run(g, 2);
        Assert.Single(Events<UltimatumCarriedOut>(reports));
        Assert.Empty(Events<GovernmentFell>(reports));
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        Assert.Equal(new[] { "base:a", "base:c" }, Pol(g).GovernmentParties);
        Assert.Equal(Role.Opposition, Pol(g).PlayerRole);
    }

    [Fact]
    public void WithdrawingCostsALittleCredibilityAndEndsIt()
    {
        var g = Make(redLines: "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 41}\n");
        Assert.True(g.Apply(Demand(deadline: 2)).Accepted);
        g.EndTurn(); // refused
        double cred = Credibility(g);
        Assert.True(g.Apply(new WithdrawUltimatum()).Accepted);
        Assert.Equal(cred - PoliticsLimits.UltimatumWithdrawCredibility, Credibility(g), 9);
        Assert.Null(Pol(g).PlayerUltimatum);
        var reports = Run(g, 3);
        Assert.Empty(Events<UltimatumCarriedOut>(reports));
        Assert.Contains("base:b", Pol(g).GovernmentParties);
        Assert.Equal("error.ultimatum_cooldown", g.Apply(Demand()).ErrorKey);
    }

    [Fact]
    public void APolicyUltimatumAsksAtMostAQuarterOfTheRangeTowardTheProgramme()
    {
        var g = Make();
        var p = g.PreviewUltimatum("policy", "base:tax", 3)!;
        Assert.Null(p.ErrorKey);
        Assert.Equal("base:tax", p.PolicyId);
        Assert.Equal(50.0, p.Target, 9); // b wants 50; tax is 40: within a quarter of the range (15)
        var far = New(Load(new[] { new Spec("a", 0.42, Tax: 30), new Spec("b", 0.20, Tax: 60), new Spec("c", 0.15, Price: 99), new Spec("d", 0.15, Price: 99),
            new Spec("z", 0.08, Price: 99) }, "b", government: new[] { "a", "b" }, leader: "a", turnsToElection: 60, interval: 60));
        Assert.Equal(40.0 + 0.25 * 60, far.PreviewUltimatum("policy", "base:tax", 3)!.Target, 9); // capped
    }

    [Fact]
    public void ItSurvivesSaveAndLoad()
    {
        var packs = Load(Parties(), "b", government: new[] { "a", "b" }, leader: "a", turnsToElection: 60, interval: 60,
            redLines: "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 41}\n");
        var g = New(packs);
        Assert.True(g.Apply(Demand(deadline: 3)).Accepted);
        g.EndTurn();
        var loaded = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(Pol(g).PlayerUltimatum, Pol(loaded).PlayerUltimatum);
        Assert.True(g.Apply(new WithdrawUltimatum()).Accepted);
        var again = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(Pol(g).Party!.Credibility, Pol(again).Party!.Credibility, 12);
        Assert.Null(Pol(again).PlayerUltimatum);
    }

    [Fact]
    public void ItLapsesWhenTheGovernmentChanges()
    {
        var e = New(Load(Parties(), "b", government: new[] { "a", "b" }, leader: "a", turnsToElection: 2, interval: 60,
            redLines: "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 41}\n"));
        Assert.True(e.Apply(Demand(deadline: 4)).Accepted);
        var reports = Run(e, 3);
        Assert.Single(Events<UltimatumLapsed>(reports));
        Assert.Empty(Events<UltimatumCarriedOut>(reports));
        Assert.Null(Pol(e).PlayerUltimatum);
    }
}
