# M2a Parties, Support Model and Elections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (the user chose native execution) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fictional parties with a platform and voter-group affinities, a poll/support model driven by voter satisfaction, a proportional electoral-system module (D'Hondt + threshold), and elections that produce seats - all data-driven and deterministic, playable in the text client.

**Architecture:** New data kinds (`parties/`, `electoral_systems/`, a `politics:` block in countries and scenarios) flow through the existing reader, loader and validator. A new `OpenPolitics.Core.Politics` namespace holds `IElectoralSystem`, `SupportModel` and the political state kept inside `Game` for scenarios that define politics. Scenarios without politics behave exactly as in M1 (goldens unchanged).

**Tech Stack:** .NET 10, C#, xUnit, YamlDotNet (unchanged).

**Spec:** `docs/specs/2026-11-12-m2-domestic-politics-design.md` sections 1-4, 9-10, 13 (M2a part). M1 constraints in `docs/specs/2026-11-12-simulation-model-design.md` still apply.

## Global Constraints

- `export PATH="$HOME/.dotnet:$PATH"`; run from the repo root; `net10.0`; warnings are errors.
- Determinism: PCG32 only; politics draws noise from the game RNG in ordinal id order (groups, then parties); no hash-order iteration.
- Core has no Godot types; texts only through localization keys; fictional parties only, no real names.
- Voter-group satisfaction stays bounded to [-1, 1]; party support shares are fractions that sum to 1.
- A scenario without a `politics:` block must produce bit-identical results to before (existing golden tests stay green).
- Never push or create remote repos. Commits small, end with the attribution line from the session reminder.
- TDD: failing test first, watch it fail.

## Review Focus

1. A party platform or affinity naming an unknown policy or voter group: diagnostic with file and line, never an exception (T1).
2. Starting support that does not sum to 1, a party with zero support, or a single party: calibration still terminates and shares stay finite and sum to 1 (T3).
3. All votes below the threshold, or a single party above it: seats still sum to the seat count (T2).
4. Scenario whose `player_party` or government party is not defined: validator error, not a crash in `NewGame` (T1/T4).
5. 1000 consecutive simulated elections: shares sum to 1, seats sum to the seat count, deterministic per seed (T5).

---

## File structure

```text
core/Packs/Definitions.cs        modify: PartyDef, ElectoralSystemDef, CountryPolitics, ScenarioPolitics (+ fields on CountryDef/ScenarioDef)
core/Packs/PackSet.cs            modify: Parties, ElectoralSystems
core/Packs/PackReader.cs         modify: parties/, electoral_systems/, politics blocks
core/Packs/PackLoader.cs         modify: merge parties and electoral systems
core/Packs/PackValidator.cs      modify: party/electoral/politics checks
core/Politics/ElectoralSystem.cs IElectoralSystem, ProportionalSystem, ElectoralSystems
core/Politics/SupportModel.cs    SupportModel (+ calibration, noise)
core/Politics/PoliticsState.cs   CountryPolitics snapshot records, PoliticalEvent records, Phase
core/Gameplay/Game.cs            modify: politics step, election
data/base/parties|electoral_systems|countries|scenarios|localization   content
core.tests/Politics/*            tests
tools/OpenPolitics.Cli/Play/PlayLoop.cs   modify: POLITICS block in the overview
```

---

### Task 1: Data format for parties, electoral systems and politics blocks

**Files:** Modify `core/Packs/{Definitions,PackSet,PackReader,PackLoader,PackValidator}.cs`; Test `core.tests/Packs/PoliticsDataTests.cs`.

