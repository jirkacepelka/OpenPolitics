using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>The voter groups: the electorate as one stacked bar, then every group with its mood, size and what it likes most and least.</summary>
public sealed class VotersScreen : Screen
{
    public override string TitleKey => "ui.client.tab.voters";
    public override IconKind Icon => IconKind.Voters;
    public override string TabId => "voters";
    public override float Width => 1000;

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        d.Body.AddChild(Ui.L(t.T("ui.client.voters_hint"), Tx.Body, AppTheme.Ink2, true));
        var groups = s.Packs.VoterGroups.OrderByDescending(g => g.Size).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();
        d.Body.AddChild(new StackBar(groups.Select((g, i) => (t.Name(g.Id), g.Size, Ui.PaletteColor(i))).ToList()));
        var moods = Charts.Available(s).Where(c => c.Id is "groups" or "mood" or "group_support").OrderBy(c => c.Id == "groups" ? 0 : c.Id == "mood" ? 1 : 2).ToList();
        if (moods.Count > 0 && s.History.Turns.Count > 1) d.Body.AddChild(new ChartPanel(s, "voters", moods, Layout.Short ? 150 : 200));
        d.Body.AddChild(Ui.Section(t.T("ui.vp.groups")));
        foreach (var g in groups)
        {
            double sat = s.Country.Satisfaction.GetValueOrDefault(g.Id);
            var likes = s.Packs.Policies.Where(p => g.PolicyWeights.ContainsKey(p.Id)).Select(p => (p, op: Ui.Opinion(s, g, p))).OrderByDescending(x => x.op).ToList();
            string like = string.Join(", ", likes.Where(x => x.op > 0.1).Take(2).Select(x => t.Name(x.p.Id)));
            string dislike = string.Join(", ", likes.Where(x => x.op < -0.1).OrderBy(x => x.op).Take(2).Select(x => t.Name(x.p.Id)));
            string id = g.Id;
            var row = Detail.EffectRow(Icons.For(id), $"{t.Name(id)} · {Ui.Num(g.Size * 100, "0")} %", sat, Ui.Signed(sat, "0.00"), () => main.OpenDetail("group", id),
                $"{t.Name(id)}\n{t.T(Moods.Key(sat))}" + (like.Length > 0 ? $"\n{t.T("ui.vp.likes")}: {like}" : "") + (dislike.Length > 0 ? $"\n{t.T("ui.vp.dislikes")}: {dislike}" : ""), true, 270);
            var item = Ui.VBox(0);
            item.AddChild(row);
            if (like.Length + dislike.Length > 0)
            {
                var sub = Ui.L((like.Length > 0 ? $"{t.T("ui.vp.likes")}: {like}" : "") + (like.Length > 0 && dislike.Length > 0 ? "   ·   " : "") + (dislike.Length > 0 ? $"{t.T("ui.vp.dislikes")}: {dislike}" : ""), Tx.Small, AppTheme.Ink3);
                sub.ClipText = true; sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                item.AddChild(Ui.Margin(sub, 36, 0, 0, 2));
            }
            d.Body.AddChild(item);
        }
    }
}

/// <summary>A 100 % stacked bar with inline labels where they fit.</summary>
public partial class StackBar : Control
{
    private readonly List<(string Name, double Share, Color Color)> _parts;
    public StackBar(List<(string, double, Color)> parts)
    {
        _parts = parts; CustomMinimumSize = new Vector2(200, 34); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        TooltipText = string.Join("\n", parts.Select(p => $"{p.Item1}: {Ui.Num(p.Item2 * 100, "0")} %"));
    }
    public override void _Draw()
    {
        double total = Math.Max(1e-9, _parts.Sum(p => p.Share));
        float x = 0;
        foreach (var (name, share, color) in _parts)
        {
            float w = (float)(share / total) * Size.X;
            DrawRect(new Rect2(x, 0, w - 1, Size.Y), color);
            string label = $"{name} {Ui.Num(share * 100, "0")} %";
            if (AppTheme.Semi.GetStringSize(label, HorizontalAlignment.Left, -1, 12).X < w - 10)
                DrawString(AppTheme.Semi, new Vector2(x + 6, Size.Y / 2 + 4), label, HorizontalAlignment.Left, w - 8, 12, Colors.White);
            x += w;
        }
    }
    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
}

