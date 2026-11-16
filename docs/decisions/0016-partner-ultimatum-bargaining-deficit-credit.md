# 0016 – Partner ultimatum, coalition bargaining, fiscal credit

Status: accepted (2026-11-16). Three requests from the Czech-speaking playtester (translated):
(1) "As a coalition partner I have no way to explicitly threaten the government (you don't deliver = we leave)."
(2) "Coalition building does not respect the differences between parties and does not let me negotiate more."
(3) "Add positive points for reducing a high deficit (not so aggressive, a smaller bonus would be great)."
All three were built test-first. The term had just been doubled (`election_interval_turns` 32, `PromiseWindowTurns` 16); its effect on the
batches is measured below.

## 1. A junior partner's ultimatum

- `IssueUltimatum(Kind, SubjectId, Deadline)`: only a **junior partner of a sitting government** (`error.not_partner` for the leader and the
  opposition; `error.wrong_phase` outside Governing). Kind `demand`: one of the party's own demands, agreed or not (a policy demand asks its
  target; a value goal is asked as a move of its strongest lever – `Governor.Levers` – by a fifth of the range); kind `policy`: a programme
  position, asked as a move toward it of at most a quarter of the range. Deadline 2–6 quarters (`error.bad_deadline`), cost 4 capital, one at a
  time (`error.ultimatum_open`), the next 8 quarters after one is settled (`error.ultimatum_cooldown`). Nothing to ask: `error.ultimatum_met`;
  a goal with no lever: `error.not_deliverable`.
- **The AI leader answers at the end of the quarter** (`UltimatumAnswer`, deterministic, no draws): a red line of a government member newly
  crossed → refuse (`red_line`); else a score `0.3 × relation + 0.3 × fit with the leader's platform + need + 0.15 × the partner's share of
  the government's seats − 0.6 × the move (share of range) − 0.2 × the leader's grudge + 0.15 if the partner holds the portfolio of the policy
  (item 2) + 0.08 × quarters already waited`, where need is 0.3 when the government loses its majority without the partner and nobody could
  replace it (`CoalitionBuilder.Complete`, as the 0013 patch-up), 0.15 when it could be replaced, 0.05 when it is not pivotal. ≥ 0.55 deliver,
  < 0.3 refuse (`score`), otherwise **stall** and re-evaluate every quarter (the waiting term grows). A refusal is final.
- **Delivered**: the change is enacted as a government measure (the same `_approved` path as accepted proposals); +5 % credibility, the
  party's voter groups +0.03 momentum (by affinity), the leader's relation −0.05 (it does not like being pushed). **Refused**: relation −0.05.
- **Deadline passed undelivered**: the party walks out exactly like an AI partner walk-out (`Depart`: the leader resents it, it resents the
  leader, commitments drop), credibility −5 % (instead of −15 % for an unannounced `LeaveCoalition`: it kept its word), relation −0.3, a little
  voter warmth; then the AI leader may patch the majority (0013, coin flip) or the government falls into formation/early elections
  (`FallIfNoMajority`). **Withdraw** (`WithdrawUltimatum`): −3 % credibility, cooldown starts. An election, formation or fall lapses it
  (`UltimatumLapsed`); leaving voluntarily ends it quietly.
- Snapshot `CountryPolitics.PlayerUltimatum` (`PlayerUltimatumInfo`: kind, subject, policy, from, target, turns left, deadline, answer) and
  `UltimatumCooldown`; events `UltimatumAnswered`, `UltimatumDelivered`, `UltimatumCarriedOut`, `UltimatumLapsed`; `Game.PreviewUltimatum` →
  `UltimatumPreview` (likely first answer, after how many quarters a stalling leader would give in, pivotal / replaceable, cost, score).
- `Decisions.Open` lists the own ultimatum (`DecisionKind.OwnUltimatum`, urgent in its last quarter, so x4 stops before the walk-out).
- Client: an **Ultimatum to the leader** card in the Party → Actions tab, partners only (Demand / Programme switch, picker of unmet demands or
  programme positions away from the setting, deadline 2–6, the four preview lines – what is asked, the leader's likely answer, leverage, stakes –
  and *Give the ultimatum · 4*; while it runs its status and *Withdraw*); in the briefing a card with the countdown, the leader's answer and
  Withdraw. CLI `ultimatum [check] demand N|policy POLICY [QUARTERS]`, `withdraw`; the status is printed after each quarter. Save/replay
  `issue_ultimatum` / `withdraw_ultimatum`. Texts cs/en (`ui.myult.*`, `ui.event.myult_*`, `ui.explain.ultimatum`); the carried-out and lapsed
  events are in the role-text test (they are read in opposition afterwards).
