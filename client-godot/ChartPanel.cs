using Godot;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// One chart at a time with its controls: a row of tabs when several charts belong together (switching replaces the chart, nothing is shown
/// twice), the range (1, 3, 5 years, all), clickable legend chips that hide series, and the chart with its crosshair. The chosen tab, range and
/// hidden series are remembered per <c>key</c>, so the panel looks the same after a dialog rebuilds itself on a new turn.
/// </summary>
public sealed partial class ChartPanel : VBoxContainer
{
    private sealed class Memo { public int Tab; public int Range = 3; public readonly HashSet<string> Hidden = new(); }
    private static readonly Dictionary<string, Memo> Memory = new(StringComparer.Ordinal);

    private readonly Session _s;
    private readonly Memo _memo;
    private IReadOnlyList<ChartSpec> _specs;
    private readonly Chart _chart;
    private readonly HFlowContainer _tabs = new();
    private readonly HFlowContainer _legend = new();
    private readonly float _minHeight;
    public event Action<ChartSpec>? SpecChanged;

    public ChartPanel(Session s, string key, IReadOnlyList<ChartSpec> specs, float minHeight = 220, bool fill = false, string? select = null)
    {
        _s = s; _specs = specs; _minHeight = minHeight;
        if (!Memory.TryGetValue(key, out _memo!)) Memory[key] = _memo = new Memo();
        if (select != null) { int i = specs.ToList().FindIndex(x => x.Id == select); if (i >= 0) _memo.Tab = i; }
        _memo.Tab = Math.Clamp(_memo.Tab, 0, Math.Max(0, specs.Count - 1));
        AddThemeConstantOverride("separation", 10);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (fill) SizeFlagsVertical = SizeFlags.ExpandFill;
        _tabs.AddThemeConstantOverride("h_separation", 16); _tabs.AddThemeConstantOverride("v_separation", 2);
        _legend.AddThemeConstantOverride("h_separation", 6); _legend.AddThemeConstantOverride("v_separation", 6);
        _chart = new Chart(large: true) { CustomMinimumSize = new Vector2(260, minHeight) };
        _chart.SizeFlagsVertical = fill ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin;
        _chart.HiddenSeries.UnionWith(_memo.Hidden);
        _tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_tabs);
        var controls = Ui.HBox(12);
        controls.AddChild(Controls());
        _legend.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        controls.AddChild(_legend);
        AddChild(controls);
        AddChild(_chart);
        BuildTabs();
        Fill();
    }

    /// <summary>The spec on show.</summary>
    public ChartSpec? Current => _specs.Count == 0 ? null : _specs[Math.Clamp(_memo.Tab, 0, _specs.Count - 1)];

    /// <summary>Short tab text: the title without its unit (after a bracket or a comma).</summary>
    public static string TabLabel(ChartSpec spec, Localizer t)
    {
        if (spec.Tab != null) return spec.Tab(t);
        string title = spec.Title(t);
        int cut = title.IndexOfAny(new[] { '(', ',' });
        return (cut > 2 ? title[..cut] : title).Trim();
    }

    private Control Controls()
    {
        var t = _s.T;
        var range = Ui.Segmented(new[] { t.T("ui.chart.range.1y"), t.T("ui.chart.range.3y"), t.T("ui.chart.range.5y"), t.T("ui.chart.range.all") }, _memo.Range,
            i => { _memo.Range = i; Fill(); }, 26);
        range.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        return range;
    }

    private void BuildTabs()
    {
        Ui.Clear(_tabs);
        _tabs.Visible = _specs.Count > 1;
        if (_specs.Count <= 1) return;
        var group = new ButtonGroup();
        for (int i = 0; i < _specs.Count; i++)
        {
            int k = i;
            var b = new Button
            {
                Text = TabLabel(_specs[i], _s.T), ToggleMode = true, ButtonGroup = group, ButtonPressed = i == _memo.Tab, ThemeTypeVariation = "ButtonTab",
                FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30), MouseDefaultCursorShape = CursorShape.PointingHand,
            };
            b.Pressed += () => { _memo.Tab = k; _memo.Hidden.Clear(); _chart.HiddenSeries.Clear(); Fill(); SpecChanged?.Invoke(_specs[k]); };
            _tabs.AddChild(b);
        }
    }

    /// <summary>Replaces the charts offered (the tab stays where it was when it still exists).</summary>
    public void SetSpecs(IReadOnlyList<ChartSpec> specs, string? select = null)
    {
        _specs = specs;
        _memo.Tab = select != null ? Math.Max(0, specs.ToList().FindIndex(x => x.Id == select)) : Math.Clamp(_memo.Tab, 0, Math.Max(0, specs.Count - 1));
        BuildTabs();
        Fill();
    }

    private void Fill()
    {
        var spec = Current;
        if (spec == null) { _chart.Set("", Array.Empty<int>(), Array.Empty<ChartSeries>(), "0"); return; }
        int tpy = _s.Packs.GetScenario(_s.ScenarioId).TurnsPerYear;
        int last = _memo.Range switch { 0 => tpy, 1 => 3 * tpy, 2 => 5 * tpy, _ => 0 };
        var (turns, series) = Charts.Build(_s, spec, last);
        _chart.Set(spec.Title(_s.T), turns, series, spec.Format, _s.ShortDateAt);
        Ui.Clear(_legend);
        if (series.Count <= 1) return;
        foreach (var line in series)
        {
            var b = new IconTextButton(null) { Text = "●  " + line.Name, ToggleMode = true, ButtonPressed = !_chart.HiddenSeries.Contains(line.Name), ThemeTypeVariation = "ButtonSecondary", FocusMode = FocusModeEnum.None };
            foreach (var fc in new[] { "font_color", "font_pressed_color", "font_hover_color", "font_hover_pressed_color" }) b.AddThemeColorOverride(fc, line.Color);
            b.AddThemeStyleboxOverride("pressed", AppTheme.Box(AppTheme.Surface, 0, line.Color, 2, 12, 4));
            b.AddThemeStyleboxOverride("hover_pressed", AppTheme.Box(AppTheme.Panel, 0, line.Color, 2, 12, 4));
            b.Modulate = b.ButtonPressed ? Colors.White : new Color(1, 1, 1, 0.45f);
            b.CustomMinimumSize = new Vector2(0, 26);
            string name = line.Name;
            b.Toggled += on =>
            {
                if (on) { _chart.HiddenSeries.Remove(name); _memo.Hidden.Remove(name); } else { _chart.HiddenSeries.Add(name); _memo.Hidden.Add(name); }
                b.Modulate = on ? Colors.White : new Color(1, 1, 1, 0.45f); _chart.QueueRedraw();
            };
            _legend.AddChild(b);
        }
    }
}
