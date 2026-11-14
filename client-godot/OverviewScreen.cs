using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>The state of the nation at a glance: key figures as tiles, the politics in one card, every chart, and the latest news.</summary>
public partial class OverviewScreen : Screen
{
    public override string TitleKey => "ui.client.tab.overview";
    public override float PreferredWidth => 0.7f;
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
            _body.AddChild(Ui.Callout(Ui.Text(t.T(s.HasPolitics ? "ui.client.welcome_politics" : "ui.client.welcome"), AppTheme.Text, 14, true), AppTheme.Accent));

        var tiles = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tiles.AddThemeConstantOverride("h_separation", 12); tiles.AddThemeConstantOverride("v_separation", 12);
        if (f.DebtPercentOfGdp is { } d) tiles.AddChild(Ui.Stat(t.T("ui.line.debt_ratio"), Fmt.Number(d, "0") + " %", d > 120 ? Ui.Bad : null, null, null, IconKind.Bank));
        if (f.BudgetPercentOfGdp is { } b) tiles.AddChild(Ui.Stat(t.T("ui.line.budget"), Fmt.Signed(b) + " %", b < -3 ? Ui.Bad : b >= 0 ? Ui.Good : null, null, null, IconKind.Gauge));
        var moodTile = Ui.Stat(t.T("ui.line.mood"), t.T(Moods.Key(f.Mood)), Ui.MoodColor(f.Mood), null, null, IconKind.Voters);
        ((VBoxContainer)moodTile.GetChild(0)).AddChild(new Meter(f.Mood, -1, 1, Ui.MoodColor(f.Mood), 120, true) { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        tiles.AddChild(moodTile);
        if (s.HasPolitics) PoliticsTiles(s, tiles);
        _body.AddChild(tiles);
        _body.AddChild(Ui.Text(t.T("ui.client.hud_hint"), Ui.Muted, AppTheme.Caption, true));

        _body.AddChild(Ui.Section(t.T("ui.client.charts"), t.T("ui.client.charts_hint")));
        var charts = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        charts.AddThemeConstantOverride("h_separation", 12);
        charts.AddThemeConstantOverride("v_separation", 12);
        foreach (var spec in Charts.Available(s))
        {
            var chart = new Chart { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var (turns, series) = Charts.Build(s, spec, 0);
            chart.Set(spec.Title(t), turns, series, spec.Format, s.ShortDateAt);
            var captured = spec;
            chart.Clicked += () => ChartDialog.Open(this, s, captured);
            charts.AddChild(chart);
        }
        _body.AddChild(charts);

        _body.AddChild(Ui.Section(t.T("ui.heading.events")));
        var news = Ui.VBox(10);
        bool any = false;
        foreach (var (turn, text) in s.News.Where(n => s.Last != null && n.Turn == s.Last.Turn))
        {
            var row = Ui.HBox(10);
            row.AddChild(new IconView(IconKind.News, 16, AppTheme.Accent));
            row.AddChild(Ui.Text(text, AppTheme.Text, 14, true));
            news.AddChild(row);
            any = true;
        }
        if (!any) news.AddChild(Ui.Text(t.T("ui.client.no_news"), Ui.Muted, 14));
        _body.AddChild(Ui.Card(news));
    }

    private static void PoliticsTiles(Session s, GridContainer tiles)
    {
        var t = s.T;
        var pol = s.Politics!;
        tiles.AddChild(Ui.Stat(t.T("ui.line.next_election"),
            pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.politics.election_in", pol.TurnsToElection), null, null, null, IconKind.Ballot));
        int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p));
        var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
        bool majority = held > total * rules.MajorityFraction;
        tiles.AddChild(Ui.Stat(t.T("ui.politics.government"), $"{held} / {total}", majority ? Ui.Good : Ui.Bad,
            t.T(majority ? "ui.politics.majority" : "ui.politics.no_majority"), majority ? Ui.Good : Ui.Bad, IconKind.Politics));
        if (pol.Party != null)
            tiles.AddChild(Ui.Stat(t.T("ui.party.capital"), Fmt.Number(pol.Party.PoliticalCapital), AppTheme.Warn,
                $"{t.T("ui.party.organisation")}: {Fmt.Number(pol.Party.Organization * 100, "0")} %", null, IconKind.Coin));
    }
}
