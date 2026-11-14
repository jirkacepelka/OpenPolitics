using OpenPolitics.Core.Graph;

namespace OpenPolitics.Core.Economy;

/// <summary>
/// How a spending policy follows the economy once the player left the default (share of GDP): "inflation" keeps the
/// real amount fixed, "none" keeps the nominal amount fixed. The base is real GDP and the price level at the switch.
/// </summary>
public sealed record IndexSetting(string Mode, double BaseGdp, double BasePrice);

/// <summary>Everything an economy model sees for one step.</summary>
public sealed record EconomyInput(
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, Rate> EdgeRates,
    IReadOnlyDictionary<string, double> PolicyParams,
    IReadOnlyDictionary<string, double> Parameters,
    IReadOnlyDictionary<string, IndexSetting>? Indexation = null);

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