**Interfaces:** Produces (new records in `OpenPolitics.Core.Packs`):
`PlatformPosition(double Target, double Weight)`;
`DemandDef(string Id, string Kind /* policy|value */, string Subject, string Op /* >= <= */, double Target, double Weight, int DueTurns)`;
`RedLineDef(string Policy, string Op, double Threshold)`;
`FactionDef(string Id, double Weight, IReadOnlyDictionary<string,double> Platform)`;
`PartyDef(string Id, string NameKey, IReadOnlyDictionary<string,PlatformPosition> Platform, IReadOnlyDictionary<string,double> Affinity, bool Swing, IReadOnlyList<DemandDef> Demands, IReadOnlyList<RedLineDef> RedLines, double Price, IReadOnlyList<FactionDef> Factions, string Source)`;
`ElectoralSystemDef(string Id, string Type, int Seats, string Method, double Threshold, string Source)`;
`CountryPoliticsDef(string ElectoralSystem, int ElectionIntervalTurns, double MajorityFraction, int MaxFormationTurns, int CaretakerTurns, int ProgramSlots)`;
`ScenarioPoliticsDef(string PlayerParty, IReadOnlyDictionary<string,double> Support, IReadOnlyList<string> Government, string? Leader, int TurnsToElection)`.
`CountryDef` gains a last parameter `CountryPoliticsDef? Politics = null`; `ScenarioDef` gains `ScenarioPoliticsDef? Politics = null`. `PackSet` gains `Parties` and `ElectoralSystems` (ordinal-sorted) as optional trailing constructor arguments (default empty) so existing callers still compile; `PackSet.Where` kinds `party` and `electoral_system`. `RawPack` gains `Parties`, `ElectoralSystems`.
YAML (spec section 2): `parties/*.yaml` entries use `platform: {policyId: {target, weight}}`, `affinity: {groupId: number}`, `swing`, `demands: [{id, kind, policy|value, op, target, weight, due_turns}]`, `red_lines: [{policy, op, threshold}]`, `price`, `factions: [{id, weight, platform: {policyId: number}}]`; `electoral_systems/*.yaml` entries `type, seats, method, threshold`; country `politics: {electoral_system, election_interval_turns, majority_fraction, max_formation_turns, caretaker_turns, program_slots}`; scenario `politics: {player_party, support: {partyId: share}, government: [partyIds], leader, turns_to_election}`.
Validator (codes continue): `E004` unknown policy/value/group/party/electoral system references (platform, affinity, demands, red lines, faction platforms, country electoral_system, scenario player_party/support/government/leader); `E007` electoral type not `proportional`, method not `dhondt`, seats < 1, threshold outside [0,1), `op` not `>=`/`<=`, party price < 0, support shares not within 1e-6 of summing to 1 or negative, majority_fraction outside (0,1), intervals < 1; `E006` missing `name.<partyId>` (cs, en); `E003` duplicate ids (existing loader logic via kinds `party`, `electoral_system`).

- [ ] **Step 1: Write failing tests** in `PoliticsDataTests` using `TestPacks.Baseline()` plus extra files: `ReadsPartyWithAllFields` (all fields parsed, `Platform["t:tax"].Target == 20`, `Demands[0].Op == "<="`), `ReadsElectoralSystemAndPoliticsBlocks`, `PartyReferencingUnknownPolicyIsE004WithLine`, `AffinityToUnknownGroupIsE004`, `UnknownElectoralTypeIsE007`, `SupportNotSummingToOneIsE007`, `PlayerPartyNotDefinedIsE004`, `MissingPartyNameIsE006`, `DuplicatePartyIsE003`, `PartyWithUnknownFieldIsE002`. Baseline test pack gets one voter group `t:g1`, policy `t:tax`, values `t:gdp`.
- [ ] **Step 2: Run** `dotnet test` -> FAIL (types missing).
- [ ] **Step 3: Implement** records, `PackSet` additions, reader (new `Fields` helpers for nested mappings reuse `NumMap`, `Map`, `Seq`), loader merge for the two kinds, validator checks and localization requirement for parties.
- [ ] **Step 4: Run** full suite -> PASS (existing 208 tests unchanged).
- [ ] **Step 5: Commit** `feat(core): data format for parties, electoral systems and politics blocks`.

