using Godot;

namespace OpenPolitics.Client;

/// <summary>The political compass as a square: economic left to right, liberal (bottom) to authoritarian (top). Shows every party; the player's is ringed.</summary>
public partial class CompassView : Control
{
    private readonly List<(string Id, string Name, Vector2 Pos, Color Color)> _dots = new();
    private string? _player;
    private string _left = "", _right = "", _top = "", _bottom = "";
    private int _hover = -1;

    public CompassView()
    {
        CustomMinimumSize = new Vector2(340, 340);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void Set(Session s, string? player)
    {
        _player = player;
        var t = s.T;
        (_left, _right, _top, _bottom) = (t.T("ui.compass.left"), t.T("ui.compass.right"), t.T("ui.compass.authoritarian"), t.T("ui.compass.liberal"));
        _dots.Clear();
        _scale = 1f;
        var ids = s.Packs.Parties.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var (id, pos) in s.Game.Compass.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            _dots.Add((id, t.Name(id), new Vector2((float)pos.X, (float)pos.Y), Charts.PaletteColor(ids.IndexOf(id))));
        // Parties cluster near the middle; stretch the picture so the outermost one sits near the edge (the numbers in tooltips stay true).
        float far = _dots.Count == 0 ? 0 : _dots.Max(d => Math.Max(Math.Abs(d.Pos.X), Math.Abs(d.Pos.Y)));
        _scale = far > 0.02f ? Math.Min(4f, 0.85f / far) : 1f;
        QueueRedraw();
    }

    private Rect2 Plot => new(10, 10, Math.Max(Size.X - 20, 40), Math.Max(Size.Y - 20, 40));
    private float _scale = 1f;

    private Vector2 ToScreen(Vector2 p)
    {
        var r = Plot;
        p *= _scale;
        return new Vector2(r.Position.X + r.Size.X * (p.X + 1) / 2, r.Position.Y + r.Size.Y * (1 - (p.Y + 1) / 2));
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var r = Plot;
        DrawStyleBox(AppTheme.Box(AppTheme.Card, 12, AppTheme.Border, 1), new Rect2(Vector2.Zero, Size));
        DrawRect(r, new Color("0b0f19"));
                var mid = r.Position + r.Size / 2;
        DrawLine(new Vector2(r.Position.X, mid.Y), new Vector2(r.End.X, mid.Y), AppTheme.Border, 1.5f);
        DrawLine(new Vector2(mid.X, r.Position.Y), new Vector2(mid.X, r.End.Y), AppTheme.Border, 1.5f);
        DrawRect(r, AppTheme.Border, false, 1.5f);
        DrawString(font, new Vector2(r.Position.X + 6, mid.Y - 5), _left, HorizontalAlignment.Left, 120, 12, AppTheme.Muted);
        DrawString(font, new Vector2(r.End.X - 126, mid.Y - 5), _right, HorizontalAlignment.Right, 120, 12, AppTheme.Muted);
        DrawString(font, new Vector2(mid.X - 60, r.Position.Y + 16), _top, HorizontalAlignment.Center, 120, 12, AppTheme.Muted);
        DrawString(font, new Vector2(mid.X - 60, r.End.Y - 8), _bottom, HorizontalAlignment.Center, 120, 12, AppTheme.Muted);

        for (int i = 0; i < _dots.Count; i++)
        {
            var d = _dots[i];
            var p = ToScreen(d.Pos);
            bool me = d.Id == _player;
            DrawCircle(p, me ? 9 : 7, d.Color);
            if (me) DrawArc(p, 13, 0, Mathf.Tau, 32, Colors.White, 2f, true);
            if (i == _hover) DrawArc(p, 11, 0, Mathf.Tau, 32, AppTheme.Accent, 2f, true);
            var label = d.Name.Length > 18 ? d.Name[..17] + "…" : d.Name;
            var color = me ? Colors.White : new Color(AppTheme.Text, 0.85f);
            if (p.X > Plot.GetCenter().X) DrawString(font, p + new Vector2(-161, 4), label, HorizontalAlignment.Right, 150, 12, color);
            else DrawString(font, p + new Vector2(11, 4), label, HorizontalAlignment.Left, 150, 12, color);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion mm) return;
        int best = -1; float bestD = 16;
        for (int i = 0; i < _dots.Count; i++) { float d = ToScreen(_dots[i].Pos).DistanceTo(mm.Position); if (d < bestD) { bestD = d; best = i; } }
        if (best != _hover)
        {
            _hover = best;
            TooltipText = best >= 0 ? $"{_dots[best].Name}\n{_dots[best].Pos.X:0.00} / {_dots[best].Pos.Y:0.00}" : "";
            QueueRedraw();
        }
    }
}
