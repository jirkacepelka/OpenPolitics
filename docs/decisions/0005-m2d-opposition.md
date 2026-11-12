# 0005 - M2d opposition and party management

Status: accepted (2026-11-12). Numbers: `batch --politics` on `base:cz_2015`, 500 games x 48 turns.

## Decisions

- The player's party has political capital (start 6, income `2 + 8 * organisation * share`, +2 as the governing
  leader), organisation (start 0.2, decays 0.01 per quarter), relations with every other party (default 0.3,
  decay 0.02 toward it), credibility and cohesion. Formulas and costs are the spec's.
- **Capital is capped at 30**: without a cap an idle opposition party banked 130 capital in twelve years and could
  then run an unbeatable campaign. The spec does not mention a cap.
- **Campaign strength**: the spec gives no magnitudes. The first guess (0.12 momentum per effort) let a campaign every
  quarter hold a group at momentum 0.7 to 0.9, that is about six times the weight in that group. 0.06 per effort holds
  it near 0.4, and an activist party gains about three points over the run instead of ten.
- `Criticize` and `TakeStance` are refused for the governing leader (`error.not_opposition`); `Criticize` is refused
  without spending capital when nobody cares about the value or it did not change (`error.no_effect`).
- Government measures are the AI governor's policy moves; stances are one per measure.
- Opposition pressure lowers voters' effective satisfaction (`s - pressure`), which both lowers the government
  parties' poll (blame) and the partners' loyalty (their voters' mood).

## Gate

| strategy | player poll over the run | seats at the end |
|---|---|---|
| ignorer | 18.5 % | 39.3 |
| activist | 21.6 % | 36.3 |

- Activism raises the player's poll by 3.1 points averaged over every quarter (needed: at least 1, at most 15).
- The end-of-run poll and the last election are **not** good measures: the activist party more often ends up
  governing, and governing is blamed (its end poll is the same size as the others'). The gate therefore uses the mean
  over all quarters, and a controlled unit test (`CampaignRaisesTheSupportOfThePlayersPartyAmongThatGroup`) checks the
  campaign effect with everything else equal.
- No game ends with negative capital or outside `Governing` for more than two formation cycles; results are
  deterministic per seed.

## Open

- Faction splits and new parties are out of scope (spec section 12). Turnout is one constant for every group.
