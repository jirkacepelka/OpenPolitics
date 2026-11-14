using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>Where a party stands on two axes, each -1..1: economic (left to right) and social (liberal to authoritarian).</summary>
public static class Compass
{
    /// <summary>
    /// Weighted mean over the party's platform of (axis loading x position of the target in the policy's range, -1..1),
    /// divided by the weight that can move the axis at all. A party with nothing on an axis sits at 0.
    /// </summary>
    public static (double X, double Y) Position(PartyDef party, IReadOnlyDictionary<string, PolicyDef> policies)
    {
        double x = 0, xw = 0, y = 0, yw = 0;
        foreach (var (id, pos) in party.Platform.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!policies.TryGetValue(id, out var def) || !(def.Max > def.Min)) continue;
            double centered = 2.0 * Math.Clamp((pos.Target - def.Min) / (def.Max - def.Min), 0.0, 1.0) - 1.0;
            x += pos.Weight * def.AxisX * centered; xw += pos.Weight * Math.Abs(def.AxisX);
            y += pos.Weight * def.AxisY * centered; yw += pos.Weight * Math.Abs(def.AxisY);
        }
        return (xw > 0 ? Math.Clamp(x / xw, -1.0, 1.0) : 0.0, yw > 0 ? Math.Clamp(y / yw, -1.0, 1.0) : 0.0);
    }
}
