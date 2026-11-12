using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

public partial class OverviewScreen : Screen
{
    public override string TitleKey => "ui.client.tab.overview";
    private readonly VBoxContainer _body;

    public OverviewScreen()
    {
        var (scroll, body) = Ui.Page();
        _body = body;
        AddChild(scroll);
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void Refresh(Session s)
    {
        Ui.Clear(_body);
        var t = s.T;
        var f = s.Figures;
        if (s.Turn == 0)
        {
            _body.AddChild(Ui.Text(t.T(s.HasPolitics ? "ui.client.welcome_politics" : "ui.client.welcome"), Ui.Warn, 0, true));
            _body.AddChild(Ui.Rule());
        }
        _body.AddChild(Ui.Heading(t.T("ui.heading.overview")));

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        void Row(string key, string value, Color? color = null)
        {
            grid.AddChild(Ui.Text(t.T(key)));
            grid.AddChild(Ui.Text(value, color, 18));
        }
        var values = s.Packs.Values.ToDictionary(v => v.Id);
        if (f.Gdp is { } gdp) Row("ui.line.gdp", Fmt.Value(values["base:gdp"], gdp));
        if (f.GrowthPercent is { } g) Row("ui.line.growth", Fmt.Signed(g) + " %", g < 0 ? Ui.Bad : Ui.Good);
        if (f.Unemployment is { } u) Row("ui.line.unemployment", Fmt.Value(values["base:unemployment"], u));
        if (f.Inflation is { } i) Row("ui.line.inflation", Fmt.Value(values["base:inflation"], i), i > 6 ? Ui.Bad : null);
        if (f.DebtPercentOfGdp is { } d) Row("ui.line.debt_ratio", Fmt.Number(d, "0") + " %", d > 120 ? Ui.Bad : null);
        if (f.BudgetPercentOfGdp is { } b) Row("ui.line.budget", Fmt.Signed(b) + " %", b < -3 ? Ui.Bad : null);
        _body.AddChild(grid);

        var mood = Ui.HBox();
        mood.AddChild(Ui.Text(t.T("ui.line.mood")));
        mood.AddChild(Ui.Text(t.T(Moods.Key(f.Mood)), null, 18));
        mood.AddChild(Ui.Bar(f.Mood, -1, 1, f.Mood < -0.2 ? Ui.Bad : f.Mood > 0.2 ? Ui.Good : Ui.Warn));
        _body.AddChild(mood);

        if (s.HasPolitics) _body.AddChild(PoliticsSummary(s));

        _body.AddChild(Ui.Rule());
        var charts = new HFlowContainer();
        charts.AddThemeConstantOverride("h_separation", 12);
        charts.AddThemeConstantOverride("v_separation", 12);
        void Chart(string key, IReadOnlyList<double> series, string fmt, Color color)
        {
            var sp = new Sparkline { LineColor = color };
            sp.Set(t.T(key), series, fmt);
            charts.AddChild(sp);
        }
        Chart("ui.line.gdp", s.GdpHistory.Select(x => x / 1e12).ToList(), "0.00", Ui.Accent);
        Chart("ui.line.unemployment", s.UnemploymentHistory, "0.0", Ui.Warn);
        Chart("ui.line.inflation", s.InflationHistory, "0.0", Ui.Bad);
        Chart("ui.line.mood", s.MoodHistory, "0.00", Ui.Good);
        _body.AddChild(charts);

        _body.AddChild(Ui.Rule());
        _body.AddChild(Ui.Heading(t.T("ui.heading.events")));
        bool any = false;
        foreach (var (turn, text) in s.News.Where(n => s.Last != null && n.Turn == s.Last.Turn))
        { _body.AddChild(Ui.Text("• " + text, null, 0, true)); any = true; }
        if (!any) _body.AddChild(Ui.Text(t.T("ui.client.no_news"), Ui.Muted));
    }

    private Control PoliticsSummary(Session s)
    {
        var t = s.T;
        var pol = s.Politics!;
        var box = Ui.VBox(4);
        box.AddChild(Ui.Rule());
        box.AddChild(Ui.Heading(t.T("ui.heading.politics")));
        box.AddChild(Ui.Text(t.T("ui.line.next_election") + ": " +
            (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.politics.election_in", pol.TurnsToElection))));
        int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p));
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        bool majority = held > total * rules.MajorityFraction;
        box.AddChild(Ui.Text($"{t.T("ui.politics.government")}: {t.T("ui.politics.seats_of", held, total)} ({t.T(majority ? "ui.politics.majority" : "ui.politics.no_majority")})",
            majority ? Ui.Good : Ui.Bad));
        if (pol.Party != null)
            box.AddChild(Ui.Text($"{t.T("ui.party.capital")}: {Fmt.Number(pol.Party.PoliticalCapital)}   {t.T("ui.party.organisation")}: {Fmt.Number(pol.Party.Organization * 100, "0")} %"));
        return box;
    }
}
