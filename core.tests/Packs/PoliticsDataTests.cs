using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Tests.Packs;

public class PoliticsDataTests
{
    private const string Party =
        "- id: t:p1\n  platform:\n    t:tax: {target: 20, weight: 0.8}\n  affinity: {t:g1: 0.5}\n  swing: false\n" +
        "  demands:\n    - {id: d1, kind: policy, policy: t:tax, op: \"<=\", target: 22, weight: 3, due_turns: 8}\n" +
        "    - {id: d2, kind: value, value: t:gdp, op: \">=\", target: 90, weight: 1}\n" +
        "  red_lines:\n    - {policy: t:tax, op: \">=\", threshold: 40}\n  price: 4\n" +
        "  factions:\n    - {id: f1, weight: 1, platform: {t:tax: 18}}\n";

    private const string System = "- id: t:pr\n  type: proportional\n  seats: 200\n  method: dhondt\n  threshold: 0.05\n";

    private static void AddParty(Dictionary<string, string> f, string yaml = Party)
    {
        f["parties/p.yaml"] = yaml;
        f["localization/cs.yaml"] += "name.t:p1: Strana\n";
        f["localization/en.yaml"] += "name.t:p1: Party\n";
    }

    private static void AddPolitics(Dictionary<string, string> f, string support = "{t:p1: 1.0}", string player = "t:p1",
        string government = "[t:p1]", string electoral = "t:pr")
    {
        AddParty(f);
        f["electoral_systems/pr.yaml"] = System;
        f["countries/c.yaml"] = "- id: t:land\n  name_key: country.land\n  politics:\n    electoral_system: " + electoral +
            "\n    election_interval_turns: 16\n    majority_fraction: 0.5\n    max_formation_turns: 4\n    caretaker_turns: 2\n    program_slots: 6\n";
        f["scenarios/s.yaml"] = "- id: t:s\n  country: t:land\n  start_year: 2015\n  politics:\n    player_party: " + player +
            "\n    support: " + support + "\n    government: " + government + "\n    leader: t:p1\n    turns_to_election: 12\n";
    }

    [Fact]
    public void ReadsPartyWithAllFields()
    {
        var r = TestPacks.Load(f => AddParty(f));
        Assert.Empty(r.Diagnostics);
        var p = Assert.Single(r.Packs!.Parties);
        Assert.Equal("name.t:p1", p.NameKey);
        Assert.Equal(20.0, p.Platform["t:tax"].Target);
        Assert.Equal(0.8, p.Platform["t:tax"].Weight);
        Assert.Equal(0.5, p.Affinity["t:g1"]);
        Assert.False(p.Swing);
        Assert.Equal(2, p.Demands.Count);
        Assert.Equal("<=", p.Demands[0].Op);
        Assert.Equal("t:tax", p.Demands[0].Subject);
        Assert.Equal("value", p.Demands[1].Kind);
        Assert.Equal(8, p.Demands[0].DueTurns);
        Assert.Equal(40.0, p.RedLines.Single().Threshold);
        Assert.Equal(4.0, p.Price);
        Assert.Equal(18.0, p.Factions.Single().Platform["t:tax"]);
    }

    [Fact]
    public void ReadsElectoralSystemAndPoliticsBlocks()
    {
        var r = TestPacks.Load(f => AddPolitics(f));
        Assert.Empty(r.Diagnostics);
        var sys = Assert.Single(r.Packs!.ElectoralSystems);
        Assert.Equal((200, 0.05, "dhondt", "proportional"), (sys.Seats, sys.Threshold, sys.Method, sys.Type));
        var c = r.Packs.Countries.Single().Politics!;
        Assert.Equal((16, 0.5, 4, 2, 6), (c.ElectionIntervalTurns, c.MajorityFraction, c.MaxFormationTurns, c.CaretakerTurns, c.ProgramSlots));
        var s = r.Packs.Scenarios.Single().Politics!;
        Assert.Equal("t:p1", s.PlayerParty);
        Assert.Equal(1.0, s.Support["t:p1"]);
        Assert.Equal(12, s.TurnsToElection);
        Assert.Equal("t:p1", s.Leader);
    }

    [Fact]
    public void ScenariosAndCountriesWithoutPoliticsStillLoad()
    {
        var r = TestPacks.Load(TestPacks.Baseline());
        Assert.Null(r.Packs!.Countries.Single().Politics);
        Assert.Null(r.Packs.Scenarios.Single().Politics);
    }

    [Fact]
    public void PartyReferencingUnknownPolicyIsE004WithLine()
    {
        var r = TestPacks.Load(f => AddParty(f, Party.Replace("t:tax: {target: 20", "t:ghost: {target: 20")));
        var d = Assert.Single(r.Diagnostics, x => x.Code == "E004");
        Assert.Contains("t:ghost", d.Message);
        Assert.Equal("parties/p.yaml", d.File);
        Assert.Equal(1, d.Line);
    }

