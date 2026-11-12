using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenPolitics.Core.Gameplay;

public sealed class SaveMismatchException(string message) : Exception(message);

/// <summary>A saved game is the scenario, seed, pack hashes and the log of accepted actions (state is replayed).</summary>
public sealed record SaveData(
    string ScenarioId, ulong Seed,
    IReadOnlyDictionary<string, string> PackHashes,
    IReadOnlyList<(int Turn, PlayerAction Action)> Actions,
    int Turn)
{
    private sealed class Dto
    {
        public string Scenario { get; set; } = "";
        public string Seed { get; set; } = "0";
        public int Turn { get; set; }
        public SortedDictionary<string, string> PackHashes { get; set; } = new(StringComparer.Ordinal);
        public List<ActionDto> Actions { get; set; } = new();
    }

    private sealed class ActionDto
    {
        public int Turn { get; set; }
        public string Type { get; set; } = "";
        public string Policy { get; set; } = "";
        public string Value { get; set; } = "0";
    }

    private static ISerializer Serializer => new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance).Build();

    private static IDeserializer Deserializer => new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance).Build();

    public string ToYaml()
    {
        var dto = new Dto
        {
            Scenario = ScenarioId,
            Seed = Seed.ToString(CultureInfo.InvariantCulture),
            Turn = Turn,
            PackHashes = new SortedDictionary<string, string>(PackHashes.ToDictionary(k => k.Key, k => k.Value), StringComparer.Ordinal),
        };
        foreach (var (turn, action) in Actions)
        {
            if (action is SetPolicy sp)
                dto.Actions.Add(new ActionDto { Turn = turn, Type = "set_policy", Policy = sp.PolicyId, Value = sp.Value.ToString("R", CultureInfo.InvariantCulture) });
            else throw new InvalidOperationException($"Cannot serialize action {action.GetType().Name}.");
        }
        return Serializer.Serialize(dto);
    }

    public static SaveData FromYaml(string yaml)
    {
        var dto = Deserializer.Deserialize<Dto>(yaml) ?? throw new FormatException("Empty save file.");
        var actions = new List<(int, PlayerAction)>();
        foreach (var a in dto.Actions)
        {
            if (a.Type != "set_policy") throw new FormatException($"Unknown action type '{a.Type}' in save.");
            actions.Add((a.Turn, new SetPolicy(a.Policy, double.Parse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture))));
        }
        return new SaveData(dto.Scenario, ulong.Parse(dto.Seed, CultureInfo.InvariantCulture),
            new Dictionary<string, string>(dto.PackHashes), actions, dto.Turn);
    }
}
