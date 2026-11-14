using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// Every policy as a card in a grid, filtered by category; the selected one opens in the inspector on the right with its slider in real
/// units, indexation, the apply button and an infographic of what the move would do.
/// </summary>
public partial class PoliciesScreen : Screen
{
    public override string TitleKey => "ui.client.tab.policies";
    public override float PreferredWidth => 0.92f;
    private readonly HBoxContainer _filters = Ui.HBox(4);
    private readonly GridContainer _grid = new() { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _inspector = Ui.VBox(12);
    private readonly PolicyEffectsPanel _effects = new() { ShowRange = false };
    private readonly Label _hint = Ui.Text("", Ui.Muted, AppTheme.Caption, true);
    private readonly List<PolicyCard> _cards = new();
    private string? _focus;
    private double _focusValue = double.NaN;
    private int _filter = -1;

    public PoliciesScreen()
    {
        var root = Ui.HBox(16);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 20; root.OffsetRight = -16; root.OffsetBottom = -16;

        var left = Ui.VBox(12);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.AddChild(_filters);
        left.AddChild(_hint);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        var gridMargin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gridMargin.AddThemeConstantOverride("margin_right", 10); gridMargin.AddThemeConstantOverride("margin_bottom", 8);
        gridMargin.AddChild(_grid);
        scroll.AddChild(gridMargin);
        left.AddChild(scroll);
        root.AddChild(left);

        var side = new PanelContainer { CustomMinimumSize = new Vector2(372, 0) };
        side.AddThemeStyleboxOverride("panel", AppTheme.Box(AppTheme.Header, 14, AppTheme.Border, 1, 0));
        var sideScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _inspector.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var im = Ui.Margin(_inspector, 20, 18);
        im.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sideScroll.AddChild(im);
        side.AddChild(sideScroll);
        root.AddChild(side);
        AddChild(root);
    }

    /// <summary>Highlights a policy at a value in the inspector (as moving its slider does).</summary>
    public void Focus(Session s, string policyId, double value)
    {
        if (s.Packs.Policies.FirstOrDefault(p => p.Id == policyId) is null) return;
        _focus = policyId; _focusValue = value;
        Inspector(s);
        foreach (var c in _cards) c.Selected = c.PolicyId == _focus;
    }

    private static int GroupOf(PolicyDef p) => WorldMap.GroupOf(p);

    private static string? Blocked(Session s) => s.Politics is { } pol
        ? (pol.Phase != Phase.Governing ? "ui.err.no_majority" : pol.PlayerRole != Role.Leader ? "ui.err.not_leader" : null)
        : null;

    public override void Refresh(Session s)
    {
        var t = s.T;
        _hint.Text = t.T("ui.client.policies_hint2");

        // Category filter.
        Ui.Clear(_filters);
        var group = new ButtonGroup();
        void Filter(int g, string text, Color color)
        {
            int count = g < 0 ? s.Packs.Policies.Count : s.Packs.Policies.Count(p => GroupOf(p) == g);
            var b = new Button
            {
                Text = $"{text}  {count}", ToggleMode = true, ButtonGroup = group, ButtonPressed = _filter == g, ThemeTypeVariation = "Ghost",
                CustomMinimumSize = new Vector2(0, 36), FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand,
            };
            if (g >= 0) b.AddThemeColorOverride("font_pressed_color", color);
            b.Pressed += () => { _filter = g; Refresh(s); };
            _filters.AddChild(b);
        }
        Filter(-1, t.T("ui.client.filter_all"), AppTheme.Accent);
        for (int g = 0; g < 3; g++) Filter(g, t.T("ui.client.group." + g), WorldMap.GroupColor(g));

        // Cards.
        Ui.Clear(_grid); _cards.Clear();
        var pending = s.Game.PendingPolicies;
        foreach (var p in s.Packs.Policies.Where(p => _filter < 0 || GroupOf(p) == _filter)
                     .OrderBy(GroupOf).ThenBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal))
        {
            var queued = pending.LastOrDefault(x => x.PolicyId == p.Id);
            var card = new PolicyCard(s, p, s.Country.Policies[p.Id], queued) { Selected = p.Id == _focus };
            card.Clicked += () => { _focus = p.Id; _focusValue = double.NaN; foreach (var c in _cards) c.Selected = c.PolicyId == _focus; Inspector(s); };
            _cards.Add(card);
            _grid.AddChild(card);
        }
        _focus ??= s.Packs.Policies.OrderBy(GroupOf).ThenBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal).FirstOrDefault()?.Id;
        foreach (var c in _cards) c.Selected = c.PolicyId == _focus;
        Inspector(s);
    }

    private void Inspector(Session s)
    {
        if (_effects.GetParent() != null) _effects.GetParent().RemoveChild(_effects);
        Ui.Clear(_inspector);
        if (_focus == null || s.Packs.Policies.FirstOrDefault(x => x.Id == _focus) is not { } p) return;
        var t = s.T;
        int g = GroupOf(p);
        var color = WorldMap.GroupColor(g);
        double now = s.Country.Policies[p.Id];
        var queued = s.Game.PendingPolicies.LastOrDefault(x => x.PolicyId == p.Id);
        string? blocked = Blocked(s);
        double start = double.IsNaN(_focusValue) ? queued?.Value ?? now : _focusValue;

        var head = Ui.HBox(12);
        head.AddChild(new IconBadge(Icons.For(p.Id), color, 48));
        var titles = Ui.VBox(2);
        titles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titles.AddChild(Ui.Caption(t.T("ui.client.group." + g), color));
        titles.AddChild(Ui.Text(t.Name(p.Id), Colors.White, AppTheme.SectionSize, true, AppTheme.Bold));
        head.AddChild(titles);
        _inspector.AddChild(head);
        if (t.Description(p.Id) is { } desc) _inspector.AddChild(Ui.Text(desc, AppTheme.Text2, 13, true));

        // Current value and the target being dialled in.
        var values = Ui.HBox(10);
        var nowBox = Ui.VBox(0);
        nowBox.AddChild(Ui.Caption(t.T("ui.client.now")));
        nowBox.AddChild(Ui.Big(Fmt.Policy(p, now, t), Colors.White, 28));
        values.AddChild(nowBox);
        var arrow = Ui.Text("→", AppTheme.Muted, 22);
        arrow.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        values.AddChild(arrow);
        var targetBox = Ui.VBox(0);
        targetBox.AddChild(Ui.Caption(t.T("ui.client.target")));
        var target = Ui.Big(Fmt.Policy(p, start, t), AppTheme.Accent, 28);
        targetBox.AddChild(target);
        values.AddChild(targetBox);
        _inspector.AddChild(values);

        double step = Math.Max((p.Max - p.Min) / 400.0, 0.05);
        var slider = new HSlider
        {
            MinValue = p.Min, MaxValue = p.Max, Step = step, Value = start, Editable = blocked == null,
            CustomMinimumSize = new Vector2(0, 28), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        var track = AppTheme.Box(new Color(AppTheme.Bg, 0.95f), 5, AppTheme.Border, 1); track.ContentMarginTop = 5; track.ContentMarginBottom = 5;
        slider.AddThemeStyleboxOverride("slider", track);
        slider.AddThemeStyleboxOverride("grabber_area", AppTheme.Box(color, 5));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", AppTheme.Box(color.Lightened(0.15f), 5));
        slider.AddThemeIconOverride("grabber", AppTheme.Grabber(color, 24));
        slider.AddThemeIconOverride("grabber_highlight", AppTheme.Grabber(color.Lightened(0.3f), 24));
        _inspector.AddChild(slider);
        var range = Ui.HBox();
        range.AddChild(Ui.Text(Fmt.Policy(p, p.Min, t), AppTheme.Muted, AppTheme.Caption));
        range.AddChild(Ui.Spacer());
        range.AddChild(Ui.Text(Fmt.Policy(p, p.Max, t), AppTheme.Muted, AppTheme.Caption));
        _inspector.AddChild(range);

        if (p.CostPerUnit != 0)
        {
            var modes = new[] { "gdp", "inflation", "none" };
            var row = Ui.HBox(10);
            var cap = Ui.Text(t.T("ui.client.indexation"), AppTheme.Text2, 14);
            cap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(cap);
            var box = new OptionButton { Disabled = blocked != null, TooltipText = t.T("ui.index.hint"), CustomMinimumSize = new Vector2(170, 36), FocusMode = FocusModeEnum.None };
            foreach (var m in modes) box.AddItem(t.T("ui.index." + m));
            string mode = s.Game.Indexation.GetValueOrDefault(p.Id, "gdp");
            box.Selected = Array.IndexOf(modes, mode);
            box.ItemSelected += i => s.Do(new SetIndexation(p.Id, modes[i]), t.T("ui.index.ok", t.Name(p.Id), t.T("ui.index.mode." + modes[i])));
            row.AddChild(box);
            _inspector.AddChild(row);
        }

        var apply = Ui.Primary(t.T("ui.client.apply"), () => s.Do(new SetPolicy(p.Id, slider.Value), t.T("ui.set.ok", t.Name(p.Id), Fmt.Policy(p, slider.Value, t))));
        apply.CustomMinimumSize = new Vector2(0, 42);
        apply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        apply.Disabled = blocked != null || Math.Abs(start - (queued?.Value ?? now)) < step / 2;
        if (blocked != null) _inspector.AddChild(Ui.Callout(Ui.Text(t.T(blocked), AppTheme.Warn, 14, true), AppTheme.Warn));
        else _inspector.AddChild(apply);
        if (queued != null)
        {
            int quarters = queued.EffectiveTurn - s.Turn + 1;
            _inspector.AddChild(Ui.Callout(Ui.Text(quarters <= 1 ? t.T("ui.pending.now", Fmt.Policy(p, queued.Value, t))
                : t.T("ui.pending.later", Fmt.Policy(p, queued.Value, t), quarters), AppTheme.Warn, 14, true), AppTheme.Warn));
        }

        _inspector.AddChild(Ui.Rule());
        _inspector.AddChild(_effects);
        _effects.Show(s, p, start);

        slider.ValueChanged += v =>
        {
            target.Text = Fmt.Policy(p, v, t);
            apply.Disabled = blocked != null || Math.Abs(v - (queued?.Value ?? now)) < step / 2;
            _focusValue = v;
            _effects.Show(s, p, v);
        };
    }
}

