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
        Exclusive = false; Transient = true; Borderless = true; Transparent = true; TransparentBg = true;
        Theme = AppTheme.Instance;
        CloseRequested += QueueFree;

        var root = Ui.VBox(14);
        var head = Ui.DialogHeader(IconKind.Chart, spec.Title(s.T), null, QueueFree, s.T.T("ui.client.close"));
        int tpy = s.Packs.GetScenario(s.ScenarioId).TurnsPerYear;
        var seg = Ui.HBox(2);
        var group = new ButtonGroup();
        foreach (var (key, turns) in new[] { ("ui.chart.range.1y", tpy), ("ui.chart.range.3y", 3 * tpy), ("ui.chart.range.5y", 5 * tpy), ("ui.chart.range.all", 0) })
        {
            int r = turns;
            var b = new Button { Text = s.T.T(key), ToggleMode = true, ButtonGroup = group, ButtonPressed = r == 0, ThemeTypeVariation = "Ghost", FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 34), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            b.Pressed += () => { _range = r; Fill(); };
            seg.AddChild(b);
        }
        head.AddChild(seg);
        head.MoveChild(seg, head.GetChildCount() - 2);
        root.AddChild(head);
        root.AddChild(_chart);
        _legend.AddThemeConstantOverride("h_separation", 18);
        _legend.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(_legend);
        Ui.FillWindow(this, root, 22);
        Fill();
    }

    private void Fill()
    {
        var (turns, series) = Charts.Build(_s, _spec, _range);
        _chart.Set(_spec.Title(_s.T), turns, series, _spec.Format, _s.ShortDateAt);
        Ui.Clear(_legend);
        foreach (var line in series)
        {
            var item = Ui.HBox(6);
            item.AddChild(new ColorDot(line.Color));
            item.AddChild(Ui.Text(line.Name, AppTheme.Text2, 14));
            _legend.AddChild(item);
        }
    }

    public static void Open(Control host, Session s, ChartSpec spec)
    {
        var dialog = new ChartDialog(s, spec);
        host.GetTree().Root.AddChild(dialog);
        dialog.PopupCentered();
    }
}
