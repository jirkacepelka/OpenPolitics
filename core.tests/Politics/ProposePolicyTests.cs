using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class ProposePolicyTests
{
    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Tax: 30), new Spec("b", 0.33, Tax: 31), new Spec("c", 0.22, Tax: 45), new Spec("z", 0.03),
    };

    /// <summary>a leads with b and c; the player is the partner b (or another role). The leader's tax target is 30, the policy sits at 40.</summary>
    private static Game Partner(string player = "b", string? redLines = null, string[]? gov = null, ulong seed = 1) =>
        New(Load(Parties(), player, government: gov ?? new[] { "a", "b", "c" }, leader: "a", turnsToElection: 60, interval: 60, redLines: redLines), seed);

    private static double Tax(Game g) => g.State.Countries.Values.Single().Policies["base:tax"];
    private static double Capital(Game g) => Pol(g).Party!.PoliticalCapital;

    [Fact]
    public void TheLeaderUsesThePolicyDialogAndEveryoneElseMayPropose()
    {
        Assert.Equal("error.not_for_leader", Partner("a").Apply(new ProposePolicy("base:tax", 30)).ErrorKey);
        Assert.True(Partner("z").Apply(new ProposePolicy("base:tax", 30)).Accepted); // decision 0015: the opposition proposes publicly
        var g = Partner();
        Assert.Equal("error.unknown_policy", g.Apply(new ProposePolicy("base:nope", 30)).ErrorKey);
        Assert.Equal("error.value_out_of_range", g.Apply(new ProposePolicy("base:tax", 999)).ErrorKey);
    }

    [Fact]
    public void ProposingCostsThreeCapital()
    {
        var g = Partner();
        double before = Capital(g);
        Assert.True(g.Apply(new ProposePolicy("base:tax", 31)).Accepted);
        Assert.Equal(before - 3, Capital(g), 9);
    }

    [Fact]
    public void ProposalNearTheLeadersPositionIsAccepted()
    {
        var g = Partner();
        Assert.True(g.Apply(new ProposePolicy("base:tax", 31)).Accepted);
        var r = g.EndTurn();
        var e = Assert.Single(r.PoliticalEvents.OfType<PolicyProposalAccepted>());
        Assert.Equal(("base:tax", 31.0), (e.PolicyId, e.Target));
        Assert.Contains(r.PoliticalEvents.OfType<GovernmentMeasure>(), m => m.PolicyId == "base:tax" && m.To == 31.0);
        g.EndTurn();
        Assert.Equal(31.0, Tax(g));
    }

    [Fact]
    public void AcceptedMoveIsCappedAtAQuarterOfTheRange()
    {
        var g = Partner();
        Assert.True(g.Apply(new ProposePolicy("base:tax", 30)).Accepted); // 40 -> 30 is within 15
        g.EndTurn(); g.EndTurn();
        Assert.Equal(30.0, Tax(g));
    }

    [Fact]
    public void ProposalFarFromTheLeaderIsRejectedForScore()
    {
        var g = Partner();
        Assert.True(g.Apply(new ProposePolicy("base:tax", 60)).Accepted);
        var r = g.EndTurn();
        Assert.Equal("score", Assert.Single(r.PoliticalEvents.OfType<PolicyProposalRejected>()).Reason);
        g.EndTurn();
        Assert.Equal(40.0, Tax(g));
    }

    [Fact]
    public void GoodRelationsMakeADistantProposalPass()
    {
        // a proposal of 48 is 18 away from the leader's 30: refused at default relations, accepted after courting the leader
        var cold = Partner();
        Assert.True(cold.Apply(new ProposePolicy("base:tax", 48)).Accepted);
        Assert.Single(cold.EndTurn().PoliticalEvents.OfType<PolicyProposalRejected>());

        var warm = Partner();
        Assert.True(warm.Apply(new Court("base:a")).Accepted);
        Assert.True(Pol(warm).Party!.Relations["base:a"] >= 0.4);
        Assert.True(warm.Apply(new ProposePolicy("base:tax", 48)).Accepted);
        Assert.Single(warm.EndTurn().PoliticalEvents.OfType<PolicyProposalAccepted>());
    }

    [Fact]
    public void CrossingAnotherPartnersRedLineIsRejected()
    {
        // every party refuses tax <= 30; the move 40 -> 25 would cross it
        var g = Partner(redLines: "  red_lines:\n    - {policy: base:tax, op: \"<=\", threshold: 30}\n");
        Assert.True(g.Apply(new ProposePolicy("base:tax", 25)).Accepted);
        var r = g.EndTurn();
        Assert.Equal("red_line", Assert.Single(r.PoliticalEvents.OfType<PolicyProposalRejected>()).Reason);
    }

    [Fact]
    public void RelationMovesWithTheAnswer()
    {
        var yes = Partner();
        yes.Apply(new ProposePolicy("base:tax", 31)); yes.EndTurn();
        Assert.Equal(0.35, Pol(yes).Party!.Relations["base:a"], 9);
        var no = Partner();
        no.Apply(new ProposePolicy("base:tax", 60)); no.EndTurn();
        Assert.Equal(0.27, Pol(no).Party!.Relations["base:a"], 9);
    }

    [Fact]
    public void ProposalsSurviveSaveAndReplay()
    {
        var packs = Load(Parties(), "b", government: new[] { "a", "b", "c" }, leader: "a", turnsToElection: 60, interval: 60);
        var live = New(packs, 4);
        live.Apply(new ProposePolicy("base:tax", 31));
        for (int i = 0; i < 4; i++) live.EndTurn();
        var back = SaveData.FromYaml(live.Save().ToYaml());
        var loaded = OpenPolitics.Core.Gameplay.Simulation.Load(packs, back);
        Assert.Equal(Tax(live), Tax(loaded));
        Assert.Equal(Pol(live).Party!.Relations["base:a"], Pol(loaded).Party!.Relations["base:a"]);
    }

    [Fact]
    public void TheRedLineCheckSeesChangesStillWaitingToTakeEffect()
    {
        // base:slow (delay 2, range 0..10, default 1); everybody wants 9, nobody accepts slow >= 6
        var g = New(Load(Parties(), "b", government: new[] { "a", "b", "c" }, leader: "a", turnsToElection: 60, interval: 60,
            redLines: "  red_lines:\n    - {policy: base:slow, op: \">=\", threshold: 6}\n", slowTarget: 9), 1);
        Assert.True(g.Apply(new ProposePolicy("base:slow", 9)).Accepted);
        Assert.Single(g.EndTurn().PoliticalEvents.OfType<PolicyProposalAccepted>()); // 1 -> 3.5, queued for two turns
        Assert.True(g.Apply(new ProposePolicy("base:slow", 9)).Accepted);
        // judged from the queued 3.5, the next step would reach 6 and cross the line, although 1 is still in effect
        var second = g.EndTurn();
        Assert.Equal("red_line", Assert.Single(second.PoliticalEvents.OfType<PolicyProposalRejected>()).Reason);
    }
}
