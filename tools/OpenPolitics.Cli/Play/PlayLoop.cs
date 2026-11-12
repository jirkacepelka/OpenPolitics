using System.Globalization;
using System.Text;
using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Cli.Play;

/// <summary>
/// A text client for a human player. It only asks the core for state and sends it player actions.
/// Texts come from the packs' localization (ui.*, name.*, desc.*).
/// </summary>
public sealed class PlayLoop
{
    private const int Label = 31;
    private readonly PackSet _packs;
    private readonly string _scenarioId;
    private readonly ScenarioDef _scenario;
    private readonly ulong _seed;
    private readonly int _maxTurns;
    private readonly bool _tips;
    private readonly TextReader _in;
    private readonly TextWriter _out;
    private readonly Localizer _t;
    private readonly List<ValueDef> _values;
    private readonly List<PolicyDef> _policies;
    private readonly Dictionary<string, ValueDef> _valueById;
    private readonly Dictionary<string, PolicyDef> _policyById;
    private Game _game;
    private TurnReport? _last;
    private readonly List<double> _gdpHistory = new();
    private readonly double _startGdp;
    private int _nextTip = 2;

    public PlayLoop(PackSet packs, string scenarioId, ulong seed, string language, int maxTurns, bool tips,
        TextReader input, TextWriter output)
    {
        _packs = packs; _scenarioId = scenarioId; _scenario = packs.GetScenario(scenarioId);
        _seed = seed; _maxTurns = maxTurns; _tips = tips; _in = input; _out = output;
        _t = new Localizer(packs, language);
        _game = Simulation.NewGame(packs, scenarioId, seed);
        _valueById = packs.Values.ToDictionary(v => v.Id);
        _policyById = packs.Policies.ToDictionary(p => p.Id);
        _values = packs.Values
            .OrderBy(v => v.Owner == null ? 1 : 0)
            .ThenBy(v => Localizer.Fold(_t.Name(v.Id)), StringComparer.Ordinal).ToList();
        _policies = packs.Policies
            .OrderBy(p => p.RevenueBaseValueId != null ? 0 : p.CostPerUnit != 0 ? 1 : 2)
            .ThenBy(p => Localizer.Fold(_t.Name(p.Id)), StringComparer.Ordinal).ToList();
        _startGdp = Current.Values.TryGetValue(MacroModelV1.Ids.Gdp, out var g) ? g : 0;
        _gdpHistory.Add(_startGdp);
    }

    private CountryState Current => _game.State.Countries.Single().Value;

    public int Run()
    {
        _out.WriteLine($"=== {_t.T("ui.title")} ===");
        ShowTurnHeader();
        ShowOverview();
        ShowPolicies();
        if (_tips) _out.WriteLine("\n" + _t.T("ui.tip.1"));
        while (true)
        {
            _out.WriteLine("\n" + _t.T("ui.prompt"));
            _out.Write("> ");
            var line = _in.ReadLine();
            if (line == null) { _out.WriteLine(); return 0; }
            var cmd = Parse(line);
            switch (cmd.Verb)
            {
                case "next": if (EndQuarter()) return 0; break;
                case "quit": _out.WriteLine(_t.T("ui.bye")); return 0;
                case "help": ShowHelp(); break;
                case "list": ShowPolicies(); break;
                case "voters": ShowVoters(); break;
                case "values": ShowValues(); break;
                case "set": DoSet(cmd.Args); break;
                case "why": DoWhy(cmd.Args); break;
                case "info": DoInfo(cmd.Args); break;
                case "save": DoSave(cmd.Args); break;
                case "load": DoLoad(cmd.Args); break;
                case "coalition": ShowCoalition(); break;
                case "auto": DoAuto(); break;
                case "invite": DoInvite(cmd.Args, add: true); break;
                case "drop": DoInvite(cmd.Args, add: false); break;
                case "agree": DoAgree(cmd.Args); break;
                case "submit": DoSubmit(); break;
                case "accept": Report(_game.Apply(new AcceptOffer()), "ui.formation.joined"); break;
                case "reject": Report(_game.Apply(new RejectOffer()), "ui.formation.rejected"); break;
                case "early": Report(_game.Apply(new CallEarlyElection()), "ui.formation.early_called"); break;
                default: _out.WriteLine(_t.T("ui.err.unknown_command")); break;
            }
        }
    }

