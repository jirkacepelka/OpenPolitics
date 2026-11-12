using System.Text.Json;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Tests.Content;

namespace OpenPolitics.Core.Tests.Gameplay;

/// <summary>
/// Golden snapshots: bit-exact values of an 80-turn passive game. A change here is either intentional
/// (regenerate with OPENPOLITICS_UPDATE_GOLDEN=1 dotnet test, then review the diff) or a bug.
/// </summary>
public class GoldenTests
{
    private static readonly int[] Checkpoints = { 10, 40, 80 };

    private static SortedDictionary<string, SortedDictionary<string, string>> Play(ulong seed)
    {
        var g = Simulation.NewGame(BasePackTests.LoadBase().Packs!, "base:cz_2015_sandbox", seed);
        var result = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        for (int turn = 1; turn <= 80; turn++)
        {
            g.EndTurn();
            if (!Checkpoints.Contains(turn)) continue;
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (id, v) in g.State.Countries["base:czechia"].Values)
                values[id] = BitConverter.DoubleToInt64Bits(v).ToString("X16");
            foreach (var (id, v) in g.State.Countries["base:czechia"].Satisfaction)
                values["satisfaction:" + id] = BitConverter.DoubleToInt64Bits(v).ToString("X16");
            result[$"turn{turn:D2}"] = values;
        }
        return result;
    }

    private static string GoldenPath(ulong seed) =>
        Path.Combine(BasePackTests.RepoRoot(), "core.tests", "Golden", $"seed{seed}.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void GoldenSnapshot(ulong seed)
    {
        var actual = Play(seed);
        string path = GoldenPath(seed);
        if (Environment.GetEnvironmentVariable("OPENPOLITICS_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(actual, Json) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"Golden file missing: {path}. Run with OPENPOLITICS_UPDATE_GOLDEN=1 once and review it.");
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, SortedDictionary<string, string>>>(File.ReadAllText(path))!;
        foreach (var (turn, values) in expected)
            foreach (var (id, bits) in values)
                Assert.True(actual[turn][id] == bits, $"seed {seed} {turn} {id}: expected {bits}, got {actual[turn][id]}");
        Assert.Equal(expected.Keys, actual.Keys);
    }
}
