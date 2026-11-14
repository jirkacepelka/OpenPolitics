using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>The player's own party: resources, relations, the actions to build support and the opposition's tools.</summary>
public partial class PartyScreen : Screen
{
    public override string TitleKey => "ui.client.tab.party";
    private readonly VBoxContainer _body;
    private int _groupIndex, _effort = 1, _valueIndex;
    private double _proposeTarget = double.NaN;
    private int _proposeIndex;

    public PartyScreen()
    {
        var (scroll, body) = Ui.Page();
        _body = body;
        AddChild(scroll);
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void Refresh(Session s)
    {
        Ui.Clear(_body);
        var t = s.T;
        var pol = s.Politics;
        if (pol?.Party is not { } ps) { _body.AddChild(Ui.Text(t.T("ui.err.no_politics"), Ui.Muted)); return; }

        _body.AddChild(Ui.Heading($"{t.T("ui.heading.party")}: {t.Name(pol.PlayerParty)}"));
        _body.AddChild(Ui.Text(t.T("ui.role." + pol.PlayerRole.ToString().ToLowerInvariant()), Ui.Muted));
        var top = Ui.HBox(16);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 20);
        void Stat(string key, string value) { grid.AddChild(Ui.Text(t.T(key), Ui.Muted)); grid.AddChild(Ui.Text(value, null, 18)); }
        Stat("ui.party.capital", Fmt.Number(ps.PoliticalCapital));
        Stat("ui.party.organisation", Fmt.Number(ps.Organization * 100, "0") + " %");
        Stat("ui.party.credibility", Fmt.Number(ps.Credibility * 100, "0") + " %");
        Stat("ui.party.cohesion", Fmt.Number(ps.Cohesion * 100, "0") + " %");
        Stat("ui.party.income", Fmt.Signed(ps.Income) + " " + t.T("ui.party.per_quarter"));
        var stats = Ui.VBox(10);
        stats.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        stats.AddChild(grid);
        var compass = new CompassView();
        compass.Set(s, pol.PlayerParty);
        top.AddChild(stats);
        top.AddChild(compass);
        _body.AddChild(top);

        Goals(s, pol, ps);
        _body.AddChild(Ui.Rule());
        Actions(s, pol, ps);
        _body.AddChild(Ui.Rule());
        Platform(s, pol, ps);
        _body.AddChild(Ui.Rule());
        Promises(s, pol, ps);
        _body.AddChild(Ui.Rule());
        Relations(s, pol, ps);
        _body.AddChild(Ui.Rule());
        Measures(s, pol, ps);
    }

    private void Goals(Session s, CountryPolitics pol, PartyStatus ps)
    {
        var t = s.T;
        _body.AddChild(Ui.Text(t.T("ui.party.goals"), Ui.Accent, 17));
        void Goal(string key, double value, double max, Color color, string text)
        {
            var row = Ui.HBox(10);
            var l = Ui.Text(t.T(key));
            l.CustomMinimumSize = new Vector2(240, 0);
            row.AddChild(l);
            row.AddChild(Ui.Bar(value, 0, max, color, 240));
            row.AddChild(Ui.Text(text));
            _body.AddChild(row);
        }
        double share = pol.Support.GetValueOrDefault(pol.PlayerParty);
        Goal("ui.goal.poll", share, 0.4, Ui.Accent, Fmt.Number(share * 100) + " %");
        double fragility = pol.Loyalty is { Count: > 0 } l ? 100 - l.Values.Min() : 0;
        Goal("ui.goal.fragility", fragility, 100, Ui.Bad, pol.Loyalty is { Count: > 0 } ? Fmt.Number(fragility, "0") + " %" : "-");
        double rel = ps.Relations.Values.DefaultIfEmpty(0).Average();
        Goal("ui.goal.relations", rel, 1, Ui.Good, Fmt.Number(rel, "0.00"));
    }

