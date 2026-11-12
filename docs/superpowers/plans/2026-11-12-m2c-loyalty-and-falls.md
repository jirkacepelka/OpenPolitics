# M2c Loyalty, Commitments and Government Falls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (native; the user chose no approval stops). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Partners have loyalty that rises and falls with how well the government delivers on the coalition agreement, how happy their voters are and whether red lines are crossed. Low loyalty leads to warnings, ultimatums and departures; a government that loses its majority falls into a new formation round or early elections. A government not led by the player is run by a simple AI governor.

**Architecture:** Pure math in `core/Politics/Commitments.cs` and `LoyaltyModel.cs`; state and thresholds inside `PoliticsEngine`; the AI governor returns policy moves that `Game` applies. A politics-only batch runner with strategies (`ignorer`, `keeper`, `random`, `extreme`) measures the gate.

**Tech Stack:** C# net10.0, xUnit.

**Spec:** `docs/specs/2026-11-12-m2-domestic-politics-design.md` sections 6, 7, 8 (AI governor), 9, 10, 11 (gate).

## Global Constraints

- Sandbox scenarios and all earlier tests/goldens stay bit-identical; politics uses its own RNG stream.
- Loyalty is in [0, 100] (an attitude, so bounded like voter satisfaction); starts at 70; leader has none.
- Defaults (record `LoyaltyParams`): warn below 60 (reset at 65), ultimatum below 40 for `ultimatum_turns = 4` (resolved above 50 or when the named demand reaches its expected progress), leave at `<= 15` or on ultimatum expiry, red-line crossing shock 30, lambda 1 per year (2 for swing parties), target `clamp(50 + 40*fulfil - 60*shortfall + 20*base_mood - 20*[red line violated], 0, 100)`.
- Sandbox (no politics) and `Phase != Governing` behaviour from M2b unchanged.
- Never push or create remote repos. Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. TDD: failing test first.

## Rulings made while planning

- `base_mood_p` weights voter groups by their share of the party's base: `size_g * exp(affinity(g,p))` normalised; `opposition_pressure` is 0 until M2d.
- A partner's commitment progress uses the *current* policy/value; value demands count as satisfied when the value condition holds.
- When a partner leaves and the remaining coalition has no majority, the government falls: a formation round starts with the government leader first, then by seats; the leaver may be re-invited at its price. A newly formed government replaces the old one entirely (all loyalties reset to 70, commitments replaced).
- The AI governor only runs when the player is not the leader and the phase is `Governing`; the player's own government is run by the player (the `keeper` strategy moves policies itself).
- Politics now steps after the economy state is committed (so politics reads the new values); turn numbering of events is unchanged.

## Review Focus

1. A partner with no commitments (price 0 or none agreed): loyalty still evolves (fulfil 0.5) and cannot leave on a divide-by-zero (T1, T2).
2. Two partners leaving in the same turn, or the leaver being the only reason for the majority: one fall, never a crash or two falls (T3).
3. A government falling in the same turn as an election, or while a formation round is already running: no double formation, no stuck phase (T3).
4. Commitments on values whose target equals the baseline, or already satisfied at formation: progress 1, no NaN (T1).
5. 1000 games of `ignorer` and `keeper`: no exception, no stuck phase, deterministic per seed (T6).

---

## File structure

```text
core/Politics/Commitments.cs     create: Progress, Expected, Fulfil, Shortfall, MostLate
core/Politics/LoyaltyModel.cs    create: LoyaltyParams, Target, Step, RedLineViolated, BaseMood
core/Politics/Governor.cs        create: AI governor (moves toward the largest unmet commitment)
core/Politics/PoliticsState.cs   modify: CommitmentStatus, loyalty/ultimatum fields on CountryPolitics; new events
core/Politics/PoliticsEngine.cs  modify: loyalty state, thresholds, departures, falls, governor moves
core/Gameplay/Game.cs            modify: step order (commit, then politics), apply governor moves
core/Batch/PoliticsBatch.cs      create: strategies and report
tools/OpenPolitics.Cli/Commands/BatchCommand.cs  modify: --politics flag
tools/OpenPolitics.Cli/Play/PlayLoop.cs          modify: coalition panel, new events
data/base/localization/{cs,en}.yaml              modify
core.tests/Politics/{CommitmentsTests,LoyaltyModelTests,LoyaltyTests,FallTests,GovernorTests}.cs
core.tests/Batch/PoliticsBatchTests.cs
docs/decisions/0004-m2c-loyalty.md
```

### Task 1: Commitment math and loyalty model (pure)

**Files:** Create `core/Politics/Commitments.cs`, `core/Politics/LoyaltyModel.cs`; Test `core.tests/Politics/CommitmentsTests.cs`, `core.tests/Politics/LoyaltyModelTests.cs`.

