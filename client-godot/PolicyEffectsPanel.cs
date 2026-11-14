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
    private readonly VBoxContainer _body = Ui.VBox(10);

    /// <summary>Whether to repeat "from → to" (the policy inspector already shows it).</summary>
    public bool ShowRange { get; set; } = true;

    public PolicyEffectsPanel()
    {
        CustomMinimumSize = new Vector2(300, 0);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(_body);
    }

    /// <summary>A row of the infographic: arrows in a tinted chip, the pictogram, the name and the amount.</summary>
    private static Control EffectRow(string id, string name, string arrows, Color color, string? amount)
    {
        var row = Ui.HBox(10);
        var chip = new PanelContainer { CustomMinimumSize = new Vector2(52, 26) };
        var box = AppTheme.Box(AppTheme.Tint(color, 0.16f), 13); box.ContentMarginLeft = 6; box.ContentMarginRight = 6;
        chip.AddThemeStyleboxOverride("panel", box);
        var a = Ui.Text(arrows, color, 13, false, AppTheme.Bold);
        a.HorizontalAlignment = HorizontalAlignment.Center; a.VerticalAlignment = VerticalAlignment.Center;
        chip.AddChild(a);
        chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(chip);
        row.AddChild(new IconView(Icons.For(id), 18, AppTheme.Text2));
        var n = Ui.Text(name, AppTheme.Text, 14, true);
        row.AddChild(n);
        if (amount != null) { var am = Ui.Text(amount, AppTheme.Muted, AppTheme.Caption); am.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(am); }
        return row;
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
        _body.AddChild(Ui.Text(t.T("ui.effects.title"), Colors.White, AppTheme.Subhead, false, AppTheme.Semi));
        var preview = s.Game.PreviewPolicy(policy.Id, target);
        if (preview == null) return;
        if (ShowRange) _body.AddChild(Ui.Text($"{Fmt.Policy(policy, preview.From, t)}  →  {Fmt.Policy(policy, preview.To, t)}", AppTheme.Text2, 14));
        if (Math.Abs(preview.To - preview.From) < 1e-9)
        {
            _body.AddChild(Ui.Text(t.T("ui.effects.none"), Ui.Muted, 14, true));
            return;
        }

        if (Math.Abs(preview.BudgetPercentOfGdp) > 1e-9)
        {
            bool wider = preview.BudgetPercentOfGdp > 0;
            var color = wider ? Ui.Bad : Ui.Good;
            var row = Ui.HBox(10);
            row.AddChild(new IconView(wider ? IconKind.CoinOut : IconKind.CoinIn, 20, color));
            row.AddChild(Ui.Text($"{t.T(wider ? "ui.effects.budget_worse" : "ui.effects.budget_better")}  {Fmt.Signed(preview.BudgetPercentOfGdp, "0.0")} {t.T("ui.unit.gdp_share")}", color, 14, true));
            _body.AddChild(Ui.Callout(row, color));
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
            _body.AddChild(Ui.Caption(t.T("ui.effects.values")));
            foreach (var r in rows.OrderByDescending(r => r.Strength))
                _body.AddChild(EffectRow(r.Id, t.Name(r.Id), Arrows(r.Strength, r.Up), r.Color, r.Text));
        }

        var groups = preview.Groups.Where(g => Math.Abs(g.Pressure) > 0.002).OrderByDescending(g => Math.Abs(g.Pressure)).ToList();
        if (groups.Count > 0)
        {
            _body.AddChild(Ui.Gap(2));
            _body.AddChild(Ui.Caption(t.T("ui.effects.groups")));
            foreach (var g in groups)
                _body.AddChild(EffectRow(g.GroupId, t.Name(g.GroupId), Arrows(Math.Abs(g.Pressure) * 5, g.Pressure > 0), g.Pressure > 0 ? Ui.Good : Ui.Bad, null));
        }
        if (rows.Count == 0 && groups.Count == 0)
            _body.AddChild(Ui.Text(t.T("ui.effects.budget_only"), Ui.Muted, 14, true));
        _body.AddChild(Ui.Text(t.T("ui.effects.hint"), Ui.Muted, AppTheme.Caption, true));
    }
}
