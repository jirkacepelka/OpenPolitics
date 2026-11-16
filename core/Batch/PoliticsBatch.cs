using System.Globalization;
using System.Text;
using OpenPolitics.Core.Determinism;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;

namespace OpenPolitics.Core.Batch;

/// <summary>
/// How the scripted player party behaves in a politics batch. All of them answer formation questions. Reformer (decision 0013) governs like the AI
/// governor with its own platform - commitments, crisis responses, drift toward the platform - and pays for every change in political capital.
/// </summary>
/// <remarks>
/// Challenger (decision 0015): an opposition party that plays the opposition game consistently - campaigns among its core voter groups, proposes
/// its platform positions that its core groups like, criticises what got worse, promises its goals before elections, builds its organisation, and
/// governs like the reformer once it leads.
/// </remarks>
/// <remarks>
/// Threatener (decision 0016): plays the challenger outside the lead and the reformer as leader; as a junior partner it gives the leader ultimatums
/// on its unmet demands and programme positions whenever one is not refused outright (deadline 4), and lets a stalled or refused one run out.
/// </remarks>
public enum PoliticsStrategy { Ignorer, Keeper, Random, Extreme, Activist, Reformer, Challenger, Threatener }

public sealed record PoliticsBatchReport(
    int Games, int Turns, string Strategy,
    int Governments, int GamesWithPartnerLoss, int PartnerDepartures, int Falls, int FallsWithNewMajority,
    int EarlyElections, double MeanTurnsToFirstPartnerLoss, int StuckGames,
    double MeanPlayerSupportAtEnd = 0, double MeanPlayerSeatsAtEnd = 0, int NegativeCapitalGames = 0,
    double MeanPlayerSupportOverRun = 0,
    double GoverningShare = 0, double PlayerLeadShare = 0, double MeanCapital = 0,
    double PolicyChangesPerYear = 0, double PolicyRefusalsPerYear = 0, int GamesLosingLead = 0, double MeanCapitalLeading = 0,
    int PlayerReshuffles = 0, int Reshuffles = 0,
    int ProposalsPut = 0, int ProposalsAnswered = 0, int ProposalsFailed = 0, int Announcements = 0, int AnnouncementsAdopted = 0,
    IReadOnlyList<double>? PollByQuarter = null, int Elections = 0, int ElectionsWon = 0, int GovernmentsLed = 0,
    int PlayerUltimatums = 0, int UltimatumsDelivered = 0, int UltimatumsCarriedOut = 0, int FiscalCredits = 0, double FiscalCreditCapital = 0)
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
        sb.AppendLine($"  Player capital (mean):       {MeanCapital.ToString("0.0", inv)} overall, {MeanCapitalLeading.ToString("0.0", inv)} while leading");
        sb.AppendLine($"  Reshuffles:                  {Reshuffles} ({PlayerReshuffles} asked by the player)");
        sb.AppendLine($"  Proposals put to the player: {ProposalsPut} ({ProposalsAnswered} answered, {ProposalsFailed} failed or turned down)");
        sb.AppendLine($"  Announced proposals:         {Announcements} ({AnnouncementsAdopted} adopted by the government)");
        sb.AppendLine($"  Policy changes per year led: {PolicyChangesPerYear.ToString("0.00", inv)} accepted, {PolicyRefusalsPerYear.ToString("0.00", inv)} refused for capital");
        sb.AppendLine($"  Partner ultimatums:          {PlayerUltimatums} ({UltimatumsDelivered} delivered, {UltimatumsCarriedOut} carried out)");
        sb.AppendLine($"  Fiscal credit earned:        {FiscalCredits} quarter(s), {FiscalCreditCapital.ToString("0.0", inv)} capital in all");
        sb.AppendLine($"  Elections won (most votes):  {ElectionsWon} of {Elections} ({(Elections > 0 ? 100.0 * ElectionsWon / Elections : 0).ToString("0.0", inv)} %); governments led by the player: {GovernmentsLed}");
        if (PollByQuarter is { Count: > 0 } traj)
            sb.AppendLine("  Player poll by quarter (mean %): " + string.Join(" | ", Enumerable.Range(0, traj.Count).Where(q => q % 4 == 0)
                .Select(q => $"q{q} {(100 * traj[q]).ToString("0.0", inv)}")));
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
        int negativeCapital = 0, governingTurns = 0, leadTurns = 0, lostLead = 0, reshuffles = 0;
        double sumCapital = 0, sumCapitalLeading = 0;
        var pollByQuarter = new double[turns + 1];
        int elections = 0, electionsWon = 0, governmentsLed = 0;
        var counter = new PolicyCounter();
        var policies = packs.Policies.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var levers = Governor.Levers(packs);

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
            var start = game.State.Countries.Values.Single().Politics!;
            pollByQuarter[0] += start.Support.GetValueOrDefault(start.PlayerParty);

            for (int t = 0; t < turns; t++)
            {
                Act(game, strategy, packs, policies, levers, rng, memory, counter);
                var report = game.EndTurn();
                foreach (var e in report.PoliticalEvents)
                {
                    switch (e)
                    {
                        case GovernmentFormed gf: governments++; formedTurns.Add(report.Turn); if (gf.Government.Leader == start.PlayerParty) governmentsLed++; break;
                        case ElectionHeld eh:
                            elections++;
                            if (eh.Votes.TryGetValue(start.PlayerParty, out var mine) && eh.Votes.All(kv => kv.Key == start.PlayerParty || kv.Value < mine)) electionsWon++;
                            break;
                        case PartnerLeft: departures++; if (firstLoss < 0) firstLoss = report.Turn; break;
                        case GovernmentFell: falls++; fallTurns.Add(report.Turn); break;
                        case EarlyElectionsCalled: early++; break;
                        case CoalitionReshuffled: reshuffles++; break;
                        case ProposalPut: counter.ProposalsPut++; break;
                        case ProposalFailed: counter.ProposalsFailed++; break;
                        case AnnouncementAdopted: counter.Adopted++; break;
                        case UltimatumDelivered: counter.UltimatumsDelivered++; break;
                        case UltimatumCarriedOut: counter.UltimatumsCarriedOut++; break;
                        case FiscalCreditEarned fc: counter.FiscalCredits++; counter.FiscalCreditCapital += fc.Capital; break;
                    }
                }
                var now = game.State.Countries.Values.Single().Politics!;
                if (now.Party!.PoliticalCapital < -1e-9) overspent = true;
                runShare += now.Support.GetValueOrDefault(now.PlayerParty);
                pollByQuarter[t + 1] += now.Support.GetValueOrDefault(now.PlayerParty);
                var phase = now.Phase;
                sumCapital += now.Party.PoliticalCapital;
                if (phase == Phase.Governing) governingTurns++;
                if (now.PlayerRole == Role.Leader) { leadTurns++; led = true; sumCapitalLeading += now.Party.PoliticalCapital; }
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
            PerYear(counter.Accepted, packs, scenarioId, leadTurns), PerYear(counter.Refused, packs, scenarioId, leadTurns), lostLead,
            leadTurns > 0 ? sumCapitalLeading / leadTurns : 0, counter.Reshuffles, reshuffles,
            counter.ProposalsPut, counter.ProposalsAnswered, counter.ProposalsFailed, counter.Announcements, counter.Adopted,
            pollByQuarter.Select(x => games > 0 ? x / games : 0).ToList(), elections, electionsWon, governmentsLed,
            counter.Ultimatums, counter.UltimatumsDelivered, counter.UltimatumsCarriedOut, counter.FiscalCredits, counter.FiscalCreditCapital);
    }

    private static double Share(int count, int games, int turns) => games * turns > 0 ? (double)count / (games * turns) : 0;

    /// <summary>Per year in which the player led the government.</summary>
    private static double PerYear(int count, PackSet packs, string scenarioId, int leadTurns) =>
        leadTurns > 0 ? count / (leadTurns / (double)packs.GetScenario(scenarioId).TurnsPerYear) : 0;

    /// <summary>Policy changes the scripted player tried as leader: accepted, and refused because they cost more capital than it had.</summary>
    private sealed class PolicyCounter
    {
        public int Accepted, Refused, Reshuffles, ProposalsPut, ProposalsAnswered, ProposalsFailed, Announcements, Adopted;
        public int Ultimatums, UltimatumsDelivered, UltimatumsCarriedOut, FiscalCredits;
        public double FiscalCreditCapital;

        public void Set(Game game, string policyId, double value)
        {
            var r = game.Apply(new SetPolicy(policyId, value));
            if (r.Accepted) Accepted++;
            else if (r.ErrorKey is "error.policy_cost" or "error.not_enough_capital") Refused++;
        }
    }

    private sealed class ActivistMemory { public readonly List<IReadOnlyDictionary<string, double>> Values = new(); }

    private static void Act(Game game, PoliticsStrategy strategy, PackSet packs, Dictionary<string, PolicyDef> policies,
        IReadOnlyDictionary<string, IReadOnlyList<Governor.Lever>> levers, Pcg32 rng, ActivistMemory memory, PolicyCounter counter)
    {
        var state = game.State;
        var pol = state.Countries.Values.Single().Politics!;

        // Formation questions are answered the same way by every strategy.
        if (pol.Formation is { PlayerIsFormateur: true } && game.SuggestCoalition() is { } s)
            game.Apply(new ProposeCoalition(s.Partners, s.Agreements));
        else if (pol.Formation?.Offer != null) game.Apply(new AcceptOffer());

        AnswerProposals(game, pol, strategy, rng, counter);
        if (strategy is PoliticsStrategy.Activist or PoliticsStrategy.Reformer) Announce(game, pol, packs, policies, counter);
        if (strategy == PoliticsStrategy.Activist) { Activism(game, state, pol, packs, memory); return; }
        if (strategy == PoliticsStrategy.Threatener && pol.Phase == Phase.Governing && pol.PlayerRole == Role.Partner) Threaten(game, pol, packs, policies, counter);
        if (strategy is PoliticsStrategy.Challenger or PoliticsStrategy.Threatener && !(pol.Phase == Phase.Governing && pol.PlayerRole == Role.Leader))
        { Challenge(game, game.State, game.State.Countries.Values.Single().Politics!, packs, policies, memory, counter); return; }
        if (pol.Phase != Phase.Governing || pol.PlayerRole != Role.Leader) return;
        var current = state.Countries.Values.Single().Policies;
        switch (strategy)
        {
            case PoliticsStrategy.Keeper:
                if (pol.Government != null
                    && Governor.Move(pol.Government, id => LatestPolicy(game, current, id), _ => 0, policies, state.Turn) is { } m)
                    counter.Set(game, m.PolicyId, m.Value);
                break;
            case PoliticsStrategy.Reformer:
            case PoliticsStrategy.Challenger:
            case PoliticsStrategy.Threatener:
                TryReshuffle(game, pol, counter);
                var own = packs.Parties.FirstOrDefault(p => p.Id == pol.PlayerParty);
                var partnerLines = pol.Government!.Parties.Where(p => p != pol.PlayerParty)
                    .SelectMany(p => packs.Parties.First(x => x.Id == p).RedLines).ToList();
                var values = state.Countries.Values.Single().Values;
                if (Governor.Move(pol.Government, id => LatestPolicy(game, current, id), id => values.GetValueOrDefault(id), policies, state.Turn, own, partnerLines, levers) is { } r)
                    counter.Set(game, r.PolicyId, r.Value);
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

    /// <summary>
    /// How the scripted players answer proposals (decision 0014): the ignorer never does; keeper supports unless the change goes clearly against its
    /// programme and never blocks a measure with pivotal seats; reformer and activist follow their programme; random flips a coin; extreme opposes all.
    /// </summary>
    private static void AnswerProposals(Game game, CountryPolitics pol, PoliticsStrategy strategy, Pcg32 rng, PolicyCounter counter)
    {
        if (strategy == PoliticsStrategy.Ignorer || pol.Proposals?.FirstOrDefault(p => p.Answer == null) is not { } p) return;
        bool? support = strategy switch
        {
            PoliticsStrategy.Keeper => p.Fit >= -0.1 || (p.Kind == ProposalKind.Measure && p.Pivotal),
            PoliticsStrategy.Reformer or PoliticsStrategy.Activist or PoliticsStrategy.Challenger or PoliticsStrategy.Threatener => p.Fit > 1e-9 ? true : p.Fit < -1e-9 ? false : p.Kind == ProposalKind.Request ? true : null,
            PoliticsStrategy.Random => rng.NextDouble() < 0.5,
            PoliticsStrategy.Extreme => false,
            _ => null,
        };
        if (support == null) return;
        if (game.Apply(support.Value ? new SupportProposal(p.Id) : new OpposeProposal(p.Id)).Accepted) counter.ProposalsAnswered++;
    }

    /// <summary>Reformer and activist outside the lead announce the platform position furthest from the current setting, when they can spare the capital.</summary>
    private static void Announce(Game game, CountryPolitics pol, PackSet packs, Dictionary<string, PolicyDef> policies, PolicyCounter counter)
    {
        if (pol.PlayerRole == Role.Leader && pol.Phase == Phase.Governing || pol.Party is not { } party || party.PoliticalCapital < PoliticsLimits.AnnounceCost + 6) return;
        var current = game.State.Countries.Values.Single().Policies;
        var best = party.Platform.Where(kv => policies.ContainsKey(kv.Key))
            .Select(kv => (Id: kv.Key, Target: kv.Value, Gap: Math.Abs(kv.Value - current[kv.Key]) / Math.Max(1e-9, policies[kv.Key].Max - policies[kv.Key].Min)))
            .Where(x => x.Gap > 0.05)
            .OrderByDescending(x => x.Gap * (party.Priorities?.GetValueOrDefault(x.Id, 1.0) ?? 1.0)).ThenBy(x => x.Id, StringComparer.Ordinal);
        foreach (var c in best)
        {
            var preview = game.PreviewAnnouncement(c.Id, c.Target);
            if (preview == null || preview.Approval <= 0) continue;
            if (game.Apply(new AnnouncePolicy(c.Id, c.Target)).Accepted) { counter.Announcements++; return; }
        }
    }

    /// <summary>
    /// The threatener as a junior partner (decision 0016): when no ultimatum runs and the pause is over, it gives the leader one with a 4-quarter
    /// deadline on what matters most to it - its heaviest unmet demand, else its weightiest programme position away from the setting - unless the
    /// leader would refuse it at once (then the next one). A stalled or refused ultimatum is left to run out: the threatener walks.
    /// </summary>
    private static void Threaten(Game game, CountryPolitics pol, PackSet packs, Dictionary<string, PolicyDef> policies, PolicyCounter counter)
    {
        if (pol.PlayerUltimatum != null || pol.UltimatumCooldown > 0 || pol.Party is not { } party || party.PoliticalCapital < PoliticsLimits.UltimatumCost + 2) return;
        var own = packs.Parties.First(p => p.Id == pol.PlayerParty);
        var candidates = own.Demands.OrderByDescending(d => d.Weight).ThenBy(d => d.Id, StringComparer.Ordinal).Select(d => ("demand", d.Id))
            .Concat(party.Platform.Keys.Where(policies.ContainsKey)
                .OrderByDescending(k => party.Priorities?.GetValueOrDefault(k, 0.0) ?? 0.0).ThenBy(k => k, StringComparer.Ordinal).Select(k => ("policy", k)));
        foreach (var (kind, id) in candidates)
        {
            if (game.PreviewUltimatum(kind, id, 4) is not { ErrorKey: null, Answer: not "refuse" }) continue;
            if (game.Apply(new IssueUltimatum(kind, id, 4)).Accepted) counter.Ultimatums++;
            return;
        }
    }

    /// <summary>The reformer swaps a partner under an ultimatum for an outside party that would accept, when the cooldown allows it.</summary>
    private static void TryReshuffle(Game game, CountryPolitics pol, PolicyCounter counter)
    {
        if (pol.Reshuffle is not { CooldownLeft: 0, Queued: false } || pol.Ultimatums is not { Count: > 0 } ultimatums) return;
        string unhappy = ultimatums.Select(u => u.PartyId).OrderBy(x => x, StringComparer.Ordinal).First();
        if (unhappy == pol.PlayerParty) return;
        foreach (var outside in pol.Seats.Where(kv => kv.Value > 0 && !pol.GovernmentParties.Contains(kv.Key))
                     .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key))
        {
            var drop = new[] { unhappy }; var add = new[] { outside };
            if (game.SuggestReshuffle(drop, add) is not { } deal) continue;
            var preview = game.PreviewReshuffle(drop, add, deal);
            if (preview.ErrorKey != null || preview.Refusals.Count > 0 || !preview.Affordable) continue;
            if (game.Apply(new ReshuffleCoalition(drop, add, deal)).Accepted) counter.Reshuffles++;
            return;
        }
    }

    private static double LatestPolicy(Game game, IReadOnlyDictionary<string, double> current, string id) =>
        game.PendingPolicies.LastOrDefault(p => p.PolicyId == id)?.Value ?? current[id];

    /// <summary>
    /// The challenger outside the lead (decision 0015): keeps a small reserve and spends the rest every quarter - its goals as promises in the window
    /// before an election, the platform proposal its core groups like most (the one way to propose: to the leader as a partner, public otherwise),
    /// criticism of the value that got worse for its core groups, a campaign among the core group where it gains most, organisation up to a half.
    /// Its core groups are those with the highest affinity to it (top three by affinity × size).
    /// </summary>
    private static void Challenge(Game game, GameState state, CountryPolitics pol, PackSet packs, Dictionary<string, PolicyDef> policies, ActivistMemory memory,
        PolicyCounter counter)
    {
        var country = state.Countries.Values.Single();
        memory.Values.Add(new Dictionary<string, double>(country.Values));
        if (memory.Values.Count > 5) memory.Values.RemoveAt(0);
        var party = pol.Party!;
        var def = packs.Parties.First(p => p.Id == pol.PlayerParty);
        var core = packs.VoterGroups.OrderByDescending(g => def.Affinity.GetValueOrDefault(g.Id) * g.Size + 0.01 * (party.GroupSupport?.GetValueOrDefault(g.Id) ?? 0))
            .ThenBy(g => g.Id, StringComparer.Ordinal).Take(3).ToList();
        double Pc() => game.State.Countries.Values.Single().Politics!.Party!.PoliticalCapital;
        const double reserve = 2;

        // 1. promises: the party's own unmet goals, in the window before an election
        if (pol.TurnsToElection <= PoliticsLimits.PromiseWindowTurns && pol.Phase != Phase.Formation)
            foreach (var d in def.Demands.OrderByDescending(d => d.Weight).ThenBy(d => d.Id, StringComparer.Ordinal))
                if (Pc() >= PartyRules.PromiseCost + reserve) game.Apply(new MakePromise(d.Id));

        // 2. one proposal: the platform position that moves the core groups' support most (size-weighted), if any gains
        if (Pc() >= PoliticsLimits.ProposeToLeaderCost + reserve)
        {
            var current = country.Policies;
            (string Id, double Target, double Gain)? best = null;
            foreach (var (id, target) in party.Platform.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!policies.TryGetValue(id, out var pd) || Math.Abs(target - LatestPolicy(game, current, id)) < 0.05 * (pd.Max - pd.Min)) continue;
                if (game.PreviewProposal(id, target) is not { ErrorKey: null } pv || pv.Reaction.Credibility < 0) continue;
                double gain = packs.VoterGroups.Sum(g => g.Size * pv.SupportPoints.GetValueOrDefault(g.Id));
                if (gain > 0 && (best == null || gain > best.Value.Gain)) best = (id, target, gain);
            }
            if (best is { } b && game.Apply(new ProposePolicy(b.Id, b.Target)).Accepted) counter.Announcements++;
        }

        // 3. criticism: the value that got worse for the core groups over the last four quarters
        if (Pc() >= PartyRules.CriticizeCost + reserve && memory.Values.Count > 1)
        {
            var then = memory.Values[0];
            string? worst = null; double bestScore = 0;
            foreach (var v in packs.Values.OrderBy(v => v.Id, StringComparer.Ordinal))
            {
                double delta = country.Values[v.Id] - then[v.Id], score = 0;
                if (delta == 0) continue;
                foreach (var g in core)
                {
                    double w = g.ValueWeights.GetValueOrDefault(v.Id);
                    score += g.Size * Math.Abs(w) * (Math.Sign(w) * Math.Sign(delta) < 0 ? 1 : -1);
                }
                if (score > bestScore) { bestScore = score; worst = v.Id; }
            }
            if (worst != null) game.Apply(new Criticize(worst));
        }

        // 4. before an election, court the closest seated party it would need as a partner (relations lower its price in coalition talks)
        if (pol.TurnsToElection <= PoliticsLimits.PromiseWindowTurns && Pc() >= PartyRules.CourtCost + reserve)
        {
            var compass = game.Compass;
            var me = compass.GetValueOrDefault(pol.PlayerParty);
            var partner = pol.Seats.Where(kv => kv.Value > 0 && kv.Key != pol.PlayerParty && party.Relations.GetValueOrDefault(kv.Key) < 0.6)
                .OrderBy(kv => Math.Abs(compass.GetValueOrDefault(kv.Key).X - me.X) + Math.Abs(compass.GetValueOrDefault(kv.Key).Y - me.Y)).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key).FirstOrDefault();
            if (partner != null) game.Apply(new Court(partner));
        }

        // 5. organisation to a half (more income and turnout), then campaigns among the core group where they win most
        if (party.Organization < 0.5 && Pc() >= PartyRules.OrgBuildCost + reserve + PartyRules.CampaignCostPerLevel) game.Apply(new BuildOrganization());
        int effort = (int)Math.Min(3, Math.Floor((Pc() - reserve) / PartyRules.CampaignCostPerLevel));
        if (effort >= 1 && core.Count > 0)
        {
            var target = core.OrderByDescending(g => g.Size * game.PreviewCampaign(g.Id, effort)).ThenBy(g => g.Id, StringComparer.Ordinal).First();
            game.Apply(new Campaign(target.Id, effort));
        }
    }

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
