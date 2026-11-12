# M2b Government Formation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (native execution was chosen by the user; no approval stops between tasks). Steps use checkbox (`- [ ]`) syntax.

**Goal:** After every election a government is formed by rules: the largest party is formateur, the AI (or the player) proposes a majority coalition with agreed demands, parties accept or refuse with reasons, failed attempts lead to caretaker and early elections.

**Architecture:** Pure rule functions (`CoalitionRules`, `CoalitionBuilder`, `PartyMath`) in `core/Politics`, no state. `Game` owns the phase machine (`Governing`, `Formation`, `Caretaker`) and the new player actions. Everything iterates in ordinal id order; politics keeps its own PCG stream.

**Tech Stack:** C# net10.0, xUnit, YamlDotNet (save format).

**Spec:** `docs/specs/2026-11-12-m2-domestic-politics-design.md` sections 5, 9, 10 (formation, turn order, API).

## Global Constraints

- Sandbox scenarios (no `politics:` block) and the M1/M2a golden tests stay bit-identical.
- Seat majority rule: coalition seats `> majority_fraction * total_seats` (country parameter, 0.5 for Czechia).
- Refusal reasons are fixed keys: `slots`, `majority`, `red_line`, `contradiction`, `price`, `unknown`. Action errors are `error.refused.<reason>`, `error.no_majority`, `error.not_leader`, `error.not_formateur`, `error.no_offer`, `error.wrong_phase`.
- Default relation between two parties is `0.3` (M2d adds courting); price test is `weight_sum >= price * (1 - 0.5 * relation)`.
- Never push or create remote repos. Commits small, end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. TDD: failing test first.

## Rulings made while planning

- Phases: `Formation` = formateur attempts in progress (old government is caretaker); `Caretaker` = two attempts failed, waiting `caretaker_turns` before early elections.
- An AI formateur answers within one turn. A player formateur has `max_formation_turns` turns; refusals do not end the attempt, the timer does.
- An unanswered offer to the player lapses as a rejection at the next `EndTurn` (the player must opt in).
- Besides demand-vs-red-line conflicts, two agreed demands on the same policy whose feasible ranges do not overlap are refused as `contradiction` (commitments could never both be met).
- Only the government leader sets policies: other roles get `error.not_leader`; outside `Governing` the error is `error.no_majority`. (AI governor for non-leader governments is M2c.)

## Review Focus

1. Election results with a single party above the threshold, or a party with a majority alone: formation still ends (single-party government without partners) (T2, T3).
2. No coalition can ever reach a majority (many tiny parties) or all candidate coalitions are refused: ends in caretaker then early elections, never an endless loop (T3, T5).
3. The player is formateur and does nothing: the timer fails the attempt and the next party tries (T3).
4. Player refuses or ignores an offer: AI proceeds with a coalition without the player's party (T3, T4).
5. Replaying a saved game with formation actions reproduces the same government (T4).

---

## File structure

```text
core/Politics/PartyMath.cs        create: Distance(PartyDef, PartyDef, ranges), policy-range helper
core/Politics/Government.cs       create: Commitment, Government, AgreedDemand, CoalitionProposal, Refusal, Role
core/Politics/CoalitionRules.cs   create: HasMajority, Evaluate
core/Politics/CoalitionBuilder.cs create: AI formateur (minimal winning coalition + demand allocation)
core/Politics/PoliticsState.cs    modify: Phase gains Formation/Caretaker; CountryPolitics gains trailing optional fields; new events
core/Gameplay/PlayerAction.cs     modify: ProposeCoalition, AcceptOffer, RejectOffer, CallEarlyElection
core/Gameplay/SaveData.cs         modify: serialize the new actions
core/Gameplay/Game.cs             modify: phase machine, new actions, SuggestCoalition, role checks
tools/OpenPolitics.Cli/Play/PlayLoop.cs  modify: formation screen and commands
data/base/localization/{cs,en}.yaml     modify: formation texts
core.tests/Politics/{CoalitionRulesTests,CoalitionBuilderTests,FormationTests,FormationActionTests}.cs
core.tests/Play/PlayFormationTests.cs
docs/decisions/0003-m2b-formation.md
```

