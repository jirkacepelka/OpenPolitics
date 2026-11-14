# 0006 – Wave 1 model fixes (playtest round)

Date: 2026-11-14. Spec: `docs/specs/2026-11-14-playtest-round-design.md`.

## Changes
- **Attribution of blame** (`SupportModel`, `PoliticsEngine`): blame is `kGov * sat * (a - 0.5 * (1 - a))` with `a` in [0, 1] per party,
  moving toward its role (leader 1.0, partner 0.7, opposition 0.0) with a half-life of 4 turns. Entering government at
  mood −1 now costs a party 30 % of its poll over two quarters instead of 70 % in one.
- **Grudge** (`PoliticsEngine`, `CoalitionRules`, `CoalitionBuilder`, `LoyaltyModel.StartLoyalty`): leaving costs
  `0.5 + 0.1 per unmet commitment` towards the leader (leader towards leaver 0.3), a fall adds 0.2 for all remaining
  partners; half-life 8 turns. A grudge of 0.5 or more vetoes a coalition (`error.refused.grudge`), below that it raises
  the price by `(1 + grudge)`. Start loyalty is `clamp(70 + 20*(relation-0.3)/0.7 - 30*grudge, 20, 85)`.
- **Prices**: `base:price_level`, `base:gdp_nominal`, `base:real_growth` and `base:deficit_pct` (unit `percent_flow`,
  not range-checked by the batch) are owned by `macro_v1`.
- **Indexation** of spending: `SetIndexation(policy, gdp|inflation|none)`; CLI `index N mode`. Taxes are not indexed.
- **Level effects**: `level_weights` per voter group with a habit (EMA, half-life 3 years); GDP 1.0 for middle and high
  income, 0.5 for rural, health 1.0 for pensioners.
- **Batch**: nominal indices are exempt from the explosion check (runaway prices show in the inflation range).

## Gates (200 games x 80 turns, `cz_2015`)
| strategy | partner loss | mean turns to first loss | player poll (mean) | before (0005) |
|---|---|---|---|---|
| ignorer | 100 % | 8.0 | 17.4 % | 100 %, 8.0 |
| keeper | 82.5 % | 31.1 | 17.5 % | 99.4 %, 21.0 |
| activist | 100 % | 8.0 | 21.3 % (+3.9 vs ignorer) | +3.1 |

Every fall still ends with a new majority; no game stuck outside Governing; passive/random/extreme economy batches stable.

## Not done here (spec items deferred)
Tax rates displayed as rates on a base (kept `percent_of_gdp` units so party platforms and demands stay valid); the
client has no indexation control yet (wave 5).
