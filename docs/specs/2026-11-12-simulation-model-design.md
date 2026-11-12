# Simulation model design (M1)

Status: **draft for user approval** · Scope: milestone M1 (simulation core). Politics, elections and coalitions are M2.

## 1. Goals and non-goals

Goals (from the brief):
- Deterministic core: same pack set, scenario, seed and player actions give the same state, bit for bit.
- No universal GDP ceiling. Values are in real units, influences change the *rate of change*, limits arise from the model
  (capital, labour, productivity, diminishing returns, budget, inflation, debt).
- Everything content-related is data. The model is small now and extensible (economy behind an interface).
- Explainability: every change in a value can be decomposed into per-edge contributions ("why did this change?").

Non-goals for M1: parties, elections, coalitions, UI, geopolitics, mod code execution.

## 2. Concepts

| Concept | Meaning |
|---|---|
| `World` | Holds a list of `Country`. M1 has exactly one, but ids and edges are country-scoped so M5 can add more. |
| `Value` | A simulated quantity with id, unit, initial value, optional soft display range (display only). |
| `Policy` | Player-controlled lever with a parameter in real units (rate in %, spending in CZK), cost and implementation delay. |
| `VoterGroup` | Population segment with size, weight, satisfaction in [-1, 1] and per-value/per-policy preferences. |
| `Edge` | Influence source → target (see 3). |
| `Event` | Condition + probability + effects (value deltas, policy side effects, text keys). |
| `Scenario` | Start year, turn length, starting values/policies, seed rules. |

Ids are namespaced strings (`base:gdp`). Texts are never in code: every user-visible string is a localization key (cs, en).

## 3. Influence graph

An edge is `{ from, to, kind, fn, strength, delay }`:
- `kind: additive` – adds `f(x)·strength` units per year to the target's rate of change.
- `kind: relative` – adds `f(x)·strength` percent per year to the target's growth rate.
- `fn` – `linear`, `power(p)`, `tanh(scale)` (saturating), `threshold(at, width)`; operate on the source's deviation from a
  reference value (`ref`, default: the scenario-start value) normalised by a scale, so they work in any unit.
- `delay` – `none`, `lag(turns)` (ring buffer) or `smooth(tau_turns)` (exponential smoothing).

Edges act on rates, never on target levels. A cycle of zero-delay edges is rejected by the validator; cycles are
allowed when at least one edge has a delay (feedback loops are the point).

Each turn the evaluator stores, per target, the list of `(edge id, contribution)` so the TurnReport can explain changes.

## 4. Economy core (`IEconomyModel`)

Default implementation `MacroModelV1`. State variables: `K` (capital), `A` (productivity), `Lf` (labour force),
`u` (unemployment), `P` (price level), `D` (public debt). Per-turn step (annualised rates, `dt = 0.25`):

- Output: `Y = A · K^α · L^(1−α)`, `L = Lf·(1−u)`, `α ≈ 0.35` (data parameter). Diminishing returns of capital come from `α < 1`.
- Capital: `dK = I − δK`, `I = s·Y·(1 + graph effects)`, with `s` derived from taxes, confidence and interest rate.
- Productivity: `dA/A = g_base + Σ graph contributions` (education, research, corruption, infrastructure).
- Unemployment (Okun): `du = −β·(output gap) + graph contributions`; `u` is clamped only to the physical range [0, 1].
- Budget: `revenue = Σ rate_i · base_i`, `spending = Σ policy costs`, `dD = spending − revenue + interest·D`;
  `interest = r0 + premium(D/Y)` with premium convex in the debt ratio (debt-spiral risk is real).
- Inflation: `π = π0 + κ·(output gap) + fiscal-impulse term`; feeds back to real interest and confidence.

`MacroModelV1` exposes its variables as ordinary `Value`s so the graph can connect to them (`base:gdp`, `base:capital`,
...). A later sector-based model implements the same interface and the same value ids.

## 5. Voters (M1 level)

`satisfaction_g` follows a leaky integrator of weighted value deviations and policy preferences:
`d s_g = −λ·s_g + Σ w_gv·norm(Δ value_v) + Σ w_gp·(policy_p − pref_gp)`, clamped to [-1, 1] (an attitude, not an economic
quantity). M1 only computes and reports satisfaction. It does not yet drive elections.

## 6. Turn order (fixed, deterministic)

1. Apply queued player actions (policy changes enter their delay pipeline).
2. Resolve effective policy parameters (delays applied).
3. Evaluate all edges from state at turn start (synchronous, order independent). Record contributions.
4. Step `IEconomyModel`.
5. Integrate other values: `x += rate·dt` (additive) or `x *= (1 + rate·dt)` (relative). Explicit Euler, `dt = 0.25`.
6. Update voter satisfaction.
7. Roll events (seeded RNG, events visited in id order).
8. Emit `TurnReport`.

Determinism rules: own PRNG (PCG32) seeded from the game seed; no `System.Random`, no `DateTime`, no iteration over hash
containers (use sorted lists); `double` arithmetic only with ops whose result is identical across platforms (no `Math.Pow`
fast paths – use a vetted implementation or `Math.Exp/Log` consistently; cross-platform golden tests run in CI on Linux,
Windows and macOS).

## 7. Public API of the core

```
PackLoader.Load(IEnumerable<string> packPaths) -> LoadResult { PackSet?, IReadOnlyList<Diagnostic> }
Simulation.NewGame(PackSet, string scenarioId, ulong seed) -> Game
Game.State            // immutable snapshot
Game.Apply(PlayerAction) -> ActionResult
Game.EndTurn() -> TurnReport  // changes, events, per-edge contributions
Game.Save() / Simulation.Load(PackSet, SaveData)  // seed + action log + pack hashes (+ optional snapshot)
```

No Godot types. The core reads packs through an abstraction (`IPackSource`) so tests can use in-memory packs.

## 8. Validation, balancing, stability

- Unit tests are written before implementation (xUnit): edge functions, delays, integration, macro equations, determinism,
  save/replay equality, pack merging, validator diagnostics.
- Headless `batch` runner: N games × strategies (`passive`, `random`, `extreme`). Reports: NaN/Inf, values leaving
  plausible bands (e.g. |GDP growth| > 20 %/yr), collapse to ~0 or explosion, and the **no-ceiling check**: in passive
  games with positive baseline productivity, no value converges to a fixed upper bound (tested by growth-rate trend
  and by comparing run lengths 80 vs 400 turns).
- Regression: golden snapshots for a few seeds. Changes are intentional or bugs.

## 9. Starter content for M1 (illustrative, Czechia 2015)

~15 values (GDP, capital, productivity, labour force, unemployment, inflation, debt, interest rate, revenue, spending,
health, education level, crime, environment quality, inequality), ~12 policies (income tax, VAT, corporate tax, pensions,
health, education, police, research, social benefits, environmental regulation, infrastructure, migration), ~6 voter groups
(by income, age, values), ~5 events, scenario `cz_2015_sandbox`. All data flagged `illustrative: true`.

## 10. Open points to confirm

1. Content counts above (values/policies/voter groups/events) – OK as a first slice?
2. Voter satisfaction bounded to [-1, 1] while economic values stay unbounded – OK? (ADR 0001, point 7)
3. `core` targets `net10.0` (ADR 0001, point 1).
