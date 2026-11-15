using Godot;

namespace OpenPolitics.Client;

/// <summary>
/// The political compass as a square: economic left to right, liberal (bottom) to authoritarian (top). Party dots in their fixed colours,
/// labels placed greedily (right, left, above, below, else hidden and shown on hover); the player's party is ringed in ink.
/// </summary>
public partial class CompassView : Control
{
    private readonly List<(string Id, string Name, Vector2 Pos, Color Color)> _dots = new();
    private string? _player;
    private string _left = "", _right = "", _top = "", _bottom = "", _you = "";
    private int _hover = -1;
    private float _scale = 1f;

    public CompassView()
    {
        CustomMinimumSize = new Vector2(240, 240);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void Set(Session s, string? player)
    {
        _player = player;
        var t = s.T;
        (_left, _right, _top, _bottom) = (t.T("ui.compass.left"), t.T("ui.compass.right"), t.T("ui.compass.authoritarian"), t.T("ui.compass.liberal"));
        _you = t.T("ui.politics.you");
        _dots.Clear();
        foreach (var (id, pos) in s.Game.Compass.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            _dots.Add((id, t.Name(id), new Vector2((float)pos.X, (float)pos.Y), Ui.PartyColor(s, id)));
        float far = _dots.Count == 0 ? 0 : _dots.Max(d => Math.Max(Math.Abs(d.Pos.X), Math.Abs(d.Pos.Y)));
        _scale = far > 0.02f ? Math.Min(4f, 0.82f / far) : 1f;
        QueueRedraw();
    }

    private Rect2 Plot
    {
        get { float side = Math.Min(Size.X, Size.Y); return new Rect2((Size.X - side) / 2, (Size.Y - side) / 2, side, side); }
    }

    private Vector2 ToScreen(Vector2 p)
    {
        var r = Plot; p *= _scale;
        return new Vector2(r.Position.X + r.Size.X * (p.X + 1) / 2, r.Position.Y + r.Size.Y * (1 - (p.Y + 1) / 2));
    }

    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }

    public override void _Draw()
    {
        var r = Plot;
        DrawStyleBox(AppTheme.Box(AppTheme.Panel, 12), r);
        for (int i = 1; i < 4; i++)
        {
            float x = r.Position.X + r.Size.X * i / 4, y = r.Position.Y + r.Size.Y * i / 4;
            var col = i == 2 ? new Color("a8a8a8") : new Color("e2e2e2");
            DrawLine(new Vector2(x, r.Position.Y), new Vector2(x, r.End.Y), col, 1);
            DrawLine(new Vector2(r.Position.X, y), new Vector2(r.End.X, y), col, 1);
        }
        var ov = AppTheme.Face(700, false, 1);
        var mid = r.GetCenter();
        DrawString(ov, new Vector2(r.Position.X + 8, mid.Y - 6), _left.ToUpperInvariant(), HorizontalAlignment.Left, r.Size.X / 2, 10, AppTheme.Ink3);
        DrawString(ov, new Vector2(mid.X, mid.Y - 6), _right.ToUpperInvariant(), HorizontalAlignment.Right, r.Size.X / 2 - 8, 10, AppTheme.Ink3);
        DrawString(ov, new Vector2(r.Position.X, r.Position.Y + 16), _top.ToUpperInvariant(), HorizontalAlignment.Center, r.Size.X, 10, AppTheme.Ink3);
        DrawString(ov, new Vector2(r.Position.X, r.End.Y - 8), _bottom.ToUpperInvariant(), HorizontalAlignment.Center, r.Size.X, 10, AppTheme.Ink3);

        for (int i = 0; i < _dots.Count; i++)
        {
            var d = _dots[i]; var p = ToScreen(d.Pos);
            bool me = d.Id == _player;
            float rad = me ? 8 : 6.5f;
            DrawCircle(p, rad + 2, AppTheme.Surface);
            DrawCircle(p, rad, d.Color);
            if (me) DrawArc(p, rad + 4, 0, Mathf.Tau, 32, AppTheme.Ink, 2f, true);
            if (i == _hover) DrawArc(p, rad + 4, 0, Mathf.Tau, 32, AppTheme.Chrome, 2f, true);
        }
        // Names only on hover (the plot is crowded when every party is labelled); your own party is the ringed dot.
        if (_hover >= 0)
        {
            var d = _dots[_hover]; var p = ToScreen(d.Pos);
            string label = d.Id == _player ? $"{d.Name} ({_you})" : d.Name;
            var f = AppTheme.Bold;
            var size = f.GetStringSize(label, HorizontalAlignment.Left, -1, 12) + new Vector2(16, 10);
            var pos = p + new Vector2(-size.X / 2, -size.Y - 12);
            if (pos.Y < r.Position.Y) pos.Y = p.Y + 12;
            pos.X = Math.Clamp(pos.X, r.Position.X + 2, r.End.X - size.X - 2);
            var pill = AppTheme.Box(AppTheme.Ink, 6);
            pill.ShadowColor = new Color(0, 0, 0, 0.18f); pill.ShadowSize = 6; pill.ShadowOffset = new Vector2(0, 2);
            DrawStyleBox(pill, new Rect2(pos, size));
            DrawString(f, pos + new Vector2(8, 5 + size.Y * 0.62f - 5), label, HorizontalAlignment.Left, -1, 12, Colors.White);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion mm) return;
        int best = -1; float bestD = 14;
        for (int i = 0; i < _dots.Count; i++) { float d = ToScreen(_dots[i].Pos).DistanceTo(mm.Position); if (d < bestD) { bestD = d; best = i; } }
        if (best != _hover)
        {
            _hover = best;
            TooltipText = best >= 0 ? $"{_dots[best].Name}\n{Ui.Num(_dots[best].Pos.X, "0.00")} / {Ui.Num(_dots[best].Pos.Y, "0.00")}" : "";
            QueueRedraw();
        }
    }
}
