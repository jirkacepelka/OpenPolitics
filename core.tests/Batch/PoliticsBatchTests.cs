using OpenPolitics.Core.Batch;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Tests.Content;
using Xunit;
using Xunit.Abstractions;

namespace OpenPolitics.Core.Tests.Batch;

public class PoliticsBatchTests(ITestOutputHelper output)
{
    private static readonly PackSet Packs = BasePackTests.LoadBase().Packs!;
    private const string Scenario = "base:cz_2015";
    private const int Turns = 48; // twelve years

    private PoliticsBatchReport Run(PoliticsStrategy s, int games, int turns = Turns)
    {
        var r = PoliticsBatch.Run(Packs, Scenario, games, turns, s);
        output.WriteLine(r.ToText());
        return r;
    }

    [Theory]
    [InlineData(PoliticsStrategy.Ignorer)]
    [InlineData(PoliticsStrategy.Keeper)]
    [InlineData(PoliticsStrategy.Random)]
    [InlineData(PoliticsStrategy.Extreme)]
    public void StrategiesOnlyUsePublicApiAndNeverThrow(PoliticsStrategy s)
    {
        var r = Run(s, 200);
        Assert.Equal(200, r.Games);
        Assert.Equal(0, r.StuckGames);
    }

    [Fact]
    public void IgnorerCoalitionsLosePartners()
    {
        var r = Run(PoliticsStrategy.Ignorer, 1000);
        Assert.True(r.GamesWithPartnerLoss >= 0.8 * r.Games, $"{r.GamesWithPartnerLoss}/{r.Games} games lost a partner");
    }

    [Fact]
    public void FallsLeadToNewMajorities()
    {
        var r = Run(PoliticsStrategy.Ignorer, 1000);
        Assert.True(r.Falls > 50, $"only {r.Falls} falls");
        Assert.True(r.FallsWithNewMajority >= 0.6 * r.Falls, $"{r.FallsWithNewMajority}/{r.Falls} falls produced a new majority");
    }

    [Fact]
    public void KeeperCoalitionsLastLonger()
    {
        var ignorer = Run(PoliticsStrategy.Ignorer, 500);
        var keeper = Run(PoliticsStrategy.Keeper, 500);
        Assert.True(keeper.MeanTurnsToFirstPartnerLoss >= 1.5 * ignorer.MeanTurnsToFirstPartnerLoss,
            $"keeper {keeper.MeanTurnsToFirstPartnerLoss:0.0} vs ignorer {ignorer.MeanTurnsToFirstPartnerLoss:0.0}");
    }

    [Fact]
    public void BatchIsDeterministic()
    {
        Assert.Equal(PoliticsBatch.Run(Packs, Scenario, 50, Turns, PoliticsStrategy.Keeper).ToText(),
            PoliticsBatch.Run(Packs, Scenario, 50, Turns, PoliticsStrategy.Keeper).ToText());
    }

    [Fact]
    public void CliPoliticsFlagRunsThePoliticsBatch()
    {
        var w = new StringWriter();
        int code = OpenPolitics.Cli.Commands.BatchCommand.Execute(new[]
        {
            "--packs", Path.Combine(BasePackTests.RepoRoot(), "data", "base"), "--politics", "--games", "5", "--turns", "40", "--strategy", "keeper",
        }, w);
        Assert.Equal(0, code);
        Assert.Contains("Politics batch: 5 game(s) x 40 turns, strategy 'keeper'", w.ToString());
    }
}
