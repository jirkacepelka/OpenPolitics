using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>The leading party reshuffles its coalition: drops partners, brings in others (decision 0013).</summary>
public class ReshuffleTests
{
    // a (player) leads b; c asks a price (its demand d1 on spend has weight 3), d is free, z has no seats.
    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.40, Tax: 30), new Spec("b", 0.15, Tax: 32), new Spec("c", 0.20, Price: 3, Tax: 34), new Spec("d", 0.20, Tax: 36), new Spec("z", 0.03),
        new Spec("e", 0.02),
    };

    private static Game Make(string player = "a", int turnsToElection = 60, string[]? government = null)
    {
        var g = New(Load(Parties(), player, government: government ?? new[] { "a", "b" }, leader: "a", turnsToElection: turnsToElection, interval: 60), 1);
        g.EndTurn(); // a quarter of income, so a swap is affordable
        return g;
    }

    private static string[] P(params string[] ids) => ids.Select(i => "base:" + i).ToArray();
    private static AgreedDemand Ag(string party, string demand) => new("base:" + party, demand);
    private static ReshuffleCoalition Swap(string[] drop, string[] add, params AgreedDemand[] agreements) => new(P(drop), P(add), agreements);
    private static double Capital(Game g) => Pol(g).Party!.PoliticalCapital;
    private static double Income(Game g) => Pol(g).Party!.Income;

    [Fact]
    public void TheCostGrowsWithEveryPartyChanged()
    {
        Assert.Equal(PoliticsLimits.ReshuffleBaseCost + 2 * PoliticsLimits.ReshufflePerParty, Make().PreviewReshuffle(P("b"), P("c"), new[] { Ag("c", "d1") }).Cost);
        Assert.Equal(PoliticsLimits.ReshuffleBaseCost + PoliticsLimits.ReshufflePerParty, Make().PreviewReshuffle(Array.Empty<string>(), P("d"), Array.Empty<AgreedDemand>()).Cost);
    }

    [Fact]
    public void ASwapIsPaidWhenAcceptedAndTakesEffectAtTheEndOfTheQuarter()
    {
        var g = Make();
        double before = Capital(g);
        Assert.True(g.Apply(Swap(new[] { "b" }, new[] { "c" }, Ag("c", "d1"))).Accepted);
        Assert.Equal(before - 7, Capital(g), 9);
        Assert.Equal(P("a", "b"), Pol(g).GovernmentParties);
        var r = g.EndTurn();
        var e = Assert.Single(r.PoliticalEvents.OfType<CoalitionReshuffled>());
        Assert.Equal("base:a", e.Leader);
        Assert.Equal(P("b"), e.Dropped);
        Assert.Equal(P("c"), e.Added);
        Assert.Equal(P("a", "c"), Pol(g).GovernmentParties);
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        var c = Assert.Single(Pol(g).Government!.Commitments, x => x.PartyId == "base:c");
        Assert.Equal("d1", c.Demand.Id);
        Assert.Equal(r.Turn + c.Demand.DueTurns, c.DueTurn); // a fresh deadline from today
        Assert.DoesNotContain(Pol(g).Government!.Commitments, x => x.PartyId == "base:b");
        Assert.True(Pol(g).Loyalty!.ContainsKey("base:c"));
        Assert.False(Pol(g).Loyalty!.ContainsKey("base:b"));
        Assert.Equal(0.0, CommitmentMath.Expected(c, r.Turn, Pol(g).Government!.FormedTurn)); // expected from the reshuffle, not from formation
    }

    [Fact]
    public void TheDroppedPartyResentsTheLeaderAndRefusesToCooperateForAWhile()
    {
        var g = Make();
        double relation = Pol(g).Party!.Relations["base:b"];
        g.Apply(Swap(new[] { "b" }, new[] { "c" }, Ag("c", "d1")));
        g.EndTurn();
        Assert.True(Pol(g).Grudge!["base:b|base:a"] >= CoalitionRules.GrudgeVeto);
        Assert.True(Pol(g).Party!.Relations["base:b"] < relation - 0.1);
        // a few quarters later b still refuses to sit with a
        for (int i = 0; i < 3; i++) g.EndTurn();
        Assert.True(Pol(g).Grudge!["base:b|base:a"] >= CoalitionRules.GrudgeVeto);
    }

    [Fact]
    public void ARefusedAttemptRefundsAllButASmallFee()
    {
        var g = Make();
        double before = Capital(g);
        Assert.True(g.Apply(Swap(new[] { "b" }, new[] { "c" })).Accepted); // c's price is not paid
        var r = g.EndTurn();
        var e = Assert.Single(r.PoliticalEvents.OfType<ReshuffleRefused>());
        Assert.Equal(("price", "base:c"), (e.Reason, e.PartyId));
        Assert.Empty(r.PoliticalEvents.OfType<CoalitionReshuffled>());
        Assert.Equal(P("a", "b"), Pol(g).GovernmentParties);
        Assert.Equal(before - PoliticsLimits.ReshuffleRefusalCost + Income(g), Capital(g), 6);
    }

    [Fact]
    public void AddingAPartnerOrDroppingOneWhileKeepingAMajorityWorks()
    {
        var g = Make();
        Assert.True(g.Apply(Swap(Array.Empty<string>(), new[] { "d" })).Accepted);
        g.EndTurn();
        Assert.Equal(P("a", "b", "d"), Pol(g).GovernmentParties);
        for (int i = 0; i < PoliticsLimits.ReshuffleCooldownTurns; i++) g.EndTurn();
        Assert.True(g.Apply(Swap(new[] { "b" }, Array.Empty<string>())).Accepted);
        g.EndTurn();
        Assert.Equal(P("a", "d"), Pol(g).GovernmentParties);
    }

    [Theory]
    [InlineData(new string[0], new string[0], "error.no_change")]
    [InlineData(new[] { "c" }, new string[0], "error.unknown_party")]       // c is not in the government
    [InlineData(new[] { "a" }, new string[0], "error.unknown_party")]       // the leader cannot drop itself
    [InlineData(new string[0], new[] { "b" }, "error.unknown_party")]       // b already governs
    [InlineData(new string[0], new[] { "z" }, "error.unknown_party")]       // z has no seats
    [InlineData(new string[0], new[] { "nobody" }, "error.unknown_party")]
    [InlineData(new[] { "b" }, new string[0], "error.would_lose_majority")]
    public void InvalidReshufflesAreRefusedForFree(string[] drop, string[] add, string error)
    {
        var g = Make();
        double before = Capital(g);
        var r = g.Apply(Swap(drop, add));
        Assert.Equal(error, r.ErrorKey);
        Assert.Equal(before, Capital(g));
        Assert.Equal("error.would_lose_majority", g.PreviewReshuffle(P("b"), Array.Empty<string>(), Array.Empty<AgreedDemand>()).ErrorKey);
    }

    [Fact]
    public void OnlyTheLeaderReshufflesOnlyOnceAQuarterAndOnlyWithEnoughCapital()
    {
        Assert.Equal("error.not_leader", Make("b").Apply(Swap(new string[0], new[] { "d" })).ErrorKey);
        var g = Make();
        Assert.True(g.Apply(Swap(new string[0], new[] { "d" })).Accepted);
        Assert.Equal("error.already_queued", g.Apply(Swap(new[] { "b" }, new[] { "c" }, Ag("c", "d1"))).ErrorKey);
        var poor = New(Load(Parties(), "a", government: new[] { "a", "b" }, leader: "a", turnsToElection: 60, interval: 60), 1);
        Assert.True(poor.Apply(new BuildOrganization()).Accepted); // 6 - 4 = 2 left
        Assert.Equal("error.not_enough_capital", poor.Apply(Swap(new string[0], new[] { "d" })).ErrorKey);
    }

    [Fact]
    public void AfterAReshuffleTheNextOneWaitsForTheCooldown()
    {
        var g = Make();
        Assert.True(g.Apply(Swap(new string[0], new[] { "d" })).Accepted);
        g.EndTurn();
        Assert.Equal(PoliticsLimits.ReshuffleCooldownTurns, Pol(g).Reshuffle!.CooldownLeft);
        var r = g.Apply(Swap(new[] { "b" }, new string[0]));
        Assert.Equal("error.reshuffle_cooldown", r.ErrorKey);
        Assert.Equal(PoliticsLimits.ReshuffleCooldownTurns.ToString(), r.Detail);
        for (int i = 0; i < PoliticsLimits.ReshuffleCooldownTurns; i++) g.EndTurn();
        Assert.Equal(0, Pol(g).Reshuffle!.CooldownLeft);
        Assert.True(g.Apply(Swap(new[] { "b" }, new string[0])).Accepted);
    }

    [Fact]
    public void ARefusedAttemptHasAShortCooldown()
    {
        var g = Make();
        g.Apply(Swap(new[] { "b" }, new[] { "c" }));
        g.EndTurn();
        Assert.Equal(PoliticsLimits.ReshuffleRefusedCooldownTurns, Pol(g).Reshuffle!.CooldownLeft);
    }

    [Fact]
    public void AnElectionBeforeTheReshuffleCancelsItWithAFullRefund()
    {
        var g = Make(turnsToElection: 2); // the election is at the end of the next quarter
        double before = Capital(g);
        Assert.True(g.Apply(Swap(new string[0], new[] { "d" })).Accepted);
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<ElectionHeld>());
        Assert.Empty(r.PoliticalEvents.OfType<CoalitionReshuffled>());
        Assert.Equal(Math.Min(PoliticsLimits.MaxCapital, before + Income(g)), Capital(g), 6);
    }

    [Fact]
    public void PreviewShowsMajorityCostAndWhoWouldRefuse()
    {
        var g = Make();
        var none = g.PreviewReshuffle(P("b"), P("c"), Array.Empty<AgreedDemand>());
        Assert.Null(none.ErrorKey);
        Assert.True(none.Majority);
        Assert.True(none.SeatsHeld > none.SeatsNeeded);
        Assert.Contains(none.Refusals, x => x.Reason == "price" && x.PartyId == "base:c");
        var suggested = g.SuggestReshuffle(P("b"), P("c"));
        Assert.NotNull(suggested);
        Assert.Contains(Ag("c", "d1"), suggested!);
        Assert.Empty(g.PreviewReshuffle(P("b"), P("c"), suggested).Refusals);
        Assert.Equal(PoliticsLimits.ReshuffleBaseCost + 2 * PoliticsLimits.ReshufflePerParty, none.Cost);
        Assert.False(g.PreviewReshuffle(P("b"), Array.Empty<string>(), Array.Empty<AgreedDemand>()).Majority);
    }

    [Fact]
    public void AReshuffleSurvivesSaveAndLoad()
    {
        var packs = Load(Parties(), "a", government: new[] { "a", "b" }, leader: "a", turnsToElection: 60, interval: 60);
        var live = New(packs, 1);
        live.EndTurn();
        Assert.True(live.Apply(Swap(new[] { "b" }, new[] { "c" }, Ag("c", "d1"))).Accepted);
        live.EndTurn();
        var yaml = live.Save().ToYaml();
        Assert.Contains("reshuffle_coalition", yaml);
        var loaded = Simulation.Load(packs, SaveData.FromYaml(yaml));
        Assert.Equal(Pol(live).GovernmentParties, Pol(loaded).GovernmentParties);
        Assert.Equal(Capital(live), Capital(loaded), 9);
        Assert.Equal(Pol(live).Reshuffle, Pol(loaded).Reshuffle);
    }

    [Fact]
    public void ReshuffleRulesReuseTheFormationRulesForTheNewcomers()
    {
        var parties = Load(Parties(), "a").Parties.ToDictionary(p => p.Id);
        var seats = new Dictionary<string, int> { ["base:a"] = 84, ["base:b"] = 32, ["base:c"] = 42, ["base:d"] = 42 };
        var sitting = new SittingCoalition(P("b"), Array.Empty<AgreedDemand>());
        // b's price is not asked again; the newcomer's is
        Assert.Empty(CoalitionRules.Evaluate(new CoalitionProposal("base:a", P("d"), Array.Empty<AgreedDemand>()), parties, seats, 0.5, 6, (_, _) => 0.3, null, sitting));
        Assert.Contains(CoalitionRules.Evaluate(new CoalitionProposal("base:a", P("c"), Array.Empty<AgreedDemand>()), parties, seats, 0.5, 6, (_, _) => 0.3, null, sitting),
            x => x.Reason == "price" && x.PartyId == "base:c");
        // a grudge between a newcomer and a sitting partner vetoes; an old grudge between sitting members does not
        double Grudge(string x, string y) => (x, y) is ("base:d", "base:b") or ("base:b", "base:a") ? 1.0 : 0.0;
        Assert.Contains(CoalitionRules.Evaluate(new CoalitionProposal("base:a", P("d"), Array.Empty<AgreedDemand>()), parties, seats, 0.5, 6, (_, _) => 0.3, Grudge, sitting),
            x => x.Reason == "grudge" && x.PartyId == "base:d");
        Assert.DoesNotContain(CoalitionRules.Evaluate(new CoalitionProposal("base:a", P("d"), Array.Empty<AgreedDemand>()), parties, seats, 0.5, 6, (_, _) => 0.3, Grudge, sitting),
            x => x.PartyId == "base:b");
    }

    [Fact]
    public void AnAiLeaderReplacesAPartnerThatWalksOutWhenItCan()
    {
        // b (AI) leads a and c. c's red line is crossed from the start and its voters are miserable, so it walks out after an ultimatum;
        // b + a alone have no majority, d can step in.
        const string redLine = "  red_lines:\n    - {policy: base:tax, op: \">=\", threshold: 35}\n";
        var parties = new[]
        {
            new Spec("a", 0.20, Tax: 40), new Spec("b", 0.30, Tax: 40), new Spec("c", 0.15, Tax: 40), new Spec("d", 0.20, Tax: 40), new Spec("z", 0.15),
        };
        static void RedLineForC(Dictionary<string, string> f)
        {
            var blocks = f["parties/p.yaml"].Split("- id: ").Where(b => b.Length > 0)
                .Select(b => b.StartsWith("base:c\n", StringComparison.Ordinal) ? b + redLine : b);
            f["parties/p.yaml"] = string.Concat(blocks.Select(b => "- id: " + b));
        }
        var g = New(Load(parties, "z", government: new[] { "a", "b", "c" }, leader: "b", turnsToElection: 60, interval: 60, grumpy: true, mutate: RedLineForC), 1);
        var reports = Run(g, 40);
        var events = reports.SelectMany(r => r.PoliticalEvents).ToList();
        Assert.Contains(events.OfType<PartnerLeft>(), e => e.PartyId == "base:c");
        var patch = Assert.Single(events.OfType<CoalitionReshuffled>(), e => e.Leader == "base:b");
        Assert.Contains("base:d", patch.Added);
        Assert.DoesNotContain("base:z", patch.Added); // never the player's party
        Assert.DoesNotContain(events.OfType<GovernmentFell>(), e => e.Turn <= patch.Turn);
    }
}
