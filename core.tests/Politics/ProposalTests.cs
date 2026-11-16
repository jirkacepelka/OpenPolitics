using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>
/// Decision 0014: now and then a government puts a measure to parliament (or, in the player's government, a partner asks for a change), and the
/// player's party supports or opposes it in the start-of-quarter briefing. Unanswered proposals expire at the end of the quarter.
/// </summary>
public class ProposalTests
{
    // a leads; its platform wants tax 31 (it starts at 40), so the AI governor lowers the tax.
    private static Spec[] World(double playerTax) =>
        new[] { new Spec("a", 0.45, Tax: 31), new Spec("b", 0.25, Tax: 31), new Spec("c", 0.30, Tax: playerTax) };

    private static Game Make(string player, string[] government, string leader, double playerTax = 45, int turnsToElection = 40) =>
        New(Load(World(playerTax), player, government: government, leader: leader, turnsToElection: turnsToElection, interval: 40,
            mutate: GameFixture.FreePolicies));

    private static ProposalInfo? Open(Game g) => Pol(g).Proposals?.FirstOrDefault(p => p.Answer == null);

    /// <summary>Ends quarters until a proposal is open (at most twelve).</summary>
    private static ProposalInfo WaitForProposal(Game g)
    {
        for (int i = 0; i < 12; i++)
        {
            if (Open(g) is { } p) return p;
            g.EndTurn();
        }
        throw new Xunit.Sdk.XunitException("no proposal within twelve quarters");
    }

    [Fact]
    public void AnAiGovernmentPutsAMeasureToTheOpposition()
    {
        var g = Make("c", new[] { "a", "b" }, "a");
        var p = WaitForProposal(g);
        Assert.Equal("base:a", p.Proposer);
        Assert.Equal("base:tax", p.PolicyId);
        Assert.Equal(ProposalKind.Measure, p.Kind);
        Assert.True(p.To < p.From);
        Assert.False(p.Pivotal);
        Assert.True(p.Fit < 0); // c wants tax 45: lowering it goes against its programme
    }

    [Fact]
    public void ProposalsComeNowAndThenNotEveryQuarter()
    {
        var g = Make("c", new[] { "a", "b" }, "a");
        var turns = Events<ProposalPut>(Run(g, 24)).Select(e => e.Turn).ToList();
        Assert.InRange(turns.Count, 2, 8);
        for (int i = 1; i < turns.Count; i++) Assert.True(turns[i] - turns[i - 1] >= 3, $"proposals at {turns[i - 1]} and {turns[i]}");
    }

    [Fact]
    public void AnUnansweredMeasurePassesAtTheEndOfTheQuarter()
    {
        var g = Make("c", new[] { "a", "b" }, "a");
        var p = WaitForProposal(g);
        var r = g.EndTurn();
        var passed = Assert.Single(r.PoliticalEvents.OfType<ProposalPassed>());
        Assert.Equal(p.PolicyId, passed.PolicyId);
        Assert.Null(passed.Answer);
        Assert.Contains(g.PendingPolicies, x => x.PolicyId == p.PolicyId && x.Value == p.To);
        Assert.DoesNotContain(Pol(g).Proposals ?? new List<ProposalInfo>(), x => x.Id == p.Id);
    }

    [Fact]
    public void AnsweringIsFreeAndOnlyOnce()
    {
        var g = Make("c", new[] { "a", "b" }, "a");
        var p = WaitForProposal(g);
        double capital = Pol(g).Party!.PoliticalCapital;
        Assert.Equal("error.unknown_proposal", g.Apply(new SupportProposal("nope")).ErrorKey);
        Assert.True(g.Apply(new OpposeProposal(p.Id)).Accepted);
        Assert.Equal("error.already_answered", g.Apply(new SupportProposal(p.Id)).ErrorKey);
        Assert.Equal(capital, Pol(g).Party!.PoliticalCapital);
        Assert.Equal(Stance.Oppose, Pol(g).Proposals!.Single(x => x.Id == p.Id).Answer);
    }

