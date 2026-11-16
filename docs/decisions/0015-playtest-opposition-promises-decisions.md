# 0015 – Playtest: promises for the next term, opposition play, one way to propose, decisions in the briefing, role-aware texts, deficit, fiscal blame

Status: accepted (2026-11-16). A Czech-speaking user playtested and reported seven problems (1–6 at once, 7 later with screenshots, refined
twice). The game still has no end (`Session.Finished` is always false), as decided before this work.

## 1. Promises are for the next term

User: a promise means "what I will do if you elect me and I govern in the following term"; it was reported as broken right after the election.

- A promise made in the window before election E1 (8 quarters, unchanged) is **carried over E1** (event `PromiseCarried`, news line) and judged
  at the election **E2 that ends the next term** (also an early or caretaker election). At E1 nothing is judged.
- It counts only if the party sat in the government for **at least half of the quarters since E1** (the quarters are counted only once the
  promise is carried). Counted: kept +0.1 credibility and goodwill, broken −0.2 (as in 0014). Otherwise no penalty and no credit.
- **Max 2 per election window**: promises carried over from the previous window (judged at the coming election) do **not** count against the
  two. The same demand cannot be promised again while a promise on it is still pending (`error.already_promised`).
- `PromiseInfo` gains `Carried` and `JudgedIn` (quarters until the election that judges it: before E1 = quarters to E1 + interval).
  Texts: "Judged at the election after the coming term (in N quarters)" / "Judged at the next election (in N quarters)", the counting rule;
  Promises tab: when a promise made now is judged and how many of the two are used; explanation, CLI help, errors updated (cs, en).
- Saves are action logs, so no format change; a test saves across E1 and reloads.
- Tests (`PromiseDeadlineTests`, rewritten): not judged at E1; broken and kept at E2 when governing; no effect in opposition; a partner that
  leaves early in the next term is not held to it; previous-cycle promises do not block new ones; save/load across E1.

## 2. Opposition play

User: in opposition, following the programme and campaigning, "voters had a negative opinion everywhere" (middle class +0.05 at best).

**Finding.** The sidebar showed each group's **mood about the country** (satisfaction, what the government delivers), not its support for the
player's party. The engine already rewarded consistent opposition work: a level-3 campaign moves its group by about 5 points at once (Civic
Right among the middle class: +5.5 pts; level 1: +1.7), halving every 4 quarters; the old activist batch as Civic Right went from 13 % to 31 %
of the poll in 12 quarters. So the fix is mainly readability; **no balance constants were changed for item 2**.

- (a) `SupportModel.GroupShares` gives the share of each group that would vote for a party (no noise). The snapshot carries
  `PartyStatus.GroupSupport` and `GroupSupportPrev` (a quarter earlier). The voter sidebar has a **Mood / Support** toggle (default Support
  outside the lead, Mood as leader); in Support the rows show "14.2 %" with a ▲/▼ change, the overall figure shows the party's poll. The group
  dialog shows the support bar with its change and a hint; the CLI `voters` list prints "vote for you: x % (±y pts)".
- (b) Numbers everywhere: the campaign card previews "adds about +2.1 pts at once" (`Game.PreviewCampaign`), the propose card previews the points
  per group (`ProposalPreview.SupportPoints`), every accepted action's message appends "Support: +2.1 pts among Middle class" (client and CLI),
  and each quarter's news has one line `SupportMoved` with the groups that moved at least half a point. Opposition support already gains from
  the government's weakness: a negative mood lowers governing parties and lifts opposition ones (attribution term), fiscal blame is a penalty
  for blamed parties only.
- (c) New batch strategy **challenger** and report metrics (poll every 4 quarters, elections won by most votes, governments led by the player).

### Opposition poll trajectories (200 games × 80 turns, mean player poll %)

