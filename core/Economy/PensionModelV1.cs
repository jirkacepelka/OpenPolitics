using OpenPolitics.Core.Determinism;

namespace OpenPolitics.Core.Economy;

/// <summary>
/// Private pensions (decision 0021). People save for retirement on their own when they have money to spare, when the state pension is small and
/// when they must work long; the state can add a contribution, a tax relief or a compulsory share of the social contributions. The funds grow by
/// contributions and returns and pay out to the retirees; their net saving feeds the capital stock, and what they pay out lowers the burden the
/// state pensions put on the budget (the pensions policy follows <c>base:pension_burden</c>, the pensioners' share less what the funds cover).
/// <list type="bullet">
/// <item>participation moves 20 % a year toward a target: 10 % + 3 points per point of growth above 1.5 % + 25 points per unit by which the state
/// pension falls below its starting level + 3 points per year of retirement age above 63 + 0.42 per % of GDP of tax relief + 0.4 per % of GDP of
/// state contribution; the compulsory share sets a floor of coverage;</item>
/// <item>contributions = participation x wage bill (half of GDP) x 4 % + the state contribution + the compulsory share (both % of GDP);</item>
/// <item>the funds earn the real rate plus 2 % and pay out half their assets over the years the retirees have left.</item>
/// </list>
/// </summary>
public sealed class PensionModelV1 : IEconomyModel
{
    public static class Ids
    {
        public const string Participation = "base:pension_participation", Fund = "base:pension_fund", Saving = "base:pension_saving";
        public const string Burden = "base:pension_burden", Funded = "base:pension_funded_share";
        public const string Pensions = "base:pensions", Subsidy = "base:pension_subsidy", Relief = "base:pension_tax_relief", Mandatory = "base:mandatory_pension_pillar";
    }

    private const double ContributionRate = 0.04, WageShare = 0.5, ReturnPremium = 0.02, PayoutShare = 0.5, PensionStart = 9.0;

    public string Id => "pensions_v1";

    public IReadOnlyList<string> OwnedValueIds { get; } = new[] { Ids.Participation, Ids.Fund, Ids.Saving, Ids.Burden, Ids.Funded };

    public IReadOnlyDictionary<string, double> Step(EconomyInput input, double dt)
    {
        double V(string id, double fallback = 0.0) => input.Values.TryGetValue(id, out var v) && double.IsFinite(v) ? v : fallback;
        double Pol(string id, double fallback = 0.0) => input.PolicyParams.TryGetValue(id, out var v) && double.IsFinite(v) ? v : fallback;
        double gdp = Math.Max(V(MacroModelV1.Ids.Gdp), 1.0), growth = V(MacroModelV1.Ids.RealGrowth);
        double part = Math.Clamp(V(Ids.Participation, 0.12), 0.0, 1.0), fund = Math.Max(V(Ids.Fund), 0.0);
        double retire = Pol(DemographyModelV1.Ids.RetirementAge, 63.0), life = V(DemographyModelV1.Ids.LifeExpectancy, 78.0);
        double pensions = Pol(Ids.Pensions, PensionStart), subsidy = Pol(Ids.Subsidy), relief = Pol(Ids.Relief), mandatory = Pol(Ids.Mandatory);

        double generosity = pensions / PensionStart;
        double target = 0.10 + 0.03 * Math.Clamp(growth - 1.5, -3.0, 5.0) + 0.25 * (1.0 - generosity) + 0.03 * (retire - 63.0)
            + 0.4167 * relief + 0.4 * subsidy;
        target = Math.Clamp(target, 0.02, 0.9);
        double floor = Math.Min(1.0, mandatory / 1.5);
        double part2 = Math.Clamp(Math.Max(part + dt * 0.2 * (target - part), floor), 0.0, 1.0);

        double contrib = part2 * gdp * WageShare * ContributionRate + (subsidy + mandatory) * gdp / 100.0;
        double realRate = Math.Clamp((V(MacroModelV1.Ids.InterestRate) - V(MacroModelV1.Ids.Inflation)) / 100.0 + ReturnPremium, 0.0, 0.10);
        double payout = fund * PayoutShare / Math.Max(life - retire, 4.0);
        double saving = contrib + realRate * fund - payout;
        double fund2 = Math.Max(fund + dt * saving, 0.0);

        double bill = Math.Max(pensions, 0.1) * gdp / 100.0 * Math.Max(V(DemographyModelV1.Ids.Pensioners, 0.2) / 0.215, 0.1);
        double funded = Math.Clamp(payout / bill, 0.0, 1.0);
        double burden = V(DemographyModelV1.Ids.Pensioners, 0.215) * (1.0 - Math.Min(0.6, 0.8 * funded));
        var r = new Dictionary<string, double>
        {
            [Ids.Participation] = part2, [Ids.Fund] = fund2, [Ids.Saving] = saving, [Ids.Burden] = burden, [Ids.Funded] = funded,
        };
        foreach (var k in r.Keys.ToList()) if (!double.IsFinite(r[k])) r[k] = V(k);
        return r;
    }
}
