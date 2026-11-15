# 0013 – Party choice, coalition reshuffle, political capital costs

Status: accepted (2026-11-15). Three playtest requests from the user: choose your party at the start; something like a coalition
reshuffle; political capital favours the governing party, so changing laws should cost something and every party should earn a bit more.

## 1. Choosing a party

- `Simulation.NewGame(packs, scenario, seed, playerParty = null)`: any party of a political scenario can be the player's. The scenario's
  `player_party` is only the default. An unknown party, or a party in a scenario without politics, throws `ArgumentException`. The starting
  role follows from the scenario's `government` / `leader` (in `base:cz_2015`: Social Alliance leads, People's Union and Citizens' Movement are
  junior partners, the other five are in opposition). The starting poll and seats do not depend on the choice (test).
- `PartyChoice.Options(packs, scenario)` lists every party with poll share, seats, starting role and compass position, largest first, so clients
  never work these out themselves.
- **Saves** record the chosen party (`player_party:` in the YAML; always written, so a replay never depends on the scenario default). A save
  without the field loads with the scenario's party. **Ruling:** saves made before this change are still rejected, but by the existing mechanism:
  the base pack's hash changed (new `capital_cost` fields), so `Simulation.Load` throws `SaveMismatchException` exactly as in decision 0007. The
  format itself stays readable. An unknown party in a save is also a `SaveMismatchException`.
- **A player partner has no loyalty meter any more.** Before, a player who accepted an offer as a partner had a loyalty value like an AI partner and
  could be "led out" of the government by ultimatum or at loyalty 15, which made no sense for the player and becomes the common case now that
  partners can be chosen at the start. The player decides alone when to leave (`LeaveCoalition`). Effect on the keeper batch: 109 → 97 falls
  before any other change.
- CLI: `play --party <id>|list`, `run --party <id>`, `batch --politics --party <id>|all` (full id or the part after the colon). `--party all`
  prints one row per party.
- Godot start screen: under the scenario cards a card per party (colour, name, role pill, poll % and seats, a mini compass and its words),
  largest first, and a line explaining the chosen role. The welcome briefing depends on the role.
- Every role was walked through: policy changes stay leader-only (`error.not_leader` first, then the cost); partners propose, leave, campaign,
  court, promise; the opposition criticises, takes stances, calls no confidence, campaigns, courts, promises and joins formation after a fall or
  an election. The smoke test plays 30 quarters as Civic Right (opposition) through every screen; `PartyChoiceTests.EveryPartyCanBePlayedForTwentyYears`
  plays every party for 80 turns with the keeper, activist and reformer strategies (no stuck phase, no exception, no negative capital, every
  fall followed by a new majority).

### Outcome per party (200 games x 80 turns, `--party all`, after all changes)

