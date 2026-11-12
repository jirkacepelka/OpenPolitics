using OpenPolitics.Cli.Play;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Play;

public class PlayCoalitionTests
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
            ["scenarios/s.yaml"] = Scenario("leads", "citizens_movement"),
        };
        var r = PackLoader.Load(new IPackSource[]
        {
            new DirectoryPackSource(Path.Combine(BasePackTests.RepoRoot(), "data", "base")),
            new InMemoryPackSource("test", files),
        });
        Assert.True(r.Ok, string.Join("\n", r.Diagnostics));
        return r.Packs!;
    }

    private static string Play(string input, string lang = "en")
    {
        var output = new StringWriter();
        Assert.Equal(0, new PlayLoop(Packs(), "test:leads", 1, lang, 80, false, new StringReader(input), output).Run());
        return output.ToString();
    }

    private static string Quarters(int n) => string.Concat(Enumerable.Repeat("\n", n));

    [Fact]
    public void CoalitionPanelShowsLoyaltyAndCommitments()
    {
        var o = Play("\nauto\n\ncoalition\nquit\n");
        Assert.Contains("COALITION", o);
        Assert.Contains("loyalty", o);
        Assert.Contains("stable", o);
        Assert.Matches(@"\[(done|on track|late)\]", o);
    }

    [Fact]
    public void OverviewShowsPartnerLoyaltyWhileGoverning()
    {
        var o = Play("\nauto\n\nquit\n");
        Assert.Contains("COALITION", o);
    }

    [Fact]
    public void LoyaltyEventsAreReportedInTheQuarter()
    {
        var o = Play("\nauto\n" + Quarters(14) + "quit\n");
        Assert.Contains("is losing patience", o);
        Assert.Contains("gives you an ultimatum", o);
    }

    [Fact]
    public void FallsAndDeparturesAreReported()
    {
        var o = Play("\nauto\n" + Quarters(30) + "quit\n");
        Assert.Contains("has left the government", o);
    }

    [Fact]
    public void CoalitionTextsExistInCzech()
    {
        var o = Play("\nauto\n" + Quarters(14) + "quit\n", "cs");
        Assert.Contains("KOALICE", o);
        Assert.Contains("ztrácí trpělivost", o);
    }
}
