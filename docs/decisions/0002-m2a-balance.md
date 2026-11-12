# 0002 - M2a balance notes (parties, polls, elections)

Status: accepted (2026-11-12). Content is illustrative and fictional.

## Decisions

- **Calibration.** Per-party offsets are fitted at game start so the scenario's stated poll numbers are reproduced
  exactly (within 1e-6). The update is `offset += tau * ln(target / model)`; the spec's plain `ln` step overshoots by
  `1 / tau` because offsets enter the softmax divided by `tau`.
- **Separate random stream.** Politics draws from its own PCG stream (seed, stream 99), so adding a politics block to
  a scenario never shifts the economy or event draws, and the old sandbox goldens stay bit-identical.
- **Starting affinities** were set by hand from each party's character (voter-group sympathies in about [-0.45, +0.55])
  and needed no retuning: the starting coalition has 112 of 200 seats and the swing party starts at 6 % against a 5 %
  threshold.
- **Drift.** In passive play the government parties lose support over the first year because the voters' starting
  satisfaction is about -0.3 (known M1 balance note: policy defaults differ from group preferences). A test asserts
  that in at least 95 % of 200 seeds no party moves by more than 8 points in 11 quarters. Typical result: the starting
  coalition falls to a minority at the first election. That gives M2b (formation) real work to do; it is intended.

## Open

- `turnout` is a constant 0.65 for every group (spec allows a per-group value); revisit in M2d.
- `momentum` and `credibility` inputs exist in the model interface but stay at their defaults until M2d.
