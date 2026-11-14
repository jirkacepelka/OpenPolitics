# 0008 – Wave 3: elections, formation, priorities, promises, compass

Status: accepted (2026-11-14). Continues `docs/specs/2026-11-14-playtest-round-design.md`.

## What changed

- **Electoral threshold, 0–10 %.** `GameOptions(ElectoralThreshold)` is passed to `Simulation.NewGame`, validated
  (`ArgumentOutOfRangeException` outside 0..0.10 or NaN), applied to the country's electoral system and saved in
  `SaveData.Threshold` (empty = the country's own 5 %). `Game.ElectoralThreshold` shows the effective value. The CLI has
  `play --threshold <percent>`.
- **Formation market.** `BeginFormation` ranks the seated parties by `CoalitionBuilder.MarketScore` instead of by seats:
  every party works out the coalition it could build; parties that can are ranked by
  `0.9 * coherence + 0.1 * formateur's share of the coalition's seats`, parties that cannot follow by seats; the party
  named by a fall or a no-confidence vote still goes first. **Ruling:** the sketch in the spec (0.6 / 0.4, +0.1 for the
  largest party) made the largest party lead 100 % of 1303 governments; 0.9 / 0.1 gives 72 % over 300 seeds (gate test
  `TheLargestPartyDoesNotAlwaysLead`, bounds 40–95 %). The cost is that formation fails a little more often.
- **Priorities.** `SetPriority(policy, weight)` changes a platform weight by at most 0.2 (cost 1 capital, credibility
  −0.1 per unit moved). Platform weights already fed voter distance; they now also decide what a partner asks for first
  in coalition talks (`demand.Weight * (0.5 + platform weight)`).
- **Promises.** `MakePromise(demandId)` – one of the player's own catalogue demands, within 8 turns of an election, at
  most two, cost 2 capital, small momentum gain among groups that favour the party. At the election each promise is
  checked: kept → credibility +0.1 and momentum; broken → credibility −0.2 and momentum loss. Events `PromiseKept`,
  `PromiseBroken`.
- **Compass.** Policies have `axis_x` (economic, left −1 … right +1) and `axis_y` (social, liberal −1 … authoritarian
  +1) loadings in -1..1 (validated, default 0). `Compass.Position(party, policies)`; `Game.Compass` gives every party's
  point with its current platform, so the player's point moves when they shift the platform.

## Gates

- 532/532 tests; `validate data/base` OK.
- Politics batches (200 × 80 turns): ignorer, keeper, random, extreme, activist all `Stable`, no stuck games.
- Old saves keep reading (the new `Threshold` field is optional); the action-log replay of earlier saves was already
  rejected by decision 0007.

## Not done here

Client controls for threshold (new game), compass, priorities and promises come with wave 5, because that wave rebuilds
the party and new-game screens.
