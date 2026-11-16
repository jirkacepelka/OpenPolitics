using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class MajoritarianSystemTests
{
    private static Dictionary<string, double> V(params (string, double)[] xs) => xs.ToDictionary(x => x.Item1, x => x.Item2);

    private static MajoritarianSystem Fptp(double spread = 0.35, double strategic = 0.0, Func<string, string, double>? dist = null) =>
        new(200, runoff: false, spread, strategic, 2, 0.0, 0.1, dist);

    private static MajoritarianSystem TwoRound(double spread = 0.35, double abstain = 0.1, Func<string, string, double>? dist = null) =>
        new(200, runoff: true, spread, 0.0, 2, 0.0, abstain, dist);

    [Fact]
    public void SeatsSumToTheNumberOfDistricts()
    {
        var votes = V(("a", .30), ("b", .27), ("c", .18), ("d", .15), ("e", .10));
        Assert.Equal(200, Fptp().Allocate(votes).Values.Sum());
        Assert.Equal(200, TwoRound().Allocate(votes).Values.Sum());
    }

    [Fact]
    public void WithoutLocalVariationTheLeaderWinsEveryDistrict()
    {
        var seats = Fptp(spread: 0).Allocate(V(("a", .40), ("b", .35), ("c", .25)));
        Assert.Equal(200, seats["a"]);
        Assert.Equal(0, seats["b"]);
    }

    [Fact]
    public void ThePluralityPartyGetsMoreSeatsThanVotes()
    {
        var seats = Fptp().Allocate(V(("a", .38), ("b", .33), ("c", .17), ("d", .12)));
        Assert.True(seats["a"] / 200.0 > .38 + .10, $"a won {seats["a"]}");
        Assert.True(seats["d"] < 200 * .12 / 2, $"d won {seats["d"]}");
    }

    [Fact]
    public void SmallPartyWithEvenSupportWinsNothing()
    {
        var seats = Fptp(spread: 0.2).Allocate(V(("a", .45), ("b", .40), ("c", .15)));
        Assert.Equal(0, seats["c"]);
    }

    [Fact]
    public void LocalStrongholdsLetASmallPartyWinSeatsWithTheSameVotes()
    {
        var votes = V(("a", .40), ("b", .35), ("c", .25));
        Assert.True(Fptp(spread: 0.9).Allocate(votes)["c"] > Fptp(spread: 0.3).Allocate(votes)["c"]);
    }

    [Fact]
    public void SameVotesGiveTheSameSeats()
    {
        var votes = V(("a", .30), ("b", .27), ("c", .18), ("d", .15), ("e", .10));
        Assert.Equal(Fptp().Allocate(votes), Fptp().Allocate(votes));
        Assert.Equal(TwoRound().Allocate(votes), TwoRound().Allocate(votes));
    }

    [Fact]
    public void StrategicVotingSqueezesTheThirdParty()
    {
        var votes = V(("a", .38), ("b", .34), ("c", .28));
        Assert.True(Fptp(spread: 0.5, strategic: 0.5).Allocate(votes)["c"] <= Fptp(spread: 0.5, strategic: 0.0).Allocate(votes)["c"]);
    }

    /// <summary>Left (L), centre (C), right (R) on a line; C's voters are close to L.</summary>
    private static double Line(string a, string b)
    {
        double P(string x) => x switch { "L" => 0.0, "C" => 1.0, _ => 3.0 };
        return Math.Abs(P(a) - P(b));
    }

    [Fact]
    public void TheRunoffLetsTheEliminatedPartysVotersDecide()
    {
        var votes = V(("L", .33), ("C", .30), ("R", .37));
        var plurality = Fptp(spread: 0.05, dist: Line).Allocate(votes);
        var runoff = TwoRound(spread: 0.05, dist: Line).Allocate(votes);
        Assert.True(plurality["R"] > plurality["L"]);
        Assert.True(runoff["L"] > runoff["R"], $"L {runoff["L"]} R {runoff["R"]}");
    }

    [Fact]
    public void AnOutrightMajorityNeedsNoRunoff()
    {
        var seats = TwoRound(spread: 0).Allocate(V(("a", .55), ("b", .25), ("c", .20)));
        Assert.Equal(200, seats["a"]);
    }

    [Fact]
    public void FactoryBuildsTheMajorityTypes()
    {
        var f = ElectoralSystems.Create(new ElectoralSystemDef("x", "fptp", 150, "dhondt", 0, "t"));
        Assert.Equal(150, f.Seats);
        Assert.IsType<MajoritarianSystem>(ElectoralSystems.Create(new ElectoralSystemDef("y", "two_round", 150, "dhondt", 0, "t")));
    }

    [Fact]
    public void RandomVotesNeverBreakTheSeatTotal()
    {
        var rng = new OpenPolitics.Core.Determinism.Pcg32(11);
        for (int i = 0; i < 200; i++)
        {
            var raw = Enumerable.Range(0, 6).Select(k => ("p" + k, 0.02 + rng.NextDouble())).ToArray();
            double t = raw.Sum(r => r.Item2);
            var votes = raw.ToDictionary(r => r.Item1, r => r.Item2 / t);
            Assert.Equal(200, Fptp(strategic: 0.3).Allocate(votes).Values.Sum());
            Assert.Equal(200, TwoRound().Allocate(votes).Values.Sum());
        }
    }
}
