# 0003 - M2b government formation

Status: accepted (2026-11-12).

## Decisions

- Phases: `Governing`, `Formation` (formateur attempts; the old government caretakes), `Caretaker` (two attempts failed,
  `caretaker_turns` before early elections). The election countdown runs in every phase, so elections keep their fixed
  term; an election that falls due during formation aborts it.
- The largest party forms first (seats, then votes, then lower id). An AI formateur answers within one turn and picks
  the closest minimal winning coalition whose partners' prices can be paid with demands that hit no red line. A player
  formateur has `max_formation_turns`; refusals carry a reason (`majority`, `price`, `red_line`, `contradiction`,
  `slots`, `unknown`) and do not use up the attempt.
- Added beyond the spec: two agreed demands that cannot both be met are refused as `contradiction` (they would be
  commitments nobody can keep).
- An unanswered offer lapses at the next end of turn (the player must opt in); `RejectOffer` does the same at once.
- Only the leader of the government may change policy (`error.not_leader`); outside `Governing` the error is
  `error.no_majority`. The AI governor for non-leader governments arrives in M2c.
- Relations between parties are a constant 0.3 until M2d adds courting.

## Gate result (1000 seeds x 48 turns on `base:cz_2015`, player only uses `auto` and `accept`)

- 3000 elections (three per game), 3000 governments formed, 0 early-election calls.
- Coalition sizes (parties): 1 -> 1, 2 -> 283, 3 -> 545, 4 -> 1990, 5 -> 181. The AI often prefers four small parties
  to two larger ones because ideological closeness is averaged per partner; tune with real play in M2c/M2d.
- The failure branches (nobody can pay the price, player formateur idle, lapsed offers) are covered by
  `FormationTests`; the Czech content never reaches them on its own.
