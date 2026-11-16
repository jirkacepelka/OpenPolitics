using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using Xunit;
using static OpenPolitics.Core.Tests.Politics.FormationFixture;

namespace OpenPolitics.Core.Tests.Politics;

/// <summary>Decision 0016 (2): coalition talks that respect party differences - prices, red lines, half-way values, portfolios, rounds, counter-offers.</summary>
public class NegotiationTests
{
    // a (40 %) forms. b (20 %) sits close to a (tax 32 against 30), c (20 %) far away (tax 50) with a red line spend >= 45; both ask price 4.
    // d (15 %) is the far right of the compass (tax 60 with an economic axis) and z is small. Two portfolios: tax (value 2) and spend (value 3).
    private const string PartiesYaml =
        "- id: base:a\n  price: 0\n  platform:\n    base:tax: {target: 30, weight: 1}\n    base:spend: {target: 40, weight: 0.6}\n" +
        "  demands:\n    - {id: d1, kind: policy, policy: base:spend, op: \">=\", target: 41, weight: 1}\n" +
        "- id: base:b\n  price: 4\n  platform:\n    base:tax: {target: 32, weight: 1}\n    base:spend: {target: 46, weight: 1}\n" +
        "  demands:\n    - {id: more, kind: policy, policy: base:spend, op: \">=\", target: 46, weight: 3}\n" +
        "    - {id: slow, kind: policy, policy: base:slow, op: \">=\", target: 3, weight: 1}\n" +
        "    - {id: health, kind: value, value: base:health, op: \">=\", target: 90, weight: 2}\n" +
        "    - {id: lots, kind: policy, policy: base:spend, op: \">=\", target: 48, weight: 0.5}\n" +
        "- id: base:c\n  price: 4\n  platform:\n    base:tax: {target: 50, weight: 1}\n" +
        "  demands:\n    - {id: high_tax, kind: policy, policy: base:tax, op: \">=\", target: 45, weight: 3}\n" +
        "    - {id: slow, kind: policy, policy: base:slow, op: \">=\", target: 3, weight: 2}\n" +
        "    - {id: health, kind: value, value: base:health, op: \">=\", target: 90, weight: 2}\n" +
        "  red_lines:\n    - {policy: base:spend, op: \">=\", threshold: 45}\n" +
        "- id: base:d\n  price: 1\n  platform:\n    base:tax: {target: 60, weight: 1}\n" +
        "  demands:\n    - {id: max_tax, kind: policy, policy: base:tax, op: \">=\", target: 55, weight: 3}\n" +
        "- id: base:z\n  price: 0\n  platform:\n    base:tax: {target: 40, weight: 1}\n  demands: []\n";

    private const string Portfolios =
        "    portfolios:\n      - {id: base:min_tax, value: 2, policies: [base:tax]}\n      - {id: base:min_spend, value: 3, policies: [base:spend]}\n" +
        "      - {id: base:min_slow, value: 1, policies: [base:slow]}\n";

    private static PackSet Packs(string player = "a") => Load(
        new[] { new Spec("a", 0.40), new Spec("b", 0.20), new Spec("c", 0.20), new Spec("d", 0.15), new Spec("z", 0.05) }, player,
        government: new[] { "z" }, leader: "z", turnsToElection: 1, interval: 60, portfolios: Portfolios,
        mutate: f =>
        {
            f["parties/p.yaml"] = PartiesYaml;
            f["policies/p.yaml"] = f["policies/p.yaml"].Replace("- id: base:tax\n", "- id: base:tax\n  axis_x: 1\n");
            foreach (var lang in new[] { "cs", "en" })
                f[$"localization/{lang}.yaml"] += "name.base:min_tax: T\nname.base:min_spend: S\nname.base:min_slow: L\n";
        });

    /// <summary>After the election a is the formateur (or b receives a's offer when b is the player).</summary>
    private static Game Formateur(string player = "a")
    {
        var g = New(Packs(player));
        g.EndTurn();
        for (int i = 0; i < 3 && Pol(g).Formation is not ({ PlayerIsFormateur: true } or { Offer: not null }); i++) g.EndTurn();
        return g;
    }

    private static string P(string id) => "base:" + id;
    private static AgreedDemand Ag(string party, string demand, double? target = null) => new(P(party), demand, target);
    private static CoalitionProposal Draft(string[] partners, AgreedDemand[]? agreements = null, PortfolioGrant[]? portfolios = null, Concession[]? concessions = null) =>
        new(P("a"), partners.Select(P).ToList(), agreements ?? Array.Empty<AgreedDemand>(), portfolios, concessions);
    private static PartnerView View(Game g, CoalitionProposal draft, string partner) => g.AssessCoalition(draft)!.Partners.Single(v => v.PartyId == P(partner));

