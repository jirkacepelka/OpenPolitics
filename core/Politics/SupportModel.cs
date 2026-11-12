using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>Inputs of one poll: group satisfaction in [-1,1], the governing parties, party credibility (default 1).</summary>
public sealed record SupportInput(
    IReadOnlyDictionary<string, double> Satisfaction,
    ISet<string> GovernmentParties,
    IReadOnlyDictionary<string, double> Credibility);

/// <summary>
/// Turns voter-group satisfaction and party platforms into vote shares (spec M2, section 3):
/// a softmax over group-by-party leanings. Per-party offsets are calibrated once so the start scenario matches
/// its stated poll numbers; after that the shares move only through the model's inputs.
/// </summary>
public sealed class SupportModel
{
    private readonly List<PartyDef> _parties;
    private readonly List<VoterGroupDef> _groups;
    private readonly double _kPos, _kGov, _tau, _sigma, _turnout;
    private readonly double[,] _distance; // [group, party]
    private readonly double[] _offset;

    public SupportModel(
        IReadOnlyList<PartyDef> parties, IReadOnlyList<VoterGroupDef> groups, IReadOnlyList<PolicyDef> policies,
        double kPos = 1.0, double kGov = 1.0, double tau = 0.5, double sigma = 0.05, double turnout = 0.65)
    {
        _parties = parties.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        _groups = groups.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        _kPos = kPos; _kGov = kGov; _tau = tau; _sigma = sigma; _turnout = turnout;
        _offset = new double[_parties.Count];
        _distance = new double[_groups.Count, _parties.Count];
        var range = policies.ToDictionary(p => p.Id, p => p.Max - p.Min, StringComparer.Ordinal);
        for (int g = 0; g < _groups.Count; g++)
            for (int p = 0; p < _parties.Count; p++)
                _distance[g, p] = Distance(_groups[g], _parties[p], range);
    }

    private static double Distance(VoterGroupDef g, PartyDef p, Dictionary<string, double> range)
    {
        double num = 0, den = 0;
        foreach (var pid in g.PolicyPrefs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!p.Platform.TryGetValue(pid, out var pos) || !range.TryGetValue(pid, out var r) || !(r > 0)) continue;
            double w = g.PolicyWeights.GetValueOrDefault(pid, 1.0) * pos.Weight;
            if (!(w > 0)) continue;
            double d = Math.Min(1.0, Math.Abs(g.PolicyPrefs[pid] - pos.Target) / r);
            num += w * d; den += w;
        }
        return den > 0 ? num / den : 0.5;
    }

    /// <summary>Fixes the per-party offsets so that <paramref name="start"/> reproduces <paramref name="targetShares"/>.</summary>
    public void Calibrate(IReadOnlyDictionary<string, double> targetShares, SupportInput start)
    {
        var t = _parties.Select(p => Math.Max(targetShares.GetValueOrDefault(p.Id, 0.0), 1e-6)).ToArray();
        double total = t.Sum();
        for (int i = 0; i < t.Length; i++) t[i] /= total;
        Array.Clear(_offset);
        for (int iter = 0; iter < 100; iter++)
        {
            var m = Shares(start, null);
            for (int i = 0; i < _offset.Length; i++)
                _offset[i] += _tau * DetMath.Log(t[i] / Math.Max(m[i], 1e-12)); // offsets enter as offset/tau
        }
    }

    public IReadOnlyDictionary<string, double> Compute(SupportInput input, Pcg32? noise)
    {
        var s = Shares(input, noise);
        var r = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < _parties.Count; i++) r[_parties[i].Id] = s[i];
        return r;
    }

    private double[] Shares(SupportInput input, Pcg32? noise)
    {
        int np = _parties.Count;
        var support = new double[np];
        var u = new double[np];
        for (int g = 0; g < _groups.Count; g++)
        {
            var grp = _groups[g];
            double sat = Math.Clamp(input.Satisfaction.GetValueOrDefault(grp.Id, 0.0), -1.0, 1.0);
            for (int p = 0; p < np; p++)
            {
                var party = _parties[p];
                double cred = input.Credibility.GetValueOrDefault(party.Id, 1.0);
                double blame = input.GovernmentParties.Contains(party.Id) ? _kGov * sat : -0.5 * _kGov * sat;
                double v = _offset[p] + party.Affinity.GetValueOrDefault(grp.Id, 0.0)
                    + _kPos * (1.0 - _distance[g, p]) * (0.5 + 0.5 * cred) + blame;
                if (noise is not null)
                {
                    double sum = 0;
                    for (int k = 0; k < 12; k++) sum += noise.NextDouble();
                    v += _sigma * (sum - 6.0) * (party.Swing ? 3.0 : 1.0);
                }
                u[p] = v / _tau;
            }
            double max = u.Max(), z = 0;
            for (int p = 0; p < np; p++) { u[p] = DetMath.Exp(u[p] - max); z += u[p]; }
            double weight = grp.Size * _turnout;
            for (int p = 0; p < np; p++) support[p] += weight * u[p] / z;
        }
        double sumAll = support.Sum();
        if (!(sumAll > 0)) return Enumerable.Repeat(1.0 / np, np).ToArray();
        for (int p = 0; p < np; p++) support[p] /= sumAll;
        return support;
    }
}
