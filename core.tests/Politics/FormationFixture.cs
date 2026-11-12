using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Gameplay;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Builds small political worlds. Scenario "base:f" starts with an election after turn 1 at the given support.</summary>
internal static class FormationFixture
{
    public sealed record Spec(string Id, double Support, double Price = 0, double Tax = 30, bool Swing = false, string DemandPolicy = "base:spend");

    public static PackSet Load(Spec[] parties, string player, string[]? government = null, string? leader = null,
        int interval = 16, int turnsToElection = 1, int maxFormation = 4, int caretaker = 2, int slots = 6,
        string? redLines = null, string? agreements = null)
    {
        return GameFixture.Load(f =>
        {
            f["parties/p.yaml"] = string.Join("", parties.Select(p =>
                $"- id: base:{p.Id}\n  swing: {(p.Swing ? "true" : "false")}\n  price: {p.Price}\n  platform:\n    base:tax: {{target: {p.Tax}, weight: 1}}\n" +
                $"  demands:\n    - {{id: d1, kind: policy, policy: {p.DemandPolicy}, op: \">=\", target: 41, weight: 3}}\n" +
                $"    - {{id: d2, kind: policy, policy: {p.DemandPolicy}, op: \">=\", target: 42, weight: 2}}\n" +
                $"    - {{id: d3, kind: policy, policy: {p.DemandPolicy}, op: \">=\", target: 43, weight: 1}}\n" +
                (redLines ?? "")));
            f["electoral_systems/pr.yaml"] = "- id: base:pr\n  type: proportional\n  seats: 200\n  method: dhondt\n  threshold: 0.05\n";
            f["countries/c.yaml"] = "- id: base:czechia\n  name_key: country.czechia\n  politics:\n    electoral_system: base:pr\n" +
                $"    election_interval_turns: {interval}\n    majority_fraction: 0.5\n    max_formation_turns: {maxFormation}\n    caretaker_turns: {caretaker}\n    program_slots: {slots}\n";
            var gov = government ?? new[] { parties[0].Id };
            f["scenarios/s.yaml"] =
                "- id: base:test\n  country: base:czechia\n  start_year: 2015\n  turns_per_year: 4\n" +
                "- id: base:f\n  country: base:czechia\n  start_year: 2015\n  turns_per_year: 4\n  politics:\n" +
                $"    player_party: base:{player}\n    support: {{{string.Join(", ", parties.Select(p => $"base:{p.Id}: {p.Support}"))}}}\n" +
                $"    government: [{string.Join(", ", gov.Select(g => "base:" + g))}]\n    leader: base:{leader ?? gov[0]}\n    turns_to_election: {turnsToElection}\n" +
                (agreements == null ? "" : "    agreements:\n" + agreements);
            foreach (var lang in new[] { "cs", "en" })
                f[$"localization/{lang}.yaml"] += string.Join("", parties.Select(p => $"name.base:{p.Id}: {lang}-{p.Id}\n"));
        });
    }

    public static Game New(PackSet packs, ulong seed = 1) => Simulation.NewGame(packs, "base:f", seed);
    public static CountryPolitics Pol(Game g) => g.State.Countries.Values.Single().Politics!;

    /// <summary>Ends turns until the predicate holds on a report; returns all reports.</summary>
    public static List<TurnReport> Run(Game g, int turns)
    {
        var reports = new List<TurnReport>();
        for (int i = 0; i < turns; i++) reports.Add(g.EndTurn());
        return reports;
    }

    public static IEnumerable<T> Events<T>(IEnumerable<TurnReport> rs) where T : PoliticalEvent =>
        rs.SelectMany(r => r.PoliticalEvents).OfType<T>();
}
