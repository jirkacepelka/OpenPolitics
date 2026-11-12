using System.Globalization;
using System.Text;
using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Batch;

/// <summary>How the scripted player party behaves in a politics batch. All of them answer formation questions.</summary>
public enum PoliticsStrategy { Ignorer, Keeper, Random, Extreme }

public sealed record PoliticsBatchReport(
    int Games, int Turns, string Strategy,
    int Governments, int GamesWithPartnerLoss, int PartnerDepartures, int Falls, int FallsWithNewMajority,
    int EarlyElections, double MeanTurnsToFirstPartnerLoss, int StuckGames)
{
    public string ToText()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"Politics batch: {Games} game(s) x {Turns} turns, strategy '{Strategy}'");
        sb.AppendLine($"  Governments formed:          {Governments}");
        sb.AppendLine($"  Games with a partner loss:   {GamesWithPartnerLoss} ({(Games > 0 ? 100.0 * GamesWithPartnerLoss / Games : 0).ToString("0.0", inv)} %)");
        sb.AppendLine($"  Partner departures:          {PartnerDepartures}");
        sb.AppendLine($"  Falls (majority lost):       {Falls}");
        sb.AppendLine($"  Falls with a new majority:   {FallsWithNewMajority}");
        sb.AppendLine($"  Early-election calls:        {EarlyElections}");
        sb.AppendLine($"  Turns to first partner loss: {MeanTurnsToFirstPartnerLoss.ToString("0.0", inv)} (mean, games without a loss count as {Turns})");
        sb.AppendLine($"  Games stuck outside Governing: {StuckGames}");
        return sb.ToString();
    }
}

/// <summary>Headless runs of the political layer. Strategies use only the public game API.</summary>
public static class PoliticsBatch
{
    public static PoliticsBatchReport Run(PackSet packs, string scenarioId, int games, int turns, PoliticsStrategy strategy, ulong firstSeed = 1)
    {
        var rules = packs.Countries.First(c => c.Id == packs.GetScenario(scenarioId).CountryId).Politics
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' has no politics.");
        int stuckLimit = 2 * rules.MaxFormationTurns + rules.CaretakerTurns + 2;
        int governments = 0, withLoss = 0, departures = 0, falls = 0, fallsOk = 0, early = 0, stuck = 0;
        double sumFirstLoss = 0;
        var policies = packs.Policies.ToDictionary(p => p.Id, StringComparer.Ordinal);

        for (int i = 0; i < games; i++)
        {
            ulong seed = firstSeed + (ulong)i;
            var game = Simulation.NewGame(packs, scenarioId, seed);
            var rng = new Pcg32(seed, 7); // strategy randomness, separate from the game's streams
            var fallTurns = new List<int>();
            var formedTurns = new List<int>();
            int firstLoss = -1, sinceGoverning = 0;
            bool wasStuck = false;

            for (int t = 0; t < turns; t++)
            {
                Act(game, strategy, packs, policies, rng);
                var report = game.EndTurn();
                foreach (var e in report.PoliticalEvents)
                {
                    switch (e)
                    {
                        case GovernmentFormed: governments++; formedTurns.Add(report.Turn); break;
                        case PartnerLeft: departures++; if (firstLoss < 0) firstLoss = report.Turn; break;
                        case GovernmentFell: falls++; fallTurns.Add(report.Turn); break;
                        case EarlyElectionsCalled: early++; break;
                    }
                }
                var phase = game.State.Countries.Values.Single().Politics!.Phase;
                sinceGoverning = phase == Phase.Governing ? 0 : sinceGoverning + 1;
                // After a fall the machinery may start several rounds (early elections), so only count a game
                // as stuck when it stays out of Governing far longer than one full cycle (two rounds of that).
                if (sinceGoverning > 2 * stuckLimit) wasStuck = true;
            }

            if (firstLoss >= 0) withLoss++;
            sumFirstLoss += firstLoss >= 0 ? firstLoss : turns;
            if (wasStuck) stuck++;
            foreach (var fall in fallTurns)
            {
                if (fall + stuckLimit > turns) { fallsOk++; continue; } // not resolved yet when the run ended: not counted against
                if (formedTurns.Any(f => f > fall && f <= fall + 2 * stuckLimit)) fallsOk++;
            }
        }
        return new PoliticsBatchReport(games, turns, strategy.ToString().ToLowerInvariant(), governments, withLoss, departures,
            falls, fallsOk, early, games > 0 ? sumFirstLoss / games : 0, stuck);
    }

    private static void Act(Game game, PoliticsStrategy strategy, PackSet packs, Dictionary<string, PolicyDef> policies, Pcg32 rng)
    {
        var state = game.State;
        var pol = state.Countries.Values.Single().Politics!;

        // Formation questions are answered the same way by every strategy.
        if (pol.Formation is { PlayerIsFormateur: true } && game.SuggestCoalition() is { } s)
            game.Apply(new ProposeCoalition(s.Partners, s.Agreements));
        else if (pol.Formation?.Offer != null) game.Apply(new AcceptOffer());

        if (pol.Phase != Phase.Governing || pol.PlayerRole != Role.Leader) return;
        var current = state.Countries.Values.Single().Policies;
        switch (strategy)
        {
            case PoliticsStrategy.Keeper:
                if (pol.Government != null
                    && Governor.Move(pol.Government, id => LatestPolicy(game, current, id), _ => 0, policies, state.Turn) is { } m)
                    game.Apply(new SetPolicy(m.PolicyId, m.Value));
                break;
            case PoliticsStrategy.Random:
                if (state.Turn % 4 == 0 && packs.Policies.Count > 0)
                {
                    var p = packs.Policies[rng.NextInt(packs.Policies.Count)];
                    game.Apply(new SetPolicy(p.Id, p.Min + rng.NextDouble() * (p.Max - p.Min)));
                }
                break;
            case PoliticsStrategy.Extreme:
                if (state.Turn % 8 == 0)
                    for (int i = 0; i < packs.Policies.Count; i++)
                    {
                        var p = packs.Policies[i];
                        game.Apply(new SetPolicy(p.Id, (state.Turn / 8 + i) % 2 == 0 ? p.Max : p.Min));
                    }
                break;
        }
    }

    private static double LatestPolicy(Game game, IReadOnlyDictionary<string, double> current, string id) =>
        game.PendingPolicies.LastOrDefault(p => p.PolicyId == id)?.Value ?? current[id];
}
