using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0016 (3): a government party that brings a high deficit down earns a small "fiscal credit" - the gentle counterpart of fiscal blame.</summary>
public class FiscalCreditTests
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private static IReadOnlyDictionary<string, double> Step(double from, double to, SortedDictionary<string, double>? credit = null) =>
        FiscalBlame.CreditStep(credit ?? new SortedDictionary<string, double>(Ord), "lead", new[] { "partner" }, from, to);

    [Fact]
    public void CuttingAHighDeficitEarnsCreditTheLeaderFullThePartnerHalf()
    {
        var gained = Step(8, 6);
        double expected = FiscalBlame.CreditRatio * 2 * FiscalBlame.Severity(8);
        Assert.Equal(expected, gained["lead"], 9);
        Assert.Equal(expected * FiscalBlame.PartnerShare, gained["partner"], 9);
        Assert.False(gained.ContainsKey("opposition"));
    }

    [Fact]
    public void NoCreditForASurplusOrARisingDeficit()
    {
        Assert.Empty(Step(-3, -1)); // a shrinking surplus is no cut
        Assert.Empty(Step(-1, -3)); // a surplus that merely stays a surplus
        Assert.Empty(Step(4, 5));   // rising
        Assert.Empty(Step(4, 4.0 + 1e-6));
    }

    [Fact]
    public void OnlyTheCutDownToBalanceCounts()
    {
        // from +2 to -1: two points of real deficit cut, the surplus beyond balance earns nothing
        Assert.Equal(FiscalBlame.CreditRatio * 2 * FiscalBlame.Severity(2), Step(2, -1)["lead"], 9);
    }

    [Fact]
    public void CreditIsAboutAThirdOfTheBlameForTheSameStep()
    {
        var blame = new SortedDictionary<string, double>(Ord);
        FiscalBlame.Step(blame, "lead", Array.Empty<string>(), new Dictionary<string, double> { ["lead"] = 0 }, 6, 8);
        double credit = Step(8, 6)["lead"];
        Assert.InRange(credit / blame["lead"], 0.3, 0.4);
    }

    [Fact]
    public void CapitalFromCreditIsSmallAndCapped()
    {
        Assert.Equal(FiscalBlame.CreditCapitalMax, FiscalBlame.CreditCapital(Step(8, 4)["lead"]), 9); // a huge cut: capped at 1
        double partner = FiscalBlame.CreditCapital(Step(8, 6)["partner"]);
        double leader = FiscalBlame.CreditCapital(Step(8, 6)["lead"]);
        Assert.True(partner < leader && partner > 0);
        Assert.InRange(FiscalBlame.CreditCapital(Step(5, 4.5)["lead"]), 0.1, 0.5); // an ordinary quarter: a few tenths
        Assert.Equal(0.0, FiscalBlame.CreditCapital(0));
    }

    [Fact]
    public void CreditFadesAndItsPollBonusIsSmallerThanTheBlamePenalty()
    {
        var credit = new SortedDictionary<string, double>(Ord);
        Step(8, 6, credit);
        double first = credit["lead"];
        FiscalBlame.CreditStep(credit, null, Array.Empty<string>(), 6, 6);
        Assert.True(credit["lead"] < first);
        Assert.True(FiscalBlame.Bonus(100) <= FiscalBlame.CreditMaxBonus && FiscalBlame.CreditMaxBonus < FiscalBlame.MaxPenalty / 2);
        Assert.Equal(FiscalBlame.PenaltyPerPoint * 1.5, FiscalBlame.Bonus(1.5), 12);
    }

    /// <summary>The player (Social Alliance) leads, cuts VAT so the deficit jumps to about 5 % of GDP, then raises it again (the deficit falls to ~2 %).</summary>
    private static (Game Game, List<TurnReport> Cutting) RaiseThenCut()
    {
        var packs = BasePackTests.LoadBase().Packs!;
        var g = Simulation.NewGame(packs, "base:cz_2015", 1);
        for (int i = 0; i < 4 && !g.Apply(new SetPolicy("base:vat", 7)).Accepted; i++) g.EndTurn();
        for (int i = 0; i < 2; i++) g.EndTurn();
        Assert.True(g.State.Countries.Single().Value.Values["base:deficit_pct"] > 3);
        var cutting = new List<TurnReport>();
        for (int i = 0; i < 4 && !g.Apply(new SetPolicy("base:vat", 10)).Accepted; i++) cutting.Add(g.EndTurn());
        for (int i = 0; i < 2; i++) cutting.Add(g.EndTurn());
        return (g, cutting);
    }

    [Fact]
    public void TheEngineCreditsALeaderWhoCutsAHighDeficit()
    {
        var (g, cutting) = RaiseThenCut();
        var earned = cutting.SelectMany(r => r.PoliticalEvents).OfType<FiscalCreditEarned>().ToList();
        Assert.NotEmpty(earned);
        Assert.All(earned, e =>
        {
            Assert.Equal("base:social_alliance", e.PartyId);
            Assert.InRange(e.Capital, 0.0, FiscalBlame.CreditCapitalMax + 1e-9);
            Assert.True(e.From > e.To);
        });
        var pol = g.State.Countries.Single().Value.Politics!;
        Assert.True(pol.FiscalCredit!.GetValueOrDefault("base:social_alliance") > 0);
        Assert.True(pol.Party!.FiscalCredit > 0);
        Assert.Equal(0.0, pol.FiscalCredit!.GetValueOrDefault("base:civic_right")); // the opposition earns nothing
        // replayed exactly
        var loaded = Simulation.Load(BasePackTests.LoadBase().Packs!, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(pol.FiscalCredit, loaded.State.Countries.Single().Value.Politics!.FiscalCredit);
    }

    [Fact]
    public void AnOppositionPlayerEarnsNoCredit()
    {
        var packs = BasePackTests.LoadBase().Packs!;
        var g = Simulation.NewGame(packs, "base:cz_2015", 1, "base:civic_right");
        var reports = new List<TurnReport>();
        for (int i = 0; i < 24; i++) reports.Add(g.EndTurn());
        Assert.Empty(reports.SelectMany(r => r.PoliticalEvents).OfType<FiscalCreditEarned>());
        Assert.Equal(0.0, g.State.Countries.Single().Value.Politics!.Party!.FiscalCredit);
    }
}
