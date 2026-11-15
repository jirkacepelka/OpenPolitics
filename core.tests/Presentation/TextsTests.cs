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
}
