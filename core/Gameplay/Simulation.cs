using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Gameplay;

/// <summary>Entry point of the core API: start a new game or restore one from a save.</summary>
public static class Simulation
{
    /// <summary>
    /// Starts a game. <paramref name="playerParty"/> picks the player's party in a political scenario (null = the scenario's
    /// <c>player_party</c>); its starting role (leader, partner or opposition) follows from the scenario's government.
    /// Throws <see cref="ArgumentException"/> for an unknown party or a party in a scenario without politics.
    /// </summary>
    public static Game NewGame(PackSet packs, string scenarioId, ulong seed, string? playerParty = null) =>
        new(packs, scenarioId, seed, playerParty);

    /// <summary>Replays the saved actions on a fresh game. Throws <see cref="SaveMismatchException"/> if the packs differ.</summary>
    public static Game Load(PackSet packs, SaveData save)
    {
        foreach (var (id, hash) in save.PackHashes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!packs.PackHashes.TryGetValue(id, out var current))
                throw new SaveMismatchException($"Pack '{id}' required by the save is not loaded.");
            if (current != hash)
                throw new SaveMismatchException($"Pack '{id}' has changed since the game was saved (hash mismatch).");
        }
        foreach (var id in packs.PackHashes.Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (!save.PackHashes.ContainsKey(id))
                throw new SaveMismatchException($"Pack '{id}' is loaded but was not part of the saved game.");

        Game game;
        try { game = new Game(packs, save.ScenarioId, save.Seed, save.PlayerParty); }
        catch (ArgumentException e) { throw new SaveMismatchException(e.Message); }
        int i = 0;
        void ApplyTurnActions(int turn)
        {
            while (i < save.Actions.Count && save.Actions[i].Turn == turn)
            {
                var r = game.Apply(save.Actions[i].Action);
                if (!r.Accepted)
                    throw new SaveMismatchException($"Saved action at turn {turn} is no longer valid: {r.ErrorKey} ({r.Detail}).");
                i++;
            }
        }
        for (int t = 0; t < save.Turn; t++) { ApplyTurnActions(t); game.EndTurn(); }
        ApplyTurnActions(save.Turn);
        if (i != save.Actions.Count) throw new SaveMismatchException("Save contains actions for turns after its last turn.");
        return game;
    }
}
