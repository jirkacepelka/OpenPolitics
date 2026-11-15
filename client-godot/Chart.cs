using Godot;

namespace OpenPolitics.Client;

public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, Color Color);

/// <summary>
/// A line chart on white: horizontal gridlines only, small tabular axis labels, 2.5 px lines (a single series in ink with a faint area),
/// direct end labels for up to four series, the current quarter tinted, a crosshair that reads values. Small charts (tiles) show the
/// title and the latest value and raise <see cref="Clicked"/>.
/// </summary>
public partial class Chart : Control
{
    private IReadOnlyList<int> _turns = Array.Empty<int>();
    private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();
    private string _title = "", _format = "0.0";
    private Func<int, string> _xLabel = t => t.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private int _hover = -1;
    private bool _inside;
    public bool Large { get; set; }
    /// <summary>Tile look: title and latest value on top, no axes.</summary>
    public bool Tile { get; set; }
    public HashSet<string> Hidden { get; } = new();
    public event Action? Clicked;

    public Chart(bool large = false)
    {
        Large = large;
        CustomMinimumSize = large ? new Vector2(320, 300) : new Vector2(220, 150);
        MouseFilter = MouseFilterEnum.Stop;
        if (large) { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }
    }

    public void Set(string title, IReadOnlyList<int> turns, IReadOnlyList<ChartSeries> series, string format, Func<int, string>? xLabel = null)
    {
        _title = title; _turns = turns; _series = series; _format = format;
        if (xLabel != null) _xLabel = xLabel;
        if (Tile) { TooltipText = title; MouseDefaultCursorShape = CursorShape.PointingHand; }
        QueueRedraw();
    }

    private IEnumerable<ChartSeries> Shown => _series.Where(s => !Hidden.Contains(s.Name));