/// <summary>Every chart as a tile; click for the large view.</summary>
public sealed class ChartsScreen : Screen
{
    public override string TitleKey => "ui.client.charts";
    public override IconKind Icon => IconKind.Chart;
    public override string TabId => "charts";
    public override float Width => 1180;

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        if (s.Turn == 0) d.Body.AddChild(Ui.L(t.T("ui.client.charts_hint"), Tx.Body, AppTheme.Ink2, true));
        var available = Charts.Available(s).ToList();
        foreach (var group in Charts.Groups)
        {
            var specs = available.Where(c => Charts.GroupOf(c) == group).ToList();
            if (specs.Count == 0) continue;
            d.Body.AddChild(Ui.L(t.T("ui.chart.group." + group), Tx.Overline, AppTheme.Ink2));
            var grid = new FlowGrid { MinColumn = 230, MaxColumns = 5, Gap = 10 };
            foreach (var spec in specs)
            {
                var chart = new Chart { Tile = true, DeltaSteps = s.Packs.GetScenario(s.ScenarioId).TurnsPerYear, CustomMinimumSize = new Vector2(200, Layout.Short ? 130 : 150) };
                var (turns, series) = Charts.Build(s, spec, 0);
                chart.Set(spec.Title(t), turns, series, spec.Format, s.ShortDateAt);
                var captured = spec;
                chart.Clicked += () => main.OpenChart(captured);
                grid.AddChild(chart);
            }
            d.Body.AddChild(grid);
        }
    }
}

/// <summary>Income and expenditure, the balance and the debt, then every spending and tax policy with its size.</summary>
public sealed class BudgetScreen : Screen
{
    public override string TitleKey => "ui.tab.budget";
    public override IconKind Icon => IconKind.Coin;
    public override string TabId => "budget";
    public override float Width => 1080;

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        var v = s.Country.Values;
        var f = s.Figures;
        var tiles = new FlowGrid { MinColumn = 200, MaxColumns = 4, Gap = 10 };
        void Tile(string label, string fig, string unit, Color? color = null, string? note = null)
        {
            var box = Ui.VBox(2);
            box.AddChild(Ui.L(label, Tx.Overline, AppTheme.Ink3));
            var row = Ui.HBox(6);
            row.AddChild(Ui.L(fig, Tx.FigureXl, color ?? AppTheme.Ink));
            if (unit.Length > 0) { var u = Ui.L(unit, Tx.Label, AppTheme.Ink2); u.SizeFlagsVertical = SizeFlags.ShrinkEnd; row.AddChild(u); }
            box.AddChild(row);
            if (note != null) box.AddChild(Ui.L(note, Tx.Small, null, true));
            tiles.AddChild(Ui.Panel(box, "Inset"));
        }
        var (rf, ru) = Ui.Money(t, v.GetValueOrDefault("base:revenue"));
        var (sf, su) = Ui.Money(t, v.GetValueOrDefault("base:spending"));
        Tile(t.T("ui.fig.income"), rf, ru);
        Tile(t.T("ui.fig.expenditure"), sf, su, null, t.T("ui.bp.spending_note"));
        if (f.DebtInterest is { } interest)
        {
            var (inf, inu) = Ui.Money(t, interest);
            Tile(t.T("ui.fig.interest"), inf, inu, null, t.T("ui.bp.interest_note", Ui.Num(Math.Max(0, v.GetValueOrDefault("base:interest_rate") - v.GetValueOrDefault("base:inflation")))));
        }
        // decision 0015: the deficit once, as in the top bar (positive = deficit), not a balance with the opposite sign beside it
        if (v.TryGetValue("base:deficit_pct", out var deficit))
        {
            if (deficit < 0) Tile(t.T("ui.fig.surplus"), Ui.Num(-deficit), t.T("ui.unit.gdp_short"), AppTheme.GoodText, t.T("ui.bp.surplus_note"));
            else Tile(t.T("ui.fig.deficit"), Ui.Num(deficit), t.T("ui.unit.gdp_short"), deficit > 3 ? AppTheme.BadText : null, t.T("ui.bp.deficit_note"));
        }
        if (f.DebtPercentOfGdp is { } debt) Tile(t.T("ui.fig.debt"), Ui.Num(debt, "0"), t.T("ui.unit.gdp_short"), debt > 60 ? AppTheme.BadText : null);
        d.Body.AddChild(tiles);
        var trend = new[] { "revenue", "deficit", "debt", "interest_paid" }.Select(id => Charts.Available(s).FirstOrDefault(c => c.Id == id)).OfType<ChartSpec>().ToList();
        if (trend.Count > 0 && s.History.Turns.Count > 1) d.Body.AddChild(new ChartPanel(s, "budget", trend, Layout.Short ? 150 : 190));

