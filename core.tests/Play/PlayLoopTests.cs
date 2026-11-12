using OpenPolitics.Cli.Play;
using OpenPolitics.Core.Tests.Content;

namespace OpenPolitics.Core.Tests.Play;

public class PlayLoopTests
{
    private static string Play(string input, string lang = "en", int turns = 80, ulong seed = 1, bool tips = true)
    {
        var packs = BasePackTests.LoadBase().Packs!;
        var output = new StringWriter();
        var loop = new PlayLoop(packs, "base:cz_2015_sandbox", seed, lang, turns, tips, new StringReader(input), output);
        Assert.Equal(0, loop.Run());
        return output.ToString();
    }

    private static string Quarters(int n) => string.Concat(Enumerable.Repeat("\n", n));

    [Fact]
    public void StartScreenShowsOverviewPoliciesAndFirstTip()
    {
        var o = Play("quit\n");
        Assert.Contains("OVERVIEW", o);
        Assert.Contains("Economy (GDP)", o);
        Assert.Contains("4.60 tn CZK", o);
        Assert.Contains("Unemployment", o);
        Assert.Contains("5.0 %", o);
        Assert.Contains("YOUR POLICIES", o);
        Assert.Contains("Income tax and contributions", o);
        Assert.Contains("Year 2015, quarter 1", o);
        Assert.Contains("Tip: you run the country", o);
    }

    [Fact]
    public void EmptyLineEndsTheQuarter()
    {
        var o = Play(Quarters(2) + "quit\n");
        Assert.Contains("Year 2015, quarter 2", o);
        Assert.Contains("Year 2015, quarter 3", o);
        Assert.Contains("Tip: changes take time", o);
    }

    [Fact]
    public void PoliciesAreGroupedTaxesFirstThenSpendingThenRegulation()
    {
        var o = Play("list\nquit\n");
        int tax = o.IndexOf("Income tax and contributions", StringComparison.Ordinal);
        int spending = o.IndexOf("Pensions", StringComparison.Ordinal);
        int regulation = o.IndexOf("Environmental regulation", StringComparison.Ordinal);
        Assert.InRange(tax, 1, spending - 1);
        Assert.InRange(spending, 1, regulation - 1);
        Assert.Contains("1. ", o);
    }

    [Fact]
    public void SetSchedulesAChangeAndListShowsItPending()
    {
        var o = Play("set income 26\nlist\nquit\n");
        Assert.Contains("Scheduled: Income tax and contributions -> 26.0 % of GDP.", o);
        Assert.Contains("-> 26.0 % of GDP in 2 quarters", o);   // income tax has delay_turns: 1
    }

    [Fact]
    public void SetAcceptsAPolicyNumber()
    {
        var o = Play("set 1 3\nquit\n");
        Assert.Contains("Scheduled:", o);
        Assert.Contains("-> 3.0", o);
    }

    [Fact]
    public void SetAcceptsACommaAsDecimalSeparator()
    {
        Assert.Contains("-> 26.5", Play("set income 26,5\nquit\n"));
    }

    [Fact]
    public void OutOfRangeValueIsRefusedPolitely()
    {
        var o = Play("set income 99\nquit\n");
        Assert.Contains("outside the allowed range", o);
        Assert.DoesNotContain("Scheduled", o);
    }

    [Theory]
    [InlineData("set zzz 5", "I could not find 'zzz'")]
    [InlineData("set income abc", "'abc' is not a number")]
    [InlineData("set", "Usage: set N VALUE")]
    [InlineData("dance", "I did not understand")]
    [InlineData("why zzz", "I could not find 'zzz'")]
    public void BadInputGetsAHelpfulMessage(string command, string expected)
    {
        Assert.Contains(expected, Play(command + "\nquit\n"));
    }

    [Fact]
    public void HelpListsTheCommands()
    {
        var o = Play("?\nquit\n");
        Assert.Contains("HELP", o);
        Assert.Contains("set N VALUE", o);
        Assert.Contains("why NAME", o);
    }

    [Fact]
    public void VotersScreenShowsEveryGroup()
    {
        var o = Play("voters\nquit\n");
        Assert.Contains("VOTERS", o);
        Assert.Contains("Pensioners", o);
        Assert.Contains("Young urban voters", o);
    }

    [Fact]
    public void ValuesScreenListsWhatTheCountryMeasures()
    {
        var o = Play("values\nquit\n");
        Assert.Contains("Public debt", o);
        Assert.Contains("Inequality", o);
    }

    [Fact]
    public void WhyExplainsWhatPushedAValue()
    {
        var o = Play("set health 12\n" + Quarters(8) + "why health\nquit\n");
        Assert.Contains("WHY DID IT CHANGE: Public health", o);
        Assert.Contains("Healthcare spending: pushes up", o);
    }

    [Fact]
    public void WhyOnAMacroValueMentionsTheEconomicModel()
    {
        var o = Play(Quarters(1) + "why gdp\nquit\n");
        Assert.Contains("The economic model also moves this value", o);
    }

    [Fact]
    public void InfoExplainsInPlainWords()
    {
        var o = Play("info unemployment\ninfo vat\nquit\n");
        Assert.Contains("Share of the labour force without a job", o);
        Assert.Contains("Easy to collect", o);
    }

    [Fact]
    public void CzechInterfaceAndDiacriticFreeInput()
    {
        var o = Play("proc nezamestnanost\nquit\n", lang: "cs");
        Assert.Contains("PŘEHLED", o);
        Assert.Contains("Rok 2015, 1. čtvrtletí", o);
        Assert.Contains("PROČ SE TO ZMĚNILO: Nezaměstnanost", o);
    }

    [Fact]
    public void GameEndsWithASummaryAfterTheLastTurn()
    {
        var o = Play(Quarters(5), turns: 4);
        Assert.Contains("THE TERM IS OVER", o);
        Assert.Contains("After 1 years", o);
    }

    [Fact]
    public void EndOfInputExitsGracefully()
    {
        Assert.Contains("OVERVIEW", Play(""));
    }

    [Fact]
    public void TipsCanBeSwitchedOff()
    {
        Assert.DoesNotContain("Tip:", Play(Quarters(3) + "quit\n", tips: false));
    }

    [Fact]
    public void SaveAndLoadResumeTheSameGame()
    {
        string file = Path.Combine(Path.GetTempPath(), "op-save-" + Guid.NewGuid().ToString("N") + ".yaml");
        try
        {
            var first = Play("set income 22\n" + Quarters(3) + $"save {file}\nquit\n");
            Assert.Contains("Game saved", first);
            var second = Play($"load {file}\nlist\nquit\n");
            Assert.Contains("Game loaded", second);
            Assert.Contains("Year 2015, quarter 4", second);
            Assert.Contains("22.0 % of GDP", second);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void LoadOfAMissingFileIsAFriendlyError()
    {
        Assert.Contains("Could not use file", Play("load /no/such/file.yaml\nquit\n"));
    }

    [Fact]
    public void SameInputGivesTheSameOutput()
    {
        var script = "set income 28\n" + Quarters(10) + "why unemployment\nvoters\nquit\n";
        Assert.Equal(Play(script, seed: 5), Play(script, seed: 5));
    }

    [Fact]
    public void ABadPolicyChoiceShowsUpInTheOverview()
    {
        // Slash health spending and cut taxes hard for two years: the mood line must not read "delighted".
        var script = "set health 3\nset police 0.5\nset education 2\nset income 8\n" + Quarters(8) + "quit\n";
        var o = Play(script);
        Assert.DoesNotContain("Voter mood                     delighted", o);
        Assert.Contains("Budget balance", o);
    }
}
