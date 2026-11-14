using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The infographic of a policy move, in the spirit of Democracy 4: arrows on the values it pushes (more arrows = stronger), the voter groups
/// it pleases or annoys, and what it does to the budget. First-order and without delays: the point is direction and size.
/// </summary>
public partial class PolicyEffectsPanel : PanelContainer
{
    private readonly VBoxContainer _body = Ui.VBox(6);

    public PolicyEffectsPanel()
    {
        CustomMinimumSize = new Vector2(320, 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(_body);
    }

    public static string Arrows(double strength, bool up)
    {
        string a = up ? "▲" : "▼";
        return strength < 0.1 ? a : strength < 0.3 ? a + a : a + a + a;
    }

    /// <summary>Whether voters like this value going up (positive), down (negative) or are indifferent (0), summed over the groups that care.</summary>
    private static double VoterLike(Session s, string valueId) =>
        s.Packs.VoterGroups.Sum(g => g.Size * g.Weight * g.ValueWeights.GetValueOrDefault(valueId));

    public void Show(Session s, PolicyDef policy, double target)
    {
        var t = s.T;
        Ui.Clear(_body);
        _body.AddChild(Ui.Section(t.T("ui.effects.title")));
        _body.AddChild(Ui.Text(t.Name(policy.Id), Colors.White, 18));
        var preview = s.Game.PreviewPolicy(policy.Id, target);
        if (preview == null) return;
        _body.AddChild(Ui.Text($"{Fmt.Policy(policy, preview.From, t)}  →  {Fmt.Policy(policy, preview.To, t)}", Ui.Muted));
        if (Math.Abs(preview.To - preview.From) < 1e-9)
        {
            _body.AddChild(Ui.Text(t.T("ui.effects.none"), Ui.Muted, 14, true));
            return;
        }

        if (Math.Abs(preview.BudgetPercentOfGdp) > 1e-9)
        {
            bool wider = preview.BudgetPercentOfGdp > 0;
            _body.AddChild(Ui.Card(Ui.Text($"{t.T(wider ? "ui.effects.budget_worse" : "ui.effects.budget_better")}  {Fmt.Signed(preview.BudgetPercentOfGdp, "0.0")} {t.T("ui.unit.gdp_share")}",
                wider ? Ui.Bad : Ui.Good, 15, true), AppTheme.Surface));
        }

        var values = s.Packs.Values.ToDictionary(v => v.Id);
        var rows = new List<(string Id, double Strength, bool Up, string Text, Color Color)>();
        foreach (var e in preview.Values)
        {
            if (!values.TryGetValue(e.ValueId, out var def)) continue;
            double strength = Math.Abs(e.Additive) / Math.Max(def.Scale, 1e-12) + Math.Abs(e.Relative) / 2.0;
            double signed = e.Additive / Math.Max(def.Scale, 1e-12) + e.Relative / 2.0;
            if (strength < 0.02) continue;
            double like = VoterLike(s, e.ValueId) * Math.Sign(signed);
            string text = e.Relative != 0 && e.Additive == 0
                ? $"{Fmt.Signed(e.Relative, "0.00")} % / {t.T("ui.effects.year")}"
                : $"{Fmt.Signed(e.Additive, Math.Abs(e.Additive) >= 10 ? "0" : "0.00")} / {t.T("ui.effects.year")}";
            rows.Add((e.ValueId, strength, signed > 0, text, like > 0 ? Ui.Good : like < 0 ? Ui.Bad : Ui.Muted));
        }
        if (rows.Count > 0)
        {
            _body.AddChild(Ui.Text(t.T("ui.effects.values"), Ui.Accent, 15));
            foreach (var r in rows.OrderByDescending(r => r.Strength))
            {
                var row = Ui.HBox(8);
                var arrows = Ui.Text(Arrows(r.Strength, r.Up), r.Color, 15);
                arrows.CustomMinimumSize = new Vector2(48, 0);
                row.AddChild(arrows);
                var name = Ui.Text(t.Name(r.Id), null, 15);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(name);
                row.AddChild(Ui.Text(r.Text, Ui.Muted, 13));
                _body.AddChild(row);
            }
        }

        var groups = preview.Groups.Where(g => Math.Abs(g.Pressure) > 0.002).OrderByDescending(g => Math.Abs(g.Pressure)).ToList();
        if (groups.Count > 0)
        {
            _body.AddChild(Ui.Text(t.T("ui.effects.groups"), Ui.Accent, 15));
            foreach (var g in groups)
            {
                var row = Ui.HBox(8);
                var arrows = Ui.Text(Arrows(Math.Abs(g.Pressure) * 5, g.Pressure > 0), g.Pressure > 0 ? Ui.Good : Ui.Bad, 15);
                arrows.CustomMinimumSize = new Vector2(48, 0);
                row.AddChild(arrows);
                row.AddChild(Ui.Text(t.Name(g.GroupId), null, 15));
                _body.AddChild(row);
            }
        }
        if (rows.Count == 0 && groups.Count == 0)
            _body.AddChild(Ui.Text(t.T("ui.effects.budget_only"), Ui.Muted, 14, true));
        _body.AddChild(Ui.Text(t.T("ui.effects.hint"), Ui.Muted, 12, true));
    }
}