"falls" counts every government that lost its majority in those games (not only the player's), "governing" the quarters with a sitting
government, "leads" the quarters in which the player led it, "lost-lead" the games in which a player who had led ended up out of the lead.

| party (start role) | keeper falls | keeper leads | activist falls | activist leads | activist poll | ignorer falls | before (keeper / ignorer falls, no costs, no reshuffle) |
|---|---|---|---|---|---|---|---|
| Citizens' Movement (partner) | 73 | 39 % | 695 | 64 % | 28.5 % | 443 | 80 / 461 |
| Social Alliance (leader) | 82 | 16 % | 679 | 33 % | 19.7 % | 337 | 97 / 374 |
| Civic Right (opposition) | 51 | 5 % | 477 | 35 % | 18.3 % | 126 | 93 / 170 |
| Workers' Left (opposition) | 76 | 0 % | 347 | 9 % | 23.3 % | 76 | 102 / 102 |
| People's Union (partner) | 67 | 36 % | 464 | 57 % | 9.1 % | 336 | 87 / 347 |
| Homeland First (opposition) | 43 | 0 % | 26 | 0 % | 12.1 % | 43 | 58 / 58 |
| Green Wave (opposition) | 73 | 0 % | 211 | 2 % | 12.1 % | 74 | 94 / 94 |
| Open Net (opposition) | 65 | 0 % | 93 | 1 % | 10.3 % | 65 | 86 / 86 |

No party got stuck in any run (0 stuck games everywhere). Citizens' Movement, the largest party, leads after most elections; the small
opposition parties (Homeland First, Green Wave, Open Net) almost never govern unless the player works for it (activist). That is the intended
difficulty spread: picking a small party is the hard mode.

## 2. Coalition reshuffle

`ReshuffleCoalition(Drop, Add, Agreements)` – as the leader of a sitting government: drop partners, bring in seated outside parties with the
agreements they ask for, or both.

- **Hard checks when asked** (refused for free, error keys): `error.wrong_phase`, `error.not_leader`, `error.reshuffle_cooldown` (detail: quarters
  left), `error.already_queued`, `error.unknown_party` (dropping the leader or a non-partner, adding a governing, unseated or unknown party),
  `error.no_change`, `error.would_lose_majority`, `error.refused.unknown` / `error.refused.slots`, `error.not_enough_capital`.
- **Cost** `3 + 2 per party dropped or added` (a swap costs 7), paid when asked. The parties answer **at the end of the quarter** by
  **`CoalitionRules.Evaluate`** – the same function as formation – extended with a `SittingCoalition` (partners that stay, agreements in force):
  the newcomers' price is checked (relation and grudge as at formation), grudges count when a newcomer is involved, new agreements are checked
  against every member's red lines and the old agreements against the newcomers' red lines, contradictions between new and old agreements count.
  The kept partners' price is not asked again and old grudges among those already sitting are ignored. The client and CLI show the answer in
  advance (`Game.PreviewReshuffle`), and `Game.SuggestReshuffle` proposes the agreements the AI would offer.
- **Refused**: everything but 2 capital comes back, the refusing party's relation drops 0.05, the next attempt may come after 2 quarters, event
  `ReshuffleRefused` (reason price / grudge / red_line / contradiction). If an election, a fall or a dismissal changes the situation before the
  answer, the whole cost comes back.
- **Done** (event `CoalitionReshuffled`): a dropped party resents the leader by 0.9 (it refuses to sit with it while the grudge is above the 0.5
  veto: about seven quarters, so it can turn into a hostile opposition after the next election), its relation with the player falls by 0.2, and
  the voter groups that like it lose 0.04 momentum (scaled by affinity) toward the player's party; partners who stay lose 5 loyalty; newcomers
  start at the formation loyalty and their agreements get fresh deadlines counted from today (`Commitment.StartTurn`; the new `Commitment.MidTerm`
  flag now marks mid-term demands, which the old code recognised by `StartTurn >= 0`). The next reshuffle may come after 8 quarters.
- `DismissPartner` stays as before (4 capital, no cooldown).
- **AI leaders**: when a partner walks out and the AI-led government loses its majority, the leader patches it with the closest smallest set of
  one or two seated outside parties that accepts the sitting coalition (`CoalitionBuilder.Complete`; never the player's party, never one whose
  red line is crossed right now), at most once per 8 quarters and **only with probability 0.5** (`PoliticsLimits.AiPatchChance`, a draw on
  the politics stream). **Ruling:** first implemented without the coin flip; that cut the keeper's falls from 97 to 55 and the ignorer's from 374
  to 294, outside the range of 0011. With the coin flip they are 82 and 337. AI leaders do not reshuffle proactively (swapping an unhappy partner
  before it leaves): not needed for the request and harder to keep balanced.
- Save/replay (`reshuffle_coalition` with `drop`, `partners`, `agreements`), texts in `Texts.Event`/`Texts.Error`/`Texts.ReshuffleReason`, cs and
  en keys, CLI `reshuffle [check] drop PARTY... add PARTY...` (agreements are the suggested ones; `check` only previews), Godot: a
  **Reshuffle the coalition** card at the bottom of the Politics screen for the leader (drop / bring-in checkboxes, demands per newcomer, seats
  held and needed, predicted answer, cost, cooldown, confirm button) with a jump button on the parliament card.

## 3. Political capital economy

Before: policy changes were free, only the leader could make them, and capital paid for campaigns and actions; the leader also earned the most
(income `2 + 8 x org x share + 2 for the leader +-3 for the coalition`).

- **Policy changes cost the leader capital**, charged when the change is accepted:
  `cost = round(capital_cost x (1 + 20 x |change| / range))`, at least 1 for any move (`PolicyCost`). The weight `capital_cost` (0–10, default 1)
  is new in policy definitions; the base pack sets 2–2.5 for pensions, income tax, the retirement age, the electoral threshold and method,
  migration; 1.5 for VAT, corporate tax, health, social benefits, minimum wage, wealth tax, drugs, surveillance, labour flexibility; 1.25 for
  education, defence, energy mix, rent control; 0.75 for the add-on programmes, the transaction tax and administration. So a tenth of the
  range costs 3 for an ordinary lever and 6 for pensions; one step of the electoral method costs 22 (affordable with saved capital, max 30).
  **Ruling:** a minimum of 1 per move (no free nudging; it also makes splitting a move into small steps dearer than one decisive step).
- The cost is measured from the value the policy will have once queued changes are in. **Cancelling a scheduled change** (back to the value in
  force) **is free and refunds nothing**. Indexation switches are free. Not enough capital → `error.policy_cost` with detail `cost|capital`
  ("This change costs 22 political capital; you have 6.").
- `Game.PolicyChangeCost`, `Game.AffordableRange` (the band the capital pays for), `PolicyPreview.CapitalCost` / `Affordable`. The policy dialog
  shows the cost of the dialled change, a green band on the slider and **Apply changes · N**, disabled when unaffordable; CLI `set` prints
  "Cost: N political capital (M left)" or refuses.
- **Income**: every party `3.5 + 8 x org x share` (base was 2), the leader of a sitting government `+1` (was +2 in every phase), the coalition
  `+-2` (was +-3). The flat advantage of leading fell from 2 + 3 to 1 + 2 points a quarter, and the leader now pays for its laws.
- **AI governments** stay free of player capital: the AI governor makes at most one measure per quarter and accepted partner proposals are paid by
  the proposing player (3 capital), unchanged. Without politics (sandbox) policy changes are free.
- A leader earns about 4.5–6.5 a quarter (18–26 a year): four to eight changes of a tenth of the range a year, or one big reform after saving.
  The keeper strategy makes 3.8 changes per year of leading and is never refused; the leader's mean capital while leading dropped from 30 (always
  at the cap) to 23.