    private void Actions(Session s, CountryPolitics pol, PartyStatus ps)
    {
        var t = s.T;
        bool leader = pol.PlayerRole == Role.Leader;
        _body.AddChild(Ui.Text(t.T("ui.client.actions"), Ui.Accent, 17));

        // Campaign
        var groups = s.Packs.VoterGroups.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        var campaign = Ui.HBox();
        campaign.AddChild(Ui.Text(t.T("ui.client.campaign") + ":"));
        var groupBox = new OptionButton();
        foreach (var g in groups) groupBox.AddItem(t.Name(g.Id));
        groupBox.Selected = Math.Clamp(_groupIndex, 0, Math.Max(0, groups.Count - 1));
        groupBox.ItemSelected += i => _groupIndex = (int)i;
        campaign.AddChild(groupBox);
        var effortBox = new OptionButton();
        for (int e = 1; e <= 3; e++) effortBox.AddItem($"{t.T("ui.client.effort")} {e}  ({3 * e})");
        effortBox.Selected = _effort - 1;
        effortBox.ItemSelected += i => _effort = (int)i + 1;
        campaign.AddChild(effortBox);
        campaign.AddChild(Ui.Btn(t.T("ui.client.go"), () => s.Do(new Campaign(groups[_groupIndex].Id, _effort), t.T("ui.party.campaign_ok", t.Name(groups[_groupIndex].Id)))));
        _body.AddChild(campaign);

        // Organise
        _body.AddChild(Ui.Btn($"{t.T("ui.client.organize")}  (4)", () => s.Do(new BuildOrganization(), t.T("ui.party.organized"))));

        // Criticise (opposition and coalition partners)
        var values = s.Packs.Values.OrderBy(v => Localizer.Fold(t.Name(v.Id)), StringComparer.Ordinal).ToList();
        var criticize = Ui.HBox();
        criticize.AddChild(Ui.Text(t.T("ui.client.criticize") + ":"));
        var valueBox = new OptionButton { Disabled = leader };
        foreach (var v in values) valueBox.AddItem(t.Name(v.Id));
        valueBox.Selected = Math.Clamp(_valueIndex, 0, Math.Max(0, values.Count - 1));
        valueBox.ItemSelected += i => _valueIndex = (int)i;
        criticize.AddChild(valueBox);
        criticize.AddChild(Ui.Btn($"{t.T("ui.client.go")}  (2)", () => s.Do(new Criticize(values[_valueIndex].Id), t.T("ui.party.criticized", t.Name(values[_valueIndex].Id))),
            leader ? t.T("ui.err.not_opposition") : null, leader));
        _body.AddChild(criticize);

        // Government play: partners propose and may leave, the leader dismisses (see the relations list), the opposition can topple
        bool partner = pol.PlayerRole == Role.Partner, opposition = pol.PlayerRole == Role.Opposition;
        var allPolicies = s.Packs.Policies.OrderBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal).ToList();
        if (allPolicies.Count > 0)
        {
            _proposeIndex = Math.Clamp(_proposeIndex, 0, allPolicies.Count - 1);
            var pdef = allPolicies[_proposeIndex];
            double current = s.Country.Policies.GetValueOrDefault(pdef.Id, pdef.Default);
            if (double.IsNaN(_proposeTarget) || _proposeTarget < pdef.Min || _proposeTarget > pdef.Max) _proposeTarget = current;
            var propose = Ui.HBox();
            propose.AddChild(Ui.Text(t.T("ui.client.propose") + ":"));
            var pBox = new OptionButton { Disabled = !partner };
            foreach (var p in allPolicies) pBox.AddItem(t.Name(p.Id));
            pBox.Selected = _proposeIndex;
            pBox.ItemSelected += i => { _proposeIndex = (int)i; _proposeTarget = double.NaN; s.Touch(); };
            propose.AddChild(pBox);
            var pSlider = new HSlider { MinValue = pdef.Min, MaxValue = pdef.Max, Step = Math.Max((pdef.Max - pdef.Min) / 400.0, 0.05), Value = _proposeTarget, CustomMinimumSize = new Vector2(240, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter, Editable = partner };
            var pLabel = Ui.Text(Fmt.Policy(pdef, _proposeTarget, t));
            pSlider.ValueChanged += v => { _proposeTarget = v; pLabel.Text = Fmt.Policy(pdef, v, t); };
            propose.AddChild(pSlider);
            propose.AddChild(pLabel);
            propose.AddChild(Ui.Btn($"{t.T("ui.client.go")}  (3)", () => s.Do(new ProposePolicy(pdef.Id, _proposeTarget), t.T("ui.exit.propose_ok", t.Name(pdef.Id))),
                partner ? null : t.T("ui.err.not_partner"), !partner));
            _body.AddChild(propose);
            _body.AddChild(Ui.Text(t.T("ui.client.propose_hint"), Ui.Muted, 0, true));
        }
        var exits = Ui.HBox();
        exits.AddChild(Ui.Btn(t.T("ui.client.leave"), () => s.Do(new LeaveCoalition(), t.T("ui.exit.leave_ok")),
            partner ? null : t.T("ui.err.not_partner"), !partner));
        exits.AddChild(Ui.Btn($"{t.T("ui.client.confidence")}  (10)", () => s.Do(new NoConfidence(), t.T("ui.exit.confidence_ok")),
            opposition ? null : t.T("ui.err.not_opposition"), !opposition));
        _body.AddChild(exits);
    }

