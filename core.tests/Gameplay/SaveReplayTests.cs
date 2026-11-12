using OpenPolitics.Core.Gameplay;

namespace OpenPolitics.Core.Tests.Gameplay;

public class SaveReplayTests
{
    private static Game Play(PackSetHolder h, int turns)
    {
        var g = Simulation.NewGame(h.Packs, "base:test", 99);
        for (int t = 0; t < turns; t++)
        {
            if (t == 3) g.Apply(new SetPolicy("base:tax", 33));
            if (t == 3) g.Apply(new SetPolicy("base:slow", 7));
            if (t == 11) g.Apply(new SetPolicy("base:spend", 52.5));
            g.EndTurn();
        }
        g.Apply(new SetPolicy("base:tax", 41)); // made after the last EndTurn
        return g;
    }

    private sealed class PackSetHolder { public OpenPolitics.Core.Packs.PackSet Packs = GameFixture.Load(); }

    [Fact]
    public void SaveReplayEqualsLiveState()
    {
        var h = new PackSetHolder();
        var live = Play(h, 20);
        var save = live.Save();
        var loaded = Simulation.Load(h.Packs, save);
        Assert.Equal(GameTests.Fingerprint(live), GameTests.Fingerprint(loaded));
        for (int i = 0; i < 10; i++) { live.EndTurn(); loaded.EndTurn(); }
        Assert.Equal(GameTests.Fingerprint(live), GameTests.Fingerprint(loaded));
    }

    [Fact]
    public void SaveYamlRoundTrips()
    {
        var h = new PackSetHolder();
        var save = Play(h, 15).Save();
        var back = SaveData.FromYaml(save.ToYaml());
        Assert.Equal(save.ScenarioId, back.ScenarioId);
        Assert.Equal(save.Seed, back.Seed);
        Assert.Equal(save.Turn, back.Turn);
        Assert.Equal(save.PackHashes.OrderBy(k => k.Key), back.PackHashes.OrderBy(k => k.Key));
        Assert.Equal(save.Actions, back.Actions);
        Assert.Equal(GameTests.Fingerprint(Simulation.Load(h.Packs, save)), GameTests.Fingerprint(Simulation.Load(h.Packs, back)));
    }

    [Fact]
    public void SaveWithMaxSeedRoundTrips()
    {
        var g = Simulation.NewGame(GameFixture.Load(), "base:test", ulong.MaxValue);
        var back = SaveData.FromYaml(g.Save().ToYaml());
        Assert.Equal(ulong.MaxValue, back.Seed);
    }

    [Fact]
    public void LoadWithChangedPackThrowsNamingPack()
    {
        var h = new PackSetHolder();
        var save = Play(h, 5).Save();
        var changed = GameFixture.Load(f => f["values/social.yaml"] = f["values/social.yaml"].Replace("initial: 70", "initial: 71"));
        var ex = Assert.Throws<SaveMismatchException>(() => Simulation.Load(changed, save));
        Assert.Contains("'base'", ex.Message);
    }

    [Fact]
    public void LoadWithMissingOrExtraPackThrows()
    {
        var h = new PackSetHolder();
        var save = Play(h, 2).Save();
        var tampered = save with { PackHashes = new Dictionary<string, string>(save.PackHashes) { ["ghost"] = "00" } };
        var ex = Assert.Throws<SaveMismatchException>(() => Simulation.Load(h.Packs, tampered));
        Assert.Contains("'ghost'", ex.Message);
    }
}