**Interfaces:** Consumes `Commitment`, `DemandDef`, `RedLineDef`, `PartyDef`, `VoterGroupDef` (M2a/M2b). Produces (namespace `OpenPolitics.Core.Politics`):
- `public static class Commitments { public static bool Satisfied(DemandDef d, double x); public static double Progress(Commitment c, double x); public static double Expected(Commitment c, int turn, int formedTurn); public static double Fulfil(IReadOnlyList<Commitment> mine, Func<Commitment,double> current, int turn, int formedTurn); public static double Shortfall(...same args...); public static Commitment? MostLate(IReadOnlyList<Commitment> mine, Func<Commitment,double> current, int turn, int formedTurn); }` - `Progress` is 1 when satisfied, else `clamp((x - baseline) / (target - baseline), 0, 1)` (0 when `target == baseline` and not satisfied); `Expected = min(1, (turn - formedTurn) / demand.DueTurns)`; `Fulfil` weight-averaged progress (0.5 with no commitments); `Shortfall` weight-averaged `max(0, expected - progress)` (0 with none); `MostLate` is the commitment with the largest `weight * (expected - progress)` (ties: lower demand id; null when none late).
- `public sealed record LoyaltyParams(double Start = 70, double WarnBelow = 60, double WarnReset = 65, double UltimatumBelow = 40, int UltimatumTurns = 4, double ResolveAbove = 50, double LeaveAt = 15, double Shock = 30, double Lambda = 1.0, double SwingLambda = 2.0);`
- `public static class LoyaltyModel { public static double Target(double fulfil, double shortfall, double baseMood, bool redLineViolated); public static double Step(double loyalty, double target, double lambdaPerYear, double dt); public static bool RedLineViolated(RedLineDef r, double policyValue); public static double BaseMood(PartyDef p, IReadOnlyList<VoterGroupDef> groups, IReadOnlyDictionary<string,double> satisfaction); }` - `Target` per the Global Constraints formula; `Step = clamp(loyalty + lambda*dt*(target - loyalty), 0, 100)`; `RedLineViolated`: `>=` -> value `>= threshold`, `<=` -> value `<= threshold`; `BaseMood` is `sum_g share_g * s_g` with `share_g` proportional to `size_g * exp(affinity(g,p))`, result in [-1, 1].

- [ ] **Step 1: Write failing tests**: progress at baseline 0, halfway, at target, beyond target (clamped), already satisfied at formation (1), `target == baseline` (no NaN, Review Focus 4), `Expected` growth and cap at 1, `Fulfil`/`Shortfall` weighted and defaults with no commitments (Review Focus 1), `MostLate` choice and tie, `Target` examples (all delivered and happy -> 50+40+20 = 100 clamped; shortfall 1 and unhappy -> 0), `Step` approaches the target monotonically and never leaves [0, 100], swing lambda doubles the rate, red-line directions, `BaseMood` bounded and weights groups by size and affinity.
- [ ] **Step 2-4:** run FAIL, implement, run PASS. **Step 5: Commit** `feat(core): commitment progress and loyalty model`.

### Task 2: Loyalty in the engine (warnings, ultimatums, departures)

**Files:** Modify `core/Politics/PoliticsState.cs`, `core/Politics/PoliticsEngine.cs`, `core/Gameplay/Game.cs`; Test `core.tests/Politics/LoyaltyTests.cs`.

**Interfaces:** Consumes T1 and M2b. Produces:
- `public sealed record CommitmentStatus(string PartyId, string DemandId, double Progress, double Expected);`
- `public sealed record UltimatumInfo(string PartyId, string? DemandId, int TurnsLeft);`
- `CountryPolitics` gains trailing optional `IReadOnlyDictionary<string,double>? Loyalty = null, IReadOnlyList<CommitmentStatus>? Commitments = null, IReadOnlyList<UltimatumInfo>? Ultimatums = null`.
- New events: `LoyaltyWarning(int Turn, string PartyId, string? DemandId)`, `Ultimatum(int Turn, string PartyId, string? DemandId, int TurnsLeft)`, `PartnerLeft(int Turn, string PartyId)`.
- `PoliticsEngine` ctor gains `double turnsPerYear` and reads current values through the existing `baseline` func (renamed `lookup`, "current value of kind/subject"). `Game.EndTurn` now commits the new values and increments the turn *before* calling `_politics.Step(_turn, ...)`.

