using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;

namespace OpenPolitics.Core.Tests.Politics;

public class DemandPriorityTests
{
    private static PartyDef Party(string id, double price, double taxWeight, double spendWeight) => new(
        id, "n", new Dictionary<string, PlatformPosition>
        {
            ["base:tax"] = new(30, taxWeight), ["base:spend"] = new(40, spendWeight),
        }, new Dictionary<string, double>(), false,
        new[]
        {
            new DemandDef("tax", "policy", "base:tax", "<=", 25, 2, 8),
            new DemandDef("spend", "policy", "base:spend", ">=", 45, 2, 8),
        }, Array.Empty<RedLineDef>(), price, Array.Empty<FactionDef>(), "t");

    private static IReadOnlyList<AgreedDemand> Agreements(double taxWeight, double spendWeight)
    {
        var parties = new Dictionary<string, PartyDef>
        {
            ["base:lead"] = Party("base:lead", 0, 1, 1),
            ["base:partner"] = Party("base:partner", 2, taxWeight, spendWeight),
        };
        var seats = new Dictionary<string, int> { ["base:lead"] = 80, ["base:partner"] = 80, ["base:other"] = 40 };
        var ranges = new Dictionary<string, double> { ["base:tax"] = 60, ["base:spend"] = 80 };
        var proposal = CoalitionBuilder.Build("base:lead", parties, seats, ranges, 0.5, 6, (_, _) => 0.0, new HashSet<string>())!;
        return proposal.Agreements;
    }

    [Fact]
    public void ThePartnerAsksFirstForWhatItsPlatformStresses()
    {
        Assert.Equal("tax", Assert.Single(Agreements(taxWeight: 1.0, spendWeight: 0.2)).DemandId);
        Assert.Equal("spend", Assert.Single(Agreements(taxWeight: 0.2, spendWeight: 1.0)).DemandId);
    }
}