    private void Platform(Session s, CountryPolitics pol, PartyStatus ps)
    {
        var t = s.T;
        _body.AddChild(Ui.Section(t.T("ui.party.platform")));
        _body.AddChild(Ui.Text(t.T("ui.client.platform_hint2"), Ui.Muted, 0, true));
        var card = Ui.VBox(8);
        foreach (var id in ps.Platform.Keys.OrderBy(k => Localizer.Fold(t.Name(k)), StringComparer.Ordinal))
        {
            if (s.Packs.Policies.FirstOrDefault(p => p.Id == id) is not { } def) continue;
            var row = Ui.HBox(10);
            var name = Ui.Text(t.Name(id));
            name.CustomMinimumSize = new Vector2(210, 0);
            row.AddChild(name);
            var slider = new HSlider { MinValue = def.Min, MaxValue = def.Max, Step = Math.Max((def.Max - def.Min) / 400.0, 0.05), Value = ps.Platform[id], CustomMinimumSize = new Vector2(190, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var label = Ui.Text(Fmt.Policy(def, slider.Value, t));
            label.CustomMinimumSize = new Vector2(100, 0);
            slider.ValueChanged += v => label.Text = Fmt.Policy(def, v, t);
            row.AddChild(slider);
            row.AddChild(label);
            row.AddChild(Ui.Btn($"{t.T("ui.client.shift")} (1)", () => s.Do(new ShiftPlatform(id, slider.Value), t.T("ui.party.platform_ok", t.Name(id)))));
            double weight = ps.Priorities?.GetValueOrDefault(id, 1.0) ?? 1.0;
            var prio = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = weight, CustomMinimumSize = new Vector2(110, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = t.T("ui.client.priority_hint") };
            row.AddChild(Ui.Text(t.T("ui.party.priority"), Ui.Muted, 13));
            row.AddChild(prio);
            row.AddChild(Ui.Btn($"{t.T("ui.client.set")} (1)", () => s.Do(new SetPriority(id, prio.Value), t.T("ui.party.priority_ok", t.Name(id)))));
            card.AddChild(row);
        }
        _body.AddChild(Ui.Card(card));
        _body.AddChild(Ui.Text(t.T("ui.client.platform_hint"), Ui.Muted, 0, true));
    }

    private void Promises(Session s, CountryPolitics pol, PartyStatus ps)
    {
        var t = s.T;
        _body.AddChild(Ui.Section(t.T("ui.party.promises_heading")));
        bool soon = pol.TurnsToElection <= PoliticsLimits.PromiseWindowTurns && pol.Phase != Phase.Formation;
        _body.AddChild(Ui.Text(t.T(soon ? "ui.client.promises_hint_open" : "ui.client.promises_hint_closed"), soon ? Ui.Warn : Ui.Muted, 0, true));
        var demands = s.Packs.Parties.FirstOrDefault(p => p.Id == pol.PlayerParty)?.Demands;
        if (demands == null || demands.Count == 0) return;
        var card = Ui.VBox(8);
        foreach (var d in demands)
        {
            var row = Ui.HBox(10);
            var text = Ui.Text(s.Texts.Demand(d));
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(text);
            bool made = ps.Promises?.Contains(d.Id) == true;
            if (made) row.AddChild(Ui.Pill(t.T("ui.party.promised"), Ui.Good));
            else row.AddChild(Ui.Btn($"{t.T("ui.client.promise")} (2)", () => s.Do(new MakePromise(d.Id), t.T("ui.party.promise_ok", s.Texts.Demand(d))),
                soon ? null : t.T("ui.err.too_early"), !soon));
            card.AddChild(row);
        }
        _body.AddChild(Ui.Card(card));
    }

    private void Relations(Session s, CountryPolitics pol, PartyStatus ps)
    {
        var t = s.T;
        _body.AddChild(Ui.Text(t.T("ui.party.relations"), Ui.Accent, 17));
        foreach (var party in s.Packs.Parties.Where(p => p.Id != pol.PlayerParty).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            double r = ps.Relations.GetValueOrDefault(party.Id);
            var row = Ui.HBox(10);
            var name = Ui.Text(t.Name(party.Id) + (pol.GovernmentParties.Contains(party.Id) ? " ★" : ""));
            name.CustomMinimumSize = new Vector2(260, 0);
            row.AddChild(name);
            row.AddChild(Ui.Bar(r, 0, 1, Ui.Good, 200));
            row.AddChild(Ui.Text(Fmt.Number(r, "0.00")));
            row.AddChild(Ui.Btn($"{t.T("ui.client.court")}  (3)", () => s.Do(new Court(party.Id), t.T("ui.party.court_ok", t.Name(party.Id)))));
            if (pol.PlayerRole == Role.Leader && pol.GovernmentParties.Contains(party.Id))
                row.AddChild(Ui.Btn($"{t.T("ui.client.dismiss")}  (4)", () => s.Do(new DismissPartner(party.Id), t.T("ui.exit.dismiss_ok", t.Name(party.Id)))));
            _body.AddChild(row);
        }
    }

    private void Measures(Session s, CountryPolitics pol, PartyStatus ps)
    {
        var t = s.T;
        bool leader = pol.PlayerRole == Role.Leader;
        _body.AddChild(Ui.Text(t.T("ui.party.measures"), Ui.Accent, 17));
        if (ps.Measures.Count == 0) { _body.AddChild(Ui.Text(t.T("ui.party.no_measures"), Ui.Muted)); return; }
        foreach (var m in ps.Measures.AsEnumerable().Reverse())
        {
            if (s.Packs.Policies.FirstOrDefault(p => p.Id == m.PolicyId) is not { } def) continue;
            var row = Ui.HBox(10);
            row.AddChild(Ui.Text(t.T("ui.party.measure_line", m.Turn, t.Name(m.PolicyId), Fmt.Policy(def, m.From, t), Fmt.Policy(def, m.To, t))));
            if (m.StanceTaken) row.AddChild(Ui.Text("[" + t.T("ui.party.stance_taken") + "]", Ui.Muted));
            else
            {
                row.AddChild(Ui.Btn(t.T("ui.client.support"), () => s.Do(new TakeStance(m.Id, Stance.Support), t.T("ui.party.stance_ok")), null, leader));
                row.AddChild(Ui.Btn(t.T("ui.client.oppose"), () => s.Do(new TakeStance(m.Id, Stance.Oppose), t.T("ui.party.stance_ok")), null, leader));
            }
            _body.AddChild(row);
        }
    }
}
