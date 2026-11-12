# Domestic politics design (M2)

Status: **draft for user approval** · Scope: milestone M2 (elections, majority building and keeping, coalitions, opposition,
party management, Czech content). Builds on the M1 core (graph, macro model, voter satisfaction, events, save/replay).

## 0. What the user decided, and what I filled in

User input for this spec:
- Czech elections are **proportional**.
- Partner loyalty: "I don't know, propose something" - section 6 is my proposal.
- Opposition play is "criticism of the government, support or rejection of what it did, campaigning, party building;
  work it out" - section 8 is my interpretation.

Everything else follows the brief: no per-law votes, the majority rule is the same in every electoral system, partners
have demands and red lines, a small swing bloc is intentional, politicians and parties are fictional.
Assumptions I made are collected in section 12 so you can overrule them.

## 1. Concepts

| Concept | Meaning |
|---|---|
| `Party` | Fictional party with a platform (policy positions + weights), voter-group affinities, demands, red lines, factions. |
| `Seats` | Result of an election: seats per party under the country's electoral system. |
| `Government` | Set of parties (leader + partners) with a coalition agreement, or none (caretaker). |
| `Commitment` | A partner demand that was written into the coalition agreement. Tracked every turn. |
| `Loyalty` | A partner's willingness to stay, in [0, 100] (an attitude, so it is bounded like voter satisfaction). |
| `Relation` | How well two parties get along, in [0, 1]; the player builds it by courting. Used in coalition talks. |
| `Phase` | `Governing`, `Formation`, `Caretaker`; election day is an event between phases. |
| `Role` | The player party is `Leader` (heads the government), `Partner` or `Opposition`. |
| Resources | `Political capital` (spent on actions) and `Organization` in [0, 1] (the party's machine). |

Everything is per country and id-ordered, as in M1. Parties are not tied to one country, so the USA (M4) reuses the model.

## 2. Data additions

New or extended folders (all data, validated like M1; error codes continue the E-series):

```yaml
# parties/*.yaml  (fictional)
- id: base:civic_right
  name_key: party.civic_right
  platform:                         # preferred policy settings and how much the party cares (0..1)
    base:income_tax: {target: 20, weight: 0.8}
    base:corporate_tax: {target: 3, weight: 0.6}
  affinity: {base:high_income: 0.6, base:middle_class: 0.3, base:low_income: -0.3}   # base pull of each voter group
  swing: false                      # true for the intentional swing bloc (section 7)
  demands:                          # what the party wants in a coalition agreement
    - {id: lower_income_tax, kind: policy, policy: base:income_tax, op: "<=", target: 22, weight: 3, due_turns: 8}
    - {id: balanced_budget,  kind: value,  value: base:debt, op: "<=", target: 1.9e12, weight: 2, due_turns: 12}
  red_lines:                        # the party leaves (or refuses to enter) if this holds
    - {policy: base:corporate_tax, op: ">=", threshold: 8}
  price: 4                          # total demand weight it needs in the agreement to join
  factions:                         # only used for the player's party
    - {id: market_wing, weight: 0.6, platform: {base:income_tax: 18, base:corporate_tax: 2}}
    - {id: social_wing, weight: 0.4, platform: {base:income_tax: 24, base:social_benefits: 9}}

# electoral_systems/*.yaml
- id: base:czech_pr
  type: proportional                # types are built into the core; mods choose and parameterise, they do not add code
  seats: 200
  method: dhondt
  threshold: 0.05
```

Country parameters (`countries/*.yaml`, `politics:` block): `electoral_system`, `election_interval_turns` (16),
`majority: {rule: more_than_fraction, value: 0.5}` (a parameter of the country, as the brief proposes; 101 of 200 seats),
`max_formation_turns` (4), `caretaker_turns` (2), `program_slots` (6, how many demands a coalition agreement can contain).

Scenario `politics:` block: `player_party`, starting `support` per party (sums to 1), starting `government` (parties,
leader, commitments) or none, `turns_to_election`.

## 3. The electorate: from satisfaction to votes

The M1 voter groups keep their satisfaction `s_g` in [-1, 1]. Each group also has a `turnout` (default 0.65). A group's
leaning to party `p`:

```
u(g,p) = affinity(g,p)
       + k_pos * (1 - d(g,p)) * (0.5 + 0.5 * credibility_p)          # platform closeness, d in [0,1]
       + blame(g,p)
       + momentum(g,p)                                               # temporary, from campaigns and criticism
       + noise(g,p)
blame(g,p) = +k_gov * s_g          if p is in the government          # happy voters reward, unhappy punish
           = -0.5 * k_gov * s_g    otherwise
support(p) = sum_g size_g * turnout_g * softmax_p( u(g,p) / tau )      # shares of all votes, normalised to 1
```

`d(g,p)` is the weighted mean distance between the group's `policy_prefs` and the party's platform over policies both
mention (distance divided by the policy range). Defaults: `k_pos = 1.0`, `k_gov = 1.0`, `tau = 0.5`.
`noise` is drawn from the seeded RNG as the sum of 12 uniforms minus 6 (no trigonometry, deterministic), scaled by
`sigma = 0.05` per turn (and by 3 for swing parties). `momentum` decays with a half-life of 4 turns. Polls (`support`)
are visible every turn; the election result is `support` plus a small election-day noise (`sigma = 0.01`), renormalised.

## 4. Electoral system module

The core has an `IElectoralSystem` implementation per `type`. M2 ships `proportional`:
parties below `threshold` of the valid votes are excluded, seats go by D'Hondt (highest averages; ties to the party
with the lower id). Plurality for the USA (M4) is another implementation of the same interface; coalition building
and loyalty never look at the system, only at the resulting seats and the country's majority rule.

