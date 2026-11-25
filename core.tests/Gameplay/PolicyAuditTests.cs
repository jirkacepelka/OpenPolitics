using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using Xunit;

namespace OpenPolitics.Core.Tests.Gameplay;

/// <summary>
/// Decision 0021: an audit of what every law actually does, not only whether the numbers balance. Each law is raised by half of the room above its
/// default in a game without politics and compared with the same game left alone. The table says what a sensible person expects: the sign of the
/// change of an indicator after four years (<c>value+</c>, <c>value-</c>), that it stays near the control (<c>value0</c>), its peak in the first
/// three years (<c>value^+</c>) or that it is back near the control between two and three years (<c>value~</c>, within a unit of the indicator's scale), with <c>@N</c> to look N quarters ahead.
/// Every law must have a row, so a new law cannot arrive without saying what it is for.
/// </summary>
public class PolicyAuditTests
{
    private static readonly PackSet Packs = Content.BasePackTests.LoadBase().Packs!;
    private const string Scenario = "base:cz_2015_sandbox";
    private const int Horizon = 16;

    private static readonly Dictionary<string, string> Expect = new()
    {
        // taxes
        ["income_tax"] = "revenue+ inequality- capital-",
        ["vat"] = "price_level+ inflation^+ inflation~ revenue+",
        ["corporate_tax"] = "capital- productivity- revenue+",
        ["property_tax"] = "inequality- capital- revenue+",
        ["carbon_tax"] = "environment+ price_level+ revenue+",
        ["wealth_tax"] = "inequality- capital-",
        ["transaction_tax"] = "revenue+",
        ["excise_taxes"] = "health+ price_level+ revenue+",
        ["inheritance_tax"] = "inequality- capital- revenue+",
        ["sugar_tax"] = "health+ price_level+ revenue+",
        ["digital_services_tax"] = "productivity- inequality- revenue+",
        ["bank_levy"] = "capital- price_level+ revenue+",
        ["windfall_tax"] = "capital- inequality- revenue+",
        ["vehicle_tax"] = "environment+ price_level+ revenue+",
        // spending
        ["pensions"] = "spending+ inequality-",
        ["health_spending"] = "health+ life_expectancy+ spending+",
        ["education_spending"] = "education_level+ spending+",
        ["police"] = "crime- spending+",
        ["research"] = "productivity+ spending+",
        ["social_benefits"] = "inequality- spending+",
        ["infrastructure"] = "productivity+ capital+ spending+",
        ["administration"] = "spending+",
        ["defence"] = "spending+",
        ["culture_sport"] = "health+ spending+",
        ["public_transport"] = "environment+ productivity+ spending+",
        ["housing_support"] = "inequality- fertility+ spending+",
        ["family_allowances"] = "fertility+ inequality- spending+",
        ["active_labour_policy"] = "unemployment- spending+",
        ["rural_development"] = "urban_share- spending+",
        ["green_subsidies"] = "environment+ spending+",
        ["digital_infrastructure"] = "productivity+ spending+",
        ["childcare"] = "fertility+ spending+",
        ["elderly_care"] = "health+ spending+",
        ["disability_support"] = "inequality- spending+",
        ["mental_health"] = "health+ crime- spending+",
        ["vocational_training"] = "unemployment- productivity+ spending+",
        ["school_meals"] = "education_level+ spending+",
        ["scholarships"] = "education_level+ mobility_up+ spending+",
        ["rail_network"] = "productivity+ environment+ spending+",
        ["flood_protection"] = "environment+ spending+",
        ["nuclear_programme"] = "environment+ spending+",
        ["cybersecurity"] = "crime- spending+",
        ["justice_system"] = "crime- spending+",
        ["prisons_and_rehabilitation"] = "crime- spending+",
        ["border_guard"] = "crime- spending+",
        ["startup_support"] = "productivity+ spending+",
        ["agriculture_subsidies"] = "spending+",
        ["regional_funds"] = "unemployment- urban_share- spending+",
        ["foreign_aid"] = "spending+",
        ["public_housing"] = "inequality- fertility+@28 spending+",
        ["pension_subsidy"] = "pension_participation+ pension_fund+ spending+",
        ["pension_tax_relief"] = "pension_participation+ pension_fund+ spending+",
        ["mandatory_pension_pillar"] = "pension_participation+ pension_fund+ capital+@160 deficit_pct+",
        // rules
        ["environment_regulation"] = "environment+ productivity-",
        ["migration"] = "net_migration+ population+@160",
        ["minimum_wage"] = "inequality- price_level+",
        ["labour_flexibility"] = "unemployment-",
        ["rent_control"] = "inequality- capital-",
        ["drug_policy"] = "crime+ health+",
        ["university_fees"] = "education_level-",
        ["retirement_age"] = "share_pensioners-@160 labour_force+",
        ["energy_mix"] = "environment+ price_level+",
        ["business_regulation"] = "productivity- capital-",
        ["surveillance"] = "crime-",
        ["gun_laws"] = "crime-",
        ["data_protection"] = "crime+",
        ["press_freedom"] = "productivity+",
        ["abortion_access"] = "fertility-",
        ["same_sex_unions"] = "",
        ["euthanasia"] = "",
        ["smoking_ban"] = "health+",
        ["alcohol_policy"] = "health+ crime-",
        ["vaccination_policy"] = "health+",
        ["curriculum_reform"] = "education_level+ productivity+",
        ["paid_leave"] = "health+ fertility+",
        ["union_rights"] = "inequality- productivity-",
        ["foreign_investment_screening"] = "capital-",
        ["competition_policy"] = "productivity+ price_level-",
        ["procurement_transparency"] = "productivity+",
        ["anti_corruption"] = "productivity+ capital+",
        ["judicial_independence"] = "productivity+",
        ["citizenship_rules"] = "net_migration+",
        ["asylum_policy"] = "net_migration+",
        ["zoning_planning"] = "capital+ inequality-",
        ["gmo_policy"] = "productivity+",
        // elections: no economic effect
        ["electoral_threshold"] = "", ["electoral_method"] = "", ["electoral_system"] = "",
    };

