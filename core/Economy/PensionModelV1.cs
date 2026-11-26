using OpenPolitics.Core.Determinism;

namespace OpenPolitics.Core.Economy;

/// <summary>What the private pension funds do in one stretch of time, in money per year unless stated (see <see cref="PensionModelV1"/>).</summary>
public sealed record PensionFlows(
    double Participation, double Fund, double PrivateContribution, double StateContribution, double Returns, double Payout,
    double NetSaving, double Funded, double Cover, double Bill, double StateSaving, double Burden);

/// <summary>Where the funds stand after some years at today's settings, and where they would stand without any help from the state.</summary>
public sealed record PensionOutlook(double Years, PensionFlows WithState, PensionFlows Alone);

/// <summary>
/// Private pensions (decision 0021). People save for retirement on their own when they have money to spare, when the state pension is small and
/// when they must work long; the state can add a contribution, a tax relief or a compulsory share of the social contributions. The funds grow by
/// contributions and returns and pay out to the retirees. They are the retirees' wealth and part of the country's capital: most of the net saving
/// is invested at home and adds to the capital stock (which is also what a property or wealth tax is levied on). What the funds pay out lowers
/// the bill the state pensions put on the budget (the pensions policy follows <c>base:pension_burden</c>).
/// <list type="bullet">
/// <item>participation moves 20 % a year toward a target: 10 % + 3 points per point of growth above 1.5 % + 25 points per unit by which the state
/// pension falls below its starting level + 3 points per year of retirement age above 63 + 0.42 per % of GDP of tax relief + 0.4 per % of GDP of
/// state contribution; the compulsory share sets a floor of coverage;</item>
/// <item>contributions = participation x wage bill (half of GDP) x 4 % + the state contribution + the compulsory share (both % of GDP);</item>
/// <item>the funds earn the real rate plus 2 % and pay out half their assets over the years the retirees have left;</item>
/// <item>the payout replaces 80 % of its amount of the state pension bill, at most 60 % of it; <c>pension_burden</c> is the pensioners' share
/// times what the state still pays, raised so that the cost driver's elasticity (<see cref="MacroModelV1.LoadElasticity"/>) leaves exactly that share.</item>
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

    private const double ContributionRate = 0.04, WageShare = 0.5, ReturnPremium = 0.02, PayoutShare = 0.5, PensionStart = 9.0, Replacement = 0.8, MaxCover = 0.6;

    public string Id => "pensions_v1";

    public IReadOnlyList<string> OwnedValueIds { get; } = new[] { Ids.Participation, Ids.Fund, Ids.Saving, Ids.Burden, Ids.Funded };

    /// <summary>The economy and the settings the funds react to.</summary>
    private readonly record struct Context(double Gdp, double Growth, double Retire, double Life, double Pensioners, double Rate, double Pensions, double Subsidy, double Relief, double Mandatory);

    private static PensionFlows Compute(in Context c, double part, double fund, double dt)
    {
        double generosity = c.Pensions / PensionStart;
        double target = 0.10 + 0.03 * Math.Clamp(c.Growth - 1.5, -3.0, 5.0) + 0.25 * (1.0 - generosity) + 0.03 * (c.Retire - 63.0)
            + 0.4167 * c.Relief + 0.4 * c.Subsidy;
        target = Math.Clamp(target, 0.02, 0.9);
        double floor = Math.Min(1.0, c.Mandatory / 1.5);
        double part2 = Math.Clamp(Math.Max(part + dt * 0.2 * (target - part), floor), 0.0, 1.0);

        double people = part2 * c.Gdp * WageShare * ContributionRate, state = (c.Subsidy + c.Mandatory) * c.Gdp / 100.0;
        double returns = c.Rate * fund;
        double payout = fund * PayoutShare / Math.Max(c.Life - c.Retire, 4.0);
        double saving = people + state + returns - payout;
        double fund2 = Math.Max(fund + dt * saving, 0.0);

        double bill = Math.Max(c.Pensions, 0.1) * c.Gdp / 100.0 * DetMath.Pow(Math.Max(c.Pensioners / 0.215, 0.1), MacroModelV1.LoadElasticity);
        double funded = Math.Clamp(payout / bill, 0.0, 1.0);
        double cover = Math.Min(MaxCover, Replacement * funded);
        double burden = c.Pensioners * DetMath.Pow(1.0 - cover, 1.0 / MacroModelV1.LoadElasticity);
        return new PensionFlows(part2, fund2, people, state, returns, payout, saving, funded, cover, bill, cover * bill, burden);
    }

    private static Context ContextOf(Func<string, double, double> V, Func<string, double, double> Pol) => new(
        Math.Max(V(MacroModelV1.Ids.Gdp, 0.0), 1.0), V(MacroModelV1.Ids.RealGrowth, 0.0),
        Pol(DemographyModelV1.Ids.RetirementAge, 63.0), V(DemographyModelV1.Ids.LifeExpectancy, 78.0), V(DemographyModelV1.Ids.Pensioners, 0.215),
        Math.Clamp((V(MacroModelV1.Ids.InterestRate, 0.0) - V(MacroModelV1.Ids.Inflation, 0.0)) / 100.0 + ReturnPremium, 0.0, 0.10),
        Pol(Ids.Pensions, PensionStart), Pol(Ids.Subsidy, 0.0), Pol(Ids.Relief, 0.0), Pol(Ids.Mandatory, 0.0));

    private static (Func<string, double, double> V, Func<string, double, double> Pol) Readers(IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, double> policies) => (
        (id, fallback) => values.TryGetValue(id, out var v) && double.IsFinite(v) ? v : fallback,
        (id, fallback) => policies.TryGetValue(id, out var v) && double.IsFinite(v) ? v : fallback);

    public IReadOnlyDictionary<string, double> Step(EconomyInput input, double dt)
    {
        var (V, Pol) = Readers(input.Values, input.PolicyParams);
        double part = Math.Clamp(V(Ids.Participation, 0.12), 0.0, 1.0), fund = Math.Max(V(Ids.Fund, 0.0), 0.0);
        var f = Compute(ContextOf(V, Pol), part, fund, dt);
        var r = new Dictionary<string, double>
        {
            [Ids.Participation] = f.Participation, [Ids.Fund] = f.Fund, [Ids.Saving] = f.NetSaving, [Ids.Burden] = f.Burden, [Ids.Funded] = f.Funded,
        };
        foreach (var k in r.Keys.ToList()) if (!double.IsFinite(r[k])) r[k] = V(k, 0.0);
        return r;
    }

    /// <summary>What the funds do right now at the current settings (money per year).</summary>
    public static PensionFlows Explain(IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, double> policies)
    {
        var (V, Pol) = Readers(values, policies);
        return Compute(ContextOf(V, Pol), Math.Clamp(V(Ids.Participation, 0.12), 0.0, 1.0), Math.Max(V(Ids.Fund, 0.0), 0.0), 0.25);
    }

    /// <summary>
    /// The funds after <paramref name="years"/> at today's settings with the economy standing still apart from growth at its present pace (capped
    /// at 0-3 %), next to the same funds without a state contribution, tax relief or compulsory share. A forecast to show what the settings lead to.
    /// </summary>
    public static PensionOutlook Project(IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, double> policies, double years)
    {
        var (V, Pol) = Readers(values, policies);
        var now = ContextOf(V, Pol);
        PensionFlows Run(Context c)
        {
            double part = Math.Clamp(V(Ids.Participation, 0.12), 0.0, 1.0), fund = Math.Max(V(Ids.Fund, 0.0), 0.0);
            double g = Math.Clamp(c.Growth, 0.0, 3.0);
            var flows = Compute(c, part, fund, 0.25);
            for (int i = 0; i < (int)Math.Round(years * 4); i++)
            {
                c = c with { Gdp = c.Gdp * (1 + 0.25 * g / 100.0) };
                flows = Compute(c, flows.Participation, flows.Fund, 0.25);
            }
            return flows;
        }
        return new PensionOutlook(years, Run(now), Run(now with { Subsidy = 0.0, Relief = 0.0, Mandatory = 0.0 }));
    }
}