## 5. Government formation

Phases: `Governing` -> (election, or a fall) -> `Formation` -> `Governing`. After an election the previous
government becomes `Caretaker` until a new one forms.

**Who forms.** The party with the most seats is the formateur (ties: more votes, then lower id). If it cannot form a
government within `max_formation_turns`, the next largest party tries; after two failures the caretaker government
calls early elections.

**Player as formateur.** The player proposes `ProposeCoalition(partners, agreements)` where `agreements` is a list of
`(party, demand)` pairs, at most `program_slots` in total. Each invited party answers at once:

1. **Majority:** the coalition's seats satisfy the country's majority rule, otherwise it cannot form.
2. **Red lines:** no agreed demand (from any partner) contradicts a red line of any partner. A demand contradicts a red
   line when both concern the same policy and the demand's target lies inside the red line's forbidden range.
3. **Price:** the weight of that party's demands in the agreement is at least `price * (1 - 0.5 * relation)`.

A refusal comes with a reason (`majority`, `red_line:<demand>`, `price`) so the player knows what to change. The slot
limit makes conflicts real: you cannot give everybody everything.

**Player not formateur.** The AI formateur picks the minimal winning coalition with the smallest ideological distance
to its own platform (relation to the player counts as a bonus), then allocates demands greedily by weight per slot
until each partner's price is met. If the player's party is a chosen partner, the player receives an offer and answers
`AcceptOffer` / `RejectOffer`; on rejection the AI picks its next-best coalition.

A formed government stores the agreed demands as `Commitment`s with their baseline (the value when the agreement was
made) and `due_turns`.

## 6. Keeping the majority: loyalty (proposal)

Every partner (not the leader) has `loyalty` in [0, 100], starting at 70. Each turn, for partner `p`:

```
progress(d)  = clamp((x - baseline) / (target - baseline), 0, 1)          # x: current policy/value, direction by op
expected(d)  = min(1, turns_since_formation / due_turns)                  # delivery is expected gradually
fulfil_p     = sum_d weight_d * progress(d) / sum_d weight_d              # 0.5 if p has no commitments
shortfall_p  = sum_d weight_d * max(0, expected(d) - progress(d)) / sum_d weight_d
base_mood_p  = sum_g affinity_share(g,p) * (s_g - opposition_pressure_g)   # how happy p's own voters are, in [-1, 1]
target_p     = clamp(50 + 40*fulfil_p - 60*shortfall_p + 20*base_mood_p - 20*[red line currently violated], 0, 100)
loyalty'     = loyalty + lambda * dt * (target_p - loyalty)               # lambda = 1 per year (2 for swing parties)
```

A red line **crossing** (a condition that was false and becomes true) also subtracts `shock = 30` at once.
A shift in the readable target means players see "what are they waiting for": the panel shows each partner's
commitments with progress against expected, which red lines are close, and the mood of the partner's voters.

**Thresholds** (all country parameters):

