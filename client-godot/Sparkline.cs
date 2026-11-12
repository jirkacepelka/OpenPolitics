using Godot;

namespace OpenPolitics.Client;

/// <summary>A tiny line chart of a series (min and max labelled), used for the history of key figures.</summary>
public partial class Sparkline : Control
{
    private IReadOnlyList<double> _values = Array.Empty<double>();
    private string _label = "";
    private string _format = "0.0";
    public Color LineColor { get; set; } = Ui.Accent;

    public Sparkline() { CustomMinimumSize = new Vector2(280, 90); }

    public void Set(string label, IReadOnlyList<double> values, string format = "0.0")
    {
        _label = label; _values = values.ToList(); _format = format;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.04f));
        DrawString(font, new Vector2(6, 16), _label, HorizontalAlignment.Left, -1, 13, Ui.Muted);
        if (_values.Count < 2) return;
        double min = _values.Min(), max = _values.Max();
        if (max - min < 1e-12) { min -= 1; max += 1; }
        var rect = new Rect2(6, 24, Size.X - 12, Size.Y - 34);
        var pts = new Vector2[_values.Count];
        for (int i = 0; i < _values.Count; i++)
            pts[i] = new Vector2(rect.Position.X + rect.Size.X * i / (_values.Count - 1),
                                 rect.End.Y - rect.Size.Y * (float)((_values[i] - min) / (max - min)));
        DrawPolyline(pts, LineColor, 2f, true);
        DrawString(font, new Vector2(Size.X - 70, 16), _values[^1].ToString(_format, System.Globalization.CultureInfo.InvariantCulture),
            HorizontalAlignment.Right, 64, 13, Colors.White);
    }
}
