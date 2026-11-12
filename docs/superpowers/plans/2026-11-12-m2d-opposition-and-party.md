# M2d Opposition and Party Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (native; the user chose no approval stops). Steps use checkbox (`- [ ]`) syntax.

**Goal:** The player's party has resources (political capital, organization), relations with other parties, a platform it can shift, factions that shape its cohesion, and actions to criticise the government, take stances on its measures, campaign, build the organisation and court future partners. Opposition becomes a playable role.

**Architecture:** `SupportModel` learns momentum, per-party platform updates and a turnout boost; `PoliticsEngine` owns a `PartyState` for the player's party (resources, relations, credibility, momentum, pressure, measures, value history) and exposes the new actions; the play client gets commands and a party panel; the politics batch gets an `Activist` strategy for the final gate.

**Tech Stack:** C# net10.0, xUnit.

**Spec:** `docs/specs/2026-11-12-m2-domestic-politics-design.md` section 8 (formulas below are from it).

## Global Constraints

- Earlier tests, goldens and gates stay green; politics keeps its own RNG stream; sandbox scenarios unchanged.
- Political capital (PC) income per quarter: `2 + 8 * organization * support_share + (2 if the player's party leads the government)`; start PC 6. Organization `o` in [0, 1], start 0.2, decays 0.01 per turn; `BuildOrganization` adds 0.04 (cost 4); organization raises the player's party's vote weight by `1 + 0.2 * o`.
- Costs: `Criticize` 2, `TakeStance` 1, `Campaign` `3 * effort` (effort 1..3), `BuildOrganization` 4, `Court` 3, `ShiftPlatform` 1. Not enough PC: `error.not_enough_capital`. Momentum and pressure decay with a half-life of 4 turns (factor `0.5^(1/4)` per turn).
- `Court(party)`: `relation(player, party) += 0.1` (clamped to [0, 1]); relations decay by 0.02 per turn toward the default 0.3. Relations feed coalition prices and the AI formateur's scoring (replacing the constant 0.3 for pairs that include the player's party).
- `ShiftPlatform(policy, target)`: the party's target moves by at most 10 % of the policy range per call; credibility falls by half the shift as a fraction of the range; credibility recovers 0.1 per year (max 1); the support model's platform term is scaled by `0.5 + 0.5 * credibility * cohesion`.
- `cohesion = 1 - sum_f weight_f * d(platform, faction_f)` where `d` is the weighted mean `|a - b| / range` over the policies the faction mentions (0 for a faction with none); clamp to [0, 1]; factions weights are normalised to sum 1; no factions: cohesion 1.
- Effective satisfaction for blame and loyalty is `s_g - pressure_g` (clamped to [-1, 1]); momentum `m_g` adds `+m_g` to the leaning of group `g` toward the player's party.
- `Criticize` and `TakeStance` need the player not to lead the government (`error.not_opposition`); the other party actions work in every role. One stance per measure (`error.already_taken`).
- Never push or create remote repos. Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. TDD: failing test first.

## Rulings made while planning

- Magnitudes the spec leaves open: `Criticize` on a value that got worse over the last 4 turns gives each caring group `momentum += 0.15 * |w_g| / max|w|` and `pressure += 0.10 * |w_g| / max|w|` (backlash `momentum -= 0.10 * ...` when it got better); "caring" = non-zero `value_weights` and "worse" is judged with the sign of that weight. `TakeStance`: a group approves a measure when its preferred setting is closer to the new value than to the old; supporting an approved or opposing a disapproved measure gives `momentum += 0.10 * share_g`, the reverse `-= 0.10 * share_g` (`share_g` = group size share, only groups with a stated preference for that policy). `Campaign` gives `momentum += 0.06 * effort` to the group (0.12 was too strong: see decision 0005) (doubled in the last 4 turns before an election).
- A measure is a policy change made by the AI governor; id `"<turn>:<policy id>"`.
- Mood of voters is the only thing the player's own policies change when leading; the opposition-only actions are refused for the leader.

## Review Focus

1. Not enough PC, effort outside 1..3, unknown group/party/policy/value, stance on an unknown or old measure: error keys, never exceptions (T2, T3).
2. Shifting the platform to its own current position, or repeatedly in one turn: no NaN, credibility stays in [0, 1] (T2).
3. Replaying a saved game with party actions reproduces PC, relations and platform (T2, T3).
4. Momentum and pressure never make support shares non-finite or outside the simplex (T1).
5. 1000 games of the `activist` strategy: no exception, no stuck phase, deterministic per seed (T5).

---

## File structure

