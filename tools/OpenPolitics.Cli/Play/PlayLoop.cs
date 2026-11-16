using System.Globalization;
using System.Text;
using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;
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
        TextReader input, TextWriter output, string? playerParty = null)
    {
        _packs = packs; _scenarioId = scenarioId; _scenario = packs.GetScenario(scenarioId);
        _seed = seed; _maxTurns = maxTurns; _tips = tips; _in = input; _out = output;
        _t = new Localizer(packs, language) { NominalGdpSource = () => Current.Values.GetValueOrDefault(Core.Economy.MacroModelV1.Ids.GdpNominal) };
        _game = Simulation.NewGame(packs, scenarioId, seed, playerParty);
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

    /// <summary>A text in the variant for the player's role (decision 0015); the sandbox has no roles and reads the base text.</summary>
    private string RoleText(string key) => Current.Politics is { } pol ? new Texts(_packs, _t).ForRole(key, pol.PlayerRole) : _t.T(key);

    public int Run()
    {
        _out.WriteLine($"=== {_t.T("ui.title")} ===");
        ShowTurnHeader();
        ShowOverview();
        ShowPolicies();
        if (_tips) _out.WriteLine("\n" + RoleText("ui.tip.1"));
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
                case "index": DoIndex(cmd.Args); break;
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
                case "party": ShowParty(); break;
                case "criticize": DoCriticize(cmd.Args); break;
                case "stance": DoStance(cmd.Args); break;
                case "campaign": DoCampaign(cmd.Args); break;
                case "organize": Report(_game.Apply(new BuildOrganization()), "ui.party.organized", quiet: true); break;
                case "court": DoCourt(cmd.Args); break;
                case "propose": DoProposePolicy(cmd.Args); break;
                case "leave": Report(_game.Apply(new LeaveCoalition()), "ui.exit.leave_ok", quiet: true); break;
                case "dismiss": DoDismiss(cmd.Args); break;
                case "reshuffle": DoReshuffle(cmd.Args); break;
                case "confidence": Report(_game.Apply(new NoConfidence()), "ui.exit.confidence_ok", quiet: true); break;
                case "platform": DoPlatform(cmd.Args); break;
                case "priority": DoPriority(cmd.Args); break;
                case "promise": DoPromise(cmd.Args); break;
                case "support": DoAnswer(support: true); break;
                case "oppose": DoAnswer(support: false); break;
                case "announce": DoProposePolicy(cmd.Args); break; // decision 0015: an alias of propose
                case "ultimatum": DoUltimatum(cmd.Args); break;
                case "withdraw": Report(_game.Apply(new WithdrawUltimatum()), "ui.myult.withdrawn", quiet: true); break;
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
        ["party"] = "party", ["strana"] = "party",
        ["criticize"] = "criticize", ["kritizuj"] = "criticize",
        ["stance"] = "stance", ["postoj"] = "stance",
        ["campaign"] = "campaign", ["kampan"] = "campaign",
        ["organize"] = "organize", ["organizuj"] = "organize",
        ["court"] = "court", ["nabidka"] = "court", ["sbliz"] = "court",
        ["platform"] = "platform", ["program"] = "platform",
        ["index"] = "index", ["valorizace"] = "index",
        ["propose"] = "propose", ["navrhni"] = "propose",
        ["priority"] = "priority", ["priorita"] = "priority",
        ["promise"] = "promise", ["slib"] = "promise",
        ["support"] = "support", ["podpor"] = "support", ["podporit"] = "support",
        ["oppose"] = "oppose", ["proti"] = "oppose", ["nepodpor"] = "oppose",
        ["announce"] = "announce", ["ohlas"] = "announce", ["ohlasit"] = "announce",
        ["leave"] = "leave", ["odejit"] = "leave",
        ["dismiss"] = "dismiss", ["vyloucit"] = "dismiss",
        ["reshuffle"] = "reshuffle", ["prestavba"] = "reshuffle", ["prestavet"] = "reshuffle",
        ["ultimatum"] = "ultimatum",
        ["withdraw"] = "withdraw", ["stahnout"] = "withdraw", ["stahni"] = "withdraw",
        ["confidence"] = "confidence", ["nedoveru"] = "confidence", ["nedovera"] = "confidence",
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
        ShowProposal();
        ShowOwnUltimatum();
        ShowOverview();
        if (_tips && _nextTip <= 5) _out.WriteLine("\n" + RoleText($"ui.tip.{_nextTip++}"));
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
        value = Fmt.ToParameter(policy, value, _t); // typed in the shown unit (tax rate in %, spending in bn per year)
        double cost = _game.PolicyChangeCost(policy.Id, value);
        var result = _game.Apply(new SetPolicy(policy.Id, value));
        if (result.Accepted)
        {
            _out.WriteLine(_t.T("ui.set.ok", _t.Name(policy.Id), Fmt.Policy(policy, value, _t)));
            if (cost > 0 && Current.Politics?.Party is { } party)
                _out.WriteLine(_t.T("ui.set.cost", Fmt.Number(cost, "0"), Fmt.Number(Math.Floor(party.PoliticalCapital + 1e-9), "0")));
        }
        else
        {
            _out.WriteLine(new Texts(_packs, _t).Error(result));
            if (result.ErrorKey == "error.value_out_of_range")
                _out.WriteLine("  " + _t.T("ui.range", Fmt.Policy(policy, policy.Min, _t), Fmt.Policy(policy, policy.Max, _t)));
        }
    }

    private void DoIndex(string[] args)
    {
        if (args.Length < 2) { _out.WriteLine(_t.T("ui.err.usage_index")); return; }
        var policy = Resolve(args[0], _policies, p => p.Id, numbers: true);
        if (policy == null) { _out.WriteLine(_t.T("ui.err.unknown_item", args[0])); return; }
        string mode = args[1].ToLowerInvariant();
        var result = _game.Apply(new SetIndexation(policy.Id, mode));
        if (result.Accepted) _out.WriteLine(_t.T("ui.index.ok", _t.Name(policy.Id), _t.T("ui.index.mode." + mode)));
        else _out.WriteLine(_t.T("ui." + (result.ErrorKey ?? "error.unknown_action").Replace("error.", "err.")));
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
        if (pol.Party != null)
            Line(_t.T("ui.party.capital"), $"{Fmt.Number(pol.Party.PoliticalCapital)}   ({_t.T("ui.party.organisation")} {Fmt.Number(pol.Party.Organization * 100, "0")} %)");
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
                _out.WriteLine($"      [{_t.T(status)}] {(d == null ? c.DemandId : DemandText(d))}  ({Fmt.Number(c.Progress * 100 + 0.0, "0")} % / {Fmt.Number(c.Expected * 100, "0")} %)");
            }
        }
    }

    private static string ShareBar(double share, int width = 20) =>
        new string('#', (int)Math.Round(Math.Clamp(share / 0.4, 0, 1) * width)).PadRight(width);

    private void ShowPoliticalEvents(IReadOnlyList<PoliticalEvent> events)
    {
        bool header = false;
        var texts = new Texts(_packs, _t);
        foreach (var e in events)
        {
            if (e is ElectionHeld held) { ShowElectionResult(held); continue; }
            string? text = texts.Event(e, Current.Politics?.PlayerRole ?? Role.Leader); // one place for event wording, shared with the Godot client
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
                _t.T("ui.formation.party_line", pol.Seats[p.Id], Fmt.Number(CoalitionRules.RequiredWeight(p, pol.Party?.Relations.GetValueOrDefault(p.Id, 0.3) ?? 0.3))));
            if (p.Id == pol.PlayerParty) continue;
            for (int d = 0; d < p.Demands.Count; d++)
            {
                bool agreed = _draftAgreements.Any(a => a.PartyId == p.Id && a.DemandId == p.Demands[d].Id);
                _out.WriteLine($"        [{(agreed ? "x" : " ")}] {d + 1}) {DemandText(p.Demands[d])}  ({_t.T("ui.formation.weight", Fmt.Number(p.Demands[d].Weight))})");
            }
        }
        _out.WriteLine("  " + _t.T("ui.formation.coalition_help"));
    }

    // ---- the player's party -------------------------------------------------------

    private List<PartyDef> OtherParties() =>
        _packs.Parties.Where(p => p.Id != Current.Politics?.PlayerParty).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    private void ShowParty()
    {
        var pol = Current.Politics;
        if (pol?.Party is not { } ps) { _out.WriteLine(_t.T("ui.err.no_politics")); return; }
        _out.WriteLine($"\n{_t.T("ui.heading.party")}: {_t.Name(pol.PlayerParty)}  ({_t.T("ui.role." + pol.PlayerRole.ToString().ToLowerInvariant())})");
        Line(_t.T("ui.party.capital"), Fmt.Number(ps.PoliticalCapital));
        Line(_t.T("ui.party.organisation"), Fmt.Number(ps.Organization * 100, "0") + " %");
        Line(_t.T("ui.party.credibility"), Fmt.Number(ps.Credibility * 100, "0") + " %");
        Line(_t.T("ui.party.cohesion"), Fmt.Number(ps.Cohesion * 100, "0") + " %");
        Line(_t.T("ui.party.fiscal_blame"), Fmt.Number(ps.FiscalBlame, "0.0"));
        Line(_t.T("ui.party.fiscal_credit"), Fmt.Number(ps.FiscalCredit, "0.0") + (ps.FiscalCreditCapital > 0 ? $"  (+{Fmt.Number(ps.FiscalCreditCapital, "0.0")})" : ""));
        _out.WriteLine("  " + _t.T("ui.party.relations"));
        var others = OtherParties();
        for (int i = 0; i < others.Count; i++)
        {
            double r = ps.Relations.GetValueOrDefault(others[i].Id);
            _out.WriteLine($"   {i + 1,2}. {_t.Name(others[i].Id),-24}{Fmt.Number(r, "0.00")}  {Fmt.Bar(r * 2 - 1, 5)}");
        }
        if (_game.Compass.TryGetValue(pol.PlayerParty, out var spot))
            Line(_t.T("ui.party.compass"), $"{Fmt.Number(spot.X, "0.00")} / {Fmt.Number(spot.Y, "0.00")}  ({_t.T("ui.party.compass_hint")})");
        _out.WriteLine("  " + _t.T("ui.party.platform"));
        foreach (var (policy, target) in ps.Platform)
            if (_policyById.TryGetValue(policy, out var def))
                _out.WriteLine($"      {_t.Name(policy),-34}{Fmt.Policy(def, target, _t),-18}{_t.T("ui.party.priority")} {Fmt.Number(ps.Priorities?.GetValueOrDefault(policy, 1.0) ?? 1.0, "0.0")}");
        var ownDemands = _packs.Parties.FirstOrDefault(p => p.Id == pol.PlayerParty)?.Demands ?? (IReadOnlyList<DemandDef>)Array.Empty<DemandDef>();
        if (ownDemands.Count > 0)
        {
            _out.WriteLine("  " + _t.T("ui.party.promises"));
            var texts = new Texts(_packs, _t);
            for (int i = 0; i < ownDemands.Count; i++)
            {
                bool met = Texts.IsMet(ownDemands[i], Current);
                _out.WriteLine($"   {i + 1,2}. {texts.Demand(ownDemands[i])}  ({_t.T(met ? "ui.goal.met" : "ui.goal.unmet")})" + (ps.Promises?.Contains(ownDemands[i].Id) == true ? "  [" + _t.T("ui.party.promised") + "]" : ""));
                if (ps.PromiseDetails?.FirstOrDefault(p => p.DemandId == ownDemands[i].Id) is { } made)
                    foreach (var line in texts.PromiseStatus(made)) _out.WriteLine("       " + line);
            }
        }
        _out.WriteLine("  " + _t.T("ui.party.measures"));
        if (ps.Measures.Count == 0) _out.WriteLine("      " + _t.T("ui.party.no_measures"));
        for (int i = 0; i < ps.Measures.Count; i++)
        {
            var m = ps.Measures[i];
            if (!_policyById.TryGetValue(m.PolicyId, out var def)) continue;
            _out.WriteLine($"   {i + 1,2}. {_t.T("ui.party.measure_line", m.Turn, _t.Name(m.PolicyId), Fmt.Policy(def, m.From, _t), Fmt.Policy(def, m.To, _t))}" +
                           (m.StanceTaken ? "  [" + _t.T("ui.party.stance_taken") + "]" : ""));
        }
        _out.WriteLine("  " + new Texts(_packs, _t).GoalsHeading(pol.PlayerRole));
        double share = pol.Support.GetValueOrDefault(pol.PlayerParty);
        Line("    " + _t.T("ui.goal.poll"), $"{Fmt.Number(share * 100)} %  {ShareBar(share)}");
        if (pol.Loyalty is { Count: > 0 } loyalty)
            Line("    " + RoleText("ui.goal.fragility"), Fmt.Number(100 - loyalty.Values.Min(), "0") + " %");
        else Line("    " + RoleText("ui.goal.fragility"), "-");
        Line("    " + _t.T("ui.goal.relations"), Fmt.Number(ps.Relations.Values.DefaultIfEmpty(0).Average(), "0.00"));
        _out.WriteLine("  " + _t.T("ui.party.help"));
    }

    private void DoCriticize(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.unknown_item", "")); return; }
        var value = Resolve(JoinArgs(args, 0), _values, v => v.Id, numbers: true);
        if (value == null) { _out.WriteLine(_t.T("ui.err.unknown_item", JoinArgs(args, 0))); return; }
        Report(Supported(new Criticize(value.Id)), "ui.party.criticized", quiet: true, _t.Name(value.Id));
    }

    private void DoStance(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        { _out.WriteLine(_t.T("ui.err.usage_stance")); return; }
        var measures = Current.Politics?.Party?.Measures;
        if (measures == null || n < 1 || n > measures.Count) { _out.WriteLine(_t.T("ui.err.unknown_measure")); return; }
        string word = Localizer.Fold(args[1]);
        Stance? stance = word is "support" or "podpor" or "podporit" ? Stance.Support : word is "oppose" or "odmitni" or "odmitnout" ? Stance.Oppose : null;
        if (stance == null) { _out.WriteLine(_t.T("ui.err.usage_stance")); return; }
        Report(_game.Apply(new TakeStance(measures[n - 1].Id, stance.Value)), "ui.party.stance_ok", quiet: true);
    }

    private void DoCampaign(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.usage_campaign")); return; }
        int effort = 1;
        var nameArgs = args;
        if (args.Length > 1 && int.TryParse(args[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var e)) { effort = e; nameArgs = args[..^1]; }
        var group = Resolve(string.Join(" ", nameArgs), _packs.VoterGroups, g => g.Id, numbers: false);
        if (group == null) { _out.WriteLine(_t.T("ui.err.unknown_item", string.Join(" ", nameArgs))); return; }
        Report(Supported(new Campaign(group.Id, effort)), "ui.party.campaign_ok", quiet: true, _t.Name(group.Id));
    }

    /// <summary>
    /// propose POLICY VALUE (decision 0015: the one way to propose; 'announce' is an alias) - a partner's proposal goes to the leader, anyone else's
    /// goes public. Shows who decides, how the voters' support moves and the credibility cost first.
    /// </summary>
    private void DoProposePolicy(string[] args)
    {
        if (args.Length < 2) { _out.WriteLine(_t.T("ui.err.usage_propose")); return; }
        var policy = Resolve(string.Join(" ", args[..^1]), _policies, p => p.Id, numbers: true);
        if (policy == null) { _out.WriteLine(_t.T("ui.err.unknown_item", string.Join(" ", args[..^1]))); return; }
        if (!double.TryParse(args[^1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
        { _out.WriteLine(_t.T("ui.err.not_a_number", args[^1])); return; }
        var preview = _game.PreviewProposal(policy.Id, target);
        if (preview is { ErrorKey: null })
            foreach (var line in new Texts(_packs, _t).ProposalPreviewLines(preview)) _out.WriteLine("  " + line);
        Report(Supported(new ProposePolicy(policy.Id, target)), preview?.ToLeader == true ? "ui.exit.propose_ok" : "ui.announce.ok", quiet: true, _t.Name(policy.Id));
    }

    /// <summary>The proposal waiting for the player's answer (decision 0014), with what an answer means.</summary>
    private void ShowProposal()
    {
        if (Current.Politics?.Proposals?.FirstOrDefault(p => p.Answer == null) is not { } p) return;
        var texts = new Texts(_packs, _t);
        var (who, what) = texts.Proposal(p);
        _out.WriteLine($"\n{_t.T("ui.proposal.heading")}: {who} {what}");
        foreach (var line in texts.ProposalHints(p)) _out.WriteLine("  " + line);
        _out.WriteLine("  " + _t.T("ui.help.proposal"));
    }

    /// <summary>The party's open ultimatum to the leader (decision 0016): status and what to do.</summary>
    private void ShowOwnUltimatum()
    {
        if (Current.Politics is not { PlayerUltimatum: { } u, Leader: { } leader }) return;
        _out.WriteLine($"\n{_t.T("ui.myult.title")}: {new Texts(_packs, _t).UltimatumStatus(u, leader)}");
        _out.WriteLine("  " + _t.T("ui.help.ultimatum_open"));
    }

    /// <summary>ultimatum [check] demand ID|N [QUARTERS] / ultimatum [check] policy POLICY [QUARTERS] (decision 0016).</summary>
    private void DoUltimatum(string[] args)
    {
        var pol = Current.Politics;
        if (pol == null) { _out.WriteLine(_t.T("ui.err.no_politics")); return; }
        var texts = new Texts(_packs, _t);
        if (pol.PlayerUltimatum != null && args.Length == 0) { ShowOwnUltimatum(); return; }
        var list = args.ToList();
        bool check = list.Count > 0 && Localizer.Fold(list[0]) is "check" or "zkus" or "nahled";
        if (check) list.RemoveAt(0);
        if (list.Count < 2) { _out.WriteLine(_t.T("ui.err.usage_ultimatum")); return; }
        string kindWord = Localizer.Fold(list[0]);
        string? kind = kindWord is "demand" or "pozadavek" or "d" ? "demand" : kindWord is "policy" or "program" or "p" ? "policy" : null;
        if (kind == null) { _out.WriteLine(_t.T("ui.err.usage_ultimatum")); return; }
        int deadline = 4;
        if (list.Count > 2 && int.TryParse(list[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var q)) { deadline = q; list.RemoveAt(list.Count - 1); }
        string token = string.Join(" ", list.Skip(1));
        string? subject;
        if (kind == "demand")
        {
            var own = _packs.Parties.First(p => p.Id == pol.PlayerParty).Demands;
            subject = Resolve(token, own, d => d.Id, numbers: true)?.Id;
        }
        else subject = Resolve(token, _policies, p => p.Id, numbers: true)?.Id;
        if (subject == null) { _out.WriteLine(_t.T("ui.err.unknown_item", token)); return; }
        var preview = _game.PreviewUltimatum(kind, subject, deadline);
        if (preview == null) return;
        if (preview.ErrorKey == "error.not_partner") { _out.WriteLine(_t.T("ui.myult.only_partner")); return; }
        _out.WriteLine($"\n{_t.T("ui.myult.title")}");
        foreach (var line in texts.UltimatumPreviewLines(preview, pol.Leader ?? "")) _out.WriteLine("  " + line);
        if (preview.ErrorKey != null || check) return;
        Report(_game.Apply(new IssueUltimatum(kind, subject, deadline)), "ui.myult.ok", quiet: true, _t.Name(pol.Leader ?? ""));
    }

    private void DoAnswer(bool support)
    {
        if (Current.Politics?.Proposals?.FirstOrDefault(p => p.Answer == null) is not { } p) { _out.WriteLine(_t.T("ui.proposal.none")); return; }
        Report(_game.Apply(support ? new SupportProposal(p.Id) : new OpposeProposal(p.Id)), support ? "ui.proposal.ok_support" : "ui.proposal.ok_oppose", quiet: true, _t.Name(p.PolicyId));
    }

    private void DoDismiss(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.usage_dismiss")); return; }
        var party = Resolve(JoinArgs(args, 0), OtherParties(), p => p.Id, numbers: true);
        if (party == null) { _out.WriteLine(_t.T("ui.err.unknown_item", JoinArgs(args, 0))); return; }
        Report(_game.Apply(new DismissPartner(party.Id)), "ui.exit.dismiss_ok", quiet: true, _t.Name(party.Id));
    }

    /// <summary>reshuffle [check] drop PARTY... add PARTY... - the agreements for newcomers are the ones the AI proposes (decision 0013).</summary>
    private void DoReshuffle(string[] args)
    {
        var pol = Current.Politics;
        if (pol == null) { _out.WriteLine(_t.T("ui.err.no_politics")); return; }
        if (args.Length == 0)
        {
            _out.WriteLine(_t.T("ui.err.usage_reshuffle"));
            if (pol.Reshuffle is { } info)
                _out.WriteLine("  " + (info.CooldownLeft == 0 ? _t.T("ui.reshuffle.cooldown_now") : _t.T("ui.reshuffle.cooldown_left", info.CooldownLeft)));
            return;
        }
        bool check = false;
        string? mode = null;
        var drop = new List<string>(); var add = new List<string>();
        foreach (var token in args)
        {
            string word = Localizer.Fold(token);
            if (word is "check" or "zkus" or "nahled") { check = true; continue; }
            if (word is "drop" or "vyrad" or "vyhod") { mode = "drop"; continue; }
            if (word is "add" or "prizvi" or "pridej") { mode = "add"; continue; }
            var party = mode == null ? null : Resolve(token, OtherParties(), p => p.Id, numbers: false);
            if (party == null) { _out.WriteLine(_t.T("ui.err.usage_reshuffle")); _out.WriteLine(_t.T("ui.err.unknown_item", token)); return; }
            (mode == "drop" ? drop : add).Add(party.Id);
        }
        var agreements = _game.SuggestReshuffle(drop, add) ?? (IReadOnlyList<AgreedDemand>)Array.Empty<AgreedDemand>();
        var preview = _game.PreviewReshuffle(drop, add, agreements);
        if (preview.ErrorKey == "error.not_leader") { _out.WriteLine(_t.T("ui.client.reshuffle_only_leader")); return; }
        var texts = new Texts(_packs, _t);
        if (preview.ErrorKey != null) { _out.WriteLine(texts.Error(new ActionResult(false, preview.ErrorKey, preview.ErrorDetail))); return; }
        _out.WriteLine();
        _out.WriteLine(_t.T("ui.reshuffle.heading"));
        _out.WriteLine("  " + _t.T("ui.reshuffle.seats", preview.SeatsHeld, preview.SeatsNeeded, _t.T(preview.Majority ? "ui.politics.majority" : "ui.politics.no_majority")));
        foreach (var group in agreements.GroupBy(a => a.PartyId))
            _out.WriteLine("  " + _t.T("ui.reshuffle.demands", _t.Name(group.Key),
                string.Join("; ", group.Select(a => texts.DemandOf(a.PartyId, a.DemandId) is { } d ? texts.Demand(d) : a.DemandId))));
        _out.WriteLine("  " + (preview.Refusals.Count == 0 ? _t.T("ui.reshuffle.agree")
            : _t.T("ui.reshuffle.would_refuse", string.Join("; ", preview.Refusals.Select(r => (r.PartyId != null ? _t.Name(r.PartyId) + ": " : "") + texts.ReshuffleReason(r.Reason))))));
        _out.WriteLine("  " + _t.T("ui.reshuffle.cost", Fmt.Number(preview.Cost, "0"), Fmt.Number(PoliticsLimits.ReshuffleRefusalCost, "0"), PoliticsLimits.ReshuffleCooldownTurns));
        if (check) return;
        var result = _game.Apply(new ReshuffleCoalition(drop, add, agreements));
        _out.WriteLine(result.Accepted ? _t.T("ui.reshuffle.ok", Fmt.Number(preview.Cost, "0")) : texts.Error(result));
    }

    private void DoCourt(string[] args)
    {
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.unknown_item", "")); return; }
        var party = Resolve(JoinArgs(args, 0), OtherParties(), p => p.Id, numbers: true);
        if (party == null) { _out.WriteLine(_t.T("ui.err.unknown_item", JoinArgs(args, 0))); return; }
        Report(_game.Apply(new Court(party.Id)), "ui.party.court_ok", quiet: true, _t.Name(party.Id));
    }

    private void DoPlatform(string[] args)
    {
        if (args.Length < 2) { _out.WriteLine(_t.T("ui.err.usage_platform")); return; }
        var policy = Resolve(string.Join(" ", args[..^1]), _policies, p => p.Id, numbers: true);
        if (policy == null) { _out.WriteLine(_t.T("ui.err.unknown_item", string.Join(" ", args[..^1]))); return; }
        if (!double.TryParse(args[^1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
        { _out.WriteLine(_t.T("ui.err.not_a_number", args[^1])); return; }
        Report(_game.Apply(new ShiftPlatform(policy.Id, target)), "ui.party.platform_ok", quiet: true, _t.Name(policy.Id));
    }

    private void DoPriority(string[] args)
    {
        if (args.Length < 2) { _out.WriteLine(_t.T("ui.err.usage_priority")); return; }
        var policy = Resolve(string.Join(" ", args[..^1]), _policies, p => p.Id, numbers: true);
        if (policy == null) { _out.WriteLine(_t.T("ui.err.unknown_item", string.Join(" ", args[..^1]))); return; }
        if (!double.TryParse(args[^1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
        { _out.WriteLine(_t.T("ui.err.not_a_number", args[^1])); return; }
        Report(_game.Apply(new SetPriority(policy.Id, weight)), "ui.party.priority_ok", quiet: true, _t.Name(policy.Id));
    }

    private void DoPromise(string[] args)
    {
        var own = _packs.Parties.FirstOrDefault(p => p.Id == Current.Politics?.PlayerParty)?.Demands ?? (IReadOnlyList<DemandDef>)Array.Empty<DemandDef>();
        if (args.Length == 0) { _out.WriteLine(_t.T("ui.err.usage_promise")); return; }
        var demand = int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= own.Count
            ? own[n - 1] : own.FirstOrDefault(d => d.Id == args[0]);
        if (demand == null) { _out.WriteLine(_t.T("ui.err.unknown_item", args[0])); return; }
        Report(_game.Apply(new MakePromise(demand.Id)), "ui.party.promise_ok", quiet: true, new Texts(_packs, _t).Demand(demand));
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
    /// <summary>Applies an action and, when accepted, prints how far it moved the party's support per voter group (decision 0015).</summary>
    private ActionResult Supported(PlayerAction action)
    {
        var before = Current.Politics?.Party?.GroupSupport;
        var r = _game.Apply(action);
        if (r.Accepted && new Texts(_packs, _t).SupportChange(before, Current.Politics?.Party?.GroupSupport) is { } moved) _pendingNote = _t.T("ui.support.now", moved);
        return r;
    }

    private string? _pendingNote;

    private void Report(ActionResult result, string successKey, bool quiet = false, params object[] args)
    {
        if (result.Accepted)
        {
            _draftPartners.Clear(); _draftAgreements.Clear();
            _out.WriteLine(_t.T(successKey, args));
            if (_pendingNote != null) { _out.WriteLine("  " + _pendingNote); _pendingNote = null; }
            if (!quiet) ShowPolitics();
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
        _out.WriteLine($"\n{RoleText("ui.heading.policies")}");
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
            // decision 0015: besides the mood about the country, how many in the group would vote for the player's party (and the change)
            string support = Current.Politics?.Party is { GroupSupport: { } gs } ps && gs.TryGetValue(g.Id, out var now)
                ? $"  {_t.T("ui.voters.support", Fmt.Number(now * 100, "0.0"), Texts.Points(100 * (now - (ps.GroupSupportPrev?.GetValueOrDefault(g.Id, now) ?? now))))}" : "";
            _out.WriteLine($"  {_t.Name(g.Id),-32}{Fmt.Number(g.Size * 100, "0"),3} %  {MoodLabel(s),-11} {Fmt.Bar(s)}{support}");
        }
        if (Current.Politics is { } pol) _out.WriteLine("  " + _t.T("ui.voters.support_hint", _t.Name(pol.PlayerParty)));
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
        _out.WriteLine(RoleText(mood >= 0.2 ? "ui.end.good" : mood >= -0.3 ? "ui.end.ok" : "ui.end.bad"));
    }
}