| Loyalty | Effect |
|---|---|
| >= 60 | stable |
| < 60 | warning: the partner publicly complains (event `LoyaltyWarning`, names the most late demand) |
| < 40 | ultimatum: names a demand and gives `ultimatum_turns = 4`; resolved when loyalty is back above 50 or that demand's progress reaches its expected level |
| <= 15, or ultimatum expired | the partner leaves the government (`PartnerLeft`) |

**Consequences of leaving.** If the remaining coalition still has the majority, nothing else changes (the partner's
commitments are dropped). If not, the phase becomes `Caretaker` for `caretaker_turns`: the largest remaining party
tries to form a new majority (player or AI formateur rules above, the leaver may be re-invited at a price). If that fails
the government falls and early elections follow. Without a majority (`Caretaker`, `Formation`) policy changes by the
player are rejected with `error.no_majority`: you need the majority to do what you want.

**Conflicting demands** are intended: when two partners want opposite things the player picks whom to disappoint;
the shortfall of the other drives their loyalty down.

## 7. The swing bloc

A party with `swing: true` (a small party or faction around the 5% line) has: its support noise tripled, loyalty
reversion doubled (it reacts faster), and demands that may be **value goals** (`kind: value`, e.g. environment quality)
rather than only policy settings. It is also frequently the party that decides the majority, so both the electorate
model (large swings) and the loyalty model (short fuse) make it the pivot, as the brief intends. It has its own goals,
not a price in votes only.

## 8. Opposition and party management

Roles: when the player's party is `Opposition`, there are no policy actions; the opposition acts through the party.
Resources: **political capital (PC)** earns `2 + 8 * organization * support_share` per quarter (+2 for the leader
party); **organization** `o` in [0, 1] decays by 0.01 per turn.

