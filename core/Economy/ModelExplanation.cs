namespace OpenPolitics.Core.Economy;

/// <summary>The capital stock: what it adds to output and what it gains and loses per year (see <see cref="MacroModelV1.ExplainCapital"/>).</summary>
public sealed record CapitalExplanation(
    double Capital, double Output, double OutputElasticity, double OutputPerOnePercent,
    double SavingRate, double BaseSaving, double TaxEffect, double InterestEffect, double DebtCrowding,
    double Investment, double Depreciation, double NetChange, double DepreciationRate);

/// <summary>How a line of an explanation is measured: money per year, an amount of money, percentage points per year, a plain percent, or percentage points of output.</summary>
public enum LineUnit { MoneyPerYear, PointsPerYear, Percent, PointsOfOutput, Money }

/// <summary>One line: a localization key (<c>model.&lt;value&gt;.&lt;line&gt;</c>) and a signed amount in <paramref name="Unit"/>.</summary>
public sealed record ModelLine(string Key, double Amount, LineUnit Unit);

/// <summary>Why a model-owned value moves (drivers) and what it moves in turn (effects), in plain numbers from the model's own formulas.</summary>
public sealed record ModelExplanation(string ValueId, IReadOnlyList<ModelLine> Drivers, IReadOnlyList<ModelLine> Effects);

/// <summary>Builds <see cref="ModelExplanation"/>s for the values the macro model owns. Policies and events act on top of these (the client lists them from the graph).</summary>
public static class ModelExplainer
{
    public static ModelExplanation? Explain(string valueId, IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, double> parameters)
    {
        double P(string name) => parameters.TryGetValue(name, out var v) ? v : MacroModelV1.Defaults[name];
        double V(string id) => values.TryGetValue(id, out var v) && double.IsFinite(v) ? v : 0.0;
        double alpha = Math.Clamp(P("alpha"), 0.01, 0.99);
        double y = V(MacroModelV1.Ids.Gdp), debt = V(MacroModelV1.Ids.Debt), pi = V(MacroModelV1.Ids.Inflation), u = V(MacroModelV1.Ids.Unemployment);
        double ratio = y > 0 ? debt / y : 0.0;
        double premium = MacroModelV1.InterestPremium(ratio, P("premium_max"), P("premium_ratio_half"));
        double rReal = P("base_interest") + premium;
        var D = new List<ModelLine>(); var E = new List<ModelLine>();
        switch (valueId)
        {
            case MacroModelV1.Ids.Capital:
                {
                    var c = MacroModelV1.ExplainCapital(values, parameters);
                    D.Add(new("model.capital.investment", c.Investment, LineUnit.MoneyPerYear));
                    D.Add(new("model.capital.depreciation", -c.Depreciation, LineUnit.MoneyPerYear));
                    D.Add(new("model.capital.saving_rate", c.SavingRate * 100, LineUnit.Percent));
                    D.Add(new("model.capital.saving_base", c.BaseSaving * 100, LineUnit.PointsOfOutput));
                    D.Add(new("model.capital.saving_tax", -c.TaxEffect * 100, LineUnit.PointsOfOutput));
                    D.Add(new("model.capital.saving_interest", -c.InterestEffect * 100, LineUnit.PointsOfOutput));
                    D.Add(new("model.capital.saving_debt", -c.DebtCrowding * 100, LineUnit.PointsOfOutput));
                    E.Add(new("model.capital.output_share", c.OutputElasticity * 100, LineUnit.Percent));
                    E.Add(new("model.capital.output", c.OutputPerOnePercent, LineUnit.MoneyPerYear));
                    break;
                }
            case MacroModelV1.Ids.Inflation:
                {
                    double a = Math.Max(V(MacroModelV1.Ids.Productivity), 1e-9), k = Math.Max(V(MacroModelV1.Ids.Capital), 1.0), lf = Math.Max(V(MacroModelV1.Ids.LabourForce), 1.0);
                    double now = MacroModelV1.Production(a, k, lf * (1 - u / 100), alpha), pot = MacroModelV1.Production(a, k, lf * (1 - P("natural_unemployment") / 100), alpha);
                    double gap = pot > 0 ? now / pot - 1 : 0.0;
                    D.Add(new("model.inflation.reversion", -P("inflation_reversion") * (pi - P("inflation_target")), LineUnit.PointsPerYear));
                    D.Add(new("model.inflation.gap", P("kappa") * gap * 100, LineUnit.PointsPerYear));
                    D.Add(new("model.inflation.deficit", P("fiscal_inflation") * V(MacroModelV1.Ids.DeficitPct), LineUnit.PointsPerYear));
                    E.Add(new("model.inflation.prices", pi, LineUnit.Percent));
                    E.Add(new("model.inflation.rate", pi, LineUnit.Percent));
                    E.Add(new("model.inflation.erosion", -pi, LineUnit.Percent));
                    break;
                }
            case MacroModelV1.Ids.PriceLevel:
                D.Add(new("model.price_level.inflation", pi, LineUnit.Percent));
                E.Add(new("model.price_level.nominal_gdp", V(MacroModelV1.Ids.GdpNominal), LineUnit.MoneyPerYear));
                E.Add(new("model.price_level.erosion", -pi, LineUnit.Percent));
                break;
            case MacroModelV1.Ids.InterestRate:
                D.Add(new("model.interest_rate.base", P("base_interest"), LineUnit.Percent));
                D.Add(new("model.interest_rate.premium", premium, LineUnit.Percent));
                D.Add(new("model.interest_rate.inflation", pi, LineUnit.Percent));
                E.Add(new("model.interest_rate.debt_service", debt * rReal / 100.0, LineUnit.MoneyPerYear));
                E.Add(new("model.interest_rate.saving", -P("interest_sensitivity") * (rReal - P("base_interest")), LineUnit.PointsOfOutput));
                break;
            case MacroModelV1.Ids.Debt:
                {
                    double primary = V(MacroModelV1.Ids.Spending) - V(MacroModelV1.Ids.Revenue), interest = debt * rReal / 100.0;
                    D.Add(new("model.debt.primary", primary, LineUnit.MoneyPerYear));
                    D.Add(new("model.debt.interest", interest, LineUnit.MoneyPerYear));
                    D.Add(new("model.debt.net", primary + interest, LineUnit.MoneyPerYear));
                    E.Add(new("model.debt.premium", premium, LineUnit.Percent));
                    E.Add(new("model.debt.saving", -P("debt_crowding") * Math.Max(ratio - 0.6, 0.0) * 100, LineUnit.PointsOfOutput));
                    break;
                }
            case MacroModelV1.Ids.Unemployment:
                {
                    double gPot = P("base_productivity_growth") / (1 - alpha) + P("labour_force_growth");
                    D.Add(new("model.unemployment.okun", -P("okun_beta") * (V(MacroModelV1.Ids.RealGrowth) - gPot * 100), LineUnit.PointsPerYear));
                    D.Add(new("model.unemployment.reversion", -P("unemployment_reversion") * (u - P("natural_unemployment")), LineUnit.PointsPerYear));
                    E.Add(new("model.unemployment.output", -(1 - alpha) * y / 100.0 / Math.Max(1 - u / 100, 0.01), LineUnit.MoneyPerYear));
                    break;
                }
            case MacroModelV1.Ids.Gdp:
                {
                    double k = Math.Max(V(MacroModelV1.Ids.Capital), 1.0), growthK = P("saving_rate") > 0 ? MacroModelV1.ExplainCapital(values, parameters).NetChange / k * 100 : 0.0;
                    D.Add(new("model.gdp.productivity", P("base_productivity_growth") * 100, LineUnit.PointsPerYear));
                    D.Add(new("model.gdp.capital", alpha * growthK, LineUnit.PointsPerYear));
                    D.Add(new("model.gdp.labour", (1 - alpha) * P("labour_force_growth") * 100, LineUnit.PointsPerYear));
                    E.Add(new("model.gdp.share_capital", alpha * 100, LineUnit.Percent));
                    break;
                }
            default: return null;
        }
        return new ModelExplanation(valueId, D, E);
    }

