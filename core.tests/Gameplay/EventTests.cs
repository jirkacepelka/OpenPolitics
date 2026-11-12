using System.Globalization;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Gameplay;

public class EventTests
{
    private static PackSet WithEvents(params (string Id, string Cond, double Prob, double Delta)[] events) =>
        GameFixture.Load(f =>
        {
            string yaml = "";
            foreach (var (id, cond, prob, delta) in events)
            {
                yaml += $"- id: base:{id}\n  condition: \"{cond}\"\n  probability: {prob.ToString(CultureInfo.InvariantCulture)}\n" +
                        $"  text_key: event.{id}\n  effects:\n    - {{value: base:health, delta: {delta.ToString(CultureInfo.InvariantCulture)}}}\n";
                f["localization/cs.yaml"] += $"event.{id}: cs\n";
                f["localization/en.yaml"] += $"event.{id}: en\n";
            }
            f["events/ev.yaml"] = yaml;
        });

    private static List<IReadOnlyList<string>> Run(PackSet packs, ulong seed, int turns)
    {
        var g = Simulation.NewGame(packs, "base:test", seed);
        var fired = new List<IReadOnlyList<string>>();
        for (int i = 0; i < turns; i++) fired.Add(g.EndTurn().FiredEventIds);
        return fired;
    }

    [Fact]
    public void ProbabilityZeroNeverFiresOneAlwaysFires()
    {
        var fired = Run(WithEvents(("never", "base:gdp > 0", 0, 0), ("always", "base:gdp > 0", 1, 0)), 3, 30);
        Assert.All(fired, f => Assert.Equal(new[] { "base:always" }, f.ToArray()));
    }

    [Fact]
    public void FalseConditionNeverFires()
    {
        var fired = Run(WithEvents(("nope", "base:gdp < 0", 1, 0)), 3, 20);
        Assert.All(fired, f => Assert.Empty(f));
    }

    [Fact]
    public void EffectsAreAppliedAndReported()
    {
        var g = Simulation.NewGame(WithEvents(("boost", "base:gdp > 0", 1, 5)), "base:test", 1);
        var report = g.EndTurn();
        var health = report.Changes.Single(c => c.ValueId == "base:health");
        Assert.Equal(65.0, health.Before);
        Assert.Equal(70.0, health.After, 9);
        Assert.Equal(70.0, g.State.Countries["base:czechia"].Values["base:health"], 9);
    }

    [Fact]
    public void FiringIsDeterministicForSeedAndVariesAcrossSeeds()
    {
        var packs = WithEvents(("coin", "base:gdp > 0", 0.3, 0));
        var a = Run(packs, 11, 60).Select(f => f.Count).ToArray();
        var b = Run(packs, 11, 60).Select(f => f.Count).ToArray();
        var c = Run(packs, 12, 60).Select(f => f.Count).ToArray();
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.InRange(a.Sum(), 5, 35); // roughly 0.3 * 60
    }

    [Fact]
    public void FiredEventsAreListedInIdOrder()
    {
        var fired = Run(WithEvents(("b", "base:gdp > 0", 1, 0), ("a", "base:gdp > 0", 1, 0), ("c", "base:gdp > 0", 1, 0)), 1, 2);
        Assert.Equal(new[] { "base:a", "base:b", "base:c" }, fired[0].ToArray());
    }

    [Fact]
    public void RngStreamStableWhenUnrelatedEventAdded()
    {
        var plain = Run(WithEvents(("coin", "base:gdp > 0", 0.5, 0)), 5, 60);
        var withFalse = Run(WithEvents(("aaa_false", "base:gdp < 0", 0.5, 0), ("coin", "base:gdp > 0", 0.5, 0)), 5, 60);
        Assert.Equal(plain.Select(f => f.Contains("base:coin")).ToArray(),
                     withFalse.Select(f => f.Contains("base:coin")).ToArray());
        Assert.All(withFalse, f => Assert.DoesNotContain("base:aaa_false", f));
    }
}
