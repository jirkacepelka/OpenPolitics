using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>
/// The laws that are not in force (decision 0021), like Democracy 4's list of policies you can add: the map shows only what is in force, and
/// here every other law sits with its introduction setting, what it costs and a button to bring it in. Repealing is in a law's own dialog.
/// </summary>
public sealed class LawsScreen : Screen
{
    public override string TitleKey => "ui.client.tab.laws";
    public override IconKind Icon => IconKind.Policies;
    public override string TabId => "laws";
    public override float Width => 1100;

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        var idle = s.Packs.Policies.Where(p => !s.Game.IsActive(p.Id)).ToList();
        int inForce = s.Packs.Policies.Count - idle.Count;
        d.Body.AddChild(Ui.L(t.T("ui.laws.hint", inForce, idle.Count), Tx.Body, AppTheme.Ink2, true));
        if (idle.Count == 0) { d.Body.AddChild(Ui.L(t.T("ui.laws.none"), Tx.BodyStrong, AppTheme.Ink, true)); return; }
        double capital = s.Politics?.Party?.PoliticalCapital ?? double.MaxValue;
        foreach (var grp in idle.GroupBy(WorldMap.GroupOf).OrderBy(g => g.Key))
        {
            d.Body.AddChild(Ui.Section(t.T("ui.client.group." + grp.Key)));
            var grid = new FlowGrid { MinColumn = 330, MaxColumns = 3, Gap = 10 };
            foreach (var p in grp.OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal))
            {
                string id = p.Id;
                var card = Ui.VBox(6);
                var head = Ui.HBox(10);
                head.AddChild(new PictoDisc(Icons.For(id), 34));
                var name = Ui.L(t.Name(id), Tx.BodyStrong);
                name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; name.SizeFlagsHorizontal = SizeFlags.ExpandFill; name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                head.AddChild(name);
                card.AddChild(head);
                if (t.Description(id) is { } desc) card.AddChild(Ui.L(desc, Tx.Small, AppTheme.Ink2, true));
                card.AddChild(Ui.L(t.T("ui.laws.starts_at", Ui.Fix(Fmt.Policy(p, s.Game.IntroductionValue(id), t))), Tx.Small, AppTheme.Ink3));
                var row = Ui.HBox(8);
                double cost = s.Game.IntroductionCost(id);
                var intro = Ui.Costed(t.T("ui.law.introduce"), (int)cost, () => s.Do(new IntroducePolicy(id), t.T("ui.law.introduced", t.Name(id))),
                    t.T("ui.law.introduce_hint"), cost > capital + 1e-9, "ButtonPlay");
                intro.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(intro);
                row.AddChild(Ui.Btn("", () => main.OpenDetail("policy", id), "ButtonSecondary", IconKind.Search, t.T("ui.map.details"), false, 34));
                card.AddChild(row);
                grid.AddChild(Ui.Panel(Ui.Margin(card, 12, 10), "Card"));
            }
            d.Body.AddChild(grid);
        }
    }
}
