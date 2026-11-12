using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Batch;

/// <summary>A scripted player used by the headless runner. Must only use the given rng for randomness.</summary>
public interface IStrategy
{
    string Name { get; }
    IReadOnlyList<PlayerAction> Choose(GameState state, PackSet packs, Pcg32 rng);
}

public sealed class PassiveStrategy : IStrategy
{
    public string Name => "passive";
    public IReadOnlyList<PlayerAction> Choose(GameState state, PackSet packs, Pcg32 rng) => Array.Empty<PlayerAction>();
}

/// <summary>Each turn changes 0 to 2 random policies to a uniformly random value in their range.</summary>
public sealed class RandomStrategy : IStrategy
{
    public string Name => "random";

    public IReadOnlyList<PlayerAction> Choose(GameState state, PackSet packs, Pcg32 rng)
    {
        var actions = new List<PlayerAction>();
        if (packs.Policies.Count == 0) return actions;
        int n = rng.NextInt(3);
        for (int i = 0; i < n; i++)
        {
            var p = packs.Policies[rng.NextInt(packs.Policies.Count)];
            actions.Add(new SetPolicy(p.Id, p.Min + rng.NextDouble() * (p.Max - p.Min)));
        }
        return actions;
    }
}

/// <summary>Every eight turns sets every policy to its minimum or maximum, alternating by policy and period.</summary>
public sealed class ExtremeStrategy : IStrategy
{
    public string Name => "extreme";

    public IReadOnlyList<PlayerAction> Choose(GameState state, PackSet packs, Pcg32 rng)
    {
        var actions = new List<PlayerAction>();
        if (state.Turn % 8 != 0) return actions;
        for (int i = 0; i < packs.Policies.Count; i++)
        {
            var p = packs.Policies[i];
            actions.Add(new SetPolicy(p.Id, (state.Turn / 8 + i) % 2 == 0 ? p.Max : p.Min));
        }
        return actions;
    }
}

public static class Strategies
{
    public static IStrategy? ByName(string name) => name switch
    {
        "passive" => new PassiveStrategy(),
        "random" => new RandomStrategy(),
        "extreme" => new ExtremeStrategy(),
        _ => null,
    };
}
