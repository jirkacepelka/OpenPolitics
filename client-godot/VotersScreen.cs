using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>The voter groups as cards: who they are, how many, how they feel.</summary>
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
        _body.AddChild(Ui.Text(t.T("ui.client.voters_hint"), AppTheme.Text2, 14, true));
        foreach (var g in s.Packs.VoterGroups.OrderByDescending(g => g.Size).ThenBy(g => g.Id, StringComparer.Ordinal))
        {
            double sat = s.Country.Satisfaction.GetValueOrDefault(g.Id);
            var color = Ui.MoodColor(sat);
            var row = Ui.HBox(14);
            row.AddChild(new IconBadge(Icons.For(g.Id), color, 44) { Circle = true });
            var names = Ui.VBox(2);
            names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            names.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            names.AddChild(Ui.Text(t.Name(g.Id), Colors.White, AppTheme.Subhead, false, AppTheme.Semi));
            names.AddChild(Ui.Caption(t.T("ui.client.voter_share", Fmt.Number(g.Size * 100, "0"))));
            row.AddChild(names);
            var mood = Ui.VBox(6);
            mood.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            mood.AddChild(Ui.Pill(t.T(Moods.Key(sat)), color));
            mood.AddChild(Ui.MoodBar(sat, 200));
            row.AddChild(mood);
            var card = Ui.Card(row, null, true, 14);
            card.TooltipText = t.Description(g.Id) ?? "";
            card.MouseFilter = MouseFilterEnum.Pass;
            _body.AddChild(card);
        }
    }
}
