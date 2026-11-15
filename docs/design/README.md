# OpenPolitics design: "Ledger"

> **Superseded in part (2026-11-15):** after reviewing this spec the user asked for Democracy 4's structure instead. The palette,
> frame, map and dialogs now follow `addendum-democracy4.md`; Ledger's display scaling, size classes, theme-variation approach, slider
> logic and chart rules remain. See decision record 0012.

The third visual direction for the Godot client, written as a spec for the developer who builds it. It replaces the
navy/cyan glass look and the dock-and-floating-panel layout.

| File | What it is |
|---|---|
| `design-language.md` | Tokens (colour, type, space, radii, elevation, icons), every component with states and StyleBoxFlat values, motion, responsive rules, accessibility. |
| `layout.md` | What Democracy 4 actually does and what we borrow; the frame (top bar, left rail, stage, inspector); wireframes for regular / compact / wide; policy inspector, briefing, Politics page, other pages, start screen; mapping from every current client panel. |
| `design-system.html` | Static proof. Open it in a browser. `?view=main` renders the live main screen (resize the window to see it reflow), `&sel=minimum_wage` selects a policy, `&brief=1` opens the briefing, `?view=start` shows the start screen, `?sec=5` shows one sheet section. No network. Inter loads from `local("Inter")` or `../../client-godot/assets/fonts/InterVariable.ttf` once bundled; until then it falls back to Noto Sans. |
| `shots/` | Exact-size PNGs rendered from the HTML: `main-regular-1280x720*.png`, `main-compact-980x560*.png`, `main-wide-1720x720.png`, `main-tall-1280x960.png`, `main-briefing-1280x720.png`, `start-*.png`, `sheet-1…5-*.png`. |

## The concept in one paragraph

