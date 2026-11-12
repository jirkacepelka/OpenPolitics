using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Graph;

/// <summary>
/// Per-edge delay of the source influence. none: identity. lag(N): value from N turns ago (the first pushed value
/// fills the history). smooth(tau): exponential smoothing y += (u - y) / max(tau, 1), starting at the first input.
/// </summary>
public sealed class DelayLine
{
    private readonly DelayDef _def;
    private readonly int _lag;
    private bool _initialised;
    private double[] _buffer;
    private int _head;
    private double _y;

    public DelayLine(DelayDef def)
    {
        _def = def;
        _lag = def.Type == "lag" ? Math.Max(0, (int)Math.Round(def.Turns)) : 0;
        _buffer = new double[_lag];
    }

    public double Push(double input)
    {
        switch (_def.Type)
        {
            case "lag" when _lag > 0:
                if (!_initialised) { Array.Fill(_buffer, input); _initialised = true; }
                double output = _buffer[_head];
                _buffer[_head] = input;
                _head = (_head + 1) % _lag;
                return output;
            case "smooth":
                if (!_initialised) { _y = input; _initialised = true; return _y; }
                _y += (input - _y) / Math.Max(_def.Turns, 1.0);
                return _y;
            default:
                return input;
        }
    }

    /// <summary>State layout: lag [initialised, head, buffer...]; smooth [initialised, y]; none [].</summary>
    public double[] Snapshot() => _def.Type switch
    {
        "lag" when _lag > 0 => new[] { _initialised ? 1.0 : 0.0, _head }.Concat(_buffer).ToArray(),
        "smooth" => new[] { _initialised ? 1.0 : 0.0, _y },
        _ => Array.Empty<double>(),
    };

    public static DelayLine FromSnapshot(DelayDef def, double[] state)
    {
        var line = new DelayLine(def);
        if (def.Type == "lag" && line._lag > 0 && state.Length == 2 + line._lag)
        {
            line._initialised = state[0] != 0;
            line._head = (int)state[1];
            Array.Copy(state, 2, line._buffer, 0, line._lag);
        }
        else if (def.Type == "smooth" && state.Length == 2)
        {
            line._initialised = state[0] != 0;
            line._y = state[1];
        }
        return line;
    }
}
