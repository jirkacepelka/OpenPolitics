using OpenPolitics.Core.Graph;

namespace OpenPolitics.Core.Economy;

/// <summary>Everything an economy model sees for one step.</summary>
public sealed record EconomyInput(
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, Rate> EdgeRates,
    IReadOnlyDictionary<string, double> PolicyParams,
    IReadOnlyDictionary<string, double> Parameters);

/// <summary>
/// Steps the values it owns by one turn of <c>dt</c> years. Implementations must be deterministic and must not
/// impose a fixed ceiling on any value.
/// </summary>
public interface IEconomyModel
{
    string Id { get; }
    IReadOnlyList<string> OwnedValueIds { get; }
    IReadOnlyDictionary<string, double> Step(EconomyInput input, double dt);
}
