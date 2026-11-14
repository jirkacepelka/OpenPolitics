# Wave 2 – Government Play Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give the player real agency inside and against a coalition: propose policies as a partner, leave or dismiss, call no-confidence, face mid-term demands, and be rewarded for a happy coalition, while the AI leader governs more visibly.

**Architecture:** New `PoliticsAction`s executed by `PoliticsEngine`, new `PoliticalEvent`s rendered by `Texts.Event` (shared by CLI and Godot), a richer `Governor`, mid-term demands built on the existing commitment/loyalty machinery, and data additions (demands, voter weights). Politics keeps its own RNG stream.

**Tech Stack:** C# / .NET 10, xUnit, YAML packs, Godot 4.7.2 .NET client.

**Spec:** `docs/specs/2026-11-14-playtest-round-design.md` (wave 2, sections 2.1–2.5). Wave 1 is done (see `docs/decisions/0006-w1-model-fixes.md`).

## Global Constraints

- Core independent of Godot; packs hold data only; every new text has cs + en keys; deterministic (Pcg32 stream `new Pcg32(seed, 99)` for politics only, ordinal iteration).
- Existing saves/scenarios still load: only new action types are added to `SaveData`.
- Never push. Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Suite: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1; dotnet test core.tests 2>&1 | grep -E "error|Failed |Passed!|Failed!"` — read the final `Passed!/Failed!` line (a pipe hides the exit code).
- Never `pkill -f` anything that matches the shell command line; do not kill the user's Godot window.

## Review Focus

- A player who is the leader, partner and opposition in turn: each new action must refuse cleanly in the wrong role (`error.not_leader`, `error.not_partner`, `error.not_opposition`).
- Dismissing/leaving in the same turn as the AI loses a partner: one fall, one `FormationStarted`.
- Mid-term demand for a demand already agreed or already satisfied: must not stack or fire.
- Governor never loops on an unreachable target; never moves a policy outside its range.
- Loyalty/PC bonus when the government has no partners (single-party majority): no division by zero.

## Task 1: ProposePolicy (partner influence)

**Files:** Modify `core/Gameplay/PlayerAction.cs`, `core/Gameplay/SaveData.cs`, `core/Gameplay/Game.cs`, `core/Politics/PoliticsEngine.cs`, `core/Politics/PoliticsState.cs`, `core/Presentation/Texts.cs`, localization cs/en. Test: `core.tests/Politics/ProposePolicyTests.cs`.

**Interfaces:**
- Produces: `public sealed record ProposePolicy(string PolicyId, double Target) : PoliticsAction`; events `PolicyProposalAccepted(int Turn, string PolicyId, double From, double To)` and `PolicyProposalRejected(int Turn, string PolicyId, string Reason)` (Reason `score` or `red_line`). `PoliticsEngine.LeaderDecides(...)` internal. Errors: `error.not_partner` (player not a non-leader government member, or phase not Governing), `error.unknown_policy`, `error.value_out_of_range`, `error.not_enough_capital` (cost 3 PC).
- Game integration: the decision is evaluated in the same `EndTurn` after commit; an accepted proposal queues a `PendingPolicy` exactly like a governor move and emits `GovernmentMeasure` (so the opposition can take stances).

- [ ] **Step 1: Tests (RED)** using `FormationFixture`: partner player (government `a,b`, leader `a`, player `b`): refused for leader/opposition role; capital 3 spent; with relation raised by `Court` and a target close to the leader's platform the proposal is accepted next turn (event + policy changes after delay, by at most 25 % of range); a target that crosses a third partner's red line is rejected with reason `red_line`; a far-fetched target with poor relation is rejected with reason `score`; relation changes +0.05 / −0.03; replay from save gives the same state.
- [ ] **Step 2: Run** → FAIL (compile).
- [ ] **Step 3: Implement.** Score `0.5*relation(leader,player) + 0.3*seatShare(player in government) + 0.2*fit − 0.3*(redLineHit ? 1 : 0)`, `fit = 1 − min(1, |target − leaderTarget| / range)` where `leaderTarget` is the leader's platform target for the policy (0.5 fit when the leader has none). Accept when `score >= 0.35` and no red-line hit; the new value is `current + clamp(target − current, ±0.25*range)`. Proposals are stored in `_proposals` and decided in `Step` after loyalty; results appended to the returned events; the engine exposes accepted moves to `Game` via a new `List<(string PolicyId,double Value)> TakeApprovedMoves()` that `Game.EndTurn` converts to pending policies and `GovernmentMeasure` events.
- [ ] **Step 4: Run** filter then suite → PASS.
- [ ] **Step 5: Commit** `feat(politics): partners propose policies, the leader decides`.

## Task 2: Richer governor

**Files:** Modify `core/Politics/Governor.cs`, `core/Politics/PoliticsEngine.cs` (`GovernorMove`). Test: `core.tests/Politics/GovernorTests.cs` (extend).

**Interfaces:**
- Produces: `Governor.Move(Government g, PartyDef? leader, Func<string,double> policy, Func<string,double> value, IReadOnlyDictionary<string,PolicyDef> policies, IReadOnlyList<RedLineDef> partnerRedLines, int turn)` — existing candidates (unmet commitments) first; then, every second turn, crisis moves (deficit above 5 % of GDP → lower the policy with the largest `value*CostPerUnit` by 10 % of its range; unemployment ≥ 8 → raise the spending policy with the most headroom below the leader's platform target, by 10 % of its range); then leader platform drift (candidates `0.5*weight*gap/range`, only gaps above 5 % of range, every second turn). A candidate that would newly violate any `partnerRedLines` entry is skipped; if nothing else remains it is allowed (the partner takes the red-line shock).

- [ ] **Step 1: Tests (RED):** commitments still come first; with no commitments and leader platform far from current, the governor moves toward the leader target by ≥ 2 % of range every second turn; deficit crisis lowers spending; unemployment crisis raises it; a move that would cross a partner's red line is skipped when an alternative exists and taken when it does not; value stays inside `[Min, Max]`; no move when everything is aligned.
- [ ] **Step 2–4:** run (FAIL), implement, run filter and suite (existing governor tests keep passing; the `Move` overload used by the engine passes the new arguments).
- [ ] **Step 5: Commit** `feat(politics): governor follows the leader's platform and reacts to crises`.