    private (double Min, double Max) Range()
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in Shown) foreach (var v in s.Values) if (double.IsFinite(v)) { min = Math.Min(min, v); max = Math.Max(max, v); }
        if (min > max) return (0, 1);
        if (max - min < 1e-12) { min -= 1; max += 1; }
        // nice bounds
        double step = Nice((max - min) / 4);
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step);
    }

    private static double Nice(double x)
    {
        double e = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(x, 1e-12))));
        double f = x / e;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * e;
    }

    private Rect2 Area()
    {
        if (Tile) return new Rect2(16, 62, Size.X - 32, Size.Y - 76);
        var font = AppTheme.FigSemi;
        var (min, max) = Range();
        float yl = Math.Max(font.GetStringSize(Ui.Num(min, _format), HorizontalAlignment.Left, -1, 11).X, font.GetStringSize(Ui.Num(max, _format), HorizontalAlignment.Left, -1, 11).X);
        float right = 10;
        var shown = Shown.ToList();
        if (shown.Count is > 1 and <= 4) right = Math.Min(140, shown.Max(s => AppTheme.Semi.GetStringSize(s.Name, HorizontalAlignment.Left, -1, 12).X) + 14);
        return new Rect2(yl + 12, 10, Math.Max(40, Size.X - yl - 12 - right), Math.Max(40, Size.Y - 34));
    }

    private IEnumerable<List<Vector2>> Segments(IReadOnlyList<double> values, Rect2 area, double min, double max)
    {
        var run = new List<Vector2>();
        int n = Math.Max(values.Count, 1);
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) { if (run.Count > 0) { yield return run; run = new(); } continue; }
            float x = area.Position.X + (n == 1 ? area.Size.X : area.Size.X * i / (n - 1));
            float y = area.End.Y - area.Size.Y * (float)((values[i] - min) / (max - min));
            run.Add(new Vector2(x, y));
        }
        if (run.Count > 0) yield return run;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        if (Tile)
        {
            var tile = AppTheme.Box(AppTheme.Surface, 14, _inside ? new Color(AppTheme.Chrome, 0.5f) : AppTheme.Hairline, 1);
            if (_inside) { tile.ShadowColor = new Color(AppTheme.Ink, 0.08f); tile.ShadowSize = 10; tile.ShadowOffset = new Vector2(0, 3); }
            DrawStyleBox(tile, rect);
        }
        var area = Area();
        var (min, max) = Range();
        var fig = AppTheme.FigSemi;
        int n = _turns.Count;
        if (Tile)
        {
            DrawString(AppTheme.Face(600, false, 1), new Vector2(16, 24), _title.ToUpperInvariant(), HorizontalAlignment.Left, Size.X - 32, 11, AppTheme.Ink3);
            double last = double.NaN;
            foreach (var s in Shown.Take(1)) foreach (var v in s.Values) if (double.IsFinite(v)) last = v;
            DrawString(AppTheme.FigSemi, new Vector2(16, 48), Ui.Num(last, _format), HorizontalAlignment.Left, Size.X - 32, 20, AppTheme.Ink);
        }
        else
        {
            if (n > 0) // the current quarter
            {
                float cw = Math.Max(6, area.Size.X / Math.Max(1, n - 1));
                DrawRect(new Rect2(area.End.X - cw / 2, area.Position.Y, cw / 2, area.Size.Y), AppTheme.ChromeTint);
            }
            for (int g = 0; g <= 4; g++)
            {
                double v = min + (max - min) * g / 4;
                float y = area.End.Y - area.Size.Y * g / 4f;
                bool zero = Math.Abs(v) < 1e-9 && min < 0;
                DrawLine(new Vector2(area.Position.X, y), new Vector2(area.End.X, y), zero ? new Color("8f8f8f") : new Color("e6e6e6"), 1f);
                DrawString(fig, new Vector2(0, y + 4), Ui.Num(v, _format), HorizontalAlignment.Right, area.Position.X - 8, 11, AppTheme.Ink3);
            }
            int ticks = Math.Min(Math.Max(2, (int)(area.Size.X / 90)), Math.Max(n, 1));
            for (int k = 0; k < ticks; k++)
            {
                int i = ticks == 1 ? 0 : (int)Math.Round((n - 1) * (double)k / (ticks - 1));
                if (i < 0 || i >= n) continue;
                float x = area.Position.X + (n <= 1 ? 0 : area.Size.X * i / (n - 1));
                DrawString(fig, new Vector2(x - 40, area.End.Y + 18), _xLabel(_turns[i]), HorizontalAlignment.Center, 80, 11, AppTheme.Ink3);
            }
        }
        var shown = Shown.ToList();
        foreach (var s in shown)
            foreach (var run in Segments(s.Values, area, min, max))
            {
                if (run.Count > 1)
                {
                    if (shown.Count == 1)
                    {
                        var poly = new List<Vector2>(run) { new(run[^1].X, area.End.Y), new(run[0].X, area.End.Y) };
                        DrawColoredPolygon(poly.ToArray(), new Color(s.Color == AppTheme.Ink ? AppTheme.Chrome : s.Color, 0.08f));
                    }
                    DrawPolyline(run.ToArray(), shown.Count == 1 && s.Color == AppTheme.Ink ? AppTheme.Chrome : s.Color, Tile ? 1.75f : 2.25f, true);
                }
                else DrawCircle(run[0], 3f, s.Color);
            }
        if (!Tile && shown.Count is > 1 and <= 4)
        {
            var placed = new List<float>();
            foreach (var s in shown)
            {
                var last = Segments(s.Values, area, min, max).LastOrDefault();
                if (last == null || last.Count == 0) continue;
                float y = last[^1].Y + 4;
                foreach (var _ in placed.ToList()) foreach (var py in placed) if (Math.Abs(py - y) < 14) y = py + 14;
                placed.Add(y);
                DrawString(AppTheme.Semi, new Vector2(area.End.X + 6, y), s.Name, HorizontalAlignment.Left, Size.X - area.End.X - 8, 12, s.Color);
            }
        }
        if (!Tile && _hover >= 0 && _hover < n)
        {
            float x = area.Position.X + (n <= 1 ? 0 : area.Size.X * _hover / (n - 1));
            DrawLine(new Vector2(x, area.Position.Y), new Vector2(x, area.End.Y), new Color(AppTheme.Ink, 0.4f), 1f);
            var lines = new List<(string Text, Color Color)> { (_xLabel(_turns[_hover]), new Color("c8c8c8")) };
            foreach (var s in shown)
                if (_hover < s.Values.Count && double.IsFinite(s.Values[_hover]))
                {
                    float y = area.End.Y - area.Size.Y * (float)((s.Values[_hover] - min) / (max - min));
                    DrawCircle(new Vector2(x, y), 5f, AppTheme.Surface); DrawCircle(new Vector2(x, y), 3.5f, s.Color);
                    lines.Add(($"{s.Name}: {Ui.Num(s.Values[_hover], _format)}", Colors.White));
                }
            var font = AppTheme.Regular;
            float w = lines.Max(l => font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, 12).X) + 20, h = 10 + lines.Count * 17;
            float bx = x + 12 + w > Size.X ? x - 12 - w : x + 12, by = area.Position.Y + 4;
            DrawStyleBox(AppTheme.Box(AppTheme.Graphite, 8), new Rect2(bx, by, w, h));
            for (int i = 0; i < lines.Count; i++) DrawString(font, new Vector2(bx + 10, by + 18 + i * 17), lines[i].Text, HorizontalAlignment.Left, -1, 12, lines[i].Color);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm && !Tile && _turns.Count > 0)
        {
            var area = Area();
            int n = _turns.Count;
            int i = n <= 1 ? 0 : (int)Math.Round((mm.Position.X - area.Position.X) / area.Size.X * (n - 1));
            _hover = mm.Position.X < area.Position.X - 10 || mm.Position.X > area.End.X + 10 ? -1 : Math.Clamp(i, 0, n - 1);
            QueueRedraw();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && Tile) Clicked?.Invoke();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _inside = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _inside = false; _hover = -1; QueueRedraw(); }
        else if (what == NotificationResized) QueueRedraw();
    }
}
