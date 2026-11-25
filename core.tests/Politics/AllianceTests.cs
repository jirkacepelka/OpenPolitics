using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0019: parties run a joint list with a joint programme; it helps small parties over the threshold and out of the Duverger squeeze.</summary>
public class AllianceTests
{
    [Fact]
    public void AJointListMustClearAHigherThreshold()
    {
        var pr = new ProportionalSystem(100, 0.05);
        var votes = new Dictionary<string, double> { ["big"] = 0.93, ["list:a+b"] = 0.07 };
        Assert.True(pr.Allocate(votes)["list:a+b"] > 0);
        Assert.Equal(0, pr.Allocate(votes, new Dictionary<string, double> { ["list:a+b"] = AllianceRules.ThresholdFactor(2) })["list:a+b"]);
        Assert.Equal(1.6, AllianceRules.ThresholdFactor(2), 9);
        Assert.Equal(2.2, AllianceRules.ThresholdFactor(3), 9);
    }

    [Fact]
    public void SeatsOfAListAreSplitByTheMembersVotes()
    {
        var split = AllianceMath.SplitSeats(10, new[] { "a", "b", "c" }, new Dictionary<string, double> { ["a"] = 0.5, ["b"] = 0.3, ["c"] = 0.2 });
        Assert.Equal(new[] { 5, 3, 2 }, new[] { split["a"], split["b"], split["c"] });
        var odd = AllianceMath.SplitSeats(7, new[] { "a", "b" }, new Dictionary<string, double> { ["a"] = 0.5, ["b"] = 0.5 });
        Assert.Equal(7, odd.Values.Sum());
        Assert.Equal(4, odd["a"]); // a tie goes to the lower id
    }

    [Fact]
    public void TheJointProgrammeIsTheMembersMergedBySupport()
    {
        PartyDef Party(string id, double tax, double w) => new(id, "name." + id,
            new Dictionary<string, PlatformPosition> { ["t:tax"] = new(tax, w) }, new Dictionary<string, double>(), false,
            Array.Empty<DemandDef>(), Array.Empty<RedLineDef>(), 0, new List<FactionDef>(), "x");
        var merged = AllianceMath.MergePlatform(new[] { Party("a", 20, 1), Party("b", 40, 1) }, new[] { 0.3, 0.1 });
        Assert.Equal(25, merged["t:tax"].Target, 9); // a counts three times as much
        var onlyOne = AllianceMath.MergePlatform(new[] { Party("a", 20, 1), new PartyDef("b", "name.b", new Dictionary<string, PlatformPosition>(), new Dictionary<string, double>(), false,
            Array.Empty<DemandDef>(), Array.Empty<RedLineDef>(), 0, new List<FactionDef>(), "x") }, new[] { 0.2, 0.2 });
        Assert.Equal(0.5, onlyOne["t:tax"].Weight, 9); // a law only one partner cares about is stressed half as much
    }

    private static Game Small(int turnsToElection = 5, string player = "y", ulong seed = 1) =>
        New(Load(new[] { new Spec("a", 0.60), new Spec("b", 0.31), new Spec("y", 0.045, Tax: 30), new Spec("z", 0.045, Tax: 30) }, player, interval: 60, turnsToElection: turnsToElection), seed);

    private static PlayerAction Join(params string[] partners) => new FormAlliance(partners.Select(p => "base:" + p).ToList());

    [Fact]
    public void TooEarlyAndBadPartnersAreRefused()
    {
        var far = Small(turnsToElection: 20);
        Assert.Equal("error.alliance_too_early", far.Apply(Join("z")).ErrorKey);
        var g = Small();
        Assert.Equal("error.alliance_size", g.Apply(Join()).ErrorKey);
        Assert.Equal("error.unknown_party", g.Apply(Join("nobody")).ErrorKey);
        double pc = Pol(g).Party!.PoliticalCapital;
        Assert.False(g.Apply(Join("nobody")).Accepted);
        Assert.Equal(pc, Pol(g).Party!.PoliticalCapital, 12);
    }

    [Fact]
    public void FormingCostsCapitalAndOnlyOneListPerParty()
    {
        var g = Small();
        double pc = Pol(g).Party!.PoliticalCapital;
        var preview = g.PreviewAlliance(new[] { "base:z" })!;
        Assert.True(preview.AllAccept, string.Join(",", preview.Views.Select(v => v.Reason)));
        Assert.True(g.Apply(Join("z")).Accepted);
        Assert.Equal(pc - AllianceRules.Cost, Pol(g).Party!.PoliticalCapital, 9);
        var list = Assert.Single(Pol(g).Alliances!);
        Assert.Equal(new[] { "base:y", "base:z" }, list.Members);
        Assert.True(list.Loss >= AllianceRules.LossBase);
        Assert.Equal("error.in_alliance", g.Apply(Join("a")).ErrorKey);
    }

