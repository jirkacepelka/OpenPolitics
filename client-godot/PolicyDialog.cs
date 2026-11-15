using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>Shared pieces of the detail dialogs.</summary>
public static class Detail
{
    /// <summary>
    /// An effect row in the Democracy 4 style: pictogram, name, a long dark track with a diverging fill from the centre (green right,
    /// red left) and the signed number in brackets. <paramref name="fill"/> is -1..1.
    /// </summary>
    public static Control EffectRow(IconKind? icon, string name, double fill, string number, Action? onClick = null, string tooltip = "", bool group = false, float nameWidth = 0)
    {
        var row = Ui.HBox(12);
        row.CustomMinimumSize = new Vector2(0, 34);
        if (icon != null) row.AddChild(group ? new PictoDisc(icon.Value, 26, AppTheme.Panel, AppTheme.Ink2) : new PictoDisc(icon.Value, 26));
        else row.AddChild(Ui.Gap(24));
        var l = Ui.L(name, Tx.Body, AppTheme.Ink);
        l.CustomMinimumSize = new Vector2(nameWidth > 0 && !Layout.Compact ? nameWidth : Layout.Compact ? 120 : Layout.Wide ? 210 : 165, 0);
        l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        l.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(l);
        var bar = new Meter(Math.Clamp(fill, -1, 1), -1, 1, fill >= 0 ? AppTheme.Good : AppTheme.Bad, 60, true, 6) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(bar);
        var n = Ui.Right(Ui.L(number, Tx.Data, AppTheme.Signed(fill)));
        n.CustomMinimumSize = new Vector2(58, 0);
        n.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(n);
        row.TooltipText = tooltip.Length > 0 ? tooltip : name;
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        if (onClick == null) return row;
        var b = new Button { ThemeTypeVariation = "ButtonGhost", FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = row.TooltipText };
        b.CustomMinimumSize = new Vector2(0, 32);
        b.AddChild(row);
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        SetIgnore(row);
        b.Pressed += onClick;
        return b;
    }

