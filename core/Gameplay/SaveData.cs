using System.Globalization;
using OpenPolitics.Core.Politics;
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
        public string Party { get; set; } = "";
        public string Group { get; set; } = "";
        public int Effort { get; set; }
        public string Stance { get; set; } = "";
        public string Mode { get; set; } = "";
        public List<string> Partners { get; set; } = new();
        public List<string> Agreements { get; set; } = new(); // "party|demand"
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
            else if (action is SetIndexation si)
                dto.Actions.Add(new ActionDto { Turn = turn, Type = "set_indexation", Policy = si.PolicyId, Mode = si.Mode });
            else if (action is ProposePolicy pp)
                dto.Actions.Add(new ActionDto { Turn = turn, Type = "propose_policy", Policy = pp.PolicyId, Value = pp.Target.ToString("R", CultureInfo.InvariantCulture) });
            else if (action is ProposeCoalition pc)
                dto.Actions.Add(new ActionDto
                {
                    Turn = turn, Type = "propose_coalition", Partners = pc.Partners.ToList(),
                    Agreements = pc.Agreements.Select(a => a.PartyId + "|" + a.DemandId).ToList(),
                });
            else if (action is Criticize cr) dto.Actions.Add(new ActionDto { Turn = turn, Type = "criticize", Policy = cr.ValueId });
            else if (action is TakeStance ts) dto.Actions.Add(new ActionDto { Turn = turn, Type = "take_stance", Policy = ts.MeasureId, Stance = ts.Stance.ToString() });
            else if (action is BuildOrganization) dto.Actions.Add(new ActionDto { Turn = turn, Type = "build_organization" });
            else if (action is Court court) dto.Actions.Add(new ActionDto { Turn = turn, Type = "court", Party = court.PartyId });
            else if (action is ShiftPlatform sh)
                dto.Actions.Add(new ActionDto { Turn = turn, Type = "shift_platform", Policy = sh.PolicyId, Value = sh.Target.ToString("R", CultureInfo.InvariantCulture) });
            else if (action is Campaign camp) dto.Actions.Add(new ActionDto { Turn = turn, Type = "campaign", Group = camp.GroupId, Effort = camp.Effort });
            else if (action is AcceptOffer) dto.Actions.Add(new ActionDto { Turn = turn, Type = "accept_offer" });
            else if (action is RejectOffer) dto.Actions.Add(new ActionDto { Turn = turn, Type = "reject_offer" });
            else if (action is CallEarlyElection) dto.Actions.Add(new ActionDto { Turn = turn, Type = "call_early_election" });
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
            PlayerAction action = a.Type switch
            {
                "set_policy" => new SetPolicy(a.Policy, double.Parse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture)),
                "set_indexation" => new SetIndexation(a.Policy, a.Mode),
                "propose_policy" => new ProposePolicy(a.Policy, double.Parse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture)),
                "propose_coalition" => new ProposeCoalition(a.Partners.ToList(), a.Agreements.Select(x =>
                {
                    var parts = x.Split('|');
                    if (parts.Length != 2) throw new FormatException($"Bad agreement '{x}' in save.");
                    return new AgreedDemand(parts[0], parts[1]);
                }).ToList()),
                "criticize" => new Criticize(a.Policy),
                "take_stance" => new TakeStance(a.Policy, Enum.TryParse<Stance>(a.Stance, out var st) ? st : throw new FormatException($"Bad stance '{a.Stance}' in save.")),
                "build_organization" => new BuildOrganization(),
                "court" => new Court(a.Party),
                "shift_platform" => new ShiftPlatform(a.Policy, double.Parse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture)),
                "campaign" => new Campaign(a.Group, a.Effort),
                "accept_offer" => new AcceptOffer(),
                "reject_offer" => new RejectOffer(),
                "call_early_election" => new CallEarlyElection(),
                _ => throw new FormatException($"Unknown action type '{a.Type}' in save."),
            };
            actions.Add((a.Turn, action));
        }
        return new SaveData(dto.Scenario, ulong.Parse(dto.Seed, CultureInfo.InvariantCulture),
            new Dictionary<string, string>(dto.PackHashes), actions, dto.Turn);
    }
}
