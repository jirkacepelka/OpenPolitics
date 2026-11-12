using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Packs;

public class DefinitionsTests
{
    [Fact]
    public void DiagnosticToStringFormat()
    {
        var d = new Diagnostic(Severity.Error, "E004", "dangling ref", "values/a.yaml", 7);
        Assert.Equal("values/a.yaml:7: Error E004: dangling ref", d.ToString());
    }

    [Fact]
    public void DiagnosticWithoutFileOmitsLocation()
    {
        var d = new Diagnostic(Severity.Warning, "W001", "hello", null, 0);
        Assert.Equal("Warning W001: hello", d.ToString());
    }

    [Fact]
    public void ListsAreOrdinalSorted()
    {
        ValueDef V(string id) => new(id, "unit", 1, 1, 1, null, "f.yaml");
        var set = new PackSet(
            new[] { V("b:x"), V("a:z"), V("a:y"), V("B:q") },
            Array.Empty<PolicyDef>(), Array.Empty<EdgeDef>(), Array.Empty<VoterGroupDef>(),
            Array.Empty<EventDef>(), Array.Empty<CountryDef>(), Array.Empty<ScenarioDef>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());
        Assert.Equal(new[] { "B:q", "a:y", "a:z", "b:x" }, set.Values.Select(v => v.Id).ToArray());
    }

    [Fact]
    public void GetScenarioThrowsForUnknownId()
    {
        var set = new PackSet(
            Array.Empty<ValueDef>(), Array.Empty<PolicyDef>(), Array.Empty<EdgeDef>(), Array.Empty<VoterGroupDef>(),
            Array.Empty<EventDef>(), Array.Empty<CountryDef>(), Array.Empty<ScenarioDef>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(), new Dictionary<string, string>());
        Assert.Throws<KeyNotFoundException>(() => set.GetScenario("nope:nope"));
    }
}