    private static List<IReadOnlyDictionary<string, double>> Run(string? policy, int turns)
    {
        var g = Simulation.NewGame(Packs, Scenario, 1);
        if (policy != null)
        {
            var def = Packs.Policies.First(p => p.Id == "base:" + policy);
            if (!g.IsActive(def.Id)) Assert.True(g.Apply(new IntroducePolicy(def.Id)).Accepted);
            double target = def.Default + 0.5 * (def.Max - def.Default);
            Assert.True(g.Apply(new SetPolicy(def.Id, target)).Accepted, $"{policy} -> {target}");
        }
        var series = new List<IReadOnlyDictionary<string, double>>();
        for (int i = 0; i <= turns; i++) { series.Add(g.State.Countries.Values.Single().Values); g.EndTurn(); }
        return series;
    }

    private static readonly Lazy<List<IReadOnlyDictionary<string, double>>> Control = new(() => Run(null, 200));

    [Fact]
    public void DumpForExploration()
    {
        if (Environment.GetEnvironmentVariable("AUDIT_DUMP") == null) return;
        var lines = new List<string>();
        var control = Control.Value;
        foreach (var (law, exp) in Expect)
        {
            if (exp.Length == 0) continue;
            var run = Run(law, 80);
            foreach (var token in exp.Split(' '))
            {
                string t = token; if (t.Contains('@')) t = t[..t.IndexOf('@')];
                t = t.TrimEnd('+', '-', '0', '~', '^');
                var def = Packs.Values.First(v => v.Id == "base:" + t);
                string cells = string.Join(" ", new[] { 4, 8, 12, 20, 40, 60, 80 }.Select(q => $"{(run[q]["base:" + t] - control[q]["base:" + t]) / def.Scale,7:0.00}"));
                lines.Add($"{law,-28} {token,-24} {cells}");
            }
        }
        File.WriteAllLines("/tmp/audit.txt", lines);
    }

    /// <summary>Laws whose point is a vote, not an indicator: they may move nothing the model measures.</summary>
    private static readonly HashSet<string> OnlyForVoters = new() { "same_sex_unions", "euthanasia" };