        var spend = s.Packs.Policies.Where(p => WorldMap.GroupOf(p) == 1).OrderByDescending(p => s.Country.Policies.GetValueOrDefault(p.Id) * p.CostPerUnit).ToList();
        var taxes = s.Packs.Policies.Where(p => WorldMap.GroupOf(p) == 0).OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal).ToList();
        Control List(string title, List<PolicyDef> list, Func<PolicyDef, double> size)
        {
            var box = Ui.VBox(2);
            box.AddChild(Ui.Section(title));
            double max = Math.Max(1e-9, list.Select(size).DefaultIfEmpty(0).Max());
            foreach (var p in list)
            {
                double now = s.Country.Policies.GetValueOrDefault(p.Id, p.Default);
                string id = p.Id;
                var row = Ui.HBox(10);
                row.AddChild(new PictoDisc(Icons.For(id), 26));
                var name = Ui.L(t.Name(id), Tx.BodyStrong);
                name.CustomMinimumSize = new Vector2(Layout.Compact ? 120 : 190, 0); name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                row.AddChild(name);
                row.AddChild(new Meter(size(p), 0, max, AppTheme.Chrome, 80, false, 10) { SizeFlagsHorizontal = SizeFlags.ExpandFill });
                var val = Ui.Right(Ui.L(Ui.Fix(Fmt.Policy(p, now, t)), Tx.Data));
                val.CustomMinimumSize = new Vector2(96, 0);
                row.AddChild(val);
                var b = new Button { ThemeTypeVariation = "ButtonGhost", CustomMinimumSize = new Vector2(0, 34), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = t.Name(id) };
                b.AddChild(row); row.SetAnchorsPreset(Control.LayoutPreset.FullRect); Detail.SetIgnore(row);
                b.Pressed += () => main.OpenDetail("policy", id);
                box.AddChild(b);
            }
            return box;
        }
        var left = List(t.T("ui.client.group.1"), spend, p => s.Country.Policies.GetValueOrDefault(p.Id) * Math.Abs(p.CostPerUnit));
        var right = List(t.T("ui.client.group.0"), taxes, p => s.Country.Policies.GetValueOrDefault(p.Id) - p.Min);
        d.Body.AddChild(Detail.Columns(left, right, 1.15f));
        if (Charts.All.FirstOrDefault(c => c.Id == "revenue") is { } spec)
            d.AddAction(t.T("ui.map.open_chart"), IconKind.Chart, () => main.OpenChart(spec));
    }
}

/// <summary>Every report so far, newest first, grouped by quarter.</summary>
public sealed class NewsScreen : Screen
{
    public override string TitleKey => "ui.client.tab.news";
    public override IconKind Icon => IconKind.News;
    public override string TabId => "news";
    public override float Width => 860;

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        if (s.Turn == 0) d.Body.AddChild(Ui.L(s.Politics is { } wp ? s.Texts.ForRole("ui.client.welcome_politics", wp.PlayerRole, t.Name(wp.PlayerParty)) : t.T("ui.client.welcome"), Tx.Body, AppTheme.Ink, true));
        if (s.News.Count == 0) { d.Body.AddChild(Ui.L(t.T("ui.client.no_news"), Tx.Body, AppTheme.Ink3)); return; }
        var sc = s.Packs.GetScenario(s.ScenarioId);
        foreach (var quarter in Enumerable.Reverse(s.News).GroupBy(n => n.Turn))
        {
            int back = quarter.Key - 1;
            d.Body.AddChild(Ui.Section(t.T("ui.date", sc.StartYear + back / sc.TurnsPerYear, back % sc.TurnsPerYear + 1)));
            foreach (var (_, text) in quarter)
            {
                var line = Ui.HBox(12);
                bool alert = text.Contains("ultim", StringComparison.OrdinalIgnoreCase);
                var icon = new PictoDisc(alert ? IconKind.Alert : IconKind.News, 28, alert ? new Color(AppTheme.Warn, 0.16f) : AppTheme.Panel, alert ? AppTheme.WarnText : AppTheme.Ink2) { SizeFlagsVertical = SizeFlags.ShrinkBegin };
                line.AddChild(icon);
                line.AddChild(Ui.L(Ui.Fix(text), Tx.Body, AppTheme.Ink, true));
                d.Body.AddChild(line);
            }
        }
    }
}
