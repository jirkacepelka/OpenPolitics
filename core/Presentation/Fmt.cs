using System.Globalization;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Presentation;

/// <summary>Formats numbers in real units for people. Never a normalised 0-1 scale.</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Value(ValueDef def, double v) => def.Unit switch
    {
        "CZK" => Math.Abs(v) >= 1e12 ? $"{(v / 1e12).ToString("0.00", Inv)} tn CZK"
               : Math.Abs(v) >= 1e9 ? $"{(v / 1e9).ToString("0.0", Inv)} bn CZK"
               : $"{(v / 1e6).ToString("0.0", Inv)} m CZK",
        "persons" => $"{(v / 1e6).ToString("0.00", Inv)} m",
        "percent" or "percent_flow" => $"{v.ToString("0.0", Inv)} %",
        "per_100k" => v.ToString("#,##0", Inv).Replace(',', ' '),
        _ => v.ToString("0.0", Inv),
    };

    public static string Policy(PolicyDef def, double v, Localizer l) => def.Unit switch
    {
        "tax_rate" when def.BaseShare > 0 => $"{(v / def.BaseShare).ToString("0.0", Inv)} %",
        "nominal_spending" when l.NominalGdp > 0 => Amount(v / 100.0 * l.NominalGdp, l),
        "tax_rate" or "nominal_spending" => $"{v.ToString("0.0", Inv)} {l.T("ui.unit.gdp_share")}",
        "percent" => $"{v.ToString("0.0", Inv)} %",
        "electoral_system" => l.T("ui.system." + Math.Clamp((int)Math.Round(v, MidpointRounding.AwayFromZero), 0, 2)),
        "electoral_method" => l.T("ui.method." + Math.Clamp((int)Math.Round(v, MidpointRounding.AwayFromZero), 0, 2)),
        _ => v.ToString("0.0", Inv),
    };

    /// <summary>The policy parameter (percent of GDP) for a figure typed in the shown unit: a tax rate in percent, or billions of money per year.</summary>
    public static double ToParameter(PolicyDef def, double shown, Localizer l) => def.Unit switch
    {
        "tax_rate" when def.BaseShare > 0 => shown * def.BaseShare,
        "nominal_spending" when l.NominalGdp > 0 => shown * 1e9 / l.NominalGdp * 100.0,
        _ => shown,
    };

    /// <summary>A yearly amount of money: "450 bn CZK / yr", "1.20 tn CZK / yr".</summary>
    public static string Amount(double money, Localizer l) =>
        (Math.Abs(money) >= 1e12 ? $"{(money / 1e12).ToString("0.00", Inv)} tn CZK" : $"{(money / 1e9).ToString("0", Inv)} bn CZK")
        + " " + l.T("ui.unit.per_year");

    public static string Number(double v, string format = "0.0") => v.ToString(format, Inv);

    public static string Signed(double v, string format = "0.0") => (v >= 0 ? "+" : "") + v.ToString(format, Inv);

    /// <summary>A bar for a value in [-1, 1] with a centre marker, e.g. "[    ####|          ]".</summary>
    public static string Bar(double s, int half = 10)
    {
        s = Math.Clamp(s, -1.0, 1.0);
        int n = (int)Math.Round(Math.Abs(s) * half);
        string left = s < 0 ? new string(' ', half - n) + new string('#', n) : new string(' ', half);
        string right = s > 0 ? new string('#', n) + new string(' ', half - n) : new string(' ', half);
        return "[" + left + "|" + right + "]";
    }
}
