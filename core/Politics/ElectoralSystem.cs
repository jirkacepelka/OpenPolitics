using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>Turns votes (shares or counts per party id) into seats.</summary>
public interface IElectoralSystem
{
    int Seats { get; }

    /// <summary>Result has an entry for every party (0 for no seats), keys in ordinal order.</summary>
    IReadOnlyDictionary<string, int> Allocate(IReadOnlyDictionary<string, double> votes);

    /// <summary>
    /// Like <see cref="Allocate(IReadOnlyDictionary{string, double})"/> with a higher threshold for some lists (joint lists, decision 0019):
    /// <paramref name="thresholdFactor"/> multiplies the threshold of the list it names. Systems without a threshold ignore it.
    /// </summary>
    IReadOnlyDictionary<string, int> Allocate(IReadOnlyDictionary<string, double> votes, IReadOnlyDictionary<string, double>? thresholdFactor) => Allocate(votes);
}

/// <summary>Proportional representation: threshold, then a divisor method (D'Hondt, Sainte-Lague) or largest remainders (Hare quota).</summary>
public sealed class ProportionalSystem : IElectoralSystem
{
    private readonly double _threshold;
    private readonly string _method;

    public ProportionalSystem(int seats, double threshold, string method = "dhondt")
    {
        _method = method;
        if (seats <= 0) throw new ArgumentOutOfRangeException(nameof(seats));
        Seats = seats;
        _threshold = threshold;
    }

    public int Seats { get; }

    public IReadOnlyDictionary<string, int> Allocate(IReadOnlyDictionary<string, double> votes) => Allocate(votes, null);

    public IReadOnlyDictionary<string, int> Allocate(IReadOnlyDictionary<string, double> votes, IReadOnlyDictionary<string, double>? thresholdFactor)
    {
        var ids = votes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        double total = ids.Sum(i => votes[i]);
        if (!(total > 0)) throw new ArgumentException("Total votes must be positive.", nameof(votes));

        var eligible = ids.Where(i => votes[i] / total >= _threshold * (thresholdFactor?.GetValueOrDefault(i, 1.0) ?? 1.0)).ToList();
        if (eligible.Count == 0) eligible = ids; // nobody passes: ignore the threshold so all seats are filled

        var seats = ids.ToDictionary(i => i, _ => 0, StringComparer.Ordinal);
        if (_method == "hare")
        {
            double quota = eligible.Sum(i => votes[i]) / Seats;
            var remainder = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var id in eligible)
            {
                int whole = (int)Math.Floor(votes[id] / quota + 1e-12);
                seats[id] = whole;
                remainder[id] = votes[id] - whole * quota;
            }
            int left = Seats - seats.Values.Sum();
            foreach (var id in eligible.OrderByDescending(i => remainder[i]).ThenBy(i => i, StringComparer.Ordinal))
            {
                if (left <= 0) break;
                seats[id]++; left--;
            }
            while (left-- > 0) seats[eligible.OrderByDescending(i => remainder[i]).ThenBy(i => i, StringComparer.Ordinal).First()]++;
            return seats;
        }
        double step = _method == "sainte_lague" ? 2.0 : 1.0, first = _method == "sainte_lague" ? 1.0 : 1.0;
        for (int s = 0; s < Seats; s++)
        {
            string? best = null;
            double bestQuotient = -1;
            foreach (var id in eligible) // ordinal order, strict '>' keeps the lower id on ties
            {
                double q = votes[id] / (step * seats[id] + first);
                if (q > bestQuotient) { bestQuotient = q; best = id; }
            }
            seats[best!]++;
        }
        return seats;
    }
}

/// <summary>
/// The majority systems (decision 0017): single-member districts won by plurality ("fptp") or by an absolute majority with a runoff ("two_round").
/// The districts are simulated, not mapped: Seats virtual districts whose party shares scatter around the national vote (a stable draw per
/// district and party, rescaled so the districts average to the national shares), so a party's seats follow the cube-law-like curve a real map
/// gives: the plurality party gains, an evenly spread small party wins nothing, a concentrated one wins seats with a fraction of the vote.
/// Strategic voters desert candidates who cannot win; a runoff sends an eliminated candidate's voters (less those who stay home) to the
/// candidates still standing in proportion to how close they are (<c>distance</c>; without it, evenly). Deterministic for given votes.
/// </summary>
public sealed class MajoritarianSystem : IElectoralSystem
{
    private const ulong MapSeed = 20150101UL;
    private const double ProximityRate = 3.0;
    private readonly bool _runoff;
    private readonly double _spread, _strategic, _minShare, _abstain;
    private readonly int _top;
    private readonly Func<string, string, double>? _distance;

    public MajoritarianSystem(int seats, bool runoff, double spread = 0.35, double strategic = 0.0, int runoffTop = 2, double runoffMinShare = 0.0,
        double abstain = 0.1, Func<string, string, double>? distance = null)
    {
        if (seats <= 0) throw new ArgumentOutOfRangeException(nameof(seats));
        Seats = seats; _runoff = runoff; _spread = spread; _strategic = strategic; _top = Math.Max(2, runoffTop);
        _minShare = runoffMinShare; _abstain = abstain; _distance = distance;
    }

