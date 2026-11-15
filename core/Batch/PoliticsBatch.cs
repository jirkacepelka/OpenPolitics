using System.Globalization;
using System.Text;
using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Batch;

/// <summary>How the scripted player party behaves in a politics batch. All of them answer formation questions.</summary>
public enum PoliticsStrategy { Ignorer, Keeper, Random, Extreme, Activist }

public sealed record PoliticsBatchReport(
    int Games, int Turns, string Strategy,
    int Governments, int GamesWithPartnerLoss, int PartnerDepartures, int Falls, int FallsWithNewMajority,
    int EarlyElections, double MeanTurnsToFirstPartnerLoss, int StuckGames,
    double MeanPlayerSupportAtEnd = 0, double MeanPlayerSeatsAtEnd = 0, int NegativeCapitalGames = 0,
    double MeanPlayerSupportOverRun = 0,
    double GoverningShare = 0, double PlayerLeadShare = 0, double MeanCapital = 0,
    double PolicyChangesPerYear = 0, double PolicyRefusalsPerYear = 0, int GamesLosingLead = 0)
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
        sb.AppendLine($"  Player party at the end:     {(100 * MeanPlayerSupportAtEnd).ToString("0.0", inv)} % of the poll, {MeanPlayerSeatsAtEnd.ToString("0.0", inv)} seats (mean)");
        sb.AppendLine($"  Player poll over the whole run: {(100 * MeanPlayerSupportOverRun).ToString("0.0", inv)} % (mean of every quarter)");
        sb.AppendLine($"  Games with negative capital: {NegativeCapitalGames}");
        sb.AppendLine($"  Quarters with a sitting government: {(100 * GoverningShare).ToString("0.0", inv)} %");
        sb.AppendLine($"  Quarters the player leads:   {(100 * PlayerLeadShare).ToString("0.0", inv)} %");
        sb.AppendLine($"  Games where the player lost the lead: {GamesLosingLead}");
        sb.AppendLine($"  Player capital (mean):       {MeanCapital.ToString("0.0", inv)}");
        sb.AppendLine($"  Policy changes per year:     {PolicyChangesPerYear.ToString("0.00", inv)} accepted, {PolicyRefusalsPerYear.ToString("0.00", inv)} refused for capital");
        return sb.ToString();
    }
}

/// <summary>Headless runs of the political layer. Strategies use only the public game API.</summary>
public static class PoliticsBatch
{
    /// <param name="playerParty">The party the scripted player plays (null = the scenario's party).</param>
    public static PoliticsBatchReport Run(PackSet packs, string scenarioId, int games, int turns, PoliticsStrategy strategy, ulong firstSeed = 1,
        string? playerParty = null)
    {
        var rules = packs.Countries.First(c => c.Id == packs.GetScenario(scenarioId).CountryId).Politics
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' has no politics.");
        int stuckLimit = 2 * rules.MaxFormationTurns + rules.CaretakerTurns + 2;
        int governments = 0, withLoss = 0, departures = 0, falls = 0, fallsOk = 0, early = 0, stuck = 0;
        double sumFirstLoss = 0, sumSupport = 0, sumSeats = 0, sumRun = 0;
        int negativeCapital = 0, governingTurns = 0, leadTurns = 0, lostLead = 0;
        double sumCapital = 0;
        var counter = new PolicyCounter();
        var policies = packs.Policies.ToDictionary(p => p.Id, StringComparer.Ordinal);

        for (int i = 0; i < games; i++)
        {
            ulong seed = firstSeed + (ulong)i;
            var game = Simulation.NewGame(packs, scenarioId, seed, playerParty);
            var rng = new Pcg32(seed, 7); // strategy randomness, separate from the game's streams
            var fallTurns = new List<int>();
            var formedTurns = new List<int>();
            double runShare = 0;
            int firstLoss = -1, sinceGoverning = 0;
            bool wasStuck = false, overspent = false;
            var memory = new ActivistMemory();
            bool led = game.State.Countries.Values.Single().Politics!.PlayerRole == Role.Leader, lost = false;

            for (int t = 0; t < turns; t++)
            {
                Act(game, strategy, packs, policies, rng, memory, counter);
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
                var now = game.State.Countries.Values.Single().Politics!;
                if (now.Party!.PoliticalCapital < -1e-9) overspent = true;
                runShare += now.Support.GetValueOrDefault(now.PlayerParty);
                var phase = now.Phase;
                sumCapital += now.Party.PoliticalCapital;
                if (phase == Phase.Governing) governingTurns++;
                if (now.PlayerRole == Role.Leader) { leadTurns++; led = true; }
                else if (led && phase == Phase.Governing) lost = true;
                sinceGoverning = phase == Phase.Governing ? 0 : sinceGoverning + 1;
                // After a fall the machinery may start several rounds (early elections), so only count a game
                // as stuck when it stays out of Governing far longer than one full cycle (two rounds of that).
                if (sinceGoverning > 2 * stuckLimit) wasStuck = true;
            }

            var end = game.State.Countries.Values.Single().Politics!;
            sumSupport += end.Support.GetValueOrDefault(end.PlayerParty);
            sumSeats += end.Seats.GetValueOrDefault(end.PlayerParty);
            if (overspent) negativeCapital++;
            if (lost) lostLead++;
            sumRun += turns > 0 ? runShare / turns : 0;
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
            falls, fallsOk, early, games > 0 ? sumFirstLoss / games : 0, stuck,
            games > 0 ? sumSupport / games : 0, games > 0 ? sumSeats / games : 0, negativeCapital,
            games > 0 ? sumRun / games : 0,
            Share(governingTurns, games, turns), Share(leadTurns, games, turns), games * turns > 0 ? sumCapital / (games * turns) : 0,
            PerYear(counter.Accepted, packs, scenarioId, games, turns), PerYear(counter.Refused, packs, scenarioId, games, turns), lostLead);
    }

