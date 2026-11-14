# 0007 – Wave 2 government play (playtest round)

Date: 2026-11-14. Spec: `docs/specs/2026-11-14-playtest-round-design.md`, plan `docs/superpowers/plans/2026-11-14-w2-government-play.md`.

## Changes
- **ProposePolicy** (partner, 3 capital): the leader decides next quarter with `score = 0.4*relation + 0.2*seatShare + 0.4*fit`,
  accepting at 0.5 and never when another partner's red line would be crossed (spec weights 0.5/0.3/0.2 with 0.35 accepted
  nearly everything). The move is capped at 25 % of the policy's range and shows up as a government measure.
- **Governor**: commitments first, then crisis responses every turn (deficit above 5 % of GDP or an unmet value demand lowers the
  largest spending line by 10 % of its range; unemployment of 8 % or more, or an unmet unemployment demand, raises spending
  toward the leader's target), then drift toward the leader's own platform every second turn. Moves that would newly cross a
  partner's red line are skipped while an alternative exists.
- **LeaveCoalition**, **DismissPartner** (4 capital, majority must survive), **NoConfidence** (10 capital; passes when willing
  opposition parties with relation of at least 0.4 plus partners below loyalty 40 hold a majority; the largest willing party
  forms first). Failing costs 0.2 credibility, leaving 0.15 credibility and 0.3 relation, plus grudges.
- **Mid-term demands**: a partner below its election result raises an unmet catalogue demand every 8–12 quarters, due in 8
  quarters (+10 loyalty when met, −25 when missed; a policy change that is queued counts as delivered). They are outside the gradual shortfall loyalty target (otherwise the
  partner left in six quarters). New value demands: balanced budget, low inflation, low unemployment (weight 1.5, so the AI
  formateur still prefers the old demands).
- **Capital**: income `+3*clamp((meanPartnerLoyalty−50)/50)` for a leader, `+3*clamp((relation−0.3)/0.7)` for a partner;
  `PartyStatus.Income` shows it.
- CLI: `propose`, `leave`, `dismiss`, `confidence`, `index`; event text now only in `Texts.Event` (the CLI had its own copy).
  Godot: Party tab has the controls.

## Gates (200 games x 80 turns, `cz_2015`)
| strategy | partner loss | falls | mean turns to first loss | player poll | after wave 1 |
|---|---|---|---|---|---|
| ignorer | 100 % | 587 | 8.0 | 17.4 % | 406 falls |
| keeper | 98.0 % | 423 | 26.5 | 17.5 % | 82.5 %, 244 falls, 31.1 |
| activist | 100 % | 804 | 8.0 | 21.3 % | 21.3 % |
| random / extreme | 100 % | 600 / 1344 | 8.6 / 9.9 | 16.9 / 13.3 % | – |

All falls end with a new majority, no game stuck outside Governing, no negative capital, economy batches stable.

## Known
- A fall roughly every 7 years for an idle player: mid-term demands raise churn; the −25 penalty is the first knob if it feels harsh.
- The no-confidence branch "unhappy partners vote with the opposition" is implemented but only the "willing opposition majority" branch has a test.

## Review fix pass (final review of waves 1+2)
- Governor no longer undoes a met policy commitment through platform drift or crisis moves (it ping-ponged forever).
- The player's own party never receives mid-term demands.
- Mid-term demands: deadline 8 quarters (was 6) and queued changes count as delivered (delays of 2–4 quarters made most demands unmeetable).
- Proposals are vetted against, and applied from, the same value (policy changes still waiting for their delay included).
- Gates after the fix (200 games): ignorer 485 falls, keeper 86.5 % partner loss / 280 falls / first loss at 29.2 turns,
  activist 751 falls, 21.4 % poll; all stable.

## Rulings
- **Old saves** are rejected with `SaveMismatchException` because the base pack changed (hash) and politics now draws from the RNG
  at formation; the save format itself still reads (only new action types were added). Saves are replayed from the action log, so no
  migration is possible. The spec line "old saves keep working" is therefore limited to the format; cost: games started before
  2026-11-14 cannot be resumed.

## Deferred minors (from the review)
Queued actions can be charged twice (NoConfidence, DismissPartner twice per turn) and proposals are dropped without a refund when a fall or
leaving happens the same turn; accepted proposals that change nothing still cost capital; the player's own grudge (fades within a
turn at the default size) could veto their own coalition choices; `DemandMet` pays when delivered, not at the deadline; indexation
is accepted with GDP 0 and extreme deflation can blow up `none`-indexed amounts; re-sending the same mode resets the indexation base;
capital income uses the pre-exit role on the turn of an exit; the no-confidence "unhappy partners" branch is untested.
