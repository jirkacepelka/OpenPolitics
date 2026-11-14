using System.Globalization;
using Godot;

namespace OpenPolitics.Client;

public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, Color Color);

/// <summary>
/// A line chart of one or more series. Small (a tile with the latest value) or large (axes, grid, legend, a crosshair that reads values).
/// Clicking a small chart raises <see cref="Clicked"/>, which opens the large view.
/// </summary>
public partial class Chart : Control
{
    private IReadOnlyList<int> _turns = Array.Empty<int>();
    private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();
    private string _title = "", _format = "0.0";
    private Func<int, string> _xLabel = t => t.ToString(CultureInfo.InvariantCulture);
    private int _hover = -1;
    private bool _inside;
    public bool Large { get; set; }

    public event Action? Clicked;

    public Chart(bool large = false)
    {
        Large = large;
        CustomMinimumSize = large ? new Vector2(640, 380) : new Vector2(290, 112);
        MouseFilter = MouseFilterEnum.Stop;
        if (!large) { MouseDefaultCursorShape = CursorShape.PointingHand; }
        if (large) { SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }
    }

    public void Set(string title, IReadOnlyList<int> turns, IReadOnlyList<ChartSeries> series, string format, Func<int, string>? xLabel = null)
    {
        _title = title; _turns = turns; _series = series; _format = format;
        if (xLabel != null) _xLabel = xLabel;
        if (!Large) TooltipText = title;
        QueueRedraw();
    }

    private static string Fmt(double v, string f) => double.IsFinite(v) ? v.ToString(f, CultureInfo.InvariantCulture) : "–";

    private (double Min, double Max) Range()
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in _series)
            foreach (var v in s.Values) if (double.IsFinite(v)) { min = Math.Min(min, v); max = Math.Max(max, v); }
        if (min > max) return (0, 1);
        if (max - min < 1e-12) { min -= 1; max += 1; }
        double pad = (max - min) * 0.06;
        return (min - pad, max + pad);
    }

    private IEnumerable<List<Vector2>> Segments(IReadOnlyList<double> values, Rect2 area, double min, double max)
    {
        var run = new List<Vector2>();
        int n = Math.Max(values.Count, 1);
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) { if (run.Count > 0) { yield return run; run = new(); } continue; }
            float x = area.Position.X + (n == 1 ? 0 : area.Size.X * i / (n - 1));
            float y = area.End.Y - area.Size.Y * (float)((values[i] - min) / (max - min));
            run.Add(new Vector2(x, y));
        }
        if (run.Count > 0) yield return run;
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        DrawStyleBox(AppTheme.Box(AppTheme.Card, 12, _inside && !Large ? AppTheme.Accent : AppTheme.Border, 1), new Rect2(Vector2.Zero, Size));
        if (Large) DrawLarge(font); else DrawSmall(font);
    }

    private void DrawSmall(Font font)
    {
        DrawString(font, new Vector2(12, 22), _title, HorizontalAlignment.Left, Size.X - 90, 13, AppTheme.Muted);
        double last = double.NaN;
        if (_series.Count > 0) foreach (var v in _series[0].Values) if (double.IsFinite(v)) last = v;
        DrawString(font, new Vector2(Size.X - 90, 22), Fmt(last, _format), HorizontalAlignment.Right, 78, 15, Colors.White);
        var (min, max) = Range();
        var area = new Rect2(12, 34, Size.X - 24, Size.Y - 46);
        foreach (var s in _series)
            foreach (var run in Segments(s.Values, area, min, max))
                if (run.Count > 1) DrawPolyline(run.ToArray(), s.Color, 2f, true);
                else DrawCircle(run[0], 2.5f, s.Color);
    }

    private void DrawLarge(Font font)
    {
        var area = new Rect2(70, 20, Size.X - 90, Size.Y - 62);
        var (min, max) = Range();
        for (int g = 0; g <= 4; g++)
        {
            float y = area.End.Y - area.Size.Y * g / 4f;
            DrawLine(new Vector2(area.Position.X, y), new Vector2(area.End.X, y), new Color(AppTheme.Border, 0.7f), 1f);
            DrawString(font, new Vector2(6, y + 5), Fmt(min + (max - min) * g / 4, _format), HorizontalAlignment.Right, 58, 12, AppTheme.Muted);
        }
        int n = _turns.Count;
        int ticks = Math.Min(6, Math.Max(n, 1));
        for (int k = 0; k < ticks; k++)
        {
            int i = ticks == 1 ? 0 : (int)Math.Round((n - 1) * (double)k / (ticks - 1));
            if (i < 0 || i >= n) continue;
            float x = area.Position.X + (n <= 1 ? 0 : area.Size.X * i / (n - 1));
            DrawString(font, new Vector2(x - 40, area.End.Y + 20), _xLabel(_turns[i]), HorizontalAlignment.Center, 80, 12, AppTheme.Muted);
        }
        foreach (var s in _series)
            foreach (var run in Segments(s.Values, area, min, max))
                if (run.Count > 1) DrawPolyline(run.ToArray(), s.Color, 2.5f, true);
                else DrawCircle(run[0], 3f, s.Color);

        if (_hover >= 0 && _hover < n)
        {
            float x = area.Position.X + (n <= 1 ? 0 : area.Size.X * _hover / (n - 1));
            DrawLine(new Vector2(x, area.Position.Y), new Vector2(x, area.End.Y), new Color(1, 1, 1, 0.35f), 1f);
            var lines = new List<(string Text, Color Color)> { (_xLabel(_turns[_hover]), AppTheme.Muted) };
            foreach (var s in _series)
                if (_hover < s.Values.Count && double.IsFinite(s.Values[_hover]))
                {
                    float y = area.End.Y - area.Size.Y * (float)((s.Values[_hover] - min) / (max - min));
                    DrawCircle(new Vector2(x, y), 4f, s.Color);
                    lines.Add(($"{s.Name}: {Fmt(s.Values[_hover], _format)}", s.Color));
                }
            float w = lines.Max(l => font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, 13).X) + 20, h = 12 + lines.Count * 18;
            float bx = x + 14 + w > Size.X ? x - 14 - w : x + 14, by = Math.Clamp(area.Position.Y + 6, 0, Size.Y - h);
            DrawStyleBox(AppTheme.Box(new Color("0b0f19", 0.95f), 8, AppTheme.Border, 1), new Rect2(bx, by, w, h));
            for (int i = 0; i < lines.Count; i++) DrawString(font, new Vector2(bx + 10, by + 18 + i * 18), lines[i].Text, HorizontalAlignment.Left, -1, 13, lines[i].Color);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm && Large && _turns.Count > 0)
        {
            var area = new Rect2(70, 20, Size.X - 90, Size.Y - 62);
            int n = _turns.Count;
            int i = n <= 1 ? 0 : (int)Math.Round((mm.Position.X - area.Position.X) / area.Size.X * (n - 1));
            _hover = mm.Position.X < area.Position.X - 10 || mm.Position.X > area.End.X + 10 ? -1 : Math.Clamp(i, 0, n - 1);
            QueueRedraw();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !Large) Clicked?.Invoke();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _inside = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _inside = false; _hover = -1; QueueRedraw(); }
    }
}
