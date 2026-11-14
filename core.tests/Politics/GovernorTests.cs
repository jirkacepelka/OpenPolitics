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

    // ---- the leader's own agenda and crisis response (wave 2) ----------------------------------------------------

    private static readonly Dictionary<string, PolicyDef> Costly = new()
    {
        ["t:tax"] = new("t:tax", "percent", 0, 40, 30, null, 0, 0, null, "x"),
        ["t:spend"] = new("t:spend", "percent", 0, 20, 10, null, 1.0, 0, null, "x"),
        ["t:other"] = new("t:other", "percent", 0, 10, 6, null, 1.0, 0, null, "x"),
    };

    private static PartyDef Leader(double taxTarget = 20, double spendTarget = 15) => new(
        "l", "name.l",
        new Dictionary<string, PlatformPosition> { ["t:tax"] = new(taxTarget, 1), ["t:spend"] = new(spendTarget, 1) },
        new Dictionary<string, double>(), false, new List<DemandDef>(), new List<RedLineDef>(), 0, new List<FactionDef>(), "x");

    private static (string PolicyId, double Value)? Move(Government g, Func<string, double> policy, Func<string, double> value,
        int turn, PartyDef? leader = null, params RedLineDef[] redLines) =>
        Governor.Move(g, policy, value, Costly, turn, leader ?? Leader(), redLines);

    private static double At(string id) => id switch { "t:tax" => 30, "t:spend" => 10, _ => 6 };

    [Fact]
    public void CommitmentsStillComeFirst()
    {
        var g = G(C("a", "t:other", ">=", 9, 6, 3));
        Assert.Equal("t:other", Move(g, At, _ => 0, 4)!.Value.PolicyId);
    }

    [Fact]
    public void WithoutCommitmentsTheLeaderMovesTowardItsOwnPlatformEverySecondTurn()
    {
        var g = G();
        var taxFirst = Leader(taxTarget: 15, spendTarget: 10); // tax gap 15/40 = 0.375 is the biggest
        Assert.Equal(("t:tax", 26.25), Move(g, At, _ => 0, 4, taxFirst)!.Value); // 30 + 0.25 * (15 - 30)
        Assert.Null(Move(g, At, _ => 0, 5, taxFirst));
    }

    [Fact]
    public void NothingToDoWhenAlignedAndCalm()
    {
        var aligned = Leader(taxTarget: 30, spendTarget: 10);
        Assert.Null(Move(G(), At, _ => 0, 4, aligned));
    }

    [Fact]
    public void DeficitCrisisLowersTheBiggestSpendingLine()
    {
        var calmLeader = Leader(taxTarget: 30, spendTarget: 10);
        var move = Move(G(), At, id => id == "base:deficit_pct" ? 6.0 : 0.0, 5, calmLeader)!.Value;
        Assert.Equal(("t:spend", 8.0), move); // 10 - 10 % of its range of 20
    }

    [Fact]
    public void UnemploymentCrisisRaisesSpendingTowardTheLeadersTarget()
    {
        var move = Move(G(), At, id => id == "base:unemployment" ? 9.0 : 0.0, 5, Leader(taxTarget: 30, spendTarget: 11))!.Value;
        Assert.Equal(("t:spend", 11.0), move); // +10 % of range would be 12, capped at the leader's own target
    }

    [Fact]
    public void ARedLineMoveIsSkippedWhenThereIsAnAlternativeAndTakenWhenThereIsNot()
    {
        var onlyTax = Leader(taxTarget: 20, spendTarget: 10);
        var red = new RedLineDef("t:tax", "<=", 28);
        // tax would go 30 -> 27.5, crossing the partner's line; nothing else to do, so it happens anyway
        Assert.Equal(("t:tax", 27.5), Move(G(), At, _ => 0, 4, onlyTax, red)!.Value);
        // with a spending gap as an alternative, the governor takes that instead
        var both = Leader(taxTarget: 10, spendTarget: 15); // tax gap 0.5 would win, but it crosses the line
        Assert.Equal("t:tax", Move(G(), At, _ => 0, 4, both)!.Value.PolicyId);
        Assert.Equal("t:spend", Move(G(), At, _ => 0, 4, both, red)!.Value.PolicyId);
    }

    [Fact]
    public void PlatformMovesStayInsideTheRange()
    {
        var greedy = Leader(taxTarget: 20, spendTarget: 500);
        for (int turn = 4; turn < 60; turn += 2)
        {
            var m = Governor.Move(G(), At, _ => 0, Costly, turn, greedy, Array.Empty<RedLineDef>());
            if (m is { } mv) Assert.InRange(mv.Value, 0.0, 40.0);
        }
    }

    [Fact]
    public void AnUnmetDeficitDemandTightensSpendingEvenBelowTheCrisisLevel()
    {
        var g = G(C("balanced", "base:deficit_pct", "<=", 0, 1, 2, kind: "value"));
        var move = Move(g, At, id => id == "base:deficit_pct" ? 2.0 : 0.0, 5, Leader(taxTarget: 30, spendTarget: 10))!.Value;
        Assert.Equal(("t:spend", 8.0), move);
    }

    [Fact]
    public void ASatisfiedDeficitDemandLeavesThingsAlone()
    {
        var g = G(C("balanced", "base:deficit_pct", "<=", 0, 1, 2, kind: "value"));
        Assert.Null(Move(g, At, id => id == "base:deficit_pct" ? -1.0 : 0.0, 5, Leader(taxTarget: 30, spendTarget: 10)));
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