    [Fact]
    public void SupportingWhatContradictsTheProgrammeCostsCredibilityAndOpposingItEarnsTheProposersCoolness()
    {
        var support = Make("c", new[] { "a", "b" }, "a");
        var oppose = Make("c", new[] { "a", "b" }, "a");
        var ps = WaitForProposal(support); var po = WaitForProposal(oppose);
        Assert.Equal(ps.Id, po.Id);
        // credibility starts at the cap: lower it the same way in both games first
        foreach (var g in new[] { support, oppose }) g.Apply(new ShiftPlatform("base:tax", 40));
        double before = Pol(support).Party!.Credibility;
        support.Apply(new SupportProposal(ps.Id));
        oppose.Apply(new OpposeProposal(po.Id));
        Assert.True(Pol(support).Party!.Credibility < before);
        Assert.True(Pol(oppose).Party!.Credibility > before);
        Assert.True(Pol(support).Party!.Relations["base:a"] > Pol(oppose).Party!.Relations["base:a"]);
    }

    [Fact]
    public void SupportingWhatMatchesTheProgrammeEarnsCredibility()
    {
        var g = Make("c", new[] { "a", "b" }, "a", playerTax: 30);
        var p = WaitForProposal(g);
        Assert.True(p.Fit > 0);
        g.Apply(new ShiftPlatform("base:tax", 25)); // below the cap
        double before = Pol(g).Party!.Credibility;
        Assert.True(g.Apply(new SupportProposal(p.Id)).Accepted);
        Assert.True(Pol(g).Party!.Credibility > before);
    }

    [Fact]
    public void APartnerWhoseSeatsArePivotalCanBlockAMeasure()
    {
        var g = Make("c", new[] { "a", "c" }, "a"); // a alone has 45 %: c's seats decide
        var p = WaitForProposal(g);
        Assert.True(p.Pivotal);
        double relation = Pol(g).Party!.Relations["base:a"];
        g.Apply(new OpposeProposal(p.Id));
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<ProposalFailed>());
        Assert.Empty(r.PoliticalEvents.OfType<ProposalPassed>());
        Assert.DoesNotContain(g.PendingPolicies, x => x.PolicyId == p.PolicyId && x.Value == p.To);
        Assert.True(Pol(g).Party!.Relations["base:a"] < relation - 0.1);
    }

    [Fact]
    public void APartnerWhoIsNotPivotalCannotBlockButStrainsTheRelation()
    {
        var g = Make("c", new[] { "a", "b", "c" }, "a"); // a and b have 70 % without c
        var p = WaitForProposal(g);
        Assert.False(p.Pivotal);
        double relation = Pol(g).Party!.Relations["base:a"];
        g.Apply(new OpposeProposal(p.Id));
        Assert.True(Pol(g).Party!.Relations["base:a"] < relation);
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<ProposalPassed>());
    }

    [Fact]
    public void AnAiPartnerAsksTheLeadingPlayerForAChange()
    {
        // the player leads a with partner b; b wants tax 31 (it starts at 40)
        var support = Make("a", new[] { "a", "b" }, "a");
        var oppose = Make("a", new[] { "a", "b" }, "a");
        var p = WaitForProposal(support); WaitForProposal(oppose);
        Assert.Equal(ProposalKind.Request, p.Kind);
        Assert.Equal("base:b", p.Proposer);
        Assert.Equal("base:tax", p.PolicyId);
        support.Apply(new SupportProposal(p.Id));
        oppose.Apply(new OpposeProposal(p.Id));
        var rs = support.EndTurn(); var ro = oppose.EndTurn();
        Assert.Single(rs.PoliticalEvents.OfType<ProposalPassed>());
        Assert.Contains(support.PendingPolicies, x => x.PolicyId == "base:tax" && x.Value == p.To);
        Assert.Single(ro.PoliticalEvents.OfType<ProposalFailed>());
        Assert.True(Pol(support).Loyalty!["base:b"] > Pol(oppose).Loyalty!["base:b"]);
    }

    [Fact]
    public void AnswersSurviveSaveAndReplay()
    {
        var packs = Load(World(45), "c", government: new[] { "a", "b" }, leader: "a", turnsToElection: 40, interval: 40, mutate: GameFixture.FreePolicies);
        var live = New(packs);
        var p = WaitForProposal(live);
        live.Apply(new SupportProposal(p.Id));
        live.EndTurn();
        var q = WaitForProposal(live);
        live.Apply(new OpposeProposal(q.Id));
        var loaded = Simulation.Load(packs, SaveData.FromYaml(live.Save().ToYaml()));
        Assert.Equal(Pol(live).Party!.Credibility, Pol(loaded).Party!.Credibility);
        Assert.Equal(Pol(live).Party!.Relations, Pol(loaded).Party!.Relations);
        Assert.Equal(Pol(live).Proposals, Pol(loaded).Proposals);
    }
}