    /// <summary>The private pension funds: what flows in and out of them, what that does to the capital and the state pension bill, and where they will be in ten years (decision 0021).</summary>
    public static ModelExplanation ExplainPensions(IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, double> policies, IReadOnlyDictionary<string, double> parameters)
    {
        double V(string id) => values.TryGetValue(id, out var v) && double.IsFinite(v) ? v : 0.0;
        double share = parameters.TryGetValue("pension_saving_share", out var sh) ? sh : MacroModelV1.Defaults["pension_saving_share"];
        var f = PensionModelV1.Explain(values, policies);
        var o = PensionModelV1.Project(values, policies, 10);
        double capital = Math.Max(V(MacroModelV1.Ids.Capital), 1.0), gdp = Math.Max(V(MacroModelV1.Ids.Gdp), 1.0);
        double Pol(string id) => policies.TryGetValue(id, out var v) && double.IsFinite(v) ? v : 0.0;
        double statePays = (Pol(PensionModelV1.Ids.Subsidy) + Pol(PensionModelV1.Ids.Mandatory) + Pol(PensionModelV1.Ids.Relief)) * gdp / 100.0;
        var D = new List<ModelLine>
        {
            new("model.pension_fund.participation", f.Participation * 100, LineUnit.Percent),
            new("model.pension_fund.people", f.PrivateContribution, LineUnit.MoneyPerYear),
            new("model.pension_fund.state", f.StateContribution, LineUnit.MoneyPerYear),
            new("model.pension_fund.returns", f.Returns, LineUnit.MoneyPerYear),
            new("model.pension_fund.payout", -f.Payout, LineUnit.MoneyPerYear),
            new("model.pension_fund.net", f.NetSaving, LineUnit.MoneyPerYear),
        };
        var E = new List<ModelLine>
        {
            new("model.pension_fund.capital", f.NetSaving * share, LineUnit.MoneyPerYear),
            new("model.pension_fund.capital_share", V(PensionModelV1.Ids.Fund) / capital * 100, LineUnit.Percent),
            new("model.pension_fund.covers", f.Cover * 100, LineUnit.Percent),
            new("model.pension_fund.saves", f.StateSaving, LineUnit.MoneyPerYear),
            new("model.pension_fund.state_pays", -statePays, LineUnit.MoneyPerYear),
            new("model.pension_fund.fund10", o.WithState.Fund, LineUnit.Money),
            new("model.pension_fund.fund10_alone", o.Alone.Fund, LineUnit.Money),
            new("model.pension_fund.covers10", o.WithState.Cover * 100, LineUnit.Percent),
            new("model.pension_fund.saves10", o.WithState.StateSaving, LineUnit.MoneyPerYear),
        };
        return new ModelExplanation(PensionModelV1.Ids.Fund, D, E);
    }
}
