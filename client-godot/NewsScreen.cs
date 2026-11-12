using Godot;

namespace OpenPolitics.Client;

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
        _body.AddChild(Ui.Heading(t.T("ui.client.tab.news")));
        if (s.News.Count == 0) { _body.AddChild(Ui.Text(t.T("ui.client.no_news"), Ui.Muted)); return; }
        var sc = s.Packs.GetScenario(s.ScenarioId);
        foreach (var (turn, text) in Enumerable.Reverse(s.News))
        {
            int tpy = sc.TurnsPerYear, back = turn - 1; // the report of turn N describes the quarter that ended at turn N
            _body.AddChild(Ui.Text($"{t.T("ui.date", sc.StartYear + back / tpy, back % tpy + 1)}:  {text}", null, 0, true));
        }
    }
}
