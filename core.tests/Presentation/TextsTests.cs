using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Presentation;

public class TextsTests
{
    private static readonly Core.Packs.PackSet Packs = BasePackTests.LoadBase().Packs!;

    private static Texts For(string lang) => new(Packs, new Localizer(Packs, lang));

    [Theory]
    [InlineData("en", Role.Leader, "Goals:")]
    [InlineData("en", Role.Partner, "Goals:")]
    [InlineData("en", Role.Opposition, "Opposition goals:")]
    [InlineData("cs", Role.Leader, "Cíle:")]
    [InlineData("cs", Role.Opposition, "Cíle opozice:")]
    public void TheGoalsHeadingDependsOnTheRole(string lang, Role role, string expected) =>
        Assert.Equal(expected, For(lang).GoalsHeading(role));

    [Fact]
    public void APromiseSaysItHoldsUntilTheElectionAndWhetherItCountsAgainstTheParty()
    {
        var t = For("en");
        var lines = t.PromiseStatus(new PromiseInfo("balanced_budget", 3, 2, 2, false), 5);
        Assert.Equal("Holds until the next election (in 5 quarters). Now: not met.", lines[0]);
        Assert.Contains("costs you trust", lines[1]);
        var free = t.PromiseStatus(new PromiseInfo("balanced_budget", 3, 4, 1, true), 1);
        Assert.Contains("Now: met", free[0]);
        Assert.Contains("would not cost you", free[1]);
    }
}
