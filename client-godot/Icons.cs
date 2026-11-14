using Godot;

namespace OpenPolitics.Client;

public enum IconKind
{
    Overview, Policies, Voters, Politics, Party, News, Coin, Menu, Save, Play, FastForward,
    Percent, Tag, Factory, House, Leaf, Arrows, Pill, Ballot, Target, Star, Bus, Family, Briefcase, Wheat, Wifi, Key, Globe, Clock,
    Bolt, Eye, Hand, Health, Education, Shield, Flask, Road, Columns, Alert, Scale, ChartUp, Gear, Users, Bank, CoinIn, CoinOut, Gauge,
    Chart, Close,
}

/// <summary>
/// One family of outline pictograms drawn with primitives on a 24-unit grid, so the client needs no image assets and stays sharp at any
/// size. <see cref="For"/> picks a pictogram for a policy or indicator id.
/// </summary>
public static class Icons
{
    /// <summary>The pictogram for a policy, indicator or voter group id (by keyword, so new content gets a sensible icon).</summary>
    public static IconKind For(string id)
    {
        string k = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
        (string Key, IconKind Icon)[] map =
        {
            ("pensioners", IconKind.Clock), ("rural_conservative", IconKind.Wheat), ("young_urban", IconKind.Users), ("middle_class", IconKind.House),
            ("low_income", IconKind.Hand), ("high_income", IconKind.Briefcase),
            ("income_tax", IconKind.Percent), ("vat", IconKind.Tag), ("corporate", IconKind.Factory), ("property", IconKind.House),
            ("carbon", IconKind.Leaf), ("wealth", IconKind.Coin), ("transaction", IconKind.Arrows), ("excise", IconKind.Pill),
            ("electoral", IconKind.Ballot), ("defence", IconKind.Target), ("culture", IconKind.Star), ("transport", IconKind.Bus),
            ("housing", IconKind.House), ("family", IconKind.Family), ("childcare", IconKind.Family), ("labour_policy", IconKind.Briefcase),
            ("rural", IconKind.Wheat), ("green", IconKind.Leaf), ("digital", IconKind.Wifi), ("environment", IconKind.Leaf),
            ("migration", IconKind.Globe), ("minimum_wage", IconKind.Coin), ("flexibility", IconKind.Briefcase), ("rent", IconKind.Key),
            ("drug", IconKind.Pill), ("university", IconKind.Education), ("retirement", IconKind.Clock), ("energy", IconKind.Bolt),
            ("business", IconKind.Factory), ("surveillance", IconKind.Eye), ("pension", IconKind.Hand), ("health", IconKind.Health),
            ("education", IconKind.Education), ("police", IconKind.Shield), ("research", IconKind.Flask), ("benefit", IconKind.Hand),
            ("infrastructure", IconKind.Road), ("administration", IconKind.Columns), ("crime", IconKind.Alert), ("inequality", IconKind.Scale),
            ("real_growth", IconKind.ChartUp), ("gdp", IconKind.ChartUp), ("capital", IconKind.Factory), ("productivity", IconKind.Gear),
            ("labour_force", IconKind.Users), ("unemployment", IconKind.Briefcase), ("inflation", IconKind.Tag), ("price", IconKind.Tag),
            ("debt", IconKind.Bank), ("interest", IconKind.Percent), ("revenue", IconKind.CoinIn), ("spending", IconKind.CoinOut),
            ("deficit", IconKind.Gauge),
        };
        foreach (var (key, icon) in map) if (k.Contains(key, StringComparison.Ordinal)) return icon;
        return IconKind.Gauge;
    }

    public static void Draw(CanvasItem c, IconKind kind, Vector2 center, float size, Color color)
    {
        float u = size / 24f;
        Vector2 P(float x, float y) => center + new Vector2(x, y) * u;
        float w = Math.Max(1.4f, 1.9f * u);
        void L(float x1, float y1, float x2, float y2) => c.DrawLine(P(x1, y1), P(x2, y2), color, w, true);
        void Poly(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[2 * i], xy[2 * i + 1]);
            c.DrawPolyline(pts, color, w, true);
        }
        void Ring(float x, float y, float r) => c.DrawArc(P(x, y), r * u, 0, Mathf.Tau, 32, color, w, true);
        void Dot(float x, float y, float r) => c.DrawCircle(P(x, y), r * u, color);
        void Arc(float x, float y, float r, float a0, float a1) => c.DrawArc(P(x, y), r * u, a0, a1, 24, color, w, true);
        void Box(float x, float y, float bw, float bh) => c.DrawRect(new Rect2(P(x, y), new Vector2(bw, bh) * u), color, false, w);
        void Fill(float x, float y, float bw, float bh) => c.DrawRect(new Rect2(P(x, y), new Vector2(bw, bh) * u), color);
        void Tri(params float[] xy) => c.DrawColoredPolygon(new[] { P(xy[0], xy[1]), P(xy[2], xy[3]), P(xy[4], xy[5]) }, color);

