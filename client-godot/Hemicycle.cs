using Godot;

namespace OpenPolitics.Client;

/// <summary>
/// The parliament as a half circle of seat dots, coloured by party from left to right; the government block gets an ink arc under its
/// seats, the majority seat a dashed radial line, and the centre the government's seat count.
/// </summary>
public partial class Hemicycle : Control
{
    private readonly List<(Color Color, bool Gov, int Seats, string Name)> _parties = new();
    private int _total, _held;
    private float _majority;
    private string _caption = "";

    public Hemicycle()
    {
        CustomMinimumSize = new Vector2(260, 150);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <param name="parties">In left-to-right order.</param>
    public void Set(IEnumerable<(Color Color, bool Gov, int Seats, string Name)> parties, float majorityFraction, string caption)
    {
        _parties.Clear(); _parties.AddRange(parties.Where(p => p.Seats > 0));
        _total = _parties.Sum(p => p.Seats);
        _held = _parties.Where(p => p.Gov).Sum(p => p.Seats);
        _majority = majorityFraction; _caption = caption;
        TooltipText = string.Join("\n", _parties.Select(p => $"{p.Name}: {p.Seats}"));
        QueueRedraw();
    }

    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }

    public override void _Draw()
    {
        if (_total == 0) return;
        float R = Math.Min(Size.X / 2 - 10, Size.Y - 14), r0 = R * 0.42f;
        var c = new Vector2(Size.X / 2, Size.Y - 8);
        int rows = Math.Clamp((int)Math.Round(Math.Sqrt(_total / 6.0)), 3, 12);
        float rowGap = (R - r0) / rows;
        var radii = Enumerable.Range(0, rows).Select(i => r0 + rowGap * (i + 0.5f)).ToArray();
        float sum = radii.Sum();
        var counts = radii.Select(rr => (int)Math.Floor(_total * rr / sum)).ToArray();
        int left = _total - counts.Sum();
        for (int i = rows - 1; left > 0; i = (i - 1 + rows) % rows, left--) counts[i]++;
        float pitch = Math.Min(rowGap, Mathf.Pi * radii[^1] / Math.Max(1, counts[^1] - 1));
        float seatR = Math.Max(1.5f, pitch * 0.36f);
        var seats = new List<(float Angle, Vector2 Pos)>();
        for (int i = 0; i < rows; i++)
            for (int k = 0; k < counts[i]; k++)
            {
                float a = counts[i] == 1 ? Mathf.Pi / 2 : Mathf.Pi - Mathf.Pi * k / (counts[i] - 1);
                seats.Add((a, c + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * radii[i]));
            }
        seats.Sort((x, y) => y.Angle.CompareTo(x.Angle));
        int idx = 0;
        float govFrom = float.NaN, govTo = float.NaN;
        foreach (var (color, gov, n, _) in _parties)
            for (int k = 0; k < n && idx < seats.Count; k++, idx++)
            {
                DrawCircle(seats[idx].Pos, seatR, color);
                if (gov) { govFrom = float.IsNaN(govFrom) ? seats[idx].Angle : Math.Max(govFrom, seats[idx].Angle); govTo = float.IsNaN(govTo) ? seats[idx].Angle : Math.Min(govTo, seats[idx].Angle); }
            }
        if (!float.IsNaN(govFrom)) DrawArc(c, R + 6, -govFrom, -govTo, 48, AppTheme.Ink, 3f, true);
        float ma = Mathf.Pi - Mathf.Pi * _majority;
        var u = new Vector2(Mathf.Cos(ma), -Mathf.Sin(ma));
        for (float x = r0 - 4; x < R + 10; x += 7) DrawLine(c + u * x, c + u * Math.Min(x + 4, R + 10), AppTheme.Ink, 1.5f);
        string held = _held.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int fs = (int)Math.Clamp(r0 * 0.55f, 16, 34);
        DrawString(AppTheme.FigBold, new Vector2(c.X - 80, c.Y - fs * 0.55f), held, HorizontalAlignment.Center, 160, fs, _held > _total * _majority ? AppTheme.GoodText : AppTheme.BadText);
        DrawString(AppTheme.Semi, new Vector2(c.X - 80, c.Y - 2), _caption, HorizontalAlignment.Center, 160, 12, AppTheme.Ink3);
    }
}
