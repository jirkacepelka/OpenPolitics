using Godot;

namespace OpenPolitics.Client;

/// <summary>
/// The large view of a chart as a dialog. A row of groups (economy, budget, society, politics) and, below, the charts of the chosen group as
/// tabs: switching replaces the chart in place, so only one is on show.
/// </summary>
public static class ChartDialog
{
    public static Dialog Open(Main main, Session s, ChartSpec spec)
    {
        var t = s.T;
        var d = main.NewDialog(spec.Title(t), IconKind.Chart, 1000);
        d.Key = "chart:" + spec.Id;
        d.FillHeight = true;
        var available = Charts.Available(s).ToList();
        if (!available.Any(x => x.Id == spec.Id)) available.Add(spec);
        var groups = Charts.Groups.Where(g => available.Any(c => Charts.GroupOf(c) == g)).ToList();
        string group = Charts.GroupOf(spec);
        var holder = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        float height = Math.Max(240, Layout.Size.Y - (Layout.Short ? 280 : 340));
        void Show(string? select)
        {
            Ui.Clear(holder);
            var specs = available.Where(c => Charts.GroupOf(c) == group).ToList();
            var panel = new ChartPanel(s, "dialog:" + group, specs, height, fill: true, select: select);
            panel.SpecChanged += sp => d.Title = sp.Title(t);
            if (panel.Current != null) d.Title = panel.Current.Title(t);
            holder.AddChild(panel);
        }
        if (groups.Count > 1)
            d.Body.AddChild(Ui.Segmented(groups.Select(g => t.T("ui.chart.group." + g)).ToList(), Math.Max(0, groups.IndexOf(group)),
                i => { group = groups[i]; Show(null); }, 32));
        d.Body.AddChild(holder);
        d.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Show(spec.Id);
        return d;
    }
}