    [Fact]
    public void LeavingCostsCredibilityAndFreesTheParty()
    {
        var g = Small();
        Assert.Equal("error.no_alliance", g.Apply(new LeaveAlliance()).ErrorKey);
        Assert.True(g.Apply(Join("z")).Accepted);
        double cred = Pol(g).Party!.Credibility;
        Assert.True(g.Apply(new LeaveAlliance()).Accepted);
        Assert.Empty(Pol(g).Alliances!);
        Assert.Equal(cred - AllianceRules.LeaveCredibility, Pol(g).Party!.Credibility, 9);
    }

    [Fact]
    public void ThePartnerRefusesWhenItWouldLoseSeats()
    {
        // a big party gains nothing from a tiny junior on its list: it loses the votes a compromise costs
        var g = New(Load(new[] { new Spec("a", 0.50, Tax: 30), new Spec("b", 0.40), new Spec("y", 0.10, Tax: 30) }, "y", interval: 60, turnsToElection: 5), 1);
        var preview = g.PreviewAlliance(new[] { "base:a" })!;
        Assert.False(preview.AllAccept);
        Assert.Contains(preview.Views, v => v.PartyId == "base:a" && !v.Accepts && v.Reason == "seats");
        Assert.Equal("error.alliance_refused", g.Apply(Join("a")).ErrorKey);
    }

    [Fact]
    public void TheElectionSplitsTheListsSeatsAndReportsTheList()
    {
        var g = Small(turnsToElection: 2);
        Assert.True(g.Apply(Join("z")).Accepted);
        ElectionHeld? held = null;
        for (int i = 0; i < 3 && held == null; i++) held = g.EndTurn().PoliticalEvents.OfType<ElectionHeld>().FirstOrDefault();
        Assert.NotNull(held);
        var list = Assert.Single(held!.Alliances!);
        Assert.Equal(new[] { "base:y", "base:z" }, list);
        Assert.Equal(200, held.Seats.Values.Sum());
        Assert.Empty(Pol(g).Alliances!); // a list stands for one election
    }

    [Fact]
    public void AlliancesReplayFromTheSave()
    {
        var packs = Load(new[] { new Spec("a", 0.60), new Spec("b", 0.31), new Spec("y", 0.045, Tax: 30), new Spec("z", 0.045, Tax: 30) }, "y", interval: 60, turnsToElection: 5);
        var g = New(packs, 3);
        Assert.True(g.Apply(Join("z")).Accepted);
        g.EndTurn();
        var loaded = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(Pol(g).Alliances!.Select(a => string.Join("+", a.Members)), Pol(loaded).Alliances!.Select(a => string.Join("+", a.Members)));
        Assert.Equal(Pol(g).Support.OrderBy(k => k.Key).Select(k => k.Value), Pol(loaded).Support.OrderBy(k => k.Key).Select(k => k.Value));
    }

    [Fact]
    public void TwoSmallPartiesCanFormAListThatTheThresholdWouldOtherwiseDeny()
    {
        // two parties at 4.5 % each are out alone (5 %); together (9 %, less the compromise, against 8 %) they are in
        var g = New(Load(new[] { new Spec("a", 0.60), new Spec("b", 0.31), new Spec("y", 0.045, Tax: 30), new Spec("z", 0.045, Tax: 30) }, "y", interval: 60, turnsToElection: 5), 1);
        var views = g.PreviewAlliance(new[] { "base:z" })!.Views;
        Assert.All(views, v => Assert.Equal(0, v.SeatsAlone));
        Assert.All(views, v => Assert.True(v.SeatsTogether > 0, $"{v.PartyId} gets no seats"));
    }

    [Theory]
    [InlineData("base:cz_2015_districts")]
    [InlineData("base:cz_2015_runoff")]
    public void InMajoritySystemsAJointListWinsSmallPartiesSeats(string scenario)
    {
        var packs = Content.BasePackTests.LoadBase().Packs!;
        var g = Simulation.NewGame(packs, scenario, 5, "base:open_net");
        for (int i = 0; i < 4; i++) g.EndTurn(); // the window opens 8 quarters before the election
        var view = g.PreviewAlliance(new[] { "base:social_alliance" })!;
        Assert.True(view.AllAccept, string.Join(",", view.Views.Select(v => v.PartyId + ":" + v.Reason)));
        var own = view.Views.Single(v => v.PartyId == "base:open_net");
        Assert.True(own.SeatsTogether >= own.SeatsAlone + 5, $"{own.SeatsAlone} -> {own.SeatsTogether}");
    }

    [Fact]
    public void AiPartiesFormJointListsWhenBothGain()
    {
        var packs = Content.BasePackTests.LoadBase().Packs!;
        var g = Simulation.NewGame(packs, "base:cz_2015_districts", 5, "base:citizens_movement");
        for (int i = 0; i < 9; i++) g.EndTurn(); // 12 quarters to the election, the AI decides 4 before it
        var lists = Pol(g).Alliances!;
        Assert.NotEmpty(lists);
        Assert.All(lists, a => { Assert.False(a.ByPlayer); Assert.DoesNotContain("base:citizens_movement", a.Members); });
    }
}