## Task 3: Leave, dismiss, no confidence

**Files:** Modify `core/Gameplay/PlayerAction.cs`, `SaveData.cs`, `PoliticsEngine.cs`, `PoliticsState.cs`, `Texts.cs`, localization, `tools/OpenPolitics.Cli/Play/PlayLoop.cs`. Test: `core.tests/Politics/CoalitionExitTests.cs`.

**Interfaces:**
- Produces: `LeaveCoalition`, `DismissPartner(string PartyId)`, `NoConfidence` (all `PoliticsAction`); events `PlayerLeftCoalition(int Turn)`, `PartnerDismissed(int Turn, string PartyId)`, `NoConfidencePassed(int Turn)`, `NoConfidenceFailed(int Turn)`. Errors `error.not_partner`, `error.not_leader`, `error.not_opposition`, `error.would_lose_majority` (dismissing a partner whose absence costs the majority), `error.unknown_party`, `error.not_enough_capital` (4 PC dismiss, 10 PC no confidence). Phase must be `Governing` (`error.wrong_phase`).
- Rules: Leave: the player's party exits at the end of the turn through the existing `Depart` path (grudge, `PartnerLeft` event), credibility −0.15, relation with the leader −0.3; a fall follows when the majority is gone. Dismiss: partner leaves through `Depart`, grudge +0.5 on top. NoConfidence: passes when seats of willing opposition parties (relation with the player ≥ 0.4, player included) plus seats of partners with loyalty < 40 exceed the majority fraction of all seats; on pass → `GovernmentFell` with `BeginFormation(first: largest willing opposition party)`; on fail credibility −0.2.

- [ ] **Step 1: Tests (RED):** each action in the wrong role is refused; leaving without majority loss keeps the government, with loss causes exactly one `GovernmentFell` and one `FormationStarted`; dismissing is refused when the majority would be lost; no confidence fails with default relations; after `Court` of all parties ≥ 0.4 and two unhappy partners (loyalty < 40) it passes; save/replay equality; capital costs.
- [ ] **Step 2–4:** run (FAIL), implement, run filter and suite.
- [ ] **Step 5: Commit** `feat(politics): leave, dismiss partners, vote of no confidence`.

## Task 4: Mid-term demands and new demand kinds

**Files:** Modify `core/Politics/Government.cs` (`Commitment` gets `int StartTurn = -1`), `CommitmentMath.cs`, `PoliticsEngine.cs`, `PoliticsState.cs`, `Texts.cs`, `data/base/parties/parties.yaml`, `data/base/voter_groups/groups.yaml`, localization. Test: `core.tests/Politics/MidTermDemandTests.cs`, `core.tests/Content/PoliticsContentTests.cs` (extend).

