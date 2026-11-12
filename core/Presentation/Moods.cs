using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Presentation;

/// <summary>Plain-language mood of voters, shared by every client.</summary>
public static class Moods
{
    /// <summary>Size- and weight-averaged satisfaction of all voter groups, in [-1, 1].</summary>
    public static double Overall(IReadOnlyList<VoterGroupDef> groups, IReadOnlyDictionary<string, double> satisfaction)
    {
        double total = 0, weight = 0;
        foreach (var g in groups)
        {
            double w = g.Size * g.Weight;
            total += w * satisfaction.GetValueOrDefault(g.Id);
            weight += w;
        }
        return weight > 0 ? total / weight : 0;
    }

    /// <summary>Localization key `ui.mood.1` (furious) to `ui.mood.5` (enthusiastic).</summary>
    public static string Key(double s) => "ui.mood." + (s < -0.6 ? 1 : s < -0.2 ? 2 : s < 0.2 ? 3 : s < 0.6 ? 4 : 5);
}