    [Fact]
    public void ThePriceDependsOnIdeologicalDistance()
    {
        var g = Formateur();
        Assert.True(Pol(g).Formation!.PlayerIsFormateur);
        var b = View(g, Draft(new[] { "b" }), "b");
        var c = View(g, Draft(new[] { "c" }), "c");
        Assert.Equal(b.BasePrice, c.BasePrice);
        Assert.True(b.Distance < c.Distance);
        Assert.True(b.DistanceFactor < c.DistanceFactor);
        Assert.True(b.Required < c.Required, $"close {b.Required} vs far {c.Required}");
    }

    [Fact]
    public void APartyTooFarAwayRefusesWhateverIsOffered()
    {
        var g = Formateur();
        var d = View(g, Draft(new[] { "d" }, new[] { Ag("d", "max_tax") }), "d");
        Assert.True(d.Distance >= Negotiation.DistanceVeto, $"distance {d.Distance}");
        Assert.Equal(("refuse", "distance"), (d.Verdict, d.Reason));
        Assert.Equal("error.refused.distance", g.Apply(new ProposeCoalition(new[] { P("d"), P("b") }, new[] { Ag("d", "max_tax"), Ag("b", "more"), Ag("b", "slow") })).ErrorKey);
    }

    [Fact]
    public void EachDemandIsAcceptableNegotiableRedLineOrMet()
    {
        var g = Formateur();
        var b = View(g, Draft(new[] { "b", "c" }), "b");
        var more = b.Demands.Single(x => x.DemandId == "more");
        Assert.Equal("red_line", more.Status); // spend >= 46 crosses c's red line (spend >= 45)
        Assert.Equal(P("c"), more.RedLineOf);
        Assert.Equal(43.0, more.HalfWay); // between a's 40 and b's 46
        Assert.Equal("acceptable", b.Demands.Single(x => x.DemandId == "slow").Status); // a has no position on slow
        var alone = View(g, Draft(new[] { "b" }), "b");
        Assert.Equal("negotiable", alone.Demands.Single(x => x.DemandId == "more").Status);
        var c = View(g, Draft(new[] { "c" }), "c");
        Assert.Equal("negotiable", c.Demands.Single(x => x.DemandId == "high_tax").Status);
    }

    [Fact]
    public void ARedLineBlocksAndAHalfWayValueCanAvoidIt()
    {
        var g = Formateur();
        var full = Draft(new[] { "b", "c" }, new[] { Ag("b", "more"), Ag("b", "slow"), Ag("c", "high_tax"), Ag("c", "slow") });
        var c = View(g, full, "c");
        Assert.Equal(("refuse", "red_line", "base:b:more"), (c.Verdict, c.Reason, c.ReasonDemand));
        Assert.Contains(c.Fixes, f => f.Kind == "half_way" && f.DemandId == "more" && f.Amount == 43.0);
        var half = Draft(new[] { "b", "c" }, new[] { Ag("b", "more", 43), Ag("b", "slow"), Ag("c", "high_tax"), Ag("c", "slow") });
        Assert.NotEqual("red_line", View(g, half, "c").Reason);
        var b = View(g, half, "b");
        Assert.Equal(1.5, b.Demands.Single(x => x.DemandId == "more").Worth, 9); // half the way pays half of weight 3
    }

    [Fact]
    public void ACounterValueOutsideTheRangeOrOnAnAcceptableDemandIsUnknown()
    {
        var g = Formateur();
        Assert.Equal("error.refused.unknown", g.AssessCoalition(Draft(new[] { "b" }, new[] { Ag("b", "more", 50) }))!.HardError);
        Assert.Equal("error.refused.unknown", g.AssessCoalition(Draft(new[] { "b" }, new[] { Ag("b", "slow", 2) }))!.HardError);
    }

