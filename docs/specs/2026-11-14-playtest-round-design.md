# Playtest round 1 – design (waves 1–6)

Status: approved by the user in conversation on 2026-11-14 ("A) … začni pracovat"). The builder check was skipped on the
user's request to start working; every call marked **(call)** is mine and can be changed.

## Why

The first human playtest of M0–M3 (Godot client) showed that the simulation is correct but not yet fun or believable:
the player's decisions matter too little, the coalition game is thin, the economy ignores inflation, and several
behaviours look like bugs. Goal of this round: make a playthrough of 20+ years feel like running a party in a
coalition, not like watching one. Non-goals: USA pack (M4), geopolitics (M5), multiplayer, new countries.
Anti-goals: no per-law votes, no fixed GDP ceiling, no hand-scripted outcomes; determinism and "packs hold data only"
stay intact.

## The playtest notes, grouped

| Wave | Notes covered |
|------|---------------|
| 1 Model fixes | popularity halves after joining government; re-proposing the same coalition restores trust; higher GDP has no effect; taxes and spending are tied to GDP; inflation and real growth are barely used |
| 2 Government play | the leader decides, not me; no way to bring down or leave a coalition; parties should give ultimatums during the term; more kinds of demands, promises and criticism (e.g. balanced budget); happier coalition = more action points |
| 3 Elections and parties | formation should not always be led by the top party; adjustable threshold 0–10 %; better work with party priorities; square compass in party preview |
| 4 Content | more laws (policies), more events |
| 5 Client | start-of-turn popup (news and decisions); clickable and more graphs; infographic of policy effects (as in Democracy 4) |
| 6 Graphics | overall visual overhaul of the Godot client |

Each wave gets its own implementation plan, written when the previous wave is done. Waves 1 and 2 are specified in
full below; later waves are sketched and get their detail when their plan is written.

## Findings behind wave 1 (from reading the code)

1. `SupportModel` adds `blame = kGov * satisfaction` for a governing party and `-0.5 * kGov * satisfaction` for the
   others. With satisfaction near −0.3 and tau 0.5, a party that enters government moves by about 0.9 in log-odds at
   once: roughly 2.5× fewer votes within two quarters. Blame must build up gradually.
2. `PoliticsEngine.Form` always calls `ResetLoyalty`: every partner starts at loyalty 70 and commitments are rebuilt,
   whatever happened before. Re-forming the same coalition erases a fall.
3. The macro model is real-only (no price level). Satisfaction reacts to the *annual relative change* of values, so a
   high GDP level has no lasting effect. All taxes and spending are `percent_of_gdp`.

## Wave 1 – model fixes

### 1.1 Gradual attribution of blame
- New per-party state `attribution` in [0, 1]: how much voters see the party as responsible for the state of the
  country. Governing leader target 1.0, governing partner 0.7 **(call)**, opposition 0.0.
- Each quarter `attribution` moves toward its target with half-life 4 turns (the same half-life already used for
  momentum). A party that just left government therefore keeps part of the blame (or the credit).
- Blame term: `kGov * sat * (a - 0.5 * (1 - a))` (reduces to the current formula at a = 1 and a = 0).
- Calibration at game start uses the attribution of the start coalition at its target values, so start polls do not
  change.
- Acceptance: a party entering government as leader loses at most 25 % of its poll share in the first two quarters at
  mood −0.3; the long-run level is unchanged from today; same seed → identical results.

### 1.2 Memory of betrayal and re-forming the same coalition
- New per-party state `grudge` toward the player-visible government: raised when a party leaves a government
  (+0.5 for the leaver towards the former leader, +0.3 for the leader towards the leaver) and when a government falls
  (+0.2 for all former partners); half-life 8 turns.
- `CoalitionRules.Evaluate`: a party refuses a coalition containing a party it holds a grudge ≥ 0.5 against
  (new refusal reason `grudge`); below that, grudge raises the price (one extra demand slot per 0.25 grudge).
- Start loyalty of a partner in a new government: `clamp(Start + 20 * (relation − 0.3) / 0.7 − 30 * grudge, 20, 85)`
  instead of a fixed 70. Commitments are still rebuilt from the new agreement, but demands the same party had left
  unfulfilled in the previous government add 0.1 to its grudge each, at the moment it leaves.
- Acceptance: re-proposing the identical coalition right after a partner left yields either a refusal or a start
  loyalty strictly lower than the original start; test with the real Czech data.

### 1.3 Economy with prices
- New value `base:price_level` (index, 100 at start, owner macro_v1); updated each turn by `inflation`.
- New derived values (computed by the model each turn, owner macro_v1): `base:gdp_nominal` (GDP × price level / 100),
  `base:real_growth` (annual real GDP growth in %), `base:deficit_pct` (total deficit, % of nominal GDP).
  Voters and demands may refer to them like any value.
