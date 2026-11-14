using OpenPolitics.Cli.Play;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Play;

public class PlayCoalitionActionsTests
{
    private static PackSet Packs()
    {
        string Scenario(string id, string player, bool slim = false) =>
            $"- id: test:{id}\n  country: base:czechia\n  start_year: 2015\n  turns_per_year: 4\n  politics:\n    player_party: base:{player}\n" +
            "    support: {base:citizens_movement: 0.30, base:social_alliance: 0.17, base:civic_right: 0.13, base:peoples_union: 0.09, base:workers_left: 0.12, base:homeland_first: 0.07, base:green_wave: 0.06, base:open_net: 0.06}\n" +
            "    government: [base:citizens_movement, base:social_alliance, " + (slim ? "" : "base:civic_right, ") + "base:peoples_union]\n    leader: base:citizens_movement\n    turns_to_election: 20\n";
        var files = new Dictionary<string, string>
        {
            ["manifest.yaml"] = "id: test\nversion: 0.1.0\nlicense: CC-BY-SA-4.0\nillustrative: true\ndependencies: [base]\n",
            ["scenarios/s.yaml"] = Scenario("leads", "citizens_movement") + Scenario("partner", "peoples_union") + Scenario("slim", "citizens_movement", slim: true) + Scenario("watch", "green_wave"),
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

    [Fact]
    public void HelpMentionsTheNewCommands()
    {
        var o = Play("test:watch", "help\nquit\n");
        Assert.Contains("propose", o);
        Assert.Contains("leave", o);
        Assert.Contains("dismiss", o);
        Assert.Contains("confidence", o);
    }

    [Fact]
    public void APartnerCanProposeAPolicyChange()
    {
        var o = Play("test:partner", "propose vat 14\nquit\n");
        Assert.Contains("passed on to the leader", o);
        Assert.Contains("answer", o);
    }

    [Fact]
    public void ProposeUsageAndWrongRoleAreExplained()
    {
        Assert.Contains("Usage: propose", Play("test:partner", "propose\nquit\n"));
        Assert.Contains("Only a governing partner", Play("test:leads", "propose vat 14\nquit\n"));
        Assert.Contains("Only a governing partner", Play("test:watch", "propose vat 14\nquit\n"));
    }

    [Fact]
    public void LeaveAnnouncesAndHappensAtTheEndOfTheQuarter()
    {
        var o = Play("test:partner", "leave\n\nquit\n");
        Assert.Contains("You will leave the government", o);
        Assert.Contains("Your party left the government.", o);
    }

    [Fact]
    public void DismissWorksWhileTheMajorityHoldsAndIsRefusedOtherwise()
    {
        Assert.Contains("will be dismissed", Play("test:leads", "dismiss peoples_union\nquit\n"));
        Assert.Contains("would lose its majority", Play("test:slim", "dismiss peoples_union\nquit\n"));
        Assert.Contains("Only the leading party", Play("test:partner", "dismiss civic_right\nquit\n"));
    }

    [Fact]
    public void IndexingSpendingWorksFromTheCommandLine()
    {
        Assert.Contains("now follows", Play("test:leads", "index 7 inflation\nquit\n"));
        Assert.Contains("Only spending can be indexed", Play("test:leads", "index 3 inflation\nquit\n"));
    }

    [Fact]
    public void ConfidenceNeedsCapitalAndTheOpposition()
    {
        Assert.Contains("Not enough political capital", Play("test:watch", "confidence\nquit\n"));
        Assert.Contains("Only parties outside the government", Play("test:leads", "confidence\nquit\n"));
    }

    [Fact]
    public void TheCzechTextsExist()
    {
        var o = Play("test:partner", "propose vat 14\nquit\n", "cs");
        Assert.Contains("předán vedoucí straně", o);
    }
}
