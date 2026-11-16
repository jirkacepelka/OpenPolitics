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
    public void APromiseSaysItIsJudgedAtTheElectionAfterTheComingTermAndWhetherItCounts()
    {
        var t = For("en");
        var fresh = t.PromiseStatus(new PromiseInfo("balanced_budget", 3, 0, 0, false, Carried: false, JudgedIn: 21));
        Assert.Equal("Judged at the election after the coming term (in 21 quarters). Now: not met.", fresh[0]);
        Assert.Contains("at least half of the next term", fresh[1]);
        var lines = t.PromiseStatus(new PromiseInfo("balanced_budget", 3, 2, 2, false, Carried: true, JudgedIn: 5));
        Assert.Equal("Judged at the next election (in 5 quarters). Now: not met.", lines[0]);
        Assert.Contains("costs you trust", lines[1]);
        var free = t.PromiseStatus(new PromiseInfo("balanced_budget", 3, 4, 1, true, Carried: true, JudgedIn: 1));
        Assert.Contains("Now: met", free[0]);
        Assert.Contains("would not cost you", free[1]);
        foreach (var lang in new[] { "cs", "en" })
            Assert.DoesNotContain("{", string.Join(" ", For(lang).PromiseStatus(new PromiseInfo("balanced_budget", 3, 0, 0, false, false, 9))));
    }

    public static readonly string[] Topics = { "capital", "organisation", "credibility", "cohesion", "relations", "fiscal_blame", "campaign", "organise", "court",
        "criticize", "stance", "promise", "priority", "shift", "propose", "announce", "proposals", "reshuffle", "exits", "fiscal_credit", "ultimatum", "negotiation" };

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
        Assert.Contains("0.35", en.Explain("fiscal_credit"));
    }

    [Fact]
    public void AnUltimatumPreviewSaysWhatTheLeaderWouldDoAndWhatIsAtStake()
    {
        var t = For("en");
        var p = new UltimatumPreview(null, null, "base:pensions", 9, 10, 4, "stall", null, 2, true, true, 0, 0.45);
        var lines = t.UltimatumPreviewLines(p, "base:social_alliance");
        Assert.StartsWith("Asks: Pensions", lines[0]);
        Assert.Contains("give in after about 2 quarter", lines[1]);
        Assert.Contains("could bring in another party", lines[2]);
        Assert.Contains("leaves the government", lines[3]);
        Assert.Contains("refuse", t.UltimatumPreviewLines(p with { Answer = "refuse", Reason = "red_line", DeliversAfter = null }, "base:social_alliance")[1]);
        Assert.Single(t.UltimatumPreviewLines(p with { ErrorKey = "error.ultimatum_cooldown", Detail = "3" }, "base:social_alliance"));
        foreach (var lang in new[] { "cs", "en" })
        {
            var x = For(lang);
            var all = string.Join(" ", x.UltimatumPreviewLines(p, "base:social_alliance"))
                + x.UltimatumStatus(new PlayerUltimatumInfo("demand", "pensions", "base:pensions", 9, 10, 2, 3, null), "base:social_alliance")
                + x.UltimatumStatus(new PlayerUltimatumInfo("demand", "pensions", "base:pensions", 9, 10, 2, 3, "refuse"), "base:social_alliance")
                + x.UltimatumStatus(new PlayerUltimatumInfo("demand", "pensions", "base:pensions", 9, 10, 2, 3, "stall"), "base:social_alliance");
            foreach (var e in new PoliticalEvent[] { new UltimatumAnswered(3, "base:social_alliance", "deliver", null, "base:pensions", 10, 2),
                         new UltimatumAnswered(3, "base:social_alliance", "stall", null, "base:pensions", 10, 2),
                         new UltimatumAnswered(3, "base:social_alliance", "refuse", "red_line", "base:pensions", 10, 2),
                         new UltimatumAnswered(3, "base:social_alliance", "refuse", "score", "base:pensions", 10, 2),
                         new UltimatumDelivered(3, "base:social_alliance", "base:pensions", 10), new UltimatumCarriedOut(3, "base:social_alliance", "base:pensions"),
                         new UltimatumLapsed(3) })
            {
                var text = x.Event(e, Role.Partner);
                Assert.False(string.IsNullOrEmpty(text) || text.StartsWith("ui."), $"{lang}: {e}");
                all += text;
            }
            Assert.DoesNotContain("{", all);
        }
    }

    [Fact]
    public void NegotiationTextsSayWhyWithNumbersInBothLanguages()
    {
        var v = new PartnerView("base:peoples_union", 18, 3, 3.6, 2.0, 0.55, "counter", "price", null, 1.6, 0.08, 0.94, 0.98, 0.95, false, 0.3, 0,
            Array.Empty<DemandView>(), Array.Empty<PortfolioView>(), 0, new CounterTerms(Array.Empty<AgreedDemand>(), new[] { new PortfolioGrant("base:peoples_union", "base:ministry_health") }),
            new[] { new Fix("give_portfolio", "base:peoples_union", null, "base:ministry_health", 1.5), new Fix("concede", "base:peoples_union", null, null, 2), new Fix("court", "base:peoples_union"),
                new Fix("wait", "base:civic_right") });
        Assert.Contains("1.6 short", For("en").NegotiationReason(v));
        foreach (var lang in new[] { "cs", "en" })
        {
            var t = For(lang);
            var all = t.NegotiationReason(v) + t.PriceBreakdown(v) + t.CounterText(v.Counter!) + string.Join(" ", v.Fixes.Select(t.FixText))
                + t.NegotiationReason(v with { Reason = "distance" }) + t.NegotiationReason(v with { Reason = "grudge", ReasonDemand = "base:civic_right" })
                + t.CounterPreviewText(new CounterPreview(false, "platform", 3, 3, 4, null), "base:social_alliance")
                + t.Event(new CounterOfferAnswered(3, "base:social_alliance", false, "too_much"), Role.Opposition)
                + t.Verdict("refuse") + t.DemandStatus("negotiable");
            Assert.DoesNotContain("{", all);
            Assert.DoesNotContain("ui.", all);
        }
    }

    [Fact]
    public void FiscalCreditIsNewsWithCapitalAndPoints()
    {
        var e = new FiscalCreditEarned(6, "base:social_alliance", 0.6, 0.4, 4.8, 3.9);
        Assert.Equal("+0.6 capital, +0.4 pts for cutting the deficit (4.8 → 3.9 % of GDP).", For("en").Event(e, Role.Leader));
        Assert.DoesNotContain("{", For("cs").Event(e, Role.Partner));
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
