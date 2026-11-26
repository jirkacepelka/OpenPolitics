using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>A chart the client can show: which series of the history, how they are named, scaled and formatted.</summary>
public sealed record ChartSpec(string Id, Func<Localizer, string> Title, string Format, double Scale, Func<Session, IReadOnlyList<(string Key, string Name, Color Color)>> Series,
    Func<Localizer, string>? Tab = null);

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
        new("productivity", t => t.T("ui.chart.productivity"), "0", 1, s => One(History.ValueKey("base:productivity"), s.T.Name("base:productivity"), InkLine)),
        new("capital", t => t.T("ui.chart.capital"), "0.0", 1e-12, s => One(History.ValueKey("base:capital"), s.T.Name("base:capital"), InkLine)),
        new("labour", t => t.T("ui.chart.labour"), "0.00", 1e-6, s => One(History.ValueKey("base:labour_force"), s.T.Name("base:labour_force"), InkLine)),
        Value("price_level", "base:price_level", "0.0", InkLine),
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
        new("group_support", t => t.T("ui.chart.support_by_group"), "0.0", 100, s => s.Packs.VoterGroups.OrderBy(g => g.Id, StringComparer.Ordinal)
            .Select((g, i) => (History.SupportKey(g.Id), s.T.Name(g.Id), PaletteColor(i))).ToList()),
        new("population", t => t.T("ui.chart.population"), "0.00", 1e-6, s => One(History.ValueKey("base:population"), s.T.Name("base:population"), InkLine)),
        new("ages", t => t.T("ui.chart.ages"), "0.0", 100, s => new[] { "base:share_children", "base:share_young", "base:share_working", "base:share_pensioners" }
            .Select((id, i) => (History.ValueKey(id), s.T.Name(id), PaletteColor(i))).ToList()),
        new("classes", t => t.T("ui.chart.classes"), "0.0", 100, s => new[] { "base:class_poor", "base:class_low", "base:class_middle", "base:class_upper", "base:class_high" }
            .Select((id, i) => (History.ValueKey(id), s.T.Name(id), PaletteColor(4 + i))).ToList()),
        new("pension_fund", t => t.T("ui.chart.pension_fund"), "0", 1e-9, s => One(History.ValueKey("base:pension_fund"), s.T.Name("base:pension_fund"), Palette[0])),
        new("pension_saving", t => t.T("ui.chart.pension_saving"), "0", 1e-9, s => One(History.ValueKey("base:pension_saving"), s.T.Name("base:pension_saving"), Palette[2])),
        new("pension_people", t => t.T("ui.chart.pension_people"), "0.0", 100, s => new[] { "base:pension_participation", "base:pension_funded_share" }
            .Select((id, i) => (History.ValueKey(id), s.T.Name(id), PaletteColor(i == 0 ? 1 : 3))).ToList()),
        new("reserve", t => t.T("ui.chart.reserve"), "0", 1e-9, s => One(History.ValueKey("base:reserve"), s.T.Name("base:reserve"), Palette[2])),
        Value("health", "base:health", "0.0", InkLine),
        Value("education", "base:education_level", "0.0", InkLine),
        Value("crime", "base:crime", "0", InkLine),
        Value("environment", "base:environment", "0.0", InkLine),
        Value("inequality", "base:inequality", "0.0", InkLine),
    };

    public static readonly string[] Groups = { "economy", "budget", "society", "politics" };

    private static readonly Dictionary<string, string> GroupById = new(StringComparer.Ordinal)
    {
        ["gdp"] = "economy", ["growth"] = "economy", ["productivity"] = "economy", ["capital"] = "economy", ["labour"] = "economy",
        ["unemployment"] = "economy", ["inflation"] = "economy", ["price_level"] = "economy", ["interest_rate"] = "economy",
        ["revenue"] = "budget", ["deficit"] = "budget", ["debt"] = "budget", ["interest_paid"] = "budget",
        ["mood"] = "society", ["groups"] = "society", ["health"] = "society", ["education"] = "society", ["crime"] = "society",
        ["environment"] = "society", ["inequality"] = "society", ["pension_fund"] = "society", ["pension_saving"] = "society", ["pension_people"] = "society", ["reserve"] = "budget", ["population"] = "society", ["ages"] = "society", ["classes"] = "society", ["polls"] = "politics", ["loyalty"] = "politics", ["group_support"] = "politics",
    };

    /// <summary>Which of the four groups a chart belongs to (the chart dialog's first row).</summary>
    public static string GroupOf(ChartSpec c) => GroupById.GetValueOrDefault(c.Id, "economy");

    private static ChartSpec ById(string id) => All.First(c => c.Id == id);

    /// <summary>
    /// The charts of an indicator's dialog, own chart first, then the ones that explain or follow from it (shown as tabs, one at a time). Every
    /// value has at least one.
    /// </summary>
    public static IReadOnlyList<ChartSpec> For(Session s, string valueId)
    {
        string[] ids = valueId switch
        {
            "base:gdp" or "base:gdp_nominal" => new[] { "gdp", "growth", "productivity", "capital", "labour" },
            "base:real_growth" => new[] { "growth", "gdp", "productivity" },
            "base:productivity" => new[] { "productivity", "growth", "capital" },
            "base:capital" => new[] { "capital", "productivity", "gdp" },
            "base:labour_force" => new[] { "labour", "unemployment" },
            "base:unemployment" => new[] { "unemployment", "labour", "growth" },
            "base:inflation" => new[] { "inflation", "price_level", "interest_rate" },
            "base:price_level" => new[] { "price_level", "inflation" },
            "base:interest_rate" => new[] { "interest_rate", "inflation", "interest_paid" },
            "base:debt" or "base:debt_pct" => new[] { "debt", "interest_paid", "deficit", "reserve" },
            "base:pension_fund" or "base:pension_participation" or "base:pension_funded_share" or "base:pension_saving" or "base:pension_burden" => new[] { "pension_fund", "pension_saving", "pension_people" },
            "base:deficit_pct" => new[] { "deficit", "debt", "revenue" },
            "base:revenue" or "base:spending" => new[] { "revenue", "deficit" },
            _ => Array.Empty<string>(),
        };
        if (ids.Length == 0)
        {
            var own = All.FirstOrDefault(c => c.Series(s).Any(x => x.Key == History.ValueKey(valueId)));
            return own == null ? Array.Empty<ChartSpec>() : new[] { own };
        }
        return ids.Select(ById).ToList();
    }

    /// <summary>A policy: its setting over time, then the indicators it drives most (by the strength of its edges).</summary>
    public static IReadOnlyList<ChartSpec> ForPolicy(Session s, string policyId)
    {
        var list = new List<ChartSpec>
        {
            new("setting:" + policyId, t => t.T("ui.chart.setting"), "0.0#", 1, x => One(History.PolicyKey(policyId), x.T.Name(policyId), InkLine), t => t.T("ui.chart.tab_setting")),
        };
        foreach (var e in s.Packs.Edges.Where(e => e.From == policyId && e.To != policyId).OrderByDescending(e => Math.Abs(e.Strength)).ThenBy(e => e.To, StringComparer.Ordinal))
        {
            var spec = For(s, e.To).FirstOrDefault();
            if (spec != null && list.All(c => c.Id != spec.Id)) list.Add(spec);
            if (list.Count >= 4) break;
        }
        return list;
    }

    /// <summary>A voter group: its mood and, in a game with politics, how many of its voters would back the player's party.</summary>
    public static IReadOnlyList<ChartSpec> ForGroup(Session s, string groupId)
    {
        var list = new List<ChartSpec> { new("mood:" + groupId, t => t.T("ui.chart.group_mood"), "0.00", 1, x => One(History.GroupKey(groupId), x.T.Name(groupId), InkLine), t => t.T("ui.chart.tab_mood")) };
        if (s.HasPolitics && s.History.Series(History.SupportKey(groupId)).Any(double.IsFinite))
            list.Add(new("support:" + groupId, t => t.T("ui.chart.group_support"), "0.0", 100, x => One(History.SupportKey(groupId), x.T.T("ui.chart.group_support"), Ui.PartyColor(x, x.Politics!.PlayerParty)), t => t.T("ui.chart.tab_support")));
        return list;
    }

    /// <summary>A party: its poll and, while it governs with partners, their loyalty.</summary>
    public static IReadOnlyList<ChartSpec> ForParty(Session s, string partyId)
    {
        var list = new List<ChartSpec> { new("poll:" + partyId, t => t.T("ui.chart.party_poll"), "0.0", 100, x => One(History.PollKey(partyId), x.T.Name(partyId), Ui.PartyColor(x, partyId)), t => t.T("ui.chart.tab_poll")) };
        if (s.History.Series(History.LoyaltyKey(partyId)).Any(double.IsFinite))
            list.Add(new("loyalty:" + partyId, t => t.T("ui.chart.loyalty"), "0", 1, x => One(History.LoyaltyKey(partyId), x.T.Name(partyId), Ui.PartyColor(x, partyId)), t => t.T("ui.chart.tab_loyalty")));
        return list;
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
