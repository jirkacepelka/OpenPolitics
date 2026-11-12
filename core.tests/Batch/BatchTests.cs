using OpenPolitics.Cli.Commands;
using OpenPolitics.Core.Batch;
using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Content;

namespace OpenPolitics.Core.Tests.Batch;

public class BatchTests
{
    private const string Scenario = "base:cz_2015_sandbox";
    private static PackSet Base => BasePackTests.LoadBase().Packs!;

    // ---- strategies -----------------------------------------------------------

    [Fact]
    public void PassiveDoesNothing()
    {
        var g = Simulation.NewGame(Base, Scenario, 1);
        Assert.Empty(new PassiveStrategy().Choose(g.State, Base, new Pcg32(1)));
    }

    [Fact]
    public void RandomIsDeterministicForTheSameRngAndOnlyProducesValidActions()
    {
        var packs = Base;
        var g1 = Simulation.NewGame(packs, Scenario, 1);
        var g2 = Simulation.NewGame(packs, Scenario, 1);
        var r1 = new Pcg32(5); var r2 = new Pcg32(5);
        var s = new RandomStrategy();
        for (int i = 0; i < 60; i++)
        {
            var a = s.Choose(g1.State, packs, r1);
            var b = s.Choose(g2.State, packs, r2);
            Assert.Equal(a, b);
            Assert.InRange(a.Count, 0, 2);
            foreach (var action in a) Assert.True(g1.Apply(action).Accepted);
            foreach (var action in b) g2.Apply(action);
            g1.EndTurn(); g2.EndTurn();
        }
    }

    [Fact]
    public void ExtremeSetsEveryPolicyToItsMinOrMaxEveryEightTurns()
    {
        var packs = Base;
        var g = Simulation.NewGame(packs, Scenario, 1);
        var actions = new ExtremeStrategy().Choose(g.State, packs, new Pcg32(1)).Cast<SetPolicy>().ToList();
        Assert.Equal(packs.Policies.Count, actions.Count);
        foreach (var a in actions)
        {
            var p = packs.Policies.Single(x => x.Id == a.PolicyId);
            Assert.True(a.Value == p.Min || a.Value == p.Max);
        }
        for (int i = 0; i < 3; i++) g.EndTurn();
        Assert.Empty(new ExtremeStrategy().Choose(g.State, packs, new Pcg32(1)));
    }

    // ---- stability checks and batches ----------------------------------------

    [Fact]
    public void NoCeilingCheckFlagsASyntheticCappedSeries()
    {
        var capped = Enumerable.Range(0, 401).Select(t => 100.0 * (1 - Math.Exp(-t / 20.0))).ToList();
        var growing = Enumerable.Range(0, 401).Select(t => 100.0 * Math.Pow(1.004, t)).ToList();
        Assert.True(StabilityChecks.DetectCeiling(capped));
        Assert.False(StabilityChecks.DetectCeiling(growing));
        Assert.False(StabilityChecks.DetectCeiling(growing.Take(100).ToList())); // too short to judge
    }

    [Fact]
    public void PassiveBatchIsStable()
    {
        var r = BatchRunner.Run(Base, Scenario, 100, 80, new PassiveStrategy());
        Assert.True(r.Stable, Describe(r));
        Assert.False(r.CeilingDetected);
    }

    [Fact]
    public void RandomBatchIsStable()
    {
        var r = BatchRunner.Run(Base, Scenario, 100, 80, new RandomStrategy());
        Assert.True(r.Stable, Describe(r));
    }

    [Fact]
    public void PassiveBatchHasNoDebtSpiral()
    {
        Assert.Equal(0, BatchRunner.Run(Base, Scenario, 50, 80, new PassiveStrategy()).DebtSpiralGames);
    }

    [Fact]
    public void ExtremeBatchDoesNotProduceNaNOrInf()
    {
        var r = BatchRunner.Run(Base, Scenario, 50, 80, new ExtremeStrategy());
        Assert.Equal(0, r.NanOrInfGames);
    }

    [Fact]
    public void BatchIsDeterministic()
    {
        var a = BatchRunner.Run(Base, Scenario, 20, 40, new RandomStrategy());
        var b = BatchRunner.Run(Base, Scenario, 20, 40, new RandomStrategy());
        Assert.Equal(Describe(a), Describe(b));
    }

    private static string Describe(BatchReport r) =>
        $"{r.Strategy}: games={r.Games} nan={r.NanOrInfGames} rate={r.ExtremeRateGames} collapse={r.CollapseGames} " +
        $"explosion={r.ExplosionGames} spiral={r.DebtSpiralGames} ceiling={r.CeilingDetected} notes=[{string.Join("; ", r.Notes)}]";

    // ---- CLI commands ---------------------------------------------------------

    private static string BaseDir => Path.Combine(BasePackTests.RepoRoot(), "data", "base");

    [Fact]
    public void RunTwiceGivesIdenticalOutput()
    {
        string Run()
        {
            var w = new StringWriter();
            int code = RunCommand.Execute(new[] { "--packs", BaseDir, "--scenario", Scenario, "--seed", "42", "--turns", "20" }, w);
            Assert.Equal(0, code);
            return w.ToString();
        }
        var a = Run();
        Assert.Equal(a, Run());
        var lines = a.TrimEnd().Split('\n');
        Assert.Equal(22, lines.Length); // header + turn 0..20
        Assert.StartsWith("turn\tyear\t", lines[0]);
        Assert.Contains("base:gdp", lines[0]);
    }

    [Fact]
    public void RunReportsUsageProblems()
    {
        var w = new StringWriter();
        Assert.Equal(2, RunCommand.Execute(new[] { "--seed", "x" }, w));
        Assert.Equal(2, RunCommand.Execute(new[] { "--packs", "/no/such/dir" }, w));
    }

    [Fact]
    public void BatchCommandPrintsReportAndSucceedsWhenStable()
    {
        var w = new StringWriter();
        int code = BatchCommand.Execute(new[] { "--packs", BaseDir, "--games", "10", "--strategy", "passive", "--turns", "40" }, w);
        Assert.Equal(0, code);
        Assert.Contains("Stable", w.ToString());
    }

    // ---- sample mod -----------------------------------------------------------

    [Fact]
    public void SampleModLoadsOnTopOfBase()
    {
        var root = BasePackTests.RepoRoot();
        var r = PackLoader.Load(new IPackSource[]
        {
            new DirectoryPackSource(Path.Combine(root, "data", "base")),
            new DirectoryPackSource(Path.Combine(root, "data", "examples", "sample-mod")),
        });
        Assert.Empty(r.Diagnostics);
        Assert.Contains(r.Packs!.Policies, p => p.Id == "sample:green_subsidy");
        Assert.Equal(2.0, r.Packs.Policies.Single(p => p.Id == "base:police").Default); // overridden by the mod
        var g = Simulation.NewGame(r.Packs, Scenario, 1);
        Assert.True(g.Apply(new SetPolicy("sample:green_subsidy", 1.5)).Accepted);
        for (int i = 0; i < 12; i++) g.EndTurn();
    }
}
