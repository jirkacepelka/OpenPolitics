using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Politics;
using Xunit;
using Xunit.Abstractions;

namespace OpenPolitics.Core.Tests.Content;

/// <summary>M2b gate: formation works for every election (or ends in early elections), never gets stuck, is deterministic.</summary>
public class FormationGateTests(ITestOutputHelper output)
{
    private static readonly Core.Packs.PackSet Packs = BasePackTests.LoadBase().Packs!;
    private const int Turns = 48;

    private sealed record Outcome(List<PoliticalEvent> Events, List<int> ElectionTurns);

    /// <summary>The player's party only ever uses 'auto' when it is formateur and accepts offers.</summary>
    private static Outcome Play(ulong seed)
    {
        var g = Simulation.NewGame(Packs, "base:cz_2015", seed);
        var events = new List<PoliticalEvent>();
        var elections = new List<int>();
        for (int i = 0; i < Turns; i++)
        {
            var pol = g.State.Countries.Values.Single().Politics!;
            if (pol.Formation is { PlayerIsFormateur: true } && g.SuggestCoalition() is { } s)
                g.Apply(new ProposeCoalition(s.Partners, s.Agreements));
            else if (pol.Formation?.Offer != null) g.Apply(new AcceptOffer());
            var r = g.EndTurn();
            events.AddRange(r.PoliticalEvents);
            if (r.PoliticalEvents.OfType<ElectionHeld>().Any()) elections.Add(r.Turn);
        }
        return new Outcome(events, elections);
    }

    private static string Signature(PoliticalEvent e) => e switch
    {
        ElectionHeld h => $"E{h.Turn}:" + string.Join(",", h.Seats.Select(kv => $"{kv.Key}={kv.Value}")),
        GovernmentFormed f => $"G{f.Turn}:{f.Government.Leader}:" + string.Join(",", f.Government.Parties) + ":" +
            string.Join(",", f.Government.Commitments.Select(c => c.PartyId + c.Demand.Id)),
        OfferMade o => $"O{o.Turn}:" + string.Join(",", o.Proposal.Partners),
        _ => e.GetType().Name + e.Turn,
    };

    [Fact]
    public void FormationGateOver1000Seeds()
    {
        int maxWait = 2 * 4 + 2 + 2; // two attempts at max_formation_turns, caretaker_turns, slack
        int elections = 0, formed = 0, early = 0, outcomesWithGovernment = 0;
        var sizes = new SortedDictionary<int, int>();
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            var o = Play(seed);
            foreach (var turn in o.ElectionTurns)
            {
                elections++;
                bool resolved = o.Events.Any(e => e.Turn > turn && e.Turn <= turn + maxWait && e is GovernmentFormed or EarlyElectionsCalled)
                    || turn + maxWait > Turns; // the run ended before the window closed
                Assert.True(resolved, $"seed {seed}: election at turn {turn} was never followed by a government or early elections");
            }
            foreach (var e in o.Events)
            {
                if (e is GovernmentFormed gf) { formed++; sizes[gf.Government.Parties.Count] = sizes.GetValueOrDefault(gf.Government.Parties.Count) + 1; }
                if (e is EarlyElectionsCalled) early++;
            }
            if (o.Events.OfType<GovernmentFormed>().Any()) outcomesWithGovernment++;
            if (seed % 100 == 0)
            {
                var again = Play(seed);
                Assert.Equal(o.Events.Select(Signature), again.Events.Select(Signature));
            }
        }
        output.WriteLine($"elections {elections}, governments formed {formed}, early-election calls {early}, games with a government {outcomesWithGovernment}/1000");
        output.WriteLine("coalition sizes (parties): " + string.Join(", ", sizes.Select(kv => $"{kv.Key}: {kv.Value}")));
        Assert.True(formed > 0);
    }

    [Fact]
    public void TheLargestPartyDoesNotAlwaysLead()
    {
        int governments = 0, largestLed = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var g = Simulation.NewGame(Packs, "base:cz_2015", seed);
            IReadOnlyDictionary<string, int>? seats = null;
            for (int i = 0; i < Turns; i++)
            {
                var pol = g.State.Countries.Values.Single().Politics!;
                if (pol.Formation is { PlayerIsFormateur: true } && g.SuggestCoalition() is { } s)
                    g.Apply(new ProposeCoalition(s.Partners, s.Agreements));
                else if (pol.Formation?.Offer != null) g.Apply(new AcceptOffer());
                foreach (var e in g.EndTurn().PoliticalEvents)
                {
                    if (e is ElectionHeld h) seats = h.Seats;
                    if (e is GovernmentFormed f && seats != null)
                    {
                        governments++;
                        string largest = seats.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
                        if (f.Government.Leader == largest) largestLed++;
                    }
                }
            }
        }
        double share = (double)largestLed / governments;
        output.WriteLine($"governments {governments}, led by the largest party {share:P0}");
        Assert.InRange(share, 0.40, 0.95);
    }
}
