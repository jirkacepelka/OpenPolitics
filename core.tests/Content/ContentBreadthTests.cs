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
            Assert.True(fromEdges.Contains(p.Id) || p.CostPerUnit != 0 || p.RevenueBaseValueId != null, $"{p.Id} does nothing");
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
}
