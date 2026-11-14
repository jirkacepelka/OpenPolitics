# 0011 – Election rules as policies, and the review of waves 3–5

Status: accepted (2026-11-15). Supersedes the "electoral threshold option" part of 0008.

## Election rules are policies (user feedback)

The user: the threshold is, like the style of elections, a policy and not fixed. So the new-game option (`GameOptions`,
`SaveData.Threshold`, `play --threshold`, the start-screen field) is gone. Instead:

- `base:electoral_threshold` (percent of the vote, 0–10, default 5) and `base:electoral_method` (0 D'Hondt, 1 Sainte-Laguë, 2 Hare
  quota; rounded to a whole number) are ordinary policies in `data/base/policies/elections.yaml`, delay 4 turns.
- The leader sets them like any policy; the AI governor, partner proposals and coalition demands treat them the same way. The engine reads them at
  the moment of the election (`PoliticsEngine.CurrentRules`), so a change counts from the next election. Packs without these policies keep the
  country's own rule. `Game.ElectoralThreshold` is the value in force.
- Small parties (Open Net, Green Wave, Workers' Left) demand a low threshold or a fairer method, big ones (Citizens' Movement, Civic Right) a
  high threshold: electoral reform is now part of coalition bargaining.
- `ProportionalSystem` implements D'Hondt, Sainte-Laguë and Hare largest remainders. Majoritarian systems remain out of scope.

## Findings of the independent review (c3f405f..HEAD) and what was done

| # | Finding | Action |
|---|---------|--------|
| 1 | Promises settled after the election poll, so they had no effect on the vote that settles them | settled before the poll on election day; tests `ABrokenPromiseCostsVotes…`, `AKeptPromiseBeatsABrokenOne…` |
| 2 | A promise could be made for a demand already met | refused with `error.already_met` |
| 3 | Events that changed GDP or interest rate were wiped out by the model next turn and caused a fake growth spike | shocks moved to persistent state (capital, debt, unemployment); test `EventsOnlyShockStateThatPersists` |
| 4 | Two new demands were met at the start (`no_wealth_tax`, `early_retirement`) | removed / retargeted to ≤ 62; test `NoNewDemandIsMetByTheStartingSettings` |
| 5 | `SetPriority` charged capital for no change | refused with `error.no_change` |
| 6 | `PreviewPolicy` listed groups the voter model ignores | lists only groups with both a preference and a weight. Indexation is still ignored in the budget figure (deferred) |
| 7 | `ElectoralThreshold` non-null without politics | moot now: derived from the engine |

Gates: 556/556 tests, `validate data/base` OK, politics batches (ignorer, keeper, random, extreme) and economy batches (passive, random, extreme)
`Stable`. Falls for a player who keeps the coalition dropped from 271 to 109 per 200 × 80 turns.
