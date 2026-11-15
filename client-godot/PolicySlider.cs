using Godot;

namespace OpenPolitics.Client;

/// <summary>
/// The policy slider (custom control, not a skinned HSlider). A thick track with tick marks; chrome fill up to the value in force;
/// stripes for a scheduled change that has not taken effect yet; a light "ghost" disc where the policy stands now and the chrome
/// target thumb you drag; a big readout of the target above. The compact variant (party platform) is a thin single row with an
/// optional ink marker for the government's current value.
/// </summary>
public partial class PolicySlider : Control
{
    public double Min { get; private set; }
    public double Max { get; private set; } = 1;
    public double Step { get; private set; } = 0.1;
    public double InForce { get; private set; }
    public double? Scheduled { get; private set; }
    public double Target { get; private set; }
    public double? GovMarker { get; set; }
    /// <summary>The settings the player's political capital pays for now (decision 0013), drawn as a soft band around the track; null = none.</summary>
    public (double Lo, double Hi)? Affordable { get => _affordable; set { _affordable = value; QueueRedraw(); } }
    private (double Lo, double Hi)? _affordable;
    public bool Editable { get; set; } = true;
    public bool Compact { get; }
    public Func<double, string> Format { get; set; } = v => Ui.Num(v);
    public string MinLabel { get; set; } = "";
    public string MaxLabel { get; set; } = "";
    public event Action<double>? TargetChanged;
    public event Action? Submitted;

    private bool _drag, _hover;

