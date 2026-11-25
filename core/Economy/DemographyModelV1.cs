using OpenPolitics.Core.Determinism;

namespace OpenPolitics.Core.Economy;

/// <summary>
/// Population (decision 0021). Four age groups (children, young adults 15-29, workers from 30 to the retirement age, pensioners) change by births,
/// ageing, deaths and migration; the adults are also spread over five income classes that move up and down. Rates are values the graph steers
/// (fertility, life expectancy, net migration, mobility), so policies and the economy act on them through edges like on any other value:
/// <list type="bullet">
/// <item>births = population x 0.0104 x fertility x (young + workers share): 1.55 children per woman is about 1 % a year;</item>
/// <item>children become young adults after 15 years, young adults workers after 15 more, workers pensioners over the years left to the
/// retirement age, so a later retirement age lowers the inflow into retirement and, over decades, the number of pensioners;</item>
/// <item>pensioners die at 0.8 / (life expectancy - retirement age) a year; migrants arrive 40 % young and 60 % workers;</item>
/// <item>each class moves up by <c>mobility_up</c> and down by <c>mobility_down</c> percent a year times a class factor chosen so that the starting
/// distribution is the resting point when both rates are at their starting value: good times raise the up rate, a bad labour market the down rate.</item>
/// </list>
/// It also publishes the growth of the working-age population (the labour force follows it) and the loads that spending policies follow.
/// </summary>
public sealed class DemographyModelV1 : IEconomyModel
{
    public static class Ids
    {
        public const string Population = "base:population";
        public const string Children = "base:share_children", Young = "base:share_young", Working = "base:share_working", Pensioners = "base:share_pensioners";
        public static readonly string[] Classes = { "base:class_poor", "base:class_low", "base:class_middle", "base:class_upper", "base:class_high" };
        public const string AdultPensioners = "base:adult_share_pensioners", AdultYoung = "base:adult_share_young", AdultFamilies = "base:adult_share_families";
        public const string StudentLoad = "base:student_load", ChildLoad = "base:child_load", PovertyLoad = "base:poverty_load";
        public const string Fertility = "base:fertility", LifeExpectancy = "base:life_expectancy", NetMigration = "base:net_migration";
        public const string MobilityUp = "base:mobility_up", MobilityDown = "base:mobility_down";
        public const string RetirementAge = "base:retirement_age";
    }

    private const double BirthK = 0.0104, YoungYears = 15, ChildYears = 15, WorkStart = 30, PensionerMortality = 0.8;
    private const double MortalityChild = 0.0004, MortalityYoung = 0.0005, MortalityWorker = 0.003;
    private const double MigrantYoung = 0.4, FamiliesPerChild = 2.2;
    /// <summary>How the classes (poor, lower, middle, upper, high) move: up and down factors that make 12/26/36/18/8 % the resting distribution.</summary>
    private static readonly double[] UpFactor = { 26.0 / 12.0, 36.0 / 26.0, 18.0 / 36.0, 8.0 / 18.0, 0.0 };
    private static readonly double[] DownFactor = { 0.0, 1.0, 1.0, 1.0, 1.0 };
    /// <summary>Where migrants land among the classes.</summary>
    private static readonly double[] MigrantClass = { 0.30, 0.40, 0.20, 0.08, 0.02 };

    public string Id => "demography_v1";

    public IReadOnlyList<string> OwnedValueIds { get; } = new[]
    {
        Ids.Population, Ids.Children, Ids.Young, Ids.Working, Ids.Pensioners,
        "base:class_poor", "base:class_low", "base:class_middle", "base:class_upper", "base:class_high",
        Ids.AdultPensioners, Ids.AdultYoung, Ids.AdultFamilies, Ids.StudentLoad, Ids.ChildLoad, Ids.PovertyLoad, MacroModelV1.Ids.LabourSupplyGrowth,
    };

