namespace OpenPolitics.Core.Determinism;

/// <summary>PCG-XSH-RR 64/32 pseudo-random generator (O'Neill). The only PRNG the core may use.</summary>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;
    private ulong _state;
    private ulong _inc;

    public Pcg32(ulong seed, ulong stream = 54)
    {
        _state = 0;
        _inc = (stream << 1) | 1UL;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    private Pcg32(ulong state, ulong inc, bool _)
    {
        _state = state;
        _inc = inc;
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _inc);
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>Uniform double in [0, 1) using 53 random bits from two draws.</summary>
    public double NextDouble()
    {
        ulong a = NextUInt() >> 5;  // 27 bits
        ulong b = NextUInt() >> 6;  // 26 bits
        return (a * 67108864.0 + b) / 9007199254740992.0;
    }

    /// <summary>Unbiased integer in [0, exclusiveMax).</summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        uint bound = (uint)exclusiveMax;
        uint threshold = unchecked((uint)(-(int)bound)) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    public Pcg32 Clone() => new(_state, _inc, true);

    public (ulong State, ulong Inc) Snapshot() => (_state, _inc);

    public static Pcg32 FromSnapshot(ulong state, ulong inc) => new(state, inc, true);
}
