using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

public partial class VotersScreen : Screen
{
    public override string TitleKey => "ui.client.tab.voters";
    private readonly VBoxContainer _body;

    public VotersScreen()
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
        _body.AddChild(Ui.Heading(t.T("ui.heading.voters")));
        _body.AddChild(Ui.Text(t.T("ui.client.voters_hint"), Ui.Muted, 0, true));
        foreach (var g in s.Packs.VoterGroups.OrderByDescending(g => g.Size).ThenBy(g => g.Id, StringComparer.Ordinal))
        {
            double sat = s.Country.Satisfaction.GetValueOrDefault(g.Id);
            var row = Ui.HBox(12);
            var name = Ui.Text(t.Name(g.Id));
            name.CustomMinimumSize = new Vector2(300, 0);
            name.TooltipText = t.Description(g.Id) ?? "";
            row.AddChild(name);
            row.AddChild(Ui.Text(Fmt.Number(g.Size * 100, "0") + " %"));
            var mood = Ui.Text(t.T(Moods.Key(sat)));
            mood.CustomMinimumSize = new Vector2(110, 0);
            row.AddChild(mood);
            row.AddChild(Ui.Bar(sat, -1, 1, sat < -0.2 ? Ui.Bad : sat > 0.2 ? Ui.Good : Ui.Warn, 220));
            _body.AddChild(row);
        }
    }
}