/// <summary>A policy in the grid: pictogram, name, value, how far the lever is turned, and a marker for a change on its way.</summary>
public partial class PolicyCard : Control
{
    private readonly string _name, _value, _pending;
    private readonly IconKind _icon;
    private readonly Color _color;
    private readonly float _frac;
    private bool _hover, _selected;
    public string PolicyId { get; }
    public event Action? Clicked;
    public bool Selected { get => _selected; set { _selected = value; QueueRedraw(); } }

    public PolicyCard(Session s, PolicyDef p, double now, PendingPolicy? queued)
    {
        var t = s.T;
        PolicyId = p.Id;
        _name = t.Name(p.Id);
        _value = Fmt.Policy(p, now, t);
        _icon = Icons.For(p.Id);
        _color = WorldMap.GroupColor(WorldMap.GroupOf(p));
        _frac = p.Max > p.Min ? (float)Math.Clamp((now - p.Min) / (p.Max - p.Min), 0, 1) : 0;
        _pending = queued != null ? "→ " + Fmt.Policy(p, queued.Value, t) : "";
        TooltipText = (t.Description(p.Id) is { } d ? _name + "\n" + d : _name);
        CustomMinimumSize = new Vector2(200, 104);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        var fill = _selected ? AppTheme.Tint(_color, 0.12f) : _hover ? AppTheme.CardHover : AppTheme.Card;
        var border = _selected ? _color : _hover ? new Color(_color, 0.5f) : AppTheme.Border;
        DrawStyleBox(AppTheme.Box(fill, 12, border, _selected ? 2 : 1), rect);

        DrawCircle(new Vector2(30, 30), 17, AppTheme.Tint(_color, 0.18f));
        Icons.Draw(this, _icon, new Vector2(30, 30), 19, _color);
        DrawMultilineString(AppTheme.Semi, new Vector2(56, 25), _name, HorizontalAlignment.Left, Size.X - 66, 14, 2, AppTheme.Text);

        DrawString(AppTheme.Bold, new Vector2(14, Size.Y - 24), _value, HorizontalAlignment.Left, Size.X - 28, 19, Colors.White);
        if (_pending.Length > 0)
        {
            float w = AppTheme.Semi.GetStringSize(_pending, HorizontalAlignment.Left, -1, AppTheme.Caption).X + 16;
            var chip = new Rect2(Size.X - 12 - w, Size.Y - 42, w, 22);
            DrawStyleBox(AppTheme.Box(AppTheme.Tint(AppTheme.Warn, 0.18f), 11, new Color(AppTheme.Warn, 0.5f), 1), chip);
            DrawString(AppTheme.Semi, new Vector2(chip.Position.X + 8, chip.Position.Y + 16), _pending, HorizontalAlignment.Left, -1, AppTheme.Caption, AppTheme.Warn);
        }
        var track = new Rect2(14, Size.Y - 13, Size.X - 28, 4);
        DrawStyleBox(AppTheme.Box(new Color(AppTheme.Bg, 0.9f), 2), track);
        if (_frac > 0) DrawStyleBox(AppTheme.Box(_color, 2), new Rect2(track.Position, new Vector2(Math.Max(track.Size.X * _frac, 4), 4)));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Clicked?.Invoke();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
    }
}