Briefing papers on a minister's desk. Warm paper surfaces (`#FAF9F6` on a `#ECE9E2` stone ground), near-black warm ink
(`#1A1917`), hairlines instead of boxes, one highlighter: ballot yellow `#FFD23F`, used only for "the one thing that matters
now" (End quarter, the selected node, selected rows). Inter with tabular figures everywhere; figures big and ink, labels small
and quiet. Layout: a top info bar, a left rail with navigation **and** permanent voter approval (Democracy 4's sidebar), the
map as the stage, and a docked right inspector where policies are changed while the map reacts next to it.

## 10 key decisions

1. **Light warm paper, not dark.** Every dark-navy game UI looks alike and the user called ours "fake modern blue". Positive
   polarity reads faster in dense tables and long Czech text; the warm off-white is ~15 % less bright than white, and the genre
   itself (Democracy 4) is light. A warm-graphite night set is specified but not built first.
2. **One accent, rationed.** Ballot yellow fills the End-quarter button and marks selection (`#FFF1BF` tint rows, yellow halo on
   the selected node, yellow slider thumb while dragging). Primary actions inside panels are ink-black buttons. Scarcity is what
   makes the CTA unmissable without glow.
3. **Inter 4, one family, tabular figures, `cv05`/`cv08`.** Complete Czech diacritics, real `tnum`, unambiguous `Il1`, optical
   sizes; it is what modern product UIs use. Type scale tuned for 1280 × 720: body 14, labels 13, captions 12, never below 11.
4. **No floating panels, no dock.** Regions are docked and share hairline edges: top bar 52, left rail 232/256/64, stage, inspector
   360/400 (drawer 340 in compact). The bottom dock and glass panels are deleted. Pages (Dashboard, Policies, Voters, Politics,
   Party, News) replace the map in the stage; Esc/M returns to the map.
5. **Voter approval is always on screen** in the left rail (share bar + diverging happiness bar per group), and clicking a group
   turns the map into that group's lens. Directly borrowed from Democracy 4, where this is the main feedback loop.
6. **Policies are changed in a docked inspector, not a modal**, so the map stays visible and highlights what the move touches.
   The inspector shows the slider, cost, time to effect, "what it does" and "who wants it" together, like D4's policy screen.
7. **A purpose-built policy slider** (custom `Control`, not a skinned `HSlider`): category fill for the value in force, stripes for
   the scheduled change, ink tick for "now", white target thumb, dotted track beyond what political capital can buy (D4's segmented
   affordability ring made linear).
8. **The map is laid out, not scattered.** Policies on a ring in three category arcs (taxes top, spending right, regulation left),
   barycentrically ordered to cut crossings; indicators force-laid inside; a greedy label placer with priorities hides what would
   collide and reveals it on hover/zoom. Edges are quiet ink at rest and turn green/red with flowing dashes on focus (D4's moving
   arrows, speed = strength).
9. **Size classes on logical width, plus an auto UI factor.** compact < 1100 ≤ regular < 1600 ≤ wide; short < 640, tall ≥ 860.
   Small windows shrink the logical viewport toward 960 × 540 instead of shrinking glyphs (formula in `layout.md` 1.2), so text
   never drops below ~1:1 physical size.
10. **Colour never alone, and validated.** Good/bad always carry ▲▼/±; categories carry glyphs; parties carry names; pending changes
    carry stripes. Text tokens ≥ 4.5 : 1 on every allowed surface; the 8-colour chart palette was tuned with a Machado-2009 CVD
    simulation to min ΔE76 15.4 / 22.9 / 17.6 (protan / deutan / tritan) at ≥ 3 : 1 contrast; party colours are fixed per id.

## Implementation order (for the Godot developer)

1. **Tokens and fonts.** Bundle `InterVariable.ttf` in `client-godot/assets/fonts/`. Rewrite `AppTheme` around the tokens in
   `design-language.md` §1-3: colours as `static readonly Color`, `FontVariation`s per text role (weight axis + `cv05`/`cv08`, a `Figures`
   variant with `tnum`), and a single `Theme` built in code that sets the default font/size and the StyleBoxes for `Button`, `LineEdit`,
   `OptionButton`/`PopupMenu`, `CheckBox`/`CheckButton`, `ScrollBar`, `TooltipPanel`, `PanelContainer`, `HSeparator`. Add theme type
   variations (`ButtonPrimary`, `ButtonGhost`, `ButtonDanger`, `ButtonCta`, `Card`, `Inset`, `Chip`…) and use `ThemeTypeVariation` on nodes
   instead of per-node overrides. Delete `Glass()`, `MapGlow`, cyan/navy.
2. **Display and size classes.** Extend `DisplaySettings`: the auto factor formula, window mode (windowed / borderless / fullscreen),
   reduced motion; a `Layout` singleton that computes `Compact/Regular/Wide` and `Short/Normal/Tall` on resize and raises an event.
3. **Frame.** New `Main` tree: TopBar, LeftRail, Stage, Inspector, ModalLayer, PopupLayer (`layout.md` §1). Remove the dock and the
   floating panel host. Wire rail navigation and keyboard shortcuts.
4. **Top bar** with HUD chips (priority overflow into "+N"), capital chip, 4Q, End quarter CTA with disabled-while-deciding state.
5. **Left rail**: nav, approval rows (approval bar component), government card; compact icon rail with sparkbars.
6. **Map rewrite** in `WorldMap`: ring-and-sector layout, barycentric order, indicator force layout (cached per pack + aspect), new
   node drawing, label placer, edge focus with dash flow, zoom/pan/fit, lenses. Selection raises an event instead of drawing its own card.
7. **Inspector** with the `PolicySlider` control, effects list, who-wants-it bars, indicator and voter-group variants; compact drawer.
8. **Briefing modal** (decisions first, option cards), toasts, chart modal with the new chart styling.
9. **Pages**: Politics (hemicycle 4.20, compass 4.21, polls, coalition, demands), Policies table, Dashboard tiles, Voters, Party (tabs),
   News.
10. **Start screen** with the display card; then motion polish (§5) and the end-quarter sequence.

Check each step with `scripts/client-smoke.sh <dir>` at 1280 × 720, 1720 × 720, 1280 × 960 and with UI scale 130 % (compact), and
compare with `shots/`.

## Risks in Godot and the pragmatic fallback

| Risk | Why it is hard | Fallback |
|---|---|---|
| Variable font axes and OpenType features | `FontVariation.VariationOpentype` / `OpentypeFeatures` work, but `tnum`/`cv05` need the exact 4-letter tags and are ignored silently if wrong. | Use the static TTFs (`Inter-Regular/Medium/SemiBold/Bold`) and keep `OpentypeFeatures = {"tnum":1}` on figure labels; verify by rendering "1111" vs "8888" widths in a smoke test. |
| Tabular figures in mixed labels | A `Label` uses one font; a value with a unit needs two styles. | Two labels in an `HBoxContainer` with `alignment` baseline approximated by equal line heights, or a `RichTextLabel` with `[font]` tags for the unit. Prefer two labels (cheaper, crisper). |
| Fractional scaling makes 1 px hairlines blurry | `canvas_items` stretch at e.g. 1.31× puts hairlines between pixels. | Draw hairlines and borders at `Mathf.Max(1, round(1 * scale)) / scale` logical px, or snap with `snap_2d_transforms_to_pixel`; keep borders on StyleBoxes (they are snapped) rather than `DrawLine`. |
| Single shadow per StyleBoxFlat | No layered shadows. | Values in `design-language.md` 3.4 are already single-shadow. |
| No backdrop blur | Godot has no cheap UI blur. | Not needed: the design uses no blur; the modal scrim is a flat 40 % ink. |
| Focus ring shows on mouse click | Godot draws `focus` for any focus change. | Track the last input device in `_Input`; when it was the mouse, call `ReleaseFocus()` after click, or swap the focus stylebox to `StyleBoxEmpty` until the next key press. |
| Czech line breaking and non-breaking rules | Autowrap breaks after one-letter prepositions and between number and unit. | Run all UI strings through a `CzechTypography.Fix()` helper that inserts U+00A0 after `v k s z o u a i` and before units; set `Label.Language = "cs"`. |
| Map label collisions at every size | Greedy placement must rerun when size, zoom, hover or selection changes; too slow if done every frame. | Recompute on those events only, cache text widths per font size, cap at ~120 labels; hide policy labels in compact unless prioritised (as specified). |
| Dash flow animation on edges | `DrawDashedLine` exists but has no offset. | Draw the dashed curve manually as short polyline segments with an offset that advances in `_Process`; reduce to static arrowheads when "reduce motion" is on or above 40 edges. |
| PopupMenu and OptionButton theming | Several theme items (`hover`, `separator`, `labeled_separator_left`, constants) must all be set or defaults show through. | Set every PopupMenu theme item listed in 4.4 in the Theme once; screenshot an open dropdown in the smoke test. |
| SpinBox arrows look default | The up/down icon is a single texture. | Build the stepper composite (4.5) from two icon buttons and a `LineEdit`; stop using `SpinBox` in UI. |
| Sizes on very wide or very tall screens | Pages could become line-length nightmares. | Max content width 1200 centred on pages; max text width 72 ch; the map fits its own rect with clamped ellipse aspect. |
| UI scale × small window | User 140 % on a 1366 × 768 laptop would make the logical viewport < 960 × 540. | Clamp the factor with `fMax` (`layout.md` 1.2) and show the resulting logical size in the display settings card. |

## What was not done

- No Godot code was changed (by design). The HTML is a faithful but simplified proof: map edges come from the real pack data,
  node values and health are mocked.
- Only the main screen, briefing and start screen are mocked in HTML; the Politics page, Policies table, Dashboard, Voters, Party and
  News pages are specified as wireframes in `layout.md` and their components appear in the sheet.