    public int Seats { get; }

    private static ulong Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>How willing the voters of <paramref name="from"/> are to back <paramref name="to"/>.</summary>
    private double Weight(string from, string to) => _distance == null ? 1.0 : Math.Exp(-ProximityRate * _distance(from, to));

    public IReadOnlyDictionary<string, int> Allocate(IReadOnlyDictionary<string, double> votes)
    {
        var ids = votes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        double total = ids.Sum(i => votes[i]);
        if (!(total > 0)) throw new ArgumentException("Total votes must be positive.", nameof(votes));
        int n = ids.Count, d = Seats;
        var share = new double[d, n];
        var national = ids.Select(i => votes[i] / total).ToArray();
        var hashes = ids.Select(Hash).ToArray();
        for (int k = 0; k < d; k++)
            for (int i = 0; i < n; i++)
            {
                var rng = new Determinism.Pcg32(MapSeed ^ hashes[i], (ulong)k + 1);
                double z = 0;
                for (int j = 0; j < 12; j++) z += rng.NextDouble();
                share[k, i] = national[i] * Determinism.DetMath.Exp(_spread * (z - 6.0));
            }
        for (int iter = 0; iter < 10; iter++) // rows to 1, then columns back to the national share
        {
            for (int k = 0; k < d; k++) Normalize(share, k, n);
            for (int i = 0; i < n; i++)
            {
                double mean = 0;
                for (int k = 0; k < d; k++) mean += share[k, i];
                mean /= d;
                double f = mean > 0 ? national[i] / mean : 1.0;
                for (int k = 0; k < d; k++) share[k, i] *= f;
            }
        }
        var seats = ids.ToDictionary(i => i, _ => 0, StringComparer.Ordinal);
        var row = new double[n];
        for (int k = 0; k < d; k++)
        {
            Normalize(share, k, n);
            for (int i = 0; i < n; i++) row[i] = share[k, i];
            if (_strategic > 0) Desert(row, ids);
            seats[ids[_runoff ? RunoffWinner(row, ids) : Leader(row, Enumerable.Range(0, n))]]++;
        }
        return seats;
    }

    private static void Normalize(double[,] a, int k, int n)
    {
        double s = 0;
        for (int i = 0; i < n; i++) s += a[k, i];
        if (s > 0) for (int i = 0; i < n; i++) a[k, i] /= s;
    }

    private static int Leader(double[] row, IEnumerable<int> among)
    {
        int best = -1;
        foreach (int i in among) if (best < 0 || row[i] > row[best]) best = i; // ordinal order, strict '>' keeps the lower id on ties
        return best;
    }

    /// <summary>Strategic voters of every candidate outside the top two move to the top two.</summary>
    private void Desert(double[] row, List<string> ids)
    {
        int n = row.Length;
        int first = Leader(row, Enumerable.Range(0, n));
        int second = Leader(row, Enumerable.Range(0, n).Where(i => i != first));
        if (second < 0) return;
        for (int q = 0; q < n; q++)
        {
            if (q == first || q == second) continue;
            double moving = row[q] * _strategic, wa = Weight(ids[q], ids[first]), wb = Weight(ids[q], ids[second]);
            row[q] -= moving;
            row[first] += moving * wa / (wa + wb);
            row[second] += moving * wb / (wa + wb);
        }
    }

    private int RunoffWinner(double[] row, List<string> ids)
    {
        int n = row.Length;
        int first = Leader(row, Enumerable.Range(0, n));
        if (row[first] > 0.5) return first;
        var through = Enumerable.Range(0, n).OrderByDescending(i => row[i]).ThenBy(i => i).Take(_top)
            .Where((i, rank) => rank < 2 || row[i] >= _minShare).ToList();
        if (through.Count == 1) return through[0];
        var final = through.ToDictionary(i => i, i => row[i]);
        for (int q = 0; q < n; q++)
        {
            if (final.ContainsKey(q)) continue;
            double moving = row[q] * (1.0 - _abstain), wsum = through.Sum(j => Weight(ids[q], ids[j]));
            foreach (int j in through) final[j] += moving * Weight(ids[q], ids[j]) / wsum;
        }
        return through.OrderByDescending(j => final[j]).ThenBy(j => j).First();
    }
}

public static class ElectoralSystems
{
    public static IElectoralSystem Create(ElectoralSystemDef def, Func<string, string, double>? distance = null) => def.Type switch
    {
        "fptp" => new MajoritarianSystem(def.Seats, false, def.Spread, def.Strategic, def.RunoffTop, def.RunoffMinShare, def.Abstain, distance),
        "two_round" => new MajoritarianSystem(def.Seats, true, def.Spread, def.Strategic, def.RunoffTop, def.RunoffMinShare, def.Abstain, distance),
        "proportional" => new ProportionalSystem(def.Seats, def.Threshold, def.Method),
        _ => throw new InvalidOperationException($"Unknown electoral system type '{def.Type}'."),
    };
}