- Fiscal policies get an optional `indexation: gdp | inflation | none` setting chosen by the player (default `gdp`,
  so old saves and scenarios behave as before):
  - `gdp`: parameter is a share of GDP (today's behaviour);
  - `inflation`: parameter is a real amount; the share of GDP floats with growth;
  - `none`: parameter is a nominal amount; its real value erodes with inflation.
  New action `SetIndexation(policyId, mode)` (free, effective immediately, logged and saved).
- Tax policies are declared as rates on a base (`unit: percent`, `revenue_base`, `base_share` of GDP) so that income
  tax, VAT and corporate tax read as familiar rates; defaults reproduce today's revenue **(call)**.
- Acceptance: with inflation shocked to 8 %, a `none`-indexed pension line shrinks in real terms and a
  `inflation`-indexed one does not; batch gates (stability, no ceiling) still pass.

### 1.4 Level effects in voter satisfaction
- New optional group field `level_weights: {value_id: w}`: satisfaction gains `w * ln(value / habit)` per year, where
  `habit` is an exponential moving average of the value with half-life 12 turns. Real GDP per worker, real income and
  health get small weights in `groups.yaml`, so durable growth is rewarded and stagnation is punished.
- Acceptance: after 20 years of 3 % growth vs 0 % growth the mean satisfaction differs by at least 0.15 under an
  otherwise idle player.

## Wave 2 – government play

### 2.1 Partner influence by proposals (chosen by the user: option A)
- New action `ProposePolicy(policyId, target)` for a party in the government that does not lead it. Cost 3 PC.
- The leader decides at the end of the turn, deterministically from
  `score = 0.5 * relation(leader, player) + 0.3 * seatShare(player, of government) + 0.2 * leaderFit − 0.3 * redLineHit`
  where `leaderFit` is 1 − normalised distance between the target and the leader's platform target for that policy.
  `score ≥ 0.35` accepts **(call)**. A red-line hit (the move crosses another partner's red line) always rejects.
- Accepted: the policy moves toward the target by at most 25 % of its range this turn, as a government measure (news
  item, shown to the opposition for stances) and relation +0.05. Rejected: relation −0.03 and a news item with the
  reason. Result arrives as a political event next turn, so the decision has a visible outcome.

### 2.2 Leader decides: richer governor
- When the player does not lead, the governor, besides unmet commitments, also moves toward the leader's own platform
  (weight 0.5 of a commitment) and responds to crises: deficit above 5 % of GDP → tightens its highest-spending policy;
  unemployment ≥ natural + 3 → expands the largest stimulus. Never crosses a partner's red line unless nothing else is
  available (then the partner takes the red-line shock). Every move is a news item naming the leader.

### 2.3 Leaving, dismissing, no confidence
- `LeaveCoalition` (player is a partner): the player's party exits; if the rest has no majority the government falls
  (existing path, leader first); credibility −0.15, relation with leader −0.3, grudge added.
- `DismissPartner(partyId)` (player is leader): the partner leaves; allowed only when the rest still has a majority or
  another party is invited at once (`ProposeCoalition`-style replacement is a later improvement; majority required for
  now). Cost 4 PC, the partner's grudge +0.5.
- `NoConfidence` (player in opposition, 10 PC): passes when seats of opposition parties willing to vote for it plus
  partners with loyalty < 40 reach a majority. An opposition party is willing when its relation with the player ≥ 0.4.
  Passing → `GovernmentFell` with the leader losing first try (**call**: the largest opposition party tries first);
  failing → credibility −0.2.

### 2.4 Ultimatums and demands during the term
- Every 6–10 turns (RNG stream of politics), a partner whose poll share is below its election result issues a
  mid-term demand from its catalog with a deadline of 6 turns; failure costs loyalty as an ultimatum does. Today's
  loyalty-based ultimatums stay.
- New demand kinds in `parties.yaml`: `value` demands on `base:deficit_pct` (balanced budget), `base:inflation`,
  `base:unemployment`, `base:debt` (≤ / ≥ target) in addition to policy demands. The opposition can criticise any
  value, including the new derived ones.
- Promises are deferred to wave 3 (they need the platform/priorities work).

### 2.5 Happy coalition earns capital
- Per-turn PC income gets `+ 3 * clamp((meanLoyalty − 50) / 50, −1, 1)` for a leader (mean over partners) and
  `+ 3 * clamp((relationWithLeader − 0.3) / 0.7, −1, 1)` for a partner **(call)**; unhappy coalitions cost up to 3 PC.
  PC stays within [0, 30].

## Sketches of later waves

**Wave 3.** AI formation becomes a market: each seated party computes its best proposal; the viable one with the best
`0.6 * coherence + 0.4 * seatShare (+0.1 for the largest party)` forms; if the player is a partner they get an offer.
Scenario option `electoral_threshold` (0–10 %, default from the country pack) editable on the new-game screen, saved in
`SaveData`. Party priorities: each platform item gets a priority weight the player can shift (1 PC); weights feed voter
distance and negotiation price. Policies get `axis_x`/`axis_y` loadings so the client can draw a square compass.
Promises as a new action before an election.

**Wave 4.** Policies grow from 13 to about 35 (tax rates, subsidies, defence, culture, transport, housing, energy,
immigration, regulation), events from 5 to about 25, demand catalogs for all eight parties. Data only, validated.

**Wave 5.** `Game.PreviewPolicy(policyId, value)` returns edge contributions per value, so the client can show a
Democracy-4 style infographic (arrows with strength) on the policies screen. Start-of-turn popup (news, offers,
ultimatums) driven by `TurnReport`. Graphs: click to open a larger view with range selector; add GDP real/nominal,
deficit, inflation, party polls, loyalty.

**Wave 6.** Theme, typography, spacing, iconography; screenshots reviewed with the user.

## Approaches considered (partner influence)

- **a) Proposals (chosen):** player spends capital, the leader accepts or refuses by a transparent score.
  Gives a decision loop with visible reasons; costs one new action and a scoring function.
- b) Veto only: cheap but passive; the player still cannot make things happen.
- c) Council: coalition parties share turns by seats; strong but replaces the single-leader model and the governor.

## Constraints that stay

C# core independent of Godot; YAML packs hold data only; cs/en localisation for every new text; deterministic
(Pcg32, ordinal iteration, own RNG stream for politics); old saves and the existing scenarios keep working (new
settings default to today's behaviour); never push.

## Left to the builder

Exact parameter values in this document are first guesses; each wave ends with a batch gate (existing
`batch --politics` plus new assertions above) and a decision record in `docs/decisions/` if values change.
