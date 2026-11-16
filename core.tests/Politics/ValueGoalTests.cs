using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Tests.Content;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0014: value goals (balanced budget, lower crime, ...) are demands like any other, judged by the engine on the value.</summary>
public class ValueGoalTests
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private static PartyDef Party(string id, double price, params DemandDef[] demands) => new(id, "name." + id,
        new Dictionary<string, PlatformPosition>(Ord), new Dictionary<string, double>(Ord), false, demands, Array.Empty<RedLineDef>(), price,
        Array.Empty<FactionDef>(), "test");

    private static readonly DemandDef Budget = new("balanced_budget", "value", "base:deficit_pct", "<=", 0, 2, 16);

    [Fact]
    public void AValueGoalThatIsAlreadyMetPaysNoPrice()
    {
        var parties = new Dictionary<string, PartyDef>(Ord) { ["a"] = Party("a", 0), ["b"] = Party("b", 2, Budget) };
        var seats = new Dictionary<string, int>(Ord) { ["a"] = 60, ["b"] = 60, ["c"] = 80 };
        var proposal = new CoalitionProposal("a", new[] { "b" }, new[] { new AgreedDemand("b", "balanced_budget") });
        Assert.Empty(CoalitionRules.Evaluate(proposal, parties, seats, 0.5, 6, (_, _) => 0.0));
        var refusals = CoalitionRules.Evaluate(proposal, parties, seats, 0.5, 6, (_, _) => 0.0, alreadyMet: d => d.Kind == "value");
        Assert.Contains(refusals, r => r.Reason == "price" && r.PartyId == "b");
    }

    [Fact]
    public void TheAiFormateurDoesNotOfferAGoalThatIsAlreadyMet()
    {
        var other = new DemandDef("other", "policy", "base:x", ">=", 5, 1, 12);
        var parties = new Dictionary<string, PartyDef>(Ord) { ["a"] = Party("a", 0), ["b"] = Party("b", 1, Budget, other) };
        var seats = new Dictionary<string, int>(Ord) { ["a"] = 60, ["b"] = 60, ["c"] = 80 };
        var ranges = new Dictionary<string, double>(Ord) { ["base:x"] = 10 };
        var plain = CoalitionBuilder.Build("a", parties, seats, ranges, 0.5, 6, (_, _) => 0.0, new HashSet<string>(Ord))!;
        Assert.Contains(plain.Agreements, x => x.DemandId == "balanced_budget");
        var met = CoalitionBuilder.Build("a", parties, seats, ranges, 0.5, 6, (_, _) => 0.0, new HashSet<string>(Ord), alreadyMet: d => d.Kind == "value")!;
        Assert.DoesNotContain(met.Agreements, x => x.DemandId == "balanced_budget");
        Assert.Contains(met.Agreements, x => x.DemandId == "other");
    }

    [Fact]
    public void TheGovernorWorksOnAnUnmetValueGoalThroughAPolicyThatDrivesIt()
    {
        var packs = BasePackTests.LoadBase().Packs!;
        var policies = packs.Policies.ToDictionary(p => p.Id, Ord);
        var crime = new DemandDef("lower_crime", "value", "base:crime", "<=", 2600, 2, 16);
        var g = new Government("base:homeland_first", new[] { "base:homeland_first" }, new[] { new Commitment("base:homeland_first", crime, 2800, 16) }, 0);
        var leader = packs.Parties.Single(p => p.Id == "base:homeland_first");
        var values = new Dictionary<string, double>(Ord) { ["base:crime"] = 2800, ["base:deficit_pct"] = 0, ["base:unemployment"] = 5 };
        var move = Governor.Move(g, id => policies[id].Default, id => values.GetValueOrDefault(id), policies, 1, leader, null, Governor.Levers(packs));
        Assert.NotNull(move);
        var levers = Governor.Levers(packs)["base:crime"];
        var lever = Assert.Single(levers, l => l.PolicyId == move!.Value.PolicyId);
        // the move pushes crime down: a lever that lowers crime goes up, one that raises it goes down
        Assert.True(Math.Sign(move!.Value.Value - policies[lever.PolicyId].Default) == -Math.Sign(lever.Sign));
    }

    [Fact]
    public void LeversComeFromThePacksEdges()
    {
        var levers = Governor.Levers(BasePackTests.LoadBase().Packs!);
        Assert.Contains(levers["base:crime"], l => l.PolicyId == "base:police" && l.Sign < 0);
        Assert.Contains(levers["base:environment"], l => l.PolicyId == "base:environment_regulation" && l.Sign > 0);
        Assert.Contains(levers["base:inequality"], l => l.PolicyId == "base:social_benefits" && l.Sign < 0);
    }
}
