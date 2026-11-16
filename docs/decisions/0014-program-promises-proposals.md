# 0014 – Programmes, promises, proposals, fiscal blame, explanations, announced proposals

Status: accepted (2026-11-16). Playtest feedback from the user, items (a)–(d) first, then (e)–(g):
(a) the party programme is thin (add e.g. a balanced budget); (b) promises hold until the next election and only cost a party that governed;
(c) support / oppose a proposal right in the start-of-quarter popup; (d) the Overview heading says "Opposition goals" in government;
(e) austerity is punished too hard for the party that cleans up a deficit; (f) the party stats and actions are not explained anywhere;
(g) the opposition must be able to propose policies publicly, and voters must react.

## (a) Broader programmes with value goals

- **Platforms**: every party now takes a position on 19–22 of the 39 policies (was 9–13), consistent with its compass, demands and red lines
  (`ProgramTests`: 15–25 positions; every platform target satisfies the party's own policy demands and never crosses its own red lines). New
  positions carry lower weights (0.15–0.4) than the old core positions, so the old priorities still dominate.
- **Goals** are demands of `kind: value` (the existing mechanism): balanced budget (deficit ≤ 0 % of GDP), low debt (**new value
  `base:debt_pct`**, debt/GDP ≤ 35 %), low unemployment, inflation ≤ 3 %, strong growth (real growth ≥ 2.5 %), lower crime, greener environment,
  less inequality, better health, better education. 2–3 per party, 10 different goals in all, weight = priority (1–2.5), due in 16 quarters.
  Civic Right: balanced budget (weight 2), low debt (replaces the bland `high_threshold`), strong growth. Workers' Left: less inequality, low
  unemployment (weight 2). Green Wave: greener environment (replaces `low_unemployment`), better education. Homeland First: lower crime.
  Social Alliance: less inequality, better health. People's Union: lower crime, better health. Citizens' Movement: strong growth. Open Net:
  better education. Goals are usable as coalition demands, mid-term demands and promises.
- **A met goal is not a free demand**: a value goal that already holds pays nothing toward a partner's price (`CoalitionRules.Worth`, used by
  `Evaluate` with the new `alreadyMet` argument) and the AI formateur does not offer it (`CoalitionBuilder.Build/AllocateFor/Complete`). Policy
  demands keep their old rule (a met policy demand still binds the government not to undo it). The balanced budget is met at the start (surplus
  0.28 %), so it cannot be the cheap price of the first coalitions; `NoNewDemandIsMetByTheStartingSettings` still holds for policy demands.
- **AI governors work on value goals**: unmet `debt_pct` joins the deficit response, unmet `real_growth` the unemployment response; other unmet
  goals (crime, environment, inequality, health, education) move the strongest lever by a tenth of its range, found from the pack's additive edges
  policy → value (`Governor.Levers`). Deterministic, no draws.
- `debt_pct` is owned by `macro_v1` (`debt / real GDP × 100`), unit `percent_flow`; the economy batch skips `percent_flow` values in its
  collapse/explosion checks (they are ratios, not stocks; the deficit already was skipped by accident of its negative start value).
- **UI**: Program tab lists the goals and demands first (value now, weight, met / not met), then the platform sliders; the promise picker shows
  met / not met. CLI `party` shows the state of each demand.
- **Ruling (balance)**: the first draft brought Citizens' Movement close to Social Alliance (pensions, health, social benefits); the AI formateur
  then picked CM + CR + SA instead of coalitions with the erratic small parties, and the keeper's falls dropped from 82 to 7. Bisected (new
  platforms with old demands: 11 / 100 games; old platforms with new demands: 37 / 100). The CM positions were moved so that its distances to its
  natural partners stay as before (CM–CR 0.05 → 0.06, CM–PU 0.06 → 0.06, CM–SA 0.15 → 0.15); keeper back to 84.

## (b) Promises hold until the next election