    // ---- input -----------------------------------------------------------------

    private static readonly Dictionary<string, string> Verbs = new(StringComparer.Ordinal)
    {
        [""] = "next", ["next"] = "next", ["dal"] = "next", ["dalsi"] = "next", ["n"] = "next",
        ["q"] = "quit", ["quit"] = "quit", ["exit"] = "quit", ["konec"] = "quit",
        ["?"] = "help", ["help"] = "help", ["napoveda"] = "help", ["h"] = "help",
        ["list"] = "list", ["seznam"] = "list", ["politiky"] = "list", ["l"] = "list",
        ["voters"] = "voters", ["volici"] = "voters", ["v"] = "voters",
        ["values"] = "values", ["hodnoty"] = "values",
        ["set"] = "set", ["nastav"] = "set",
        ["why"] = "why", ["proc"] = "why",
        ["info"] = "info", ["co"] = "info",
        ["save"] = "save", ["uloz"] = "save",
        ["load"] = "load", ["nacti"] = "load",
        ["coalition"] = "coalition", ["koalice"] = "coalition",
        ["auto"] = "auto",
        ["invite"] = "invite", ["pozvi"] = "invite",
        ["drop"] = "drop", ["vyhod"] = "drop",
        ["agree"] = "agree", ["dohoda"] = "agree",
        ["submit"] = "submit", ["predlozit"] = "submit", ["nabidni"] = "submit",
        ["accept"] = "accept", ["prijmout"] = "accept", ["prijmi"] = "accept",
        ["reject"] = "reject", ["odmitnout"] = "reject", ["odmitni"] = "reject",
        ["early"] = "early", ["predcasne"] = "early",
    };

