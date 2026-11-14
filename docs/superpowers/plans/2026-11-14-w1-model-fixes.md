# Wave 1 – Model Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Remove the playtest-visible modelling bugs: sudden poll collapse on entering government, free trust reset when re-forming a coalition, real-only economy without prices, no player control over indexation, and no lasting effect of GDP level.

**Architecture:** Small, local changes to `SupportModel` (attribution), `PoliticsEngine` (attribution state, grudge, start loyalty), `CoalitionRules` (grudge refusal/price), `MacroModelV1` (price level, derived values, indexation), `VoterModel` (habit-based level effects), plus data and a new `SetIndexation` action. Everything stays deterministic; new settings default to today's behaviour.

**Tech Stack:** C# / .NET 10, xUnit, YamlDotNet, YAML packs.

**Spec:** `docs/specs/2026-11-14-playtest-round-design.md` (wave 1, sections 1.1–1.4).

## Global Constraints

- Core stays independent of Godot; packs hold data only; every new user-visible text gets cs + en keys.
- Determinism: own `Pcg32`, `DetMath`, ordinal iteration; politics keeps its own RNG stream `new Pcg32(seed, 99)`; no new draws are added in this wave.
- Old scenarios/saves behave as before unless the new setting is used (indexation default `gdp`).
- No value gets a fixed ceiling; `batch` stability and "no ceiling" gates keep passing.
- Never push. Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Test command for the whole suite: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1; dotnet test OpenPolitics.slnx`.

## Review Focus

- A party out of government for years then re-entering: attribution must not exceed [0,1] or oscillate.
- Election right after a government change: polls must not jump (attribution is continuous across the election).
- Saves/replays from before this wave must still load (new action types only added, none changed).
- Inflation at 0 or negative: price level stays positive and finite; `none`-indexed amounts do not explode.
- Habit with value ≤ 0 (e.g. a value that crosses zero): `ln` guard, no NaN.

## Task 1: Gradual attribution of blame

**Files:**
- Modify: `core/Politics/SupportModel.cs`, `core/Politics/PoliticsEngine.cs`
- Test: `core.tests/Politics/AttributionTests.cs` (create)

**Interfaces:**
- Consumes: `SupportInput` (existing record).
- Produces: `SupportInput` gains last optional parameter `IReadOnlyDictionary<string,double>? Attribution = null` (null ⇒ 1 for governing parties, 0 for others, i.e. old behaviour). `PoliticsEngine` keeps `_attribution` (sorted dictionary per party id, [0,1]) and exposes it in `PartyStatus`? No — internal only.

- [ ] **Step 1: Write failing tests** in `AttributionTests.cs`:
  - `BlameFormulaMatchesOldAtExtremes`: `SupportModel.Compute` with `Attribution` null equals result with `Attribution` = 1 for gov and 0 for others (use the existing Fixture helper style from `SupportModelTests`).
  - `HalfAttributionIsBetween`: party with attribution 0.5 under satisfaction −0.5 has share strictly between its share at 0 and at 1.
  - `EnteringGovernmentDoesNotCollapsePoll` (full game, `cz_2015` fixture used by `GamePoliticsTests`): force the player's party into government through the existing formation helper, then assert its poll after 2 quarters ≥ 0.75 × its poll before. Use `Formation` fixture from `core.tests/Politics/FormationFixture.cs`.
- [ ] **Step 2: Run** `dotnet test --filter AttributionTests` → Expected: FAIL (compile error for missing `Attribution`, then assertion).
- [ ] **Step 3: Implement.** In `SupportModel.Shares`: `a = input.Attribution?.GetValueOrDefault(party.Id, gov?1:0) ?? (gov?1:0)`; `blame = _kGov * sat * (a - 0.5 * (1 - a))`. In `PoliticsEngine`: field `SortedDictionary<string,double> _attribution`; `AttributionTarget(id)` = 1.0 leader, 0.7 other government party, else 0.0; initialise to targets before `Calibrate`; in `TickParty` move each toward target by `a += (target - a) * (1 - HalfLifeFactor)`; pass `new Dictionary<string,double>(_attribution)` in `Input(...)`.
- [ ] **Step 4: Run** `dotnet test --filter AttributionTests` → PASS; then whole suite; fix any calibration-sensitive test by reading its failure (do not loosen assertions without a ledger ruling).
- [ ] **Step 5: Commit** `feat(politics): gradual attribution of blame`.

## Task 2: Grudge and start loyalty memory

**Files:**
- Modify: `core/Politics/CoalitionRules.cs`, `core/Politics/PoliticsEngine.cs`, `core/Politics/CoalitionBuilder.cs`
- Test: `core.tests/Politics/GrudgeTests.cs` (create)

**Interfaces:**
- Consumes: `CoalitionRules.Evaluate(CoalitionProposal, parties, seats, fraction, programSlots, Func<string,string,double> relation)`.
- Produces: `Evaluate` gains optional last parameter `Func<string,string,double>? grudge = null` (`grudge(a, b)` = how much `a` resents `b`, 0 if null). New refusal reason `"grudge"` when any member's grudge toward another member is ≥ 0.5 (`Refusal("grudge", partyId, null)`). `RequiredWeight` gains optional `double grudgeToFormateur = 0` and returns `Price * (1 - 0.5*relation) * (1 + grudgeToFormateur)`. `CoalitionBuilder.Build` gets the same optional `grudge` parameter and skips partners that would be refused for grudge. `LoyaltyModel.StartLoyalty(double start, double relation, double grudge)` = `clamp(start + 20*(relation-0.3)/0.7 - 30*grudge, 20, 85)`.

- [ ] **Step 1: Write failing tests** in `GrudgeTests.cs`:
  - `LoyaltyModelStartLoyalty`: (70, 0.3, 0) → 70; (70, 1.0, 0) → 85 (clamped from 90); (70, 0.3, 1.0) → 40; (70, 0, 3) → 20.
  - `GrudgeAboveHalfRefuses` and `GrudgeRaisesPrice` on `CoalitionRules.Evaluate` with two-party fixtures.
  - `ReformingSameCoalitionAfterDepartureIsHarder` on a full game: run a game (fixture from `FallTests`) until a partner leaves; then the identical coalition either is refused with `error.refused.grudge` or forms with that partner's start loyalty strictly below `LoyaltyParams.Start`. Assert both branches are covered by the data in this test by choosing the player as former leader and proposing the same partners.
  - `GrudgeDecays`: after 16 quiet turns the grudge is ≤ 25 % of its initial value (read via a new internal accessor `PoliticsEngine.GrudgeOf(a, b)` exposed to tests with `InternalsVisibleTo`, already enabled for the test project).
- [ ] **Step 2: Run** `dotnet test --filter GrudgeTests` → FAIL (missing members).
- [ ] **Step 3: Implement.** `PoliticsEngine`: `_grudge` = `SortedDictionary<string,double>` keyed `"a|b"`; `Resent(a,b)`; in `Depart` add 0.5 to `partner|leader`, 0.3 to `leader|partner`, plus 0.1 to `partner|leader` per commitment of the partner still unmet (`CommitmentMath.Progress < Expected`); on `GovernmentFell` add 0.2 to every former partner → leader; decay by half-life 8 turns in `TickParty`; pass `Resent` to `Evaluate` and `Build`; `ResetLoyalty` uses `LoyaltyModel.StartLoyalty(Start, Relation(leader, p), Resent(p, leader))`.
- [ ] **Step 4: Run** `--filter GrudgeTests` → PASS, then whole suite (existing `FormationGateTests`/`Fall*` may shift; investigate each, ledger any ruling).
- [ ] **Step 5: Commit** `feat(politics): grudge memory, start loyalty depends on relations`.

## Task 3: Price level and derived macro values

**Files:**
- Modify: `core/Economy/MacroModelV1.cs`, `data/base/values/macro.yaml`, `data/base/localization/{cs,en}.yaml`
- Test: `core.tests/Economy/PriceLevelTests.cs` (create)

**Interfaces:**
- Produces: new owned values `base:price_level` (index, initial 100), `base:gdp_nominal`, `base:real_growth` (annual %, computed), `base:deficit_pct` (total deficit as % of nominal GDP). `MacroModelV1.Ids` gets constants `PriceLevel`, `GdpNominal`, `RealGrowth`, `DeficitPct`.

- [ ] **Step 1: Write failing tests**: `PriceLevelGrowsWithInflation` (step 4 turns at inflation 4 % ⇒ index ≈ 104 within 0.2), `NominalGdpIsRealTimesPriceLevel`, `RealGrowthMatchesGdpChange`, `DeficitPctMatchesBudget` (revenue 1.84, spending 1.771 + interest ⇒ sign/magnitude), `PriceLevelStaysPositiveAtNegativeInflation` (pi = −20 for 40 turns).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** in `MacroModelV1.Step`: `price2 = Math.Max(price*(1+dt*pi2/100), 1e-6)`; `gdp_nominal = y2*price2/100`; `real_growth = ((y2/yOld)-1)/dt*100`; `deficit_pct = deficitPct` (already computed). Add to `OwnedValueIds`; add value defs with initial/reference/scale; cs/en names for value keys (follow existing `value.base:*` key pattern in the localization files).
- [ ] **Step 4: Run** filter → PASS; whole suite; `validate data/base` OK.
- [ ] **Step 5: Commit** `feat(economy): price level, nominal GDP, real growth, deficit percent`.

## Task 4: Indexation of spending

**Files:**
- Modify: `core/Economy/IEconomyModel.cs` (`EconomyInput`), `core/Economy/MacroModelV1.cs`, `core/Gameplay/Game.cs`, `core/Gameplay/PlayerAction.cs`, `core/Gameplay/SaveData.cs`, `tools/OpenPolitics.Cli/Play/PlayLoop.cs`, `data/base/localization/{cs,en}.yaml`
- Test: `core.tests/Economy/IndexationTests.cs` (create)

**Interfaces:**
- Produces: `EconomyInput` gains last optional parameter `IReadOnlyDictionary<string,string>? Indexation = null`. `public sealed record SetIndexation(string PolicyId, string Mode) : PlayerAction` (modes `"gdp" | "inflation" | "none"`). `Game.Indexation` read-only property (`IReadOnlyDictionary<string,string>`) and `CountryState` is unchanged (the CLI/client read it from `Game`). Saved/replayed through the action log. Unknown policy → `error.unknown_policy`; mode not in the three → `error.bad_indexation`; policy without `CostPerUnit` → `error.not_indexable`.
- Semantics for spending policies in `MacroModelV1`: `gdp` → `pv*cost/100*y2` (today); `inflation` → `pv*cost/100*gdpRef` where `gdpRef` is `Parameters["gdp_reference"]` if present else the real GDP at the policy's last mode switch (kept by `Game`, passed in `Indexation` as `"inflation@<value>"` is NOT used — instead `EconomyInput` also gets `IReadOnlyDictionary<string,double>? RealAmountBase`: for each non-`gdp` policy, the real-GDP level at switch time); `none` → `pv*cost/100*gdpBase*100/price_level`.

- [ ] **Step 1: Write failing tests**: `GdpModeIsDefaultAndUnchanged` (existing economy golden output equal with no indexation), `InflationModeKeepsRealAmount` (switch pensions to `inflation`; after 8 quarters of 8 % inflation and 0 % growth, spending/real GDP ratio unchanged), `NoneModeErodes` (same scenario: `none` ratio falls by roughly the price-level factor), `SetIndexationValidation` (three error keys), `IndexationSurvivesSaveReplay` (save, replay, equal state).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** as in Interfaces. `Game` keeps `_indexation` and `_indexBase` (real GDP at switch); `Apply(SetIndexation)` sets both immediately and logs; `SaveData` serializes the new action type with the existing polymorphic scheme (follow how `SetPolicy`/`PoliticsAction` types are mapped). CLI: `index <policy> gdp|inflation|none` command with help text and cs/en strings.
- [ ] **Step 4: Run** filter → PASS; whole suite; `batch --games 200 --strategy random` (existing gate command) still OK.
- [ ] **Step 5: Commit** `feat(economy): indexation of spending (gdp, inflation, none)`.

## Task 5: Level effects in voter satisfaction

**Files:**
- Modify: `core/Packs/Definitions.cs` (`VoterGroupDef` gets optional `LevelWeights`), `core/Packs/PackReader.cs`, `core/Packs/PackValidator.cs`, `core/Voters/VoterModel.cs`, `core/Gameplay/Game.cs`, `data/base/voter_groups/groups.yaml`
- Test: `core.tests/Voters/LevelEffectTests.cs` (create)

**Interfaces:**
- Produces: `VoterGroupDef.LevelWeights` (`IReadOnlyDictionary<string,double>`, default empty, YAML key `level_weights`, validator: ids must be values). `VoterModel.Step(satisfaction, valueChangePerYear, policyParams, dt, IReadOnlyDictionary<string,double>? values = null)`; the model keeps `_habit` (value id → moving average, half-life 3 years, initialised from the first `values` it sees) and adds `w * ln(max(value,eps)/max(habit,eps))` per year to ds/dt.

- [ ] **Step 1: Write failing tests**: `ConstantValueHasNoLevelEffect`, `SustainedGrowthRaisesSatisfaction` (20 years at 3 % vs 0 % in the pure model: difference ≥ 0.15 with w = 1), `HabitIsGuardedAgainstNonPositiveValues` (value 0 and negative ⇒ finite), validator test for unknown value id in `level_weights`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement**; parse `level_weights` in `PackReader` like `value_weights`; `Game.EndTurn` passes `after` to `_voters.Step`. In `groups.yaml` add `level_weights: {base:gdp: 1.0}` to the income/worker/business groups and `{base:health: 1.0}` where health already has a weight (read the file; keep the numbers small).
- [ ] **Step 4: Run** filter → PASS; whole suite; run `batch --politics` gates (ignorer, keeper, activist) and compare with `docs/decisions/0005-m2d-opposition.md`; record the new numbers.
- [ ] **Step 5: Commit** `feat(voters): level effects via habit`.

## Task 6: Gates, documentation, client touch-ups

**Files:**
- Create: `docs/decisions/0006-w1-model-fixes.md`
- Modify: `README.md` (CLI command list), `client-godot/*` only if it fails to build/smoke
- Test: `core.tests/Politics/ReviewFixWave1Tests.cs` only if the final review needs it

- [ ] **Step 1:** Run the whole suite, `validate data/base`, `batch --politics` for all five strategies (`ignorer|keeper|random|extreme|activist`, 200 games each), and `scripts/client-smoke.sh` with `GODOT` set to the installed 4.7.2 binary. Expected: all green, no NaN/Inf, falls still end with a new majority.
- [ ] **Step 2:** Write `0006-w1-model-fixes.md`: what changed, the parameter values chosen, batch numbers before/after.
- [ ] **Step 3:** Commit `docs: wave 1 decision record`.
