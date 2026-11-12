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
        ShowElectionResults(_last.PoliticalEvents);
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
    }

    private static string ShareBar(double share, int width = 20) =>
        new string('#', (int)Math.Round(Math.Clamp(share / 0.4, 0, 1) * width)).PadRight(width);

    private void ShowElectionResults(IReadOnlyList<PoliticalEvent> events)
    {
        foreach (var e in events.OfType<ElectionHeld>())
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
