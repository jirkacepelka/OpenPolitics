# 0009 – Wave 4: more policies, events and demands

Status: accepted (2026-11-15). Data only; no core change except the `axis_x` / `axis_y` fields of wave 3.

- **Policies 13 → 37.** Ten spending add-ons (`programs.yaml`: defence upgrade, culture and sport, public transport,
  housing support, family allowances, active labour policy, rural development, green subsidies, digital infrastructure,
  childcare), five add-on taxes (property, carbon, wealth, financial transaction, health excise) and nine regulatory
  levers (minimum wage, labour flexibility, rent control, drug liberalisation, university fees, retirement age, clean
  energy mix, business regulation, surveillance). **Ruling:** add-on spending and taxes start at 0, so the starting
  budget (and every scenario) is unchanged; the regulatory levers start at their current-law default. 53 new edges in
  `edges/content.yaml`, strengths in the range of the existing ones.
- **Events 5 → 26.** Shocks and booms with conditions on values (banking crisis, pandemic, flood, trade war, tech hub,
  wage pressure, deflation scare, ...). One-off value changes only; probabilities 0.3–5 % per quarter.
- **Voters and parties.** All six voter groups and all eight parties take positions on the new levers. **Ruling:** the
  new voter policy weights are scaled to 35 % of the first draft: at full size they added a standing dissatisfaction
  that made polls drift (173/200 seeds inside 8 points in 6 quarters instead of ≥ 95 %).
- **Demand catalogues.** Every party gained 2–3 demands on the new levers and five parties a new red line
  (minimum wage, wealth tax, labour flexibility, energy mix, surveillance). Contradicting demands (Civic Right "retire
  at 64 or later" vs People's Union "retire at 63 or earlier") are possible by design.
- All 24 new policies and 21 new events are localised in cs and en. Gate tests in
  `core.tests/Content/ContentBreadthTests.cs`.

## Gates

537/537 tests (goldens regenerated for the larger base pack); `validate data/base` OK; economy batches (passive, random,
extreme, 300 × 80) `Stable`; politics batches (ignorer, keeper, random, 200 × 80) `Stable`, no stuck games; formation gate over
1000 seeds passes.
