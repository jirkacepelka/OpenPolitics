using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Presentation;

/// <summary>
/// The course of a game turn by turn, for charts: every value, each voter group's satisfaction, overall mood, debt as a share of GDP,
/// each party's poll and each partner's loyalty. Series are aligned with <see cref="Turns"/>; a point is NaN where the thing did not exist
/// (loyalty outside a government).
/// </summary>
public sealed class History
{
    public const string Mood = "mood", DebtRatio = "debt_ratio", InterestPaid = "interest_paid";
    public static string ValueKey(string id) => "value:" + id;
    public static string GroupKey(string id) => "group:" + id;
    public static string PollKey(string id) => "poll:" + id;
    public static string LoyaltyKey(string id) => "loyalty:" + id;
    /// <summary>The setting of a policy in force.</summary>
    public static string PolicyKey(string id) => "policy:" + id;
    /// <summary>The share of a voter group that would vote for the player's party.</summary>
    public static string SupportKey(string id) => "support:" + id;

    private Game _game;
    private readonly PackSet _packs;
    private readonly List<int> _turns = new();
    private readonly SortedDictionary<string, List<double>> _series = new(StringComparer.Ordinal);

    public History(Game game, PackSet packs) { _game = game; _packs = packs; }

    public IReadOnlyList<int> Turns => _turns;
    public IEnumerable<string> Keys => _series.Keys;

    public IReadOnlyList<double> Series(string key) =>
        _series.TryGetValue(key, out var s) ? s : Array.Empty<double>();

    /// <summary>The last <paramref name="lastTurns"/> turns (all of them when 0) of a series with their turn numbers.</summary>
    public (IReadOnlyList<int> Turns, IReadOnlyList<double> Values) Window(string key, int lastTurns)
    {
        var all = Series(key);
        int count = Math.Min(all.Count, _turns.Count);
        int take = lastTurns <= 0 ? count : Math.Min(count, lastTurns + 1);
        return (_turns.Skip(count - take).Take(take).ToList(), all.Skip(count - take).Take(take).ToList());
    }

    /// <summary>Starts again from the game's current state (after loading a save).</summary>
    public void Reset(Game game)
    {
        _turns.Clear(); _series.Clear();
        _game = game;
        Record();
    }

    /// <summary>Takes one point per series from the game's present state.</summary>
    public void Record()
    {
        var state = _game.State;
        var c = state.Countries.Values.Single();
        var now = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, v) in c.Values) now[ValueKey(id)] = v;
        foreach (var (id, s) in c.Satisfaction) now[GroupKey(id)] = s;
        foreach (var (id, v) in c.Policies) now[PolicyKey(id)] = v;
        now[Mood] = Moods.Overall(_packs.VoterGroups, c.Satisfaction);
        if (c.Values.TryGetValue("base:debt", out var debt) && c.Values.TryGetValue("base:gdp", out var gdp) && gdp > 0)
            now[DebtRatio] = debt / gdp * 100.0;
        if (c.Values.TryGetValue("base:debt", out var d2) && c.Values.TryGetValue("base:interest_rate", out var rate) && c.Values.TryGetValue("base:inflation", out var infl))
            now[InterestPaid] = d2 * (rate - infl) / 100.0; // real interest paid on the debt per year, the figure the budget shows
        if (c.Politics is { } pol)
        {
            foreach (var (id, share) in pol.Support) now[PollKey(id)] = share;
            if (pol.Party?.GroupSupport is { } gs) foreach (var (id, v) in gs) now[SupportKey(id)] = v;
            if (pol.Loyalty != null) foreach (var (id, l) in pol.Loyalty) now[LoyaltyKey(id)] = l;
        }
        int before = _turns.Count;
        _turns.Add(state.Turn);
        foreach (var key in now.Keys.Where(k => !_series.ContainsKey(k)).ToList())
            _series[key] = Enumerable.Repeat(double.NaN, before).ToList();
        foreach (var (key, list) in _series) list.Add(now.TryGetValue(key, out var x) ? x : double.NaN);
    }
}
