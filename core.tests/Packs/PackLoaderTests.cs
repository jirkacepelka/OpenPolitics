using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Packs;

internal static class TestPacks
{
    public static Dictionary<string, string> Baseline(string id = "t") => new()
    {
        ["manifest.yaml"] = $"id: {id}\nversion: 0.1.0\nlicense: CC-BY-SA-4.0\nillustrative: true\n",
        ["values/v.yaml"] =
            "- id: t:gdp\n  unit: CZK\n  initial: 100\n  reference: 100\n  scale: 10\n" +
            "- id: t:unemployment\n  unit: percent\n  initial: 5\n  scale: 1\n",
        ["policies/p.yaml"] =
            "- id: t:tax\n  unit: percent\n  min: 0\n  max: 60\n  default: 20\n  revenue_base: t:gdp\n",
        ["edges/e.yaml"] =
            "- id: t:e1\n  from: t:tax\n  to: t:unemployment\n  strength: 0.1\n" +
            "- id: t:e2\n  from: t:unemployment\n  to: t:gdp\n  strength: -0.1\n",
        ["voter_groups/g.yaml"] =
            "- id: t:g1\n  size: 1\n  weight: 1\n  value_weights: {t:gdp: 1}\n  policy_prefs: {t:tax: 20}\n  policy_weights: {t:tax: 0.2}\n",
        ["events/ev.yaml"] =
            "- id: t:ev\n  condition: \"t:gdp > 50\"\n  probability: 0.1\n  text_key: event.ev\n  effects:\n    - {value: t:gdp, delta: 1}\n",
        ["countries/c.yaml"] = "- id: t:land\n  name_key: country.land\n",
        ["scenarios/s.yaml"] =
            "- id: t:s\n  country: t:land\n  start_year: 2015\n  start_values: {t:gdp: 100}\n",
        ["localization/cs.yaml"] =
            "name.t:gdp: HDP\nname.t:unemployment: Nezaměstnanost\nname.t:tax: Daň\nname.t:g1: Skupina\ncountry.land: Země\nevent.ev: Událost\n",
        ["localization/en.yaml"] =
            "name.t:gdp: GDP\nname.t:unemployment: Unemployment\nname.t:tax: Tax\nname.t:g1: Group\ncountry.land: Land\nevent.ev: Event\n",
    };

    public static LoadResult Load(Dictionary<string, string> files) =>
        PackLoader.Load(new IPackSource[] { new InMemoryPackSource("t", files) });

    public static LoadResult Load(Action<Dictionary<string, string>> mutate)
    {
        var f = Baseline();
        mutate(f);
        return Load(f);
    }
}

public class PackLoaderTests
{
    [Fact]
    public void ValidPackHasNoDiagnostics()
    {
        var r = TestPacks.Load(TestPacks.Baseline());
        Assert.Empty(r.Diagnostics);
        Assert.True(r.Ok);
        Assert.Equal(2, r.Packs!.Values.Count);
        Assert.Equal("t", r.Packs.PackHashes.Keys.Single());
    }

    private static IPackSource Overlay(string id, string valuesYaml, string deps = "") =>
        new InMemoryPackSource(id, new Dictionary<string, string>
        {
            ["manifest.yaml"] = $"id: {id}\nversion: 1\nlicense: x\n{deps}",
            ["values/v.yaml"] = valuesYaml,
        });

    [Fact]
    public void LaterPackOverridesWithFlag()
    {
        var r = PackLoader.Load(new[]
        {
            new InMemoryPackSource("t", TestPacks.Baseline()),
            Overlay("mod", "- id: t:gdp\n  override: true\n  unit: EUR\n  initial: 7\n  scale: 1\n", "dependencies: [t]\n"),
        });
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal("EUR", r.Packs!.Values.Single(v => v.Id == "t:gdp").Unit);
        Assert.Equal(2, r.Packs.PackHashes.Count);
    }

    [Fact]
    public void LaterPackRedefineWithoutFlagIsE003()
    {
        var r = PackLoader.Load(new[]
        {
            new InMemoryPackSource("t", TestPacks.Baseline()),
            Overlay("mod", "- id: t:gdp\n  unit: EUR\n  initial: 7\n  scale: 1\n", "dependencies: [t]\n"),
        });
        var d = Assert.Single(r.Diagnostics, x => x.Code == "E003");
        Assert.Equal("values/v.yaml", d.File);
        Assert.Equal(1, d.Line);
        Assert.False(r.Ok);
    }

    [Fact]
    public void MissingDependencyIsE009()
    {
        var r = PackLoader.Load(new[] { Overlay("mod", "", "dependencies: [t]\n") });
        Assert.Contains(r.Diagnostics, d => d.Code == "E009" && d.Message.Contains("'t'"));
    }

    [Fact]
    public void DependencyLoadedAfterDependentIsE009()
    {
        var r = PackLoader.Load(new[]
        {
            Overlay("mod", "", "dependencies: [t]\n"),
            new InMemoryPackSource("t", TestPacks.Baseline()),
        });
        Assert.Contains(r.Diagnostics, d => d.Code == "E009");
    }

    [Fact]
    public void DuplicateWithinOnePackIsE003()
    {
        var r = TestPacks.Load(f => f["values/w.yaml"] = "- id: t:gdp\n  initial: 1\n");
        Assert.Contains(r.Diagnostics, d => d.Code == "E003");
    }
}
