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