    private static (string Verb, string[] Args) Parse(string line)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("next", Array.Empty<string>());
        string word = Localizer.Fold(parts[0]);
        return (Verbs.TryGetValue(word, out var v) ? v : "?unknown", parts[1..]);
    }

    private static IEnumerable<string> Words(string name) =>
        Localizer.Fold(name).Split(new[] { ' ', '(', ')', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Finds an item by number, id, id suffix, exact name or unique word prefix (diacritics ignored).</summary>
    private T? Resolve<T>(string token, IReadOnlyList<T> items, Func<T, string> id, bool numbers) where T : class
    {
        if (numbers && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return n >= 1 && n <= items.Count ? items[n - 1] : null;
        string f = Localizer.Fold(token);
        var hit = items.FirstOrDefault(i => id(i).Equals(token, StringComparison.OrdinalIgnoreCase));
        if (hit != null) return hit;
        var suffix = items.Where(i => id(i).Split(':').Last().Equals(f, StringComparison.OrdinalIgnoreCase)).ToList();
        if (suffix.Count == 1) return suffix[0];
        var exact = items.Where(i => Localizer.Fold(_t.Name(id(i))) == f).ToList();
        if (exact.Count == 1) return exact[0];
        var prefix = items.Where(i => Words(_t.Name(id(i))).Any(w => w.StartsWith(f, StringComparison.Ordinal))).ToList();
        return prefix.Count == 1 ? prefix[0] : null;
    }

    private string JoinArgs(string[] args, int from) => string.Join(" ", args.Skip(from));

    // ---- commands --------------------------------------------------------------

    private bool EndQuarter()
    {
        _last = _game.EndTurn();
        var gdp = Current.Values.GetValueOrDefault(MacroModelV1.Ids.Gdp);
        _gdpHistory.Add(gdp);
        _out.WriteLine();
        ShowTurnHeader();
        if (_last.FiredEventIds.Count > 0)
        {
            _out.WriteLine($"\n{_t.T("ui.heading.events")}");
            foreach (var id in _last.FiredEventIds)
                _out.WriteLine("  * " + _t.T(_packs.Events.Single(e => e.Id == id).TextKey));
        }
        ShowPoliticalEvents(_last.PoliticalEvents);
        ShowOverview();
        if (_tips && _nextTip <= 5) _out.WriteLine("\n" + _t.T($"ui.tip.{_nextTip++}"));
        if (_game.State.Turn >= _maxTurns) { ShowEnd(); return true; }
        return false;
    }

    private void DoSet(string[] args)
    {
        if (args.Length < 2) { _out.WriteLine(_t.T("ui.err.usage_set")); return; }
        var policy = Resolve(args[0], _policies, p => p.Id, numbers: true);
        if (policy == null) { _out.WriteLine(_t.T("ui.err.unknown_item", args[0])); return; }
        string text = args[1].Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        { _out.WriteLine(_t.T("ui.err.not_a_number", args[1])); return; }
        var result = _game.Apply(new SetPolicy(policy.Id, value));
        if (result.Accepted) _out.WriteLine(_t.T("ui.set.ok", _t.Name(policy.Id), Fmt.Policy(policy, value, _t)));
        else
        {
            string key = "ui." + (result.ErrorKey ?? "error.unknown_action").Replace("error.", "err.");
            _out.WriteLine(_t.T(key));
            if (result.ErrorKey == "error.value_out_of_range")
                _out.WriteLine("  " + _t.T("ui.range", Fmt.Policy(policy, policy.Min, _t), Fmt.Policy(policy, policy.Max, _t)));
        }
    }

    private (ValueDef? Value, PolicyDef? Policy) FindItem(string[] args)
    {
        string token = JoinArgs(args, 0);
        var v = Resolve(token, _values, x => x.Id, numbers: true);
        if (v != null) return (v, null);
        var p = Resolve(token, _policies, x => x.Id, numbers: false);
        return (null, p);
    }

    private void DoWhy(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.unknown_item", "")); return; }
        var (value, policy) = FindItem(args);
        if (value == null && policy == null) { _out.WriteLine(_t.T("ui.err.unknown_item", JoinArgs(args, 0))); return; }
        string id = value?.Id ?? policy!.Id;
        _out.WriteLine($"\n{_t.T("ui.heading.why", _t.Name(id))}");
        var change = _last?.Changes.FirstOrDefault(c => c.ValueId == id);
        if (value == null || change == null) { _out.WriteLine("  " + _t.T("ui.why.none")); return; }
        _out.WriteLine("  " + _t.T("ui.info.change", Fmt.Value(value, change.Before), Fmt.Value(value, change.After)));
        var lines = change.Contributions
            .Where(c => Math.Abs(c.Amount) > 1e-9 * Math.Max(1.0, Math.Abs(change.Before)))
            .OrderByDescending(c => Math.Abs(c.Amount)).ThenBy(c => c.EdgeId, StringComparer.Ordinal).ToList();
        foreach (var c in lines)
            _out.WriteLine("  " + _t.T("ui.why.line", _t.Name(c.FromId), _t.T(c.Amount > 0 ? "ui.why.up" : "ui.why.down")));
        if (lines.Count == 0 && value.Owner == null) _out.WriteLine("  " + _t.T("ui.why.none"));
        if (value.Owner != null) _out.WriteLine("  " + _t.T("ui.why.model"));
    }

    private void DoInfo(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.unknown_item", "")); return; }
        var (value, policy) = FindItem(args);
        if (value == null && policy == null) { _out.WriteLine(_t.T("ui.err.unknown_item", JoinArgs(args, 0))); return; }
        string id = value?.Id ?? policy!.Id;
        _out.WriteLine($"\n{_t.Name(id)}");
        _out.WriteLine("  " + (_t.Description(id) ?? _t.Name(id)));
        if (value != null)
            _out.WriteLine("  " + _t.T("ui.start_value", Fmt.Value(value, _scenario.StartValues.GetValueOrDefault(id, value.Initial))) +
                           $"   ->   {Fmt.Value(value, Current.Values[id])}");
        else
            _out.WriteLine("  " + _t.T("ui.range", Fmt.Policy(policy!, policy!.Min, _t), Fmt.Policy(policy, policy.Max, _t)) +
                           $"   (now {Fmt.Policy(policy, Current.Policies[id], _t)})");
    }

    private void DoSave(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.file", "", "?")); return; }
        string file = JoinArgs(args, 0);
        try { File.WriteAllText(file, _game.Save().ToYaml()); _out.WriteLine(_t.T("ui.saved", file)); }
        catch (Exception e) { _out.WriteLine(_t.T("ui.err.file", file, e.Message)); }
    }

    private void DoLoad(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.file", "", "?")); return; }
        string file = JoinArgs(args, 0);
        try
        {
            var game = Simulation.Load(_packs, SaveData.FromYaml(File.ReadAllText(file)));
            _game = game; _last = null;
            _gdpHistory.Clear();
            _gdpHistory.Add(Current.Values.GetValueOrDefault(MacroModelV1.Ids.Gdp));
            _out.WriteLine(_t.T("ui.loaded", file));
            ShowTurnHeader();
            ShowOverview();
        }
        catch (Exception e) { _out.WriteLine(_t.T("ui.err.file", file, e.Message)); }
    }

    // ---- screens ---------------------------------------------------------------

    private void ShowTurnHeader()
    {
        int turn = _game.State.Turn, tpy = _scenario.TurnsPerYear;
        _out.WriteLine($"\n{_t.T("ui.date", _scenario.StartYear + turn / tpy, turn % tpy + 1)}   ({turn}/{_maxTurns})");
    }

    private void Line(string label, string value) => _out.WriteLine("  " + label.PadRight(Label) + value);

    private double VoterMood()
    {
        double total = 0, weight = 0;
        foreach (var g in _packs.VoterGroups)
        {
            double w = g.Size * g.Weight;
            total += w * Current.Satisfaction.GetValueOrDefault(g.Id);
            weight += w;
        }
        return weight > 0 ? total / weight : 0;
    }

    private string MoodLabel(double s) =>
        _t.T("ui.mood." + (s < -0.6 ? 1 : s < -0.2 ? 2 : s < 0.2 ? 3 : s < 0.6 ? 4 : 5));

    private void ShowOverview()
    {
        var v = Current.Values;
        _out.WriteLine($"\n{_t.T("ui.heading.overview")}");
        if (v.TryGetValue(MacroModelV1.Ids.Gdp, out var gdp))
        {
            Line(_t.T("ui.line.gdp"), Fmt.Value(_valueById[MacroModelV1.Ids.Gdp], gdp));
            string growth = _gdpHistory.Count > 4 && _gdpHistory[^5] > 0
                ? Fmt.Signed((gdp / _gdpHistory[^5] - 1) * 100) + " %" : "n/a";
            Line(_t.T("ui.line.growth"), growth);
        }
        if (v.TryGetValue(MacroModelV1.Ids.Unemployment, out var u))
            Line(_t.T("ui.line.unemployment"), Fmt.Value(_valueById[MacroModelV1.Ids.Unemployment], u));
        if (v.TryGetValue(MacroModelV1.Ids.Inflation, out var infl))
            Line(_t.T("ui.line.inflation"), Fmt.Value(_valueById[MacroModelV1.Ids.Inflation], infl));
        if (v.TryGetValue(MacroModelV1.Ids.Debt, out var debt) && v.TryGetValue(MacroModelV1.Ids.Gdp, out var y) && y > 0)
        {
            Line(_t.T("ui.line.debt_ratio"), Fmt.Number(debt / y * 100, "0") + " %");
            if (v.TryGetValue(MacroModelV1.Ids.Revenue, out var rev) && v.TryGetValue(MacroModelV1.Ids.Spending, out var spend)
                && v.TryGetValue(MacroModelV1.Ids.InterestRate, out var rate))
            {
                double interest = debt * (rate - infl) / 100.0;
                Line(_t.T("ui.line.budget"), Fmt.Signed((rev - spend - interest) / y * 100) + " %");
            }
        }
        double mood = VoterMood();
        Line(_t.T("ui.line.mood"), $"{MoodLabel(mood).PadRight(11)} {Fmt.Bar(mood)}");
        ShowPolitics();
    }

    private void ShowPolitics()
    {
        var pol = Current.Politics;
        if (pol == null) return;
        _out.WriteLine($"\n{_t.T("ui.heading.politics")}");
        Line(_t.T("ui.line.next_election"), pol.TurnsToElection <= 1
            ? _t.T("ui.politics.election_next") : _t.T("ui.politics.election_in", pol.TurnsToElection));
        _out.WriteLine("  " + _t.T("ui.politics.polls"));
        foreach (var (id, share) in pol.Support.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string tag = id == pol.PlayerParty ? "  <- " + _t.T("ui.politics.you") : "";
            string gov = pol.GovernmentParties.Contains(id) ? "*" : " ";
            _out.WriteLine($"   {gov} {_t.Name(id),-26}{ShareBar(share)} {Fmt.Number(share * 100),5} %{tag}");
        }
        int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(id => pol.Seats.GetValueOrDefault(id));
        double fraction = _packs.Countries.First(c => c.Politics != null).Politics!.MajorityFraction;
        string verdict = _t.T(held > total * fraction ? "ui.politics.majority" : "ui.politics.no_majority");
        Line(_t.T("ui.politics.government"), $"{_t.T("ui.politics.seats_of", held, total)}  ({verdict})");
        if (pol.Leader != null) Line(_t.T("ui.politics.leader"), _t.Name(pol.Leader));
        ShowFormationStatus(pol);
        if (pol.Phase == Phase.Governing && pol.Loyalty is { Count: > 0 }) ShowCoalitionPanel(pol, full: false);
    }

    private string LoyaltyWord(CountryPolitics pol, string party, double loyalty) =>
        _t.T(pol.Ultimatums?.Any(u => u.PartyId == party) == true ? "ui.loyalty.ultimatum" : loyalty < 60 ? "ui.loyalty.uneasy" : "ui.loyalty.stable");

    private void ShowCoalitionPanel(CountryPolitics pol, bool full)
    {
        _out.WriteLine($"\n{_t.T("ui.heading.coalition")}");
        foreach (var (party, loyalty) in pol.Loyalty!)
        {
            _out.WriteLine($"  {_t.Name(party),-24}{_t.T("ui.coalition.loyalty", Fmt.Number(loyalty, "0"))}  {Fmt.Bar((loyalty - 50) / 50.0, 5)}  {LoyaltyWord(pol, party, loyalty)}");
            if (pol.Ultimatums?.FirstOrDefault(u => u.PartyId == party) is { } u)
            {
                var d = u.DemandId == null ? null : DemandOf(party, u.DemandId);
                _out.WriteLine("      " + _t.T("ui.coalition.ultimatum", u.TurnsLeft, d == null ? "" : DemandText(d)));
            }
            if (!full) continue;
            var mine = pol.Commitments?.Where(c => c.PartyId == party).ToList() ?? new();
            if (mine.Count == 0) _out.WriteLine("      " + _t.T("ui.coalition.no_commitments"));
            foreach (var c in mine)
            {
                var d = DemandOf(party, c.DemandId);
                string status = c.Progress >= 0.999 ? "ui.commitment.done" : c.Progress + 0.05 >= c.Expected ? "ui.commitment.on_track" : "ui.commitment.late";
                _out.WriteLine($"      [{_t.T(status)}] {(d == null ? c.DemandId : DemandText(d))}  ({Fmt.Number(c.Progress * 100, "0")} % / {Fmt.Number(c.Expected * 100, "0")} %)");
            }
        }
    }

    private static string ShareBar(double share, int width = 20) =>
        new string('#', (int)Math.Round(Math.Clamp(share / 0.4, 0, 1) * width)).PadRight(width);

    private void ShowPoliticalEvents(IReadOnlyList<PoliticalEvent> events)
    {
        bool header = false;
        foreach (var e in events)
        {
            if (e is ElectionHeld held) { ShowElectionResult(held); continue; }
            string? text = e switch
            {
                FormationStarted f => _t.T("ui.event.formation_started", _t.Name(f.Formateur)),
                FormationFailed f => _t.T("ui.event.formation_failed", _t.Name(f.Formateur)),
                OfferMade o => _t.T("ui.event.offer_made", _t.Name(o.Proposal.Formateur)),
                OfferLapsed => _t.T("ui.event.offer_lapsed"),
                GovernmentFormed g => _t.T("ui.event.government_formed", string.Join(", ", g.Government.Parties.Select(_t.Name)), _t.Name(g.Government.Leader)),
                EarlyElectionsCalled => _t.T("ui.event.early_elections"),
                LoyaltyWarning w => w.DemandId is { } wd && DemandOf(w.PartyId, wd) is { } wdef
                    ? _t.T("ui.event.loyalty_warning_demand", _t.Name(w.PartyId), DemandText(wdef))
                    : _t.T("ui.event.loyalty_warning", _t.Name(w.PartyId)),
                Ultimatum u => u.DemandId is { } ud && DemandOf(u.PartyId, ud) is { } udef
                    ? _t.T("ui.event.ultimatum_demand", _t.Name(u.PartyId), u.TurnsLeft, DemandText(udef))
                    : _t.T("ui.event.ultimatum", _t.Name(u.PartyId), u.TurnsLeft),
                PartnerLeft l => _t.T("ui.event.partner_left", _t.Name(l.PartyId)),
                GovernmentFell => _t.T("ui.event.government_fell"),
                GovernmentMeasure m when _policyById.TryGetValue(m.PolicyId, out var mp) =>
                    _t.T("ui.event.government_measure", _t.Name(m.PolicyId), Fmt.Policy(mp, m.From, _t), Fmt.Policy(mp, m.To, _t)),
                _ => null,
            };
            if (text == null) continue;
            if (!header) { _out.WriteLine($"\n{_t.T("ui.heading.political_events")}"); header = true; }
            _out.WriteLine("  * " + text);
        }
    }

    private void ShowElectionResult(ElectionHeld e)
    {
        _out.WriteLine($"\n{_t.T("ui.politics.result_heading")}");
        foreach (var (id, seats) in e.Seats.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string votes = Fmt.Number(e.Votes[id] * 100) + " %";
            _out.WriteLine("  " + (seats > 0
                ? _t.T("ui.politics.result_line", _t.Name(id), seats, votes)
                : _t.T("ui.politics.result_out", _t.Name(id), votes)));
        }
    }

    // ---- government formation --------------------------------------------------

    private readonly SortedSet<string> _draftPartners = new(StringComparer.Ordinal);
    private readonly List<AgreedDemand> _draftAgreements = new();

    private List<PartyDef> SeatedParties()
    {
        var seats = Current.Politics?.Seats;
        return _packs.Parties.Where(p => (seats?.GetValueOrDefault(p.Id) ?? 0) > 0).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

    private DemandDef? DemandOf(string partyId, string demandId) =>
        _packs.Parties.FirstOrDefault(p => p.Id == partyId)?.Demands.FirstOrDefault(d => d.Id == demandId);

    private string DemandText(DemandDef d)
    {
        string target = d.Kind == "policy" && _policyById.TryGetValue(d.Subject, out var pol) ? Fmt.Policy(pol, d.Target, _t)
            : _valueById.TryGetValue(d.Subject, out var val) ? Fmt.Value(val, d.Target) : Fmt.Number(d.Target);
        return $"{_t.Name(d.Subject)} {d.Op} {target}";
    }

    private void ShowFormationStatus(CountryPolitics pol)
    {
        if (pol.Phase == Phase.Caretaker)
        {
            _out.WriteLine("  " + _t.T("ui.formation.caretaker"));
            return;
        }
        if (pol.Phase != Phase.Formation || pol.Formation == null) return;
        var f = pol.Formation;
        _out.WriteLine("  " + (f.PlayerIsFormateur
            ? _t.T("ui.formation.you_form", f.TurnsLeft)
            : _t.T("ui.formation.ai_forms", _t.Name(f.Formateur), f.Attempt)));
        if (f.Offer != null)
        {
            _out.WriteLine("  " + _t.T("ui.formation.offer", _t.Name(f.Offer.Formateur), string.Join(", ", f.Offer.Partners.Select(_t.Name))));
            foreach (var a in f.Offer.Agreements)
            {
                var d = _packs.Parties.First(p => p.Id == a.PartyId).Demands.First(x => x.Id == a.DemandId);
                _out.WriteLine($"      {_t.Name(a.PartyId)}: {DemandText(d)}");
            }
            _out.WriteLine("  " + _t.T("ui.formation.offer_hint"));
        }
        else if (f.PlayerIsFormateur) _out.WriteLine("  " + _t.T("ui.formation.form_hint"));
    }

    private void ShowCoalition()
    {
        var pol = Current.Politics;
        if (pol?.Formation is not { PlayerIsFormateur: true })
        {
            if (pol is { Phase: Phase.Governing, Loyalty.Count: > 0 }) ShowCoalitionPanel(pol, full: true);
            else _out.WriteLine(_t.T("ui.err.not_formateur"));
            return;
        }
        var rules = _packs.Countries.First(c => c.Politics != null).Politics!;
        int total = pol.Seats.Values.Sum();
        int held = pol.Seats.GetValueOrDefault(pol.PlayerParty) + _draftPartners.Sum(p => pol.Seats.GetValueOrDefault(p));
        _out.WriteLine($"\n{_t.T("ui.formation.heading")}");
        _out.WriteLine("  " + _t.T("ui.formation.majority_needed", (int)Math.Floor(rules.MajorityFraction * total), held, rules.ProgramSlots));
        var parties = SeatedParties();
        for (int i = 0; i < parties.Count; i++)
        {
            var p = parties[i];
            string mark = p.Id == pol.PlayerParty ? "*" : _draftPartners.Contains(p.Id) ? "x" : " ";
            _out.WriteLine($"  [{mark}] {i + 1,2}. {_t.Name(p.Id),-24}" +
                _t.T("ui.formation.party_line", pol.Seats[p.Id], Fmt.Number(p.Price * 0.85)));
            if (p.Id == pol.PlayerParty) continue;
            for (int d = 0; d < p.Demands.Count; d++)
            {
                bool agreed = _draftAgreements.Any(a => a.PartyId == p.Id && a.DemandId == p.Demands[d].Id);
                _out.WriteLine($"        [{(agreed ? "x" : " ")}] {d + 1}) {DemandText(p.Demands[d])}  ({_t.T("ui.formation.weight", Fmt.Number(p.Demands[d].Weight))})");
            }
        }
        _out.WriteLine("  " + _t.T("ui.formation.coalition_help"));
    }

    private PartyDef? ResolveParty(string token)
    {
        var parties = SeatedParties();
        return Resolve(token, parties, p => p.Id, numbers: true);
    }

    private void DoInvite(string[] args, bool add)
    {
        var pol = Current.Politics;
        if (pol?.Formation is not { PlayerIsFormateur: true }) { _out.WriteLine(_t.T("ui.err.not_formateur")); return; }
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.unknown_item", "")); return; }
        var party = ResolveParty(JoinArgs(args, 0));
        if (party == null || party.Id == pol.PlayerParty) { _out.WriteLine(_t.T("ui.err.unknown_item", JoinArgs(args, 0))); return; }
        if (add) _draftPartners.Add(party.Id);
        else { _draftPartners.Remove(party.Id); _draftAgreements.RemoveAll(a => a.PartyId == party.Id); }
        ShowCoalition();
    }

    private void DoAgree(string[] args)
    {
        var pol = Current.Politics;
        if (pol?.Formation is not { PlayerIsFormateur: true }) { _out.WriteLine(_t.T("ui.err.not_formateur")); return; }
        if (args.Length < 2 || !int.TryParse(args[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        { _out.WriteLine(_t.T("ui.err.usage_agree")); return; }
        var party = ResolveParty(string.Join(" ", args[..^1]));
        if (party == null || party.Id == pol.PlayerParty || n < 1 || n > party.Demands.Count)
        { _out.WriteLine(_t.T("ui.err.unknown_item", string.Join(" ", args))); return; }
        var key = new AgreedDemand(party.Id, party.Demands[n - 1].Id);
        if (!_draftAgreements.Remove(key)) _draftAgreements.Add(key);
        _draftPartners.Add(party.Id);
        ShowCoalition();
    }

    private void DoAuto()
    {
        var s = _game.SuggestCoalition();
        if (s == null) { _out.WriteLine(_t.T("ui.err.not_formateur")); return; }
        Report(_game.Apply(new ProposeCoalition(s.Partners, s.Agreements)), "ui.formation.proposed");
    }

    private void DoSubmit() =>
        Report(_game.Apply(new ProposeCoalition(_draftPartners.ToList(), _draftAgreements.ToList())), "ui.formation.proposed");

    /// <summary>Prints the outcome of an action: a success text, or the error (with the refusal detail).</summary>
    private void Report(ActionResult result, string successKey)
    {
        if (result.Accepted)
        {
            _draftPartners.Clear(); _draftAgreements.Clear();
            _out.WriteLine(_t.T(successKey));
            ShowPolitics();
            return;
        }
        _out.WriteLine(_t.T("ui." + (result.ErrorKey ?? "error.unknown_action").Replace("error.", "err.")));
        if (result.ErrorKey?.StartsWith("error.refused.", StringComparison.Ordinal) == true && result.Detail is { Length: > 1 } detail)
        {
            var split = detail.Split(':'); // "base:party:demand" or "base:party:"
            if (split.Length >= 3)
            {
                string partyId = split[0] + ":" + split[1];
                string demand = split[2];
                var def = _packs.Parties.FirstOrDefault(p => p.Id == partyId);
                var dd = def?.Demands.FirstOrDefault(d => d.Id == demand);
                _out.WriteLine("  " + _t.Name(partyId) + (dd != null ? ": " + DemandText(dd) : ""));
            }
        }
    }

    private void ShowPolicies()
    {
        _out.WriteLine($"\n{_t.T("ui.heading.policies")}");
        var pending = _game.PendingPolicies;
        int turn = _game.State.Turn;
        for (int i = 0; i < _policies.Count; i++)
        {
            var p = _policies[i];
            var sb = new StringBuilder($"  {i + 1,2}. {_t.Name(p.Id),-34}{Fmt.Policy(p, Current.Policies[p.Id], _t),-16}");
            sb.Append($"({_t.T("ui.range", Fmt.Number(p.Min), Fmt.Number(p.Max))})");
            var queued = pending.LastOrDefault(x => x.PolicyId == p.Id);
            if (queued != null)
            {
                int quarters = queued.EffectiveTurn - turn + 1;
                sb.Append("  " + (quarters <= 1
                    ? _t.T("ui.pending.now", Fmt.Policy(p, queued.Value, _t))
                    : _t.T("ui.pending.later", Fmt.Policy(p, queued.Value, _t), quarters)));
            }
            _out.WriteLine(sb.ToString());
        }
    }

    private void ShowVoters()
    {
        _out.WriteLine($"\n{_t.T("ui.heading.voters")}");
        foreach (var g in _packs.VoterGroups.OrderByDescending(g => g.Size).ThenBy(g => g.Id, StringComparer.Ordinal))
        {
            double s = Current.Satisfaction.GetValueOrDefault(g.Id);
            _out.WriteLine($"  {_t.Name(g.Id),-32}{Fmt.Number(g.Size * 100, "0"),3} %  {MoodLabel(s),-11} {Fmt.Bar(s)}");
        }
    }

    private void ShowValues()
    {
        _out.WriteLine();
        for (int i = 0; i < _values.Count; i++)
        {
            var v = _values[i];
            _out.WriteLine($"  {i + 1,2}. {_t.Name(v.Id),-34}{Fmt.Value(v, Current.Values[v.Id])}");
        }
    }

    private void ShowHelp()
    {
        _out.WriteLine($"\n{_t.T("ui.heading.help")}");
        foreach (var line in _t.T("ui.help.body").Split('\n')) _out.WriteLine("  " + line);
    }

    private void ShowEnd()
    {
        var v = Current.Values;
        int turn = _game.State.Turn, years = turn / _scenario.TurnsPerYear;
        double mood = VoterMood();
        double ratio = _startGdp > 0 && v.TryGetValue(MacroModelV1.Ids.Gdp, out var g) ? g / _startGdp : 1.0;
        double debt = v.TryGetValue(MacroModelV1.Ids.Debt, out var d) && v.TryGetValue(MacroModelV1.Ids.Gdp, out var y) && y > 0 ? d / y * 100 : 0;
        _out.WriteLine($"\n=== {_t.T("ui.end.heading")} ===");
        _out.WriteLine(_t.T("ui.end.summary", years, MoodLabel(mood), Fmt.Number(ratio, "0.0"), Fmt.Number(debt, "0") + " %"));
        _out.WriteLine(_t.T(mood >= 0.2 ? "ui.end.good" : mood >= -0.3 ? "ui.end.ok" : "ui.end.bad"));
    }
}