    [Fact]
    public void EveryLawMovesSomethingThatMatters()
    {
        var control = Control.Value;
        var weak = new List<string>();
        foreach (var (law, exp) in Expect)
        {
            if (exp.Length == 0 || OnlyForVoters.Contains(law)) continue;
            var run = Run(law, 40);
            double biggest = 0;
            foreach (var token in exp.Split(' '))
            {
                string t = token.Contains('@') ? token[..token.IndexOf('@')] : token;
                t = t.TrimEnd('+', '-', '0', '~', '^');
                if (t is "spending" or "revenue" or "deficit_pct" || token.Contains('@')) continue; // the budget always moves; slow demography is checked on its own
                var def = Packs.Values.First(v => v.Id == "base:" + t);
                biggest = Math.Max(biggest, Math.Abs(run[24]["base:" + t] - control[24]["base:" + t]) / def.Scale);
            }
            if (biggest > 0 && biggest < 0.06) weak.Add($"{law} ({biggest:0.000})");
        }
        Assert.True(weak.Count == 0, "laws whose effect after six years is below 6 % of an indicator's scale: " + string.Join(", ", weak));
    }

    [Fact]
    public void EveryLawSaysWhatItIsFor()
    {
        var missing = Packs.Policies.Select(p => p.Id.Split(':')[1]).Where(id => !Expect.ContainsKey(id)).ToList();
        Assert.True(missing.Count == 0, "laws without an expected effect: " + string.Join(", ", missing));
        var stale = Expect.Keys.Where(k => Packs.Policies.All(p => p.Id != "base:" + k)).ToList();
        Assert.True(stale.Count == 0, "expected effects of laws that do not exist: " + string.Join(", ", stale));
    }

    public static IEnumerable<object[]> Laws() => Expect.Where(kv => kv.Value.Length > 0).Select(kv => new object[] { kv.Key });

    [Theory]
    [MemberData(nameof(Laws))]
    public void ALawDoesWhatItIsExpectedTo(string law)
    {
        var tokens = Expect[law].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int longest = Math.Max(Horizon, tokens.Select(t => t.Contains('@') ? int.Parse(t[(t.IndexOf('@') + 1)..]) : Horizon).Max());
        var run = Run(law, longest);
        var control = Control.Value;
        var failures = new List<string>();
        foreach (var token in tokens)
        {
            string t = token, kind;
            int at = Horizon;
            if (t.Contains('@')) { at = int.Parse(t[(t.IndexOf('@') + 1)..]); t = t[..t.IndexOf('@')]; }
            char sign = t[^1];
            t = t[..^1];
            kind = "end";
            if (t.EndsWith('^')) { kind = "peak"; t = t[..^1]; }
            string id = "base:" + t;
            var def = Packs.Values.First(v => v.Id == id);
            double eps = 0.003 * def.Scale;
            double Diff(int turn) => run[turn][id] - control[turn][id];
            switch (sign)
            {
                case '~':
                    foreach (int turn in new[] { 8, 10, 12 })
                        if (Math.Abs(Diff(turn)) > 1.0 * def.Scale) failures.Add($"{token}: still {Diff(turn):0.###} off at quarter {turn}");
                    break;
                case '0':
                    if (Math.Abs(Diff(at)) > eps) failures.Add($"{token}: moved {Diff(at):0.###}");
                    break;
                default:
                    if (kind == "peak")
                    {
                        double peak = Enumerable.Range(1, 12).Select(Diff).OrderByDescending(Math.Abs).First();
                        if (sign == '+' ? !(peak > 100 * eps) : !(peak < -100 * eps)) failures.Add($"{token}: peak {peak:0.###}");
                    }
                    else
                    {
                        double d = Diff(at);
                        if (sign == '+' ? !(d > eps) : !(d < -eps)) failures.Add($"{token}: {d:0.####} (needs {(sign == '+' ? ">" : "<")} {(sign == '+' ? "" : "-")}{eps:0.####}) at quarter {at}");
                    }
                    break;
            }
        }
        Assert.True(failures.Count == 0, law + ": " + string.Join("; ", failures));
    }
}
