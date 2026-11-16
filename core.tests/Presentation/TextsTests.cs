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

    public static readonly string[] Topics = { "capital", "organisation", "credibility", "cohesion", "relations", "fiscal_blame", "campaign", "organise", "court",
        "criticize", "stance", "promise", "priority", "shift", "propose", "announce", "proposals", "reshuffle", "exits" };

    [Fact]
    public void EveryStatAndActionIsExplainedInBothLanguagesWithTheEngineNumbers()
    {
        foreach (var lang in new[] { "cs", "en" })
            foreach (var topic in Topics)
            {
                string text = For(lang).Explain(topic);
                Assert.False(text.StartsWith("ui.") || text == topic, $"{lang}:{topic} missing");
                Assert.DoesNotContain("{", text);
            }
        var en = For("en");
        Assert.Contains("3.5 + 8", en.Explain("capital"));
        Assert.Contains("exceed 30", en.Explain("capital"));
        Assert.Contains("+4 points for 4 capital", en.Explain("organisation"));
        Assert.Contains("halves every 12 quarters", en.Explain("fiscal_blame"));
    }

    [Fact]
    public void AProposalSaysWhoWantsWhatAndWhatTheAnswerMeans()
    {
        var t = For("en");
        var p = new ProposalInfo("4:base:pensions", 4, ProposalKind.Measure, "base:citizens_movement", "base:pensions", 9, 9.5, -0.4, true);
        var (who, what) = t.Proposal(p);
        Assert.Contains("puts to parliament", who);
        Assert.StartsWith("Pensions", what);
        var hints = t.ProposalHints(p);
        Assert.Contains("against your programme", hints[0]);
        Assert.Contains("Your seats decide", hints[1]);
    }
}
