using Godot;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>The large view of a chart as a dialog: range segments, clickable legend chips that toggle series, crosshair readout.</summary>
public static class ChartDialog
{
    public static Dialog Open(Main main, Session s, ChartSpec spec)
    {
        var t = s.T;
        var d = main.NewDialog(spec.Title(t), IconKind.Chart, 1000);
        d.Key = "chart:" + spec.Id;
        d.FillHeight = true;
        int tpy = s.Packs.GetScenario(s.ScenarioId).TurnsPerYear;
        int range = 0;
        var chart = new Chart(large: true) { CustomMinimumSize = new Vector2(300, Math.Max(240, Layout.Size.Y - (Layout.Short ? 230 : 270))) };
        var legend = new HFlowContainer();
        legend.AddThemeConstantOverride("h_separation", 6); legend.AddThemeConstantOverride("v_separation", 6);
        void Fill()
        {
            var (turns, series) = Charts.Build(s, spec, range);
            chart.Set(spec.Title(t), turns, series, spec.Format, s.ShortDateAt);
            Ui.Clear(legend);
            if (series.Count <= 1) return;
            foreach (var line in series)
            {
                var b = new IconTextButton(null) { Text = "●  " + line.Name, ToggleMode = true, ButtonPressed = !chart.Hidden.Contains(line.Name), ThemeTypeVariation = "ButtonSecondary", FocusMode = Control.FocusModeEnum.None };
                foreach (var fc in new[] { "font_color", "font_pressed_color", "font_hover_color", "font_hover_pressed_color" }) b.AddThemeColorOverride(fc, line.Color);
                b.AddThemeStyleboxOverride("pressed", AppTheme.Box(AppTheme.Surface, 0, line.Color, 2, 12, 4));
                b.AddThemeStyleboxOverride("hover_pressed", AppTheme.Box(AppTheme.Panel, 0, line.Color, 2, 12, 4));
                b.Modulate = b.ButtonPressed ? Colors.White : new Color(1, 1, 1, 0.45f);
                b.CustomMinimumSize = new Vector2(0, 28);
                string name = line.Name;
                b.Toggled += on => { if (on) chart.Hidden.Remove(name); else chart.Hidden.Add(name); b.Modulate = on ? Colors.White : new Color(1, 1, 1, 0.45f); chart.QueueRedraw(); };
                legend.AddChild(b);
            }
        }
        d.HeaderExtra.AddChild(Ui.Segmented(new[] { t.T("ui.chart.range.1y"), t.T("ui.chart.range.3y"), t.T("ui.chart.range.5y"), t.T("ui.chart.range.all") }, 3,
            i => { range = i switch { 0 => tpy, 1 => 3 * tpy, 2 => 5 * tpy, _ => 0 }; Fill(); }));
        d.HeaderExtra.AddChild(Ui.Gap(8));
        d.Body.AddChild(legend);
        chart.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        d.Body.AddChild(chart);
        d.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Fill();
        return d;
    }
}
