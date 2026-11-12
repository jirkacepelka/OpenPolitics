using OpenPolitics.Cli.Play;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Play;

public class PlayPartyTests
{
    private static PackSet Packs()
    {
        string Scenario(string id, string player) =>
            $"- id: test:{id}\n  country: base:czechia\n  start_year: 2015\n  turns_per_year: 4\n  politics:\n    player_party: base:{player}\n" +
            "    support: {base:citizens_movement: 0.30, base:social_alliance: 0.17, base:civic_right: 0.13, base:peoples_union: 0.09, base:workers_left: 0.12, base:homeland_first: 0.07, base:green_wave: 0.06, base:open_net: 0.06}\n" +
            "    government: [base:citizens_movement]\n    leader: base:citizens_movement\n    turns_to_election: 1\n";
        var files = new Dictionary<string, string>
        {
            ["manifest.yaml"] = "id: test\nversion: 0.1.0\nlicense: CC-BY-SA-4.0\nillustrative: true\ndependencies: [base]\n",
            ["scenarios/s.yaml"] = Scenario("leads", "citizens_movement") + Scenario("watch", "green_wave"),
        };
        var r = PackLoader.Load(new IPackSource[]
        {
            new DirectoryPackSource(Path.Combine(BasePackTests.RepoRoot(), "data", "base")),
            new InMemoryPackSource("test", files),
        });
        Assert.True(r.Ok, string.Join("\n", r.Diagnostics));
        return r.Packs!;
    }

    private static string Play(string scenario, string input, string lang = "en")
    {
        var output = new StringWriter();
        Assert.Equal(0, new PlayLoop(Packs(), scenario, 1, lang, 80, false, new StringReader(input), output).Run());
        return output.ToString();
    }

    private static string Quarters(int n) => string.Concat(Enumerable.Repeat("\n", n));

    [Fact]
    public void PartyPanelShowsResourcesRelationsAndGoals()
    {
        var o = Play("test:watch", "party\nquit\n");
        Assert.Contains("YOUR PARTY", o);
        Assert.Contains("Political capital", o);
        Assert.Contains("Organisation", o);
        Assert.Contains("Relations", o);
        Assert.Contains("Social Alliance", o);
        Assert.Contains("Poll standing", o);
    }

    [Fact]
    public void CampaignAndPlatformWork()
    {
        var o = Play("test:watch", "campaign low_income 1\nplatform environment_regulation 9\nquit\n");
        Assert.Contains("Campaign started", o);
        Assert.Contains("Your platform moved", o);
    }

    [Fact]
    public void CourtAndOrganizeWork()
    {
        Assert.Contains("Relations with Social Alliance improved", Play("test:watch", "court social_alliance\nquit\n"));
        Assert.Contains("organisation is stronger", Play("test:watch", "organize\nquit\n"));
    }

    [Fact]
    public void NotEnoughCapitalIsExplained()
    {
        var o = Play("test:watch", "court social_alliance\ncourt civic_right\ncourt workers_left\nquit\n");
        Assert.Contains("Not enough political capital", o);
    }

    [Fact]
    public void CriticizeIsRefusedForTheLeaderAndWorksInOpposition()
    {
        Assert.Contains("only parties outside the government", Play("test:leads", "criticize gdp\nquit\n").ToLowerInvariant());
        var o = Play("test:watch", Quarters(6) + "criticize gdp\nquit\n");
        Assert.True(o.Contains("You criticised") || o.Contains("no effect", StringComparison.OrdinalIgnoreCase), o[^600..]);
    }

    [Fact]
    public void StanceOnAMeasureOfTheGovernment()
    {
        var o = Play("test:watch", Quarters(8) + "party\nstance 1 support\nquit\n");
        Assert.Contains("Stance taken.", o);
    }

    [Fact]
    public void PartyTextsExistInCzech()
    {
        var o = Play("test:watch", "party\nquit\n", "cs");
        Assert.Contains("VAŠE STRANA", o);
        Assert.Contains("Politický kapitál", o);
    }
}
