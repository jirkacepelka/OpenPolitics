using Godot;

namespace OpenPolitics.Client;

/// <summary>Every report so far, newest first, grouped by quarter on a timeline.</summary>
public partial class NewsScreen : Screen
{
    public override string TitleKey => "ui.client.tab.news";
    private readonly VBoxContainer _body;

    public NewsScreen()
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
        if (s.News.Count == 0) { _body.AddChild(Ui.Text(t.T("ui.client.no_news"), Ui.Muted, 14)); return; }
        var sc = s.Packs.GetScenario(s.ScenarioId);
        foreach (var quarter in Enumerable.Reverse(s.News).GroupBy(n => n.Turn))
        {
            int tpy = sc.TurnsPerYear, back = quarter.Key - 1; // the report of turn N describes the quarter that ended at turn N
            var row = Ui.HBox(16);
            var date = Ui.VBox(4);
            date.CustomMinimumSize = new Vector2(84, 0);
            date.AddChild(Ui.Pill(s.ShortDateAt(back), AppTheme.Accent));
            date.TooltipText = t.T("ui.date", sc.StartYear + back / tpy, back % tpy + 1);
            row.AddChild(date);
            var items = Ui.VBox(8);
            items.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            foreach (var (_, text) in quarter)
            {
                var line = Ui.HBox(10);
                var dot = new IconView(IconKind.News, 14, AppTheme.Muted);
                dot.SizeFlagsVertical = SizeFlags.ShrinkBegin;
                line.AddChild(dot);
                line.AddChild(Ui.Text(text, AppTheme.Text, 14, true));
                items.AddChild(line);
            }
            row.AddChild(Ui.Card(items, null, true, 14));
            _body.AddChild(row);
        }
    }
}