Behaviour in `Step` while `Phase.Governing`, for every partner (government parties except the leader, ordinal order): compute fulfil/shortfall from its commitments, `base_mood` from group satisfaction, red-line violation of its own red lines, `target`, `Step` loyalty (lambda 1 per year, 2 for swing). A red line that was not violated last turn and is now: `loyalty -= Shock` at once. Then: warning when loyalty drops below `WarnBelow` (once until it recovers above `WarnReset`), naming the most late demand; ultimatum when below `UltimatumBelow` and none active (names the most late demand, `TurnsLeft = 4`, counts down each turn; resolved when loyalty `> ResolveAbove` or the named demand's progress `>=` its expected); the partner leaves (`PartnerLeft`) when loyalty `<= LeaveAt` or its ultimatum expires: removed from the government parties and its commitments dropped. Loyalty starts at `Start` for every partner at formation and at game start. A pure departure with the majority intact changes nothing else (the fall is Task 3).

- [ ] **Step 1: Write failing tests** (extend `FormationFixture` with policy commitments; helper to start a government with a partner): `LoyaltyStartsAt70ForPartnersNotTheLeader`, `DeliveringCommitmentsKeepsLoyaltyHigh`, `IgnoringCommitmentsLetsLoyaltyDecay` (monotone over turns), `WarningFiresOnceBelow60ThenAgainAfterRecovery`, `UltimatumNamesTheMostLateDemandAndCountsDown`, `UltimatumResolvedByDelivery`, `PartnerLeavesWhenUltimatumExpires`, `PartnerLeavesAtLoyalty15`, `RedLineCrossingCostsThirtyAtOnce`, `PartnerWithoutCommitmentsDoesNotCrash` (Review Focus 1), `SnapshotExposesLoyaltyCommitmentsAndUltimatums`, `SwingPartyReactsFaster`, `OldScenariosAreUnchanged` (full suite incl. goldens and M2a/M2b tests).
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): partner loyalty, warnings, ultimatums and departures`.

### Task 3: Falls and new majorities

**Files:** Modify `core/Politics/PoliticsState.cs`, `core/Politics/PoliticsEngine.cs`; Test `core.tests/Politics/FallTests.cs`.

**Interfaces:** Consumes T2. Produces event `GovernmentFell(int Turn)`.

Behaviour: after departures in a turn, if the remaining government parties no longer hold the majority of seats: emit `GovernmentFell`, start a formation round (`Phase.Formation`, `FormationStarted`) with the queue = government leader first, then the other parties with seats ordered by seats desc, votes desc, id; `attempt = 1`, `TurnsLeft = max_formation_turns`; the sitting government remains caretaker until `GovernmentFormed`. The existing M2b rules decide the rest (AI forms, player proposes or answers offers, two failures lead to caretaker and early elections). If a formation round or an election is already running that turn nothing new starts (no double fall). A formed government replaces the old one, loyalties reset to 70.

- [ ] **Step 1: Write failing tests**: `PartnerLeavingWithMajorityIntactDoesNotCauseAFall`, `PartnerLeavingWithoutMajorityCausesAFallAndFormationStarts` (leader is first formateur), `TwoPartnersLeavingInOneTurnIsOneFall` (Review Focus 2), `PlayerLeaderCanRebuildWithAProposal` (`ProposeCoalition` accepted; former partner may be invited), `FailedRebuildEndsInEarlyElections`, `FallDuringFormationOrOnElectionDayDoesNotDoubleStart` (Review Focus 3), `NewGovernmentResetsLoyalty`.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): government falls and rebuilding`.

### Task 4: AI governor for governments the player does not lead

**Files:** Create `core/Politics/Governor.cs`; Modify `core/Politics/PoliticsEngine.cs`, `core/Gameplay/Game.cs`; Test `core.tests/Politics/GovernorTests.cs`.

