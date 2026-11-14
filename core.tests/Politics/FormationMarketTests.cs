using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

public class FormationMarketTests
{
    // a is the largest but sits far from everybody; b and c are close and together have a majority.
    private static readonly Spec[] Split =
    {
        new("a", 0.36, Tax: 5), new("b", 0.34, Tax: 50), new("c", 0.30, Tax: 52),
    };

    // a is the largest and close to b.
    private static readonly Spec[] Aligned =
    {
        new("a", 0.36, Tax: 48), new("b", 0.34, Tax: 50), new("c", 0.30, Tax: 5),
    };

    [Fact]
    public void ACoherentSecondPartyLeadsInsteadOfTheLargest()
    {
        var g = New(Load(Split, "c", government: new[] { "a" }));
        var reports = Run(g, 1);
        var started = Events<FormationStarted>(reports).Single();
        Assert.NotEqual("base:a", started.Formateur);
        Assert.Equal("base:b", started.Formateur);
    }

    [Fact]
    public void TheLargestPartyStillLeadsWhenItIsAsCoherent()
    {
        var g = New(Load(Aligned, "c", government: new[] { "c" }));
        var started = Events<FormationStarted>(Run(g, 1)).Single();
        Assert.Equal("base:a", started.Formateur);
    }

    [Fact]
    public void ThePlayerLeadsWhenTheMarketRanksThemFirst()
    {
        var g = New(Load(Split, "b", government: new[] { "a" }));
        Run(g, 1);
        Assert.True(Pol(g).Formation!.PlayerIsFormateur);
        Assert.Equal("base:b", Pol(g).Formation!.Formateur);
    }
}