    public IReadOnlyDictionary<string, double> Step(EconomyInput input, double dt)
    {
        double V(string id, double fallback = 0.0) => input.Values.TryGetValue(id, out var v) && double.IsFinite(v) ? v : fallback;
        double n = Math.Max(V(Ids.Population, 1.0), 1.0);
        double c = V(Ids.Children), y = V(Ids.Young), w = V(Ids.Working), p = V(Ids.Pensioners);
        double fert = Math.Max(V(Ids.Fertility, 1.5), 0.0), life = V(Ids.LifeExpectancy, 78.0), mig = V(Ids.NetMigration) / 1000.0;
        double retire = input.PolicyParams.TryGetValue(Ids.RetirementAge, out var ra) ? ra : 63.0;

        double C = n * c, Y = n * y, W = n * w, P = n * p;
        double births = n * BirthK * fert * (y + w);
        double c2y = C / ChildYears, y2w = Y / YoungYears, w2p = W / Math.Max(retire - WorkStart, 5.0);
        double mp = PensionerMortality / Math.Max(life - retire, 4.0);
        double migrants = n * mig;
        double C2 = Math.Max(C + dt * (births - c2y - C * MortalityChild), 0.0);
        double Y2 = Math.Max(Y + dt * (c2y - y2w - Y * MortalityYoung + MigrantYoung * migrants), 0.0);
        double W2 = Math.Max(W + dt * (y2w - w2p - W * MortalityWorker + (1 - MigrantYoung) * migrants), 0.0);
        double P2 = Math.Max(P + dt * (w2p - P * mp), 0.0);
        double n2 = Math.Max(C2 + Y2 + W2 + P2, 1.0);

        // income classes among the adults
        var q = Ids.Classes.Select(id => V(id)).ToArray();
        double qs = q.Sum();
        if (!(qs > 0)) q = new[] { 0.12, 0.26, 0.36, 0.18, 0.08 };
        else for (int i = 0; i < q.Length; i++) q[i] /= qs;
        double up = Math.Max(V(Ids.MobilityUp, 4.0), 0.0) / 100.0, down = Math.Max(V(Ids.MobilityDown, 4.0), 0.0) / 100.0;
        double adults = Math.Max(y + w + p, 1e-9);
        double arrivals = migrants * dt / (n * adults); // migrants as a share of the adults
        var q2 = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double leaveUp = q[i] * up * UpFactor[i] * dt, leaveDown = q[i] * down * DownFactor[i] * dt;
            q2[i] += q[i] - leaveUp - leaveDown + arrivals * MigrantClass[i];
            if (i + 1 < 5) q2[i + 1] += leaveUp;
            if (i - 1 >= 0) q2[i - 1] += leaveDown;
        }
        double sum2 = q2.Sum();
        for (int i = 0; i < 5; i++) q2[i] = Math.Max(q2[i], 0.0) / (sum2 > 0 ? sum2 : 1.0);

        double c3 = C2 / n2, y3 = Y2 / n2, w3 = W2 / n2, p3 = P2 / n2;
        double adults2 = Math.Max(y3 + w3 + p3, 1e-9);
        var result = new Dictionary<string, double>
        {
            [Ids.Population] = n2,
            [Ids.Children] = c3, [Ids.Young] = y3, [Ids.Working] = w3, [Ids.Pensioners] = p3,
            [Ids.AdultPensioners] = p3 / adults2, [Ids.AdultYoung] = y3 / adults2,
            [Ids.AdultFamilies] = Math.Clamp(FamiliesPerChild * c3, 0.0, 0.7),
            [Ids.StudentLoad] = c3 + 0.4 * y3, [Ids.ChildLoad] = c3,
            [Ids.PovertyLoad] = q2[0] + 0.5 * q2[1],
            [MacroModelV1.Ids.LabourSupplyGrowth] = W > 0 ? (W2 / W - 1.0) / dt * 100.0 : 0.0,
        };
        for (int i = 0; i < 5; i++) result[Ids.Classes[i]] = q2[i];
        foreach (var k in result.Keys.ToList()) if (!double.IsFinite(result[k])) result[k] = V(k);
        return result;
    }
}
