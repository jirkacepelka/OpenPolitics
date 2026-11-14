using Godot;

namespace OpenPolitics.Client;

/// <summary>The large view of a chart: pick how far back to look, hover to read values.</summary>
public partial class ChartDialog : Window
{
    private readonly Session _s;
    private readonly ChartSpec _spec;
    private readonly Chart _chart = new(large: true);
    private readonly HFlowContainer _legend = new();
    private int _range;

    public ChartDialog(Session s, ChartSpec spec)
    {
        _s = s; _spec = spec;
        Title = spec.Title(s.T);
        Size = new Vector2I(940, 620);
        Exclusive = false;
        Transient = true;
        CloseRequested += QueueFree;

        var root = Ui.VBox(10);
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bar = Ui.HBox(8);
        bar.AddChild(Ui.Text(spec.Title(s.T), Colors.White, 22));
        bar.AddChild(Ui.Spacer());
        int tpy = s.Packs.GetScenario(s.ScenarioId).TurnsPerYear;
        foreach (var (key, turns) in new[] { ("ui.chart.range.1y", tpy), ("ui.chart.range.3y", 3 * tpy), ("ui.chart.range.5y", 5 * tpy), ("ui.chart.range.all", 0) })
        {
            int r = turns;
            bar.AddChild(Ui.Btn(s.T.T(key), () => { _range = r; Fill(); }));
        }
        root.AddChild(bar);
        root.AddChild(_chart);
        _legend.AddThemeConstantOverride("h_separation", 16);
        root.AddChild(_legend);
        Ui.FillWindow(this, root);
        Fill();
    }

    private void Fill()
    {
        var (turns, series) = Charts.Build(_s, _spec, _range);
        _chart.Set(_spec.Title(_s.T), turns, series, _spec.Format, _s.ShortDateAt);
        Ui.Clear(_legend);
        foreach (var line in series) _legend.AddChild(Ui.Text("■ " + line.Name, line.Color, 14));
    }

    public static void Open(Control host, Session s, ChartSpec spec)
    {
        var dialog = new ChartDialog(s, spec);
        host.GetTree().Root.AddChild(dialog);
        dialog.PopupCentered();
    }
}