        switch (kind)
        {
            case IconKind.Overview: Fill(-9, 2, 4, 7); Fill(-2, -3, 4, 12); Fill(5, -8, 4, 17); break;
            case IconKind.Chart: Poly(-10, 6, -4, 0, 1, 3, 9, -6); L(-10, 9, 10, 9); break;
            case IconKind.Policies:
                foreach (var (y, k) in new[] { (-7f, -3f), (0f, 4f), (7f, -1f) }) { L(-10, y, 10, y); Dot(k, y, 3.2f); }
                break;
            case IconKind.Voters:
                Ring(-4, -4, 3.6f); Arc(-4, 9, 7, Mathf.Pi * 1.08f, Mathf.Pi * 1.92f);
                Ring(6, -2, 3f); Arc(6, 9, 6, Mathf.Pi * 1.15f, Mathf.Pi * 1.9f);
                break;
            case IconKind.Politics:
                foreach (var (r, n) in new[] { (5f, 3), (8.5f, 5), (12f, 7) })
                    for (int i = 0; i < n; i++) { float a = Mathf.Pi + Mathf.Pi * i / (n - 1); Dot(Mathf.Cos(a) * r, 7 + Mathf.Sin(a) * r, 1.7f); }
                L(-12, 9.5f, 12, 9.5f);
                break;
            case IconKind.Party: L(-6, 11, -6, -10); c.DrawColoredPolygon(new[] { P(-6, -10), P(9, -5), P(-6, 0) }, color); break;
            case IconKind.News:
                Box(-10, -8, 20, 16); Fill(-7, -5, 6, 5);
                foreach (float y in new[] { -4f, -0.5f }) L(2, y, 7, y);
                L(-7, 3.5f, 7, 3.5f);
                break;
            case IconKind.Coin: Ring(0, 0, 9); L(0, -5, 0, 5); Arc(0, -1.6f, 3, Mathf.Pi * 0.9f, Mathf.Pi * 2.1f); break;
            case IconKind.Menu: foreach (float y in new[] { -6f, 0f, 6f }) L(-8, y, 8, y); break;
            case IconKind.Save: Poly(-9, -9, 5, -9, 9, -5, 9, 9, -9, 9, -9, -9); Box(-5, -9, 8, 5); Box(-5, 2, 10, 7); break;
            case IconKind.Play: c.DrawColoredPolygon(new[] { P(-6, -9), P(9, 0), P(-6, 9) }, color); break;
            case IconKind.FastForward: Tri(-10, -8, 0, 0, -10, 8); Tri(0, -8, 10, 0, 0, 8); break;
            case IconKind.Close: L(-6, -6, 6, 6); L(-6, 6, 6, -6); break;
            case IconKind.Percent: L(7, -8, -7, 8); Ring(-5.5f, -5.5f, 3); Ring(5.5f, 5.5f, 3); break;
            case IconKind.Tag: Poly(-10, -2, -2, -10, 9, -10, 9, 1, 1, 9, -10, -2); Dot(4.5f, -5.5f, 1.8f); break;
            case IconKind.Factory: Poly(-10, 9, -10, -2, -4, 2, -4, -2, 2, 2, 2, -9, 7, -9, 7, 9, -10, 9); Fill(-7, 4, 3, 2.5f); Fill(-1, 4, 3, 2.5f); break;
            case IconKind.House: Poly(-10, 0, 0, -9, 10, 0); Poly(-7, -2, -7, 9, 7, 9, 7, -2); Box(-2, 3, 4, 6); break;
            case IconKind.Leaf:
            {
                var pts = new Vector2[25];
                for (int i = 0; i <= 12; i++)
                {
                    float tt = i / 12f, x = -10 + 20 * tt, y = -Mathf.Sin(Mathf.Pi * tt) * 6.5f;
                    pts[i] = center + new Vector2(x, y).Rotated(-Mathf.Pi / 4) * u;
                    pts[24 - i] = center + new Vector2(x, -y).Rotated(-Mathf.Pi / 4) * u;
                }
                c.DrawPolyline(pts, color, w, true);
                c.DrawLine(center + new Vector2(-12, 0).Rotated(-Mathf.Pi / 4) * u, center + new Vector2(5, 0).Rotated(-Mathf.Pi / 4) * u, color, w, true);
                break;
            }
            case IconKind.Arrows: L(-9, -4, 9, -4); Poly(5, -8, 9, -4, 5, 0); L(9, 4, -9, 4); Poly(-5, 0, -9, 4, -5, 8); break;
            case IconKind.Pill:
                c.DrawSetTransform(center, -Mathf.Pi / 4, Vector2.One);
                c.DrawStyleBox(AppTheme.Box(new Color(0, 0, 0, 0), (int)(5 * u), color, (int)Math.Round(w)), new Rect2(new Vector2(-11, -5) * u, new Vector2(22, 10) * u));
                c.DrawLine(new Vector2(0, -5) * u, new Vector2(0, 5) * u, color, w, true);
                c.DrawRect(new Rect2(new Vector2(-9, -3) * u, new Vector2(9, 6) * u), color);
                c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                break;
            case IconKind.Ballot: Poly(-10, -1, -10, 9, 10, 9, 10, -1, -10, -1); L(-4, 4, 4, 4); Poly(-5, -1, -5, -10, 5, -10, 5, -1); L(-2, -6, 0, -4); L(0, -4, 3, -8); break;
            case IconKind.Target: Ring(0, 0, 9); Ring(0, 0, 4.5f); Dot(0, 0, 1.6f); L(0, -12, 0, -7); L(0, 7, 0, 12); L(-12, 0, -7, 0); L(7, 0, 12, 0); break;
            case IconKind.Star:
            {
                var pts = new Vector2[11];
                for (int i = 0; i < 10; i++) { float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5; float r = i % 2 == 0 ? 10.5f : 4.5f; pts[i] = P(Mathf.Cos(a) * r, Mathf.Sin(a) * r + 1); }
                pts[10] = pts[0];
                c.DrawPolyline(pts, color, w, true);
                break;
            }
            case IconKind.Bus: Poly(-8, 6, -8, -8, 8, -8, 8, 6, -8, 6); L(-8, -1, 8, -1); Dot(-4.5f, 3, 1.5f); Dot(4.5f, 3, 1.5f); L(-6, 6, -6, 9); L(6, 6, 6, 9); break;
            case IconKind.Family: Ring(-5, -6, 3); Arc(-5, 6, 6, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f); Ring(5, -6, 3); Arc(5, 6, 6, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f); Ring(0, 3, 2.2f); Arc(0, 11, 4, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f); break;
            case IconKind.Briefcase: Poly(-10, -4, 10, -4, 10, 8, -10, 8, -10, -4); Poly(-4, -4, -4, -8, 4, -8, 4, -4); L(-10, 1, 10, 1); break;
            case IconKind.Wheat:
                L(0, 10, 0, -10);
                foreach (float y in new[] { -6f, -1f, 4f }) { Poly(0, y + 3, -5, y - 1, -5, y - 3); Poly(0, y + 3, 5, y - 1, 5, y - 3); }
                break;
            case IconKind.Wifi:
                Dot(0, 7, 1.9f);
                foreach (float r in new[] { 5f, 10f, 15f }) Arc(0, 9, r, Mathf.Pi * 1.25f, Mathf.Pi * 1.75f);
                break;
            case IconKind.Key: Ring(-5, 0, 4.5f); L(-0.5f, 0, 10, 0); L(7, 0, 7, 4); L(4, 0, 4, 3); break;
            case IconKind.Globe:
            {
                Ring(0, 0, 9.5f); L(-9.5f, 0, 9.5f, 0);
                var pts = new Vector2[33];
                for (int i = 0; i <= 32; i++) { float a = i * Mathf.Tau / 32; pts[i] = P(Mathf.Cos(a) * 4.2f, Mathf.Sin(a) * 9.5f); }
                c.DrawPolyline(pts, color, w, true);
                break;
            }
            case IconKind.Clock: Ring(0, 0, 9.5f); L(0, 0, 0, -6); L(0, 0, 4.5f, 2.5f); break;
            case IconKind.Bolt: c.DrawColoredPolygon(new[] { P(2, -11), P(-7, 2), P(-1, 2), P(-3, 11), P(7, -2), P(1, -2) }, color); break;
            case IconKind.Eye: Arc(0, 9, 13, Mathf.Pi * 1.22f, Mathf.Pi * 1.78f); Arc(0, -9, 13, Mathf.Pi * 0.22f, Mathf.Pi * 0.78f); Ring(0, 0, 3.6f); Dot(0, 0, 1.4f); break;
            case IconKind.Hand: Poly(-11, 3, -6, 3, -1, 5, 4, 5); Poly(-11, 9, -5, 9, 3, 10, 11, 4, 9, 2, 3, 5); Ring(3, -5, 4.5f); break;
            case IconKind.Health: c.DrawStyleBox(AppTheme.Box(color, (int)(2 * u)), new Rect2(P(-3, -10), new Vector2(6, 20) * u)); c.DrawStyleBox(AppTheme.Box(color, (int)(2 * u)), new Rect2(P(-10, -3), new Vector2(20, 6) * u)); break;
            case IconKind.Education: c.DrawColoredPolygon(new[] { P(-12, -3), P(0, -9), P(12, -3), P(0, 3) }, color); Poly(-7, 0, -7, 6, 0, 9, 7, 6, 7, 0); L(10, -2, 10, 6); break;
            case IconKind.Shield: Poly(0, -10, 9, -7, 9, 0, 7, 5, 0, 10, -7, 5, -9, 0, -9, -7, 0, -10); L(-4, 0, -1, 3); L(-1, 3, 5, -4); break;
            case IconKind.Flask: Poly(-3, -10, -3, -3, -9, 8, 9, 8, 3, -3, 3, -10); L(-5, -10, 5, -10); L(-6, 3, 6, 3); break;
            case IconKind.Road: L(-4, -10, -9, 10); L(4, -10, 9, 10); L(0, -9, 0, -6); L(0, -2, 0, 2); L(0, 6, 0, 10); break;
            case IconKind.Columns: Poly(-11, -4, 0, -10, 11, -4, -11, -4); L(-11, 9, 11, 9); foreach (float x in new[] { -7f, -2.3f, 2.3f, 7f }) L(x, -2, x, 7); break;
            case IconKind.Alert: Poly(0, -10, 10, 8, -10, 8, 0, -10); L(0, -3, 0, 2); Dot(0, 5, 1.4f); break;
            case IconKind.Scale: L(0, -10, 0, 9); L(-6, 9, 6, 9); L(-9, -6, 9, -6); Poly(-9, -6, -12, 2, -6, 2, -9, -6); Poly(9, -6, 6, 2, 12, 2, 9, -6); break;
            case IconKind.ChartUp: Poly(-10, 7, -4, 0, 0, 3, 9, -6); Poly(4, -6, 9, -6, 9, -1); L(-10, 10, 10, 10); break;
            case IconKind.Gear:
                Ring(0, 0, 6.5f); Ring(0, 0, 2.5f);
                for (int i = 0; i < 8; i++) { float a = i * Mathf.Tau / 8; L(Mathf.Cos(a) * 6.5f, Mathf.Sin(a) * 6.5f, Mathf.Cos(a) * 10, Mathf.Sin(a) * 10); }
                break;
            case IconKind.Users: Ring(0, -5, 4); Arc(0, 10, 9, Mathf.Pi * 1.12f, Mathf.Pi * 1.88f); Ring(-8, -2, 2.6f); Ring(8, -2, 2.6f); break;
            case IconKind.Bank: Poly(-11, -4, 0, -10, 11, -4, -11, -4); L(-11, 9, 11, 9); L(-11, 6, 11, 6); foreach (float x in new[] { -7f, 0f, 7f }) L(x, -1, x, 4); break;
            case IconKind.CoinIn: Box(-11, -1, 22, 12); Ring(0, 5, 2.8f); L(0, -11, 0, -4); Poly(-3.5f, -7, 0, -3.5f, 3.5f, -7); break;
            case IconKind.CoinOut: Box(-11, -1, 22, 12); Ring(0, 5, 2.8f); L(0, -4, 0, -11); Poly(-3.5f, -8, 0, -11.5f, 3.5f, -8); break;
            case IconKind.Gauge: Arc(0, 4, 10, Mathf.Pi, Mathf.Tau); L(-10, 4, 10, 4); L(0, 4, 5, -3); Dot(0, 4, 1.8f); break;
        }
    }
}

