using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class SupportModelTests
{
    private static readonly PolicyDef Tax = new("t:tax", "percent", 0, 40, 20, null, 0, 0, null, "x");
    private static readonly PolicyDef Welfare = new("t:welfare", "percent", 0, 20, 10, null, 0, 0, null, "x");

    private static VoterGroupDef Group(string id, double size, double taxPref, double welfarePref) => new(
        id, size, 1,
        new Dictionary<string, double>(),
        new Dictionary<string, double> { ["t:tax"] = taxPref, ["t:welfare"] = welfarePref },
        new Dictionary<string, double> { ["t:tax"] = 1, ["t:welfare"] = 1 }, "x");

    private static PartyDef Party(string id, double taxTarget, double welfareTarget,
        IReadOnlyDictionary<string, double>? aff = null, bool swing = false) => new(
        id, "name." + id,
        new Dictionary<string, PlatformPosition>
        {
            ["t:tax"] = new(taxTarget, 1),
            ["t:welfare"] = new(welfareTarget, 1),
        },
        aff ?? new Dictionary<string, double>(), swing,
        new List<DemandDef>(), new List<RedLineDef>(), 1, new List<FactionDef>(), "x");

    private static SupportInput Input(double s = 0, params string[] gov) => new(
        new Dictionary<string, double> { ["g1"] = s, ["g2"] = s },
        new HashSet<string>(gov),
        new Dictionary<string, double>());

    private static SupportModel Model(IReadOnlyList<PartyDef> parties, double sigma = 0.05) => new(
        parties, new[] { Group("g1", 60, 10, 15), Group("g2", 40, 30, 3) }, new[] { Tax, Welfare }, sigma: sigma);

    private static readonly PartyDef[] Three =
    {
        Party("a", 10, 15), Party("b", 30, 3), Party("c", 20, 10),
    };

    [Fact]
    public void SharesSumToOne()
    {
        var r = Model(Three).Compute(Input(0.3, "a"), null);
        Assert.Equal(1.0, r.Values.Sum(), 12);
        Assert.Equal(new[] { "a", "b", "c" }, r.Keys);
    }

    [Fact]
    public void HigherAffinityGivesHigherShare()
    {
        var boosted = new[]
        {
            Party("a", 20, 10), Party("b", 20, 10, new Dictionary<string, double> { ["g1"] = 1.0, ["g2"] = 1.0 }),
        };
        var r = Model(boosted).Compute(Input(), null);
        Assert.True(r["b"] > r["a"]);
    }

    [Fact]
    public void PlatformCloserToAGroupRaisesTheShare()
    {
        var far = Model(new[] { Party("a", 40, 20), Party("b", 20, 10) }).Compute(Input(), null)["a"];
        var near = Model(new[] { Party("a", 10, 15), Party("b", 20, 10) }).Compute(Input(), null)["a"];
        Assert.True(near > far);
    }

    [Fact]
    public void UnhappyVotersPunishTheGovernment()
    {
        var m = Model(Three);
        var happy = m.Compute(Input(1, "a"), null)["a"];
        var unhappy = m.Compute(Input(-1, "a"), null)["a"];
        Assert.True(unhappy < happy);
    }

    [Fact]
    public void CalibrationReproducesTargetShares()
    {
        var m = Model(Three);
        var target = new Dictionary<string, double> { ["a"] = 0.5, ["b"] = 0.3, ["c"] = 0.2 };
        var start = Input(0.1, "a");
        m.Calibrate(target, start);
        var r = m.Compute(start, null);
        foreach (var (k, v) in target) Assert.Equal(v, r[k], 6);
    }

    [Fact]
    public void SwingPartyHasLargerNoiseSpread()
    {
        // Same party, same draws; only the swing flag differs, so the spread difference is the 3x noise scale.
        static List<double> Draw(bool swing)
        {
            var parties = new[] { Party("a", 20, 10), Party("b", 20, 10, swing: swing), Party("c", 20, 10) };
            var m = Model(parties);
            var rng = new Pcg32(11);
            var xs = new List<double>();
            for (int i = 0; i < 300; i++) xs.Add(m.Compute(Input(), rng)["b"]);
            return xs;
        }
        static double Var(List<double> xs) { var mean = xs.Average(); return xs.Sum(x => (x - mean) * (x - mean)) / xs.Count; }
        Assert.True(Var(Draw(true)) >= 4 * Var(Draw(false)));
    }

    [Fact]
    public void SameRngSameResult()
    {
        var m = Model(Three);
        var r1 = m.Compute(Input(0.2, "a"), new Pcg32(5));
        var r2 = m.Compute(Input(0.2, "a"), new Pcg32(5));
        foreach (var k in r1.Keys) Assert.Equal(r1[k], r2[k]);
    }

    [Fact]
    public void NoNoiseWhenRngIsNull()
    {
        var m = Model(Three);
        var r1 = m.Compute(Input(0.2, "a"), null);
        var r2 = m.Compute(Input(0.2, "a"), null);
        foreach (var k in r1.Keys) Assert.Equal(r1[k], r2[k]);
    }

    [Fact]
    public void ExtremeInputsStayFinite()
    {
        foreach (var s in new[] { -1.0, 1.0 })
        {
            var r = Model(Three).Compute(Input(s, "a", "b", "c"), new Pcg32(1));
            Assert.All(r.Values, v => Assert.True(double.IsFinite(v) && v >= 0));
            Assert.Equal(1.0, r.Values.Sum(), 9);
        }
        var m = Model(Three);
        m.Calibrate(new Dictionary<string, double> { ["a"] = 1, ["b"] = 0, ["c"] = 0 }, Input());
        var cal = m.Compute(Input(), null);
        Assert.All(cal.Values, v => Assert.True(double.IsFinite(v)));
        Assert.Equal(1.0, cal.Values.Sum(), 9);
        var single = Model(new[] { Party("only", 20, 10) }).Compute(Input(), new Pcg32(2));
        Assert.Equal(1.0, single["only"], 12);
    }
}
