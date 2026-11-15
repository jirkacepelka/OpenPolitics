using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class PrioritiesAndPromisesTests
{
    private static readonly Spec[] Three = { new("a", 0.50, Tax: 30), new("b", 0.30, Tax: 31), new("c", 0.20, Tax: 45) };

    private static Game Governing(int turnsToElection = 12) =>
        New(Load(Three, "a", government: new[] { "a" }, turnsToElection: turnsToElection, mutate: GameFixture.FreePolicies));

    [Fact]
    public void SettingAPriorityMovesTheWeightByAtMostTwoTenthsAndCostsOneCapital()
    {
        var g = Governing();
        double before = Pol(g).Party!.PoliticalCapital;
        Assert.Equal(1.0, Pol(g).Party!.Priorities!["base:tax"]);
        Assert.True(g.Apply(new SetPriority("base:tax", 0.1)).Accepted);
        Assert.Equal(0.8, Pol(g).Party!.Priorities!["base:tax"], 9);
        Assert.Equal(before - 1.0, Pol(g).Party!.PoliticalCapital, 9);
    }

    [Fact]
    public void ARefusedPriorityCostsNothing()
    {
        var g = Governing();
        double before = Pol(g).Party!.PoliticalCapital;
        Assert.Equal("error.not_in_platform", g.Apply(new SetPriority("base:spend", 0.5)).ErrorKey);
        Assert.Equal("error.value_out_of_range", g.Apply(new SetPriority("base:tax", 1.5)).ErrorKey);
        Assert.Equal("error.value_out_of_range", g.Apply(new SetPriority("base:tax", double.NaN)).ErrorKey);
        Assert.Equal(before, Pol(g).Party!.PoliticalCapital);
    }

    [Fact]
    public void ALowerPriorityChangesWhatVotersSee()
    {
        var packs = Load(Three, "a", government: new[] { "a" }, turnsToElection: 12, extraGroups: true);
        var plain = New(packs);
        var shifted = New(packs);
        for (int i = 0; i < 4; i++) shifted.Apply(new SetPriority("base:tax", 0.0));
        Run(plain, 3); Run(shifted, 3);
        Assert.NotEqual(Pol(plain).Support["base:a"], Pol(shifted).Support["base:a"]);
    }

    [Fact]
    public void APromiseNeedsAnElectionWithinTwoYears()
    {
        var early = Governing(turnsToElection: 12);
        Assert.Equal("error.too_early", early.Apply(new MakePromise("d1")).ErrorKey);
        var soon = Governing(turnsToElection: 6);
        double before = Pol(soon).Party!.PoliticalCapital;
        Assert.True(soon.Apply(new MakePromise("d1")).Accepted);
        Assert.Equal(before - 2.0, Pol(soon).Party!.PoliticalCapital, 9);
        Assert.Equal(new[] { "d1" }, Pol(soon).Party!.Promises);
    }

    [Fact]
    public void PromiseRulesAreEnforced()
    {
        var g = Governing(turnsToElection: 6);
        Assert.Equal("error.unknown_demand", g.Apply(new MakePromise("nope")).ErrorKey);
        Assert.True(g.Apply(new MakePromise("d1")).Accepted);
        Assert.Equal("error.already_promised", g.Apply(new MakePromise("d1")).ErrorKey);
        Assert.True(g.Apply(new MakePromise("d2")).Accepted);
        Assert.Equal("error.too_many_promises", g.Apply(new MakePromise("d3")).ErrorKey);
    }

    [Fact]
    public void APromiseIsKeptOrBrokenAtTheElection()
    {
        var kept = Governing(turnsToElection: 4);
        kept.Apply(new MakePromise("d1"));
        kept.Apply(new SetPolicy("base:spend", 60));
        var keptReports = Run(kept, 4);
        Assert.Single(Events<PromiseKept>(keptReports));
        Assert.Empty(Events<PromiseBroken>(keptReports));
        Assert.Empty(Pol(kept).Party!.Promises!);

        var broken = Governing(turnsToElection: 4);
        broken.Apply(new MakePromise("d1"));
        var brokenReports = Run(broken, 4);
        Assert.Single(Events<PromiseBroken>(brokenReports));
        Assert.True(Pol(broken).Party!.Credibility < Pol(kept).Party!.Credibility);
    }

    [Fact]
    public void PrioritiesAndPromisesSurviveSaveAndLoad()
    {
        var packs = Load(Three, "a", government: new[] { "a" }, turnsToElection: 6);
        var live = New(packs);
        live.Apply(new SetPriority("base:tax", 0.5));
        live.Apply(new MakePromise("d2"));
        var loaded = Simulation.Load(packs, SaveData.FromYaml(live.Save().ToYaml()));
        Assert.Equal(Pol(live).Party!.Priorities, Pol(loaded).Party!.Priorities);
        Assert.Equal(Pol(live).Party!.Promises, Pol(loaded).Party!.Promises);
    }

    private static double VoteOf(Game g, string party)
    {
        var e = Events<ElectionHeld>(Run(g, 5)).Single();
        return e.Votes[party];
    }

    [Fact]
    public void ABrokenPromiseCostsVotesAtTheElectionThatSettlesIt()
    {
        var packs = Load(Three, "a", government: new[] { "a" }, turnsToElection: 5, extraGroups: true);
        var none = New(packs);
        var broken = New(packs);
        Assert.True(broken.Apply(new MakePromise("d1")).Accepted); // spend stays at 40: never delivered
        Assert.True(VoteOf(broken, "base:a") < VoteOf(none, "base:a"));
    }

    [Fact]
    public void AKeptPromiseBeatsABrokenOneAtTheElectionThatSettlesIt()
    {
        var packs = Load(Three, "a", government: new[] { "a" }, turnsToElection: 5, extraGroups: true, mutate: GameFixture.FreePolicies);
        var broken = New(packs);
        broken.Apply(new MakePromise("d1"));
        var kept = New(packs);
        kept.Apply(new MakePromise("d1"));
        kept.Apply(new SetPolicy("base:spend", 60));
        Assert.True(VoteOf(kept, "base:a") > VoteOf(broken, "base:a"));
    }

    [Fact]
    public void ADemandAlreadyMetCannotBePromised()
    {
        var g = Governing(turnsToElection: 6);
        g.Apply(new SetPolicy("base:spend", 60));
        g.EndTurn();
        var r = g.Apply(new MakePromise("d1"));
        Assert.Equal("error.already_met", r.ErrorKey);
        Assert.Empty(Pol(g).Party!.Promises!);
    }

    [Fact]
    public void AnUnchangedPriorityIsRefusedAndCostsNothing()
    {
        var g = Governing();
        double before = Pol(g).Party!.PoliticalCapital;
        Assert.Equal("error.no_change", g.Apply(new SetPriority("base:tax", 1.0)).ErrorKey);
        Assert.Equal(before, Pol(g).Party!.PoliticalCapital);
        Assert.Empty(g.Save().Actions);
    }
}
