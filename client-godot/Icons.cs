using Godot;

namespace OpenPolitics.Client;

public enum IconKind { Overview, Policies, Voters, Politics, Party, News, Coin, Menu, Save, Play, FastForward }

/// <summary>Simple vector icons drawn with primitives, so the client needs no image assets and stays sharp at any size.</summary>
public static class Icons
{
    public static void Draw(CanvasItem c, IconKind kind, Vector2 center, float size, Color color)
    {
        float u = size / 24f;
        Vector2 P(float x, float y) => center + new Vector2(x, y) * u;
        float w = Math.Max(1.6f, 2f * u);
        switch (kind)
        {
            case IconKind.Overview:
                c.DrawRect(new Rect2(P(-9, 1), new Vector2(4 * u, 8 * u)), color);
                c.DrawRect(new Rect2(P(-2, -4), new Vector2(4 * u, 13 * u)), color);
                c.DrawRect(new Rect2(P(5, -9), new Vector2(4 * u, 18 * u)), color);
                break;
            case IconKind.Policies:
                foreach (var (y, k) in new[] { (-7f, -3f), (0f, 4f), (7f, -1f) })
                {
                    c.DrawLine(P(-10, y), P(10, y), new Color(color, 0.55f), w);
                    c.DrawCircle(P(k, y), 3.4f * u, color);
                }
                break;
            case IconKind.Voters:
                c.DrawCircle(P(-4, -4), 3.6f * u, color);
                c.DrawArc(P(-4, 9), 7f * u, Mathf.Pi * 1.08f, Mathf.Pi * 1.92f, 16, color, w * 1.6f, true);
                c.DrawCircle(P(6, -2), 3f * u, new Color(color, 0.7f));
                c.DrawArc(P(6, 9), 6f * u, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 16, new Color(color, 0.7f), w * 1.4f, true);
                break;
            case IconKind.Politics:
                foreach (var (r, n) in new[] { (5f, 3), (8.5f, 5), (12f, 7) })
                    for (int i = 0; i < n; i++)
                    {
                        float a = Mathf.Pi + Mathf.Pi * i / (n - 1);
                        c.DrawCircle(P(Mathf.Cos(a) * r, 7 + Mathf.Sin(a) * r), 1.7f * u, color);
                    }
                c.DrawLine(P(-12, 9), P(12, 9), new Color(color, 0.6f), w);
                break;
            case IconKind.Party:
                c.DrawLine(P(-6, 11), P(-6, -10), color, w);
                c.DrawColoredPolygon(new[] { P(-6, -10), P(9, -5), P(-6, 0) }, color);
                break;
            case IconKind.News:
                c.DrawRect(new Rect2(P(-10, -9), new Vector2(20 * u, 18 * u)), color, false, w);
                c.DrawRect(new Rect2(P(-6, -5), new Vector2(7 * u, 6 * u)), color);
                foreach (float y in new[] { -4f, 0f, 4f }) c.DrawLine(P(3, y), P(7, y), color, w * 0.7f);
                c.DrawLine(P(-6, 5), P(7, 5), color, w * 0.7f);
                break;
            case IconKind.Coin:
                c.DrawCircle(center, 9f * u, color);
                c.DrawArc(center, 5f * u, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.35f), w * 0.8f, true);
                break;
            case IconKind.Menu:
                foreach (float y in new[] { -6f, 0f, 6f }) c.DrawLine(P(-9, y), P(9, y), color, w);
                break;
            case IconKind.Save:
                c.DrawRect(new Rect2(P(-9, -9), new Vector2(18 * u, 18 * u)), color, false, w);
                c.DrawRect(new Rect2(P(-5, -9), new Vector2(10 * u, 6 * u)), color);
                c.DrawRect(new Rect2(P(-5, 2), new Vector2(10 * u, 7 * u)), color, false, w * 0.8f);
                break;
            case IconKind.Play:
                c.DrawColoredPolygon(new[] { P(-6, -9), P(9, 0), P(-6, 9) }, color);
                break;
            case IconKind.FastForward:
                c.DrawColoredPolygon(new[] { P(-10, -8), P(0, 0), P(-10, 8) }, color);
                c.DrawColoredPolygon(new[] { P(0, -8), P(10, 0), P(0, 8) }, color);
                break;
        }
    }
}

