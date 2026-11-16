using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Presentation;

/// <summary>Decision 0015 (4): every decision the game waits for is listed once, and only for the role that has to make it.</summary>
public class DecisionsTests
{
    private static readonly Spec[] Three = { new("a", 0.30, Tax: 30), new("b", 0.45, Tax: 31), new("c", 0.25, Tax: 45) };

    private Core.Packs.PackSet _packs = null!;

    private Game Make(string player, string[] government, string leader, int turnsToElection = 30)
    {
        _packs = Load(Three, player, government: government, leader: leader, turnsToElection: turnsToElection, interval: 30, mutate: GameFixture.FreePolicies);
        return New(_packs);
    }

    private static CountryState Country(Game g) => g.State.Countries.Values.Single();

    private static CountryState WithUltimatum(Game g) =>
        Country(g) with { Politics = Pol(g) with { Ultimatums = new[] { new UltimatumInfo("base:c", "d1", 2) } } };

    [Fact]
    public void AQuietQuarterHasNoDecision()
    {
        var g = Make("a", new[] { "a", "b" }, "a");
        Assert.Empty(Decisions.Open(Country(g), _packs));
    }

    [Fact]
    public void AnUltimatumIsTheLeadersDecisionOnly()
    {
        var leader = Make("b", new[] { "b", "c" }, "b");
        var d = Assert.Single(Decisions.Open(WithUltimatum(leader), _packs));
        Assert.Equal((DecisionKind.Ultimatum, "base:c"), (d.Kind, d.PartyId));
        var opposition = Make("a", new[] { "b", "c" }, "b");
        Assert.Empty(Decisions.Open(WithUltimatum(opposition), _packs));
        var partner = Make("a", new[] { "a", "b", "c" }, "b");
        Assert.Empty(Decisions.Open(WithUltimatum(partner), _packs));
    }

    [Fact]
    public void AnOfferAndTheFormationAreDecisions()
    {
        // b leads alone without a majority; after the election someone forms: the player either forms itself or gets an offer
        var g = Make("a", new[] { "b" }, "b", turnsToElection: 1);
        g.EndTurn();
        for (int i = 0; i < 3 && Pol(g).Formation is not ({ PlayerIsFormateur: true } or { Offer: not null }); i++) g.EndTurn();
        var f = Pol(g).Formation;
        Assert.NotNull(f);
        var kinds = Decisions.Open(Country(g), _packs).Select(d => d.Kind).ToList();
        Assert.Contains(f!.Offer != null ? DecisionKind.Offer : DecisionKind.Formation, kinds);
    }

    [Fact]
    public void TheWindowForPromisesIsADecisionWhileThereIsSomethingToPromise()
    {
        var g = Make("a", new[] { "b", "c" }, "b", turnsToElection: 8);
        var promises = Assert.Single(Decisions.Open(Country(g), _packs), d => d.Kind == DecisionKind.Promises);
        Assert.True(promises.Urgent); // the window just opened
        Assert.Equal(3, Decisions.Promisable(Country(g), _packs).Count);
        Assert.True(g.Apply(new MakePromise("d1")).Accepted);
        Assert.True(g.Apply(new MakePromise("d2")).Accepted);
        Assert.DoesNotContain(Decisions.Open(Country(g), _packs), d => d.Kind == DecisionKind.Promises); // both slots used
        var early = Make("a", new[] { "b", "c" }, "b", turnsToElection: 9);
        Assert.Empty(Decisions.Open(Country(early), _packs));
    }

    [Fact]
    public void AnOpenProposalIsADecisionUntilAnswered()
    {
        var g = Make("a", new[] { "b", "c" }, "b");
        var country = Country(g);
        var proposal = new ProposalInfo("5:base:tax", 5, ProposalKind.Measure, "base:b", "base:tax", 40, 35, 0.5, false);
        var open = country with { Politics = Pol(g) with { Proposals = new[] { proposal } } };
        Assert.Equal(DecisionKind.Proposal, Assert.Single(Decisions.Open(open, _packs)).Kind);
        var answered = country with { Politics = Pol(g) with { Proposals = new[] { proposal with { Answer = Stance.Support } } } };
        Assert.Empty(Decisions.Open(answered, _packs));
    }
}
