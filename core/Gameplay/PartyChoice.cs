using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Gameplay;

/// <summary>
/// One party the player can pick at the start: its starting poll share and seats, its place on the compass (X economic left -1 ..
/// right +1, Y social liberal -1 .. authoritarian +1) and the role it starts in.
/// </summary>
public sealed record PartyOption(string PartyId, double Support, int Seats, Role Role, double X, double Y);

/// <summary>What the start screen shows for the party choice. Clients use it so they never work out roles or seats themselves.</summary>
public static class PartyChoice
{
    /// <summary>Every party of a political scenario, largest first (empty for a scenario without politics).</summary>
    public static IReadOnlyList<PartyOption> Options(PackSet packs, string scenarioId)
    {
        var scenario = packs.GetScenario(scenarioId);
        if (scenario.Politics is not { } sp) return Array.Empty<PartyOption>();
        var game = Simulation.NewGame(packs, scenarioId, 1);
        var pol = game.State.Countries.Values.Single().Politics;
        if (pol == null) return Array.Empty<PartyOption>();
        var compass = game.Compass;
        string? leader = sp.Leader ?? pol.Leader;
        return packs.Parties
            .Select(p => new PartyOption(p.Id, pol.Support.GetValueOrDefault(p.Id), pol.Seats.GetValueOrDefault(p.Id),
                p.Id == leader ? Role.Leader : pol.GovernmentParties.Contains(p.Id) ? Role.Partner : Role.Opposition,
                compass.GetValueOrDefault(p.Id).X, compass.GetValueOrDefault(p.Id).Y))
            .OrderByDescending(o => o.Support).ThenBy(o => o.PartyId, StringComparer.Ordinal).ToList();
    }
}