**Interfaces:**
- Produces: events `DemandRaised(int Turn, string PartyId, string DemandId, int DueTurns)`, `DemandMet(int Turn, string PartyId, string DemandId)`, `DemandFailed(int Turn, string PartyId, string DemandId)`. `Commitment.StartTurn` (−1 ⇒ government's formed turn); `CommitmentMath.Expected` uses it. Engine state: `_nextDemand[partner]` countdown (initial `6 + rng.NextInt(5)`, afterwards `8 + rng.NextInt(5)`), `_midterm[partner] = (DemandId, DueTurn)`.
- Rules: when a partner's countdown reaches 0 and its poll is below its last election result and it has no open mid-term demand, it raises a demand from its catalog that is not already a commitment and not yet satisfied (first by `weight` desc, ordinal id), with `DueTurns = 6`; the demand becomes a commitment with `StartTurn = turn`. At the due turn: delivered (`Progress ≥ 1`) → loyalty +10 and `DemandMet`; otherwise loyalty −25 (floored at 0) and `DemandFailed`. If the poll is not below the result, the countdown restarts at 2.
- Data: each party gets 1–2 additional demands of kind `value` on `base:deficit_pct` (`<=` 0, balanced budget), `base:inflation` (`<=` 3), `base:debt` is not used; `base:unemployment` (`<=` 5.5) for left parties; `due_turns` 12–16. Groups: `base:deficit_pct: -0.1` value weight for `middle_class` and `high_income`, so the opposition can criticise it.

- [ ] **Step 1: Tests (RED):** a partner whose poll is below its result raises exactly one demand within 12 turns and never two at once; a delivered demand gives `DemandMet` and loyalty goes up by about 10; an ignored one gives `DemandFailed` and −25; a satisfied or already agreed demand is never raised; the content test checks every new demand's value id exists and demands are well-formed; `Criticize` accepts `base:deficit_pct` when the deficit moved.
- [ ] **Step 2–4:** run (FAIL), implement, run filter and suite (batch politics gate numbers will move: record them in Task 7).
- [ ] **Step 5: Commit** `feat(politics): mid-term demands, balanced-budget and inflation demands`.

## Task 5: Happy coalition earns capital

**Files:** Modify `core/Politics/PoliticsEngine.cs` (`TickParty`). Test: `core.tests/Politics/CoalitionCapitalTests.cs`.

**Interfaces:** Produces `PoliticsEngine.CoalitionCapitalBonus()` (internal): leader → `3 * clamp((meanPartnerLoyalty − 50)/50, −1, 1)` (0 with no partners); partner → `3 * clamp((relation(player, leader) − 0.3)/0.7, −1, 1)`; opposition → 0. `_pc` stays within `[0, MaxCapital]` after adding.

- [ ] **Step 1: Tests (RED):** leader with loyal partners earns more per turn than with unhappy partners (compare PC gain over the same turn count); leader without partners gets no bonus and no exception; unhappy coalition income can be below the base 2+2 but PC never goes negative; partner with good relation earns more than with bad relation.
- [ ] **Step 2–4:** run (FAIL), implement, run filter and suite.
- [ ] **Step 5: Commit** `feat(politics): a happy coalition earns political capital`.

## Task 6: Interfaces (CLI and Godot)

**Files:** Modify `tools/OpenPolitics.Cli/Play/PlayLoop.cs`, localization cs/en (help, usage, ok texts), `client-godot/PartyScreen.cs` and `PoliticsScreen.cs` (buttons), `README.md`. Test: `core.tests/Play/PlayCoalitionActionsTests.cs`.

- [ ] **Step 1: Tests (RED)** over the CLI loop helper used by `PlayPartyTests`: commands `propose N VALUE` (partner), `leave`, `dismiss PARTY`, `confidence`, each printing a success line or the localized error; wrong role prints the localized error; help lists them.
- [ ] **Step 2–4:** run (FAIL), implement CLI; Godot: buttons "Navrhnout změnu politiky" (policy selector + value), "Opustit koalici", "Vyloučit partnera" per partner, "Vyslovit nedůvěru"; enable/disable by `PlayerRole`; run `scripts/client-smoke.sh` (needs `GODOT` env) and keep it green.
- [ ] **Step 5: Commit** `feat(client): controls for partner proposals, leaving, dismissing, no confidence`.

## Task 7: Gates and documentation

**Files:** Create `docs/decisions/0007-w2-government-play.md`. Modify `README.md`.

- [ ] **Step 1:** Run suite, `validate data/base`, `batch --politics` (ignorer|keeper|random|extreme|activist, 200 games) and economy `batch` (passive|random|extreme, 200 games); build Release first (`dotnet build tools/OpenPolitics.Cli -c Release`). Expected: all stable, falls still end with a new majority, no game stuck outside Governing, no negative capital.
- [ ] **Step 2:** Write the decision record with the batch table and the parameter values chosen; commit `docs: wave 2 decision record`.