### Task 2: Electoral system module

**Files:** Create `core/Politics/ElectoralSystem.cs`; Test `core.tests/Politics/ElectoralSystemTests.cs`.

**Interfaces:** Consumes: `ElectoralSystemDef`. Produces:
`public interface IElectoralSystem { int Seats { get; } IReadOnlyDictionary<string,int> Allocate(IReadOnlyDictionary<string,double> votes); }` (keys = party ids, values = vote shares or counts; result has an entry for every party, 0 for no seats, keys ordinal-sorted).
`public sealed class ProportionalSystem : IElectoralSystem { public ProportionalSystem(int seats, double threshold); }` - parties with `votes / total < threshold` get none; D'Hondt highest averages; ties go to the lexicographically lower party id; if nobody passes the threshold, the threshold is ignored (so seats always sum to `Seats`; total votes <= 0 throws `ArgumentException`).
`public static class ElectoralSystems { public static IElectoralSystem Create(ElectoralSystemDef def); }` (throws `InvalidOperationException` for unknown type).

- [ ] **Step 1: Write failing tests**: `DHondtKnownExample` (votes a=100,b=80,c=30,d=20; 8 seats; threshold 0 -> a4,b3,c1,d0); `ThresholdExcludesSmallParties` (shares .5,.3,.17,.03 with threshold .05 and 10 seats -> d gets 0, seats sum 10); `TieGoesToLowerId` (a=b=100, 1 seat -> a); `NobodyAboveThresholdStillAllocatesAllSeats` (four parties of 20% with threshold .3); `SeatsAlwaysSumToTotal` (1000 random vote vectors from `Pcg32`, 2-12 parties, 200 seats); `SingleParty` (gets all seats); `AllZeroVotesThrows`; `FactoryCreatesProportionalAndRejectsUnknownType`.
- [ ] **Step 2: Run** -> FAIL. **Step 3: Implement.** **Step 4: Run** -> PASS. **Step 5: Commit** `feat(core): proportional electoral system (D'Hondt with threshold)`.

### Task 3: Support (poll) model with calibration

**Files:** Create `core/Politics/SupportModel.cs`; Test `core.tests/Politics/SupportModelTests.cs`.

**Interfaces:** Consumes: `PartyDef`, `VoterGroupDef`, `PolicyDef`, `Pcg32`, `DetMath`. Produces:
`public sealed record SupportInput(IReadOnlyDictionary<string,double> Satisfaction, ISet<string> GovernmentParties, IReadOnlyDictionary<string,double> Credibility /* default 1 */);`
`public sealed class SupportModel { public SupportModel(IReadOnlyList<PartyDef> parties, IReadOnlyList<VoterGroupDef> groups, IReadOnlyList<PolicyDef> policies, double kPos = 1.0, double kGov = 1.0, double tau = 0.5, double sigma = 0.05, double turnout = 0.65); public void Calibrate(IReadOnlyDictionary<string,double> targetShares, SupportInput start); public IReadOnlyDictionary<string,double> Compute(SupportInput input, Pcg32? noise); }`
Formula = spec section 3: `u(g,p) = offset_p + affinity + kPos*(1-d)*(0.5+0.5*credibility) + blame + noise`, `blame = +kGov*s_g` for government parties, `-0.5*kGov*s_g` otherwise; `d(g,p)` = weight-weighted mean of `|pref_g - target_p| / (max - min)` over policies in both `PolicyPrefs` and the party `Platform` (0.5 if none); noise = `sigma * (sum of 12 uniforms - 6)` (times 3 for `Swing` parties), drawn in group-then-party ordinal order only when `noise` is not null; softmax with temperature `tau`; support = `sum_g size_g*turnout*softmax`, normalised to sum 1. `Calibrate` runs 100 fixed-point iterations `offset_p += ln(target_p / model_p)` with no noise (targets are renormalised; a target of 0 is treated as 1e-6). The result dictionary is ordinal-sorted.

