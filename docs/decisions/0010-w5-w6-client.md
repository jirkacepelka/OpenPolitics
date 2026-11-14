# 0010 – Waves 5 and 6: charts, briefing, infographic and a new look

Status: accepted (2026-11-15).

## Core additions (wave 5)

- `Game.PreviewPolicy(policyId, value)` returns a `PolicyPreview`: first-order, steady-state change per value (from the edges that leave the
  policy, delays ignored), the voter groups' pressure from the policy setting itself and the change of the deficit in percent of GDP. It starts
  from the value the policy will have once queued changes are in, returns null for an unknown policy or a value outside its range and changes
  nothing (tests in `PolicyPreviewTests`).
- `History` (core/Presentation) records every value, each group's satisfaction, mood, debt ratio, party polls and partner loyalty per turn for the
  charts; missing points are NaN (`HistoryTests`).
- `PoliticsLimits` holds the promise window and count so clients can show when the button is available.

## Client

The first draft was a tabbed window; playtest feedback ("classic Godot design, I want something modern, inspired by Democracy 4 and Cities:
Skylines") led to a redesign: the influence graph became the permanent backdrop (`WorldMap`), tabs became a dock plus one floating glass panel
(`Main`, `Screen.PreferredWidth`), key figures moved into a HUD of clickable indicators (`HudChip`), icons are drawn as vectors (`Icons`,
`IconButton`) and the theme is built in code (`AppTheme`: dark navy, cyan accent, rounded glass cards, bold headings). New files: `Chart`,
`Charts`, `ChartDialog`, `TurnPopup`, `PolicyEffectsPanel`, `CompassView`, `WorldMap`, `AppTheme`, `Icons`.

**Ruling:** screens are still built in code rather than as scenes (as before); the look was checked with windowed screenshots from
`scripts/client-smoke.sh <dir>` (start, map, every panel, effects, chart, briefing), not by a human playtest.

## Known limits

- The effects infographic ignores delays and second-order effects by design (the panel says so).
- The map layout (values by a force layout, policies on a ring at the angle of what they act on) is computed once per pack and is not animated.
- Panels are laid out for a 1280 x 800 window or larger; below that the top bar overflows.

## Addendum (2026-11-15): second redesign

The map-first first draft was still judged "terribly bad". An Opus subagent researched the Democracy 4 and Cities: Skylines interfaces
(Positech blog and forum, Paradox dev diaries), wrote a design spec and iterated five critique rounds on windowed screenshots using the
`design:design-critique` framework. Result: Fira Sans type scale (13/15/17/20/26/32), 8 px grid, policy cards with pictograms and a category filter plus
an inspector, labelled map nodes that never collide, a parliament hemicycle, shared panel header with fade-in, borderless dialogs. Known weak spots it
reported: hemicycle wedges, compass labels can touch, the inspector scrolls at 800 px height, the GDP trend is dropped from the top bar, map arrows are
not animated. Still not a human playtest.