    [Theory]
    [InlineData("affinity: {t:g1: 0.5}", "affinity: {t:nobody: 0.5}")]
    [InlineData("policy: t:tax, op: \"<=\"", "policy: t:nope, op: \"<=\"")]
    [InlineData("value: t:gdp", "value: t:nope")]
    [InlineData("{policy: t:tax, op: \">=\", threshold: 40}", "{policy: t:nope, op: \">=\", threshold: 40}")]
    [InlineData("platform: {t:tax: 18}", "platform: {t:nope: 18}")]
    public void OtherPartyReferencesAreE004(string from, string to)
    {
        var r = TestPacks.Load(f => AddParty(f, Party.Replace(from, to)));
        Assert.Contains(r.Diagnostics, d => d.Code == "E004" && d.Message.Contains("t:nobody") | d.Message.Contains("t:nope"));
    }

    [Theory]
    [InlineData("type: proportional", "type: majoritarian")]
    [InlineData("method: dhondt", "method: sainte_lague")]
    [InlineData("seats: 200", "seats: 0")]
    [InlineData("threshold: 0.05", "threshold: 1.5")]
    public void BadElectoralSystemIsE007(string from, string to)
    {
        var r = TestPacks.Load(f => { AddPolitics(f); f["electoral_systems/pr.yaml"] = System.Replace(from, to); });
        Assert.Contains(r.Diagnostics, d => d.Code == "E007");
    }

    [Theory]
    [InlineData("op: \"<=\", target: 22", "op: \"==\", target: 22")]
    [InlineData("price: 4", "price: -1")]
    public void BadPartyNumbersOrOperatorsAreE007(string from, string to)
    {
        var r = TestPacks.Load(f => AddParty(f, Party.Replace(from, to)));
        Assert.Contains(r.Diagnostics, d => d.Code == "E007");
    }

    [Fact]
    public void SupportNotSummingToOneIsE007()
    {
        var r = TestPacks.Load(f => AddPolitics(f, support: "{t:p1: 0.7}"));
        Assert.Contains(r.Diagnostics, d => d.Code == "E007" && d.Message.Contains("sum"));
    }

    [Fact]
    public void PlayerPartyNotDefinedIsE004()
    {
        var r = TestPacks.Load(f => AddPolitics(f, player: "t:ghost"));
        Assert.Contains(r.Diagnostics, d => d.Code == "E004" && d.Message.Contains("t:ghost"));
    }

    [Fact]
    public void GovernmentPartyNotDefinedIsE004()
    {
        var r = TestPacks.Load(f => AddPolitics(f, government: "[t:p1, t:ghost]"));
        Assert.Contains(r.Diagnostics, d => d.Code == "E004" && d.Message.Contains("t:ghost"));
    }

    [Fact]
    public void CountryReferencingUnknownElectoralSystemIsE004()
    {
        var r = TestPacks.Load(f => AddPolitics(f, electoral: "t:nope"));
        Assert.Contains(r.Diagnostics, d => d.Code == "E004" && d.Message.Contains("t:nope"));
    }

    [Fact]
    public void MissingPartyNameIsE006()
    {
        var r = TestPacks.Load(f => { AddParty(f); f["localization/cs.yaml"] = f["localization/cs.yaml"].Replace("name.t:p1: Strana\n", ""); });
        Assert.Contains(r.Diagnostics, d => d.Code == "E006" && d.Message.Contains("name.t:p1"));
    }

    [Fact]
    public void DuplicatePartyIsE003()
    {
        var r = TestPacks.Load(f => { AddParty(f); f["parties/q.yaml"] = Party; });
        Assert.Contains(r.Diagnostics, d => d.Code == "E003");
    }

    [Fact]
    public void PartyWithUnknownFieldIsE002()
    {
        var r = TestPacks.Load(f => AddParty(f, Party + "  mascot: owl\n"));
        Assert.Contains(r.Diagnostics, d => d.Code == "E002" && d.Message.Contains("mascot"));
    }

    private static void AddAgreements(Dictionary<string, string> f, string agreements)
    {
        AddPolitics(f);
        f["scenarios/s.yaml"] += "    agreements:\n" + agreements;
    }

    [Fact]
    public void ReadsScenarioAgreements()
    {
        var r = TestPacks.Load(f => AddAgreements(f, "      - {party: t:p1, demand: d1}\n      - {party: t:p1, demand: d2}\n"));
        Assert.Empty(r.Diagnostics);
        var a = r.Packs!.Scenarios.Single().Politics!.Agreements!;
        Assert.Equal(new[] { ("t:p1", "d1"), ("t:p1", "d2") }, a.Select(x => (x.Party, x.Demand)).ToArray());
    }

    [Fact]
    public void AgreementWithUnknownDemandOrPartyIsE004()
    {
        var bad = TestPacks.Load(f => AddAgreements(f, "      - {party: t:p1, demand: nope}\n"));
        Assert.Contains(bad.Diagnostics, d => d.Code == "E004" && d.Message.Contains("nope"));
        var ghost = TestPacks.Load(f => AddAgreements(f, "      - {party: t:ghost, demand: d1}\n"));
        Assert.Contains(ghost.Diagnostics, d => d.Code == "E004" && d.Message.Contains("t:ghost"));
    }
}
