using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Presentation;

/// <summary>The headline numbers every client shows on its overview, computed in one place.</summary>
public sealed record Figures(
    double? Gdp, double? GrowthPercent, double? Unemployment, double? Inflation,
    double? DebtPercentOfGdp, double? BudgetPercentOfGdp, double Mood)
{
    /// <param name="gdpHistory">GDP at the end of every quarter so far, oldest first, including the start (growth is year on year).</param>
    public static Figures From(CountryState c, IReadOnlyList<double> gdpHistory, IReadOnlyList<VoterGroupDef> groups)
    {
        var v = c.Values;
        double? Get(string id) => v.TryGetValue(id, out var x) ? x : null;
        double? gdp = Get(MacroModelV1.Ids.Gdp);
        double? growth = gdp is { } g && gdpHistory.Count > 4 && gdpHistory[^5] > 0 ? (g / gdpHistory[^5] - 1) * 100 : null;
        double? debtRatio = null, budget = null;
        if (Get(MacroModelV1.Ids.Debt) is { } debt && gdp is { } y && y > 0)
        {
            debtRatio = debt / y * 100;
            if (Get(MacroModelV1.Ids.Revenue) is { } rev && Get(MacroModelV1.Ids.Spending) is { } spend
                && Get(MacroModelV1.Ids.InterestRate) is { } rate && Get(MacroModelV1.Ids.Inflation) is { } infl)
                budget = (rev - spend - debt * (rate - infl) / 100.0) / y * 100;
        }
        return new Figures(gdp, growth, Get(MacroModelV1.Ids.Unemployment), Get(MacroModelV1.Ids.Inflation),
            debtRatio, budget, Moods.Overall(groups, c.Satisfaction));
    }
}