    [Fact]
    public void PortfoliosPayTowardThePriceMoreWhenThePartnerCaresAboutThem()
    {
        var g = Formateur();
        var b = View(g, Draft(new[] { "b" }), "b");
        Assert.Equal(3.0, b.Portfolios.Single(x => x.PortfolioId == P("min_spend")).Worth, 9); // b's spend weight 1: full value
        var c = View(g, Draft(new[] { "c" }), "c");
        Assert.Equal(1.5, c.Portfolios.Single(x => x.PortfolioId == P("min_spend")).Worth, 9); // c does not care about spend: half
        var with = View(g, Draft(new[] { "b" }, portfolios: new[] { new PortfolioGrant(P("b"), P("min_spend")) }), "b");
        Assert.Equal(b.Offered + 3.0, with.Offered, 9);
        // no portfolio twice, and a partner cannot take more than the limit
        Assert.Equal("error.refused.unknown", g.AssessCoalition(Draft(new[] { "b", "c" },
            portfolios: new[] { new PortfolioGrant(P("b"), P("min_spend")), new PortfolioGrant(P("c"), P("min_spend")) }))!.HardError);
    }

    [Fact]
    public void APartnerCountersAndTheFormateurCanTakeItsTermsInTheNextRound()
    {
        var g = Formateur();
        double relation = Pol(g).Party!.Relations[P("b")];
        var r1 = g.Apply(new NegotiateCoalition(new[] { P("b") }, Array.Empty<AgreedDemand>()));
        Assert.True(r1.Accepted);
        Assert.Equal("replies", r1.Detail);
        var f = Pol(g).Formation!;
        Assert.Equal(PoliticsLimits.NegotiationRoundsPerQuarter - 1, f.RoundsLeft);
        var reply = Assert.Single(f.Replies!);
        Assert.Equal(("counter", "price"), (reply.Verdict, reply.Reason));
        Assert.True(reply.Shortfall > 0);
        Assert.Equal(relation, Pol(g).Party!.Relations[P("b")], 12); // a counter costs no patience
        var terms = reply.Counter!;
        var r2 = g.Apply(new NegotiateCoalition(new[] { P("b") }, terms.Agreements, terms.Portfolios));
        Assert.Equal("formed", r2.Detail);
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        Assert.Equal(new[] { P("a"), P("b") }, Pol(g).GovernmentParties);
        Assert.All(terms.Portfolios, t => Assert.Equal(P("b"), Pol(g).Government!.PortfolioHolders[t.PortfolioId]));
    }

    [Fact]
    public void ARefusalSaysWhyWithNumbersAndCostsALittlePatienceAndRoundsRunOut()
    {
        var g = Formateur();
        // c alone cannot be paid with one demand while b holds every slot but one... simpler: offer c only a red-line-crossing partner
        var crossing = new NegotiateCoalition(new[] { P("b"), P("c") }, new[] { Ag("b", "more"), Ag("c", "high_tax"), Ag("c", "slow") });
        double before = Pol(g).Party!.Relations[P("c")];
        for (int i = 0; i < PoliticsLimits.NegotiationRoundsPerQuarter; i++) Assert.True(g.Apply(crossing).Accepted);
        var c = Pol(g).Formation!.Replies!.Single(x => x.PartyId == P("c"));
        Assert.Equal(("refuse", "red_line"), (c.Verdict, c.Reason));
        Assert.True(Pol(g).Party!.Relations[P("c")] < before);
        Assert.Equal("error.no_rounds", g.Apply(crossing).ErrorKey);
        g.EndTurn();
        Assert.Equal(PoliticsLimits.NegotiationRoundsPerQuarter, Pol(g).Formation!.RoundsLeft); // a new quarter, new rounds
    }

    [Fact]
    public void ConcessionsCostCapitalAndWinGoodwill()
    {
        var g = Formateur();
        var b = View(g, Draft(new[] { "b" }, new[] { Ag("b", "slow") }), "b");
        int units = (int)Math.Ceiling(Math.Max(0, b.Required - b.Offered) / Negotiation.ConcessionWorth);
        Assert.InRange(units, 1, Negotiation.MaxConcessions);
        double capital = Pol(g).Party!.PoliticalCapital, relation = Pol(g).Party!.Relations[P("b")];
        var r = g.Apply(new ProposeCoalition(new[] { P("b") }, new[] { Ag("b", "slow") }, null, new[] { new Concession(P("b"), units) }));
        Assert.True(r.Accepted, r.ErrorKey);
        Assert.Equal(capital - Negotiation.ConcessionCost * units, Pol(g).Party!.PoliticalCapital, 9);
        Assert.True(Pol(g).Party!.Relations[P("b")] > relation);
    }

    [Fact]
    public void TheAiFormateurUsesTheSameTalks()
    {
        var g = Formateur();
        var s = g.SuggestCoalition()!;
        Assert.Equal(new[] { P("b") }, s.Partners); // the close partner, not the far one
        var view = g.AssessCoalition(s)!;
        Assert.True(view.AllAccept);
        Assert.True(g.Apply(ProposeCoalition.From(s)).Accepted);
    }

