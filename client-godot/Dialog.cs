using Godot;

namespace OpenPolitics.Client;

/// <summary>
/// A centred dialog over the washed-out map (Democracy 4 style): white, square corners, 1 px dark border, a header with a pictogram disc
/// and a bold title, a square chrome CLOSE in the top-right corner, a scrolling body, and action buttons that hang attached under the
/// bottom edge on the right. In the compact size class it becomes a full-window sheet.
/// </summary>
public partial class Dialog : Control
{
    private readonly ColorRect _veil = new() { Color = AppTheme.Veil, MouseFilter = MouseFilterEnum.Stop };
    private readonly VBoxContainer _frame = Ui.VBox(0);
    private readonly PanelContainer _panel = new() { ThemeTypeVariation = "Dialog", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly ScrollContainer _scroll;
    private readonly Label _title;
    private readonly SquareTab _close;
    private readonly MarginContainer _bodyMargin;
    private readonly VBoxContainer _footer;

    public VBoxContainer Body { get; }
    /// <summary>Controls placed in the header between the title and CLOSE (tabs, ranges).</summary>
    public HBoxContainer HeaderExtra { get; } = Ui.HBox(4);
    /// <summary>A strip under the header that does not scroll (e.g. tabs).</summary>
    public VBoxContainer Fixed { get; } = Ui.VBox(0);
    /// <summary>Buttons attached under the dialog's bottom edge.</summary>
    public HBoxContainer Actions { get; } = Ui.HBox(0);
    /// <summary>The width the dialog asks for (it never exceeds the window minus a margin).</summary>
    public float PreferredWidth { get; set; } = 760;
    /// <summary>A blocking dialog ignores clicks on the veil and Esc.</summary>
    public bool Blocking { get; set; }
    /// <summary>Fill the available height instead of fitting the content.</summary>
    public bool FillHeight { get; set; }
    public string Key { get; set; } = "";
    public event Action? Closed;

    public Dialog(string title, IconKind? icon, string closeTip)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _veil.SetAnchorsPreset(LayoutPreset.FullRect);
        _veil.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !Blocking) Close(); };
        AddChild(_veil);

        var column = Ui.VBox(0);
        var head = Ui.HBox(14);
        var headLeft = Ui.HBox(14);
        headLeft.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (icon != null) headLeft.AddChild(new PictoDisc(icon.Value, 40));
        _title = Ui.L(title, Tx.Title);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.ClipText = true; _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        headLeft.AddChild(_title);
        head.AddChild(Ui.Margin(headLeft, 26, 14, 20, 12));
        head.GetChild<Control>(0).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        HeaderExtra.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(HeaderExtra);
        _close = new SquareTab(IconKind.Close, closeTip) { CustomMinimumSize = new Vector2(44, 44), SizeFlagsVertical = SizeFlags.ShrinkBegin, Inset = 4, IconScale = 0.34f };
        _close.Pressed += Close;
        head.AddChild(Ui.Margin(_close, 0, 0, 14, 0));
        head.AddChild(Ui.Gap(10));
        column.AddChild(head);
        column.AddChild(Fixed);

        (_scroll, Body) = Ui.Scroller(14);
        Body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.RemoveChild(Body);
        _bodyMargin = Ui.Margin(Body, 26, 0, 4, 24);
        _bodyMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_bodyMargin);
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(_scroll);
        _footer = new VBoxContainer { Visible = false };
        _footer.AddThemeConstantOverride("separation", 0);
        _footer.AddChild(new HairLine { CustomMinimumSize = new Vector2(0, 1) });
        Actions.AddThemeConstantOverride("separation", 10);
        Actions.Alignment = BoxContainer.AlignmentMode.End;
        _footer.AddChild(Ui.Margin(Actions, 22, 14));
        column.AddChild(_footer);
        _panel.AddChild(column);
        _frame.AddChild(_panel);
        AddChild(_frame);
        Layout.Changed += Relayout;
        TreeExiting += () => Layout.Changed -= Relayout;
    }

    public string Title { get => _title.Text; set => _title.Text = value; }
    public bool CloseEnabled { set { _close.Visible = value; } }

    /// <summary>Adds a flat chrome button (icon + label) to the attached action bar.</summary>
    public IconTextButton AddAction(string text, IconKind icon, Action onPress, bool disabled = false, string variation = "ButtonSecondary")
    {
        var b = Ui.Btn(text, onPress, variation, icon, null, disabled, 40);
        b.CustomMinimumSize = new Vector2(Layout.Compact ? 0 : 132, 40);
        if (Layout.Compact) b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Actions.AddChild(b);
        _footer.Visible = true;
        Relayout();
        return b;
    }

    /// <summary>Removes every action (when a dialog rebuilds itself).</summary>
    public void ClearActions() { Ui.Clear(Actions); _footer.Visible = false; }

    public void ClearBody() { Ui.Clear(Body); }

    public void Close()
    {
        if (!IsInsideTree()) return;
        Closed?.Invoke();
        QueueFree();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized || what == NotificationReady) Relayout();
        if (what == NotificationReady && Main.Animations && !DisplaySettings.ReduceMotion)
        {
            Modulate = new Color(1, 1, 1, 0);
            _frame.PivotOffset = _frame.Size / 2;
            _frame.Scale = new Vector2(0.97f, 0.97f);
            var tw = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quart).SetEase(Tween.EaseType.Out);
            tw.TweenProperty(this, "modulate:a", 1f, 0.18f);
            tw.TweenProperty(_frame, "scale", Vector2.One, 0.22f);
        }
    }

    public override void _Process(double delta)
    {
        // Fit to content once wrapped labels know their width (a couple of frames), cheaply.
        if (_fitFrames-- > 0) Relayout(); else SetProcess(false);
    }
    private int _fitFrames = 3;

    public void Refit() { _fitFrames = 3; SetProcess(true); }

    private void Relayout()
    {
        if (!IsInsideTree()) return;
        var size = Size;
        bool sheet = Layout.Compact || size.X < PreferredWidth + 40 || size.Y < 420;
        float actionsH = _footer.Visible ? 69 : 0;
        if (sheet)
        {
            _frame.Position = Vector2.Zero; _frame.Size = size;
            _panel.ThemeTypeVariation = "Sheet";
            return;
        }
        _panel.ThemeTypeVariation = "Dialog";
        float margin = Layout.Short ? 24 : 40;
        float w = Math.Min(PreferredWidth, size.X - 2 * margin);
        float maxH = size.Y - 2 * margin;
        float headH = 76 + Fixed.GetCombinedMinimumSize().Y;
        float contentH = _bodyMargin.GetCombinedMinimumSize().Y + 4;
        float h = FillHeight ? maxH : Math.Min(maxH, headH + contentH + actionsH);
        h = Math.Max(h, Math.Min(maxH, 220));
        _frame.Size = new Vector2(w, h);
        _frame.Position = new Vector2((size.X - w) / 2, (size.Y - h) / 2);
    }
}
