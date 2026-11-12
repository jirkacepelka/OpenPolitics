using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Defects found by the final M2 review; each test failed before its fix.</summary>
public class ReviewFixTests
{
    [Fact]
    public void CaretakerShowsTheRealCountdownToTheEarlyElection()
    {
        var spec = new[] { new Spec("a", 0.40, 100), new Spec("b", 0.35, 100), new Spec("c", 0.25, 100) };
        var g = New(Load(spec, "a", interval: 40));
        for (int i = 0; i < 20 && Pol(g).Phase != Phase.Caretaker; i++) g.EndTurn();
        Assert.Equal(Phase.Caretaker, Pol(g).Phase);
        int shown = Pol(g).TurnsToElection;
        int turns = 0;
        while (true) { turns++; if (g.EndTurn().PoliticalEvents.OfType<ElectionHeld>().Any()) break; Assert.True(turns < 10); }
        Assert.Equal(turns, shown);
    }

    [Fact]
    public void CampaignDoublesBeforeAnEarlyElection()
    {
        var spec = new[] { new Spec("a", 0.40, 100), new Spec("b", 0.35, 100), new Spec("c", 0.25, 100) };
        var g = New(Load(spec, "a", interval: 40));
        for (int i = 0; i < 20 && Pol(g).Phase != Phase.Caretaker; i++) g.EndTurn();
        Assert.True(g.Apply(new Campaign("base:workers", 1)).Accepted);
        Assert.Equal(0.12, Pol(g).Party!.Momentum["base:workers"], 12);
    }

    [Fact]
    public void RepeatingAnAgreementDoesNotBuyAPartner()
    {
        var spec = new[] { new Spec("a", 0.42), new Spec("b", 0.33, Price: 2.5), new Spec("c", 0.22, Price: 3), new Spec("z", 0.03) };
        var g = New(Load(spec, "a", interval: 60));
        g.EndTurn();
        var once = new ProposeCoalition(new[] { "base:b" }, new[] { new AgreedDemand("base:b", "d3") });
        Assert.Equal("error.refused.price", g.Apply(once).ErrorKey);
        var thrice = new ProposeCoalition(new[] { "base:b" }, Enumerable.Repeat(new AgreedDemand("base:b", "d3"), 3).ToList());
        Assert.Equal("error.refused.price", g.Apply(thrice).ErrorKey);
    }

    private static readonly Dictionary<string, PolicyDef> Policies = new()
    {
        ["t:spend"] = new("t:spend", "percent", 0, 80, 40, null, 0, 0, null, "x"),
        ["t:tax"] = new("t:tax", "percent", 0, 60, 40, null, 0, 0, null, "x"),
    };

    [Fact]
    public void UnreachableCommitmentDoesNotFreezeTheGovernor()
    {
        var impossible = new Commitment("p", new DemandDef("huge", "policy", "t:spend", ">=", 90, 8, 8), 40, 8);
        var possible = new Commitment("p", new DemandDef("ok", "policy", "t:tax", ">=", 50, 1, 8), 40, 8);
        var g = new Government("l", new[] { "l", "p" }, new[] { impossible, possible }, 0);
        var move = Governor.Move(g, id => id == "t:spend" ? 80 : 44, _ => 0, Policies, 4);
        Assert.NotNull(move);
        Assert.Equal("t:tax", move!.Value.PolicyId);
    }

    [Fact]
    public void NegativeVoterGroupSizeIsAValidationError()
    {
        var r = Packs.TestPacks.Load(f => f["voter_groups/g.yaml"] = "- id: t:g\n  size: -0.8\n  weight: 1\n");
        Assert.Contains(r.Diagnostics, d => d.Code == "E007" && d.Message.Contains("size"));
    }
}
