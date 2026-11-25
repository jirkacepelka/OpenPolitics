# 0018: Ministries for partners, a programme you edit, many more laws

Status: accepted. Date: 2026-11-25.

## Context
Playtest notes from the player: the leading party never offered a single minister for joining its government; programmes seemed to contain every
law and could not be edited; the game needed many more laws.

## Decision
1. **Ministries come with the seat at the table.** The AI formateur now gives every partner ministries in proportion to its seats in the coalition
   (rounded, at least one, at most `MaxPortfoliosPerPartner`, together at most two thirds of the cabinet), picked in rounds from the biggest partner
   down, each taking the portfolio it values most that is still free (`CoalitionBuilder.OfferMinistries`). They are a share of power on top of the
   price in policy demands, not a substitute for them: partners still have to be paid in agreements, which keeps commitments and loyalty alive.
   Reshuffles and patch-ups still pay with agreements only. In a counter-offer the player may ask for more ministries; only what the player asks for
   counts against the formateur's ceiling, the ministries already in the offer do not.
2. **The programme is a choice.** `AddToPlatform` puts a law into the programme at the setting in force with weight 0.4 (1 capital);
   `RemoveFromPlatform` takes one out (2 capital, 0.1 credibility; positions are not dropped for free because voters score a party on the laws it
   stands for). A programme holds 2 to 30 laws. Both actions are saved and replayed. The party screen shows `Programme: n of 30`, a minus on
   every law and an "Add program" button that lists the laws not in the programme by sector.
3. **48 new laws (88 in all)**: six taxes, eighteen spending programmes and twenty-four rules (civil liberties, labour, justice, health, security,
   economy). Taxes and programmes start at 0, rules at a neutral setting, so nothing changes until someone moves them. Every law has effects on
   the existing indicators, a place in a cabinet portfolio and, for some, a position in the AI parties' programmes.
4. **Voter preferences for the new laws sit close to the starting setting** (within 2 % of the range). Satisfaction pays a penalty for every law
   away from a group's preference, so a pref far from the start drains all groups at once and the polls drift (the passive-play drift test failed
   at 20 % of the range). The laws matter when they are moved, not by their mere existence.

## Consequences
Golden snapshots were regenerated (new preferences change satisfaction). Tests: ministry offers, programme actions and replay. Batches stay stable.
