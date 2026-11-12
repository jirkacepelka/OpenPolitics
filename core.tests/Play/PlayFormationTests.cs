using OpenPolitics.Cli.Play;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Play;

public class PlayFormationTests
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
            ["scenarios/s.yaml"] = Scenario("leads", "citizens_movement") + Scenario("invited", "civic_right"),
        };
        var r = PackLoader.Load(new IPackSource[]
        {
            new DirectoryPackSource(Path.Combine(BasePackTests.RepoRoot(), "data", "base")),
            new InMemoryPackSource("test", files),
        });
        Assert.True(r.Ok, string.Join("\n", r.Diagnostics));
        return r.Packs!;
    }

    private static string Play(string scenario, string input, string lang = "en", ulong seed = 1)
    {
        var output = new StringWriter();
        Assert.Equal(0, new PlayLoop(Packs(), scenario, seed, lang, 80, false, new StringReader(input), output).Run());
        return output.ToString();
    }

    [Fact]
    public void ForceElectionShowsFormationScreen()
    {
        var o = Play("test:leads", "\nquit\n");
        Assert.Contains("ELECTION RESULT", o);
        Assert.Contains("YOU ARE FORMING THE GOVERNMENT", o);
        Assert.Contains("auto", o);
    }

    [Fact]
    public void CoalitionScreenListsPartiesDemandsAndPrices()
    {
        var o = Play("test:leads", "\ncoalition\nquit\n");
        Assert.Contains("GOVERNMENT FORMATION", o);
        Assert.Contains("Majority: more than 100 seats", o);
        Assert.Contains("price", o);
        Assert.Contains("weight 3", o);
    }

    [Fact]
    public void AutoProposesAndGovernmentFormsInTheClient()
    {
        var o = Play("test:leads", "\nauto\n\nquit\n");
        Assert.Contains("Coalition formed.", o);
        Assert.Contains("Government formed:", o);
        Assert.Contains("Government parties hold", o);
    }

    [Fact]
    public void RefusalIsExplainedInTheClient()
    {
        var o = Play("test:leads", "\nsubmit\nquit\n");
        Assert.Contains("does not have a majority", o);
    }

    [Fact]
    public void OfferCanBeAcceptedInTheClient()
    {
        var o = Play("test:invited", "\n\naccept\nquit\n");
        Assert.Contains("OFFER:", o);
        Assert.Contains("You accepted the offer", o);
    }

    [Fact]
    public void FormationTextsExistInCzech()
    {
        var o = Play("test:leads", "\ncoalition\nquit\n", "cs");
        Assert.Contains("SESTAVUJETE VLÁDU", o);
        Assert.Contains("SESTAVOVÁNÍ VLÁDY", o);
    }

    [Fact]
    public void PolicyChangesAreRefusedWithAnExplanationDuringFormation()
    {
        var o = Play("test:leads", "\nset 1 30\nquit\n");
        Assert.Contains("no majority right now", o);
    }
}
