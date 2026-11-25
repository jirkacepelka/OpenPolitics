using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0020: a game can start from a scattered poll and a government formed from the seats; the player's programme starts lean.</summary>
public class RandomStartTests
{
    private static readonly OpenPolitics.Core.Packs.PackSet Packs = Content.BasePackTests.LoadBase().Packs!;
    private static CountryPolitics Start(ulong seed, bool random = true, string? party = null) =>
        Simulation.NewGame(Packs, "base:cz_2015", seed, party, random).State.Countries.Values.Single().Politics!;

    [Fact]
    public void WithoutTheFlagTheStartIsTheScenariosOwn()
    {
        var pol = Start(3, random: false);
        Assert.Equal("base:social_alliance", pol.Leader);
        Assert.Equal(0.30, pol.Support["base:citizens_movement"], 6);
    }

    [Fact]
    public void ThePollIsScatteredFromSeedToSeedAndStaysAProbabilityDistribution()
    {
        var shares = Enumerable.Range(1, 12).Select(s => Start((ulong)s).Support["base:citizens_movement"]).ToList();
        Assert.True(shares.Max() - shares.Min() > 0.05, $"{shares.Min():P1} .. {shares.Max():P1}");
        for (ulong s = 1; s <= 12; s++) Assert.Equal(1.0, Start(s).Support.Values.Sum(), 6);
        Assert.Equal(Start(5).Support.OrderBy(k => k.Key).Select(k => k.Value), Start(5).Support.OrderBy(k => k.Key).Select(k => k.Value)); // the same seed, the same start
    }

    [Fact]
    public void TheGovernmentIsNotAlwaysTheSame()
    {
        var leaders = Enumerable.Range(1, 40).Select(s => Start((ulong)s).Leader).Distinct().ToList();
        Assert.True(leaders.Count >= 3, string.Join(",", leaders));
        var coalitions = Enumerable.Range(1, 40).Select(s => string.Join("+", Start((ulong)s).GovernmentParties)).Distinct().Count();
        Assert.True(coalitions >= 4, coalitions.ToString());
        for (ulong s = 1; s <= 40; s++)
        {
            var pol = Start(s);
            int total = pol.Seats.Values.Sum();
            Assert.True(pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p)) > total / 2, $"seed {s}: no majority");
        }
    }

    [Fact]
    public void ThePartyChoiceFollowsTheSeed()
    {
        var a = PartyChoice.Options(Packs, "base:cz_2015", 1, true);
        var roles = Enumerable.Range(1, 20).Select(s => string.Join(",", PartyChoice.Options(Packs, "base:cz_2015", (ulong)s, true).Select(o => o.Role))).Distinct().Count();
        Assert.True(roles > 1);
        Assert.Single(a, o => o.Role == Role.Leader);
    }

    [Fact]
    public void ARandomStartIsSavedAndReplayed()
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015", 9, "base:green_wave", true);
        g.EndTurn(); g.EndTurn();
        var loaded = Simulation.Load(Packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(g.State.Countries.Values.Single().Politics!.Leader, loaded.State.Countries.Values.Single().Politics!.Leader);
        Assert.Equal(g.State.Countries.Values.Single().Politics!.Support["base:green_wave"], loaded.State.Countries.Values.Single().Politics!.Support["base:green_wave"], 12);
    }

    [Fact]
    public void ThePlayersProgrammeStartsLean()
    {
        foreach (var party in Packs.Parties)
        {
            var g = Simulation.NewGame(Packs, "base:cz_2015", 1, party.Id);
            var platform = g.State.Countries.Values.Single().Politics!.Party!.Platform;
            Assert.Equal(PartyRules.StartProgramme, platform.Count);
            // the six it stresses most
            var top = party.Platform.OrderByDescending(kv => kv.Value.Weight).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(PartyRules.StartProgramme).Select(kv => kv.Key).OrderBy(x => x);
            Assert.Equal(top, platform.Keys.OrderBy(x => x));
        }
    }

    [Fact]
    public void ChangesToTheProgrammeCostNoCapital()
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015", 1, "base:green_wave");
        double pc = g.State.Countries.Values.Single().Politics!.Party!.PoliticalCapital;
        Assert.True(g.Apply(new AddToPlatform("base:union_rights")).Accepted);
        Assert.True(g.Apply(new RemoveFromPlatform("base:union_rights")).Accepted);
        Assert.Equal(pc, g.State.Countries.Values.Single().Politics!.Party!.PoliticalCapital, 12);
    }

    [Theory]
    [InlineData("base:cz_2015")]
    [InlineData("base:cz_2015_districts")]
    public void RandomStartGamesRunForTwentyYearsWithAGovernmentMostOfTheTime(string scenario)
    {
        int governing = 0, quarters = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var g = Simulation.NewGame(Packs, scenario, seed, null, true);
            for (int i = 0; i < 80; i++)
            {
                var before = g.State.Countries.Values.Single().Politics!;
                if (before.Formation is { PlayerIsFormateur: true } && g.SuggestCoalition() is { } suggested) g.Apply(ProposeCoalition.From(suggested)); // a player who forms and accepts what is offered
                else if (before.Formation?.Offer != null) g.Apply(new AcceptOffer());
                g.EndTurn();
                var pol = g.State.Countries.Values.Single().Politics!;
                quarters++;
                if (pol.Phase == Phase.Governing) governing++;
                Assert.Equal(1.0, pol.Support.Values.Sum(), 6);
            }
        }
        Assert.True(governing > quarters * 0.85, $"{governing}/{quarters} quarters with a sitting government");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryPartnerOfTheStartingGovernmentHoldsAMinistry(bool random)
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015", 4, null, random);
        var gov = g.State.Countries.Values.Single().Politics!.Government!;
        var holders = gov.PortfolioHolders;
        foreach (var partner in gov.Parties.Where(p => p != gov.Leader))
            Assert.Contains(partner, holders.Values);
        Assert.True(holders.Count <= g.Portfolios.Count * 2 / 3);
        Assert.All(holders.Keys, id => Assert.Contains(g.Portfolios, p => p.Id == id));
    }
}
