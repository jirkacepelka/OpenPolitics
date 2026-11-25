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

    private int _tab, _groupIndex, _effort = 1, _valueIndex, _proposeIndex, _ultKind, _ultIndex, _ultDeadline = 4;
    private double _proposeTarget = double.NaN;
    private bool _adding;
    private readonly Dictionary<string, double> _platformTargets = new(StringComparer.Ordinal);

    public override void Build(Session s, Dialog d, Main main)
    {
        var t = s.T;
        var pol = s.Politics;
        if (pol?.Party is not { } ps) { d.Body.AddChild(Ui.L(t.T("ui.err.no_politics"), Tx.Body)); return; }
        d.Title = t.Name(pol.PlayerParty);
        var tabs = Ui.HBox(26);
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
        tabScroll.AddChild(Ui.Margin(tabs, 26, 0));
        d.Fixed.AddChild(tabScroll);
        d.Fixed.AddChild(new HairLine { CustomMinimumSize = new Vector2(0, 1) });
        d.Body.AddChild(Ui.Gap(4));
        switch (_tab)
        {
            case 0: Overview(s, pol, ps, d.Body); break;
            case 1: Platform(s, ps, d.Body, main); break;
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
        var tiles = new FlowGrid { MinColumn = 210, MaxColumns = 4, Gap = 10 };
        void Tile(string label, string value, string sub, IconKind icon, Color? color = null, string? topic = null)
        {
            var box = Ui.VBox(2);
            if (topic != null) { box.TooltipText = s.Texts.Explain(topic); box.MouseFilter = Control.MouseFilterEnum.Stop; }
            var head = Ui.HBox(6); head.AddChild(new IconView(icon, 16, AppTheme.Chrome)); head.AddChild(Ui.L(label, Tx.Overline, AppTheme.Ink3));
            box.AddChild(head);
            box.AddChild(Ui.L(value, Tx.FigureXl, color ?? AppTheme.Ink));
            if (sub.Length > 0) box.AddChild(Ui.L(sub, Tx.Small, null, true));
            var panel = Ui.Panel(box, "Inset");
            if (topic != null) panel.TooltipText = s.Texts.Explain(topic);
            tiles.AddChild(panel);
        }
        Tile(t.T("ui.party.capital"), Ui.Num(ps.PoliticalCapital), Ui.Signed(ps.Income) + " " + t.T("ui.party.per_quarter"), IconKind.Coin, null, "capital");
        Tile(t.T("ui.party.organisation"), Ui.Num(ps.Organization * 100, "0") + " %", "", IconKind.Gear, null, "organisation");
        Tile(t.T("ui.party.credibility"), Ui.Num(ps.Credibility * 100, "0") + " %", "", IconKind.Shield, null, "credibility");
        Tile(t.T("ui.party.cohesion"), Ui.Num(ps.Cohesion * 100, "0") + " %", "", IconKind.Users, null, "cohesion");
        Tile(t.T("ui.party.fiscal_blame"), Ui.Num(ps.FiscalBlame, "0.0"), ps.FiscalBlame >= 0.05 ? t.T("ui.party.fiscal_blame_sub", Ui.Num(FiscalBlame.Penalty(ps.FiscalBlame) * 100, "0.#")) : t.T("ui.party.fiscal_blame_none"), IconKind.Alert,
            ps.FiscalBlame > 1 ? AppTheme.BadText : null, "fiscal_blame");
        // decision 0016 (3): the gentle counterpart - credit for cutting a high deficit in government
        Tile(t.T("ui.party.fiscal_credit"), Ui.Num(ps.FiscalCredit, "0.0"), ps.FiscalCredit >= 0.05
                ? t.T("ui.party.fiscal_credit_sub", Ui.Num(FiscalBlame.Bonus(ps.FiscalCredit) * 100, "0.#"), Ui.Num(ps.FiscalCreditCapital, "0.0")) : t.T("ui.party.fiscal_credit_none"),
            IconKind.Coin, ps.FiscalCredit >= 0.05 ? AppTheme.GoodText : null, "fiscal_credit");
        left.AddChild(tiles);
        left.AddChild(Ui.Section(s.Texts.GoalsHeading(pol.PlayerRole)));
        double share = pol.Support.GetValueOrDefault(pol.PlayerParty);
        left.AddChild(Detail.LabelledBar(t.T("ui.goal.poll"), Ui.Num(share * 100) + " %", share / 0.4, AppTheme.Chrome));
        double fragility = pol.Loyalty is { Count: > 0 } l ? 100 - l.Values.Min() : 0;
        left.AddChild(Detail.LabelledBar(s.Texts.ForRole("ui.goal.fragility", pol.PlayerRole), pol.Loyalty is { Count: > 0 } ? Ui.Num(fragility, "0") + " %" : "–", fragility / 100, AppTheme.Bad));
        double rel = ps.Relations.Values.DefaultIfEmpty(0).Average();
        left.AddChild(Detail.LabelledBar(t.T("ui.goal.relations"), Ui.Num(rel, "0.00"), rel, AppTheme.Good));
        var compass = new CompassView { CustomMinimumSize = new Vector2(240, 260), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        compass.Set(s, pol.PlayerParty);
        var right = Ui.VBox(8);
        right.AddChild(Ui.L(t.T("ui.client.compass"), Tx.Overline, AppTheme.Ink3));
        right.AddChild(compass);
        body.AddChild(Detail.Columns(left, right, 1.5f));
        // How it works: the rules behind the stats, with the engine's numbers (decision 0014 f)
        body.AddChild(Ui.Section(t.T("ui.pt.how")));
        var how = new FlowGrid { MinColumn = 320, MaxColumns = 2, Gap = 12 };
        foreach (var (topic, key) in new[] { ("capital", "ui.party.capital"), ("organisation", "ui.party.organisation"), ("credibility", "ui.party.credibility"),
                     ("cohesion", "ui.party.cohesion"), ("relations", "ui.party.relations"), ("fiscal_blame", "ui.party.fiscal_blame"),
                     ("fiscal_credit", "ui.party.fiscal_credit") })
        {
            var box = Ui.VBox(4);
            box.AddChild(Ui.L(t.T(key).TrimEnd(':'), Tx.BodyStrong));
            box.AddChild(Ui.L(s.Texts.Explain(topic), Tx.Small, AppTheme.Ink2, true));
            how.AddChild(Ui.Panel(box, "Inset"));
        }
        body.AddChild(how);
    }

    private void Platform(Session s, PartyStatus ps, VBoxContainer body, Main main)
    {
        var t = s.T;
        Goals(s, body);
        body.AddChild(Ui.L(t.T("ui.client.platform_hint2") + " " + t.T("ui.client.platform_hint"), Tx.Body, AppTheme.Ink2, true));
        body.AddChild(Ui.L(s.Texts.Explain("shift") + " " + s.Texts.Explain("priority"), Tx.Small, AppTheme.Ink3, true));
        // the programme is a choice of laws, not all of them: add one, or take one out with the minus
        var head = Ui.HBox(10);
        head.AddChild(Ui.L(t.T("ui.program.count", ps.Platform.Count, PartyRules.PlatformMax), Tx.BodyStrong));
        head.AddChild(Ui.Spacer());
        bool full = ps.Platform.Count >= PartyRules.PlatformMax;
        head.AddChild(Ui.Btn(t.T(_adding ? "ui.program.done" : "ui.program.add"), () => { _adding = !_adding; main.Show(this); }, _adding ? "ButtonSecondary" : "", _adding ? IconKind.Check : IconKind.Plus,
            full ? t.T("ui.err.platform_full", PartyRules.PlatformMax) : t.T("ui.program.add_hint", PartyRules.AddCost), full && !_adding, 34));
        body.AddChild(head);
        if (_adding && !full) AddProgram(s, ps, body);
        foreach (var grp in ps.Platform.Keys.Select(k => s.Packs.Policies.FirstOrDefault(p => p.Id == k)).Where(p => p != null).Select(p => p!)
                     .GroupBy(WorldMap.GroupOf).OrderBy(g => g.Key))
        {
            body.AddChild(Ui.Section(t.T("ui.client.group." + grp.Key)));
            foreach (var def in grp.OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal))
            {
                string id = def.Id;
                var row = Ui.HBox(10);
                row.AddChild(new PictoDisc(Icons.For(id), 26));
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
                row.AddChild(Ui.Costed(t.T("ui.client.shift"), (int)PartyRules.ShiftCost, () => { if (s.Do(new ShiftPlatform(id, slider.Target), t.T("ui.party.platform_ok", t.Name(id)))) _platformTargets.Remove(id); }, null, false, "ButtonSecondary"));
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
                bool atMin = ps.Platform.Count <= PartyRules.PlatformMin;
                row.AddChild(Ui.Btn("", () => { if (s.Do(new RemoveFromPlatform(id), t.T("ui.program.removed", t.Name(id)))) _platformTargets.Remove(id); }, "ButtonDanger", IconKind.Minus,
                    atMin ? t.T("ui.err.platform_min", PartyRules.PlatformMin) : t.T("ui.program.remove_hint", PartyRules.RemoveCost), atMin, 30));
                body.AddChild(row);
            }
        }
    }

    /// <summary>The laws that are not in the programme yet, by sector: choosing one puts it in at the setting now in force.</summary>
    private void AddProgram(Session s, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        var panel = Ui.VBox(6);
        var free = s.Packs.Policies.Where(p => !ps.Platform.ContainsKey(p.Id)).GroupBy(WorldMap.GroupOf).OrderBy(g => g.Key).ToList();
        foreach (var grp in free)
        {
            panel.AddChild(Ui.Section(t.T("ui.client.group." + grp.Key)));
            var grid = new FlowGrid { MinColumn = 330, MaxColumns = 3, Gap = 6 };
            foreach (var def in grp.OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal))
            {
                string id = def.Id;
                var row = Ui.HBox(8);
                row.AddChild(new PictoDisc(Icons.For(id), 24));
                var text = Ui.VBox(0);
                text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var nm = Ui.L(t.Name(id), Tx.BodyStrong);
                nm.ClipText = true; nm.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                text.AddChild(nm);
                text.AddChild(Ui.L(Ui.Fix(t.T("ui.program.now", Fmt.Policy(def, s.Country.Policies.GetValueOrDefault(id, def.Default), t))), Tx.Small, AppTheme.Ink3));
                row.AddChild(text);
                row.AddChild(Ui.Btn("", () => s.Do(new AddToPlatform(id), t.T("ui.program.added", t.Name(id))), "ButtonSecondary", IconKind.Plus, t.T("ui.program.add_hint", PartyRules.AddCost), false, 30));
                grid.AddChild(Ui.Panel(Ui.Margin(row, 8, 6), "Inset"));
            }
            panel.AddChild(grid);
        }
        body.AddChild(Ui.Panel(Ui.Margin(panel, 12, 10), "Card"));
    }

    /// <summary>The programme's goals and demands (decision 0014 a): value goals and policy demands with their weight and whether they are met now.</summary>
    private static void Goals(Session s, VBoxContainer body)
    {
        var t = s.T;
        var party = s.Packs.Parties.FirstOrDefault(p => p.Id == s.Politics?.PlayerParty);
        if (party == null || party.Demands.Count == 0) return;
        body.AddChild(Ui.Section(t.T("ui.program.goals")));
        body.AddChild(Ui.L(t.T("ui.program.goals_hint"), Tx.Small, AppTheme.Ink3, true));
        var grid = new FlowGrid { MinColumn = 320, MaxColumns = 2, Gap = 8 };
        foreach (var dm in party.Demands.OrderBy(d => d.Kind == "value" ? 0 : 1).ThenByDescending(d => d.Weight).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            var row = Ui.HBox(8);
            row.AddChild(new PictoDisc(Icons.For(dm.Subject), 24));
            var text = Ui.VBox(0);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AddChild(Ui.L(Ui.Fix(s.Texts.Demand(dm)), Tx.Body, AppTheme.Ink, true));
            double now = dm.Kind == "policy" ? s.Country.Policies.GetValueOrDefault(dm.Subject) : s.Country.Values.GetValueOrDefault(dm.Subject);
            string shown = dm.Kind == "policy" && s.Packs.Policies.FirstOrDefault(p => p.Id == dm.Subject) is { } pd ? Fmt.Policy(pd, now, t)
                : s.Packs.Values.FirstOrDefault(v => v.Id == dm.Subject) is { } vd ? Fmt.Value(vd, now) : Fmt.Number(now);
            text.AddChild(Ui.L(Ui.Fix(t.T("ui.program.now", shown) + " · " + t.T("ui.program.weight", Fmt.Number(dm.Weight))), Tx.Small, AppTheme.Ink3));
            row.AddChild(text);
            bool met = Texts.IsMet(dm, s.Country);
            row.AddChild(Ui.Pill(t.T(met ? "ui.goal.met" : "ui.goal.unmet"), met ? AppTheme.Good : AppTheme.Chrome));
            grid.AddChild(row);
        }
        body.AddChild(grid);
        body.AddChild(Ui.Section(t.T("ui.pt.platform")));
    }

    private static void Promises(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        bool soon = pol.TurnsToElection <= PoliticsLimits.PromiseWindowTurns && pol.Phase != Phase.Formation;
        body.AddChild(Ui.Panel(Ui.L(t.T(soon ? "ui.client.promises_hint_open" : "ui.client.promises_hint_closed"), Tx.BodyStrong, soon ? AppTheme.WarnText : AppTheme.Ink2, true), soon ? "Warn" : "Inset"));
        // decision 0015: a promise made now is about the next term and is judged at the election that ends it
        int interval = s.Packs.Countries.FirstOrDefault(c => c.Politics != null)?.Politics?.ElectionIntervalTurns ?? 16;
        int fresh = ps.PromiseDetails?.Count(p => !p.Carried) ?? 0;
        body.AddChild(Ui.L(t.T("ui.client.promise_when", pol.TurnsToElection + interval, fresh, PoliticsLimits.MaxPromises), Tx.Body, AppTheme.Ink2, true));
        body.AddChild(Ui.L(s.Texts.Explain("promise"), Tx.Small, AppTheme.Ink3, true));
        var demands = s.Packs.Parties.FirstOrDefault(p => p.Id == pol.PlayerParty)?.Demands;
        if (demands == null || demands.Count == 0) return;
        foreach (var dm in demands)
        {
            var row = Ui.HBox(12);
            row.AddChild(new PictoDisc(Icons.For(dm.Subject), 26));
            var text = Ui.VBox(2);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AddChild(Ui.L(Ui.Fix(s.Texts.Demand(dm)), Tx.Body, AppTheme.Ink, true));
            var made = ps.PromiseDetails?.FirstOrDefault(p => p.DemandId == dm.Id);
            if (made != null)
                foreach (var line in s.Texts.PromiseStatus(made))
                    text.AddChild(Ui.L(Ui.Fix(line), Tx.Small, made.CountsAgainst ? AppTheme.Ink2 : AppTheme.Ink3, true));
            row.AddChild(text);
            bool met = Texts.IsMet(dm, s.Country);
            row.AddChild(Ui.Pill(t.T(met ? "ui.goal.met" : "ui.goal.unmet"), met ? AppTheme.Good : AppTheme.Chrome));
            if (made != null) row.AddChild(Ui.Pill(t.T("ui.party.promised"), AppTheme.Good));
            else
            {
                bool full = fresh >= PoliticsLimits.MaxPromises;
                row.AddChild(Ui.Costed(t.T("ui.client.promise"), (int)PartyRules.PromiseCost, () => s.Do(new MakePromise(dm.Id), t.T("ui.party.promise_ok", s.Texts.Demand(dm))),
                    !soon ? t.T("ui.err.too_early") : met ? t.T("ui.err.already_met") : full ? t.T("ui.err.too_many_promises") : null, !soon || met || full));
            }
            body.AddChild(row);
            body.AddChild(Ui.Rule());
        }
    }

    private static void Relations(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body, Main main)
    {
        var t = s.T;
        body.AddChild(Ui.L(s.Texts.Explain("relations"), Tx.Small, AppTheme.Ink3, true));
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
        head.AddChild(new PictoDisc(icon, 34));
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

        var groups = s.Groups.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        var campaign = Ui.VBox(8);
        var groupBox = new OptionButton { FocusMode = Control.FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var g in groups) groupBox.AddItem(t.Name(g.Id));
        groupBox.Selected = Math.Clamp(_groupIndex, 0, Math.Max(0, groups.Count - 1));
        groupBox.ItemSelected += i => _groupIndex = (int)i;
        campaign.AddChild(groupBox);
        var effortRow = Ui.HBox(8);
        effortRow.AddChild(Ui.L(t.T("ui.client.effort"), Tx.Label));
        effortRow.AddChild(Ui.Segmented(new[] { "1 · 3", "2 · 6", "3 · 9" }, _effort - 1, i => { _effort = i + 1; s.Touch(); }));
        campaign.AddChild(effortRow);
        // decision 0015: what the campaign wins, in points of support among the group, before paying for it
        if (groups.Count > 0)
        {
            var gid = groups[Math.Clamp(_groupIndex, 0, groups.Count - 1)].Id;
            double share = ps.GroupSupport?.GetValueOrDefault(gid) ?? 0;
            campaign.AddChild(Ui.L(Ui.Fix(t.T("ui.campaign.preview", t.Name(gid), Ui.Num(share * 100, "0.0"), Texts.Points(s.Game.PreviewCampaign(gid, _effort)), PartyRules.MomentumHalfLifeTurns)), Tx.Small, AppTheme.GoodText, true));
        }
        groupBox.ItemSelected += _ => s.Touch();
        campaign.AddChild(Ui.Btn(t.T("ui.client.go"), () => s.Do(new Campaign(groups[_groupIndex].Id, _effort), t.T("ui.party.campaign_ok", t.Name(groups[_groupIndex].Id))), "", IconKind.Check));
        grid.AddChild(ActionCard(IconKind.Voters, t.T("ui.client.campaign"), campaign, s.Texts.Explain("campaign")));

        grid.AddChild(ActionCard(IconKind.Gear, t.T("ui.client.organize"), Ui.Costed(t.T("ui.client.go"), (int)PartyRules.OrgBuildCost, () => s.Do(new BuildOrganization(), t.T("ui.party.organized"))), s.Texts.Explain("organise")));

        var values = s.Packs.Values.OrderBy(v => Localizer.Fold(t.Name(v.Id)), StringComparer.Ordinal).ToList();
        var crit = Ui.VBox(8);
        var valueBox = new OptionButton { Disabled = leader, FocusMode = Control.FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var v in values) valueBox.AddItem(t.Name(v.Id));
        valueBox.Selected = Math.Clamp(_valueIndex, 0, Math.Max(0, values.Count - 1));
        valueBox.ItemSelected += i => _valueIndex = (int)i;
        crit.AddChild(valueBox);
        crit.AddChild(Ui.Costed(t.T("ui.client.go"), 2, () => s.Do(new Criticize(values[_valueIndex].Id), t.T("ui.party.criticized", t.Name(values[_valueIndex].Id))), leader ? t.T("ui.err.not_opposition") : null, leader));
        grid.AddChild(ActionCard(IconKind.Alert, t.T("ui.client.criticize"), crit, s.Texts.Explain("criticize")));

        var allPolicies = s.Packs.Policies.OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal).ToList();
        if (allPolicies.Count > 0) grid.AddChild(Propose(s, pol, allPolicies));
        if (partner && pol.Phase == Phase.Governing) grid.AddChild(UltimatumCard(s, pol));
        var exits = Ui.VBox(8);
        exits.AddChild(Ui.Btn(t.T("ui.client.leave"), () => s.Do(new LeaveCoalition(), t.T("ui.exit.leave_ok")), "ButtonDanger", IconKind.Back, partner ? null : t.T("ui.err.not_partner"), !partner));
        exits.AddChild(Ui.Costed(t.T("ui.client.confidence"), 10, () => s.Do(new NoConfidence(), t.T("ui.exit.confidence_ok")), opposition ? null : t.T("ui.err.not_opposition"), !opposition, "ButtonDanger"));
        grid.AddChild(ActionCard(IconKind.Ballot, t.T("ui.pt.government_play"), exits, s.Texts.Explain("exits")));
        body.AddChild(grid);
    }

    /// <summary>
    /// Propose a policy (decision 0015): the one control for every party that does not lead - policy picker, slider with the target, who decides
    /// (the leader's likely answer for a partner; public for the opposition), how each group's support would move and the credibility cost.
    /// </summary>
    private Control Propose(Session s, CountryPolitics pol, List<PolicyDef> policies)
    {
        var t = s.T;
        bool leader = pol.PlayerRole == Role.Leader && pol.Phase == Phase.Governing;
        bool partner = pol.PlayerRole == Role.Partner && pol.Phase == Phase.Governing;
        _proposeIndex = Math.Clamp(_proposeIndex, 0, policies.Count - 1);
        var def = policies[_proposeIndex];
        double current = s.Game.PendingPolicies.LastOrDefault(p => p.PolicyId == def.Id)?.Value ?? s.Country.Policies.GetValueOrDefault(def.Id, def.Default);
        if (double.IsNaN(_proposeTarget) || _proposeTarget < def.Min || _proposeTarget > def.Max)
            _proposeTarget = pol.Party?.Platform.TryGetValue(def.Id, out var own) == true ? own : current;
        var box = Ui.VBox(8);
        var picker = new OptionButton { Disabled = leader, FocusMode = Control.FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var p in policies) picker.AddItem(t.Name(p.Id));
        picker.Selected = _proposeIndex;
        picker.ItemSelected += i => { _proposeIndex = (int)i; _proposeTarget = double.NaN; s.Touch(); };
        box.AddChild(picker);
        var srow = Ui.HBox(8);
        var slider = new PolicySlider(compact: true) { Editable = !leader, GovMarker = current };
        slider.Configure(def.Min, def.Max, Math.Max((def.Max - def.Min) / 200.0, 0.05), current, null, _proposeTarget);
        var value = Ui.Right(Ui.L(Ui.Fix(Fmt.Policy(def, _proposeTarget, t)), Tx.Data));
        value.CustomMinimumSize = new Vector2(84, 0);
        srow.AddChild(slider); srow.AddChild(value);
        box.AddChild(srow);
        var preview = Ui.VBox(2);
        void Preview()
        {
            Ui.Clear(preview);
            if (leader || s.Game.PreviewProposal(def.Id, _proposeTarget) is not { } r) return;
            if (r.ErrorKey == "error.no_change") { preview.AddChild(Ui.L(t.T("ui.pd.move_hint"), Tx.Small, AppTheme.Ink3, true)); return; }
            if (r.ErrorKey != null) preview.AddChild(Ui.L(s.Texts.Error(new ActionResult(false, r.ErrorKey, null)), Tx.Small, AppTheme.WarnText, true));
            var lines = s.Texts.ProposalPreviewLines(r);
            for (int i = 0; i < lines.Count; i++)
            {
                var color = i == 0 ? (r.ToLeader && r.LeaderAgrees != true ? AppTheme.WarnText : AppTheme.Ink2)
                    : i == lines.Count - 1 && r.Reaction.Credibility < 0 ? AppTheme.WarnText : AppTheme.Ink2;
                preview.AddChild(Ui.L(Ui.Fix(lines[i]), Tx.Small, color, true));
            }
        }
        slider.TargetChanged += v => { _proposeTarget = v; value.Text = Ui.Fix(Fmt.Policy(def, v, t)); Preview(); };
        Preview();
        box.AddChild(preview);
        box.AddChild(Ui.Costed(t.T("ui.client.propose_go"), (int)(partner ? PoliticsLimits.ProposeToLeaderCost : PoliticsLimits.AnnounceCost), () =>
        {
            bool toLeader = s.Game.PreviewProposal(def.Id, _proposeTarget)?.ToLeader == true;
            s.Do(new ProposePolicy(def.Id, _proposeTarget), t.T(toLeader ? "ui.exit.propose_ok" : "ui.announce.ok", t.Name(def.Id)));
        }, leader ? t.T("ui.err.not_for_leader") : null, leader));
        string hint = t.T(leader ? "ui.client.propose_hint_leader" : partner ? "ui.client.propose_hint" : "ui.client.propose_hint_opposition") + " " + s.Texts.Explain("propose");
        return ActionCard(IconKind.Policies, t.T("ui.client.propose"), box, hint);
    }

    /// <summary>
    /// Decision 0016 (1): the junior partner's ultimatum to the leader - what to demand (one of the party's demands, or a programme position), the
    /// deadline, the leader's likely answer, the party's leverage and the stakes, Give the ultimatum · 4; while one runs, its status and Withdraw.
    /// </summary>
    private Control UltimatumCard(Session s, CountryPolitics pol)
    {
        var t = s.T;
        var box = Ui.VBox(8);
        string leader = pol.Leader ?? "";
        if (pol.PlayerUltimatum is { } open)
        {
            box.AddChild(Ui.L(Ui.Fix(s.Texts.UltimatumStatus(open, leader)), Tx.BodyStrong, open.Answer == "refuse" ? AppTheme.BadText : AppTheme.WarnText, true));
            var withdraw = Ui.Btn(t.T("ui.myult.withdraw"), () => s.Do(new WithdrawUltimatum(), t.T("ui.myult.withdrawn")), "ButtonSecondary", IconKind.Back, s.Texts.Explain("ultimatum"), false, 36);
            withdraw.Name = "UltimatumWithdraw";
            withdraw.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            box.AddChild(withdraw);
            return ActionCard(IconKind.Alert, t.T("ui.myult.title"), box, s.Texts.Explain("ultimatum"));
        }
        var party = s.Packs.Parties.First(p => p.Id == pol.PlayerParty);
        // what can be demanded: the party's unmet demands, or programme positions the setting is away from
        var options = new List<(string Kind, string Id, string Label)>();
        if (_ultKind == 0)
            foreach (var dm in party.Demands.Where(d => !Texts.IsMet(d, s.Country)).OrderByDescending(d => d.Weight).ThenBy(d => d.Id, StringComparer.Ordinal))
                options.Add(("demand", dm.Id, Ui.Fix(s.Texts.Demand(dm))));
        else
            foreach (var (id, target) in (pol.Party?.Platform ?? new Dictionary<string, double>()).OrderBy(kv => Localizer.Fold(t.Name(kv.Key)), StringComparer.Ordinal))
                if (s.Packs.Policies.FirstOrDefault(p => p.Id == id) is { } def && Math.Abs(target - s.Country.Policies.GetValueOrDefault(id)) > 0.02 * (def.Max - def.Min))
                    options.Add(("policy", id, $"{t.Name(id)} → {Ui.Fix(Fmt.Policy(def, target, t))}"));
        box.AddChild(Ui.Segmented(new[] { t.T("ui.myult.kind_demand"), t.T("ui.myult.kind_policy") }, _ultKind, i => { _ultKind = i; _ultIndex = 0; s.Touch(); }));
        if (options.Count == 0)
        {
            box.AddChild(Ui.L(t.T("ui.myult.none"), Tx.Small, AppTheme.Ink3, true));
            return ActionCard(IconKind.Alert, t.T("ui.myult.title"), box, s.Texts.Explain("ultimatum"));
        }
        _ultIndex = Math.Clamp(_ultIndex, 0, options.Count - 1);
        var picker = new OptionButton { FocusMode = Control.FocusModeEnum.None, ClipText = true, CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = "UltimatumSubject" };
        foreach (var o in options) picker.AddItem(o.Label);
        picker.Selected = _ultIndex;
        picker.ItemSelected += i => { _ultIndex = (int)i; s.Touch(); };
        box.AddChild(Ui.L(t.T("ui.myult.subject"), Tx.Label));
        box.AddChild(picker);
        var drow = Ui.HBox(8);
        drow.AddChild(Ui.L(t.T("ui.myult.deadline"), Tx.Label));
        int min = PoliticsLimits.UltimatumMinDeadline, max = PoliticsLimits.UltimatumMaxDeadline;
        drow.AddChild(Ui.Segmented(Enumerable.Range(min, max - min + 1).Select(q => q.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            Math.Clamp(_ultDeadline, min, max) - min, i => { _ultDeadline = min + i; s.Touch(); }));
        box.AddChild(drow);
        var choice = options[_ultIndex];
        var preview = s.Game.PreviewUltimatum(choice.Kind, choice.Id, _ultDeadline);
        if (preview != null)
        {
            var lines = s.Texts.UltimatumPreviewLines(preview, leader);
            for (int i = 0; i < lines.Count; i++)
            {
                var color = preview.ErrorKey != null ? AppTheme.WarnText
                    : i == 1 ? (preview.Answer == "deliver" || preview.DeliversAfter != null ? AppTheme.GoodText : AppTheme.BadText)
                    : i == 2 ? (preview.Pivotal && !preview.Patchable ? AppTheme.GoodText : preview.Pivotal ? AppTheme.Ink2 : AppTheme.WarnText) : AppTheme.Ink2;
                box.AddChild(Ui.L(Ui.Fix(lines[i]), i == 1 ? Tx.BodyStrong : Tx.Small, color, true));
            }
        }
        var go = Ui.Costed(t.T("ui.myult.go"), (int)PoliticsLimits.UltimatumCost, () => s.Do(new IssueUltimatum(choice.Kind, choice.Id, _ultDeadline), t.T("ui.myult.ok", t.Name(leader))),
            preview?.ErrorKey != null ? s.Texts.Error(new ActionResult(false, preview.ErrorKey, preview.Detail)) : null, preview?.ErrorKey != null, "ButtonDanger");
        go.Name = "UltimatumGo";
        box.AddChild(go);
        return ActionCard(IconKind.Alert, t.T("ui.myult.title"), box, s.Texts.Explain("ultimatum"));
    }

    private static void Measures(Session s, CountryPolitics pol, PartyStatus ps, VBoxContainer body)
    {
        var t = s.T;
        bool leader = pol.PlayerRole == Role.Leader;
        body.AddChild(Ui.Section(t.T("ui.party.measures")));
        body.AddChild(Ui.L(s.Texts.Explain("stance"), Tx.Small, AppTheme.Ink3, true));
        if (ps.Measures.Count == 0) { body.AddChild(Ui.L(t.T("ui.party.no_measures"), Tx.Body, AppTheme.Ink3)); return; }
        foreach (var m in ps.Measures.AsEnumerable().Reverse())
        {
            if (s.Packs.Policies.FirstOrDefault(p => p.Id == m.PolicyId) is not { } def) continue;
            var row = Ui.HBox(10);
            row.AddChild(new PictoDisc(Icons.For(m.PolicyId), 26));
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
