using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>The people (decision 0021): how many there are, how old, how well off, what moves them, and the private pension funds.</summary>
public sealed class PopulationScreen : Screen
{
    public override string TitleKey => "ui.client.tab.population";
    public override IconKind Icon => IconKind.Family;
    public override string TabId => "population";
    public override float Width => 1080;

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        var v = s.Country.Values;
        double V(string id) => v.GetValueOrDefault(id);
        d.Body.AddChild(Ui.L(t.T("ui.pop.hint"), Tx.Body, AppTheme.Ink2, true));

        var tiles = new FlowGrid { MinColumn = 190, MaxColumns = 4, Gap = 10 };
        void Tile(string label, string figure, string unit, string? note = null)
        {
            var box = Ui.VBox(2);
            box.AddChild(Ui.L(label, Tx.Overline, AppTheme.Ink3));
            var row = Ui.HBox(6);
            row.AddChild(Ui.L(figure, Tx.FigureXl));
            if (unit.Length > 0) { var u = Ui.L(unit, Tx.Label, AppTheme.Ink2); u.SizeFlagsVertical = SizeFlags.ShrinkEnd; row.AddChild(u); }
            box.AddChild(row);
            if (note != null) box.AddChild(Ui.L(note, Tx.Small, AppTheme.Ink3, true));
            tiles.AddChild(Ui.Panel(Ui.Margin(box, 10, 8), "Inset"));
        }
        Tile(t.T("ui.pop.people"), Ui.Num(V("base:population") / 1e6, "0.00"), t.T("ui.pop.million"));
        Tile(t.T("ui.pop.pensioners"), Ui.Num(V("base:share_pensioners") * 100, "0.0"), "%", t.T("ui.pop.pensioners_note"));
        Tile(t.T("ui.pop.children"), Ui.Num(V("base:share_children") * 100, "0.0"), "%", t.T("ui.pop.children_note"));
        Tile(t.T("ui.pop.fertility"), Ui.Num(V("base:fertility"), "0.00"), t.T("ui.pop.per_woman"));
        Tile(t.T("ui.pop.life"), Ui.Num(V("base:life_expectancy"), "0.0"), t.T("ui.pop.years"));
        Tile(t.T("ui.pop.migration"), Ui.Signed(V("base:net_migration"), "0.0"), t.T("ui.pop.per_1000"));
        Tile(t.T("ui.pop.up"), Ui.Num(V("base:mobility_up"), "0.0"), t.T("ui.pop.pct_year"), t.T("ui.pop.up_note"));
        Tile(t.T("ui.pop.down"), Ui.Num(V("base:mobility_down"), "0.0"), t.T("ui.pop.pct_year"), t.T("ui.pop.down_note"));
        d.Body.AddChild(tiles);

        d.Body.AddChild(Ui.Section(t.T("ui.pop.ages")));
        d.Body.AddChild(new StackBar(new List<(string, double, Color)>
        {
            (t.Name("base:share_children"), V("base:share_children"), Ui.PaletteColor(0)), (t.Name("base:share_young"), V("base:share_young"), Ui.PaletteColor(1)),
            (t.Name("base:share_working"), V("base:share_working"), Ui.PaletteColor(2)), (t.Name("base:share_pensioners"), V("base:share_pensioners"), Ui.PaletteColor(3)),
        }));
        d.Body.AddChild(Ui.Section(t.T("ui.pop.classes")));
        var classes = DemographyClasses.Select((id, i) => (t.Name(id), V(id), Ui.PaletteColor(4 + i))).ToList();
        d.Body.AddChild(new StackBar(classes));
        d.Body.AddChild(Ui.L(t.T("ui.pop.classes_hint"), Tx.Small, AppTheme.Ink3, true));
        var specs = new[] { "population", "ages", "classes" }.Select(id => Charts.Available(s).FirstOrDefault(c => c.Id == id)).OfType<ChartSpec>().ToList();
        if (specs.Count > 0 && s.History.Turns.Count > 1) d.Body.AddChild(new ChartPanel(s, "population", specs, Layout.Short ? 150 : 200));

        d.Body.AddChild(Ui.Section(t.T("ui.pop.pensions")));
        d.Body.AddChild(Ui.L(t.T("ui.pop.pensions_hint"), Tx.Small, AppTheme.Ink3, true));
        var pensions = new FlowGrid { MinColumn = 190, MaxColumns = 4, Gap = 10 };
        void PTile(string label, string figure, string unit)
        {
            var box = Ui.VBox(2);
            box.AddChild(Ui.L(label, Tx.Overline, AppTheme.Ink3));
            var row = Ui.HBox(6);
            row.AddChild(Ui.L(figure, Tx.FigureXl));
            if (unit.Length > 0) { var u = Ui.L(unit, Tx.Label, AppTheme.Ink2); u.SizeFlagsVertical = SizeFlags.ShrinkEnd; row.AddChild(u); }
            box.AddChild(row);
            pensions.AddChild(Ui.Panel(Ui.Margin(box, 10, 8), "Inset"));
        }
        var (ff, fu) = Ui.Money(t, V("base:pension_fund"));
        double funded = V("base:pension_funded_share"), capital = Math.Max(V("base:capital"), 1.0);
        PTile(t.T("ui.pop.saving_share"), Ui.Num(V("base:pension_participation") * 100, "0"), "%");
        PTile(t.T("ui.pop.fund"), ff, fu);
        PTile(t.T("ui.pop.fund_share"), Ui.Num(V("base:pension_fund") / capital * 100, "0.0"), "%");
        PTile(t.T("ui.pop.funded"), Ui.Num(funded * 100, "0.0"), "%");
        PTile(t.T("ui.pop.burden"), Ui.Num((1.0 - Math.Min(0.6, 0.8 * funded)) * 100, "0"), "%");
        d.Body.AddChild(pensions);
        d.Body.AddChild(Ui.L(t.T("ui.pop.fund_open"), Tx.Overline, AppTheme.Ink2));
        d.Body.AddChild(ValueDialog.ModelCard(t, s.Game.ExplainPensions()));
        var levers = new FlowGrid { MinColumn = 260, MaxColumns = 3, Gap = 8 };
        foreach (var id in new[] { "base:pension_subsidy", "base:pension_tax_relief", "base:mandatory_pension_pillar", "base:pensions", "base:retirement_age" })
        {
            if (s.Packs.Policies.FirstOrDefault(p => p.Id == id) is not { } p) continue;
            string pid = id;
            var b = new Button { ThemeTypeVariation = "ButtonCard", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44), MouseDefaultCursorShape = CursorShape.PointingHand };
            var row = Ui.HBox(8);
            row.AddChild(new PictoDisc(Icons.For(id), 26));
            var name = Ui.L(t.Name(id), Tx.BodyStrong); name.SizeFlagsHorizontal = SizeFlags.ExpandFill; name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            row.AddChild(name);
            row.AddChild(Ui.L(s.Game.IsActive(id) ? Ui.Fix(Fmt.Policy(p, s.Country.Policies.GetValueOrDefault(id, p.Default), t)) : t.T("ui.laws.not_in_force"), Tx.Data, AppTheme.Ink2));
            var m = Ui.Margin(row, 10, 6); m.SetAnchorsPreset(Control.LayoutPreset.FullRect); Detail.SetIgnore(m); b.AddChild(m);
            b.Pressed += () => main.OpenDetail("policy", pid);
            levers.AddChild(b);
        }
        d.Body.AddChild(levers);
    }

    private static readonly string[] DemographyClasses = { "base:class_poor", "base:class_low", "base:class_middle", "base:class_upper", "base:class_high" };
}
