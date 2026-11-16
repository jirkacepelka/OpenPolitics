using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0014 (e): parties in government while the deficit rises are blamed for it; the blame fades slowly; cleaning up sheds it.</summary>
public class FiscalBlameTests
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    [Fact]
    public void BlameAccruesToTheGovernmentWhileTheDeficitRisesAndThenDecays()
    {
        var blame = new SortedDictionary<string, double>(Ord);
        for (int i = 0; i < 4; i++) FiscalBlame.Step(blame, "lead", new[] { "partner" }, deficitChange: 1.0);
        Assert.InRange(blame["lead"], 3.0 * FiscalBlame.LeaderShare, 4.0 * FiscalBlame.LeaderShare);
        Assert.True(blame["partner"] > 0 && blame["partner"] < blame["lead"]);
        Assert.False(blame.ContainsKey("opposition"));
        double peak = blame["lead"];
        FiscalBlame.Step(blame, "other", Array.Empty<string>(), deficitChange: 0.0);
        Assert.True(blame["lead"] < peak); // fades
        for (int i = 0; i < 40; i++) FiscalBlame.Step(blame, "other", Array.Empty<string>(), deficitChange: 0.0);
        Assert.True(blame.GetValueOrDefault("lead") < 0.1 * peak);
    }

    [Fact]
    public void ThePartyThatCleansUpIsNotPunishedLikeTheOneThatCausedIt()
    {
        var blame = new SortedDictionary<string, double>(Ord);
        for (int i = 0; i < 8; i++) FiscalBlame.Step(blame, "spender", Array.Empty<string>(), deficitChange: 0.75); // +6 points
        for (int i = 0; i < 8; i++) FiscalBlame.Step(blame, "cleaner", Array.Empty<string>(), deficitChange: -0.75); // back down
        Assert.Equal(0.0, blame.GetValueOrDefault("cleaner"));
        Assert.True(blame["spender"] > 0.5);
        Assert.True(FiscalBlame.Penalty(blame["spender"]) > FiscalBlame.Penalty(blame.GetValueOrDefault("cleaner")));
    }

    [Fact]
    public void CuttingTheDeficitInGovernmentShedsBlameFasterThanWaiting()
    {
        var waiting = new SortedDictionary<string, double>(Ord) { ["p"] = 3.0 };
        var cutting = new SortedDictionary<string, double>(Ord) { ["p"] = 3.0 };
        FiscalBlame.Step(waiting, "x", Array.Empty<string>(), 0.0);
        FiscalBlame.Step(cutting, "p", Array.Empty<string>(), -1.0);
        Assert.True(cutting["p"] < waiting["p"]);
    }

    // decision 0015 (7): blame is measured from the deficit a government inherited, and only above it
    private static Dictionary<string, double> Base(double b) => new() { ["lead"] = b, ["partner"] = b };

    [Fact]
    public void AShrinkingSurplusGivesNoBlameAndShedsWhatThereIs()
    {
        var fresh = new SortedDictionary<string, double>(Ord);
        var old = new SortedDictionary<string, double>(Ord) { ["lead"] = 1.5 };
        var faded = new SortedDictionary<string, double>(Ord) { ["lead"] = 1.5 };
        FiscalBlame.Step(fresh, "lead", new[] { "partner" }, Base(0), -3.0, -1.6);
        FiscalBlame.Step(old, "lead", Array.Empty<string>(), Base(0), -3.0, -1.6);
        FiscalBlame.Step(faded, "nobody", Array.Empty<string>(), Base(0), 0, 0);
        Assert.Empty(fresh);
        Assert.True(old["lead"] < faded["lead"], "a government in surplus sheds blame faster than it fades");
    }

    [Fact]
    public void AnInheritedDeficitThatFallsGivesNoBlameAndSheds()
    {
        var fresh = new SortedDictionary<string, double>(Ord);
        FiscalBlame.Step(fresh, "lead", Array.Empty<string>(), Base(5), 5.0, 3.0);
        Assert.Empty(fresh);
        var old = new SortedDictionary<string, double>(Ord) { ["lead"] = 3.0 };
        var waiting = new SortedDictionary<string, double>(Ord) { ["lead"] = 3.0 };
        FiscalBlame.Step(old, "lead", Array.Empty<string>(), Base(5), 5.0, 3.0);
        FiscalBlame.Step(waiting, "x", Array.Empty<string>(), Base(5), 5.0, 5.0);
        Assert.True(old["lead"] < waiting["lead"] - 1.9);
    }

    [Fact]
    public void GoingAboveTheInheritedDeficitIsBlamedForTheExcessOnly()
    {
        var blame = new SortedDictionary<string, double>(Ord);
        FiscalBlame.Step(blame, "lead", new[] { "partner" }, Base(5), 5.0, 7.0);
        Assert.Equal(2.0 * (1 + 7.0 / 3), blame["lead"], 9);
        Assert.Equal(1.0 * (1 + 7.0 / 3), blame["partner"], 9);
        var below = new SortedDictionary<string, double>(Ord);
        FiscalBlame.Step(below, "lead", Array.Empty<string>(), Base(5), 3.0, 4.5); // rising, but still under the inherited level
        Assert.Empty(below);
    }

    [Fact]
    public void AnInheritedSurplusIsBlamedOnlyOnceTheBudgetGoesIntoDeficit()
    {
        var blame = new SortedDictionary<string, double>(Ord);
        FiscalBlame.Step(blame, "lead", Array.Empty<string>(), Base(0), -1.0, 1.0);
        Assert.Equal(1.0 * (1 + 1.0 / 3), blame["lead"], 9);
    }

    [Fact]
    public void TheUsersThreeExamples()
    {
        // (1) -3 -> -1: still a surplus, nothing; (2) -3 -> +1: the part that went into deficit; (3) +10 -> +12: heavier, the deficit was already high
        double Blame(double from, double to)
        {
            var b = new SortedDictionary<string, double>(Ord);
            FiscalBlame.Step(b, "lead", Array.Empty<string>(), Base(0), from, to);
            return b.GetValueOrDefault("lead");
        }
        Assert.Equal(0.0, Blame(-3, -1));
        Assert.Equal(4.0 / 3, Blame(-3, 1), 9);
        Assert.Equal(8.0, Blame(10, 12), 9);
        Assert.True(Blame(10, 12) > Blame(-3, 1) && Blame(-3, 1) > Blame(-3, -1));
    }

    [Fact]
    public void TheBaselineResetsWithANewGovernment()
    {
        // b leads from the start (no deficit inherited); after the election a new government is measured from the deficit of that moment
        var packs = BasePackTests.LoadBase().Packs!;
        var g = Simulation.NewGame(packs, "base:cz_2015", 1);
        foreach (var (id, v) in new[] { ("base:pensions", 14.0), ("base:social_benefits", 14.0), ("base:health_spending", 12.0) })
            for (int i = 0; i < 3 && !g.Apply(new SetPolicy(id, v)).Accepted; i++) g.EndTurn();
        double before = 0;
        for (int i = 0; i < 30; i++)
        {
            var pol = g.State.Countries.Single().Value.Politics!;
            if (pol.Formation is { PlayerIsFormateur: true } && g.SuggestCoalition() is { } s) g.Apply(ProposeCoalition.From(s));
            else if (pol.Formation?.Offer != null) g.Apply(new AcceptOffer());
            var r = g.EndTurn();
            if (r.PoliticalEvents.OfType<GovernmentFormed>().Any())
            {
                var now = g.State.Countries.Single().Value;
                before = Math.Max(0, now.Values["base:deficit_pct"]);
                break;
            }
        }
        Assert.True(before > 0, "the deficit should be positive when the new government forms");
        // the same rule as the engine: a deficit at the inherited level is no excess
        var blame = new SortedDictionary<string, double>(Ord);
        FiscalBlame.Step(blame, "lead", Array.Empty<string>(), Base(before), before, before);
        Assert.Empty(blame);
    }

    [Fact]
    public void TheEngineBlamesALeaderWhoseDeficitRisesAndShowsItInTheSnapshot()
    {
        var packs = BasePackTests.LoadBase().Packs!;
        var g = Simulation.NewGame(packs, "base:cz_2015", 1);
        // the player leads; spend a lot so the deficit climbs
        foreach (var (id, v) in new[] { ("base:pensions", 14.0), ("base:social_benefits", 14.0), ("base:health_spending", 12.0) })
            for (int i = 0; i < 3 && !g.Apply(new SetPolicy(id, v)).Accepted; i++) g.EndTurn();
        for (int i = 0; i < 8; i++) g.EndTurn();
        var pol = g.State.Countries.Single().Value.Politics!;
        Assert.True(pol.Party!.FiscalBlame > 0.2, $"blame {pol.Party.FiscalBlame}");
        Assert.True(pol.FiscalBlame!["base:social_alliance"] > pol.FiscalBlame.GetValueOrDefault("base:civic_right"));
        var loaded = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(pol.FiscalBlame, loaded.State.Countries.Single().Value.Politics!.FiscalBlame);
    }
}