/// <summary>
/// A button with a pictogram. Three looks: a toolbar tile (icon above a caption), an icon-only square, and an inline pill (icon then
/// caption, sized to the text). Primary paints it in the accent colour. Optional notification dot.
/// </summary>
public partial class IconButton : Control
{
    private readonly IconKind _kind;
    private bool _hover, _down;
    private string _caption;
    private float _anim; // 0..1 hover/active highlight, eased
    public string Caption { get => _caption; set { _caption = value; Fit(); QueueRedraw(); } }
    public bool Active { get; set; }
    public bool Badge { get; set; }
    public bool Primary { get; set; }
    /// <summary>Inline look: icon and caption on one line (sized to the caption).</summary>
    public bool Round { get; set; }
    public Color? IconColor { get; set; }
    public string SubCaption { get; set; } = "";
    public event Action? Pressed;

    public IconButton(IconKind kind, string caption, string tooltip = "", Vector2? size = null)
    {
        _kind = kind; _caption = caption; TooltipText = tooltip;
        CustomMinimumSize = size ?? new Vector2(88, 64);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        FocusMode = FocusModeEnum.None;
    }

    private float _baseWidth = -1;

    /// <summary>Inline buttons grow to fit their caption.</summary>
    private void Fit()
    {
        if (!Round || _caption.Length == 0) return;
        if (_baseWidth < 0) _baseWidth = CustomMinimumSize.X;
        float h = CustomMinimumSize.Y;
        float text = AppTheme.Bold.GetStringSize(_caption, HorizontalAlignment.Left, -1, AppTheme.Body).X;
        if (SubCaption.Length > 0) text = Math.Max(text, AppTheme.Regular.GetStringSize(SubCaption, HorizontalAlignment.Left, -1, 12).X);
        CustomMinimumSize = new Vector2(Math.Max(_baseWidth, h * 0.9f + text + 18), h);
    }

