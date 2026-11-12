# 0004 - M2c loyalty, falls and the AI governor

Status: accepted (2026-11-12). Numbers are from `batch --politics` on `base:cz_2015` (illustrative data).

## Decisions

- Loyalty follows the spec's target-and-lag model with the spec's constants (start 70, warn < 60, ultimatum < 40 for
  4 quarters, leave at <= 15 or on expiry, red-line shock 30, lambda 1 per year and 2 for swing parties). The
  constants live in `LoyaltyParams`; they are not data yet.
- `base_mood` weights voter groups by `size * exp(affinity)`; `opposition_pressure` stays 0 until M2d.
- A government that loses its majority to departures falls: the leader gets the first formation attempt, any other
  seated party follows (M2b rules). A new government replaces the old one entirely.
- The AI governor moves a quarter of the gap per turn, but at least 2 % of the policy range so it arrives.
- The starting coalition of `base:cz_2015` now has a written agreement (scenario `agreements:`: infrastructure and a
  lean state for the Citizens' Movement, pensions for the People's Union), otherwise no commitments exist to be
  broken before the first election.
- `run` and `batch` default to the first scenario without politics, `play` to the first with politics: the economy
  batch on a political scenario runs the governor's unfunded promises into debt spirals.

## Gate (1000 games x 48 turns, strategies act only through the public API)

| strategy | games losing a partner | mean turns to first loss | falls per game | falls with a new majority |
|---|---|---|---|---|
| ignorer | 100 % | 8.0 | 2.5 | 100 % (2549 of 2549) |
| keeper | 99.4 % | 21.0 | 1.2 | 100 % (1201 of 1201) |

- Ignorer: at least 80 % lose a partner (pass, 100 %); at least 60 % of falls lead to a new majority (pass, 100 %).
- Keeper coalitions last 2.6 times as long as ignorer coalitions (needs 1.5 times; pass).
- No game stays outside `Governing` longer than two formation cycles.

## Observations for tuning

- Falls are frequent even for the keeper (a partner loss about every 5 years): the AI picks four-party coalitions and
  voters' mood (about -0.3 at the start) drags `base_mood` down. Expect to soften this when the opposition game (M2d)
  and real play give feedback.
- The Czech content never ends in early elections on its own; that branch is covered by unit tests only.
