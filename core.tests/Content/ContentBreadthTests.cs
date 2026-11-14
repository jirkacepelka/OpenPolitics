using Xunit;

namespace OpenPolitics.Core.Tests.Content;

/// <summary>Wave 4: the base pack is broad enough to play for 20+ years and every lever actually does something.</summary>
public class ContentBreadthTests
{
    private static readonly Core.Packs.PackSet Packs = BasePackTests.LoadBase().Packs!;

    [Fact]
    public void ThereAreAtLeastThirtyFivePoliciesAndTwentyFiveEvents()
    {
        Assert.True(Packs.Policies.Count >= 35, $"{Packs.Policies.Count} policies");
        Assert.True(Packs.Events.Count >= 25, $"{Packs.Events.Count} events");
    }

    [Fact]
    public void EveryPolicyActsOnTheEconomyOrTheBudget()
    {
        var fromEdges = Packs.Edges.Select(e => e.From).ToHashSet();
        foreach (var p in Packs.Policies)
            Assert.True(fromEdges.Contains(p.Id) || p.CostPerUnit != 0 || p.RevenueBaseValueId != null || p.Unit is "electoral_method" || p.Id == "base:electoral_threshold", $"{p.Id} does nothing");
    }

    [Fact]
    public void EveryPolicyMattersToSomeVotersOrParties()
    {
        var referenced = Packs.VoterGroups.SelectMany(g => g.PolicyPrefs.Keys)
            .Concat(Packs.Parties.SelectMany(p => p.Platform.Keys)).ToHashSet();
        foreach (var p in Packs.Policies) Assert.True(referenced.Contains(p.Id), $"{p.Id} matters to nobody");
    }

    [Fact]
    public void EveryPartyHasDemandsOnTheNewLevers()
    {
        var original = new HashSet<string>(new[] { "pensions", "health_spending", "education_spending", "police", "research", "social_benefits",
            "infrastructure", "administration", "income_tax", "vat", "corporate_tax", "migration", "environment_regulation" }.Select(x => "base:" + x));
        foreach (var party in Packs.Parties)
            Assert.Contains(party.Demands, d => d.Kind == "policy" && !original.Contains(d.Subject));
    }

    [Fact]
    public void EveryEventTextAndPolicyTextExistsInBothLanguages()
    {
        foreach (var lang in new[] { "cs", "en" })
        {
            var t = new OpenPolitics.Core.Presentation.Localizer(Packs, lang);
            foreach (var e in Packs.Events) Assert.NotEqual(e.TextKey, t.T(e.TextKey));
            foreach (var p in Packs.Policies) Assert.NotEqual("name." + p.Id, t.Name(p.Id));
        }
    }

    [Fact]
    public void NoNewDemandIsMetByTheStartingSettings()
    {
        var original = new HashSet<string>(new[] { "pensions", "health_spending", "education_spending", "police", "research", "social_benefits",
            "infrastructure", "administration", "income_tax", "vat", "corporate_tax", "migration", "environment_regulation" }.Select(x => "base:" + x));
        var defaults = Packs.Policies.ToDictionary(p => p.Id, p => p.Default);
        foreach (var party in Packs.Parties)
            foreach (var d in party.Demands.Where(d => d.Kind == "policy" && !original.Contains(d.Subject)))
            {
                bool met = d.Op == ">=" ? defaults[d.Subject] >= d.Target : defaults[d.Subject] <= d.Target;
                Assert.False(met, $"{party.Id}:{d.Id} is met by the default setting");
            }
    }

    [Fact]
    public void EventsOnlyShockStateThatPersists()
    {
        // These values are recomputed by the economy model every turn, so a one-off change would be wiped out and then rebound.
        var recomputed = new[] { "base:gdp", "base:interest_rate", "base:revenue", "base:spending", "base:price_level", "base:gdp_nominal" };
        foreach (var e in Packs.Events.Where(e => e.Id != "base:recession"))
            foreach (var effect in e.Effects) Assert.DoesNotContain(effect.ValueId, recomputed);
    }
}
