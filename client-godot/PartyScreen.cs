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
    private int _groupIndex, _effort = 1, _valueIndex, _policyIndex;
    private double _platformTarget = double.NaN;

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
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 20);
        void Stat(string key, string value) { grid.AddChild(Ui.Text(t.T(key), Ui.Muted)); grid.AddChild(Ui.Text(value, null, 18)); }
        Stat("ui.party.capital", Fmt.Number(ps.PoliticalCapital));
        Stat("ui.party.organisation", Fmt.Number(ps.Organization * 100, "0") + " %");
        Stat("ui.party.credibility", Fmt.Number(ps.Credibility * 100, "0") + " %");
        Stat("ui.party.cohesion", Fmt.Number(ps.Cohesion * 100, "0") + " %");
        _body.AddChild(grid);

        Goals(s, pol, ps);
        _body.AddChild(Ui.Rule());
        Actions(s, pol, ps);
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

        // Platform
        var policies = ps.Platform.Keys.Where(id => s.Packs.Policies.Any(p => p.Id == id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (policies.Count > 0)
        {
            _policyIndex = Math.Clamp(_policyIndex, 0, policies.Count - 1);
            var def = s.Packs.Policies.First(p => p.Id == policies[_policyIndex]);
            if (double.IsNaN(_platformTarget) || _platformTarget < def.Min || _platformTarget > def.Max) _platformTarget = ps.Platform[def.Id];
            var platform = Ui.HBox();
            platform.AddChild(Ui.Text(t.T("ui.client.platform") + ":"));
            var polBox = new OptionButton();
            foreach (var id in policies) polBox.AddItem($"{t.Name(id)}  ({Fmt.Policy(s.Packs.Policies.First(p => p.Id == id), ps.Platform[id], t)})");
            polBox.Selected = _policyIndex;
            polBox.ItemSelected += i => { _policyIndex = (int)i; _platformTarget = double.NaN; s.Touch(); };
            platform.AddChild(polBox);
            var slider = new HSlider { MinValue = def.Min, MaxValue = def.Max, Step = Math.Max((def.Max - def.Min) / 400.0, 0.05), Value = _platformTarget, CustomMinimumSize = new Vector2(240, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var label = Ui.Text(Fmt.Policy(def, _platformTarget, t));
            slider.ValueChanged += v => { _platformTarget = v; label.Text = Fmt.Policy(def, v, t); };
            platform.AddChild(slider);
            platform.AddChild(label);
            platform.AddChild(Ui.Btn($"{t.T("ui.client.go")}  (1)", () => s.Do(new ShiftPlatform(def.Id, _platformTarget), t.T("ui.party.platform_ok", t.Name(def.Id)))));
            _body.AddChild(platform);
            _body.AddChild(Ui.Text(t.T("ui.client.platform_hint"), Ui.Muted, 0, true));
        }
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