| party / strategy | q0 | q4 | q8 | q12 | q16 | q24 | q40 | q80 | elections won | quarters leading |
|---|---|---|---|---|---|---|---|---|---|---|
| Civic Right, ignorer (before) | 13.0 | 14.7 | 16.1 | 16.4 | 13.6 | 11.2 | 11.8 | 11.3 | 0.1 % | 0.3 % |
| Civic Right, activist (before) | 13.0 | 19.6 | 27.0 | 31.4 | 17.8 | 13.2 | 17.9 | 15.8 | 27.5 % | 24.1 % |
| Civic Right, activist (after) | 13.0 | 19.6 | 26.9 | 31.4 | 17.8 | 13.2 | 18.0 | 15.3 | 27.5 % | 25.0 % |
| Civic Right, challenger (new) | 13.0 | 19.3 | 23.6 | 25.9 | 16.9 | 12.7 | 21.1 | 15.7 | 37.4 % | 22.2 % |
| Workers' Left, ignorer (before) | 12.0 | 14.7 | 16.3 | 16.7 | 17.7 | 17.5 | 16.0 | 17.6 | 1.9 % | 0.0 % |
| Workers' Left, activist (before) | 12.0 | 17.8 | 23.9 | 27.5 | 31.3 | 28.4 | 23.5 | 25.8 | 72.4 % | 4.9 % |
| Workers' Left, activist (after) | 12.0 | 17.8 | 23.9 | 27.5 | 31.4 | 28.5 | 23.5 | 25.6 | 73.7 % | 6.5 % |
| Workers' Left, challenger (new) | 12.0 | 19.9 | 24.3 | 26.1 | 29.8 | 27.1 | 22.2 | 25.3 | 73.9 % | 3.8 % |

Reading: a consistent challenger gains 10–14 points in 8–12 quarters and wins the most votes in 37 % (Civic Right) / 74 % (Workers' Left) of
elections. Civic Right then leads governments (22 % of quarters) and loses support while governing (q16–q28). Workers' Left wins votes but rarely
finds partners (its formation price and distance to the others): it leads 4–7 % of quarters; courting before the election (the challenger does)
helps little. That is a coalition-arithmetic difficulty, not a dead opposition; recorded as a known gap.

`--party all` with the challenger: leads Citizens' Movement 48 %, Social Alliance 34 %, Civic Right 22 %, Workers' Left 4 %, People's Union 19 %,
Homeland First / Green Wave / Open Net 0 %; no stuck game anywhere.

## 3. One way to propose

- `ProposePolicy` is the one action for every party that does not lead a sitting government: a **partner**'s proposal goes to the leader (3
  capital, answered at the end of the quarter, as before), **anyone else**'s goes public (2 capital, the 0014 announcement mechanics, an AI-led
  government may adopt it). Either way the voters react at once (warmth per group, credibility against the programme). One proposal a quarter
  for everybody; the same policy again after 4 quarters for public proposals only (a partner may press the leader again).
- `AnnouncePolicy` stays as an alias (old saves parse; `announce_policy` replays as a proposal). CLI: `propose` and `announce` both work, help
  shows `propose`. Client: one **Propose a policy** card (picker, slider, who decides – the leader's likely answer for a partner –, support
  points, credibility, Propose · 3 / · 2); the separate Announce card is gone. `Game.PreviewProposal` / `ProposalPreview`.
- The leader gets `error.not_for_leader` (was `error.not_partner`).

## 4. Every decision in the start-of-quarter briefing

Audit of what the game waits for (`PoliticsEngine` actions): offer (`AcceptOffer`/`RejectOffer`), own formation (`ProposeCoalition`, also after a
fall or lost majority: the old leader forms first), proposals (`SupportProposal`/`OpposeProposal`), ultimatums to the leader (deliver = policy
change, `Court`, `ReshuffleCoalition`, `DismissPartner`), the promise window (`MakePromise`). Not waiting decisions: mid-term demands (a deadline,
shown in the coalition card and the news), stances on measures (optional), the reshuffle's answer (news at the end of the quarter).