**Interfaces:** Consumes T1-T3. Produces `public sealed record GovernmentMeasure(int Turn, string PolicyId, double From, double To) : PoliticalEvent(Turn)`; `internal static class Governor { public static (string PolicyId, double Value)? Move(Government g, Func<string,double> policy, Func<string,double> value, IReadOnlyDictionary<string,PolicyDef> policies, int turn); }` - chooses the policy commitment with the largest `weight * unmet_fraction`, and moves its policy by `25%` of the remaining gap (clamped to the policy's range); `null` when everything is satisfied or only value commitments are unmet.
`PoliticsEngine.Step` returns governor moves (turn, phase `Governing`, player not leader) and `Game` applies each as an accepted pending policy change (same delay rules as `SetPolicy`, not written to the player action log) and emits `GovernmentMeasure` (also for the M2d stances).

- [ ] **Step 1: Write failing tests**: `MovesTheLargestUnmetCommitmentByAQuarterOfTheGap`, `StopsWhenSatisfied`, `IgnoresValueCommitments`, `RespectsPolicyRange`, `DoesNotRunWhenThePlayerIsTheLeader`, `DoesNotRunDuringFormation`, `PartnersStayLoyalLongerUnderTheGovernorThanUnderAnIdleLeader` (same seed, compare loyalty after 16 turns), `GovernorMovesAreDeterministicAndReplayed` (save/load equality).
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): AI governor`.

### Task 5: Client coalition panel and new events

**Files:** Modify `tools/OpenPolitics.Cli/Play/PlayLoop.cs`, `data/base/localization/{cs,en}.yaml`; Test `core.tests/Play/PlayCoalitionTests.cs`.

While `Governing` the POLITICS block gets a COALITION section when the government has partners: per partner a loyalty bar and word (`stable`, `uneasy`, `ultimatum`), the commitments with progress against expected (`done`, `on track`, `late`) and active ultimatums with turns left. The `coalition` command shows this panel when the player is not forming a government. Events print: warning, ultimatum, partner left, government fell, government measure (by the AI governor). Texts in cs and en.

- [ ] **Step 1: Write failing tests** (pack = base + in-memory test pack scenario as in `PlayFormationTests`): `CoalitionPanelShowsLoyaltyAndCommitments`, `LoyaltyEventsAreReportedInTheQuarter`, `CoalitionTextsExistInCzech`.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite and `validate data/base` PASS. **Step 5: Commit** `feat: coalition panel in the play client`.

### Task 6: Politics batch, strategies, gate and tuning

**Files:** Create `core/Batch/PoliticsBatch.cs`, `docs/decisions/0004-m2c-loyalty.md`; Modify `tools/OpenPolitics.Cli/Commands/BatchCommand.cs`; Test `core.tests/Batch/PoliticsBatchTests.cs`.

**Interfaces:** Consumes everything. Produces `public enum PoliticsStrategy { Ignorer, Keeper, Random, Extreme }`; `public static class PoliticsBatch { public static PoliticsBatchReport Run(PackSet packs, string scenarioId, int games, int turns, PoliticsStrategy strategy, ulong firstSeed = 1); }`; `public sealed record PoliticsBatchReport(int Games, int Governments, int FirstCoalitionsLosingAPartnerWithin12Years, int Falls, int FallsWithNewMajority, double MeanFirstCoalitionLifetimeTurns, ... )` with a `ToText()` and the CLI flag `batch --politics --strategy ignorer|keeper|random|extreme --games N`.
Strategies act only through the public API each turn: `Ignorer` does nothing except answering formation (`auto` when formateur, `accept` offers); `Keeper` additionally sets the policy of the largest unmet commitment 25% of the gap toward its target while leader; `Random` sets random in-range policies every few turns; `Extreme` sets policies to the range ends.
Gate (spec section 11), measured over 1000 games x 48 turns of `base:cz_2015` with a player-led coalition forced at the start (scenario in the test pack where the player's party leads a majority coalition): `Ignorer`: at least 80% of the first coalitions lose a partner within 12 years; of the falls that left no majority, at least 60% produce a new majority (formed government) or early elections that produce one within the run; `Keeper`: mean first-coalition lifetime is at least 1.5x that of `Ignorer`. Tune `LoyaltyParams` defaults, the scenario and party content until the gate holds; record numbers and any retuning in the decision note.

- [ ] **Step 1: Write failing tests**: `StrategiesOnlyUsePublicApiAndNeverThrow` (200 games each strategy), `IgnorerCoalitionsLosePartners` (>= 80%), `FallsLeadToNewMajorities` (>= 60%), `KeeperCoalitionsLastLonger` (>= 1.5x), `BatchIsDeterministic`, `NoStuckPhase` (no game spends more than 2*max_formation + caretaker + 2 consecutive turns outside `Governing` after a fall) - Review Focus 5.
- [ ] **Step 2-4:** run FAIL, implement, tune, run the full suite and a CLI smoke run PASS. **Step 5:** write the decision note with measured numbers. **Step 6: Commit** `feat: politics batch runner and the M2c gate`.

---

## Self-review notes

- Spec coverage (section 6): loyalty target-and-lag (T1, T2), thresholds, warning, ultimatum, departure (T2), consequences of leaving and falls (T3), conflicting demands handled by shortfall (T1/T2); section 7 swing: doubled lambda (T1/T2); section 8 AI governor (T4); section 9 turn order (Game step order, T2); section 10 events `LoyaltyWarning`, `Ultimatum`, `PartnerLeft`, `GovernmentFell` (T2, T3); section 11 gate (T6).
- Deferred to M2d: opposition actions, `opposition_pressure`, momentum, credibility, courting (relations), party management; `GovernmentMeasure` events feed M2d stances.
