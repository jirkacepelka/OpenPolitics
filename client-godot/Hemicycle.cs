using Godot;

namespace OpenPolitics.Client;

/// <summary>The parliament as a half circle of seats, coloured by party from left to right; the government's seats are solid.</summary>
public partial class Hemicycle : Control
{
    private readonly List<(Color Color, bool Gov, int Seats)> _parties = new();
    private int _total;
    private float _majority;

    public Hemicycle()
    {
        CustomMinimumSize = new Vector2(300, 160);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <param name="parties">In left-to-right order.</param>
    public void Set(IEnumerable<(Color Color, bool Gov, int Seats)> parties, float majorityFraction)
    {
        _parties.Clear(); _parties.AddRange(parties.Where(p => p.Seats > 0));
        _total = _parties.Sum(p => p.Seats);
        _majority = majorityFraction;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_total == 0) return;
        float R = Math.Min(Size.X / 2 - 4, Size.Y - 8), r0 = R * 0.42f;
        var c = new Vector2(Size.X / 2, Size.Y - 4);
        int rows = Math.Clamp((int)Math.Round(Math.Sqrt(_total / 6.0)), 3, 10);
        float rowGap = (R - r0) / rows;
        float seatR = Math.Min(rowGap * 0.42f, 7f);
        // seats per row proportional to the radius
        var radii = Enumerable.Range(0, rows).Select(i => r0 + rowGap * (i + 0.5f)).ToArray();
        float sum = radii.Sum();
        var counts = radii.Select(rr => (int)Math.Floor(_total * rr / sum)).ToArray();
        int left = _total - counts.Sum();
        for (int i = rows - 1; left > 0; i = (i - 1 + rows) % rows, left--) counts[i]++;
        var seats = new List<(float Angle, Vector2 Pos)>();
        for (int i = 0; i < rows; i++)
            for (int k = 0; k < counts[i]; k++)
            {
                float a = counts[i] == 1 ? Mathf.Pi / 2 : Mathf.Pi - Mathf.Pi * k / (counts[i] - 1);
                seats.Add((a, c + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * radii[i]));
            }
        seats.Sort((x, y) => y.Angle.CompareTo(x.Angle));
        int idx = 0;
        foreach (var (color, gov, n) in _parties)
            for (int k = 0; k < n && idx < seats.Count; k++, idx++)
            {
                var p = seats[idx].Pos;
                if (gov) DrawCircle(p, seatR, color);
                else { DrawCircle(p, seatR, new Color(color, 0.28f)); DrawArc(p, seatR - 0.6f, 0, Mathf.Tau, 16, color, 1.2f, true); }
            }
        // the majority line
        float ma = Mathf.Pi - Mathf.Pi * _majority;
        DrawLine(c + new Vector2(Mathf.Cos(ma), -Mathf.Sin(ma)) * (r0 - 6), c + new Vector2(Mathf.Cos(ma), -Mathf.Sin(ma)) * (R + 2), new Color(AppTheme.Text, 0.5f), 1.5f, true);
    }
}
