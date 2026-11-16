using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>A chart the client can show: which series of the history, how they are named, scaled and formatted.</summary>
public sealed record ChartSpec(string Id, Func<Localizer, string> Title, string Format, double Scale, Func<Session, IReadOnlyList<(string Key, string Name, Color Color)>> Series);

public static class Charts
{
    private static readonly Color[] Palette = Enumerable.Range(0, 8).Select(Ui.PaletteColor).ToArray();
    private static readonly Color InkLine = AppTheme.Ink;

    public static Color PaletteColor(int i) => Ui.PaletteColor(i);

    private static IReadOnlyList<(string, string, Color)> One(string key, string name, Color color) => new[] { (key, name, color) };

    private static ChartSpec Value(string id, string valueId, string format, Color color, double scale = 1) =>
        new(id, t => t.Name(valueId), format, scale, s => One(History.ValueKey(valueId), s.T.Name(valueId), color));

    public static IReadOnlyList<ChartSpec> All { get; } = new List<ChartSpec>
    {
        new("gdp", t => t.T("ui.chart.gdp"), "0.00", 1e-12, s => new[]
        {
            (History.ValueKey("base:gdp"), s.T.T("ui.chart.real"), Palette[0]),
            (History.ValueKey("base:gdp_nominal"), s.T.T("ui.chart.nominal"), Palette[1]),
        }),
        new("revenue", t => t.T("ui.chart.budget"), "0.00", 1e-12, s => new[]
        {
            (History.ValueKey("base:revenue"), s.T.T("ui.fig.income"), Palette[2]),
            (History.ValueKey("base:spending"), s.T.T("ui.fig.expenditure"), Palette[7]),
        }),
        Value("growth", "base:real_growth", "0.0", InkLine),
        Value("unemployment", "base:unemployment", "0.0", InkLine),
        Value("inflation", "base:inflation", "0.0", InkLine),
        Value("deficit", "base:deficit_pct", "0.0", InkLine),
        Value("interest_rate", "base:interest_rate", "0.0", InkLine),
        new("interest_paid", t => t.T("ui.chart.interest_paid"), "0", 1e-9, s => One(History.InterestPaid, t(s, "ui.chart.interest_paid"), Palette[3])),
        new("debt", t => t.T("ui.chart.debt_ratio"), "0", 1, s => One(History.DebtRatio, t(s, "ui.chart.debt_ratio"), InkLine)),
        new("mood", t => t.T("ui.line.mood"), "0.00", 1, s => One(History.Mood, s.T.T("ui.line.mood"), InkLine)),
        new("polls", t => t.T("ui.chart.polls"), "0.0", 100, s => Parties(s, History.PollKey, includeAll: true)),
        new("loyalty", t => t.T("ui.chart.loyalty"), "0", 1, s => Parties(s, History.LoyaltyKey, includeAll: false)),
        new("groups", t => t.T("ui.chart.groups"), "0.00", 1, s => s.Packs.VoterGroups.OrderBy(g => g.Id, StringComparer.Ordinal)
            .Select((g, i) => (History.GroupKey(g.Id), s.T.Name(g.Id), PaletteColor(i))).ToList()),
        Value("health", "base:health", "0.0", InkLine),
        Value("education", "base:education_level", "0.0", InkLine),
        Value("crime", "base:crime", "0", InkLine),
        Value("environment", "base:environment", "0.0", InkLine),
        Value("inequality", "base:inequality", "0.0", InkLine),
    };

    /// <summary>The charts shown in an indicator's dialog: the debt gets its ratio to GDP and the interest paid on it; others the chart of their own series.</summary>
    public static IReadOnlyList<ChartSpec> For(Session s, string valueId)
    {
        if (valueId == "base:debt") return All.Where(c => c.Id is "debt" or "interest_paid").ToList();
        var own = All.FirstOrDefault(c => c.Series(s).Any(x => x.Key == History.ValueKey(valueId)));
        return own == null ? Array.Empty<ChartSpec>() : new[] { own };
    }

    private static string t(Session s, string key) => s.T.T(key);

    private static IReadOnlyList<(string, string, Color)> Parties(Session s, Func<string, string> key, bool includeAll)
    {
        var ids = s.Packs.Parties.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return ids.Select(id => (key(id), s.T.Name(id), Ui.PartyColor(s, id)))
            .Where(x => includeAll || s.History.Series(x.Item1).Any(double.IsFinite)).ToList();
    }

    /// <summary>The charts that have data in this game (a sandbox has no polls or loyalty).</summary>
    public static IEnumerable<ChartSpec> Available(Session s) =>
        All.Where(c => c.Series(s).Any(x => s.History.Series(x.Key).Any(double.IsFinite)));

    /// <summary>The series of a chart over the last <paramref name="lastTurns"/> turns (0 = all), already scaled.</summary>
    public static (IReadOnlyList<int> Turns, IReadOnlyList<ChartSeries> Series) Build(Session s, ChartSpec spec, int lastTurns)
    {
        IReadOnlyList<int> turns = Array.Empty<int>();
        var series = new List<ChartSeries>();
        foreach (var (key, name, color) in spec.Series(s))
        {
            var (tt, vv) = s.History.Window(key, lastTurns);
            if (!vv.Any(double.IsFinite)) continue;
            turns = tt;
            series.Add(new ChartSeries(name, vv.Select(v => v * spec.Scale).ToList(), color));
        }
        return (turns, series);
    }
}