New batch strategy **reformer**: governs like the AI governor with its own platform (commitments, crisis responses, drift), pays for every change
and reshuffles away a partner under ultimatum. New batch figures: share of quarters with a government, share the player leads, mean capital
(overall and while leading), policy changes accepted / refused for capital per year of leading, reshuffles.

## Gates: before and after (200 games x 80 turns, `base:cz_2015`, default party)

"Before" is the commit before this work (metrics added without changing behaviour); policy changes per year of leading are derived from its
per-game-year figure.

| strategy | falls before → after | governing quarters | player leads | player poll | leader capital | changes / refused per led year | stuck |
|---|---|---|---|---|---|---|---|
| ignorer | 369 → 337 | 87.9 → 88.1 % | 16.1 → 16.3 % | 15.2 → 15.1 % | 30 → 26.8 | 0 / 0 | 0 → 0 |
| keeper | 109 → 82 | 89.4 → 89.5 % | 16.1 → 16.3 % | 14.8 → 14.8 % | 30 → 23.4 | 3.8 / 0 → 3.8 / 0 | 0 → 0 |
| random | 412 → 341 | 87.5 → 88.0 % | 16.7 → 15.9 % | 14.9 → 15.0 % | 30 → 24.8 | 0.7 / 0 → 0.5 / 0.2 | 0 → 0 |
| extreme | 1329 → 335 | 82.9 → 88.2 % | 25.9 → 16.1 % | 12.7 → 15.3 % | 30 → 25.2 | 23 / 0 → 3.1 / 10.1 | 0 → 0 |
| activist | 618 → 679 | 86.1 → 86.0 % | 27.3 → 32.7 % | 18.8 → 19.7 % | 4.4 → 6.2 | 0 / 0 | 0 → 0 |
| reformer (new) | – → 80 | 89.6 % | 16.3 % | 14.8 % | 23.4 | 3.8 / 0, 10 reshuffles | 0 |

Every fall was followed by a new majority in every run. Where the falls come from: the AI patch (half of the walk-outs that would have toppled an
AI government no longer do), the player partner without a loyalty meter, and for random / extreme the refused policy changes (the "extreme"
stress strategy can no longer flip every lever every two years, so it no longer wrecks its coalitions 1300 times; it is refused ten times a year).
The activist earns more (higher base income) and so campaigns more: more polls, more leading, slightly more falls. Keeper and random still
govern without stuck phases; the keeper is never refused.

Economy batches (300 x 80, sandbox, `passive`, `random`, `extreme`): `Stable` before and after with identical figures (0 NaN, extreme-rate,
collapse and explosion games; informational debt spirals 0 / 157 / 300 as before). The sandbox has no politics, so policy changes there stay free.

Tests: 562 → 622 (`PartyChoiceTests`, `PlayPartyChoiceTests`, `PlayCapitalCostTests`, `PlayReshuffleTests`, `PolicyCapitalCostTests`,
`ReshuffleTests`, reformer in the batch theory). **Goldens were not regenerated**: they run the sandbox scenario, which neither reads
`capital_cost` nor has politics, and they still match. Existing tests about other mechanics that moved policies a long way with 6 capital
(threshold, promises, polls) now use `GameFixture.FreePolicies` (capital_cost 0 in their test packs); the income tests use the new constants.

## Known gaps

- The new UI was checked with windowed smoke screenshots (start screen with party cards, policy dialog with cost and band, reshuffle card), not
  by a human playtest. At 1280 x 720 logical points the party cards push the Start button below the fold of the start panel (it scrolls).
- The reshuffle card is at the bottom of the Politics screen (a button on the parliament card scrolls to it).
- AI leaders only patch a lost majority; they never reshuffle proactively, and never invite the player's party in a patch.
- The opposition has no capital-funded way to block or delay a measure; its capital use is unchanged (campaigns, criticism, stances,
  no confidence).
