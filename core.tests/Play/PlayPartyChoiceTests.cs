using OpenPolitics.Cli.Commands;
using OpenPolitics.Cli.Play;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Play;

/// <summary>The CLI's --party flag for play, run and batch (decision 0013).</summary>
public class PlayPartyChoiceTests
{
    private static readonly string BaseDir = Path.Combine(BasePackTests.RepoRoot(), "data", "base");
    private static readonly PackSet Packs = BasePackTests.LoadBase().Packs!;

    [Fact]
    public void PlayAsAnOppositionPartyShowsItAsYoursAndItsRole()
    {
        var output = new StringWriter();
        Assert.Equal(0, new PlayLoop(Packs, "base:cz_2015", 1, "en", 80, false, new StringReader("party\nquit\n"), output, "base:civic_right").Run());
        string text = output.ToString();
        Assert.Contains("Civic Right", text.Split('\n').First(l => l.Contains("<-")));
        Assert.Contains("(opposition)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayCommandAcceptsAShortPartyIdAndRejectsUnknownOnes()
    {
        var w = new StringWriter();
        Assert.Equal(0, PlayCommand.Execute(new[] { "--packs", BaseDir, "--party", "green_wave", "--lang", "en", "--no-tips" }, new StringReader("quit\n"), w));
        Assert.Contains("Green Wave", w.ToString().Split('\n').First(l => l.Contains("<-")));
        var bad = new StringWriter();
        Assert.Equal(2, PlayCommand.Execute(new[] { "--packs", BaseDir, "--party", "nobody" }, new StringReader(""), bad));
        Assert.Contains("Unknown party 'nobody'", bad.ToString());
    }

    [Fact]
    public void PlayPartyListShowsEveryPartyWithItsRole()
    {
        var w = new StringWriter();
        Assert.Equal(0, PlayCommand.Execute(new[] { "--packs", BaseDir, "--party", "list", "--lang", "en" }, new StringReader(""), w));
        var lines = w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(Packs.Parties.Count, lines.Length);
        Assert.Contains(lines, l => l.Contains("base:social_alliance") && l.Contains("lead the government"));
    }

    [Fact]
    public void RunWithAPartyRefusesAScenarioWithoutPolitics()
    {
        var w = new StringWriter();
        Assert.Equal(2, RunCommand.Execute(new[] { "--packs", BaseDir, "--scenario", "base:cz_2015_sandbox", "--party", "civic_right" }, w));
        Assert.Contains("has no politics", w.ToString());
        Assert.Equal(0, RunCommand.Execute(new[] { "--packs", BaseDir, "--scenario", "base:cz_2015", "--party", "civic_right", "--turns", "2" }, new StringWriter()));
    }

    [Fact]
    public void BatchForAllPartiesPrintsOneRowPerParty()
    {
        var w = new StringWriter();
        int code = BatchCommand.Execute(new[] { "--packs", BaseDir, "--politics", "--games", "2", "--turns", "20", "--strategy", "keeper", "--party", "all" }, w);
        Assert.Equal(0, code);
        foreach (var p in Packs.Parties) Assert.Contains(p.Id, w.ToString());
        var one = new StringWriter();
        Assert.Equal(0, BatchCommand.Execute(new[] { "--packs", BaseDir, "--politics", "--games", "2", "--turns", "20", "--party", "open_net" }, one));
        Assert.Contains("Player party: base:open_net", one.ToString());
    }
}
