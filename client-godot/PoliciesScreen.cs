using System.Globalization;
using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

public partial class PoliciesScreen : Screen
{
    public override string TitleKey => "ui.client.tab.policies";
    private readonly VBoxContainer _body;

    public PoliciesScreen()
    {
        var (scroll, body) = Ui.Page();
        _body = body;
        AddChild(scroll);
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
    }

    private static int GroupOf(PolicyDef p) => p.RevenueBaseValueId != null ? 0 : p.CostPerUnit != 0 ? 1 : 2;

    public override void Refresh(Session s)
    {
        Ui.Clear(_body);
        var t = s.T;
        _body.AddChild(Ui.Heading(t.T("ui.heading.policies")));
        _body.AddChild(Ui.Text(t.T("ui.client.policies_hint"), Ui.Muted, 0, true));

        string? blocked = s.Politics is { } pol
            ? (pol.Phase != OpenPolitics.Core.Politics.Phase.Governing ? "ui.err.no_majority"
               : pol.PlayerRole != OpenPolitics.Core.Politics.Role.Leader ? "ui.err.not_leader" : null)
            : null;
        if (blocked != null) _body.AddChild(Ui.Text(t.T(blocked), Ui.Warn, 0, true));

        var pending = s.Game.PendingPolicies;
        var current = s.Country.Policies;
        int lastGroup = -1;
        foreach (var p in s.Packs.Policies.OrderBy(GroupOf).ThenBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal))
        {
            int group = GroupOf(p);
            if (group != lastGroup)
            {
                _body.AddChild(Ui.Rule());
                _body.AddChild(Ui.Text(t.T("ui.client.group." + group), Ui.Accent, 17));
                lastGroup = group;
            }
            _body.AddChild(Row(s, p, current[p.Id], pending.LastOrDefault(x => x.PolicyId == p.Id), blocked != null));
        }
    }

    private Control Row(Session s, PolicyDef p, double now, PendingPolicy? queued, bool blocked)
    {
        var t = s.T;
        var row = Ui.HBox(10);
        var name = Ui.Text(t.Name(p.Id));
        name.CustomMinimumSize = new Vector2(250, 0);
        name.TooltipText = t.Description(p.Id) ?? "";
        row.AddChild(name);

        double step = Math.Max((p.Max - p.Min) / 400.0, 0.05);
        var slider = new HSlider
        {
            MinValue = p.Min, MaxValue = p.Max, Step = step, Value = queued?.Value ?? now,
            CustomMinimumSize = new Vector2(320, 24), Editable = !blocked, SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(slider);

        var value = Ui.Text(Fmt.Policy(p, slider.Value, t), null, 17);
        value.CustomMinimumSize = new Vector2(130, 0);
        row.AddChild(value);

        var set = Ui.Btn(t.T("ui.client.set"), () => s.Do(new SetPolicy(p.Id, slider.Value), t.T("ui.set.ok", t.Name(p.Id), Fmt.Policy(p, slider.Value, t))));
        set.Disabled = true;
        row.AddChild(set);
        slider.ValueChanged += v =>
        {
            value.Text = Fmt.Policy(p, v, t);
            set.Disabled = blocked || Math.Abs(v - (queued?.Value ?? now)) < step / 2;
        };

        if (queued != null)
        {
            int quarters = queued.EffectiveTurn - s.Turn + 1;
            row.AddChild(Ui.Text(quarters <= 1 ? t.T("ui.pending.now", Fmt.Policy(p, queued.Value, t))
                                               : t.T("ui.pending.later", Fmt.Policy(p, queued.Value, t), quarters), Ui.Warn));
        }
        return row;
    }
}
