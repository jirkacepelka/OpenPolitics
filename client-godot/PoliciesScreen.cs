using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>Every policy with a slider in real units, next to an infographic of what the highlighted move would do.</summary>
public partial class PoliciesScreen : Screen
{
    public override string TitleKey => "ui.client.tab.policies";
    public override float PreferredWidth => 0.9f;
    private readonly VBoxContainer _body;
    private readonly PolicyEffectsPanel _effects = new();
    private string? _focus;
    private double _focusValue;

    public PoliciesScreen()
    {
        var split = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        var (scroll, body) = Ui.Page();
        _body = body;
        split.AddChild(scroll);
        var side = new MarginContainer { CustomMinimumSize = new Vector2(340, 0) };
        side.AddThemeConstantOverride("margin_right", 12); side.AddThemeConstantOverride("margin_top", 12); side.AddThemeConstantOverride("margin_bottom", 12);
        side.AddChild(_effects);
        split.AddChild(side);
        AddChild(split);
    }

    /// <summary>Highlights a policy at a value in the effects panel (as moving its slider does).</summary>
    public void Focus(Session s, string policyId, double value)
    {
        if (s.Packs.Policies.FirstOrDefault(p => p.Id == policyId) is not { } p) return;
        _focus = policyId; _focusValue = value;
        _effects.Show(s, p, value);
    }

    private static int GroupOf(PolicyDef p) => p.RevenueBaseValueId != null ? 0 : p.CostPerUnit != 0 ? 1 : 2;

    public override void Refresh(Session s)
    {
        Ui.Clear(_body);
        var t = s.T;
        _body.AddChild(Ui.Heading(t.T("ui.heading.policies")));
        _body.AddChild(Ui.Text(t.T("ui.client.policies_hint"), Ui.Muted, 0, true));

        string? blocked = s.Politics is { } pol
            ? (pol.Phase != Phase.Governing ? "ui.err.no_majority" : pol.PlayerRole != Role.Leader ? "ui.err.not_leader" : null)
            : null;
        if (blocked != null) _body.AddChild(Ui.Card(Ui.Text(t.T(blocked), Ui.Warn, 0, true), AppTheme.Surface));

        var pending = s.Game.PendingPolicies;
        var current = s.Country.Policies;
        int lastGroup = -1;
        VBoxContainer? card = null;
        foreach (var p in s.Packs.Policies.OrderBy(GroupOf).ThenBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal))
        {
            int group = GroupOf(p);
            if (group != lastGroup)
            {
                _body.AddChild(Ui.Section(t.T("ui.client.group." + group)));
                card = Ui.VBox(10);
                _body.AddChild(Ui.Card(card));
                lastGroup = group;
            }
            card!.AddChild(Row(s, p, current[p.Id], pending.LastOrDefault(x => x.PolicyId == p.Id), blocked != null));
        }
        if (_focus != null && s.Packs.Policies.FirstOrDefault(p => p.Id == _focus) is { } fp) _effects.Show(s, fp, _focusValue);
        else _effects.Show(s, s.Packs.Policies.First(), s.Game.PendingPolicies.LastOrDefault(x => x.PolicyId == s.Packs.Policies.First().Id)?.Value
            ?? current[s.Packs.Policies.First().Id]);
    }

    private Control Row(Session s, PolicyDef p, double now, PendingPolicy? queued, bool blocked)
    {
        var t = s.T;
        var row = Ui.HBox(10);
        var name = Ui.Text(t.Name(p.Id));
        name.CustomMinimumSize = new Vector2(200, 0);
        name.ClipText = true;
        name.TooltipText = t.Description(p.Id) ?? "";
        row.AddChild(name);

        double step = Math.Max((p.Max - p.Min) / 400.0, 0.05);
        var slider = new HSlider
        {
            MinValue = p.Min, MaxValue = p.Max, Step = step, Value = queued?.Value ?? now,
            CustomMinimumSize = new Vector2(140, 24), Editable = !blocked, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(slider);

        var value = Ui.Text(Fmt.Policy(p, slider.Value, t), null, 16);
        value.CustomMinimumSize = new Vector2(110, 0);
        row.AddChild(value);

        var set = Ui.Btn(t.T("ui.client.set"), () => s.Do(new SetPolicy(p.Id, slider.Value), t.T("ui.set.ok", t.Name(p.Id), Fmt.Policy(p, slider.Value, t))));
        set.Disabled = true;
        slider.ValueChanged += v =>
        {
            value.Text = Fmt.Policy(p, v, t);
            set.Disabled = blocked || Math.Abs(v - (queued?.Value ?? now)) < step / 2;
            _focus = p.Id; _focusValue = v;
            _effects.Show(s, p, v);
        };
        name.MouseFilter = MouseFilterEnum.Stop;
        name.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _focus = p.Id; _focusValue = slider.Value; _effects.Show(s, p, slider.Value); }
        };

        if (p.CostPerUnit != 0)
        {
            var modes = new[] { "gdp", "inflation", "none" };
            var box = new OptionButton { Disabled = blocked, TooltipText = t.T("ui.index.hint") };
            foreach (var m in modes) box.AddItem(t.T("ui.index." + m));
            string mode = s.Game.Indexation.GetValueOrDefault(p.Id, "gdp");
            box.Selected = Array.IndexOf(modes, mode);
            box.ItemSelected += i => s.Do(new SetIndexation(p.Id, modes[i]), t.T("ui.index.ok", t.Name(p.Id), t.T("ui.index.mode." + modes[i])));
            row.AddChild(box);
        }
        else
        {
            var spacer = new Control { CustomMinimumSize = new Vector2(128, 0) };
            row.AddChild(spacer);
        }
        row.AddChild(set);

        if (queued == null) return row;
        int quarters = queued.EffectiveTurn - s.Turn + 1;
        var cell = Ui.VBox(2);
        cell.AddChild(row);
        cell.AddChild(Ui.Text(quarters <= 1 ? t.T("ui.pending.now", Fmt.Policy(p, queued.Value, t))
                                            : t.T("ui.pending.later", Fmt.Policy(p, queued.Value, t), quarters), Ui.Warn, 13));
        return cell;
    }
}
