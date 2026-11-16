using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Presentation;

/// <summary>
/// Decision 0015 (5): an opposition player is never addressed as the government. Every text key is either neutral, leader-only
/// (<see cref="RoleTexts.LeaderOnly"/>) or has an <c>_opposition</c> variant; and the texts produced for an opposition player contain none of the
/// phrases that address the government.
/// </summary>
public class RoleTextsTests
{
    private static readonly Core.Packs.PackSet Packs = BasePackTests.LoadBase().Packs!;

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    public void NoKeyAnOppositionPlayerCanSeeAddressesThemAsTheGovernment(string lang)
    {
        var table = Packs.Localization[lang];
        var bad = new List<string>();
        foreach (var (key, text) in table.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!key.StartsWith("ui.", StringComparison.Ordinal) || key.EndsWith("_partner", StringComparison.Ordinal) || RoleTexts.IsLeaderOnly(key)) continue;
            if (table.ContainsKey(key + "_opposition")) continue; // the opposition reads the variant
            foreach (var phrase in RoleTexts.Forbidden(lang))
                if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase)) bad.Add($"{key}: '{phrase}'");
        }
        Assert.True(bad.Count == 0, "texts that address an opposition player as the government:\n" + string.Join("\n", bad));
    }

    public static IEnumerable<object[]> Events()
    {
        var gov = new Government("base:social_alliance", new[] { "base:social_alliance", "base:peoples_union" }, Array.Empty<Commitment>(), 0);
        yield return new object[] { new Ultimatum(4, "base:peoples_union", null, 2) };
        yield return new object[] { new Ultimatum(4, "base:peoples_union", Packs.Parties.First(p => p.Id == "base:peoples_union").Demands[0].Id, 2) };
        yield return new object[] { new LoyaltyWarning(4, "base:peoples_union", null) };
        yield return new object[] { new DemandRaised(4, "base:peoples_union", Packs.Parties.First(p => p.Id == "base:peoples_union").Demands[0].Id, 8) };
        yield return new object[] { new DemandFailed(4, "base:peoples_union", Packs.Parties.First(p => p.Id == "base:peoples_union").Demands[0].Id) };
        yield return new object[] { new GovernmentFormed(4, gov) };
        yield return new object[] { new PartnerLeft(4, "base:peoples_union") };
        yield return new object[] { new GovernmentFell(4) };
        yield return new object[] { new OfferLapsed(4) };
        yield return new object[] { new GovernmentMeasure(4, "base:pensions", 9, 9.5) };
        yield return new object[] { new CoalitionReshuffled(4, "base:social_alliance", new[] { "base:peoples_union" }, Array.Empty<string>()) };
        yield return new object[] { new PartnerDismissed(4, "base:peoples_union") };
        yield return new object[] { new UltimatumCarriedOut(4, "base:social_alliance", "base:pensions") }; // decision 0016: read in opposition afterwards
        yield return new object[] { new UltimatumLapsed(4) };
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void EventsShownToAnOppositionPlayerDoNotAddressItAsTheGovernment(PoliticalEvent e)
    {
        foreach (var lang in new[] { "en", "cs" })
        {
            var text = new Texts(Packs, new Localizer(Packs, lang)).Event(e, Role.Opposition);
            Assert.NotNull(text);
            foreach (var phrase in RoleTexts.Forbidden(lang)) Assert.DoesNotContain(phrase, text!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ScreenTextsAndExplanationsForTheOppositionDoNotAddressItAsTheGovernment()
    {
        foreach (var lang in new[] { "en", "cs" })
        {
            var t = new Texts(Packs, new Localizer(Packs, lang));
            var l = new Localizer(Packs, lang);
            var texts = Texts.RoleScreenKeys.Select(k => l.T(t.RoleKey(k, Role.Opposition)))
                .Concat(TextsTests.Topics.Select(t.Explain)).Append(t.GoalsHeading(Role.Opposition)).Append(l.T("ui.role.opposition")).ToList();
            foreach (var text in texts)
                foreach (var phrase in RoleTexts.Forbidden(lang)) Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AnUltimatumIsWordedForTheRole()
    {
        var t = new Texts(Packs, new Localizer(Packs, "en"));
        var u = new Ultimatum(4, "base:peoples_union", null, 2);
        Assert.Contains("gives you an ultimatum", t.Event(u, Role.Leader));
        Assert.Contains("the government", t.Event(u, Role.Opposition));
        Assert.Contains("the government", t.Event(u, Role.Partner));
    }
}