### Task 1: Rules (majority, red lines, price) and government types

**Files:** Create `core/Politics/PartyMath.cs`, `core/Politics/Government.cs`, `core/Politics/CoalitionRules.cs`; Test `core.tests/Politics/CoalitionRulesTests.cs`.

**Interfaces:** Consumes `PartyDef`, `DemandDef`, `RedLineDef`, `PolicyDef`. Produces (namespace `OpenPolitics.Core.Politics`):
- `public enum Role { Leader, Partner, Opposition }`
- `public sealed record AgreedDemand(string PartyId, string DemandId);`
- `public sealed record CoalitionProposal(string Formateur, IReadOnlyList<string> Partners, IReadOnlyList<AgreedDemand> Agreements);`
- `public sealed record Refusal(string Reason, string? PartyId, string? DemandId);`
- `public sealed record Commitment(string PartyId, DemandDef Demand, double Baseline, int DueTurn);`
- `public sealed record Government(string Leader, IReadOnlyList<string> Parties, IReadOnlyList<Commitment> Commitments, int FormedTurn);` (`Parties` ordinal-sorted, includes the leader)
- `public static class PartyMath { public static double Distance(PartyDef a, PartyDef b, IReadOnlyDictionary<string,double> policyRange); }` weighted mean (weights = product of both platform weights) of `|targetA - targetB| / range` over policies both mention, clamped to [0,1]; 0.5 when none shared.
- `public static class CoalitionRules { public static bool HasMajority(IEnumerable<string> parties, IReadOnlyDictionary<string,int> seats, double fraction); public static IReadOnlyList<Refusal> Evaluate(CoalitionProposal p, IReadOnlyDictionary<string,PartyDef> parties, IReadOnlyDictionary<string,int> seats, double fraction, int programSlots, Func<string,string,double> relation); public static bool DemandConflictsWithRedLine(DemandDef d, RedLineDef r); public static bool DemandsContradict(DemandDef a, DemandDef b); }`

Behaviour of `Evaluate` (returns all refusals, empty = accepted, checks in this order and returns every finding): `unknown` (party or demand id not found, or an agreement's party not a partner); `slots` (`Agreements.Count > programSlots`); `majority` (formateur + partners seats not `> fraction * total`); `red_line` (an agreed demand conflicts with a red line of any coalition member, `DemandId` and `PartyId` of the demand's owner); `contradiction` (two agreed demands contradict); `price` (a partner whose agreed demand weight sum is `< price * (1 - 0.5 * relation(formateur, partner))`; `PartyId` = that partner). The formateur's own price is never checked.
`DemandConflictsWithRedLine`: only for `Kind == "policy"` with `Subject == r.Policy`; true when `d.Target` lies in the red line's forbidden range (`op ">="`: `Target >= Threshold`; `op "<="`: `Target <= Threshold`).
`DemandsContradict`: same policy; feasible sets (`>= t` is `[t, inf)`, `<= t` is `(-inf, t]`) do not intersect (`>= a` and `<= b` contradict when `a > b`; same-direction demands never contradict).

- [ ] **Step 1: Write failing tests** (hand-made `PartyDef`s): `HasMajorityNeedsMoreThanHalf` (100 of 200 is not a majority, 101 is), `RedLineConflictForbiddenRange` (demand tax `<= 22` vs red line `>= 40` no conflict; demand `>= 45` vs red line `>= 40` conflict), `OppositeDemandsContradict` / `SameDirectionDemandsDoNot`, `AcceptsAValidCoalition`, `RefusesWithoutMajority`, `RefusesWhenDemandHitsAPartnersRedLine` (reason `red_line`, carries party and demand), `RefusesWhenPriceNotMet` (price 4, relation 0.3, needs `4 * 0.85 = 3.4`; weight 3 refused, weight 3.5 accepted), `RefusesTooManyAgreements` (`slots`), `UnknownPartyOrDemandIsRefusedNotThrown`, `AllFindingsAreReported` (majority and price in one call), `DistanceIsZeroForIdenticalPlatformsAndHalfWhenNothingShared`.
- [ ] **Step 2:** Run `dotnet test core.tests --filter CoalitionRulesTests` -> FAIL (types missing). **Step 3:** Implement. **Step 4:** Run -> PASS.
- [ ] **Step 5: Commit** `feat(core): coalition rules (majority, red lines, price)`.

