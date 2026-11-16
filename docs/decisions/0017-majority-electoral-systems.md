# 0017: Majority electoral systems (single-member districts, one or two rounds)

Status: accepted. Date: 2026-11-16.

## Context
Until now parliament was always elected proportionally (D'Hondt, Sainte-Lague, Hare with a threshold). The player asked for a majority system:
single-member districts, in a one-round and a two-round version. The follow-up said the districts do not have to exist in the code or the interface;
a simulation of the effect is enough.

## Decision
1. **Simulated districts, no map.** `MajoritarianSystem` (core/Politics/ElectoralSystem.cs) turns the national vote shares into seats through
   `seats` virtual districts. A party's share in a district is its national share times a stable lognormal factor (one draw per district and party id,
   fixed map seed), rescaled so the districts average back to the national shares. `spread` controls the scatter: an evenly spread small party wins
   nothing, a concentrated one wins seats with a small vote, the plurality party gains (cube-law-like). No pack data about districts, nothing shown per
   district; the result is the usual seats-per-party dictionary, so government formation, majorities and the AI work unchanged.
2. **First past the post (`fptp`).** Plurality winner per district. `strategic` (0.30 for Czechia) moves that share of the voters of every candidate
   outside the local top two to the top two, in proportion to how close the party is on the compass: the Duverger squeeze.
3. **Two rounds (`two_round`).** More than half of the votes wins at once, otherwise the best `runoff_top` meet again. The voters of eliminated
   candidates (less `abstain`) go to the finalists by ideological proximity (compass distance, weight exp(-3 d)), so a centre party's supporters
   can pick the winner and the plurality leader can lose. Weaker strategic voting (0.08).
4. **A policy, not a hard-coded setting.** `base:electoral_system` (0 proportional, 1 districts, 2 districts with runoff) is a lever of the interior
   ministry, capital weight 1.5 (about 17 points per step, several quarters of saving), takes effect at the next election. The threshold and method
   levers only act on proportional elections. Two start scenarios, `cz_2015_districts` and `cz_2015_runoff`, begin in the majority systems.
5. **Consequence for coalitions.** The five small parties have a red line `electoral_system >= 1` (and the lever in their programmes at 0), so a
   government that introduces districts risks losing its small partners. Big parties have no position and the AI governor does not reform the system
   on its own, because no AI party's programme asks for it.
6. `ElectionHeld` carries `Kind`, so texts say "no district won" instead of "below the threshold".

## Results (batch, 40 games x 160 quarters, base pack)
Both new scenarios and both strategies (ignorer, keeper): Stable, no game stuck outside Governing, a new majority after every fall
(districts: 35 and 17 falls, runoff: 39 and 18), no negative capital.

## Known gaps
No per-district view, campaign targeting or by-elections. Regional parties are not modelled (every party has the same `spread`). The reform
has no direct voter reaction. Tuning of `spread` and `strategic` is by feel; the Czech illustrative parties stay below 40 % so a single party seldom
wins alone.
