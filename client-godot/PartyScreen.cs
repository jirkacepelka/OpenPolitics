using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

using static Godot.Control;

namespace OpenPolitics.Client;

/// <summary>The player's own party, in tabs: overview, platform (position and priority per policy), promises, relations, actions.</summary>
public sealed class PartyScreen : Screen
{
    public override string TitleKey => "ui.client.tab.party";
    public override IconKind Icon => IconKind.Party;
    public override string TabId => "party";
    public override float Width => 1100;

    private int _tab, _groupIndex, _effort = 1, _valueIndex, _proposeIndex;
    private double _proposeTarget = double.NaN;
    private readonly Dictionary<string, double> _platformTargets = new(StringComparer.Ordinal);

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        var pol = s.Politics;
        if (pol?.Party is not { } ps) { d.Body.AddChild(Ui.L(t.T("ui.err.no_politics"), Tx.Body)); return; }
        d.Title = t.Name(pol.PlayerParty);
        var tabs = Ui.HBox(0);
        var group = new ButtonGroup();
        string[] keys = { "ui.pt.overview", "ui.pt.platform", "ui.pt.promises", "ui.pt.relations", "ui.pt.actions" };
        for (int i = 0; i < keys.Length; i++)
        {
            int k = i;
            var b = new Button { Text = t.T(keys[i]), ToggleMode = true, ButtonGroup = group, ButtonPressed = _tab == i, ThemeTypeVariation = "ButtonTab", FocusMode = Control.FocusModeEnum.None,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 42) };
            b.Pressed += () => { _tab = k; main.Show(this); };
            tabs.AddChild(b);
        }
        var tabScroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 46) };
        tabScroll.AddChild(Ui.Margin(tabs, 18, 0));
        d.Fixed.AddChild(tabScroll);
        d.Fixed.AddChild(new HairLine { CustomMinimumSize = new Vector2(0, 1) });
        d.Body.AddChild(Ui.Gap(4));
        switch (_tab)
        {
            case 0: Overview(s, pol, ps, d.Body); break;
            case 1: Platform(s, ps, d.Body); break;
            case 2: Promises(s, pol, ps, d.Body); break;
            case 3: Relations(s, pol, ps, d.Body, main); break;
            default: Actions(s, pol, ps, d.Body); Measures(s, pol, ps, d.Body); break;
        }
    }

    private static void Overview(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        var left = Ui.VBox(12);
        var role = Ui.HBox(8);
        role.AddChild(Ui.PartyName(s, pol.PlayerParty, null, Tx.Heading));
        role.AddChild(Ui.Pill(t.T("ui.role." + pol.PlayerRole.ToString().ToLowerInvariant()), AppTheme.Chrome));
        left.AddChild(role);
        var tiles = new FlowGrid { MinColumn = 150, MaxColumns = 4, Gap = 10 };
        void Tile(string label, string value, string sub, IconKind icon, Color? color = null)
        {
            var box = Ui.VBox(2);
            var head = Ui.HBox(6); head.AddChild(new IconView(icon, 16, AppTheme.Chrome)); head.AddChild(Ui.L(label, Tx.Overline, AppTheme.Chrome));
            box.AddChild(head);
            box.AddChild(Ui.L(value, Tx.FigureXl, color ?? AppTheme.Ink));
            if (sub.Length > 0) box.AddChild(Ui.L(sub, Tx.Small));
            tiles.AddChild(Ui.Panel(box, "Inset"));
        }
        Tile(t.T("ui.party.capital"), Ui.Num(ps.PoliticalCapital), Ui.Signed(ps.Income) + " " + t.T("ui.party.per_quarter"), IconKind.Coin);
        Tile(t.T("ui.party.organisation"), Ui.Num(ps.Organization * 100, "0") + " %", "", IconKind.Gear);
        Tile(t.T("ui.party.credibility"), Ui.Num(ps.Credibility * 100, "0") + " %", "", IconKind.Shield);
        Tile(t.T("ui.party.cohesion"), Ui.Num(ps.Cohesion * 100, "0") + " %", "", IconKind.Users);
        left.AddChild(tiles);
        left.AddChild(Ui.Section(t.T("ui.party.goals")));
        double share = pol.Support.GetValueOrDefault(pol.PlayerParty);
        left.AddChild(Detail.LabelledBar(t.T("ui.goal.poll"), Ui.Num(share * 100) + " %", share / 0.4, AppTheme.Chrome));
        double fragility = pol.Loyalty is { Count: > 0 } l ? 100 - l.Values.Min() : 0;
        left.AddChild(Detail.LabelledBar(t.T("ui.goal.fragility"), pol.Loyalty is { Count: > 0 } ? Ui.Num(fragility, "0") + " %" : "–", fragility / 100, AppTheme.Bad));
        double rel = ps.Relations.Values.DefaultIfEmpty(0).Average();
        left.AddChild(Detail.LabelledBar(t.T("ui.goal.relations"), Ui.Num(rel, "0.00"), rel, AppTheme.Good));
        var compass = new CompassView { CustomMinimumSize = new Vector2(240, 260), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        compass.Set(s, pol.PlayerParty);
        var right = Ui.VBox(8);
        right.AddChild(Ui.L(t.T("ui.client.compass"), Tx.Overline, AppTheme.Chrome));
        right.AddChild(compass);
        body.AddChild(Detail.Columns(left, right, 1.5f));
    }

    private void Platform(Session s, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        body.AddChild(Ui.L(t.T("ui.client.platform_hint2") + " " + t.T("ui.client.platform_hint"), Tx.Body, AppTheme.Ink2, true));
        foreach (var grp in ps.Platform.Keys.Select(k => s.Packs.Policies.FirstOrDefault(p => p.Id == k)).Where(p => p != null).Select(p => p!)
                     .GroupBy(WorldMap.GroupOf).OrderBy(g => g.Key))
        {
            body.AddChild(Ui.Section(t.T("ui.client.group." + grp.Key)));
            foreach (var def in grp.OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal))
            {
                string id = def.Id;
                var row = Ui.HBox(10);
                row.AddChild(new PictoDisc(Icons.For(id), 26, AppTheme.Chrome, Colors.White));
                var name = Ui.L(t.Name(id), Tx.BodyStrong);
                name.CustomMinimumSize = new Vector2(Layout.Compact ? 120 : 190, 0);
                name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; name.TooltipText = t.Name(id);
                name.MouseFilter = Control.MouseFilterEnum.Pass;
                row.AddChild(name);
                double target = _platformTargets.GetValueOrDefault(id, ps.Platform[id]);
                var slider = new PolicySlider(compact: true) { GovMarker = s.Country.Policies.GetValueOrDefault(id, def.Default), TooltipText = t.T("ui.pt.gov_marker") };
                slider.Configure(def.Min, def.Max, Math.Max((def.Max - def.Min) / 200.0, 0.05), ps.Platform[id], null, target);
                var label = Ui.Right(Ui.L(Ui.Fix(Fmt.Policy(def, target, t)), Tx.Data));
                label.CustomMinimumSize = new Vector2(92, 0);
                slider.TargetChanged += v => { _platformTargets[id] = v; label.Text = Ui.Fix(Fmt.Policy(def, v, t)); };
                row.AddChild(slider);
                row.AddChild(label);
                row.AddChild(Ui.Costed(t.T("ui.client.shift"), 1, () => { if (s.Do(new ShiftPlatform(id, slider.Target), t.T("ui.party.platform_ok", t.Name(id)))) _platformTargets.Remove(id); }, null, false, "ButtonSecondary"));
                // priority stepper
                double weight = ps.Priorities?.GetValueOrDefault(id, 1.0) ?? 1.0;
                var prio = Ui.HBox(0);
                prio.TooltipText = t.T("ui.client.priority_hint");
                prio.AddChild(Ui.Btn("", () => s.Do(new SetPriority(id, Math.Max(0, weight - 0.2)), t.T("ui.party.priority_ok", t.Name(id))), "ButtonSecondary", IconKind.Minus, t.T("ui.party.priority") + " −", weight <= 0, 30));
                var pv = Ui.L(Ui.Num(weight, "0.0"), Tx.Data);
                pv.HorizontalAlignment = HorizontalAlignment.Center; pv.CustomMinimumSize = new Vector2(38, 0);
                prio.AddChild(pv);
                prio.AddChild(Ui.Btn("", () => s.Do(new SetPriority(id, Math.Min(1, weight + 0.2)), t.T("ui.party.priority_ok", t.Name(id))), "ButtonSecondary", IconKind.Plus, t.T("ui.party.priority") + " +", weight >= 1, 30));
                row.AddChild(prio);
                body.AddChild(row);
            }
        }
    }

    private static void Promises(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        bool soon = pol.TurnsToElection <= PoliticsLimits.PromiseWindowTurns && pol.Phase != Phase.Formation;
        body.AddChild(Ui.Panel(Ui.L(t.T(soon ? "ui.client.promises_hint_open" : "ui.client.promises_hint_closed"), Tx.BodyStrong, soon ? AppTheme.WarnText : AppTheme.Ink2, true), soon ? "Warn" : "Inset"));
        var demands = s.Packs.Parties.FirstOrDefault(p => p.Id == pol.PlayerParty)?.Demands;
        if (demands == null || demands.Count == 0) return;
        foreach (var dm in demands)
        {
            var row = Ui.HBox(12);
            row.AddChild(Ui.L(Ui.Fix(s.Texts.Demand(dm)), Tx.Body, AppTheme.Ink, true));
            bool made = ps.Promises?.Contains(dm.Id) == true;
            if (made) row.AddChild(Ui.Pill(t.T("ui.party.promised"), AppTheme.Good));
            else row.AddChild(Ui.Costed(t.T("ui.client.promise"), 2, () => s.Do(new MakePromise(dm.Id), t.T("ui.party.promise_ok", s.Texts.Demand(dm))), soon ? null : t.T("ui.err.too_early"), !soon));
            body.AddChild(row);
            body.AddChild(Ui.Rule());
        }
    }

    private static void Relations(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body, Main main)
    {
        var t = s.T;
        foreach (var party in s.Packs.Parties.Where(p => p.Id != pol.PlayerParty).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            double r = ps.Relations.GetValueOrDefault(party.Id);
            var row = Ui.HBox(12);
            var name = Ui.PartyName(s, party.Id, t.Name(party.Id) + (pol.GovernmentParties.Contains(party.Id) ? "  ★" : ""), Tx.BodyStrong);
            name.CustomMinimumSize = new Vector2(Layout.Compact ? 150 : 230, 0);
            row.AddChild(name);
            row.AddChild(new Meter(r, 0, 1, Ui.PartyColor(s, party.Id), 80, false, 12) { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var v = Ui.Right(Ui.L(Ui.Num(r, "0.00"), Tx.Data));
            v.CustomMinimumSize = new Vector2(44, 0);
            row.AddChild(v);
            string pid = party.Id;
            row.AddChild(Ui.Btn("", () => main.OpenDetail("party", pid), "ButtonGhost", IconKind.Search, t.T("ui.map.details"), false, 32));
            row.AddChild(Ui.Costed(t.T("ui.client.court"), 3, () => s.Do(new Court(pid), t.T("ui.party.court_ok", t.Name(pid))), null, false, "ButtonSecondary"));
            if (pol.PlayerRole == Role.Leader && pol.GovernmentParties.Contains(party.Id))
                row.AddChild(Ui.Costed(t.T("ui.client.dismiss"), 4, () => s.Do(new DismissPartner(pid), t.T("ui.exit.dismiss_ok", t.Name(pid))), null, false, "ButtonDanger"));
            body.AddChild(row);
        }
    }

    private static Control ActionCard(IconKind icon, string title, Control controls, string? hint = null)
    {
        var box = Ui.VBox(8);
        var head = Ui.HBox(10);
        head.AddChild(new PictoDisc(icon, 34, AppTheme.Chrome, Colors.White));
        head.AddChild(Ui.L(title, Tx.Heading));
        box.AddChild(head);
        box.AddChild(controls);
        if (hint != null) box.AddChild(Ui.L(hint, Tx.Small, AppTheme.Ink3, true));
        return Ui.Panel(box, "Card");
    }

    private void Actions(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        bool leader = pol.PlayerRole == Role.Leader, partner = pol.PlayerRole == Role.Partner, opposition = pol.PlayerRole == Role.Opposition;
        var grid = new FlowGrid { MinColumn = 320, MaxColumns = 3, Gap = 12 };

        var groups = s.Packs.VoterGroups.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        var campaign = Ui.VBox(8);
        var groupBox = new OptionButton { FocusMode = Control.FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var g in groups) groupBox.AddItem(t.Name(g.Id));
        groupBox.Selected = Math.Clamp(_groupIndex, 0, Math.Max(0, groups.Count - 1));
        groupBox.ItemSelected += i => _groupIndex = (int)i;
        campaign.AddChild(groupBox);
        var effortRow = Ui.HBox(8);
        effortRow.AddChild(Ui.L(t.T("ui.client.effort"), Tx.Label));
        effortRow.AddChild(Ui.Segmented(new[] { "1 · 3", "2 · 6", "3 · 9" }, _effort - 1, i => _effort = i + 1));
        campaign.AddChild(effortRow);
        campaign.AddChild(Ui.Btn(t.T("ui.client.go"), () => s.Do(new Campaign(groups[_groupIndex].Id, _effort), t.T("ui.party.campaign_ok", t.Name(groups[_groupIndex].Id))), "", IconKind.Check));
        grid.AddChild(ActionCard(IconKind.Voters, t.T("ui.client.campaign"), campaign));

        grid.AddChild(ActionCard(IconKind.Gear, t.T("ui.client.organize"), Ui.Costed(t.T("ui.client.go"), 4, () => s.Do(new BuildOrganization(), t.T("ui.party.organized")))));

        var values = s.Packs.Values.OrderBy(v => Localizer.Fold(t.Name(v.Id)), StringComparer.Ordinal).ToList();
        var crit = Ui.VBox(8);
        var valueBox = new OptionButton { Disabled = leader, FocusMode = Control.FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var v in values) valueBox.AddItem(t.Name(v.Id));
        valueBox.Selected = Math.Clamp(_valueIndex, 0, Math.Max(0, values.Count - 1));
        valueBox.ItemSelected += i => _valueIndex = (int)i;
        crit.AddChild(valueBox);
        crit.AddChild(Ui.Costed(t.T("ui.client.go"), 2, () => s.Do(new Criticize(values[_valueIndex].Id), t.T("ui.party.criticized", t.Name(values[_valueIndex].Id))), leader ? t.T("ui.err.not_opposition") : null, leader));
        grid.AddChild(ActionCard(IconKind.Alert, t.T("ui.client.criticize"), crit));

        var allPolicies = s.Packs.Policies.OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal).ToList();
        if (allPolicies.Count > 0)
        {
            _proposeIndex = Math.Clamp(_proposeIndex, 0, allPolicies.Count - 1);
            var pdef = allPolicies[_proposeIndex];
            double current = s.Country.Policies.GetValueOrDefault(pdef.Id, pdef.Default);
            if (double.IsNaN(_proposeTarget) || _proposeTarget < pdef.Min || _proposeTarget > pdef.Max) _proposeTarget = current;
            var prop = Ui.VBox(8);
            var pBox = new OptionButton { Disabled = !partner, FocusMode = Control.FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var p in allPolicies) pBox.AddItem(t.Name(p.Id));
            pBox.Selected = _proposeIndex;
            pBox.ItemSelected += i => { _proposeIndex = (int)i; _proposeTarget = double.NaN; s.Touch(); };
            prop.AddChild(pBox);
            var srow = Ui.HBox(8);
            var slider = new PolicySlider(compact: true) { Editable = partner, GovMarker = current };
            slider.Configure(pdef.Min, pdef.Max, Math.Max((pdef.Max - pdef.Min) / 200.0, 0.05), current, null, _proposeTarget);
            var pl = Ui.Right(Ui.L(Ui.Fix(Fmt.Policy(pdef, _proposeTarget, t)), Tx.Data));
            pl.CustomMinimumSize = new Vector2(84, 0);
            slider.TargetChanged += v => { _proposeTarget = v; pl.Text = Ui.Fix(Fmt.Policy(pdef, v, t)); };
            srow.AddChild(slider); srow.AddChild(pl);
            prop.AddChild(srow);
            prop.AddChild(Ui.Costed(t.T("ui.client.go"), 3, () => s.Do(new ProposePolicy(pdef.Id, _proposeTarget), t.T("ui.exit.propose_ok", t.Name(pdef.Id))), partner ? null : t.T("ui.err.not_partner"), !partner));
            grid.AddChild(ActionCard(IconKind.Policies, t.T("ui.client.propose"), prop, t.T("ui.client.propose_hint")));
        }
        var exits = Ui.VBox(8);
        exits.AddChild(Ui.Btn(t.T("ui.client.leave"), () => s.Do(new LeaveCoalition(), t.T("ui.exit.leave_ok")), "ButtonDanger", IconKind.Back, partner ? null : t.T("ui.err.not_partner"), !partner));
        exits.AddChild(Ui.Costed(t.T("ui.client.confidence"), 10, () => s.Do(new NoConfidence(), t.T("ui.exit.confidence_ok")), opposition ? null : t.T("ui.err.not_opposition"), !opposition, "ButtonDanger"));
        grid.AddChild(ActionCard(IconKind.Ballot, t.T("ui.pt.government_play"), exits));
        body.AddChild(grid);
    }

    private static void Measures(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        bool leader = pol.PlayerRole == Role.Leader;
        body.AddChild(Ui.Section(t.T("ui.party.measures")));
        if (ps.Measures.Count == 0) { body.AddChild(Ui.L(t.T("ui.party.no_measures"), Tx.Body, AppTheme.Ink3)); return; }
        foreach (var m in ps.Measures.AsEnumerable().Reverse())
        {
            if (s.Packs.Policies.FirstOrDefault(p => p.Id == m.PolicyId) is not { } def) continue;
            var row = Ui.HBox(10);
            row.AddChild(new PictoDisc(Icons.For(m.PolicyId), 26, AppTheme.Chrome, Colors.White));
            row.AddChild(Ui.L(Ui.Fix(t.T("ui.party.measure_line", m.Turn, t.Name(m.PolicyId), Fmt.Policy(def, m.From, t), Fmt.Policy(def, m.To, t))), Tx.Body, AppTheme.Ink, true));
            if (m.StanceTaken) row.AddChild(Ui.Pill(t.T("ui.party.stance_taken"), AppTheme.Ink3));
            else
            {
                row.AddChild(Ui.Btn(t.T("ui.client.support"), () => s.Do(new TakeStance(m.Id, Stance.Support), t.T("ui.party.stance_ok")), "ButtonSecondary", IconKind.Check, null, leader));
                row.AddChild(Ui.Btn(t.T("ui.client.oppose"), () => s.Do(new TakeStance(m.Id, Stance.Oppose), t.T("ui.party.stance_ok")), "ButtonSecondary", IconKind.Close, null, leader));
            }
            body.AddChild(row);
        }
    }
}