### Task 2: AI formateur (coalition builder)

**Files:** Create `core/Politics/CoalitionBuilder.cs`; Test `core.tests/Politics/CoalitionBuilderTests.cs`.

**Interfaces:** Consumes T1. Produces: `public static class CoalitionBuilder { public static CoalitionProposal? Build(string formateur, IReadOnlyDictionary<string,PartyDef> parties, IReadOnlyDictionary<string,int> seats, IReadOnlyDictionary<string,double> policyRange, double fraction, int programSlots, Func<string,string,double> relation, ISet<string> excluded); }`

Algorithm: candidates are all subsets of `parties` with seats `> 0`, not in `excluded`, other than the formateur, such that formateur + subset has a majority and is *minimal winning* (removing any partner loses the majority; the empty subset is allowed when the formateur alone has a majority). Order candidates by `score = mean over partners of (Distance(formateur, p) - 0.2 * relation(formateur, p))` ascending (empty subset: 0), then fewer partners, then partner ids joined ordinally. For each candidate in order try to allocate demands: for each partner in ordinal order, go through that partner's demands by weight descending (ties by id) and add a demand if it does not hit a red line of any coalition member and does not contradict an already chosen demand and slots remain, until the partner's price requirement `price * (1 - 0.5 * relation)` is met; a partner whose requirement cannot be met makes the candidate fail. The first candidate whose proposal passes `CoalitionRules.Evaluate` with no refusals is returned; `null` if none. A party with price 0 needs no demands.

- [ ] **Step 1: Write failing tests**: `FormateurWithAMajorityGetsASoloGovernment` (empty partners, empty agreements), `PicksTheIdeologicallyClosestMinimalCoalition` (formateur plus two candidates, one closer), `ResultIsMinimalWinning`, `AllocatesDemandsToMeetEachPrice`, `SkipsDemandsThatHitRedLines`, `FailsWhenProgramSlotsAreTooFew` (returns null or another candidate that fits), `ExcludedPartiesAreNeverChosen`, `ReturnsNullWhenNoMajorityIsPossible`, `OutputAlwaysPassesEvaluate` (1000 random seat vectors over 3-9 random parties from `Pcg32`, random prices 0-6 and weights, 200 seats, fraction 0.5: whenever a proposal is returned `Evaluate` is empty) - Review Focus 1/2, `IsDeterministic` (same input twice gives equal proposals).
- [ ] **Step 2-4:** run FAIL, implement, run PASS. **Step 5: Commit** `feat(core): AI coalition builder`.

### Task 3: Phase machine in Game (AI formation, caretaker, early elections)

**Files:** Modify `core/Politics/PoliticsState.cs`, `core/Gameplay/Game.cs`; Test `core.tests/Politics/FormationTests.cs`.