- Batch strategy **threatener**: the challenger outside the lead, the reformer as leader; as a partner it issues a 4-quarter ultimatum on what
  matters most to it (heaviest unmet demand, else its weightiest programme position) unless the leader would refuse at once, and lets a stalled
  or refused one run out. **Ruling:** a first version picked the candidate the leader liked most and won 962 of 978 ultimatums; picking by
  importance gives a realistic mix.

## 2. Coalition talks that respect party differences

Before: a partner's price was `price × (1 − relation/2) × (1 + grudge)` whatever the formateur; the player could only tick demands; the answer
was yes or the first refusal.

- **Price** (`Negotiation`, `CoalitionRules.RequiredWeight(…, factor)`): × **distance** factor `clamp(1 + 1.5 × (d − 0.12), 0.75, 2.2)` with
  talk distance `d = 0.7 × platform distance + 0.3 × compass distance / 2√2`; × **strength** `clamp(0.85 + 0.25 × partner seats / formateur seats,
  0.85, 1.25)`; × **alternatives** 1.05 when the partner could be in another majority without the formateur (≤ 4 parties within talk distance
  0.3), else 0.95. At talk distance ≥ 0.42 a party refuses whatever is offered (`distance`; in the base pack only Homeland First – Green Wave).
  Examples at the start: Civic Right toward Citizens' Movement 3.0 (pack 4), Social Alliance toward Citizens' Movement ≈ 1.3 × its pack price.
- **What pays the price**: an agreed demand its weight (a met value goal nothing, as in 0014); a **counter value** for a *negotiable* demand –
  the formateur's platform pulls the other way – anywhere between the formateur's position and the demand's target, paying that share of the
  weight (half-way pays half; `AgreedDemand.Target`; the commitment then has that target); a **cabinet portfolio** `value × (0.5 + 0.5 ×
  the partner's largest platform weight among its policies)`; a **concession** 1 per unit (max 2 per partner, 3 capital each for the player,
  +0.05 relation per unit when the government forms).
- **Portfolios** are data (`politics.portfolios` in the country, see docs/modding/format.md): nine Czech ministries (finance 3, labour 2.5,
  health / education / interior 2, industry / environment / regional 1.5, defence 1), each with its policies. Partners together hold at most
  two thirds, each at most three; the leader keeps the rest. A holder is heard more on its policies (+0.15 on the leader's score for its
  proposals and ultimatums), an AI holder is a little more loyal (+2 loyalty target each, at most +4); a departing party's portfolios return to
  the leader. Reshuffles and AI patch-ups pay with agreements only.
- **Assessment** (`Game.AssessCoalition(draft)` → `NegotiationView`): per invited party its price and the factors behind it, what the offer
  pays, satisfaction, the answer – **accept**, **counter** (the terms it would take: full value instead of half-way, more of its demands in the
  AI's order while slots are free, then the portfolios it values most) or **refuse** with the reason (`price` with the shortfall, `red_line`
  naming the agreement and whose line, `grudge` naming the party, `distance`, `contradiction`) – its demands as acceptable / negotiable /
  red line / met with the half-way value, its portfolios, and fixes (add a demand, half-way, drop a demand, give a portfolio, concede, court,
  wait). Hard errors before anyone answers: not formateur, majority, slots, portfolios, an unknown term.
- **Rounds**: `NegotiateCoalition` sends one round; all accept → the government forms (concessions paid); else the replies are kept in the
  formation snapshot (`FormationInfo.Replies`, `RoundsLeft`), parties that refused lose 0.02 relation (patience), and the formateur can take a
  party's counter terms. Three rounds a quarter (`error.no_rounds`) within `max_formation_turns`. `ProposeCoalition` (Form this coalition)
  stays the one-shot path and now carries portfolios and concessions too (`ProposeCoalition.From(suggestion)`).
- **The invited player can counter**: `CounterOffer(extra demands, portfolios)` – the AI formateur answers at once: refused for a red line,
  slots, a contradiction, portfolios, a demand against a formateur position weighted ≥ 0.5 (`platform`), or when the player would get more than
  its price × 1.5 (+2 when the formateur has no coalition without the player); accepted → the government forms on those terms. One per offer;
  the offer stands after a refusal. `Game.PreviewCounter`, event `CounterOfferAnswered`.
- **AI formateurs use the same talks**: `CoalitionBuilder.Build/Complete/AllocateFor` take the `Talks` (factor, veto, portfolios); the AI
  pays with demands first and adds the portfolios a partner values most only when demands or slots run out.