    public static void SetIgnore(Control c)
    {
        c.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var ch in c.GetChildren()) if (ch is Control cc) SetIgnore(cc);
    }

    /// <summary>A labelled bar: "Label: value" above a thick rounded bar.</summary>
    public static Control LabelledBar(string label, string value, double frac, Color color)
    {
        var box = Ui.VBox(6);
        var head = Ui.HBox(8);
        head.AddChild(Ui.L(label, Tx.Small, AppTheme.Ink3));
        head.AddChild(Ui.Spacer());
        head.AddChild(Ui.L(value, Tx.Data));
        box.AddChild(head);
        box.AddChild(new Meter(frac, 0, 1, color, 120, false, 6) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return box;
    }

    /// <summary>A stat line in the right-hand block: chrome label, value right-aligned.</summary>
    public static Control Stat(string label, string value, Color? color = null)
    {
        var row = Ui.HBox(8);
        row.AddChild(Ui.L(label, Tx.Small, AppTheme.Ink3));
        row.AddChild(Ui.Spacer());
        var v = Ui.Right(Ui.L(value, Tx.Data, color ?? AppTheme.Ink));
        row.AddChild(v);
        return row;
    }

    public static Control VLine() => new ColorRect { Color = AppTheme.Hairline, CustomMinimumSize = new Vector2(1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Two columns side by side (regular) or stacked (compact).</summary>
    public static Container Columns(Control left, Control right, float leftRatio = 2f)
    {
        if (Layout.Compact)
        {
            var v = Ui.VBox(16); v.AddChild(left); v.AddChild(right); return v;
        }
        var h = Ui.HBox(32); // white space separates the columns; a hairline between them only cut through cards that overflowed
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; left.SizeFlagsStretchRatio = leftRatio;
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; right.SizeFlagsStretchRatio = 1;
        h.AddChild(left); h.AddChild(right);
        return h;
    }

    /// <summary>Rebuilds a dialog whenever the game changes, until it closes.</summary>
    public static void Live(Dialog d, Session s, Action rebuild)
    {
        void OnChanged() { if (GodotObject.IsInstanceValid(d) && d.IsInsideTree()) rebuild(); }
        s.Changed += OnChanged;
        d.TreeExiting += () => s.Changed -= OnChanged;
    }

    public static string? Blocked(Session s) => s.Politics is { } pol
        ? (pol.Phase != Phase.Governing ? "ui.err.no_majority" : pol.PlayerRole != Role.Leader ? "ui.err.not_leader" : null)
        : null;
}

/// <summary>
/// The policy dialog (Democracy 4's policy screen): description, implementation and popularity bars, a stat block, effect rows for voter
/// groups and indicators, the big slider with cost and budget figures, the political-capital box, and Apply / Cancel change / Revert
/// hanging under the dialog.
/// </summary>
public static class PolicyDialog
{
    public static Dialog Open(Main main, Session s, string id)
    {
        var p = s.Packs.Policies.First(x => x.Id == id);
        var t = s.T;
        var d = main.NewDialog(t.Name(id), Icons.For(id), 1060);
        d.Key = "policy:" + id;
        main.Map?.Select(id);
        double target = double.NaN;
        void Build()
        {
            d.ClearBody(); d.ClearActions();
            double now = s.Country.Policies.GetValueOrDefault(id, p.Default);
            var queued = s.Game.PendingPolicies.LastOrDefault(x => x.PolicyId == id);
            double latest = queued?.Value ?? now;
            if (double.IsNaN(target)) target = latest;
            string? blocked = Detail.Blocked(s);
            int g = WorldMap.GroupOf(p);

            // Top: description and bars | stat block.
            var left = Ui.VBox(14);
            if (t.Description(id) is { } desc) left.AddChild(Ui.L(desc, Tx.Body, AppTheme.Ink, true));
            var bars = Ui.HBox(24);
            int remaining = queued == null ? 0 : Math.Max(0, queued.EffectiveTurn - s.Turn + 1);
            double impl = queued == null || p.DelayTurns <= 0 ? 1 : 1 - remaining / (double)Math.Max(1, p.DelayTurns + 1);
            var delayBox = Detail.LabelledBar(t.T("ui.pd.delay", p.DelayTurns), Ui.Num(impl * 100, "0") + " %", impl, AppTheme.Chrome);
            delayBox.TooltipText = t.T("ui.pd.delay_hint");
            bars.AddChild(delayBox);
            var popLabel = Ui.L("", Tx.Data);
            var popBar = new Meter(0, 0, 1, AppTheme.Good, 120, false, 6) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var popBox = Ui.VBox(6);
            var popHead = Ui.HBox(8);
            popHead.AddChild(Ui.L(t.T("ui.pd.popularity"), Tx.Small, AppTheme.Ink3)); popHead.AddChild(Ui.Spacer()); popHead.AddChild(popLabel);
            popBox.AddChild(popHead); popBox.AddChild(popBar);
            popBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bars.AddChild(popBox);
            left.AddChild(bars);

            var right = Ui.VBox(6);
            right.AddChild(Detail.Stat(t.T("ui.pd.category"), t.T("ui.client.group." + g)));
            right.AddChild(Detail.Stat(t.T("ui.pd.in_force"), Ui.Fix(Fmt.Policy(p, now, t))));
            right.AddChild(Detail.Stat(t.T("ui.pd.time"), t.T("ui.pd.quarters", p.DelayTurns)));
            if (queued != null && Math.Abs(queued.Value - now) > 1e-9)
                right.AddChild(Detail.Stat(t.T("ui.pd.scheduled"), Ui.Fix(Fmt.Policy(p, queued.Value, t)) + " · " + t.T("ui.pd.in_q", remaining), AppTheme.WarnText));
            if (p.CostPerUnit != 0)
            {
                var modes = new[] { "gdp", "inflation", "none" };
                var row = Ui.HBox(8);
                row.AddChild(Ui.L(t.T("ui.client.indexation"), Tx.Small, AppTheme.Ink3));
                row.AddChild(Ui.Spacer());
                var box = new OptionButton { Disabled = blocked != null, TooltipText = t.T("ui.index.hint"), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 30) };
                foreach (var m in modes) box.AddItem(t.T("ui.index." + m));
                box.Selected = Array.IndexOf(modes, s.Game.Indexation.GetValueOrDefault(id, "gdp"));
                box.ItemSelected += i => s.Do(new SetIndexation(id, modes[i]), t.T("ui.index.ok", t.Name(id), t.T("ui.index.mode." + modes[i])));
                row.AddChild(box);
                right.AddChild(row);
            }
            d.Body.AddChild(Detail.Columns(left, right, 2.2f));
            if (blocked != null)
            {
                var warn = Ui.HBox(10);
                warn.AddChild(new IconView(IconKind.Alert, 18, AppTheme.WarnText));
                warn.AddChild(Ui.L(t.T(blocked), Tx.BodyStrong, AppTheme.WarnText, true));
                d.Body.AddChild(Ui.Panel(warn, "Warn"));
            }
            d.Body.AddChild(Ui.Rule());

            // Effects.
            var effects = Ui.VBox(2);
            d.Body.AddChild(effects);
            d.Body.AddChild(Ui.Rule());

            // Slider | capital box.
            var sliderCol = Ui.VBox(6);
            var slider = new PolicySlider { Editable = blocked == null, Format = v => Fmt.Policy(p, v, t) };
            if (p.Unit == "index") slider.Format = v => Ui.Num(v) + " /" + Ui.Num(p.Max, "0");
            double step = p.Unit == "electoral_method" ? 1 : Math.Max((p.Max - p.Min) / 200.0, 0.05);
            if (p.Max - p.Min >= 20) step = Math.Max(step, 0.5);
            slider.Configure(p.Min, p.Max, step, now, queued?.Value, target);
            sliderCol.AddChild(slider);
            var money = Ui.HBox(30);
            var costBox = Ui.VBox(0); var costValue = Ui.L("", Tx.Figure);
            costBox.AddChild(Ui.L(t.T("ui.pd.cost"), Tx.Small, AppTheme.Ink3)); costBox.AddChild(costValue);
            var budBox = Ui.VBox(0); var budValue = Ui.L("", Tx.Figure);
            budBox.AddChild(Ui.L(t.T("ui.pd.budget_change"), Tx.Small, AppTheme.Ink3)); budBox.AddChild(budValue);
            money.AddChild(costBox); money.AddChild(budBox);
            if (p.CostPerUnit != 0 || p.RevenueBaseValueId != null) sliderCol.AddChild(money);
            // Political capital (decision 0013): what the leader has, what the dialled change costs, the band the capital pays for.
            var capBox = Ui.HBox(12);
            capBox.AddChild(new PictoDisc(IconKind.Coin, 54));
            var capText = Ui.VBox(2);
            var costLine = Ui.L("", Tx.Figure);
            var costNote = Ui.L("", Tx.Small, AppTheme.Ink3, true);
            double capital = s.Politics?.Party?.PoliticalCapital ?? 0;
            if (s.Politics?.Party is { } ps)
            {
                capText.AddChild(Ui.L(Ui.Num(ps.PoliticalCapital, "0"), Tx.FigureXl));
                capText.AddChild(Ui.L(t.T("ui.party.capital") + " · " + Ui.Signed(ps.Income) + " " + t.T("ui.party.per_quarter"), Tx.Small, AppTheme.Ink2, true));
                if (blocked == null)
                {
                    var costRow = Ui.HBox(8);
                    costRow.AddChild(Ui.L(t.T("ui.pd.capital_cost"), Tx.Small, AppTheme.Ink3));
                    costRow.AddChild(costLine);
                    capText.AddChild(costRow);
                    capText.AddChild(costNote);
                    var hint = Ui.L(t.T("ui.pd.capital_hint", Ui.Num(p.CapitalCost, "0.##")) + " " + t.T("ui.pd.affordable_band"), Tx.Small, AppTheme.Ink3, true);
                    capText.AddChild(hint);
                }
            }
            capText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            capBox.AddChild(capText);
            if (blocked == null) slider.Affordable = s.Game.AffordableRange(id);
            Control sliderBlock = s.Politics?.Party != null ? Detail.Columns(sliderCol, capBox, 2.6f) : sliderCol;
            d.Body.AddChild(sliderBlock);
            // In small windows the slider comes before the effect list so it is never below the fold.
            if (Layout.Compact || Layout.Short) d.Body.MoveChild(sliderBlock, effects.GetIndex());

            var revert = d.AddAction(t.T("ui.pd.revert"), IconKind.Undo, () => { });
            var apply = d.AddAction(t.T("ui.pd.apply"), IconKind.Check, () =>
            {
                if (s.Do(new SetPolicy(id, slider.Target), t.T("ui.set.ok", t.Name(id), Ui.Fix(Fmt.Policy(p, slider.Target, t))))) target = double.NaN;
            }, false, "");
            var cancel = d.AddAction(t.T("ui.pd.cancel"), IconKind.Close, () =>
            {
                if (s.Do(new SetPolicy(id, now), t.T("ui.pd.cancel_ok", t.Name(id)))) target = double.NaN;
            }, blocked != null || queued == null || Math.Abs(queued.Value - now) < 1e-9);
            revert.Pressed += () => { target = latest; slider.SetTarget(latest); };
            cancel.TooltipText = t.T("ui.pd.cancel_hint");
            cancel.ThemeTypeVariation = "ButtonGhost";
            d.Actions.MoveChild(cancel, 0);
            var spacer = Ui.Spacer(); d.Actions.AddChild(spacer); d.Actions.MoveChild(spacer, 1);

            void Update(double v)
            {
                target = v;
                bool changed = Math.Abs(v - latest) > step / 2;
                double cost = s.Game.PolicyChangeCost(id, v);
                bool affordable = cost <= capital + 1e-9;
                apply.Disabled = blocked != null || !changed || !affordable;
                apply.Text = changed && cost > 0 && blocked == null ? t.T("ui.pd.apply") + " · " + Ui.Num(cost, "0") : t.T("ui.pd.apply");
                costLine.Text = changed ? t.T("ui.pd.capital_points", Ui.Num(cost, "0")) : "–";
                costLine.AddThemeColorOverride("font_color", changed && !affordable ? AppTheme.BadText : AppTheme.Ink);
                costNote.Text = !changed ? t.T("ui.pd.capital_nochange") : affordable ? "" : t.T("ui.pd.capital_short", Ui.Num(Math.Floor(capital + 1e-9), "0"));
                costNote.AddThemeColorOverride("font_color", changed && !affordable ? AppTheme.BadText : AppTheme.Ink3);
                costNote.Visible = costNote.Text.Length > 0;
                revert.Disabled = !changed;
                double pop = Ui.Popularity(s, p, v);
                popLabel.Text = Ui.Num(pop * 100, "0") + " %";
                popBar.Set(pop, AppTheme.Traffic(pop));
                double gdpN = s.Country.Values.GetValueOrDefault("base:gdp_nominal", s.Country.Values.GetValueOrDefault("base:gdp"));
                if (p.CostPerUnit != 0) { var (f, u) = Ui.Money(t, v * p.CostPerUnit / 100 * gdpN); costValue.Text = f + " " + u + " / " + t.T("ui.effects.year"); }
                else costValue.Text = "–";
                var preview = s.Game.PreviewPolicy(id, v);
                double bud = preview?.BudgetPercentOfGdp ?? 0;
                budValue.Text = Math.Abs(bud) < 1e-6 ? "–" : Ui.Signed(-bud, "0.00") + " " + t.T("ui.unit.gdp_short");
                budValue.AddThemeColorOverride("font_color", Math.Abs(bud) < 1e-6 ? AppTheme.Ink : bud > 0 ? AppTheme.BadText : AppTheme.GoodText);
                FillEffects(main, s, p, effects, preview, changed);
            }
            slider.TargetChanged += Update;
            slider.Submitted += () => { if (!apply.Disabled) apply.EmitSignal(BaseButton.SignalName.Pressed); };
            Update(target);
            d.Refit();
        }
        Build();
        Detail.Live(d, s, Build);
        return d;
    }

    private static void FillEffects(Main main, Session s, PolicyDef p, VBoxContainer box, PolicyPreview? preview, bool changed)
    {
        var t = s.T;
        Ui.Clear(box);
        var head = Ui.HBox(8);
        head.AddChild(Ui.L(t.T(changed ? "ui.pd.effects_change" : "ui.pd.effects"), Tx.Overline, AppTheme.Ink2));
        head.AddChild(Ui.Spacer());
        head.AddChild(Ui.L(t.T(changed ? "ui.pd.per_year_hint" : "ui.pd.move_hint"), Tx.Small));
        box.AddChild(head);
        var values = s.Packs.Values.ToDictionary(v => v.Id, StringComparer.Ordinal);
        if (changed && preview != null)
        {
            var groups = preview.Groups.Where(g => Math.Abs(g.Pressure) > 0.0005).OrderByDescending(g => Math.Abs(g.Pressure)).ToList();
            double gmax = Math.Max(0.05, groups.Select(g => Math.Abs(g.Pressure)).DefaultIfEmpty(0).Max());
            foreach (var g in groups)
                box.AddChild(Detail.EffectRow(Icons.For(g.GroupId), t.Name(g.GroupId), g.Pressure / gmax, "(" + Ui.Signed(g.Pressure * 100, "0") + ")", () => main.OpenDetail("group", g.GroupId), "", true));
            var rows = new List<(string Id, double Signed, double Like, string Text)>();
            foreach (var e in preview.Values)
            {
                if (!values.TryGetValue(e.ValueId, out var def)) continue;
                double signed = e.Additive / Math.Max(def.Scale, 1e-12) + e.Relative / 2.0;
                if (Math.Abs(signed) < 0.005) continue;
                double like = Math.Sign(Ui.VoterLike(s, e.ValueId));
                string text = e.Relative != 0 && e.Additive == 0 ? Ui.Signed(e.Relative, "0.00") + " %" : Ui.Signed(e.Additive, Math.Abs(e.Additive) >= 10 ? "0" : "0.00");
                rows.Add((e.ValueId, signed, like, text));
            }
            double vmax = Math.Max(0.05, rows.Select(r => Math.Abs(r.Signed)).DefaultIfEmpty(0).Max());
            foreach (var r in rows.OrderByDescending(r => Math.Abs(r.Signed)))
            {
                double fill = r.Signed / vmax * (r.Like == 0 ? 1 : r.Like);
                var row = Detail.EffectRow(Icons.For(r.Id), t.Name(r.Id), fill, r.Text, () => main.OpenDetail("value", r.Id),
                    t.Name(r.Id) + ": " + (r.Signed > 0 ? "▲" : "▼") + " " + r.Text + " / " + t.T("ui.effects.year"));
                box.AddChild(row);
            }
            if (groups.Count == 0 && rows.Count == 0) box.AddChild(Ui.L(t.T("ui.effects.budget_only"), Tx.Body, AppTheme.Ink3, true));
            return;
        }
        // No change dialled in: what the policy acts on, by direction and strength of each link.
        var edges = s.Packs.Edges.Where(e => e.From == p.Id && values.ContainsKey(e.To)).OrderByDescending(e => Math.Abs(e.Strength)).ThenBy(e => e.To, StringComparer.Ordinal).ToList();
        double emax = Math.Max(0.01, edges.Select(e => Math.Abs(e.Strength)).DefaultIfEmpty(0).Max());
        foreach (var e in edges)
        {
            double like = Math.Sign(Ui.VoterLike(s, e.To));
            double fill = e.Strength / emax * (like == 0 ? 1 : like);
            box.AddChild(Detail.EffectRow(Icons.For(e.To), t.Name(e.To), fill, e.Strength >= 0 ? "▲" : "▼", () => main.OpenDetail("value", e.To),
                t.T(e.Strength >= 0 ? "ui.pd.raises" : "ui.pd.lowers", t.Name(e.To))));
        }
        foreach (var g in s.Packs.VoterGroups.Where(g => g.PolicyWeights.ContainsKey(p.Id)).OrderByDescending(g => Math.Abs(Ui.Opinion(s, g, p))))
        {
            double op = Ui.Opinion(s, g, p);
            box.AddChild(Detail.EffectRow(Icons.For(g.Id), t.Name(g.Id), op, "(" + Ui.Signed(op * 100, "0") + ")", () => main.OpenDetail("group", g.Id), t.T("ui.pd.opinion_hint", t.Name(g.Id)), true));
        }
        if (edges.Count == 0) box.AddChild(Ui.L(t.T("ui.effects.none"), Tx.Body, AppTheme.Ink3, true));
    }
}

/// <summary>An indicator: value and trend, its chart, why it moved last quarter and what it influences.</summary>
public static class ValueDialog
{
    public static Dialog Open(Main main, Session s, string id)
    {
        var t = s.T;
        var def = s.Packs.Values.First(v => v.Id == id);
        var d = main.NewDialog(t.Name(id), Icons.For(id), 1000);
        d.Key = "value:" + id;
        main.Map?.Select(id);
        int range = 0;
        void Build()
        {
            d.ClearBody(); d.ClearActions();
            var left = Ui.VBox(12);
            var head = Ui.HBox(14);
            var (fig, unit) = Ui.Split(Fmt.Value(def, s.Country.Values.GetValueOrDefault(id)));
            head.AddChild(Ui.L(fig, Tx.FigureXl));
            if (unit.Length > 0) { var u = Ui.L(unit, Tx.Heading, AppTheme.Ink2); u.SizeFlagsVertical = SizeFlags.ShrinkEnd; head.AddChild(u); }
            double good = Ui.Goodness(s, id);
            bool cares = Math.Abs(Ui.VoterLike(s, id)) > 1e-12;
            if (cares) head.AddChild(Ui.Pill(t.T(good >= 0.6 ? "ui.vd.better" : good >= 0.4 ? "ui.vd.same" : "ui.vd.worse"), AppTheme.Traffic(good)));
            left.AddChild(head);
            if (t.Description(id) is { } desc) left.AddChild(Ui.L(desc, Tx.Body, AppTheme.Ink, true));
            var spec = Charts.All.FirstOrDefault(c => c.Series(s).Any(x => x.Key == History.ValueKey(id)));
            if (spec != null)
            {
                int tpy = s.Packs.GetScenario(s.ScenarioId).TurnsPerYear;
                left.AddChild(Ui.Segmented(new[] { t.T("ui.chart.range.1y"), t.T("ui.chart.range.3y"), t.T("ui.chart.range.all") }, range == tpy ? 0 : range == 3 * tpy ? 1 : 2,
                    i => { range = i == 0 ? tpy : i == 1 ? 3 * tpy : 0; Build(); }, 28));
                var chart = new Chart { CustomMinimumSize = new Vector2(280, 200), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var (turns, series) = Charts.Build(s, spec, range);
                chart.Set(spec.Title(t), turns, series.Where(x => x.Name == t.Name(id) || series.Count == 1).DefaultIfEmpty(series.FirstOrDefault()).Where(x => x != null).Select(x => x!).ToList(), spec.Format, s.ShortDateAt);
                left.AddChild(chart);
            }
            var right = Ui.VBox(4);
            right.AddChild(Ui.L(t.T("ui.heading.why", t.Name(id)), Tx.Overline, AppTheme.Ink2));
            var change = s.Last?.Changes.FirstOrDefault(c => c.ValueId == id);
            if (change == null) right.AddChild(Ui.L(t.T("ui.why.none"), Tx.Body, AppTheme.Ink3, true));
            else
            {
                right.AddChild(Ui.L(Ui.Fix(t.T("ui.info.change", Fmt.Value(def, change.Before), Fmt.Value(def, change.After))), Tx.Body, AppTheme.Ink2, true));
                var lines = change.Contributions.Where(c => Math.Abs(c.Amount) > 1e-9 * Math.Max(1.0, Math.Abs(change.Before)))
                    .OrderByDescending(c => Math.Abs(c.Amount)).ThenBy(c => c.EdgeId, StringComparer.Ordinal).Take(8).ToList();
                double max = Math.Max(1e-12, lines.Select(c => Math.Abs(c.Amount)).DefaultIfEmpty(0).Max());
                double like = Math.Sign(Ui.VoterLike(s, id));
                foreach (var c in lines)
                {
                    string from = c.FromId;
                    right.AddChild(Detail.EffectRow(Icons.For(from), t.Name(from), c.Amount / max * (like == 0 ? 1 : like), c.Amount > 0 ? "▲" : "▼",
                        () => main.OpenDetail(s.Packs.Policies.Any(p => p.Id == from) ? "policy" : "value", from), t.T("ui.why.line", t.Name(from), t.T(c.Amount > 0 ? "ui.why.up" : "ui.why.down"))));
                }
                if (def.Owner != null) right.AddChild(Ui.L(t.T("ui.why.model"), Tx.Small, AppTheme.Ink3, true));
            }
            right.AddChild(Ui.Gap(8));
            var outs = s.Packs.Edges.Where(e => e.From == id && e.To != id).OrderByDescending(e => Math.Abs(e.Strength)).ToList();
            if (outs.Count > 0)
            {
                right.AddChild(Ui.L(t.T("ui.client.graph.influences").TrimEnd(':'), Tx.Overline, AppTheme.Ink2));
                double emax = Math.Max(0.01, outs.Max(e => Math.Abs(e.Strength)));
                foreach (var e in outs.Take(8))
                {
                    string to = e.To;
                    right.AddChild(Detail.EffectRow(Icons.For(to), t.Name(to), e.Strength / emax, e.Strength >= 0 ? "▲" : "▼",
                        () => main.OpenDetail(s.Packs.Policies.Any(p => p.Id == to) ? "policy" : "value", to)));
                }
            }
            d.Body.AddChild(Detail.Columns(left, right, 1.1f));
            if (spec != null) d.AddAction(t.T("ui.map.open_chart"), IconKind.Chart, () => main.OpenChart(spec));
            d.Refit();
        }
        Build();
        Detail.Live(d, s, Build);
        return d;
    }
}

/// <summary>A voter group: size, mood and its trend, what it likes and dislikes.</summary>
public static class GroupDialog
{
    public static Dialog Open(Main main, Session s, string id)
    {
        var t = s.T;
        var g = s.Packs.VoterGroups.First(x => x.Id == id);
        var d = main.NewDialog(t.Name(id), Icons.For(id), 960);
        d.Key = "group:" + id;
        void Build()
        {
            d.ClearBody(); d.ClearActions();
            double sat = s.Country.Satisfaction.GetValueOrDefault(id);
            var left = Ui.VBox(12);
            var head = Ui.HBox(14);
            head.AddChild(Ui.L(Ui.Signed(sat, "0.00"), Tx.FigureXl, AppTheme.TrafficText((sat + 1) / 2)));
            head.AddChild(Ui.Pill(t.T(Moods.Key(sat)), AppTheme.Traffic((sat + 1) / 2)));
            left.AddChild(head);
            left.AddChild(Detail.LabelledBar(t.T("ui.gd.share"), Ui.Num(g.Size * 100, "0") + " %", g.Size, AppTheme.Chrome));
            if (t.Description(id) is { } desc) left.AddChild(Ui.L(desc, Tx.Body, AppTheme.Ink, true));
            var chart = new Chart { CustomMinimumSize = new Vector2(260, 180), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var (turns, vals) = s.History.Window(History.GroupKey(id), 0);
            chart.Set(t.Name(id), turns, new[] { new ChartSeries(t.Name(id), vals, AppTheme.Ink) }, "0.00", s.ShortDateAt);
            left.AddChild(chart);

            var right = Ui.VBox(4);
            right.AddChild(Ui.L(t.T("ui.gd.policies"), Tx.Overline, AppTheme.Ink2));
            foreach (var p in s.Packs.Policies.Where(p => g.PolicyWeights.ContainsKey(p.Id)).OrderByDescending(p => Ui.Opinion(s, g, p)))
            {
                double op = Ui.Opinion(s, g, p);
                string pid = p.Id;
                right.AddChild(Detail.EffectRow(Icons.For(pid), t.Name(pid), op, "(" + Ui.Signed(op * 100, "0") + ")", () => main.OpenDetail("policy", pid)));
            }
            var cares = g.ValueWeights.Keys.Concat(g.LevelWeights?.Keys ?? Enumerable.Empty<string>()).Distinct().Where(v => s.Packs.Values.Any(x => x.Id == v)).ToList();
            if (cares.Count > 0)
            {
                right.AddChild(Ui.Gap(8));
                right.AddChild(Ui.L(t.T("ui.gd.values"), Tx.Overline, AppTheme.Ink2));
                foreach (var v in cares.OrderBy(v => v, StringComparer.Ordinal))
                {
                    double like = g.ValueWeights.GetValueOrDefault(v) + (g.LevelWeights?.GetValueOrDefault(v) ?? 0);
                    double good = Ui.Goodness(s, v);
                    if (Math.Sign(like) != Math.Sign(Ui.VoterLike(s, v))) good = 1 - good;
                    string vid = v;
                    right.AddChild(Detail.EffectRow(Icons.For(v), t.Name(v), (good - 0.5) * 2, like > 0 ? "▲" : "▼", () => main.OpenDetail("value", vid),
                        t.T(like > 0 ? "ui.gd.wants_up" : "ui.gd.wants_down", t.Name(v))));
                }
            }
            d.Body.AddChild(Detail.Columns(left, right, 1f));
            d.AddAction(t.T(main.Map?.LensGroup == id ? "ui.gd.lens_off" : "ui.gd.lens_on"), IconKind.Eye, () => { main.SetLens(main.Map?.LensGroup == id ? null : id); d.Close(); });
            d.Refit();
        }
        Build();
        Detail.Live(d, s, Build);
        return d;
    }
}

/// <summary>A party: seats, poll and its trend, position, relation and loyalty, demands.</summary>
public static class PartyDialog
{
    public static Dialog Open(Main main, Session s, string id)
    {
        var t = s.T;
        var d = main.NewDialog(t.Name(id), IconKind.Party, 900);
        d.Key = "party:" + id;
        void Build()
        {
            d.ClearBody(); d.ClearActions();
            var pol = s.Politics;
            if (pol == null) return;
            var left = Ui.VBox(10);
            int total = Math.Max(1, pol.Seats.Values.Sum());
            left.AddChild(Ui.PartyName(s, id, t.Name(id) + (id == pol.PlayerParty ? "  (" + t.T("ui.politics.you") + ")" : ""), Tx.Heading));
            left.AddChild(Detail.Stat(t.T("ui.party.seats"), $"{pol.Seats.GetValueOrDefault(id)} / {total}"));
            left.AddChild(Detail.Stat(t.T("ui.party.poll"), Ui.Num(pol.Support.GetValueOrDefault(id) * 100) + " %"));
            if (pol.GovernmentParties.Contains(id)) left.AddChild(Detail.Stat(t.T("ui.politics.government"), "✓"));
            if (pol.Loyalty?.TryGetValue(id, out var loy) == true) left.AddChild(Detail.LabelledBar(t.T("ui.chart.loyalty"), Ui.Num(loy, "0"), loy / 100, AppTheme.Traffic(loy / 100)));
            if (pol.Party?.Relations.TryGetValue(id, out var rel) == true) left.AddChild(Detail.LabelledBar(t.T("ui.party.relations"), Ui.Num(rel, "0.00"), rel, AppTheme.Chrome));
            if (s.Game.Compass.TryGetValue(id, out var c))
                left.AddChild(Detail.Stat(t.T("ui.client.compass"), $"{Ui.Num(c.X, "0.00")} / {Ui.Num(c.Y, "0.00")}"));
            var chart = new Chart { CustomMinimumSize = new Vector2(260, 170), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var (turns, vals) = s.History.Window(History.PollKey(id), 0);
            chart.Set(t.Name(id), turns, new[] { new ChartSeries(t.Name(id), vals.Select(v => v * 100).ToList(), Ui.PartyColor(s, id)) }, "0.0", s.ShortDateAt);
            left.AddChild(chart);
            var right = Ui.VBox(8);
            right.AddChild(Ui.L(t.T("ui.party.demands"), Tx.Overline, AppTheme.Ink2));
            var party = s.Packs.Parties.First(x => x.Id == id);
            foreach (var dm in party.Demands) right.AddChild(Ui.L("• " + Ui.Fix(s.Texts.Demand(dm)), Tx.Body, AppTheme.Ink, true));
            if (party.Demands.Count == 0) right.AddChild(Ui.L("–", Tx.Body));
            d.Body.AddChild(Detail.Columns(left, right, 1.2f));
            if (id != pol.PlayerParty && pol.Party != null)
                d.AddAction(t.T("ui.client.court") + " · 3", IconKind.Hand, () => s.Do(new Court(id), t.T("ui.party.court_ok", t.Name(id))));
            d.Refit();
        }
        Build();
        Detail.Live(d, s, Build);
        return d;
    }
}
