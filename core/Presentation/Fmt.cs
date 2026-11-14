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
        "percent_of_gdp" => $"{v.ToString("0.0", Inv)} {l.T("ui.unit.gdp_share")}",
        _ => v.ToString("0.0", Inv),
    };

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