/// <summary>A round or rounded-square button with a vector icon and an optional caption and notification dot.</summary>
public partial class IconButton : Control
{
    private readonly IconKind _kind;
    private bool _hover, _down;
    public string Caption { get; set; }
    public bool Active { get; set; }
    public bool Badge { get; set; }
    public bool Primary { get; set; }
    public bool Round { get; set; }
    public event Action? Pressed;

    public IconButton(IconKind kind, string caption, string tooltip = "", Vector2? size = null)
    {
        _kind = kind; Caption = caption; TooltipText = tooltip;
        CustomMinimumSize = size ?? new Vector2(84, 66);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var fill = Primary ? (_hover ? AppTheme.Accent : AppTheme.AccentDim)
            : Active ? new Color(AppTheme.Accent, 0.22f) : _hover ? AppTheme.CardHover : new Color(AppTheme.Card, 0.55f);
        var border = Primary || Active ? AppTheme.Accent : _hover ? AppTheme.AccentDim : new Color(AppTheme.Border, 0.8f);
        DrawStyleBox(AppTheme.Box(fill, Round ? (int)(Size.Y / 2) : 14, border, 1), new Rect2(Vector2.Zero, Size));
        var ink = Primary || Active || _hover ? Colors.White : AppTheme.Muted;
        bool captioned = Caption.Length > 0 && !Round;
        bool inline = Caption.Length > 0 && Round;
        Icons.Draw(this, _kind, inline ? new Vector2(Size.Y * 0.55f, Size.Y / 2) : new Vector2(Size.X / 2, captioned ? Size.Y * 0.4f : Size.Y / 2),
            Math.Min(Size.Y, Size.X) * (captioned ? 0.42f : inline ? 0.46f : 0.5f), ink);
        if (captioned) DrawString(font, new Vector2(0, Size.Y - 9), Caption, HorizontalAlignment.Center, Size.X, 12, ink);
        if (inline) DrawString(AppTheme.Bold, new Vector2(Size.Y * 1.0f, Size.Y / 2 + 6), Caption, HorizontalAlignment.Left, Size.X - Size.Y * 1.1f, 15, ink);
        if (Badge) { DrawCircle(new Vector2(Size.X - 14, 14), 6.5f, AppTheme.Bg); DrawCircle(new Vector2(Size.X - 14, 14), 5f, AppTheme.Bad); }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed) _down = true;
            else { if (_down && Size.X > mb.Position.X && mb.Position.X >= 0 && mb.Position.Y >= 0 && mb.Position.Y <= Size.Y) Pressed?.Invoke(); _down = false; }
            QueueRedraw();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; _down = false; QueueRedraw(); }
    }
}

/// <summary>An indicator in the top bar: a caption, a big number and a coloured trend arrow. Clicking opens its chart.</summary>
public partial class HudChip : Control
{
    private string Caption = "", _value = "", _delta = "";
    private Color _color = Colors.White;
    private bool _hover;
    public event Action? Pressed;

    public HudChip() { CustomMinimumSize = new Vector2(106, 54); MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand; }

    public void Set(string caption, string value, string delta, Color color, string tooltip = "")
    {
        Caption = caption; _value = value; _delta = delta; _color = color; TooltipText = tooltip;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        DrawStyleBox(AppTheme.Box(_hover ? AppTheme.CardHover : new Color(AppTheme.Card, 0.6f), 12, new Color(_hover ? AppTheme.Accent : AppTheme.Border, 0.9f), 1), new Rect2(Vector2.Zero, Size));
        DrawRect(new Rect2(10, 8, 3, Size.Y - 16), _color);
        DrawString(font, new Vector2(22, 20), Caption, HorizontalAlignment.Left, Size.X - 28, 11, AppTheme.Muted);
        DrawString(AppTheme.Bold, new Vector2(22, 43), _value, HorizontalAlignment.Left, Size.X - 60, 20, Colors.White);
        DrawString(font, new Vector2(Size.X - 56, 43), _delta, HorizontalAlignment.Right, 48, 13, _color);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Pressed?.Invoke();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
    }
}