    public override void _Ready() => Fit();

    public override void _Process(double delta)
    {
        float target = _hover || Active ? 1f : 0f;
        if (Math.Abs(_anim - target) < 0.01f) { if (_anim != target) { _anim = target; QueueRedraw(); } return; }
        _anim = Mathf.MoveToward(_anim, target, (float)delta * 7f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        bool tile = _caption.Length > 0 && !Round, inline = _caption.Length > 0 && Round;
        var rect = new Rect2(Vector2.Zero, Size);
        Color ink;
        if (Primary)
        {
            var fill = (_down ? AppTheme.Accent.Darkened(0.12f) : AppTheme.Accent).Lerp(AppTheme.Accent.Lightened(0.15f), _hover ? 1 : 0);
            var box = AppTheme.Box(fill, 10);
            box.ShadowColor = new Color(AppTheme.Accent, 0.25f + 0.15f * _anim); box.ShadowSize = 10;
            DrawStyleBox(box, rect);
            ink = AppTheme.AccentInk;
        }
        else if (tile)
        {
            var fill = Active ? AppTheme.Tint(AppTheme.Accent, 0.18f) : new Color(AppTheme.CardHover, 0.9f * _anim);
            DrawStyleBox(AppTheme.Box(fill, 12, Active ? new Color(AppTheme.Accent, 0.6f) : null, 1), rect);
            if (Active) DrawStyleBox(AppTheme.Box(AppTheme.Accent, 2), new Rect2(Size.X / 2 - 14, 3, 28, 3));
            ink = Active ? Colors.White : AppTheme.Text2.Lerp(Colors.White, _anim);
        }
        else
        {
            var fill = new Color(AppTheme.Card, 0.55f).Lerp(AppTheme.CardHover, _anim);
            DrawStyleBox(AppTheme.Box(fill, 10, new Color(AppTheme.BorderStrong, 0.35f + 0.4f * _anim), 1), rect);
            ink = AppTheme.Text2.Lerp(Colors.White, _anim);
        }
        var iconInk = IconColor ?? ink;
        if (tile)
        {
            Icons.Draw(this, _kind, new Vector2(Size.X / 2, Size.Y * 0.38f), 24, iconInk);
            var font = Active ? AppTheme.Semi : AppTheme.Medium;
            DrawString(font, new Vector2(2, Size.Y - 11), _caption, HorizontalAlignment.Center, Size.X - 4, AppTheme.Caption, ink);
        }
        else if (inline)
        {
            float icon = Math.Min(22, Size.Y * 0.46f);
            Icons.Draw(this, _kind, new Vector2(14 + icon / 2, Size.Y / 2), icon, iconInk);
            float x = 14 + icon + 10;
            if (SubCaption.Length > 0)
            {
                DrawString(AppTheme.Bold, new Vector2(x, Size.Y / 2 - 2), _caption, HorizontalAlignment.Left, Size.X - x - 8, AppTheme.Body, ink);
                DrawString(AppTheme.Regular, new Vector2(x, Size.Y / 2 + 14), SubCaption, HorizontalAlignment.Left, Size.X - x - 8, 12, Primary ? ink : AppTheme.Muted);
            }
            else DrawString(AppTheme.Bold, new Vector2(x, Size.Y / 2 + 6), _caption, HorizontalAlignment.Left, Size.X - x - 8, AppTheme.Body, ink);
        }
        else Icons.Draw(this, _kind, Size / 2, Math.Min(22, Math.Min(Size.X, Size.Y) * 0.5f), iconInk);
        if (Badge) { DrawCircle(new Vector2(Size.X - 13, 13), 7f, AppTheme.Surface); DrawCircle(new Vector2(Size.X - 13, 13), 5f, AppTheme.Bad); }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed) _down = true;
            else { if (_down && new Rect2(Vector2.Zero, Size).HasPoint(mb.Position)) Pressed?.Invoke(); _down = false; }
            QueueRedraw();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; _down = false; QueueRedraw(); }
    }
}

