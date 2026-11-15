using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Presentation;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Presentation;

/// <summary>Interest on the debt is part of the deficit, so it has to be visible next to spending (it is not a policy).</summary>
public class FiguresTests
{
    [Fact]
    public void DebtInterestIsShownAndAddsUpToTheBalance()
    {
        var packs = BasePackTests.LoadBase().Packs!;
        var game = Simulation.NewGame(packs, "base:cz_2015_sandbox", 1);
        var c = game.State.Countries.Single().Value;
        var f = Figures.From(c, new[] { c.Values[MacroModelV1.Ids.Gdp] }, packs.VoterGroups);
        Assert.NotNull(f.DebtInterest);
        double debt = c.Values[MacroModelV1.Ids.Debt];
        double real = c.Values[MacroModelV1.Ids.InterestRate] - c.Values[MacroModelV1.Ids.Inflation];
        Assert.Equal(debt * real / 100.0, f.DebtInterest!.Value, 3);
        double gdp = c.Values[MacroModelV1.Ids.Gdp];
        double expected = (c.Values[MacroModelV1.Ids.Revenue] - c.Values[MacroModelV1.Ids.Spending] - f.DebtInterest.Value) / gdp * 100;
        Assert.Equal(expected, f.BudgetPercentOfGdp!.Value, 6);
    }
}
