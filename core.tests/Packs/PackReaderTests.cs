using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Packs;

public class PackReaderTests
{
    internal const string Manifest = "id: test\nversion: 0.1.0\nlicense: CC-BY-SA-4.0\nillustrative: true\n";
    internal const string OneValue = "- id: test:gdp\n  unit: CZK\n  initial: 100\n  reference: 100\n  scale: 10\n";

    internal static InMemoryPackSource Pack(params (string Path, string Text)[] files)
    {
        var d = new Dictionary<string, string> { ["manifest.yaml"] = Manifest };
        foreach (var (p, t) in files) d[p] = t;
        return new InMemoryPackSource("test", d);
    }

    [Fact]
    public void ReadsMinimalPack()
    {
        var (pack, diags) = PackReader.Read(Pack(("values/a.yaml", OneValue)));
        Assert.Empty(diags);
        Assert.NotNull(pack);
        Assert.Equal("test", pack!.Manifest.Id);
        var v = Assert.Single(pack.Values);
        Assert.Equal("test:gdp", v.Id);
        Assert.Equal(100.0, v.Initial);
        Assert.Equal(10.0, v.Scale);
        Assert.Equal("values/a.yaml", v.Source);
    }

    [Fact]
    public void ReportsYamlSyntaxErrorWithLine()
    {
        var (_, diags) = PackReader.Read(Pack(("values/a.yaml", "- id: test:x\n  unit: [unclosed\n")));
        var d = Assert.Single(diags, x => x.Code == "E001");
        Assert.Equal("values/a.yaml", d.File);
        Assert.True(d.Line > 0);
    }

    [Fact]
    public void UnknownFieldIsE002()
    {
        var (_, diags) = PackReader.Read(Pack(("values/a.yaml", OneValue + "  colour: red\n")));
        var d = Assert.Single(diags, x => x.Code == "E002");
        Assert.Equal("values/a.yaml", d.File);
        Assert.Contains("colour", d.Message);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void MissingManifestIsAnErrorAndNoPack()
    {
        var (pack, diags) = PackReader.Read(new InMemoryPackSource("x", new Dictionary<string, string>()));
        Assert.Null(pack);
        Assert.Contains(diags, x => x.Code == "E002" && x.File == "manifest.yaml");
    }

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    public void EmptyManifestIsE002(string text)
    {
        var (pack, diags) = PackReader.Read(new InMemoryPackSource("x", new Dictionary<string, string> { ["manifest.yaml"] = text }));
        Assert.Null(pack);
        Assert.Contains(diags, d => d.Code == "E002" && d.File == "manifest.yaml" && d.Message.Contains("empty"));
    }

    [Fact]
    public void HandlesBomAndCrlf()
    {
        var lf = PackReader.Read(Pack(("values/a.yaml", OneValue))).Pack!;
        var crlf = PackReader.Read(Pack(("values/a.yaml", "﻿" + OneValue.Replace("\n", "\r\n")))).Pack!;
        Assert.Equal(lf.Values, crlf.Values);
    }

    [Fact]
    public void TabIndentationGivesDiagnosticNotException()
    {
        var (_, diags) = PackReader.Read(Pack(("values/a.yaml", "- id: test:x\n\tunit: CZK\n")));
        Assert.Contains(diags, x => x.Code == "E001" && x.File == "values/a.yaml");
    }

    [Fact]
    public void EmptyFileYieldsNoDefinitionsNoCrash()
    {
        var (pack, diags) = PackReader.Read(Pack(("values/a.yaml", ""), ("policies/b.yaml", "   \n# only a comment\n")));
        Assert.Empty(diags);
        Assert.Empty(pack!.Values);
        Assert.Empty(pack.Policies);
    }

    [Fact]
    public void HashIgnoresLineEndings()
    {
        var a = PackReader.Read(Pack(("values/a.yaml", OneValue))).Pack!;
        var b = PackReader.Read(Pack(("values/a.yaml", OneValue.Replace("\n", "\r\n")))).Pack!;
        var c = PackReader.Read(Pack(("values/a.yaml", OneValue.Replace("100", "101")))).Pack!;
        Assert.Equal(a.Hash, b.Hash);
        Assert.NotEqual(a.Hash, c.Hash);
    }

    [Fact]
    public void ReadsAllDefinitionKindsAndLocalization()
    {
        var (pack, diags) = PackReader.Read(Pack(
            ("values/a.yaml", OneValue),
            ("policies/p.yaml", "- id: test:tax\n  unit: percent\n  min: 0\n  max: 60\n  default: 20\n  delay_turns: 1\n  revenue_base: test:gdp\n"),
            ("edges/e.yaml", "- id: test:e1\n  from: test:tax\n  to: test:gdp\n  kind: relative\n  strength: -0.1\n  fn: {type: tanh, scale: 2}\n  delay: {type: lag, turns: 2}\n  override: true\n"),
            ("voter_groups/g.yaml", "- id: test:poor\n  size: 0.3\n  weight: 1\n  value_weights: {test:gdp: 0.5}\n  policy_prefs: {test:tax: 10}\n  policy_weights: {test:tax: 0.2}\n"),
            ("events/ev.yaml", "- id: test:boom\n  condition: \"test:gdp > 50\"\n  probability: 0.1\n  text_key: event.boom\n  effects:\n    - {value: test:gdp, delta: 5}\n"),
            ("countries/c.yaml", "- id: test:land\n  name_key: country.land\n  parameters: {alpha: 0.35}\n"),
            ("scenarios/s.yaml", "- id: test:s\n  country: test:land\n  start_year: 2015\n  turns_per_year: 4\n  start_values: {test:gdp: 90}\n  start_policies: {test:tax: 25}\n"),
            ("localization/cs.yaml", "name.test:gdp: HDP\n"),
            ("localization/en.yaml", "name.test:gdp: GDP\n")));
        Assert.Empty(diags);
        Assert.Equal("test:gdp", pack!.Policies[0].RevenueBaseValueId);
        var e = Assert.Single(pack.Edges);
        Assert.Equal(EdgeKind.Relative, e.Kind);
        Assert.Equal("tanh", e.Fn.Type);
        Assert.Equal(2.0, e.Fn.Scale);
        Assert.Equal("lag", e.Delay.Type);
        Assert.Contains("test:e1", pack.Overrides);
        Assert.Equal(0.5, pack.VoterGroups[0].ValueWeights["test:gdp"]);
        Assert.Equal(5.0, pack.Events[0].Effects[0].Delta);
        Assert.Equal(0.35, pack.Countries[0].Parameters["alpha"]);
        Assert.Equal(90.0, pack.Scenarios[0].StartValues["test:gdp"]);
        Assert.Equal("GDP", pack.Localization["en"]["name.test:gdp"]);
    }

    [Fact]
    public void DirectoryPackSourceListsFilesForwardSlashedAndSorted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "op-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "values"));
        File.WriteAllText(Path.Combine(dir, "manifest.yaml"), Manifest);
        File.WriteAllText(Path.Combine(dir, "values", "b.yaml"), "");
        File.WriteAllText(Path.Combine(dir, "values", "a.yaml"), "");
        try
        {
            var files = new DirectoryPackSource(dir).ListFiles().ToArray();
            Assert.Equal(new[] { "manifest.yaml", "values/a.yaml", "values/b.yaml" }, files);
        }
        finally { Directory.Delete(dir, true); }
    }
}
