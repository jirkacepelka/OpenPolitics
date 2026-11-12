using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Packs;

public class PackValidatorTests
{
    private static Diagnostic Single(LoadResult r, string code) =>
        Assert.Single(r.Diagnostics, d => d.Code == code);

    [Fact]
    public void DanglingEdgeTargetIsE004WithLine()
    {
        var r = TestPacks.Load(f => f["edges/e.yaml"] += "- id: t:e3\n  from: t:gdp\n  to: t:nothing\n");
        var d = Single(r, "E004");
        Assert.Equal("edges/e.yaml", d.File);
        Assert.Equal(9, d.Line);
        Assert.Contains("t:nothing", d.Message);
    }

    [Theory]
    [InlineData("scenarios/s.yaml", "- id: t:s\n  country: t:ghost\n  start_year: 2015\n")]
    [InlineData("events/ev.yaml", "- id: t:ev\n  condition: \"1 > 0\"\n  probability: 0.1\n  text_key: event.ev\n  effects:\n    - {value: t:ghost, delta: 1}\n")]
    [InlineData("voter_groups/g.yaml", "- id: t:g1\n  value_weights: {t:ghost: 1}\n")]
    [InlineData("policies/p.yaml", "- id: t:tax\n  min: 0\n  max: 1\n  default: 0\n  revenue_base: t:ghost\n")]
    public void OtherDanglingReferencesAreE004(string path, string yaml)
    {
        var r = TestPacks.Load(f => f[path] = yaml);
        Assert.Contains(r.Diagnostics, d => d.Code == "E004" && d.Message.Contains("t:ghost"));
    }

    [Fact]
    public void ZeroDelayCycleIsE005()
    {
        var r = TestPacks.Load(f => f["edges/e.yaml"] += "- id: t:e3\n  from: t:gdp\n  to: t:unemployment\n");
        var d = Single(r, "E005");
        Assert.Contains("t:e2", d.Message);
        Assert.Contains("t:e3", d.Message);
    }

    [Fact]
    public void SelfEdgeWithoutDelayIsE005()
    {
        var r = TestPacks.Load(f => f["edges/e.yaml"] += "- id: t:e3\n  from: t:gdp\n  to: t:gdp\n");
        Single(r, "E005");
    }

    [Fact]
    public void CycleBrokenByDelayIsAllowed()
    {
        var r = TestPacks.Load(f => f["edges/e.yaml"] +=
            "- id: t:e3\n  from: t:gdp\n  to: t:unemployment\n  delay: {type: lag, turns: 1}\n");
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void MissingLocalizationKeyIsE006PerLanguage()
    {
        var r = TestPacks.Load(f => f["localization/cs.yaml"] = f["localization/cs.yaml"].Replace("name.t:tax: Daň\n", ""));
        var d = Single(r, "E006");
        Assert.Contains("name.t:tax", d.Message);
        Assert.Contains("cs", d.Message);
    }

    [Fact]
    public void MissingEnglishFileIsE006()
    {
        var r = TestPacks.Load(f => f.Remove("localization/en.yaml"));
        Assert.Contains(r.Diagnostics, d => d.Code == "E006" && d.Message.Contains("en"));
    }

    [Theory]
    [InlineData("policies/p.yaml", "- id: t:tax\n  min: 0\n  max: 60\n  default: 99\n")]
    [InlineData("values/v.yaml", "- id: t:gdp\n  initial: 1\n  scale: 0\n- id: t:unemployment\n  initial: 5\n")]
    [InlineData("events/ev.yaml", "- id: t:ev\n  condition: \"1 > 0\"\n  probability: 1.5\n  text_key: event.ev\n")]
    [InlineData("edges/e.yaml", "- id: t:e1\n  from: t:tax\n  to: t:gdp\n  fn: {type: wobble}\n")]
    public void RangeProblemsAreE007(string path, string yaml)
    {
        var r = TestPacks.Load(f => f[path] = yaml);
        Assert.Contains(r.Diagnostics, d => d.Code == "E007");
    }

    [Fact]
    public void PolicyAndValueSharingAnIdIsE003()
    {
        var r = TestPacks.Load(f => f["policies/p.yaml"] = "- id: t:gdp\n  min: 0\n  max: 1\n  default: 0\n");
        Assert.Contains(r.Diagnostics, d => d.Code == "E003");
    }
}