```text
core/Politics/SupportModel.cs    modify: momentum, turnout boost, UpdateParty (platform changes)
core/Politics/PartyState.cs      create: PartyStatus, MeasureInfo, party resources/relations helper math (Cohesion, decay)
core/Politics/PoliticsState.cs   modify: CountryPolitics trailing optional `PartyStatus? Party`
core/Politics/PoliticsEngine.cs  modify: party state, new actions, income, decay, relations in formation
core/Gameplay/PlayerAction.cs    modify: Criticize, TakeStance (Stance enum), Campaign, BuildOrganization, Court, ShiftPlatform
core/Gameplay/SaveData.cs        modify: serialize the new actions
core/Gameplay/Game.cs            modify: route actions, feed measures and value history
core/Batch/PoliticsBatch.cs      modify: Activist strategy and share metrics
tools/OpenPolitics.Cli/Play/PlayLoop.cs  modify: commands and party panel
data/base/localization/{cs,en}.yaml      modify
core.tests/Politics/{SupportMomentumTests,PartyStateTests,PartyActionTests,OppositionTests}.cs
core.tests/Play/PlayPartyTests.cs
docs/decisions/0005-m2d-opposition.md
```

### Task 1: Support model: momentum, turnout boost, platform updates

**Files:** Modify `core/Politics/SupportModel.cs`; Test `core.tests/Politics/SupportMomentumTests.cs`.