    [Fact]
    public void AnInvitedPlayerCanCounterOnceAndTheFormateurAnswersAtOnce()
    {
        var g = Formateur("b");
        var offer = Pol(g).Formation!.Offer!;
        Assert.Equal(P("a"), offer.Formateur);
        // a cares about spend (weight 0.6 >= 0.5): asking for more spending goes against its platform
        Assert.Equal("platform", g.PreviewCounter(new[] { Ag("b", "lots") }, Array.Empty<PortfolioGrant>())!.Reason);
        var extra = offer.Agreements.Any(x => x.DemandId == "slow") ? Ag("b", "health") : Ag("b", "slow");
        var preview = g.PreviewCounter(new[] { extra }, Array.Empty<PortfolioGrant>())!;
        Assert.True(preview.Accepts, preview.Reason);
        var r = g.Apply(new CounterOffer(new[] { extra }, Array.Empty<PortfolioGrant>()));
        Assert.Equal("accepted", r.Detail);
        Assert.Equal(Phase.Governing, Pol(g).Phase);
        Assert.Contains(Pol(g).Government!.Commitments, c => c.PartyId == P("b") && c.Demand.Id == extra.DemandId);
    }

    [Fact]
    public void ARefusedCounterLeavesTheOfferStandingAndOnlyOneIsAllowed()
    {
        var g = Formateur("b");
        var r = g.Apply(new CounterOffer(new[] { Ag("b", "lots") }, Array.Empty<PortfolioGrant>()));
        Assert.Equal("refused:platform", r.Detail);
        Assert.NotNull(Pol(g).Formation!.Offer);
        Assert.Equal("platform", Pol(g).Formation!.CounterAnswer);
        Assert.Equal("error.counter_used", g.Apply(new CounterOffer(new[] { Ag("b", "slow") }, Array.Empty<PortfolioGrant>())).ErrorKey);
        Assert.True(g.Apply(new AcceptOffer()).Accepted);
    }

    [Fact]
    public void TalksAndCountersReplayFromTheSave()
    {
        var packs = Packs();
        var g = New(packs);
        g.EndTurn();
        Assert.True(g.Apply(new NegotiateCoalition(new[] { P("b") }, new[] { Ag("b", "more", 43) }, new[] { new PortfolioGrant(P("b"), P("min_slow")) })).Accepted);
        var view = View(g, Draft(new[] { "b" }, new[] { Ag("b", "more", 43) }, new[] { new PortfolioGrant(P("b"), P("min_slow")) }), "b");
        var terms = new List<AgreedDemand> { Ag("b", "more", 43) };
        var r = g.Apply(new ProposeCoalition(new[] { P("b") }, terms, new[] { new PortfolioGrant(P("b"), P("min_spend")), new PortfolioGrant(P("b"), P("min_slow")) },
            new[] { new Concession(P("b"), 1) }));
        Assert.True(r.Accepted, r.ErrorKey + " " + view.Shortfall);
        var loaded = Simulation.Load(packs, SaveData.FromYaml(g.Save().ToYaml()));
        Assert.Equal(Pol(g).Party!.PoliticalCapital, Pol(loaded).Party!.PoliticalCapital, 12);
        Assert.Equal(Pol(g).Government!.PortfolioHolders, Pol(loaded).Government!.PortfolioHolders);
        Assert.Equal(43.0, Pol(loaded).Government!.Commitments.Single(c => c.Demand.Id == "more").Demand.Target);
    }

    [Fact]
    public void APartnerHoldingThePortfolioIsHeardMoreOnItsPolicies()
    {
        // the player b joins a's government: once with the slow portfolio (its counter-offer), once without
        var holding = Formateur("b");
        var r = holding.Apply(new CounterOffer(Array.Empty<AgreedDemand>(), new[] { new PortfolioGrant(P("b"), P("min_slow")) }));
        Assert.Equal("accepted", r.Detail);
        Assert.Equal(P("b"), Pol(holding).Government!.PortfolioHolders[P("min_slow")]);
        var plain = Formateur("b");
        Assert.True(plain.Apply(new AcceptOffer()).Accepted);
        double with = holding.PreviewUltimatum("demand", "slow", 3)!.Score, without = plain.PreviewUltimatum("demand", "slow", 3)!.Score;
        Assert.Equal(PoliticsLimits.PortfolioProposalBonus, with - without, 9);
    }
}