/// <summary>An indicator in the top bar: a caption, a big number, a coloured trend and a status underline. Clicking opens its chart.</summary>
public partial class HudChip : Control
{
    private string _caption = "", _value = "", _delta = "";
    private Color _color = Colors.White;
    private bool _hover;
    public event Action? Pressed;

    public HudChip()
    {
        CustomMinimumSize = new Vector2(96, 48);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public void Set(string caption, string value, string delta, Color color, string tooltip = "")
    {
        _caption = caption; _value = value; _delta = delta; _color = color; TooltipText = tooltip;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_hover) DrawStyleBox(AppTheme.Box(AppTheme.CardHover, 10), new Rect2(Vector2.Zero, Size));
        float x = 12;
        DrawString(AppTheme.Medium, new Vector2(x, 18), _caption, HorizontalAlignment.Left, Size.X - x - 6, AppTheme.Caption, AppTheme.Muted);
        DrawString(AppTheme.Bold, new Vector2(x, 40), _value, HorizontalAlignment.Left, Size.X - x - 4, 20, Colors.White);
        float vw = AppTheme.Bold.GetStringSize(_value, HorizontalAlignment.Left, -1, 20).X;
        float dw = AppTheme.Semi.GetStringSize(_delta, HorizontalAlignment.Left, -1, AppTheme.Caption).X;
        if (_delta.Length > 0 && x + vw + 6 + dw <= Size.X - 4)
            DrawString(AppTheme.Semi, new Vector2(x + vw + 6, 40), _delta, HorizontalAlignment.Left, -1, AppTheme.Caption, _color);
        DrawStyleBox(AppTheme.Box(_color, 1), new Rect2(x, Size.Y - 4, 22, 2.5f));
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

/// <summary>A rounded bar for a value in a range. Diverging meters fill from the middle (for -1..1 moods).</summary>
public partial class Meter : Control
{
    private readonly double _value, _min, _max;
    private readonly Color _color;
    private readonly bool _diverging;

    public Meter(double value, double min, double max, Color color, float width, bool diverging = false, float height = 8)
    {
        _value = value; _min = min; _max = max; _color = color; _diverging = diverging;
        CustomMinimumSize = new Vector2(width, Math.Max(height, 14));
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        _h = height;
    }
    private readonly float _h;

    public override void _Draw()
    {
        float y = (Size.Y - _h) / 2;
        var track = new Rect2(0, y, Size.X, _h);
        DrawStyleBox(AppTheme.Box(new Color(AppTheme.Bg, 0.85f), (int)(_h / 2)), track);
        double span = Math.Max(_max - _min, 1e-12);
        float f = (float)Math.Clamp((_value - _min) / span, 0, 1);
        if (_diverging)
        {
            float mid = Size.X / 2, x = Size.X * f;
            var r = new Rect2(Math.Min(mid, x), y, Math.Max(Math.Abs(x - mid), 3), _h);
            DrawStyleBox(AppTheme.Box(_color, (int)(_h / 2)), r);
            DrawLine(new Vector2(mid, y - 3), new Vector2(mid, y + _h + 3), AppTheme.Muted, 1.5f);
        }
        else if (f > 0) DrawStyleBox(AppTheme.Box(_color, (int)(_h / 2)), new Rect2(0, y, Math.Max(Size.X * f, _h), _h));
    }
}
