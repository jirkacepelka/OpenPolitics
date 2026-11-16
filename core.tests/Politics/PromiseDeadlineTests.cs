using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>
/// Decision 0014: a promise holds until the next election and is judged once there; a broken promise only costs a party that was in
/// government for at least half of the quarters from the promise to the election.
/// </summary>
public class PromiseDeadlineTests
{
    // b and c hold a majority without a; a can join them as a partner.
    private static readonly Spec[] Three = { new("a", 0.30, Tax: 30), new("b", 0.45, Tax: 31), new("c", 0.25, Tax: 32) };

    private static Game Make(string[] government, string leader, int turnsToElection = 6) =>
        New(Load(Three, "a", government: government, leader: leader, turnsToElection: turnsToElection, mutate: GameFixture.FreePolicies));

    [Fact]
    public void APromiseStaysOpenUntilTheElectionAndIsJudgedOnlyThere()
    {
        var g = Make(new[] { "a" }, "a", turnsToElection: 6);
        Assert.True(g.Apply(new MakePromise("d1")).Accepted);
        for (int i = 0; i < 5; i++)
        {
            var r = g.EndTurn();
            Assert.Empty(r.PoliticalEvents.OfType<PromiseBroken>());
            Assert.Empty(r.PoliticalEvents.OfType<PromiseKept>());
            Assert.Equal(new[] { "d1" }, Pol(g).Party!.Promises);
        }
        var election = g.EndTurn();
        Assert.Single(election.PoliticalEvents.OfType<ElectionHeld>());
        Assert.Single(election.PoliticalEvents.OfType<PromiseBroken>());
        Assert.Empty(Pol(g).Party!.Promises!);
    }

    [Fact]
    public void TheSnapshotTellsWhenAPromiseWasMadeAndHowLongThePartyGoverned()
    {
        var g = Make(new[] { "a" }, "a", turnsToElection: 6);
        g.Apply(new MakePromise("d1"));
        Run(g, 2);
        var p = Assert.Single(Pol(g).Party!.PromiseDetails!);
        Assert.Equal("d1", p.DemandId);
        Assert.Equal(1, p.MadeTurn);
        Assert.Equal(2, p.QuartersElapsed);
        Assert.Equal(2, p.QuartersInGovernment);
        Assert.False(p.Met);
        Assert.Equal(4, Pol(g).TurnsToElection);
    }

    [Fact]
    public void ABrokenPromiseCostsNothingInOpposition()
    {
        var none = Make(new[] { "b", "c" }, "b");
        var broken = Make(new[] { "b", "c" }, "b");
        Assert.True(broken.Apply(new MakePromise("d1")).Accepted);
        var reports = Run(broken, 6);
        Run(none, 6);
        var e = Assert.Single(Events<PromiseBroken>(reports));
        Assert.False(e.Responsible);
        Assert.Equal(Pol(none).Party!.Credibility, Pol(broken).Party!.Credibility, 12);
    }

    [Fact]
    public void ABrokenPromiseCostsAGoverningPartner()
    {
        var none = Make(new[] { "a", "b" }, "b");
        var broken = Make(new[] { "a", "b" }, "b");
        Assert.Equal(Role.Partner, Pol(broken).PlayerRole);
        Assert.True(broken.Apply(new MakePromise("d1")).Accepted);
        var reports = Run(broken, 6);
        Run(none, 6);
        var e = Assert.Single(Events<PromiseBroken>(reports));
        Assert.True(e.Responsible);
        Assert.True(Pol(broken).Party!.Credibility < Pol(none).Party!.Credibility - 0.1);
    }

    [Fact]
    public void APartnerWhoLeftEarlyIsNotHeldToItsPromise()
    {
        var none = Make(new[] { "a", "b", "c" }, "b");
        var broken = Make(new[] { "a", "b", "c" }, "b");
        foreach (var g in new[] { none, broken }) Assert.True(g.Apply(new LeaveCoalition()).Accepted);
        Assert.True(broken.Apply(new MakePromise("d1")).Accepted);
        var reports = Run(broken, 6);
        Run(none, 6);
        var e = Assert.Single(Events<PromiseBroken>(reports));
        Assert.False(e.Responsible); // one quarter in government out of six
        Assert.Equal(Pol(none).Party!.Credibility, Pol(broken).Party!.Credibility, 12);
    }

    [Fact]
    public void AKeptPromiseInOppositionEarnsNoCredit()
    {
        // the government owes c "spending >= 41", so it delivers what the opposition party a promised as well
        var packs = Load(Three, "a", government: new[] { "b", "c" }, leader: "b", turnsToElection: 4,
            agreements: "      - {party: base:c, demand: d1}\n", mutate: GameFixture.FreePolicies);
        var none = New(packs);
        var kept = New(packs);
        Assert.True(kept.Apply(new MakePromise("d1")).Accepted);
        var reports = Run(kept, 4);
        Run(none, 4);
        var e = Assert.Single(Events<PromiseKept>(reports));
        Assert.False(e.Responsible);
        Assert.Equal(Pol(none).Party!.Credibility, Pol(kept).Party!.Credibility, 12);
    }
}
