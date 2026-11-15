using System.Globalization;
using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Gameplay;

/// <summary>A small but complete in-memory pack ("base") with the ten macro values plus a few extras.</summary>
internal static class GameFixture
{
    private static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    public static Dictionary<string, string> Files(Action<Dictionary<string, string>>? mutate = null)
    {
        double y0 = 4.6e12, lf = 5.3e6, u = 5, alpha = 0.35;
        double l = lf * (1 - u / 100), k = 4 * y0;
        double a = y0 / (Math.Pow(k, alpha) * Math.Pow(l, 1 - alpha));

        string Macro(string id, string unit, double v) =>
            $"- id: base:{id}\n  unit: {unit}\n  initial: {N(v)}\n  scale: {N(Math.Abs(v) * 0.1 + 1)}\n  owner: macro_v1\n";

        var f = new Dictionary<string, string>
        {
            ["manifest.yaml"] = "id: base\nversion: 0.1.0\nlicense: CC-BY-SA-4.0\nillustrative: true\n",
            ["values/macro.yaml"] =
                Macro("gdp", "CZK", y0) + Macro("capital", "CZK", k) + Macro("productivity", "index", a) +
                Macro("labour_force", "persons", lf) + Macro("unemployment", "percent", u) + Macro("inflation", "percent", 2) +
                Macro("debt", "CZK", 0.4 * y0) + Macro("interest_rate", "percent", 4.1) +
                Macro("revenue", "CZK", 0.4 * y0) + Macro("spending", "CZK", 0.4 * y0),
            ["values/social.yaml"] = "- id: base:health\n  unit: index\n  initial: 70\n  scale: 10\n",
            ["policies/p.yaml"] =
                "- id: base:tax\n  unit: percent_of_gdp\n  min: 0\n  max: 60\n  default: 40\n  revenue_base: base:gdp\n" +
                "- id: base:spend\n  unit: percent_of_gdp\n  min: 0\n  max: 80\n  default: 40\n  cost_per_unit: 1\n" +
                "- id: base:slow\n  unit: percent\n  min: 0\n  max: 10\n  default: 1\n  delay_turns: 2\n",
            ["edges/e.yaml"] =
                "- id: base:spend_health\n  from: base:spend\n  to: base:health\n  strength: 2\n" +
                "- id: base:slow_health\n  from: base:slow\n  to: base:health\n  strength: 10\n",
            ["voter_groups/g.yaml"] =
                "- id: base:workers\n  size: 1\n  weight: 1\n  value_weights: {base:gdp: 5}\n  policy_prefs: {base:tax: 40}\n  policy_weights: {base:tax: 0.5}\n",
            ["countries/c.yaml"] = "- id: base:czechia\n  name_key: country.czechia\n",
            ["scenarios/s.yaml"] =
                "- id: base:test\n  country: base:czechia\n  start_year: 2015\n  turns_per_year: 4\n  start_values: {base:health: 65}\n",
        };
        var ids = new[] { "gdp", "capital", "productivity", "labour_force", "unemployment", "inflation", "debt",
            "interest_rate", "revenue", "spending", "health", "tax", "spend", "slow", "workers" };
        string Loc(string lang) => string.Join("", ids.Select(i => $"name.base:{i}: {lang}-{i}\n")) + $"country.czechia: {lang}-czechia\n";
        f["localization/cs.yaml"] = Loc("cs");
        f["localization/en.yaml"] = Loc("en");
        mutate?.Invoke(f);
        return f;
    }

    public static PackSet Load(Action<Dictionary<string, string>>? mutate = null)
    {
        var r = PackLoader.Load(new IPackSource[] { new InMemoryPackSource("base", Files(mutate)) });
        Assert.True(r.Ok, string.Join("\n", r.Diagnostics));
        return r.Packs!;
    }

    public static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);

    /// <summary>Makes every policy of the pack free of political capital (decision 0013), for tests about other mechanics.</summary>
    public static void FreePolicies(Dictionary<string, string> f)
    {
        foreach (var key in f.Keys.Where(k => k.StartsWith("policies/", StringComparison.Ordinal)).ToList())
            f[key] = f[key].Replace("  min: ", "  capital_cost: 0\n  min: ");
    }
}