- [ ] **Step 1: Write failing tests**: `SharesSumToOne`; `HigherAffinityGivesHigherShare`; `PlatformCloserToAGroupRaisesTheShare`; `UnhappyVotersPunishTheGovernment` (satisfaction -1 lowers government parties' share vs +1); `CalibrationReproducesTargetShares` (within 1e-6, noise off); `SwingPartyHasLargerNoiseSpread` (variance over 300 draws at least 4x); `SameRngSameResult`; `ExtremeInputsStayFinite` (satisfaction +-1, targets with a 0, a single party -> share 1) - Review Focus 2; `NoNoiseWhenRngIsNull`.
- [ ] **Step 2-5:** run FAIL, implement, run PASS, commit `feat(core): party support model with calibration`.

### Task 4: Politics inside Game: polls, election, report

**Files:** Create `core/Politics/PoliticsState.cs`; Modify `core/Gameplay/{GameState,TurnReport,Game}.cs`; Test `core.tests/Politics/GamePoliticsTests.cs`.

**Interfaces:** Consumes: Tasks 1-3. Produces (namespace `OpenPolitics.Core.Politics`): `enum Phase { Governing }` (extended in M2b);
`record CountryPolitics(Phase Phase, int TurnsToElection, IReadOnlyDictionary<string,double> Support, IReadOnlyDictionary<string,int> Seats, IReadOnlyList<string> GovernmentParties, string? Leader, string PlayerParty)`;
`abstract record PoliticalEvent(int Turn)`; `sealed record ElectionHeld(int Turn, IReadOnlyDictionary<string,double> Votes, IReadOnlyDictionary<string,int> Seats) : PoliticalEvent(Turn)`.
`CountryState` gains optional last parameter `CountryPolitics? Politics = null`; `TurnReport` gains optional last parameter `IReadOnlyList<PoliticalEvent>? PoliticalEvents = null` (empty when null).
`Game`: when the scenario has `Politics`: at construction create `SupportModel` (calibrated to the scenario support with satisfaction 0 and the scenario government), the country's `IElectoralSystem`, initial seats = `Allocate(support)`, `TurnsToElection = scenario value`. In `EndTurn`, after the voter step and events: (1) `support = model.Compute(input, _rng)`; (2) `TurnsToElection--`; (3) if it reaches 0: votes = support + small election-day noise (sigma 0.01, from `_rng`), clamped at 0 and renormalised; `seats = system.Allocate(votes)`; record `ElectionHeld`; reset `TurnsToElection` to the country's `ElectionIntervalTurns`. State exposes `Politics`. M2a keeps the government unchanged after an election.

- [ ] **Step 1: Write failing tests** with a small fixture pack (extend `GameFixture` with 3 parties, an electoral system, country politics `election_interval_turns: 4`, scenario `base:test_politics` with `turns_to_election: 3`): `NoPoliticsWithoutAPoliticsBlock` (sandbox scenario: `Politics == null`, no events); `StartPollMatchesScenarioSupport` (within 1e-6 after calibration - state at turn 0); `SeatsAtStartSumToTotal`; `PollsSumToOneEveryTurn`; `ElectionHappensOnTheScheduledTurn` (event at report turn 3, then 7, 11) with `Seats` summing to 200 and `Votes` summing to 1; `PollsReactToUnhappyVoters` (after a bad policy run government parties lose share vs a calm run); `PoliticsIsDeterministicPerSeed`; `ScenarioWithUnknownPartyIsRejectedByValidatorNotByNewGame` - Review Focus 4; `OldScenariosAreUnchanged` (golden tests still pass).
- [ ] **Step 2-5:** run FAIL, implement, run full suite PASS, commit `feat(core): polls and elections inside Game`.

### Task 5: Czech content, text client and batch

**Files:** Create `data/base/parties/*.yaml`, `data/base/electoral_systems/czech_pr.yaml`; Modify `data/base/countries/czechia.yaml`, add `data/base/scenarios/cz_2015.yaml`, localization (cs, en); Modify `tools/OpenPolitics.Cli/Play/PlayLoop.cs`; Test `core.tests/Content/PoliticsContentTests.cs`, `core.tests/Play/PlayPoliticsTests.cs`.

**Interfaces:** Consumes: Tasks 1-4. Content (spec section 13): 8 fictional parties - `base:citizens_movement` (centrist-populist business, ~27%), `base:social_alliance` (15%), `base:civic_right` (12%), `base:peoples_union` (8%), `base:workers_left` (12%), `base:homeland_first` (7%), `base:green_wave` (6%), `base:open_net` (swing, ~5.5%, `swing: true`); start support sums to exactly 1 (the rest, about 7.5%, is split among them in the file so the sum is 1; no "others" party); each party has a platform of 3-6 policies, affinities to all 6 voter groups, 3-4 demands, 1-2 red lines, `price`, factions (used in M2d). Electoral system `base:czech_pr` (proportional, 200 seats, d'Hondt, threshold 0.05). Country politics: interval 16, majority fraction 0.5, max_formation_turns 4, caretaker_turns 2, program_slots 6. Scenario `base:cz_2015`: country `base:czechia`, `player_party: base:social_alliance`, `government: [base:social_alliance, base:peoples_union, base:citizens_movement]` with `leader: base:social_alliance` (so the starting coalition has a majority in seats), `turns_to_election: 12`. Localization: `name.<party>` and a `ui.heading.politics`, `ui.line.next_election`, `ui.politics.*` set (cs, en).
Client: the overview gets a POLITICS block when the country has politics: next election in N quarters, polls (top parties by share with a bar and percentage), seats of the current government; at an election the "what happened" block lists the result (parties with seats). `list`/`voters` unchanged.

- [ ] **Step 1: Write failing tests**: `BasePackWithPoliticsHasNoDiagnostics`; `EightPartiesStartingSupportSumsToOne`; `StartingGovernmentHasASeatMajority`; `SwingPartyIsNearTheThreshold`; `ThousandElectionsAreConsistent` (1000 seeds, 12 turns passive on `base:cz_2015`: every `ElectionHeld` has seats summing to 200, votes summing to 1, no party under the threshold has seats; deterministic for equal seeds) - Review Focus 5; `ExistingSandboxGoldensUnchanged` (full suite); play tests `OverviewShowsPollsAndNextElection` (scenario `base:cz_2015`, `PlayLoop` takes the scenario id already), `ElectionResultIsReportedInTheQuarterItHappens`.
- [ ] **Step 2-4:** run FAIL, write data and client change, run the full suite and `validate data/base` -> PASS.
- [ ] **Step 5: Tune** the starting affinities so the calibration converges to the stated shares and the polls drift only moderately (no party swings by more than 8 points over 12 quarters of passive play in 95% of 200 seeds; assert it in a test and record any retuning in `docs/decisions/0002-m2a-balance.md`).
- [ ] **Step 6: Commit** `feat(data): fictional Czech parties, proportional system and play-client politics view`.

---

## Self-review notes

- Spec coverage (M2a): parties and data format (section 2) -> T1; electorate/support (section 3) -> T3; electoral system (section 4) -> T2; election turn integration (section 9, step 7-8 partial) and API additions (section 10, state and events) -> T4; Czech content (section 13) -> T5.
- Deferred to later plans: formation, government falls, `CallEarlyElection` and every new player action (M2b-d); momentum and credibility inputs are accepted by `SupportModel` but stay at defaults (momentum is added in M2d).
- Known simplification: `turnout` is a constant 0.65 for all groups (spec allows a per-group value); revisit in M2d.
