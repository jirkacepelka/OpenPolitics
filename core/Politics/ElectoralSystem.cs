using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>Turns votes (shares or counts per party id) into seats.</summary>
public interface IElectoralSystem
{
    int Seats { get; }

    /// <summary>Result has an entry for every party (0 for no seats), keys in ordinal order.</summary>
    IReadOnlyDictionary<string, int> Allocate(IReadOnlyDictionary<string, double> votes);
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

    public IReadOnlyDictionary<string, int> Allocate(IReadOnlyDictionary<string, double> votes)
    {
        var ids = votes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        double total = ids.Sum(i => votes[i]);
        if (!(total > 0)) throw new ArgumentException("Total votes must be positive.", nameof(votes));

        var eligible = ids.Where(i => votes[i] / total >= _threshold).ToList();
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

public static class ElectoralSystems
{
    public static IElectoralSystem Create(ElectoralSystemDef def) => def.Type switch
    {
        "proportional" => new ProportionalSystem(def.Seats, def.Threshold, def.Method),
        _ => throw new InvalidOperationException($"Unknown electoral system type '{def.Type}'."),
    };
}
