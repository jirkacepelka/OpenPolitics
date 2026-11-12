using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Economy;

/// <summary>Registry of the economy models a pack can select with a value's <c>owner</c>.</summary>
public static class EconomyModels
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { "macro_v1" };

    public static IEconomyModel Create(string owner, IEnumerable<PolicyDef> policies) => owner switch
    {
        "macro_v1" => new MacroModelV1(policies),
        _ => throw new InvalidOperationException($"Unknown economy model '{owner}'."),
    };
}
