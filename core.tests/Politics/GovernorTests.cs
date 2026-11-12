using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class GovernorTests
{
    private static readonly Dictionary<string, PolicyDef> Policies = new()
    {
        ["t:tax"] = new("t:tax", "percent", 0, 40, 30, null, 0, 0, null, "x"),
        ["t:spend"] = new("t:spend", "percent", 0, 20, 10, null, 0, 0, null, "x"),
    };

    private static Commitment C(string id, string subject, string op, double target, double baseline, double weight, string kind = "policy") =>
        new("p", new DemandDef(id, kind, subject, op, target, weight, 8), baseline, 8);

    private static Government G(params Commitment[] cs) => new("l", new[] { "l", "p" }, cs, 0);

    [Fact]
    public void MovesTheLargestUnmetCommitmentByAQuarterOfTheGap()
    {
        var g = G(C("small", "t:spend", ">=", 14, 10, 1), C("big", "t:tax", "<=", 20, 30, 3));
        var move = Governor.Move(g, id => id == "t:tax" ? 30 : 10, _ => 0, Policies, 4)!.Value;
        Assert.Equal(("t:tax", 27.5), move); // 30 + 0.25 * (20 - 30)
    }

    [Fact]
    public void StopsWhenEverythingIsSatisfied()
    {
        var g = G(C("a", "t:tax", "<=", 20, 30, 3));
        Assert.Null(Governor.Move(g, _ => 20, _ => 0, Policies, 4));
        Assert.Null(Governor.Move(g, _ => 18, _ => 0, Policies, 4));
    }

    [Fact]
    public void IgnoresValueCommitments()
    {
        var g = G(C("v", "t:gdp", ">=", 100, 50, 3, kind: "value"));
        Assert.Null(Governor.Move(g, _ => 0, _ => 50, Policies, 4));
    }

    [Fact]
    public void RespectsThePolicyRange()
    {
        var g = G(C("huge", "t:spend", ">=", 100, 10, 3)); // asks for more than the policy allows
        var (_, value) = Governor.Move(g, _ => 19.9, _ => 0, Policies, 4)!.Value;
        Assert.Equal(20.0, value);
    }

    [Fact]
    public void EventuallyReachesTheTargetInsteadOfCrawlingForever()
    {
        var g = G(C("a", "t:spend", ">=", 11, 10, 3));
        double x = 10;
        for (int i = 0; i < 60 && Governor.Move(g, _ => x, _ => 0, Policies, 4) is { } m; i++) x = m.Value;
        Assert.True(x >= 11);
    }

    // ---- inside a game: player z (never in government) watches the AI run it -----------------------------------

    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Price: 0, Tax: 30), new Spec("b", 0.33, Price: 3, Tax: 31),
        new Spec("c", 0.22, Price: 3, Tax: 45), new Spec("z", 0.03),
    };

    private static (Game Game, List<TurnReport> Reports) WatchingWithReports(ulong seed = 1)
    {
        var g = New(Load(Parties(), "z", interval: 60), seed);
        var reports = Run(g, 2); // election, the AI forms a + b with b's d1 (spend >= 41) agreed
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        return (g, reports);
    }

    private static Game Watching(ulong seed = 1) => WatchingWithReports(seed).Game;

    private static double Spend(Game g) => g.State.Countries.Values.Single().Policies["base:spend"];

    [Fact]
    public void TheGovernorMovesPoliciesWhenThePlayerDoesNotLead()
    {
        var (g, reports) = WatchingWithReports();
        Assert.Equal(40.0, Spend(g)); // the first move is queued in the turn the government forms
        reports.AddRange(Run(g, 6));
        Assert.True(Spend(g) >= 41, $"spend {Spend(g)}");
        var m = Events<GovernmentMeasure>(reports).First();
        Assert.Equal(("base:spend", 40.0), (m.PolicyId, m.From));
    }

    [Fact]
    public void DoesNotRunWhenThePlayerIsTheLeader()
    {
        var g = New(Load(Parties(), "a", interval: 60));
        g.EndTurn();
        Assert.True(g.Apply(new ProposeCoalition(new[] { "base:b" }, new[] { new AgreedDemand("base:b", "d1") })).Accepted);
        var reports = Run(g, 6);
        Assert.Equal(40.0, Spend(g));
        Assert.Empty(Events<GovernmentMeasure>(reports));
    }

    [Fact]
    public void DoesNotRunDuringFormation()
    {
        var g = New(Load(Parties(), "z", interval: 60));
        var reports = new List<TurnReport> { g.EndTurn() }; // election turn: formation starts
        Assert.Empty(Events<GovernmentMeasure>(reports));
        Assert.Equal(Phase.Formation, Pol(g).Phase);
    }

    [Fact]
    public void PartnersStayLoyalLongerUnderTheGovernorThanUnderAnIdleLeader()
    {
        var watched = Watching();
        Run(watched, 16);
        var idle = New(Load(Parties(), "a", interval: 60));
        idle.EndTurn();
        idle.Apply(new ProposeCoalition(new[] { "base:b" }, new[] { new AgreedDemand("base:b", "d1") }));
        Run(idle, 16);
        var govLoyalty = Pol(watched).Loyalty?.GetValueOrDefault("base:b") ?? 0.0;
        var idleLoyalty = Pol(idle).Loyalty?.GetValueOrDefault("base:b") ?? 0.0;
        Assert.True(govLoyalty > idleLoyalty, $"governor {govLoyalty} vs idle {idleLoyalty}");
    }

    [Fact]
    public void GovernorMovesAreDeterministicAndReplayed()
    {
        var packs = Load(Parties(), "z", interval: 60);
        var g = New(packs, 5);
        Run(g, 14);
        var h = Simulation.Load(packs, g.Save());
        Assert.Equal(g.State.Countries.Values.Single().Policies, h.State.Countries.Values.Single().Policies);
        Assert.Equal(Pol(g).Loyalty, Pol(h).Loyalty);
    }
}