- Verified: the engine already settled promises only at the election (`SettlePromises` on election day, including early and caretaker
  elections), never at the end of the quarter they were made in (`PromiseDeadlineTests.APromiseStaysOpenUntilTheElectionAndIsJudgedOnlyThere`).
  What the user saw was the UI: nothing said how long a promise holds. The window to make one (8 quarters before the election) is unchanged.
- **Responsibility**: each promise records the quarter it was made and counts the quarters since and those in which the party sat in the
  government (`Phase.Governing` and member of the government's parties, sampled at every quarter's end). It **counts against the party when it
  governed at least half of those quarters** (`PromiseInfo.CountsAgainst`). Responsible: kept +0.1 credibility and goodwill, broken −0.2 and lost
  goodwill (as before). Not responsible (mostly opposition): no penalty and no credit (events carry `Responsible`; texts say why). A partner that
  left early is not held to it (test).
- UI: the Promises tab shows "Holds until the next election (in N quarters). Now: met/not met" and whether breaking it would cost you
  ("you governed X of Y quarters"); hints, CLI help and the confirmation say "until the next election".

## (c) Proposals answered in the briefing

- **Measure** (AI-led government, player partner or opposition): when the AI governor's move of the quarter touches the player's platform and no
  proposal was settled in the last 2 quarters, the move is **put to parliament** instead of enacted (`Game.EndTurn` → `OfferMeasure`); the
  player answers during the next quarter; at its end it passes (enacted as a government measure, before the governor's next move) unless the
  player is a partner whose seats are pivotal and opposed it. **Request** (player leads, AI partners): the least loyal partner with something
  to ask asks for its own unmet policy commitment or its platform position furthest away (`Governor.Request`, a quarter of the way, never
  across a member's red line or undoing a met commitment); support enacts it free of capital (loyalty +4), oppose turns it down (loyalty −3).
- **Answers** (`SupportProposal` / `OpposeProposal`, free, once): credibility ±fit (+0.05 × fit agreeing with the programme, −0.08 × fit against
  it; fit = platform weight × direction), relation with the proposer +0.05 / −0.03 (a partner opposing its own government −0.05 more; a blocked
  measure −0.1 more), voter groups ±0.04 × size share by whether they like the change. Unanswered: a measure passes, a request lapses, no effect.
  An election, formation or fall before the end of the quarter lapses the proposal. At most one open proposal; next at the earliest 3 quarters
  after the previous one was opened.
- Snapshot `CountryPolitics.Proposals` (`ProposalInfo`: kind, proposer, policy, from, to, fit, pivotal, answer); events `ProposalPut`,
  `ProposalPassed`, `ProposalFailed`, `ProposalLapsed`; save/replay `support_proposal` / `oppose_proposal`; CLI `support`, `oppose`
  (the open proposal is shown after each quarter); batch strategies answer (keeper supports unless clearly against its programme and never
  blocks with pivotal seats; reformer/activist follow the programme; random coin; extreme opposes all; ignorer never answers).
- Client: a **Proposal** card in the briefing (proposer, change in shown units, fit hint in colour, pivotal / not pivotal, what silence does,
  Support / Oppose answered inline) and at the top of the Politics screen; `TurnPopup.DecisionCount` counts it (Politics badge). It never blocks
  the play button (it expires with the neutral default).
- The ~4 000 proposals per 200 × 80 batch are about one every 4 quarters per game.

## (d) Goals heading

`Texts.GoalsHeading(role)`: "Goals" / "Cíle" in government, "Opposition goals" / "Cíle opozice" in opposition (client and CLI).

## (e) Necessity and fiscal blame

- **Necessity** (`Voters.Necessity`): the dissatisfaction a group feels about a policy set on the *austere* side of its preference (spending
  below it, a tax above it) is scaled by `max(0.4, 1 − 0.08 × (deficit − 3)⁺ − 0.01 × (debt% − 60)⁺)`. The generous side is never softened and
  the factor stays positive, so austerity is never popular. Applied in `VoterModel` and in the policy preview's group reactions; the policy dialog
  notes "voters feel only N % of their usual dislike for this cut". Tests: the same cut / tax rise is less unpopular at a high deficit / debt
  and still unpopular; spending more is not softened; the factor curve.
- **Fiscal blame** (`Politics.FiscalBlame`): every quarter all blame fades (half-life 12 quarters); while the deficit (% of GDP) rises, the
  government's leader gains 1 point of blame per point and each partner 0.5; while it falls under a government, its parties shed blame at the
  same rates (never below 0). Opposition parties neither gain nor shed. Each point costs 0.012 utility in the poll model (at most 0.15, every
  party, via `SupportInput.Penalty`), and the player's party loses 0.01 credibility per point gained. Deterministic, part of the replayed state.
  Shown as a **Fiscal blame** tile with an explanation tooltip; `CountryPolitics.FiscalBlame` per party. Tests: accrues and decays; the party
  that cleans up carries none while the party that caused it keeps its blame; cutting sheds faster than waiting; the engine blames a spending
  leader and the blame survives save/load.
- Goldens: unchanged and still matching (the sandbox's passive deficit stays below 3 % within the snapshot turns, and blame lives in politics).

## (f) Explanations

- The numbers behind the player's stats and actions moved into named constants (`PartyRules`; behaviour unchanged, the golden and replay
  tests match) and `Texts.Explain(topic)` builds every explanation from them and from `PoliticsLimits` / `FiscalBlame`: capital, organisation,
  credibility, cohesion, relations, fiscal blame; campaign, organise, court, criticise, stance, promise, priority, shift, propose (partner),
  announce, proposals, reshuffle, exits (no confidence, dismiss, leave). Test: every topic exists in cs and en with no unfilled placeholder and
  quotes the engine's numbers.
- Client: tooltips on every stat tile, a **How it works** section on the Overview tab, an explanation line on every action card, on the
  Platform, Promises, Relations and stance sections and on the reshuffle card.
- Every stat has a mechanical effect, so none was hidden: organisation raises capital income (8 × org × poll share) and turnout (up to +20 %);
  credibility × cohesion scales platform appeal in the poll; relations set coalition prices, partner choice, proposal acceptance, no-confidence
  support and the partner's capital bonus. **Correction of the brief**: organisation does *not* change campaign strength (the campaign effect is
  fixed per level), and "court" works on parties, not voter groups; the texts say what the engine does.

## (g) Announced proposals

- `AnnouncePolicy(policy, target)` for any party that does not lead a sitting government (the leader keeps the policy dialog,
  `error.not_for_leader`): 2 capital, one a quarter, the same policy again after 4 quarters (`error.announce_limit`, `error.announce_cooldown`).
  Each group that cares about the policy moves by up to ±0.06 momentum (full effect for a change of a tenth of the range toward / away from its
  wish, scaled by how much it cares relative to the group that cares most); against the party's programme credibility falls by 0.05 × platform
  weight. When the size-weighted approval exceeds 0.02 and the government is AI-led, it **adopts** the proposal at the end of the quarter with
  probability 0.2 (its own PCG stream, so announcing never shifts other draws), at most a quarter of the range, never against a leader position of
  weight above 0.3 or across a member's red line; event `AnnouncementAdopted`, enacted as a government measure.
  **Ruling:** first tuned at approval 0.01 / chance 0.35 / leader weight 0.5; the reformer then got 2 712 adoptions per batch. Tightened.
- `Game.PreviewAnnouncement` (the voters' reaction, credibility, whether it may be adopted); save/replay `announce_policy`; CLI
  `announce POLICY VALUE` (prints the reaction first); client **Announce a proposal** card in the Actions tab (policy picker, slider, who would
  like / dislike it, credibility warning, Announce · 2); reformer and activist announce their furthest platform positions outside the lead.

## Batches (200 games × 80 turns, `base:cz_2015`, default party)

"Before" = commit 9d591ae (decision 0013); "after" = this work, final.

| strategy | falls before → after | new majority after every fall | governing quarters | player leads | player poll (run) | leader capital | stuck |
|---|---|---|---|---|---|---|---|
| ignorer | 337 → 404 | yes | 88.1 → 87.7 % | 16.3 → 15.6 % | 15.1 → 15.6 % | 26.8 → 26.6 | 0 → 0 |
| keeper | 82 → 84 | yes | 89.5 → 89.9 % | 16.3 → 16.1 % | 14.8 → 15.5 % | 23.4 → 23.6 | 0 → 0 |
| random | 341 → 395 | yes | 88.0 → 88.2 % | 15.9 → 15.9 % | 15.0 → 15.8 % | 24.8 → 24.6 | 0 → 0 |
| extreme | 335 → 451 | yes | 88.2 → 88.1 % | 16.1 → 15.4 % | 15.3 → 16.0 % | 25.2 → 25.4 | 0 → 0 |
| activist | 679 → 655 | yes | 86.0 → 87.3 % | 32.7 → 39.5 % | 19.7 → 20.1 % | 6.2 → 6.2 | 0 → 0 |
| reformer | 80 → 137 | yes | 89.6 → 89.7 % | 16.3 → 18.7 % | 14.8 → 17.2 % | 23.4 → 24.4 | 0 → 0 |

Proposals per batch: about 3 700–4 500 (ignorer 4 096, none answered); failed or turned down: keeper 8, reformer 888, random 998, activist
1 136, extreme 1 986. Announcements: reformer 13 065 (1 516 adopted), activist 8.

Reading: the keeper governs as before (84 falls, never refused for capital, 3.7 changes per led year). Falls of the passive and random
strategies rose by 15–20 % (ignorer 404, random 395 – inside the band seen in 0011/0013, 337–412); a 100-game probe at the commit before fiscal
blame and announcements already showed ignorer 408 / 200, so the rise comes from (a)/(c): measures that are put to parliament reach the
statute book a quarter later and AI governors now also chase value goals. Extreme (opposes every proposal and blocks pivotal measures) and
reformer (opposes measures against its programme, blocks as a pivotal partner, announces every quarter) pay for blocking: blocked measures
leave partners' commitments undelivered, so their coalitions break more often (451, 137) – the intended "opposing has consequences". Every
fall was followed by a new majority in every run; no stuck game; the governing share stays at 87–90 %.

Economy batches (300 × 80, sandbox): passive, random, extreme `Stable`, 0 NaN / collapse / explosion games, debt spirals 0 / 157 / 300 as before.

## Tests and gates

Tests 622 at the start of this work (641 counted by the runner at the time) → 691; new: `TextsTests`, `PromiseDeadlineTests`, `ProgramTests`, `ValueGoalTests`,
`ProposalTests`, `NecessityTests`, `FiscalBlameTests`, `AnnouncePolicyTests`. Changed: `PoliticsContentTests` (platform bound 14 → 25),
`MacroModelTests` (15 owned values). Goldens not regenerated (still match). `validate data/base` OK. Headless smoke `SMOKE OK`, extended: as
the opposition it waits for a proposal and presses Support in the briefing, promises a goal, announces a proposal and opens the overview,
programme, promises and actions tabs.

## Known gaps

- Verified visually only through smoke screenshots at 1920 × 1080 (briefing with a proposal, overview); no human playtest. The overview's
  How-it-works section is long and needs scrolling.
- Requests come only from AI partners of a player-led government; the opposition's votes never decide a measure (a sitting government has a
  majority by definition), so for it an answer is only a signal.
- Fiscal blame uses the deficit change only (not the debt level); necessity uses both. The blame penalty is the same for every voter group.
- Announcements adopted by an AI government count as its measures (opposition can take a paid stance on them like on any measure).
- Some work of this decision was swept into the coordinator's commits 488bdb0 and 67dae27 (shared working tree); the later commits are mine.
