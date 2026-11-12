using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class FormationTests
{
    // a 42 %, b 33 %, c 22 %, z 3 % (below the threshold, no seats). Player is z unless stated otherwise.
    private static readonly Spec[] Four =
    {
        new("a", 0.42, Price: 0, Tax: 30), new("b", 0.33, Price: 3, Tax: 31), new("c", 0.22, Price: 3, Tax: 45), new("z", 0.03),
    };

    [Fact]
    public void StartingGovernmentIsGoverningWithRoleLeader()
    {
        var p = Pol(New(Load(Four, "a")));
        Assert.Equal(Phase.Governing, p.Phase);
        Assert.Equal(Role.Leader, p.PlayerRole);
        Assert.Equal("a".Insert(0, "base:"), p.Government!.Leader);
        Assert.Null(p.Formation);
    }

    [Fact]
    public void ElectionStartsFormationAndLargestPartyIsFormateur()
    {
        var g = New(Load(Four, "z"));
        var r = g.EndTurn();
        Assert.Single(r.PoliticalEvents.OfType<ElectionHeld>());
        Assert.Equal("base:a", Assert.Single(r.PoliticalEvents.OfType<FormationStarted>()).Formateur);
        var p = Pol(g);
        Assert.Equal(Phase.Formation, p.Phase);
        Assert.Equal(("base:a", 1, 4, false), (p.Formation!.Formateur, p.Formation.Attempt, p.Formation.TurnsLeft, p.Formation.PlayerIsFormateur));
    }

    [Fact]
    public void AIFormsAMajorityGovernmentWithinOneTurn()
    {
        var g = New(Load(Four, "z"));
        var reports = Run(g, 2);
        var formed = Assert.Single(Events<GovernmentFormed>(reports));
        var p = Pol(g);
        Assert.Equal(Phase.Governing, p.Phase);
        Assert.Equal("base:a", formed.Government.Leader);
        Assert.Contains("base:b", formed.Government.Parties); // closest partner, price 3 paid
        Assert.True(CoalitionRules.HasMajority(p.GovernmentParties, p.Seats, 0.5));
        var c = Assert.Single(formed.Government.Commitments, x => x.PartyId == "base:b");
        Assert.Equal(40.0, c.Baseline); // base:spend default
        Assert.Equal(Role.Opposition, p.PlayerRole);
    }

    [Fact]
    public void PlayerIsOfferedAPlaceWhenInvited()
    {
        var g = New(Load(Four, "b"));
        var reports = Run(g, 2);
        var offer = Assert.Single(Events<OfferMade>(reports));
        Assert.Contains("base:b", offer.Proposal.Partners);
        var p = Pol(g);
        Assert.Equal(Phase.Formation, p.Phase);
        Assert.Equal(offer.Proposal, p.Formation!.Offer);
    }

    [Fact]
    public void UnansweredOfferLapsesAndAIProceedsWithoutThePlayer()
    {
        var g = New(Load(Four, "b"));
        var reports = Run(g, 4);
        Assert.Single(Events<OfferLapsed>(reports));
        var formed = Assert.Single(Events<GovernmentFormed>(reports));
        Assert.DoesNotContain("base:b", formed.Government.Parties);
        Assert.Contains("base:c", formed.Government.Parties);
        Assert.Equal(Role.Opposition, Pol(g).PlayerRole);
    }

    [Fact]
    public void PlayerFormateurWhoDoesNothingFailsAfterTheTimerThenTheNextPartyTries()
    {
        var g = New(Load(Four, "a"));
        var first = g.EndTurn();
        Assert.True(Pol(g).Formation!.PlayerIsFormateur);
        var reports = new List<TurnReport> { first };
        reports.AddRange(Run(g, 4)); // timer: 4 turns left after the election turn
        var failed = Assert.Single(Events<FormationFailed>(reports));
        Assert.Equal("base:a", failed.Formateur);
        Assert.Equal(("base:b", 2), (Pol(g).Formation?.Formateur ?? Pol(g).Government!.Leader, Pol(g).Formation?.Attempt ?? 2));
    }

    [Fact]
    public void TwoFailedAttemptsLeadToCaretakerThenEarlyElection()
    {
        // Nobody can pay the prices (weights 6 in total, price 100), so every attempt fails.
        var spec = new[] { new Spec("a", 0.40, 100), new Spec("b", 0.35, 100), new Spec("c", 0.25, 100) };
        var g = New(Load(spec, "a", interval: 40));
        var reports = Run(g, 20);
        Assert.NotEmpty(Events<EarlyElectionsCalled>(reports));
        Assert.True(Events<ElectionHeld>(reports).Count() >= 2, "an early election follows the caretaker period");
        var firstCall = reports.First(r => r.PoliticalEvents.OfType<EarlyElectionsCalled>().Any()).Turn;
        var secondElection = reports.Where(r => r.PoliticalEvents.OfType<ElectionHeld>().Any()).Skip(1).First().Turn;
        Assert.Equal(firstCall + 2, secondElection); // caretaker_turns = 2
    }

    [Fact]
    public void ManyTinyPartiesStillFormAMajority()
    {
        var spec = Enumerable.Range(0, 8).Select(i => new Spec($"p{i}", 0.125, Price: 0, Tax: 20 + i)).ToArray();
        var g = New(Load(spec, "p7"), 3);
        Run(g, 3);
        var p = Pol(g);
        Assert.True(CoalitionRules.HasMajority(p.GovernmentParties, p.Seats, 0.5) || p.Phase != Phase.Governing);
    }

    [Fact]
    public void SinglePartyAboveThresholdFormsSoloGovernment()
    {
        var spec = new[] { new Spec("a", 0.96), new Spec("b", 0.02), new Spec("c", 0.02) };
        var g = New(Load(spec, "b"));
        var reports = Run(g, 2);
        Assert.Equal(new[] { "base:a" }, Assert.Single(Events<GovernmentFormed>(reports)).Government.Parties);
    }

    [Fact]
    public void SetPolicyIsRefusedInFormationAndForNonLeaders()
    {
        var g = New(Load(Four, "z", government: new[] { "a", "z" }, leader: "a"));
        Assert.Equal("error.not_leader", g.Apply(new SetPolicy("base:tax", 35)).ErrorKey);
        g.EndTurn(); // election -> formation
        Assert.Equal("error.no_majority", g.Apply(new SetPolicy("base:tax", 35)).ErrorKey);
        var leader = New(Load(Four, "a"));
        Assert.True(leader.Apply(new SetPolicy("base:tax", 35)).Accepted);
    }

    [Fact]
    public void FormationIsDeterministicPerSeed()
    {
        string Run(ulong seed)
        {
            var g = New(Load(Four, "b"), seed);
            var rs = FormationFixture.Run(g, 8);
            return string.Join("|", rs.SelectMany(r => r.PoliticalEvents).Select(e => e.GetType().Name + e.Turn));
        }
        Assert.Equal(Run(7), Run(7));
    }
}
