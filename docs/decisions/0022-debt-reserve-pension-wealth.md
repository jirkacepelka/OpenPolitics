# 0022 - The debt cannot go below zero, a state reserve, and private pensions as wealth

Status: accepted. Follows 0021.

## Problems
1. A surplus pushed `base:debt` below zero (the debt was a free-floating net position).
2. It was not clear whether the debt is nominal or a share of GDP.
3. The private pension pillar had a cost the player saw at once and effects he could not see: the funds were a hidden value, nothing in the
   voters' view depended on them, the state pension bill hardly followed them (the cost driver's elasticity of 0.45 diluted the effect), and the
   wealth and property taxes were levied on a multiple of GDP, not on the capital that the funds add to.

## Decisions
1. **The debt is never negative.** The net position (debt less reserve) moves by the deficit; a surplus repays the debt first and only then piles up in
   the new `base:reserve` (hidden value, CZK). The reserve earns the base real rate and is drawn down by a deficit before the state borrows again.
   Interest on the debt and the debt-risk premium still use the debt only. With no reserve the arithmetic is bit-identical to before.
2. **Units are stated.** `base:debt` is in crowns at constant prices (a real amount); `base:debt_pct` and the top bar show it as a share of GDP. The
   description of the debt says so.
3. **Funds are wealth.** `PensionModelV1` computes flows in one place (`PensionFlows`). 85 % of the funds' net saving (was 60 %) is invested at home and
   becomes capital. `base:pension_fund` enters the voters' view as a level weight (pensioners 0.2, workers, the middle and upper-middle class 0.08,
   families 0.05): people feel their funds grow. Property tax and wealth tax are now levied on `base:capital` (rates unchanged in meaning: `base_share`
   is the share of the capital taxed; parameters, edges, voter preferences and party targets were rescaled by 1/3 for the new base).
4. **The state pension bill follows the funds in full.** `pension_burden` = pensioners x (1 - cover)^(1/0.45), so that the cost driver's elasticity leaves
   exactly the state's share (the funds replace 80 % of what they pay out, at most 60 % of the bill).
5. **The effect is shown.** `ModelExplainer.ExplainPensions` (via `Game.ExplainPensions`, optionally as if a policy stood at a slider value) lists what flows
   in and out of the funds, how much goes into the capital, the share of the capital (the base of the wealth tax), how much of the state pensions the funds
   replace and what the state pays in, plus a ten-year outlook next to the funds left alone. The three pillar laws, the pensions law and the retirement age
   show this card in their dialog (it follows the slider); the Population screen shows it under the pension tiles.

## Consequences
At 1 % of GDP a year (about 46 bn CZK) the funds hold about 1.3 tn CZK after ten years instead of 0.57 tn, replace 8 % of the state pensions instead of
3.5 % and save the state about 33 bn CZK a year, while the payment is 46 bn: the pillar pays back in wealth, investment and voters' comfort, and only
partly in the budget, which is the real trade-off of a funded pillar.

## Addendum: the funds' own bubble, a history that survives a load, the slider first
- `base:pension_fund` is no longer hidden: it is a bubble on the map (spending sector, its own pictogram) with its own dialog, which holds the whole
  explanation card. A law dialog shows only the few lines that follow the slider (what the state pays, the funds in ten years with and without help,
  how much of the pensions they replace, the saving) and a button to the funds' dialog; the Population screen has the same button. The fund acts on the
  income classes through `pension_fund_to_mobility_up` (bounded: tanh). An edge from the fund to inequality was tried and dropped: it made growth show
  up as inequality through the fund, hid the direct effects of laws and the audit caught it.
- A loaded game gets its history (charts, "why did it change") back: `Simulation.Load` calls an optional callback after the start and after every
  replayed turn, and the session records history from it. Before, only the present was recorded.
- In a law dialog the slider is the first thing (it used to follow the effects, so it could be below the fold); explanations and effects come after.