| Action | Cost (PC) | Effect |
|---|---|---|
| `Criticize(valueId)` | 2 | If the value got worse over the last 4 turns, groups that care about it (via `value_weights`) get `momentum` toward the player and `opposition_pressure` (voters blame the government, partners' voters get unhappier). If it got better: backlash (negative momentum). |
| `TakeStance(measureId, Support/Oppose)` | 1 | `measureId` is a policy change the government made. Groups whose preferences moved closer to the new setting approve it. Supporting an approved or opposing a disapproved measure gives momentum; the reverse costs it. One stance per measure. |
| `Campaign(groupId, effort 1..3)` | 3 x effort | Momentum toward the player among that group; doubled in the last 4 turns before an election. |
| `BuildOrganization` | 4 | `organization += 0.04`; organization raises turnout of the party's own supporters by up to 20% and PC income. |
| `Court(partyId)` | 3 | `relation(player, party) += 0.1` (decays 0.02 per turn). Lowers that party's price in coalition talks. |
| `ShiftPlatform(policyId, target)` | 1 | Moves the party's position by at most 10% of the policy range per turn; every shift lowers `credibility` by half its size, which recovers 0.1 per year. |

**Identity and factions** (player party). Each faction has a preferred platform and a weight. `cohesion = 1 - sum_f weight_f * d(platform, faction_f)`.
Cohesion scales the platform term of the support formula (`0.5 + 0.5 * cohesion`) and PC income. Factions that are far
from the platform are shown as unhappy in the party panel. Splits (a faction leaving) are out of M2 scope and noted for later.

**Government by the AI.** When the player is not the leader, an AI governor moves policies toward the coalition's
commitments: each turn it takes the largest unmet commitment and changes its policy by up to 25% of the remaining
gap. The same governor runs the player's government in auto-play strategies. Government policy changes are logged as
`measure` entries (`turn:policyId`), which is what `TakeStance` refers to.

**Opposition goals** (what makes it playable): raise support before the next election, bring down the government (by
making a partner's voters unhappy until the partner leaves, and by courting that partner), and build relations so you
can enter the next coalition. The UI in M3 shows these as three bars: poll standing, coalition fragility, relations.

## 9. Turn order (extends M1 section 6)

1. Player actions (role-dependent). 2. Government/AI governor policy changes. 3-6. As in M1 (edges, economy, integration,
voter satisfaction). 7. Politics: update `opposition_pressure` and `momentum`, party support, loyalty, thresholds and
ultimatums, partner departures. 8. Phase machine: election day when due (votes, seats, government falls and
formation starts), formation timers, caretaker timers. 9. Events (M1 and political). 10. Report.

All politics iterates parties and groups in ordinal id order and draws noise from the game RNG in that order.

## 10. Core API additions

Actions: `SetPlatform`/`ShiftPlatform`, `Criticize`, `TakeStance`, `Campaign`, `BuildOrganization`, `Court`,
`ProposeCoalition`, `AcceptOffer`, `RejectOffer`, `CallEarlyElection` (government leader only). Each returns an
`ActionResult` with an error key (`error.no_majority`, `error.not_enough_capital`, `error.refused.red_line`, ...).
`GameState` gains `Politics` (phase, seats, support, government, commitments with progress, loyalty, relations,
resources). `TurnReport` gains typed `PoliticalEvents`: `LoyaltyWarning`, `Ultimatum`, `PartnerLeft`, `GovernmentFell`,
`ElectionHeld`, `FormationStarted`, `GovernmentFormed`, `Offer`. The save format already replays actions, so it only
needs the new action types.

## 11. Testing, auto-play and the M2 gate

Unit tests first, as in M1: D'Hondt and threshold on known examples, support shares sum to 1, loyalty progress and
thresholds on constructed cases, formation refusal reasons, role changes, determinism and save/replay with political actions.

Headless strategies for the player party: `coalition-keeper` (tries to meet demands), `ignorer` (never acts), `random`,
and `extreme`. The runner reports, over many games: governments formed, **governments that fell by the rules**,
partner departures, new majorities after falls, early elections, coalition lifetime distribution and diversity of outcomes
(who governs after each election).

**Gate (brief):** in automatic games governments fall by the rules and a new majority forms - concretely, with the
`ignorer` strategy at least 80% of coalitions lose a partner within 12 years and, of the falls that left no majority,
at least 60% produce a new majority within `max_formation_turns` or early elections that produce one;
with `coalition-keeper` coalitions last markedly longer than with `ignorer`. Numbers are tuned against the first batch
and recorded in a decision note, like M1.

## 12. Assumptions to confirm

1. Loyalty is the target-and-lag model in section 6 (readable: "what level of trust has delivery earned"), not a pure
   event counter.
2. Czech elections use one national list with a 5% threshold and D'Hondt (regions and the 8% and 11% coalition
   thresholds are left out as illustrative detail; they can be modelled later in data).
3. The majority is "more than half of the seats" as a country parameter (still unconfirmed by you from the brief).
4. The player's starting party comes from the scenario (`player_party`); choosing it in the UI is M3.
5. Opposition has no direct "vote" on bills (no per-law votes by design); it acts through opinion, relations and
   party building.
6. Faction splits and new parties appearing mid-game are deferred. M2 content ships 8 fictional parties with
   partly overlapping bases and one swing party.
7. The AI governor is simple on purpose (it only follows the agreement) so that falls come from the loyalty model.

## 13. Content for Czechia 2015 (fictional parties)

About 8 parties covering the spectrum, with start support summing to 100%: a centrist-populist business movement
(about 27%), a social-democratic party (15%), a conservative-liberal party (12%), a Christian-democratic party (8%),
a communist-heritage left party (12%), a nationalist-populist party (7%), a green party (6%) and the **swing party**,
a small internet-and-transparency movement (around 5.5%, on the threshold). Each has 3-4 demands, 1-2 red lines and a
platform on the base policies; their affinities to the six M1 voter groups define the starting poll. No real names of
parties or politicians appear. Several events are added: scandal, partner conflict, early-election pressure, and
campaign-season events.

## 14. Delivery: four plans inside M2

Each gets its own implementation plan and ends with a green gate; later plans depend on earlier ones:

- **M2a** Parties, support model, electoral-system module, elections and seats. Gate: simulated elections are
  deterministic, shares and seats consistent, thresholds respected.
- **M2b** Formation: formateur rules, `ProposeCoalition`, AI formateur, offers, caretaker and early elections. Gate:
  formation works for every election in a 1000-election batch (or ends in early elections).
- **M2c** Loyalty, commitments, ultimatums, departures, AI governor, falls and new majorities. Gate: the automatic-play
  gate above for `ignorer` and `coalition-keeper`.
- **M2d** Opposition and party management (resources, criticism, stances, campaign, courting, platform, factions),
  Czech content and tuning. Gate: the full M2 gate in the brief.
