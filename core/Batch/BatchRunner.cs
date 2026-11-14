using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Economy;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Batch;

/// <summary>Plays many games with a strategy (seeds 1..N) and reports stability problems.</summary>
public static class BatchRunner
{
    private const int CeilingTurns = 400;
    private const int CeilingGames = 10;

    public static BatchReport Run(PackSet packs, string scenarioId, int games, int turns, IStrategy strategy,
        StabilityThresholds? thresholds = null)
    {
        var th = thresholds ?? new StabilityThresholds();
        int nan = 0, rate = 0, collapse = 0, explosion = 0, spiral = 0;
        var notes = new List<string>();
        var collapsedBy = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        var explodedBy = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);

        var shortFlags = new Dictionary<int, Flags>();
        for (int i = 1; i <= games; i++)
        {
            var flags = PlayOne(packs, scenarioId, (ulong)i, turns, strategy, th, null);
            shortFlags[i] = flags;
            if (flags.NanOrInf) nan++;
            if (flags.ExtremeRate) rate++;
            if (flags.Collapse) collapse++;
            if (flags.Explosion) explosion++;
            if (flags.DebtSpiral) spiral++;
            foreach (var id in flags.CollapsedIds) AddSeed(collapsedBy, id, i);
            foreach (var id in flags.ExplodedIds) AddSeed(explodedBy, id, i);
        }
        foreach (var (id, seeds) in collapsedBy) notes.Add($"Collapse: {id} in {seeds.Count} game(s), first seeds: {string.Join(",", seeds.Take(5))}.");
        foreach (var (id, seeds) in explodedBy) notes.Add($"Explosion: {id} in {seeds.Count} game(s), first seeds: {string.Join(",", seeds.Take(5))}.");

        bool ceiling = false;
        if (strategy is PassiveStrategy)
        {
            int n = Math.Min(games, CeilingGames);
            for (int i = 1; i <= n; i++)
            {
                var gdp = new List<double>();
                var longFlags = PlayOne(packs, scenarioId, (ulong)i, CeilingTurns, strategy, th, gdp);
                if (StabilityChecks.DetectCeiling(gdp)) { ceiling = true; notes.Add($"GDP looks capped in game seed {i}."); }
                // Problems that only show up in the long run count too (once per game and category).
                var s0 = shortFlags[i];
                if (longFlags.NanOrInf && !s0.NanOrInf) { nan++; notes.Add($"NaN/Inf after turn {turns} in long run, seed {i}."); }
                if (longFlags.ExtremeRate && !s0.ExtremeRate) { rate++; notes.Add($"Extreme growth after turn {turns} in long run, seed {i}."); }
                if (longFlags.Collapse && !s0.Collapse) { collapse++; notes.Add($"Collapse after turn {turns} in long run, seed {i}: {string.Join(",", longFlags.CollapsedIds)}."); }
                if (longFlags.Explosion && !s0.Explosion) { explosion++; notes.Add($"Explosion after turn {turns} in long run, seed {i}: {string.Join(",", longFlags.ExplodedIds)}."); }
                if (longFlags.DebtSpiral && !s0.DebtSpiral) { spiral++; notes.Add($"Debt spiral after turn {turns} in long run, seed {i}."); }
            }
            notes.Add($"No-ceiling check: {n} passive game(s) x {CeilingTurns} turns.");
        }
        else notes.Add("No-ceiling check runs for the passive strategy only.");

        if (spiral > 0) notes.Add($"Debt spiral (debt above {th.DebtSpiralRatio:0.#}x GDP) in {spiral} game(s); informational.");
        return new BatchReport(games, turns, strategy.Name, nan, rate, collapse, explosion, spiral, ceiling, notes);
    }

    private static void AddSeed(SortedDictionary<string, List<int>> map, string id, int seed)
    {
        if (!map.TryGetValue(id, out var list)) map[id] = list = new List<int>();
        list.Add(seed);
    }

    private readonly record struct Flags(bool NanOrInf, bool ExtremeRate, bool Collapse, bool Explosion, bool DebtSpiral,
        IReadOnlyCollection<string> CollapsedIds, IReadOnlyCollection<string> ExplodedIds);

    private static Flags PlayOne(PackSet packs, string scenarioId, ulong seed, int turns, IStrategy strategy,
        StabilityThresholds th, List<double>? gdpSeries)
    {
        var game = Simulation.NewGame(packs, scenarioId, seed);
        var strategyRng = new Pcg32(seed, 99);
        var country = game.State.Countries.Single();
        var initial = country.Value.Values;
        var units = packs.Values.ToDictionary(v => v.Id, v => v.Unit);
        double dt = 1.0 / packs.GetScenario(scenarioId).TurnsPerYear;
        bool nan = false, rate = false, spiral = false;
        var collapsed = new SortedSet<string>(StringComparer.Ordinal);
        var exploded = new SortedSet<string>(StringComparer.Ordinal);
        double prevGdp = initial.TryGetValue(MacroModelV1.Ids.Gdp, out var g0) ? g0 : double.NaN;
        gdpSeries?.Add(prevGdp);

        for (int t = 0; t < turns; t++)
        {
            foreach (var action in strategy.Choose(game.State, packs, strategyRng)) game.Apply(action);
            var report = game.EndTurn();
            if (report.Warnings.Count > 0) nan = true;
            var values = game.State.Countries.Single().Value.Values;
            foreach (var (id, v) in values)
            {
                if (!double.IsFinite(v)) { nan = true; continue; }
                if (units[id] == "percent")
                {
                    if (v < th.PercentMin) collapsed.Add(id);
                    if (v > th.PercentMax) exploded.Add(id);
                }
                else if (th.NominalIndices.Contains(id)) { /* cumulative nominal index: runaway inflation is flagged via the percent range */ }
                else if (th.NetPositions.Contains(id))
                {
                    if (values.TryGetValue(MacroModelV1.Ids.Gdp, out var gdpNow) && gdpNow > 0)
                    {
                        if (Math.Abs(v) / gdpNow > th.ExplosionRatio) exploded.Add(id);
                        if (id == MacroModelV1.Ids.Debt && v / gdpNow > th.DebtSpiralRatio) spiral = true;
                    }
                }
                else if (initial[id] > 0)
                {
                    double ratio = v / initial[id];
                    if (ratio < th.CollapseRatio) collapsed.Add(id);
                    if (Math.Abs(ratio) > th.ExplosionRatio) exploded.Add(id);
                }
            }
            if (values.TryGetValue(MacroModelV1.Ids.Gdp, out var gdp))
            {
                gdpSeries?.Add(gdp);
                if (prevGdp > 0 && Math.Abs(gdp / prevGdp - 1.0) / dt > th.MaxAnnualGdpGrowth) rate = true;
                prevGdp = gdp;
            }
        }
        return new Flags(nan, rate, collapsed.Count > 0, exploded.Count > 0, spiral, collapsed, exploded);
    }
}
