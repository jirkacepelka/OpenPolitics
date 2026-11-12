using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Content;

public class BasePackTests
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenPolitics.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    internal static LoadResult LoadBase() =>
        PackLoader.Load(new IPackSource[] { new DirectoryPackSource(Path.Combine(RepoRoot(), "data", "base")) });

    [Fact]
    public void BasePackLoadsWithoutDiagnostics()
    {
        var r = LoadBase();
        Assert.Empty(r.Diagnostics);
        Assert.True(r.Ok);
    }

    [Fact]
    public void BasePackIsMarkedIllustrative()
    {
        var (pack, _) = PackReader.Read(new DirectoryPackSource(Path.Combine(RepoRoot(), "data", "base")));
        Assert.True(pack!.Manifest.Illustrative);
    }

    [Fact]
    public void ContentIsBroadEnoughForTheFirstSlice()
    {
        var p = LoadBase().Packs!;
        Assert.True(p.Values.Count >= 15, $"values {p.Values.Count}");
        Assert.True(p.Policies.Count >= 12, $"policies {p.Policies.Count}");
        Assert.True(p.VoterGroups.Count >= 6, $"voter groups {p.VoterGroups.Count}");
        Assert.True(p.Events.Count >= 5, $"events {p.Events.Count}");
        Assert.True(p.Edges.Count >= 40, $"edges {p.Edges.Count}");
        Assert.Contains(p.Scenarios, s => s.Id == "base:cz_2015_sandbox");
    }

    [Fact]
    public void EveryNamedThingHasNonEmptyCsAndEnText()
    {
        var p = LoadBase().Packs!;
        foreach (var lang in new[] { "cs", "en" })
        {
            var t = p.Localization[lang];
            foreach (var id in p.Values.Select(v => v.Id).Concat(p.Policies.Select(x => x.Id)).Concat(p.VoterGroups.Select(g => g.Id)))
                Assert.False(string.IsNullOrWhiteSpace(t[$"name.{id}"]), $"{lang} name.{id}");
            foreach (var e in p.Events) Assert.False(string.IsNullOrWhiteSpace(t[e.TextKey]), $"{lang} {e.TextKey}");
        }
        Assert.NotEqual(p.Localization["cs"]["name.base:gdp"], p.Localization["en"]["name.base:gdp"]);
    }

    [Fact]
    public void ScenarioStartsInstantiableGame()
    {
        var g = Simulation.NewGame(LoadBase().Packs!, "base:cz_2015_sandbox", 1);
        for (int i = 0; i < 8; i++) g.EndTurn();
        var c = g.State.Countries["base:czechia"];
        Assert.All(c.Values, kv => Assert.True(double.IsFinite(kv.Value), kv.Key));
        Assert.Equal(2017.0, g.State.Year);
    }

    [Fact]
    public void StartingStateIsInternallyConsistent()
    {
        var packs = LoadBase().Packs!;
        var c = Simulation.NewGame(packs, "base:cz_2015_sandbox", 1).State.Countries["base:czechia"];
        double a = c.Values[MacroModelV1.Ids.Productivity], k = c.Values[MacroModelV1.Ids.Capital];
        double l = c.Values[MacroModelV1.Ids.LabourForce] * (1 - c.Values[MacroModelV1.Ids.Unemployment] / 100);
        double y = MacroModelV1.Production(a, k, l, 0.35);
        Assert.Equal(c.Values[MacroModelV1.Ids.Gdp], y, y * 1e-4);
        Assert.Equal(0.40 * y, c.Values[MacroModelV1.Ids.Revenue], y * 1e-3);
    }

    [Fact]
    public void PassiveEightyTurnsStayPlausible()
    {
        var g = Simulation.NewGame(LoadBase().Packs!, "base:cz_2015_sandbox", 1);
        var start = g.State.Countries["base:czechia"].Values;
        for (int i = 0; i < 80; i++) g.EndTurn();
        var v = g.State.Countries["base:czechia"].Values;
        double gdpGrowth = v[MacroModelV1.Ids.Gdp] / start[MacroModelV1.Ids.Gdp];
        Assert.InRange(gdpGrowth, 1.1, 2.5);
        Assert.InRange(v[MacroModelV1.Ids.Unemployment], 2.0, 10.0);
        Assert.InRange(v[MacroModelV1.Ids.Inflation], 0.0, 6.0);
        Assert.InRange(v[MacroModelV1.Ids.Debt] / v[MacroModelV1.Ids.Gdp], 0.0, 0.8);
        foreach (var id in new[] { "base:health", "base:education_level", "base:environment", "base:inequality", "base:crime" })
            Assert.InRange(v[id] / start[id], 0.5, 1.6);
    }
}