- `Decisions.Open(country, packs)` lists exactly these; `TurnPopup.DecisionCount`, the Politics badge and the briefing use it. Bugs found:
  ultimatums were counted for every role (an opposition player was told to decide about the government's ultimatums); the formation offered only
  a link to the Politics tab; after loading a save no briefing opened; **x4** skipped proposals and formations.
- The briefing offers every decision inline: offer (Accept/Decline), formation (the suggested coalition with agreements and seats, **Form this
  coalition**, **Adjust** – invitations, demands, majority, Send –, quarters left; the Politics screen uses the same card and draft), proposals,
  ultimatums (Deliver: opens the policy / indicator, Court · 3, Reshuffle, Dismiss · 4), promises (a Promise button per goal). It opens at the
  start of every quarter with a decision, after loading, and x4 stops at the first quarter with an urgent decision (all but a promise window that
  was already open). Only an offer blocks the quarter. When the role changes the news says so.
- Smoke: forms the government as Citizens' Movement from the briefing (Adjust, Form this coalition), courts a partner under ultimatum from the
  briefing, promises from the briefing, checks x4.

## 5. Role-aware texts

- Convention: a key is written for the leader or neutrally; `<key>_opposition` / `<key>_partner` replace it for that role
  (`Texts.RoleKey`, `ForRole`, `Event(e, role)`). `RoleTexts.LeaderOnly` lists keys only a leader sees; `RoleTexts.Forbidden(lang)` lists phrases
  that address the government ("your partners", "your coalition", "gives you an ultimatum", "you lead the government", "you have {2} quarters",
  cs equivalents). `RoleTextsTests` fails if any localization key an opposition player can see contains one, if an event shown to the opposition
  does, or if a screen text / explanation for the opposition does.
- New variants: ultimatum events (the government, or "your leader" for a partner), mid-term demands, coalition fragility → government fragility,
  "YOUR POLICIES" → "GOVERNMENT POLICIES", CLI tips 1/2/5, end-of-run verdicts, role pill "junior partner in the government", role-change news,
  welcome on the News screen by role. `ui.explain.capital` reworded; "no majority" says the government has none; a non-leader is told it does not
  lead before being told about the majority (`PolicyBlock` order). The sidebar shows "ultimatums running" only to the leader.

## 6. Deficit shown twice

- **Map bubbles**: `base:debt` and `base:debt_pct` ("Public debt", "Public debt (% of GDP)") were two bubbles for one quantity. The debt bubble now
  shows the ratio (as the top bar) and the ratio twin is not drawn.
- **Budget page**: the "Budget balance" tile was the deficit with the opposite sign next to the top bar's deficit; it now shows the deficit
  (positive = deficit) – and since item 7 a surplus as "Surplus". The top bar's "+N" overflow and the briefing pills each showed it once.

## 7. Fiscal blame and the surplus

User (screenshots): blame 1.5 for a government with a 1.6 % **surplus** and a debt ratio falling from 40 % to 30 %: 0014 blamed any rise of
`deficit_pct`, so a shrinking surplus (−3.1 → −1.6) counted. Refined by the user twice: count from the start of the government, only once the
deficit is real, and heavier when it was already high. Final rule (`FiscalBlame.Step` with per-party baselines):

- Each government party has a **baseline**: the deficit when it entered this government, a surplus counting as 0 (set at formation, for
  newcomers at a reshuffle or patch, for the starting government at the start; engine state, rebuilt by replay).
- It gains blame only for the rise of the **excess** `max(0, deficit − baseline)`, times a **severity** `min(4, 1 + max(deficit, 0) / 3)`;
  leader 1×, partners 0.5×. Nothing in surplus, at or below the inherited level, or while the deficit falls.
- It sheds blame by the fall of the deficit, and at least 0.25 a quarter (leader) while the deficit is at or below its baseline. Opposition gains
  and sheds nothing; the 12-quarter fade stays.
- The user's examples, tested exactly: −3 → −1: 0; −3 → +1: 1 × 1.33 = 1.33; +10 → +12: 2 × 4 = 8. Also: inherited +5 falling to +3 gives none
  and sheds; +5 → +7 gives 2 × 3.33; an inherited surplus then +1 gives 1.33.
- Presentation: a negative deficit is labelled **Surplus** with the positive figure (top bar, Budget page, briefing pill); the top-bar arrow
  follows the shown figure and is coloured by whether the budget got better (a smaller surplus is red); the value's name is "Deficit / surplus
  (% of GDP)" (chart, map). The blame tile's subtitle (was an unexplained "−1.8 %") now reads "costs 1.8 % of your voters' warmth; counted from
  the start of this government, only above the deficit it inherited"; the explanation was rewritten with the engine's constants.

## Batches (200 games × 80 turns, `base:cz_2015`, default party Social Alliance)

"Before" = commit a9bde29 (start of this work); "after" = this work, final (including item 7).

| strategy | falls before → after | new majority after every fall | governing quarters | player leads | player poll (run) | stuck |
|---|---|---|---|---|---|---|
| ignorer | 404 → 404 | yes | 87.7 → 87.1 % | 15.6 → 15.6 % | 15.6 → 15.1 % | 0 → 0 |
| keeper | 84 → 85 | yes | 89.9 → 89.3 % | 16.1 → 16.4 % | 15.5 → 14.9 % | 0 → 0 |
| random | 395 → 391 | yes | 88.2 → 87.8 % | 15.9 → 15.8 % | 15.8 → 15.4 % | 0 → 0 |
| extreme | 451 → 451 | yes | 88.1 → 87.7 % | 15.4 → 15.3 % | 16.0 → 15.9 % | 0 → 0 |
| activist | 655 → 662 | yes | 87.3 → 87.2 % | 39.5 → 40.8 % | 20.1 → 19.7 % | 0 → 0 |
| reformer | 137 → 147 | yes | 89.7 → 89.5 % | 18.7 → 19.6 % | 17.2 → 16.9 % | 0 → 0 |
| challenger (new) | – → 176 | yes | 90.0 % | 33.8 % | 17.2 % | 0 |

Before item 7 (items 1–6 only) the figures were identical to "before" for ignorer, keeper, random, extreme and activist (the changes do not touch
their play); reformer 137 → 148 (as a partner its announcements now go to the leader, adoptions 1 516 → 917). Item 7 moved falls by at most ±1 %
(blame now hits fewer governments, but harder when a high deficit grows). Every fall was followed by a new majority; no stuck game; the governing
share stays at 87–90 %, within the ranges of 0013/0014.

Economy batches (300 × 80, sandbox): passive, random, extreme `Stable`, 0 NaN games, debt spirals 0 / 157 / 300 – unchanged.

## Tests and gates

691 → 729 tests. New: `PromiseDeadlineTests` (rewritten), `OppositionPlayTests`, `DecisionsTests`, `RoleTextsTests`, new `FiscalBlameTests` cases,
challenger in the batch theory. Changed: `ProposePolicyTests` (leader error, opposition may propose), `PrioritiesAndPromisesTests` (judged at the
second election), `TextsTests` (promise status), CLI tests (proposal texts), `FormationTests` (error order). Goldens unchanged (sandbox).
`validate data/base` OK. Headless smoke `SMOKE OK`; windowed screenshots at 1920 × 1080 checked for the formation and ultimatum briefings and the
opposition Actions tab.

## Known gaps

- Not checked visually: the surplus label in the top bar and on the Budget page (no screenshot run reached a surplus), the Promise card in the
  briefing (smoke presses it, screenshot not viewed), Czech layouts of the new cards.
- Workers' Left wins the most votes in three of four elections as a challenger but rarely forms a government (few willing partners).
- The leader's likely answer in the propose preview uses today's relation; the real answer comes at the end of the quarter.
- Mid-term demands are news and coalition-card items, not briefing decisions.
- Saves from before this work are rejected by the pack hash (localization and a value name changed), as in 0013.