    public PolicySlider(bool compact = false)
    {
        Compact = compact;
        CustomMinimumSize = new Vector2(compact ? 140 : 260, compact ? 28 : 92);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public void Configure(double min, double max, double step, double inForce, double? scheduled, double target)
    {
        Min = min; Max = Math.Max(max, min + 1e-9); Step = Math.Max(step, 1e-6);
        InForce = inForce; Scheduled = scheduled; Target = Math.Clamp(target, Min, Max);
        QueueRedraw();
    }

    public void SetTarget(double v, bool notify = true)
    {
        v = Math.Clamp(Math.Round((v - Min) / Step) * Step + Min, Min, Max);
        if (Math.Abs(v - Target) < 1e-12) return;
        Target = v;
        QueueRedraw();
        if (notify) TargetChanged?.Invoke(v);
    }

    private float Pad => Compact ? 10 : 16;
    private float TrackY => Compact ? Size.Y / 2 : 56;
    private float X(double v) => Pad + (float)((v - Min) / (Max - Min)) * (Size.X - 2 * Pad);
    private double V(float x) => Min + (Math.Clamp(x, Pad, Size.X - Pad) - Pad) / Math.Max(Size.X - 2 * Pad, 1) * (Max - Min);

    public override void _Draw()
    {
        float y = TrackY, th = Compact ? 4 : 6, r = th / 2;
        if (!Compact)
        {
            var (fig, unit) = Ui.Split(Format(Target));
            DrawString(AppTheme.FigSemi, new Vector2(Pad - 2, 28), fig, HorizontalAlignment.Left, -1, 28, AppTheme.Ink);
            float fw = AppTheme.FigSemi.GetStringSize(fig, HorizontalAlignment.Left, -1, 28).X;
            if (unit.Length > 0) DrawString(AppTheme.Regular, new Vector2(Pad + fw + 5, 28), unit, HorizontalAlignment.Left, -1, 14, AppTheme.Ink3);
            double delta = Target - InForce;
            if (Math.Abs(delta) > Step / 2)
            {
                string d = (delta > 0 ? "\u25b2 " : "\u25bc ") + Ui.Num(Math.Abs(delta), Math.Abs(delta) >= 10 ? "0" : "0.0");
                float ux = Pad + fw + 5 + (unit.Length > 0 ? AppTheme.Regular.GetStringSize(unit, HorizontalAlignment.Left, -1, 14).X + 10 : 6);
                DrawString(AppTheme.FigSemi, new Vector2(ux, 27), d, HorizontalAlignment.Left, -1, 12, AppTheme.Chrome);
            }
        }
        float x0 = X(Min), x1 = X(Max);
        if (!Compact && _affordable is { } band && band.Hi > band.Lo)
        {
            float a = X(Math.Max(Min, band.Lo)), b = X(Math.Min(Max, band.Hi));
            DrawStyleBox(AppTheme.Box(new Color(AppTheme.Good, 0.20f), 7), new Rect2(a - 7, y - 7, b - a + 14, 14));
        }
        DrawStyleBox(AppTheme.Box(AppTheme.TrackLight, (int)Math.Ceiling(r)), new Rect2(x0 - r, y - r, x1 - x0 + th, th));
        if (!Compact)
            for (int i = 0; i <= 10; i++)
            {
                float tx = x0 + (x1 - x0) * i / 10f;
                bool major = i % 5 == 0;
                DrawRect(new Rect2(tx - 0.5f, y + 8, 1, major ? 6 : 3), major ? AppTheme.Ink3 : AppTheme.Border);
            }
        float fx = X(Compact ? Target : InForce);
        var fillColor = Editable ? AppTheme.Chrome : AppTheme.InkDisabled;
        if (fx > x0 + 0.5f) DrawStyleBox(AppTheme.Box(fillColor, (int)Math.Ceiling(r)), new Rect2(x0 - r, y - r, fx - x0 + th, th));
        if (!Compact && Scheduled is { } sch && Math.Abs(sch - InForce) > Step / 2)
        {
            float a = X(Math.Min(sch, InForce)), b = X(Math.Max(sch, InForce));
            for (float sx = a; sx < b; sx += 5)
                DrawLine(new Vector2(sx, y + r), new Vector2(Math.Min(sx + th, b), y - r), new Color(AppTheme.Chrome, 0.6f), 1.5f, true);
        }
        if (!Compact && Math.Abs(Target - InForce) > Step / 2)
        {
            float tx = X(Target), ix = X(InForce);
            DrawRect(new Rect2(Math.Min(tx, ix), y - 0.75f, Math.Abs(tx - ix), 1.5f), new Color(AppTheme.Chrome, 0.5f));
            DrawCircle(new Vector2(ix, y), 5, AppTheme.Surface);
            DrawArc(new Vector2(ix, y), 4.5f, 0, Mathf.Tau, 24, fillColor, 2, true);
        }
        if (GovMarker is { } g) DrawRect(new Rect2(X(g) - 1, y - 7, 2, 14), AppTheme.Ink);
        float tr = Compact ? 7 : (_drag || _hover ? 11 : 10);
        var tp = new Vector2(X(Target), y);
        DrawCircle(tp + new Vector2(0, 1.5f), tr + 1, new Color(AppTheme.Ink, 0.12f));
        DrawCircle(tp, tr, new Color("ffffff"));
        DrawArc(tp, tr - 1.25f, 0, Mathf.Tau, 40, Editable ? (_drag ? AppTheme.ChromeDark : AppTheme.Chrome) : AppTheme.InkDisabled, 2.5f, true);
        if (_drag && !Compact) DrawCircle(tp, 3.5f, AppTheme.Chrome);
        if (HasFocus() && !Compact) DrawArc(tp, tr + 4, 0, Mathf.Tau, 40, new Color(AppTheme.Chrome, 0.35f), 3, true);
        if (!Compact)
        {
            string lo = MinLabel.Length > 0 ? MinLabel : Format(Min), hi = MaxLabel.Length > 0 ? MaxLabel : Format(Max);
            DrawString(AppTheme.FigSemi, new Vector2(x0 - 4, y + 32), Ui.Fix(lo), HorizontalAlignment.Left, -1, 11, AppTheme.Ink3);
            DrawString(AppTheme.FigSemi, new Vector2(x1 - 200 + 4, y + 32), Ui.Fix(hi), HorizontalAlignment.Right, 200, 11, AppTheme.Ink3);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Editable) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _drag = mb.Pressed;
                if (mb.Pressed) { GrabFocus(); SetTarget(V(mb.Position.X)); }
                QueueRedraw(); AcceptEvent();
                break;
            case InputEventMouseMotion mm when _drag:
                SetTarget(V(mm.Position.X)); AcceptEvent();
                break;
            case InputEventKey { Pressed: true } k:
                double step = k.ShiftPressed ? Step * 10 : Step;
                if (k.Keycode is Key.Left or Key.Down) SetTarget(Target - step);
                else if (k.Keycode is Key.Right or Key.Up) SetTarget(Target + step);
                else if (k.Keycode == Key.Pageup) SetTarget(Target + (Max - Min) / 10);
                else if (k.Keycode == Key.Pagedown) SetTarget(Target - (Max - Min) / 10);
                else if (k.Keycode == Key.Home) SetTarget(Min);
                else if (k.Keycode == Key.End) SetTarget(Max);
                else if (k.Keycode is Key.Enter or Key.KpEnter) Submitted?.Invoke();
                else return;
                AcceptEvent();
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
        else if (what is (int)NotificationFocusEnter or (int)NotificationFocusExit) QueueRedraw();
    }
}