**Interfaces:** Produces: `SupportInput` gains trailing optional `IReadOnlyDictionary<string,double>? Momentum = null, string? MomentumParty = null, IReadOnlyDictionary<string,double>? TurnoutBoost = null` (momentum per group for `MomentumParty`; boost multiplies that party's support weight, default 1); `SupportModel.UpdateParty(PartyDef party)` replaces the party's platform and recomputes its distances (unknown party id: `ArgumentException`).

- [ ] **Step 1: Write failing tests**: `MomentumRaisesTheShareOfItsPartyOnly` (positive momentum for one group raises that party, others fall, sum 1), `NegativeMomentumLowersIt`, `TurnoutBoostRaisesTheShare` (boost 1.2 > 1.0 share), `NoMomentumEqualsOldBehaviour` (`Compute` without the new fields is bit-identical to before: compare with a fresh model), `UpdatePartyChangesDistances` (moving a party's platform toward a group raises its share), `UpdateUnknownPartyThrows`, `ExtremeMomentumStaysFinite` (momentum +-5 and boost 0..3, shares finite, sum 1) - Review Focus 4.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): support model momentum, turnout boost and platform updates`.

### Task 2: Party state, resources, relations and party actions

**Files:** Create `core/Politics/PartyState.cs`; Modify `core/Politics/PoliticsState.cs`, `core/Politics/PoliticsEngine.cs`, `core/Gameplay/{PlayerAction,SaveData,Game}.cs`; Test `core.tests/Politics/PartyStateTests.cs`, `core.tests/Politics/PartyActionTests.cs`.

**Interfaces:** Produces: `public sealed record MeasureInfo(string Id, int Turn, string PolicyId, double From, double To, bool StanceTaken);` `public sealed record PartyStatus(double PoliticalCapital, double Organization, double Credibility, double Cohesion, IReadOnlyDictionary<string,double> Relations, IReadOnlyDictionary<string,double> Momentum, IReadOnlyDictionary<string,double> Pressure, IReadOnlyList<MeasureInfo> Measures);` `CountryPolitics` gains trailing optional `PartyStatus? Party = null`. `public static class PartyMath { public static double Cohesion(PartyDef party, IReadOnlyDictionary<string,double> range) }` (formula in Global Constraints). Actions (namespace `OpenPolitics.Core.Gameplay`): `BuildOrganization`, `Court(string PartyId)`, `ShiftPlatform(string PolicyId, double Target)`, `Campaign(string GroupId, int Effort)`; errors `error.not_enough_capital`, `error.unknown_party`, `error.unknown_group`, `error.unknown_policy`, `error.bad_effort`, `error.not_in_platform` (policy not in the player's platform: shifting adds it? No: it is refused).
Engine: PC income and organization decay and relation decay and credibility recovery and momentum/pressure decay each `Step`; support input uses effective satisfaction, momentum, credibility times cohesion, turnout boost; the formation relation function returns the stored relation for pairs including the player's party; the player's `PartyDef` in the engine is replaced by a copy with the shifted platform (AI formateurs and the support model use it).

- [ ] **Step 1: Write failing tests**: `StartingResources` (PC 6, organization 0.2, credibility 1), `IncomeFollowsTheFormula` (leader bonus, organization and share), `OrganizationDecaysAndBuildingAddsFourHundredths`, `CourtRaisesRelationAndItDecaysTowardDefault`, `CourtedPartyIsCheaperInCoalitionTalks` (price test with relation 1.0 vs 0.3 through a `ProposeCoalition`), `ShiftPlatformMovesAtMostTenPercentOfTheRange` and `ShiftToCurrentPositionDoesNothingBad` (Review Focus 2), `ShiftLowersCredibilityAndItRecovers`, `CohesionFromFactions` (pure maths: identical platform = 1, far = lower, no factions = 1), `CampaignGivesMomentumDoubledBeforeAnElection`, `MomentumDecaysWithAHalfLifeOfFourTurns`, `NotEnoughCapitalRefusesWithoutSpending`, `BadArgumentsAreErrorsNotExceptions` (Review Focus 1), `SaveAndLoadReplayPartyActions` (Review Focus 3), `PartyStatusIsExposedInTheSnapshot`.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): party resources, relations and party actions`.

### Task 3: Opposition actions: Criticize and TakeStance

**Files:** Modify `core/Gameplay/{PlayerAction,SaveData,Game}.cs`, `core/Politics/PoliticsEngine.cs`; Test `core.tests/Politics/OppositionTests.cs`.

**Interfaces:** Produces `public enum Stance { Support, Oppose }`, `Criticize(string ValueId)`, `TakeStance(string MeasureId, Stance Stance)`; errors `error.not_opposition`, `error.unknown_value`, `error.unknown_measure`, `error.already_taken`, `error.not_enough_capital`. The engine keeps the last 5 value snapshots (values for every value id, recorded each `Step`) and a list of measures (every `GovernmentMeasure` of the AI governor becomes `MeasureInfo` with id `"<turn>:<policy>"`).

- [ ] **Step 1: Write failing tests**: `CriticizingAWorseningValueGivesMomentumAndPressure`, `CriticizingAnImprovingValueBacklashes`, `OnlyCaringGroupsReact`, `PressureLowersTheGovernmentPartiesPoll` (same seed, with and without criticism over several turns: the governing parties' share is lower), `PressureMakesPartnersLessLoyal` (loyalty after criticism is lower), `LeaderCannotCriticize`, `StanceOnAnApprovedMeasure` (supporting gives momentum, opposing costs it), `OneStancePerMeasure`, `UnknownValueOrMeasureIsAnError`, `MeasuresAreRecordedFromTheGovernor`, `SaveAndLoadReplayOppositionActions`.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite PASS. **Step 5: Commit** `feat(core): criticise and stance actions for the opposition`.

### Task 4: Client commands and party panel

**Files:** Modify `tools/OpenPolitics.Cli/Play/PlayLoop.cs`, `data/base/localization/{cs,en}.yaml`; Test `core.tests/Play/PlayPartyTests.cs`.

Commands: `party` (panel: PC, organization, credibility, cohesion, relations, factions with their distance, measures with numbers, the three opposition-goal bars: poll standing, coalition fragility (lowest partner loyalty), relations to the next likely partner), `criticize VALUE`, `stance N support|oppose` (N = measure number from the panel), `campaign GROUP [1-3]`, `organize`, `court PARTY`, `platform POLICY TARGET`. Each prints a result line or the error text. The overview shows PC and organization in the POLITICS block. Texts in cs and en.

- [ ] **Step 1: Write failing tests** (pack = base + in-memory scenario as in `PlayFormationTests`): `PartyPanelShowsResourcesAndMeasures`, `CampaignCourtOrganizeAndPlatformWork`, `CriticizeIsRefusedForTheLeaderAndWorksInOpposition`, `StanceOnAMeasure`, `NotEnoughCapitalIsExplained`, `PartyTextsExistInCzech`.
- [ ] **Step 2-4:** run FAIL, implement, run the full suite and `validate data/base` PASS. **Step 5: Commit** `feat: party commands in the play client`.

### Task 5: Activist strategy and the M2 gate

**Files:** Modify `core/Batch/PoliticsBatch.cs`, `tools/OpenPolitics.Cli/Commands/BatchCommand.cs`; Create `docs/decisions/0005-m2d-opposition.md`; Test `core.tests/Batch/PoliticsBatchTests.cs`.

`PoliticsStrategy.Activist` (CLI `activist`): every turn, with the available PC: `Criticize` the worst value when not leader, `Campaign` the largest group, `BuildOrganization` when organization < 0.5, `Court` the party with the lowest relation among the seated ones, plus the formation answers. `PoliticsBatchReport` gains `MeanPlayerSupportAtEnd` and `MeanPlayerSeatsAtEnd` (average share and seats of the player's party at the end of each game).
Gate: over 500 games x 48 turns, `Activist` raises the player party's mean end support over `Ignorer` by at least 1 point; no stuck games; all of M2b/M2c gates still hold. Tune magnitudes of the actions (not the gates) until the gate holds, record numbers in the decision note.

- [ ] **Step 1: Write failing tests**: `ActivistNeverThrowsAndIsDeterministic` (Review Focus 5), `ActivismPaysOff` (>= +1 point support vs ignorer), `ActivistSpendsCapitalAndDoesNotOverspend` (PC never negative), `CliAcceptsTheActivistStrategy`.
- [ ] **Step 2-4:** run FAIL, implement, tune, run the full suite and `batch --politics --strategy activist` smoke run PASS. **Step 5:** write the decision note. **Step 6: Commit** `feat: activist strategy and the M2d gate`.

---

## Self-review notes

- Spec coverage (section 8): resources and income (T2), all six actions (T2, T3), momentum/pressure/cohesion/credibility and platform shifting (T1, T2, T3), factions as cohesion (T2), opposition goals as three bars (T4), government measures for stances (T3 from the M2c governor), `relation` feeding prices (T2).
- Deferred: faction splits and new parties (spec section 12.6), per-group turnout, plurality system (M4).
