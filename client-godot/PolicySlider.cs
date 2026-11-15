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
    private float TrackY => Compact ? Size.Y / 2 : 54;
    private float X(double v) => Pad + (float)((v - Min) / (Max - Min)) * (Size.X - 2 * Pad);
    private double V(float x) => Min + (Math.Clamp(x, Pad, Size.X - Pad) - Pad) / Math.Max(Size.X - 2 * Pad, 1) * (Max - Min);

    public override void _Draw()
    {
        float y = TrackY, th = Compact ? 6 : 10, r = th / 2;
        var font = AppTheme.Bold;
        if (!Compact)
        {
            var (fig, unit) = Ui.Split(Format(Target));
            DrawString(AppTheme.FigBold, new Vector2(Pad - 2, 26), fig, HorizontalAlignment.Left, -1, 26, AppTheme.Ink);
            float fw = AppTheme.FigBold.GetStringSize(fig, HorizontalAlignment.Left, -1, 26).X;
            if (unit.Length > 0) DrawString(font, new Vector2(Pad + fw + 4, 26), unit, HorizontalAlignment.Left, -1, 15, AppTheme.Ink2);
        }
        float x0 = X(Min), x1 = X(Max);
        DrawStyleBox(AppTheme.Box(AppTheme.TrackLight, (int)r), new Rect2(x0 - r, y - r, x1 - x0 + th, th));
        // ticks: five segments
        if (!Compact)
            for (int i = 0; i <= 5; i++)
            {
                float tx = x0 + (x1 - x0) * i / 5f;
                DrawRect(new Rect2(tx - 1, y - r - 5, 2, th + 10), i is 0 or 5 ? AppTheme.Ink2 : new Color("a0a0a0"));
            }
        float fx = X(Compact ? Target : InForce);
        if (fx > x0 + 0.5f) DrawStyleBox(AppTheme.Box(Editable ? AppTheme.Chrome : AppTheme.Ink3, (int)r), new Rect2(x0 - r, y - r, fx - x0 + th, th));
        if (!Compact && Scheduled is { } sch && Math.Abs(sch - InForce) > Step / 2)
        {
            float a = X(Math.Min(sch, InForce)), b = X(Math.Max(sch, InForce));
            for (float sx = a; sx < b; sx += 6)
                DrawLine(new Vector2(sx, y + r), new Vector2(Math.Min(sx + th, b), y - r), new Color(AppTheme.Chrome, 0.75f), 2f, true);
        }
        if (GovMarker is { } g) DrawRect(new Rect2(X(g) - 1.5f, y - 9, 3, 18), AppTheme.Ink);
        if (!Compact && Math.Abs(Target - InForce) > Step / 2)
        {
            DrawCircle(new Vector2(X(InForce), y), 10, AppTheme.Surface);
            DrawCircle(new Vector2(X(InForce), y), 8, AppTheme.ChromeLight);
        }
        float tr = Compact ? 9 : (_drag || _hover ? 15 : 14);
        var tc = Editable ? (_drag ? AppTheme.ChromeDark : AppTheme.Chrome) : AppTheme.Ink3;
        var tp = new Vector2(X(Target), y);
        DrawCircle(tp, tr + 2.5f, AppTheme.Surface);
        DrawCircle(tp, tr, tc);
        if (HasFocus() && !Compact) DrawArc(tp, tr + 5, 0, Mathf.Tau, 40, AppTheme.Ink, 2, true);
        if (!Compact)
        {
            string lo = MinLabel.Length > 0 ? MinLabel : Format(Min), hi = MaxLabel.Length > 0 ? MaxLabel : Format(Max);
            DrawString(AppTheme.Regular, new Vector2(x0 - 6, y + 30), Ui.Fix(lo), HorizontalAlignment.Left, -1, 12, AppTheme.Ink3);
            DrawString(AppTheme.Regular, new Vector2(x1 - 200 + 6, y + 30), Ui.Fix(hi), HorizontalAlignment.Right, 200, 12, AppTheme.Ink3);
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
