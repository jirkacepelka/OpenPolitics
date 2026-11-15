using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;
using OpenPolitics.Core.Tests.Content;
using OpenPolitics.Core.Tests.Packs;
using Xunit;

namespace OpenPolitics.Core.Tests.Presentation;

/// <summary>Policies are shown as real rates (taxes, % of their base) or nominal yearly amounts (spending), not as shares of GDP.</summary>
public class PolicyUnitsTests
{
    private static readonly PackSet Packs = BasePackTests.LoadBase().Packs!;
    private static readonly Localizer En = new(Packs, "en");

    private static PolicyDef Tax(double baseShare = 0.5) =>
        new("t:tax", "tax_rate", 0, 30, 20, null, 0, 1, "base:gdp", "f", 0, 0, baseShare);

    private static PolicyDef Spend() => new("t:spend", "nominal_spending", 0, 20, 9, null, 1, 1, null, "f");

    [Fact]
    public void ATaxIsShownAsTheRateOnItsBase()
    {
        // 20 % of GDP raised from a base that is half of GDP is a 40 % rate.
        Assert.Equal("40.0 %", Fmt.Policy(Tax(0.5), 20, En));
        Assert.Equal("20.0 %", Fmt.Policy(Tax(1.0), 20, En));
    }

    [Fact]
    public void SpendingIsShownAsAYearlyAmountInMoney()
    {
        var t = new Localizer(Packs, "en") { NominalGdp = 5e12 };
        // 9 % of a 5 tn economy is 450 bn.
        Assert.Contains("450", Fmt.Policy(Spend(), 9, t));
        Assert.Contains("bn", Fmt.Policy(Spend(), 9, t));
        Assert.Contains("1.00 tn", Fmt.Policy(Spend(), 20, t));
    }

    [Fact]
    public void SpendingFallsBackToShareOfGdpWhenTheEconomyIsUnknown()
    {
        Assert.Contains("9.0", Fmt.Policy(Spend(), 9, new Localizer(Packs, "en")));
    }

    [Fact]
    public void TheBasePackHasNoPolicyInPercentOfGdp()
    {
        foreach (var p in Packs.Policies.Where(p => p.RevenueBaseValueId != null || p.CostPerUnit != 0))
            Assert.True(p.Unit is "tax_rate" or "nominal_spending", $"{p.Id} unit {p.Unit}");
    }

    [Fact]
    public void EveryTaxRateHasABaseAndRatesLookLikeRates()
    {
        foreach (var p in Packs.Policies.Where(p => p.Unit == "tax_rate"))
        {
            Assert.True(p.BaseShare > 0, $"{p.Id} has no base_share");
            Assert.True(p.Default / p.BaseShare <= 50 + 1e-9, $"{p.Id} default rate {p.Default / p.BaseShare}");
        }
    }

    [Fact]
    public void ATaxRateWithoutABaseIsRejectedByTheValidator()
    {
        var bad = TestPacks.Load(f => f["policies/p.yaml"] = "- id: t:tax\n  unit: tax_rate\n  min: 0\n  max: 60\n  default: 20\n  revenue_base: t:gdp\n");
        Assert.False(bad.Ok);
        var good = TestPacks.Load(f => f["policies/p.yaml"] = "- id: t:tax\n  unit: tax_rate\n  min: 0\n  max: 60\n  default: 20\n  revenue_base: t:gdp\n  base_share: 0.5\n");
        Assert.True(good.Ok, string.Join("; ", good.Diagnostics));
    }
}