    private static double Share(int count, int games, int turns) => games * turns > 0 ? (double)count / (games * turns) : 0;

    private static double PerYear(int count, PackSet packs, string scenarioId, int games, int turns) =>
        games * turns > 0 ? count / (games * turns / (double)packs.GetScenario(scenarioId).TurnsPerYear) : 0;

    /// <summary>Policy changes the scripted player tried as leader: accepted, and refused because they cost more capital than it had.</summary>
    private sealed class PolicyCounter
    {
        public int Accepted, Refused;

        public void Set(Game game, string policyId, double value)
        {
            var r = game.Apply(new SetPolicy(policyId, value));
            if (r.Accepted) Accepted++;
            else if (r.ErrorKey is "error.policy_cost" or "error.not_enough_capital") Refused++;
        }
    }

    private sealed class ActivistMemory { public readonly List<IReadOnlyDictionary<string, double>> Values = new(); }

    private static void Act(Game game, PoliticsStrategy strategy, PackSet packs, Dictionary<string, PolicyDef> policies, Pcg32 rng, ActivistMemory memory, PolicyCounter counter)
    {
        var state = game.State;
        var pol = state.Countries.Values.Single().Politics!;

        // Formation questions are answered the same way by every strategy.
        if (pol.Formation is { PlayerIsFormateur: true } && game.SuggestCoalition() is { } s)
            game.Apply(new ProposeCoalition(s.Partners, s.Agreements));
        else if (pol.Formation?.Offer != null) game.Apply(new AcceptOffer());

        if (strategy == PoliticsStrategy.Activist) { Activism(game, state, pol, packs, memory); return; }
        if (pol.Phase != Phase.Governing || pol.PlayerRole != Role.Leader) return;
        var current = state.Countries.Values.Single().Policies;
        switch (strategy)
        {
            case PoliticsStrategy.Keeper:
                if (pol.Government != null
                    && Governor.Move(pol.Government, id => LatestPolicy(game, current, id), _ => 0, policies, state.Turn) is { } m)
                    counter.Set(game, m.PolicyId, m.Value);
                break;
            case PoliticsStrategy.Random:
                if (state.Turn % 4 == 0 && packs.Policies.Count > 0)
                {
                    var p = packs.Policies[rng.NextInt(packs.Policies.Count)];
                    counter.Set(game, p.Id, p.Min + rng.NextDouble() * (p.Max - p.Min));
                }
                break;
            case PoliticsStrategy.Extreme:
                if (state.Turn % 8 == 0)
                    for (int i = 0; i < packs.Policies.Count; i++)
                    {
                        var p = packs.Policies[i];
                        counter.Set(game, p.Id, (state.Turn / 8 + i) % 2 == 0 ? p.Max : p.Min);
                    }
                break;
        }
    }

    private static double LatestPolicy(Game game, IReadOnlyDictionary<string, double> current, string id) =>
        game.PendingPolicies.LastOrDefault(p => p.PolicyId == id)?.Value ?? current[id];

    /// <summary>
    /// A party that works every quarter with the capital it has: criticises the value that got worse for most voters
    /// (when outside the government), campaigns among its largest group, builds the organisation and courts the
    /// party it is furthest from.
    /// </summary>
    private static void Activism(Game game, GameState state, CountryPolitics pol, PackSet packs, ActivistMemory memory)
    {
        var country = state.Countries.Values.Single();
        memory.Values.Add(new Dictionary<string, double>(country.Values));
        if (memory.Values.Count > 5) memory.Values.RemoveAt(0);
        var party = pol.Party!;
        double pc = party.PoliticalCapital;

        if (pol.PlayerRole != Role.Leader && pc >= 2 && memory.Values.Count > 1)
        {
            var then = memory.Values[0];
            string? best = null; double bestScore = 0;
            foreach (var v in packs.Values.OrderBy(v => v.Id, StringComparer.Ordinal))
            {
                double delta = country.Values[v.Id] - then[v.Id], score = 0;
                if (delta == 0) continue;
                foreach (var g in packs.VoterGroups)
                {
                    double w = g.ValueWeights.GetValueOrDefault(v.Id);
                    score += g.Size * Math.Abs(w) * (Math.Sign(w) * Math.Sign(delta) < 0 ? 1 : -1);
                }
                if (score > bestScore) { bestScore = score; best = v.Id; }
            }
            if (best != null && game.Apply(new Criticize(best)).Accepted) pc -= 2;
        }
        if (pc >= 3 && packs.VoterGroups.Count > 0)
        {
            var biggest = packs.VoterGroups.OrderByDescending(g => g.Size).ThenBy(g => g.Id, StringComparer.Ordinal).First();
            if (game.Apply(new Campaign(biggest.Id, 1)).Accepted) pc -= 3;
        }
        if (party.Organization < 0.5 && pc >= 4 && game.Apply(new BuildOrganization()).Accepted) pc -= 4;
        if (pc >= 3)
        {
            var target = party.Relations.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Where(kv => pol.Seats.GetValueOrDefault(kv.Key) > 0).Select(kv => kv.Key).FirstOrDefault();
            if (target != null) game.Apply(new Court(target));
        }
    }
}
