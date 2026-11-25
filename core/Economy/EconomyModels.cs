using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Economy;

/// <summary>Registry of the economy models a pack can select with a value's <c>owner</c>.</summary>
public static class EconomyModels
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { "macro_v1", "demography_v1", "pensions_v1" };

    public static IEconomyModel Create(string owner, IEnumerable<PolicyDef> policies, IEnumerable<ValueDef>? values = null) => owner switch
    {
        "macro_v1" => new MacroModelV1(policies, values),
        "demography_v1" => new DemographyModelV1(),
        "pensions_v1" => new PensionModelV1(),
        _ => throw new InvalidOperationException($"Unknown economy model '{owner}'."),
    };
}