- Client: *Adjust* in the formation card (briefing and Politics screen) is now the negotiation view (per party: invite, satisfaction bar,
  verdict pill, reason, price breakdown, last answer, demand rows with status pills and a half-way slider + button, top portfolios, concessions
  0–2, fixes, counter terms with *Take its terms*; *Send the offer* with the rounds left); the offer card has a *Counter-offer* section (a
  demand and/or a portfolio, the formateur's likely answer, *Join on these terms*). CLI `offer`, `offer send`, `offer counter`,
  `counter PARTY N VALUE|half|off`, `portfolio PARTY PORTFOLIO`, `concede PARTY 0-2`, and for an invited party `counter N [PORTFOLIO]`.
  Save/replay `negotiate_coalition`, `counter_offer`, portfolios / concessions / counter values in `propose_coalition`.
- **Ruling (balance)**: the first factors (slope 2.5, centre 0.10, strength up to 1.3, alternatives 1.1) raised prices about 40 % and the
  keeper's falls 116 → 146; recentred (slope 2.5, centre 0.15) they fell to 67 and the ignorer's 463 → 289 – the distance-based prices make the
  AI pick more coherent coalitions. Isolated: with factor 1 and portfolios the keeper stays at 116; with the factors and no portfolios it drops
  to 72, so the factors drive it. Slope 1.5 / centre 0.12 was kept: keeper 105, ignorer 356 (the governing share and the new-majority rate
  unchanged, no stuck game).

## 3. Fiscal credit for cutting a high deficit

- `FiscalBlame.Credit(prev, now)`: only a real cut of a deficit counts – `cut = prev − max(now, 0)` when `prev > 0`, at least 0.05 points –
  and earns `0.35 × cut × Severity(prev)` credit (the blame of an equal rise at that level is `cut × Severity`, so credit is 0.35×). Leader
  full, partners half, opposition nothing; a surplus that stays a surplus and a shrinking surplus earn nothing. Tested: 8 → 6 earns
  `0.35 × 2 × 3.67 = 2.57` (partner 1.28); −3 → −1 and −1 → −3 nothing; 2 → −1 counts the two points down to balance.
- Credit is a stock that fades like blame (half-life 12) and works as a poll bonus of 0.012 per point, at most 0.05 (blame: at most 0.15).
  The player's party gets `0.4 × credit gained` political capital in that quarter, **at most 1** (8 → 6 gives the cap, a typical 0.5-point
  cut at 5 % about 0.2), and +0.01 credibility per point.
- News line `FiscalCreditEarned`: "+0.6 capital, +0.4 pts for cutting the deficit (4.8 → 3.9 % of GDP)" (the points are the player's poll
  with and without the new credit, no noise). Party overview: a **Fiscal credit** tile next to fiscal blame (+warmth %, capital last
  quarter) and its explanation in *How it works*; CLI `party` shows blame and credit.

## Batches (200 games, `base:cz_2015`, default party Social Alliance)

### The 32-quarter term (commit 4fdb8e9 with 16 quarters → dcd151a with 32, before this work)

| strategy | falls 80 t | governments formed 80 t | governing 80 t | falls 160 t | governing 160 t | stuck |
|---|---|---|---|---|---|---|
| ignorer | 404 → 463 | 1396 → 1059 | 87.1 → 90.4 % | 450 → 567 | 88.4 → 93.0 % | 0 → 0 |
| keeper | 85 → 116 | 1083 → 715 | 89.3 → 92.9 % | 114 → 157 | 89.7 → 94.5 % | 0 → 0 |
| random | 391 → 416 | 1385 → 1013 | 87.8 → 91.4 % | 444 → 536 | 88.9 → 93.6 % | 0 → 0 |
| extreme | 451 → 476 | 1446 → 1069 | 87.7 → 91.2 % | 485 → 568 | 89.0 → 93.4 % | 0 → 0 |
| activist | 662 → 698 | 1662 → 1290 | 87.2 → 89.2 % | 1193 → 1213 | 88.1 → 91.5 % | 0 → 0 |
| reformer | 147 → 220 | 1143 → 816 | 89.5 → 92.5 % | 173 → 242 | 90.2 → 94.4 % | 0 → 0 |
| challenger | 176 → 242 | 1172 → 840 | 90.0 → 92.7 % | 225 → 326 | 90.5 → 94.7 % | 0 → 0 |

Reading: half as many elections means a quarter to a third fewer governments formed and 2–5 points more quarters with a sitting
government; each government lives twice as long, so more of them lose a partner before the next election (falls +6 % to +50 % per run),
while falls per governed quarter rose only a little (keeper 0.60 % → 0.78 %). Every fall was followed by a new majority, no stuck game:
healthy, nothing retuned for the term.

### This work (dcd151a → after item 3 → after items 1–3)