**Interfaces:** Consumes T1-T2 and M2a. Produces:
- `enum Phase { Governing, Formation, Caretaker }`.
- `public sealed record FormationInfo(string Formateur, int Attempt, int TurnsLeft, bool PlayerIsFormateur, CoalitionProposal? Offer);`
- `CountryPolitics` gains trailing optional parameters `Government? Government = null, FormationInfo? Formation = null, Role PlayerRole = Role.Opposition`; `GovernmentParties` stays (the sitting or caretaker government's parties).
- New `PoliticalEvent`s: `FormationStarted(int Turn, string Formateur)`, `FormationFailed(int Turn, string Formateur)`, `OfferMade(int Turn, CoalitionProposal Proposal)`, `OfferLapsed(int Turn)`, `GovernmentFormed(int Turn, Government Government)`, `EarlyElectionsCalled(int Turn)`.

Behaviour (inside `StepPolitics`, after the poll; replaces "government unchanged" of M2a):
1. At game start `Government = new Government(scenario.Leader ?? first government party, scenario government parties sorted, empty commitments, 0)`, `Phase.Governing`; `PlayerRole` from it.
2. Election day (turns-to-election reaches 0 while `Governing`, or the early-election countdown ends): after `ElectionHeld`, phase becomes `Formation`; formation queue = parties with seats `> 0` ordered by seats desc, votes desc, id; attempt 1 starts with `queue[0]` (`FormationStarted`); `TurnsToElection` resets to the interval.
3. A new attempt begins the turn *after* the election (the election turn only sets it up). Each `EndTurn` in `Formation`: a pending offer lapses (`OfferLapsed`, the player's party is added to the attempt's `excluded` set) and the AI re-tries the same attempt next turn; if the formateur is AI: run `CoalitionBuilder.Build` with relation default 0.3 and the attempt's excluded set; none -> `FormationFailed`, next attempt; a proposal that includes the player's party -> `OfferMade` (stays pending); otherwise `GovernmentFormed` (commitments = agreed demands with baseline = current policy/value, `DueTurn = formedTurn + due_turns`). If the formateur is the player: `TurnsLeft--`; at 0 -> `FormationFailed`, next attempt. Attempt index counts failures per election; the formateur of attempt `k` is `queue[k-1]`; after the second failure (or when the queue is exhausted) phase becomes `Caretaker` for `caretaker_turns`, `EarlyElectionsCalled`, then a new election is held (same vote logic as an ordinary one) and formation starts again.
4. `GovernmentFormed` sets phase `Governing`, `Government`, `GovernmentParties`, `PlayerRole` (Leader when the leader is the player's party, Partner when a member, else Opposition).
5. `SetPolicy` while the phase is not `Governing` is refused with `error.no_majority`; when the player's role is not Leader with `error.not_leader` (both only when the scenario has politics).

- [ ] **Step 1: Write failing tests** with a fixture (extend `GamePoliticsTests.AddPolitics`-style helper; parties with demands and red lines, a seeded election outcome forced by scenario support and `turns_to_election: 1`): `StartingGovernmentIsGoverningWithRoleLeader`, `ElectionStartsFormationNextTurn`, `LargestPartyIsFormateur`, `AIFormsAMajorityGovernmentWithinOneTurn` (player not formateur and not invited: government formed, majority in seats, commitments present with baselines), `PlayerIsOfferedAPlaceWhenInvited` (Offer visible in `Formation`), `UnansweredOfferLapsesAndAIProceedsWithoutThePlayer` (Review Focus 4), `PlayerFormateurWhoDoesNothingFailsAfterTimerThenNextPartyTries` (Review Focus 3), `TwoFailedAttemptsLeadToCaretakerThenEarlyElection`, `NoMajorityPossibleEndsInEarlyElections` (many tiny parties, Review Focus 2), `SinglePartyAboveThresholdFormsSoloGovernment` (Review Focus 1), `SetPolicyIsRefusedInFormationAndForNonLeaders`, `OldScenariosAreUnchanged` (full suite incl. goldens), `FormationIsDeterministicPerSeed`.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): government formation phases`.

### Task 4: Player actions (propose, accept, reject, early election) and save

**Files:** Modify `core/Gameplay/PlayerAction.cs`, `core/Gameplay/SaveData.cs`, `core/Gameplay/Game.cs`; Test `core.tests/Politics/FormationActionTests.cs`.

**Interfaces:** Consumes T3. Produces:
- `public sealed record ProposeCoalition(IReadOnlyList<string> Partners, IReadOnlyList<AgreedDemand> Agreements) : PlayerAction;` `public sealed record AcceptOffer : PlayerAction;` `public sealed record RejectOffer : PlayerAction;` `public sealed record CallEarlyElection : PlayerAction;`
- `Game.SuggestCoalition(): CoalitionProposal?` - what the AI would propose if the player's party were formateur (`Build` with the player's party as formateur; null when not in `Formation` as formateur).
- Errors: `ProposeCoalition` outside formation or by a non-formateur: `error.not_formateur`; refusals: `error.refused.<reason of the first refusal>` with `Detail` `"<PartyId>:<DemandId>"` (or empty); success forms the government at once (`GovernmentFormed` in the next report; state already `Governing`). `AcceptOffer`: forms the offered government (`error.no_offer` when none). `RejectOffer`: same as lapse (immediate `OfferLapsed`, excluded). `CallEarlyElection`: only the Leader in `Governing` (`error.not_leader` / `error.wrong_phase`), sets the countdown so the election happens at the end of this turn.
- Accepted actions are logged like `SetPolicy`; `SaveData` serializes them (`type` plus `partners`, `agreements` as `"party:demand"` strings) and replay reproduces the state. A refused action is not logged.

- [ ] **Step 1: Write failing tests**: `PlayerProposesAValidCoalitionAndFormsAGovernment`, `RefusalCarriesAReasonAndDetail` (`majority`, `red_line`, `price`), `SuggestCoalitionIsAcceptedByTheRules` (suggestion passed to `ProposeCoalition` succeeds), `NonFormateurCannotPropose`, `AcceptOfferFormsTheGovernmentWithThePlayerAsPartner`, `RejectOfferContinuesWithoutThePlayer`, `OnlyTheLeaderCanCallEarlyElection` and `EarlyElectionHappensAtTheEndOfTheTurn`, `SaveAndLoadReplayFormationActions` (Review Focus 5, includes the yaml round trip via `ToYaml`/`FromYaml`), `RefusedActionsAreNotSaved`.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): coalition proposal, offers and early elections as player actions`.

### Task 5: Client screens, 1000-election gate, decision note

**Files:** Modify `tools/OpenPolitics.Cli/Play/PlayLoop.cs`, `data/base/localization/{cs,en}.yaml`; Test `core.tests/Play/PlayFormationTests.cs`, `core.tests/Content/FormationGateTests.cs`; Create `docs/decisions/0003-m2b-formation.md`.

Client: the POLITICS block shows the phase (`Governing` / formation with formateur and turns left / caretaker). During `Formation` as formateur the commands are `coalition` (shows seats, majority line, each party's demands and price), `auto` (proposes `Game.SuggestCoalition()`), `invite PARTY` / `drop PARTY` / `agree PARTY DEMAND` / `submit` (draft built client-side and sent as `ProposeCoalition`; refusals printed with the reason text); when an offer is pending: `accept` / `reject` and the offer is printed (leader, partners, agreed demands); `early` calls early elections for the leader. The turn report lists formation events (formation started, failed, offer, government formed, early elections). Texts under `ui.formation.*` in cs and en.

Gate (spec section 14): over 1000 seeds on `base:cz_2015` (the player's party never acts except `auto` when it is formateur and `accept` for offers) for 48 turns: every election is followed by `GovernmentFormed` or `EarlyElectionsCalled` within `2 * max_formation_turns + caretaker_turns + 2` turns, the phase is never stuck, no exceptions, deterministic per seed for 20 sampled seeds. Record the share of elections that ended in early elections and the distribution of coalition sizes in `0003-m2b-formation.md`.

- [ ] **Step 1: Write failing tests**: `ForceElectionShowsFormationScreen` (play client: after the election turn the output contains the formation heading), `AutoProposesAndGovernmentFormsInTheClient`, `OfferCanBeAcceptedInTheClient`, `FormationTextsExistInCzech`, `FormationGateOver1000Seeds` (above).
- [ ] **Step 2-4:** run FAIL, implement, run the full suite and `validate data/base` PASS. **Step 5:** write the decision note with the measured numbers. **Step 6: Commit** `feat: formation screens in the play client and the 1000-election gate`.

---

## Self-review notes

- Spec coverage (section 5): formateur ordering (T3), `ProposeCoalition` with majority/red-line/price/slots and reasons (T1, T4), AI formateur with minimal winning ideological coalition and greedy demand allocation (T2), offers (T3, T4), caretaker and early elections after two failures (T3), commitments with baselines (T3). Section 10 actions `ProposeCoalition`, `AcceptOffer`, `RejectOffer`, `CallEarlyElection` and events (T3, T4).
- Deferred: loyalty and commitment progress, departures and falls (M2c), AI governor for non-leader governments (M2c), courting/relations (M2d, default 0.3 now).
