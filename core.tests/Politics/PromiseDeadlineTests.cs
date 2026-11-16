using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>
/// Decision 0015 (1): a promise is "what we will do if you elect us". Made in the window before election E1, it is carried over E1 and judged at the
/// election E2 that ends the next term. It counts (kept or broken) only when the party sat in the government for at least half of that term.
/// </summary>
public class PromiseDeadlineTests
{
    private const int Interval = 4;

    private static Game Make(Spec[] parties, string player, string[] government, string? leader = null, int turnsToElection = 2, int interval = Interval) =>
        New(Load(parties, player, government: government, leader: leader, interval: interval, turnsToElection: turnsToElection, mutate: GameFixture.FreePolicies));

    // a holds a majority alone, so after the election it forms the government on its own.
    private static readonly Spec[] Big = { new("a", 0.60, Tax: 30), new("b", 0.25, Tax: 31), new("c", 0.15, Tax: 45) };
    // c is small and far from the others: it stays in opposition.
    private static readonly Spec[] Small = { new("a", 0.50, Tax: 30), new("b", 0.35, Tax: 31), new("c", 0.15, Tax: 45) };
    // b needs a partner and a is closest: a is offered a place.
    private static readonly Spec[] Junior = { new("a", 0.35, Tax: 30), new("b", 0.40, Tax: 31), new("c", 0.25, Tax: 45) };

    [Fact]
    public void APromiseMadeBeforeAnElectionIsNotJudgedThere()
    {
        var g = Make(Big, "a", new[] { "a" });
        Assert.True(g.Apply(new MakePromise("d1")).Accepted);
        var p0 = Assert.Single(Pol(g).Party!.PromiseDetails!);
        Assert.False(p0.Carried);
        Assert.Equal(2 + Interval, p0.JudgedIn); // the election after the coming term
        var reports = Drive(g, 2, join: true);
        Assert.Single(Events<ElectionHeld>(reports));
        Assert.Empty(Events<PromiseBroken>(reports));
        Assert.Empty(Events<PromiseKept>(reports));
        Assert.Single(Events<PromiseCarried>(reports));
        var p = Assert.Single(Pol(g).Party!.PromiseDetails!);
        Assert.True(p.Carried);
        Assert.Equal(Pol(g).TurnsToElection, p.JudgedIn);
        Assert.Equal(new[] { "d1" }, Pol(g).Party!.Promises);
    }

    [Fact]
    public void ABrokenPromiseIsJudgedAtTheElectionAfterTheTermTheGovernmentServed()
    {
        var none = Make(Big, "a", new[] { "a" });
        var broken = Make(Big, "a", new[] { "a" });
        Assert.True(broken.Apply(new MakePromise("d1")).Accepted);
        var reports = Drive(broken, 2 + Interval, join: true);
        Drive(none, 2 + Interval, join: true);
        Assert.Equal(2, Events<ElectionHeld>(reports).Count());
        var e = Assert.Single(Events<PromiseBroken>(reports));
        Assert.True(e.Responsible);
        Assert.Equal(2 + Interval, reports.First(r => r.PoliticalEvents.OfType<PromiseBroken>().Any()).Turn);
        Assert.True(Pol(broken).Party!.Credibility < Pol(none).Party!.Credibility - 0.1);
        Assert.Empty(Pol(broken).Party!.Promises!);
    }

    [Fact]
    public void AKeptPromiseIsCreditedAtTheElectionAfterTheTermTheGovernmentServed()
    {
        var kept = Make(Big, "a", new[] { "a" });
        var broken = Make(Big, "a", new[] { "a" });
        foreach (var g in new[] { kept, broken }) Assert.True(g.Apply(new MakePromise("d1")).Accepted);
        Drive(kept, 3, join: true);
        Assert.Equal(Role.Leader, Pol(kept).PlayerRole);
        Assert.True(kept.Apply(new SetPolicy("base:spend", 60)).Accepted); // delivered in the next term
        var reports = Drive(kept, Interval - 1, join: true);
        Drive(broken, 2 + Interval, join: true);
        var e = Assert.Single(Events<PromiseKept>(reports));
        Assert.True(e.Responsible);
        Assert.True(Pol(kept).Party!.Credibility > Pol(broken).Party!.Credibility + 0.1);
    }

    [Fact]
    public void APromiseCostsNothingWhenThePartyStaysInOppositionForTheNextTerm()
    {
        var none = Make(Small, "c", new[] { "a", "b" }, "a");
        var broken = Make(Small, "c", new[] { "a", "b" }, "a");
        Assert.True(broken.Apply(new MakePromise("d1")).Accepted);
        var reports = Drive(broken, 2 + Interval, join: false);
        Drive(none, 2 + Interval, join: false);
        Assert.Equal(Role.Opposition, Pol(broken).PlayerRole);
        var e = Assert.Single(Events<PromiseBroken>(reports));
        Assert.False(e.Responsible);
        Assert.Equal(Pol(none).Party!.Credibility, Pol(broken).Party!.Credibility, 12);
    }

    [Fact]
    public void APartnerWhoLeavesEarlyInTheNextTermIsNotHeldToItsPromise()
    {
        const int interval = 10;
        var none = Make(Junior, "a", new[] { "b" }, "b", interval: interval);
        var broken = Make(Junior, "a", new[] { "b" }, "b", interval: interval);
        Assert.True(broken.Apply(new MakePromise("d1")).Accepted);
        var reports = new List<TurnReport>();
        foreach (var g in new[] { none, broken })
        {
            var mine = Drive(g, 2, join: true); // E1
            while (!(Pol(g).Phase == Phase.Governing && Pol(g).PlayerRole == Role.Partner)) mine.AddRange(Drive(g, 1, join: true));
            Assert.True(g.Apply(new LeaveCoalition()).Accepted);
            while (Events<ElectionHeld>(mine).Count() < 2) mine.AddRange(Drive(g, 1, join: false));
            if (g == broken) reports = mine;
        }
        var e = Assert.Single(Events<PromiseBroken>(reports));
        Assert.False(e.Responsible); // a quarter or two in government out of ten
        Assert.Equal(Pol(none).Party!.Credibility, Pol(broken).Party!.Credibility, 12);
    }

    [Fact]
    public void PromisesOfThePreviousCycleDoNotBlockNewOnes()
    {
        var g = Make(Big, "a", new[] { "a" });
        Assert.True(g.Apply(new MakePromise("d1")).Accepted);
        Assert.True(g.Apply(new MakePromise("d2")).Accepted);
        Assert.Equal("error.too_many_promises", g.Apply(new MakePromise("d3")).ErrorKey);
        Drive(g, 3, join: true); // E1, then the player forms the government: both carried over; the window before E2 is open
        Assert.True(g.Apply(new MakePromise("d3")).Accepted); // a new cycle: the carried promises do not count against the two
        Assert.Equal("error.already_promised", g.Apply(new MakePromise("d1")).ErrorKey); // still pending until E2
        Assert.Equal(3, Pol(g).Party!.PromiseDetails!.Count);
    }

    [Fact]
    public void PromisesSurviveSaveAndLoadAcrossTheElection()
    {
        var packs = Load(Big, "a", government: new[] { "a" }, interval: Interval, turnsToElection: 2, mutate: GameFixture.FreePolicies);
        var live = New(packs);
        live.Apply(new MakePromise("d1"));
        Drive(live, 3, join: true);
        var loaded = Simulation.Load(packs, SaveData.FromYaml(live.Save().ToYaml()));
        Assert.Equal(Pol(live).Party!.PromiseDetails, Pol(loaded).Party!.PromiseDetails);
    }
}
