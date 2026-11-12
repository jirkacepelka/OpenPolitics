using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class FormationActionTests
{
    private static readonly Spec[] Four =
    {
        new("a", 0.42, Price: 0, Tax: 30), new("b", 0.33, Price: 3, Tax: 31), new("c", 0.22, Price: 3, Tax: 45), new("z", 0.03),
    };

    private static ProposeCoalition Propose(string[] partners, params (string, string)[] agreements) =>
        new(partners.Select(p => "base:" + p).ToList(), agreements.Select(a => new AgreedDemand("base:" + a.Item1, a.Item2)).ToList());

    [Fact]
    public void PlayerProposesAValidCoalitionAndFormsAGovernment()
    {
        var g = New(Load(Four, "a"));
        g.EndTurn();
        var r = g.Apply(Propose(new[] { "b" }, ("b", "d1")));
        Assert.True(r.Accepted, r.ErrorKey);
        var p = Pol(g);
        Assert.Equal((Phase.Governing, Role.Leader, "base:a"), (p.Phase, p.PlayerRole, p.Government!.Leader));
        Assert.Equal(new[] { "base:a", "base:b" }, p.GovernmentParties);
        Assert.Single(Events<GovernmentFormed>(new[] { g.EndTurn() }));
    }

    [Fact]
    public void RefusalCarriesAReasonAndDetail()
    {
        var g = New(Load(Four, "a"));
        g.EndTurn();
        var noMajority = g.Apply(Propose(Array.Empty<string>()));
        Assert.Equal("error.refused.majority", noMajority.ErrorKey);
        var price = g.Apply(Propose(new[] { "b" }));
        Assert.Equal(("error.refused.price", "base:b:"), (price.ErrorKey, price.Detail));
        Assert.Equal(Phase.Formation, Pol(g).Phase);

        var red = FormationFixture.Load(Four, "a", redLines: "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 42}\n");
        var h = New(red);
        h.EndTurn();
        var r = h.Apply(Propose(new[] { "b" }, ("b", "d2")));
        Assert.Equal(("error.refused.red_line", "base:b:d2"), (r.ErrorKey, r.Detail));
    }

    [Fact]
    public void SuggestCoalitionIsAcceptedByTheRules()
    {
        var g = New(Load(Four, "a"));
        Assert.Null(g.SuggestCoalition()); // not formateur yet
        g.EndTurn();
        var s = g.SuggestCoalition()!;
        Assert.Equal("base:a", s.Formateur);
        Assert.True(g.Apply(new ProposeCoalition(s.Partners, s.Agreements)).Accepted);
    }

    [Fact]
    public void NonFormateurCannotPropose()
    {
        var g = New(Load(Four, "z"));
        Assert.Equal("error.not_formateur", g.Apply(Propose(new[] { "b" })).ErrorKey);
        g.EndTurn();
        Assert.Equal("error.not_formateur", g.Apply(Propose(new[] { "b" })).ErrorKey);
    }

    [Fact]
    public void AcceptOfferFormsTheGovernmentWithThePlayerAsPartner()
    {
        var g = New(Load(Four, "b"));
        Run(g, 2);
        Assert.True(g.Apply(new AcceptOffer()).Accepted);
        var p = Pol(g);
        Assert.Equal((Phase.Governing, Role.Partner), (p.Phase, p.PlayerRole));
        Assert.Contains("base:b", p.GovernmentParties);
        Assert.Single(Events<GovernmentFormed>(new[] { g.EndTurn() }));
    }

    [Fact]
    public void RejectOfferContinuesWithoutThePlayer()
    {
        var g = New(Load(Four, "b"));
        Run(g, 2);
        Assert.True(g.Apply(new RejectOffer()).Accepted);
        Assert.Null(Pol(g).Formation!.Offer);
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<OfferLapsed>());
        var formed = Assert.Single(r.PoliticalEvents.OfType<GovernmentFormed>());
        Assert.DoesNotContain("base:b", formed.Government.Parties);
    }

    [Fact]
    public void AcceptOrRejectWithoutAnOfferIsAnError()
    {
        var g = New(Load(Four, "z"));
        Assert.Equal("error.no_offer", g.Apply(new AcceptOffer()).ErrorKey);
        Assert.Equal("error.no_offer", g.Apply(new RejectOffer()).ErrorKey);
    }

    [Fact]
    public void OnlyTheLeaderCanCallEarlyElection()
    {
        var other = New(Load(Four, "z", government: new[] { "a", "z" }, leader: "a", turnsToElection: 5));
        Assert.Equal("error.not_leader", other.Apply(new CallEarlyElection()).ErrorKey);
        var g = New(Load(Four, "a", turnsToElection: 5));
        Assert.True(g.Apply(new CallEarlyElection()).Accepted);
        Assert.Single(g.EndTurn().PoliticalEvents.OfType<ElectionHeld>());
        Assert.Equal("error.wrong_phase", g.Apply(new CallEarlyElection()).ErrorKey);
    }

    [Fact]
    public void SaveAndLoadReplayFormationActions()
    {
        var packs = Load(Four, "a", turnsToElection: 1);
        var g = New(packs, 9);
        g.EndTurn();
        var s = g.SuggestCoalition()!;
        Assert.True(g.Apply(new ProposeCoalition(s.Partners, s.Agreements)).Accepted);
        Run(g, 3);
        Assert.True(g.Apply(new CallEarlyElection()).Accepted);
        g.EndTurn();

        var yaml = g.Save().ToYaml();
        var h = Simulation.Load(packs, SaveData.FromYaml(yaml));
        var (pg, ph) = (Pol(g), Pol(h));
        Assert.Equal(pg.Phase, ph.Phase);
        Assert.Equal(pg.Seats, ph.Seats);
        Assert.Equal(pg.GovernmentParties, ph.GovernmentParties);
        Assert.Equal(pg.Government!.Commitments.Count, ph.Government!.Commitments.Count);
        Assert.Equal(g.State.Turn, h.State.Turn);
    }

    [Fact]
    public void RefusedActionsAreNotSaved()
    {
        var g = New(Load(Four, "a"));
        g.EndTurn();
        Assert.False(g.Apply(Propose(Array.Empty<string>())).Accepted);
        Assert.Empty(g.Save().Actions);
    }
}