| strategy | falls 80 t | new majority | governing 80 t | player leads 80 t | poll 80 t | falls 160 t | governing 160 t | stuck |
|---|---|---|---|---|---|---|---|---|
| ignorer | 463 → 463 → 356 | all | 90.4 → 90.4 → 91.2 % | 15.4 → 15.5 % | 14.7 → 14.7 % | 567 → 461 | 93.0 → 93.4 % | 0 |
| keeper | 116 → 116 → 105 | all | 92.9 → 92.9 → 93.1 % | 16.1 → 15.9 % | 14.6 → 14.8 % | 157 → 156 | 94.5 → 94.5 % | 0 |
| random | 416 → 416 → 367 | all | 91.4 → 91.4 → 91.6 % | 15.7 → 15.9 % | 15.6 → 15.4 % | 536 → 479 | 93.6 → 93.6 % | 0 |
| extreme | 476 → 476 → 411 | all | 91.2 → 91.2 → 91.8 % | 15.5 → 15.2 % | 15.7 → 15.9 % | 568 → 457 | 93.4 → 93.9 % | 0 |
| activist | 698 → 698 → 695 | all | 89.2 → 89.2 → 89.3 % | 28.3 → 31.6 % | 20.0 → 19.8 % | 1213 → 1184 | 91.5 → 91.5 % | 0 |
| reformer | 220 → 220 → 190 | all | 92.5 → 92.5 → 92.7 % | 17.1 → 17.3 % | 18.0 → 17.6 % | 242 → 225 | 94.4 → 94.4 % | 0 |
| challenger | 242 → 240 → 213 | all | 92.7 → 92.8 → 93.0 % | 28.1 → 32.5 % | 19.5 → 18.4 % | 326 → 292 | 94.7 → 94.8 % | 0 |
| threatener (new) | – → 225 | all | 93.0 % | 33.3 % | 18.6 % | 329 | 94.6 % | 0 |

- Item 3 alone moved nothing outside noise (it rewards only real cuts of a deficit; AI governments rarely cut a high deficit). Credit earned
  per 200 × 80 batch: ignorer 18 quarters / 0.7 capital in all, reformer 54 / 4.2, threatener 160 / 57 (as a leader it cuts deficits more
  often): a small reward, as asked.
- Item 2 (distance-aware prices) lowers falls by 10–23 % for the passive strategies, and the long run by up to 20 %: the AI forms more
  coherent coalitions. The governing share and the new-majority rate are unchanged, no stuck game.
- Item 1: as People's Union (a partner) the threatener issued 833 ultimatums in 200 games, 529 delivered, 50 carried out (the rest lapsed at
  elections or falls); as Citizens' Movement 847 / 561 / 30. `--party all` with the threatener: no stuck game for any party (falls 38–280).

Economy batches (300 × 80, sandbox): passive, random, extreme `Stable`, 0 NaN games, debt spirals 0 / 157 / 300 – unchanged.

## Tests and gates

729 → 770 tests (`FiscalCreditTests`, `PartnerUltimatumTests`, `NegotiationTests`, portfolio data tests, new `TextsTests`, `DecisionsTests`,
role-text events, threatener in the batch theory). Changed: `PartyActionTests.CourtedPartyIsCheaperInCoalitionTalks` (asserts the relation
term of the price instead of a fixed threshold) and `FallTests.PartnerLeavingWithMajorityIntactDoesNotCauseAFall` (the far partner now
needs two demands). Goldens unchanged (sandbox). `validate data/base` OK. Headless smoke `SMOKE OK`, extended: one negotiation round from the
briefing with a portfolio and taking a counter, a partner's ultimatum from the Actions card and Withdraw from the briefing, a counter-offer to
an AI offer. **Smoke fix**: the check for the Mood / Support toggle failed already at dcd151a (with the 16-quarter promise window the opposition
no longer plays quarters before the check, and the compact headless window has the narrow rail without the toggle); the smoke now opens the
voter drawer first.

## Known gaps

- Checked visually only through windowed smoke screenshots at 1920 × 1080 (`ultimatum-card`, `negotiation`, `briefing-counter`); the
  negotiation view is long (one block per seated party) and the lower part of the ultimatum card (preview lines, button) is below the fold of the
  Actions tab – both scroll. Czech layouts of the new cards not viewed.
- The AI never gives the player's party an ultimatum-like threat beyond the existing loyalty ultimatums, and AI partners do not use
  `IssueUltimatum` themselves.
- Portfolios have small mechanical effects (price, loyalty, the holder's say); they do not change who sets a policy, and the AI governor does
  not respect a partner's ministry.
- The counter-offer as an invited party is limited to one extra demand and/or one portfolio in the client (the core accepts lists).
- Falls are 10–23 % lower than before item 2 (deliberately milder than the first factors); if the playtester finds governments too stable,
  `Negotiation.DistanceSlope` / `DistanceCentre` are the knobs.
