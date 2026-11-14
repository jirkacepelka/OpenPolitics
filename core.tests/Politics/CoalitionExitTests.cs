using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class CoalitionExitTests
{
    private static Spec[] Parties() => new[]
    {
        new Spec("a", 0.42, Tax: 30), new Spec("b", 0.33, Tax: 31, Price: 3), new Spec("c", 0.22, Tax: 45), new Spec("z", 0.03),
    };

    private static Game Make(string player, string[] gov, string? agreements = null, ulong seed = 1) =>
        New(Load(Parties(), player, government: gov, leader: "a", turnsToElection: 60, interval: 60, agreements: agreements), seed);

    private static double Capital(Game g) => Pol(g).Party!.PoliticalCapital;

    [Fact]
    public void ActionsAreRefusedInTheWrongRole()
    {
        var abc = new[] { "a", "b", "c" };
        Assert.Equal("error.not_partner", Make("a", abc).Apply(new LeaveCoalition()).ErrorKey);
        Assert.Equal("error.not_partner", Make("z", abc).Apply(new LeaveCoalition()).ErrorKey);
        Assert.Equal("error.not_leader", Make("b", abc).Apply(new DismissPartner("base:c")).ErrorKey);
        Assert.Equal("error.not_opposition", Make("a", abc).Apply(new NoConfidence()).ErrorKey);
        Assert.Equal("error.not_opposition", Make("b", abc).Apply(new NoConfidence()).ErrorKey);
        Assert.Equal("error.unknown_party", Make("a", abc).Apply(new DismissPartner("base:z")).ErrorKey);
        Assert.Equal("error.unknown_party", Make("a", abc).Apply(new DismissPartner("base:a")).ErrorKey);
    }

    [Fact]
    public void ActionsNeedAGoverningGovernment()
    {
        var g = New(Load(Parties(), "a", interval: 60)); // formation phase after the first election
        g.EndTurn();
        Assert.Equal(Phase.Formation, Pol(g).Phase);
        Assert.Equal("error.wrong_phase", g.Apply(new DismissPartner("base:b")).ErrorKey);
        Assert.Equal("error.wrong_phase", g.Apply(new LeaveCoalition()).ErrorKey);
        Assert.Equal("error.wrong_phase", g.Apply(new NoConfidence()).ErrorKey);
    }

    [Fact]
    public void LeavingKeepsTheGovernmentWhenTheMajorityHolds()
    {
        var g = Make("c", new[] { "a", "b", "c" });
        Assert.True(g.Apply(new LeaveCoalition()).Accepted);
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<PlayerLeftCoalition>());
        Assert.Empty(r.PoliticalEvents.OfType<GovernmentFell>());
        var p = Pol(g);
        Assert.Equal(new[] { "base:a", "base:b" }, p.GovernmentParties);
        Assert.Equal(Role.Opposition, p.PlayerRole);
        Assert.Equal(0.85, p.Party!.Credibility, 9);
        Assert.Equal(0.0, p.Party!.Relations["base:a"], 9);
        Assert.True(p.Grudge!["base:c|base:a"] >= 0.5);
    }

    [Fact]
    public void LeavingWithoutAMajorityLeftCausesOneFall()
    {
        var g = Make("b", new[] { "a", "b" });
        Assert.True(g.Apply(new LeaveCoalition()).Accepted);
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<GovernmentFell>());
        Assert.Equal("base:a", Assert.Single(r.PoliticalEvents.OfType<FormationStarted>()).Formateur);
        Assert.Equal(Phase.Formation, Pol(g).Phase);
    }

    [Fact]
    public void DismissingAPartnerNeedsTheMajorityToSurvive()
    {
        Assert.Equal("error.would_lose_majority", Make("a", new[] { "a", "b" }).Apply(new DismissPartner("base:b")).ErrorKey);
        var g = Make("a", new[] { "a", "b", "c" });
        double before = Capital(g);
        Assert.True(g.Apply(new DismissPartner("base:c")).Accepted);
        Assert.Equal(before - 4, Capital(g), 9);
        var r = g.EndTurn();
        var e = Assert.Single(r.PoliticalEvents.OfType<PartnerDismissed>());
        Assert.Equal("base:c", e.PartyId);
        Assert.Empty(r.PoliticalEvents.OfType<GovernmentFell>());
        Assert.Equal(new[] { "base:a", "base:b" }, Pol(g).GovernmentParties);
        Assert.True(Pol(g).Grudge!["base:c|base:a"] >= 1.0);
    }

    [Fact]
    public void NoConfidenceNeedsTenCapitalAndFailsWithoutSupport()
    {
        var g = Make("c", new[] { "a", "b" });
        Assert.Equal("error.not_enough_capital", g.Apply(new NoConfidence()).ErrorKey);
        for (int i = 0; i < 4 && Capital(g) < 10; i++) g.EndTurn();
        Assert.True(Capital(g) >= 10, $"capital {Capital(g)}");
        double before = Capital(g);
        Assert.True(g.Apply(new NoConfidence()).Accepted);
        Assert.Equal(before - 10, Capital(g), 9);
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<NoConfidenceFailed>());
        Assert.True(Pol(g).Party!.Credibility <= 0.8 + 0.03);
        Assert.Equal(Phase.Governing, Pol(g).Phase);
    }

    [Fact]
    public void NoConfidencePassesWhenWillingOppositionPartiesHaveAMajority()
    {
        // a governs alone with 42 % of the seats; the player's c wins b over (relation 0.5) and together they have 55 %
        var g = Make("c", new[] { "a" });
        for (int i = 0; i < 8; i++) g.EndTurn();
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        Assert.True(g.Apply(new Court("base:b")).Accepted);
        Assert.True(g.Apply(new Court("base:b")).Accepted);
        Assert.True(Capital(g) >= 10, $"capital {Capital(g)}");
        Assert.True(g.Apply(new NoConfidence()).Accepted);
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<NoConfidencePassed>());
        Assert.Single(r.PoliticalEvents.OfType<GovernmentFell>());
        Assert.Equal(Phase.Formation, Pol(g).Phase);
        Assert.Equal("base:b", Pol(g).Formation!.Formateur); // the largest willing opposition party tries first
    }

    [Fact]
    public void ExitActionsSurviveSaveAndReplay()
    {
        var packs = Load(Parties(), "a", government: new[] { "a", "b", "c" }, leader: "a", turnsToElection: 60, interval: 60);
        var live = New(packs, 2);
        live.Apply(new DismissPartner("base:c"));
        for (int i = 0; i < 3; i++) live.EndTurn();
        var back = SaveData.FromYaml(live.Save().ToYaml());
        var loaded = Simulation.Load(packs, back);
        Assert.Equal(Pol(live).GovernmentParties, Pol(loaded).GovernmentParties);
        Assert.Equal(Pol(live).Grudge, Pol(loaded).Grudge);
    }
}
