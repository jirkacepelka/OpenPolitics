# OpenPolitics screen layout

Version 1, 2026-11-15. Tokens and components: `design-language.md`. Visual proof: `design-system.html`
(open `design-system.html?view=main` in a browser and resize it to see the main screen reflow).

## 0. What Democracy 4 actually does (study notes) and what we take

Sources: the ten Steam store screenshots of Democracy 4 (app 1410710, viewed full size), the gameplay guides on
gameplay.tips / gamepretty, Positech's developer blog (variable slider costs, 2020), plus Frostpunk 2 store screenshots.

**Main screen composition.** There is no geographic map. The whole screen is a light-grey field divided by thin dashed
radial lines into **pie-slice sectors** (Economy, Tax, Law and order, Welfare, Transport, Foreign policy, Public services),
each with its name set huge in faint grey type. Inside each sector sit **circular icons**: policies in grey/white, statistics in
blue, situations in red (bad) or green (good). Icon size encodes importance (GDP is a giant circle). Hovering an icon draws
**straight lines** to everything it touches, green for positive and red for negative, with **moving arrowheads whose speed
encodes strength**; everything else stays visible. Labels are small blue name tags under each icon. It is busy but it works
because the field is light and the lines appear only on hover.

**Chrome.** A **top bar** on the left shows finances (Income, Expenditure, Deficit, Debt) as label-over-figure pairs and a
long **turn progress bar**; on the right a **toolbar of square icon buttons** (Law and order filter, voter screens, finance,
ideas/new policies, achievements, cabinet, settings, election, reports) ending with the **Next turn** play button at the far
right edge. The **left sidebar** (about 13 % of width) is "Popularity": a group list (Capitalist, Socialist, Conservative,
Environmentalist, ... "Everyone") where each row has a **grey bar for the share of the electorate** and a **coloured bar for
happiness**; selecting a group colours the whole map by how that group feels about each policy.

**Policy detail.** A **centred modal over the dimmed map**: name and paragraph on the left; minister portrait with
effectiveness stats on the right; implementation delay and popularity as two bars; a **list of voter groups with centred
diverging bars** (who likes/dislikes it); a small **effect chart** (current vs final effect); at the bottom a **horizontal
slider** with a ghost "target" handle, cost and income readouts, and the political-capital cost to raise/lower/cancel;
three big buttons **Apply changes / Cancel policy / Revert changes** along the bottom edge.

**Other screens** (voter types, focus group, disposable income, electioneering, intelligence) are **tabbed modals** with a
row of pill tabs on top, one big visual (scatter of every voter, histogram, focus-group person) and dense tables.
**Dilemmas** appear in the quarterly report: two or three options you must pick before the turn can end. **Political capital**
sits in the centre of the map. The **election result** is a big bar chart with a "breaking news" ticker.

**What makes it work**: (1) the map is the game and is always one Esc away; (2) voter happiness is permanently visible on the
left; (3) cause and effect is explorable by hover, not by reading; (4) a policy is changed in one focused panel that shows
cost, popularity, effect and time together; (5) the turn is a single, always-visible button; (6) decisions block the turn so
nothing is missed. **What is dated**: modal-on-modal everywhere, 2010 pill buttons, saturated blue tags on every label, giant
sector watermarks, inconsistent tables, photos and illustrations as decoration.

**From the others**: Cities: Skylines keeps a thin **info bar on top** and turns the map itself into data with **info views**
(a lens recolours the world). Frostpunk 2 uses **hairline frames, small tracked uppercase labels and very little chrome** so
the world carries the screen. Victoria 3 shows how dense political data stays readable with **nested tooltips** and strict
tabular figures. Linear / Vercel / Raycast / Arc: a **persistent left sidebar** with navigation and live sections, a **right
inspector** that edits the selected thing, **command-first keyboard** use, hairlines instead of boxes, inverse tooltips.

**What we borrow, in structure**:
- Map as home, sectors by category, hover-to-explain with coloured moving arrows, arrow speed = strength (D4).
- Voter approval permanently in the left sidebar with share + happiness bars, and a **lens**: selecting a group recolours policy
  nodes by that group's opinion (D4 + Cities: Skylines info views).
- Policy changes in one place with the slider, its reachable range by political capital (D4's segmented ring), cost, time,
  who likes it, what it moves (D4) — but as a **docked inspector**, not a modal, so the map stays visible and reacts.
- Finances and key indicators in a thin top info bar; the single End-quarter button at the top-right edge (D4, C:S).
- Decisions block End quarter and are shown first in the briefing (D4 dilemmas).

## 1. Frame

### 1.1 Regions

```
┌──────────────────────────────── TOP BAR (52) ─────────────────────────────────┐
├────────────┬───────────────────────────────────────────────┬──────────────────┤
│ LEFT RAIL  │                MAIN STAGE                     │   INSPECTOR      │
│ nav +      │  Map (home) or a page: Dashboard, Policies,   │   (contextual,   │
│ approval + │  Voters, Politics, Party, News                │    docked right) │
│ coalition  │                                               │                  │
└────────────┴───────────────────────────────────────────────┴──────────────────┘
```

- **Top bar**: identity + date, live indicators, political capital, 4 quarters, End quarter.
- **Left rail**: navigation (7 destinations), then the always-visible **Approval** block (Everyone + voter groups), then the
  **Government** mini card (seats, next election, pending decisions).
- **Main stage**: the map, or one page. Pages replace the map in the stage; the map is never covered by floating panels.
- **Inspector**: opens when something is selected (policy, indicator, voter group, party) and edits it. On the map without a
  selection, on wide screens it shows **This quarter** (turn feedback). Docked (pushes the map) in regular/wide, overlay drawer in compact.

Godot tree (replaces the current `Main` HUD + dock + floating glass panel):

```
Main (Control, full rect)
└─ VBox
   ├─ TopBar (PanelContainer, min height 52)
   └─ HBox (expand)
      ├─ LeftRail (PanelContainer, width by class)
      ├─ Stage (Control, expand)   ← WorldMap or a Page; toasts anchor here
      └─ Inspector (PanelContainer, width by class; in compact reparented to an overlay layer)
ModalLayer (CanvasLayer 10): scrim + modal (briefing, chart, confirm, settings)
PopupLayer (CanvasLayer 20): dropdowns, tooltips
```

### 1.2 Scale and size classes

Godot computes `base = min(Wwin/1280, Hwin/720)`. Set `Window.ContentScaleFactor = f` where

```
auto  = clamp( min(Wwin/960, Hwin/540, 1.0) / base , 1.0, 1.4 )   // small windows: grow logical-px scale so text stays ≥ physical 1:1 down to 960x540
f     = auto * userScale                                            // userScale 0.8 … 1.4 from settings
fMax  = min(Wwin/960, Hwin/540) / base                             // never let the logical viewport drop below 960 x 540
f     = clamp(f, 0.5, max(1.0, fMax))
logical size = window size / (base * f)
```

Examples: 1920x1080 at 100 % → 1280x720 logical (×1.5). 2560x1080 (21:9) → 1720x720. 1600x1200 (4:3) → 1280x960.
980x560 window → auto 1.31 → 980x560 logical at 1:1. 1366x768 at UI 130 % → ~985x554 logical (compact).
Recompute on `GetTree().Root.SizeChanged` and on settings change; emit a `LayoutClassChanged` event when the class flips.

| Class | Logical width | Top bar | Left rail | Inspector | Map |
|---|---|---|---|---|---|
| compact | < 1100 | 48, reduced indicators | 64 icon rail | overlay drawer 340 (max 60 % W) | full stage |
| regular | 1100-1599 | 52 | 232 | docked 360 when something is selected, else closed | stage minus open inspector |
| wide | ≥ 1600 | 52, indicators get sparklines | 256 | always docked 400 (This quarter when nothing selected) | stage minus 400 |

| Height class | Logical height | Changes |
|---|---|---|
| short | < 640 | Rail: Government card collapses to one line; Approval rows 28 instead of 36; dashboard tiles 96 tall. |
| normal | 640-859 | As specified. |
| tall | ≥ 860 | Rail: adds "Průzkumy" (top 4 parties poll bars) under Government; map ellipse taller (aspect adapts); dashboard gets a third row; modals may grow to 720 tall. |

## 2. Main game screen

### 2.1 Regular, 1280 x 720 (inspector open on a policy)

```
x:0        232                                                      920                1280
┌──────────────────────────────────────────────────────────────────────────────────────────┐ y:0
│[≡] Q3 2022  │ HDP        RŮST     NEZAMĚSTN.  INFLACE   DEFICIT   NÁLADA │ ◉ 2,9 kap. ⏩ [▶ Ukončit čtvrtletí ↵]│
│    30/80 · volby za 14Q│ 5,42 bil ▲  4,2 % ▲  4,3 % ▼   2,8 % ▲   5,5 % ▲   −0,19 ▼│ +2,0/Q          (yellow, 40)│
├────────────┬───────────────────────────────────────────────────────┬─────────────────────┤ y:52
│ ◎ Mapa     │                    DANĚ A ODVODY 8                    │ ‹ REGULACE      [×] │
│ ▦ Přehled  │          ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯                              │ Minimální mzda      │
│ ≡ Politiky │      ◯                       ◯  ◯                     │ Jak vysoká je zákon-│
│ ☺ Voliči  │    ◯     ●   ●    ●     ●          ◯                  │ ná minimální mzda...│
│ ⌂ Politika ②│  ◯    ●    ●  ●     ●    ●          ◯  VÝDAJE 18     │ ─────────────────── │
│ ⚑ Strana   │  ◯      ●   ●    ●   ●     ●         ◯               │ 7,0 /10      ▲ +3,0 │
│ ▤ Zprávy   │REGULACE ◯   ●  ●    ●   ●    ●       ◯               │ ███▒▒▒▒◯━━━━┆┄┄┄┄┄  │
│ ────────── │  13   ◯    ●     ●   ●    ●          ◯               │ 0  teď 4,0      10  │
│ SCHVALOVÁNÍ│     ◯        ●    ●     ●         ◯                  │ Stojí 2,0 kap. ·    │
│ Všichni  ▬▬│        ◯ ◯                    ◯ ◯                     │ účinek za 2 Q       │
│ Nízkopř. ▬ │            ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯                        │ ─────────────────── │
│ Stř. tř. ▬▬│                                                       │ CO TO UDĚLÁ         │
│ Vysokopř.▬ │                                                       │ ▲ Nezaměstnanost +0,18│
│ Důchodci ▬▬│                                                       │ ▲ Inflace     +0,06 │
│ Mladí m. ▬ │                                                       │ ▼ Nerovnost   −0,15 │
│ Venkov   ▬ │                                                       │ KDO TO CHCE         │
│ ────────── │   [lens: Všichni ▾]              [−][+][⤢]            │ Nízkopř. ▬▬▬▶       │
│ VLÁDA      │                                                       │ ─────────────────── │
│ 117/200 ▬▬▬│                                                       │ [Zrušit] [Použít ↵] │
│ ② čeká     │                                                       │                     │
└────────────┴───────────────────────────────────────────────────────┴─────────────────────┘ y:720
```

Dimensions: top bar 0-52; rail x 0-232; stage x 232-920 (688 wide) with the inspector open, 232-1280 (1048) closed;
inspector x 920-1280 (360). Region borders are 1 px `line-subtle` on the shared edge.

**Top bar (52)**, `surface`, bottom hairline. Left to right:
- Menu icon button 32 (opens game menu: Save, Load, Settings, Quit to start) at x 12.
- Date block (min width 148): `heading` "Q3 2022" + `caption` `ink-3` "30/80 · volby za 14 Q". Click opens the History chart picker.
- Vertical divider.
- Indicator strip (expand, `HBoxContainer` with `alignment = Begin`, separation 4): HUD chips in priority order
  GDP(1), Unemployment(2), Deficit(3), Mood(4), Inflation(5), Growth(6). Each chip has min width 96 (compact labels 76).
  If the strip's width cannot fit all, chips with the highest priority number collapse into a "+2" ghost button that opens a
  popover with the rest (do not shrink fonts).
- Political capital chip (bordered, `surface-2` fill, radius 8, height 36): coin icon 16, `figure-l` "2,9", `caption`
  "+2,0/Q". Tooltip explains sources. When a pending change costs more than available, the chip shows a `bad` outline.
- 4-quarters icon button (⏩, tooltip "Odehrát 4 čtvrtletí").
- **End quarter** accent CTA, height 40, right margin 12. Disabled with label "Rozhodněte (2)" while blocking decisions exist;
  clicking it then opens the briefing at the decisions.

**Left rail (232)**, `surface`, right hairline, pad (12, 12):
- Nav list: 7 rows height 30 (no gap), icon 16 + `label` 13, radius 8. Selected: bg `accent-tint` + 2 px ink bar left + `ink` weight 600;
  hover `surface-2`. Count/attention badge right-aligned (Politics shows the red count of pending decisions; News shows unread).
  Destinations: Mapa (M), Přehled (1), Politiky (2), Voliči (3), Politika (4), Strana (5), Zprávy (6).
- Labelled divider "SCHVALOVÁNÍ" with a ghost "lens" toggle icon on the right.
- Approval list: "Všichni" + every voter group (6 now, scroll if more), row height 40 (pad 3/4, text line 16, gap 3, bar 8, share bar 2 + 3): icon 14, name `body-strong` 13 (one line,
  ellipsis + tooltip), value `data` right; under the text the approval bar (diverging, 4.12) full width with the grey share bar.
  Clicking a group selects it: the inspector shows the group, and the map switches to the **group lens** (policy nodes recolour:
  liked = `good` ring, disliked = `bad` ring, neutral = faded) until Esc or clicking "Všichni".
- Labelled divider "VLÁDA". Government card (inset, `surface-2`, radius 8, pad 10): "117 / 200" `figure-l` + "většina" status pill;
  seat meter (government share with majority tick); line "② rozhodnutí čeká" (link to Politics) when pending; "Volby za 14 Q".
- The rail scrolls as one column if the height runs out (short class).

**Stage: the map**, `canvas` with a 1 px dot grid (`#DAD6CD`, 24 px pitch). Overlay controls (all 8 px from stage edges, on
`surface` pills with `line` border, radius 8):
- bottom-left: lens dropdown ("Barvy: Kategorie / Podle skupiny / Změna za čtvrtletí / Zdraví ukazatelů").
- bottom-right: zoom out, zoom in, fit (⤢) icon buttons in one segmented group.
- top-left: a legend chip row (3 category chips + "Ukazatel") only when the map is first shown or on hover of a "?".
- Toast stack: bottom-centre.

**Inspector (360)**, `surface`, left hairline, pad 20, scrolls; header sticky (overline category with glyph, title, close).
Content by selection type (see 3).

### 2.2 Compact, ~980 x 560

```
x:0  64                                                                                980
┌─────────────────────────────────────────────────────────────────────────────────────────┐ y:0
│[≡] Q3 2022 │ HDP       NEZAM.   DEFICIT   NÁLADA  [+2]│ ◉ 2,9 │⏩│[▶ Ukončit Q]│
│    30/80   │ 5,42 ▲    4,3 % ▼  5,5 % ▲   −0,19 ▼     │       │  │ (yellow, 36)│
├────┬────────────────────────────────────────────────────────────────────────────────────┤ y:48
│ ◎  │                         DANĚ 8                             ┌──────────────────────┤
│ ▦  │             ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯                                │ DRAWER (340, overlay)│
│ ≡  │        ◯                       ◯ ◯                         │ Minimální mzda   [×] │
│ ☺  │     ◯      ●   ●    ●    ●          ◯                      │ 7,0 /10   ▲ +3,0     │
│ ⌂② │   ◯     ●    ●   ●    ●    ●          ◯                    │ ███▒▒▒◯━━━┆┄┄┄┄      │
│ ⚑  │   ◯       ●    ●    ●    ●           ◯ VÝDAJE 18           │ Stojí 2,0 kap.       │
│ ▤  │  REG ◯   ●   ●    ●    ●    ●        ◯                     │ CO TO UDĚLÁ          │
│────│   13   ◯     ●    ●    ●            ◯                      │ ▲ Nezaměstnanost …   │
│ ▮▮ │        ◯ ◯                    ◯ ◯                          │ ...                  │
│ ▮▯ │             ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯ ◯                            │ [Zrušit] [Použít ↵]  │
│ ▮▮ │  [lens ▾]                                     [−][+][⤢]    └──────────────────────┤
└────┴────────────────────────────────────────────────────────────────────────────────────┘ y:560
```

- Top bar 48; date block shows "Q3 2022" + "30/80" only; indicator strip shows 4 chips with short labels (HDP, NEZAM., DEFICIT,
  NÁLADA) and a "+2" overflow; capital chip shows only the figure; CTA label shortens to "Ukončit Q" (EN "End Q"), height 36.
- Left rail 64: nav icons only (32 hit area, tooltip with name and shortcut, badges as 8 px dots); below a divider, the **approval
  sparkbars**: one 6 x 28 px vertical diverging bar per group (centre line at 14) with the group icon 12 px under it, 2 per row,
  tooltip with name and value; clicking opens the Voters page. Government card collapses to "117" + majority dot.
- Map uses the full stage (916 x 512). Labels follow the compact rules in 2.3.
- Inspector is a **drawer**: slides over the map from the right, width 340 (max 60 % of W), drawer shadow, no scrim; the map
  re-fits into the part the drawer leaves uncovered (camera tween 260 ms), so the selected node and its links stay visible. Esc or × closes.
- Pages in compact use a single column (see 5-7).

### 2.3 Wide, 1720 x 720

```
x:0        256                                                                    1320              1720
┌─────────────────────────────────────────────────────────────────────────────────────────────────────┐
│[≡] Q3 2022 · 30/80 │ HDP ⌁ RŮST ⌁ NEZAM. ⌁ INFLACE ⌁ DEFICIT ⌁ NÁLADA ⌁ DLUH ⌁ │ ◉ 2,9 +2,0/Q │⏩│[▶ Ukončit čtvrtletí ↵]│
├────────────┬─────────────────────────────────────────────────────────────────┬──────────────────────┤ y:52
│ nav        │                                                                 │ TOTO ČTVRTLETÍ  Q3 22│
│ (labels)   │                      MAP (1064 x 668)                           │ ② Rozhodnutí čeká →  │
│            │                                                                 │ ─ Co se změnilo      │
│ SCHVALOVÁNÍ│       all labels visible; values under indicators               │ ▲ Inflace 2,8 % +0,1 │
│ (rows 36   │                                                                 │ ▼ Nálada −0,19 −0,01 │
│  + trend)  │                                                                 │ ─ Zprávy             │
│            │                                                                 │ • Lidová unie ztrácí │
│ VLÁDA card │                                                                 │   trpělivost: ...    │
│            │                                                                 │ ─ Sliby (3) …        │
└────────────┴─────────────────────────────────────────────────────────────────┴──────────────────────┘
```

- Indicator strip shows all chips incl. Debt ("DLUH") and 40 x 16 sparklines; date block in one line.
- Rail 256: approval rows show a trend glyph and the group's share as `micro` ("22 %").
- Inspector always docked at 400. With no selection it shows **This quarter**: pending decisions (top, red badge, button to
  open), "Co se změnilo" (top 6 indicator deltas, click selects the node), news (5 latest, link to News page), promise status.
  Selecting a node replaces it with the node's inspector and a "‹ Toto čtvrtletí" back link.
- Pages get a 3-column grid and max content width 1200 centred inside the stage.

### 2.4 Map composition and label rules

The map is drawn by `WorldMap` (keep the class, replace the drawing).

- **Fit rect**: stage rect minus 8 px, minus the label band: 132 px left/right, 36 px top/bottom (compact: 96 / 28).
- **Policy ring**: an ellipse inscribed in the fit rect, aspect clamped to 1.15-2.1 (tall screens get a rounder ring). The 39
  policies sit on the ring in three **sector arcs** in reading order: Taxes across the **top** (from 10 to 2 o'clock), Spending down
  the **right** side and bottom-right (2 to 7 o'clock), Regulation up the **bottom-left and left** (7 to 10 o'clock). Arc lengths are
  proportional to the count (8/18/13) with a 6° gap between sectors where the sector label sits outside the ring.
  Within a sector, order policies to minimise edge crossings: sort by the angle of the centroid of the indicators they affect
  (barycentric ordering; compute once per pack load).
- **Indicators**: inside the ring, in an inner ellipse at 0.68 of the ring size. Initial positions = centroid of connected policies
  pulled 40 % toward the centre; then 120 iterations of a simple force layout (repulsion between indicators with minimum distance
  = node 40 + label 2 lines (≈ 30 px) + 12 px gap, spring to their initial position). Compute once per pack and per aspect class,
  in unit coordinates, so resizing only rescales.
- **Node scale**: node diameter = base (policy 30, indicator 40) × clamp(fitHeight / 560, 0.8, 1.2). Labels do not scale.
- **Zoom/pan**: mouse wheel zooms 0.8-2.0 around the cursor, drag pans, double-click a node centres it, ⤢ fits. Zoom resets on resize
  (fit). Keyboard + / − / 0.
- **Labels** (`caption` 12, max 116 px, 2 lines, canvas outline 3 px):
  1. Policy labels are placed outward along the normal of the ring; left half right-aligned, right half left-aligned, top/bottom
     centred above/below. Indicators: centred below the node.
  2. Each frame where layout changes (not every frame), run a greedy placement: sort labels by priority — selected 100, hovered 90,
     neighbours of hovered/selected 80, changed this quarter 60, indicators 50, policies 40 — then place each label rect; if it
     intersects an already placed label rect or a node circle (inflated by 2 px), try the alternative anchors (indicator: below →
     above → right → left; policy: outward → outward shifted ±8 px along the ring), else hide it.
  3. A hidden label is shown on hover as a tooltip-style tag; zooming in reveals more labels automatically (same algorithm).
  4. Compact: policies show labels only if priority ≥ 60 (selected, hovered, neighbours, changed); indicators always try.
  5. Ellipsis is the last resort for a single very long word; full name in the hover tooltip.
- **Edges**: drawn under nodes; only for the hovered/selected node at full strength (see design-language 4.22); with nothing
  focused, all edges at 10 % ink so the web is visible but quiet. In the "Změna za čtvrtletí" lens, edges that carried the
  biggest changes last quarter are drawn at 35 %.
- **Lens modes**: Category (default), Voter group (policy rings by group opinion, indicators unchanged), Change (nodes sized/marked
  by last quarter's change), Health (indicators ringed by good/bad, policies faded).
- The current `_insetLeft` hack goes away: the map always fits its own stage rect.

## 3. Inspector (policy detail / slider panel)

Width 360 (regular) / 400 (wide) / 340 drawer (compact). Pad 20. Sticky header; scrolling body; sticky footer.

```
┌──────────────────────────────────────┐
│ ⚖ REGULACE                  [⤢] [×]  │  overline in category colour + glyph; ⤢ = open chart; × = close
│ Minimální mzda                       │  title 20
│ Jak vysoká je zákonná minimální mzda │  body ink-2, max 3 lines + "Více" link
│ (0–10). Pomáhá nejchudším, …         │
├──────────────────────────────────────┤
│ Minimální mzda          7,0 /10 ▲+3,0│  PolicySlider (64)
│ ████████▒▒▒▒▒▒◯━━━━━━━━━━┆┄┄┄┄┄┄┄┄┄  │
│ 0      teď 4,0        do 8,5      10 │
│ [Pevně ▾]  sleduje: HDP / inflaci /… │  indexation dropdown (spending policies only)
│ Stojí 2,0 kap. · rozpočet −12,4 mld. │  caption
│ Účinek za 2 čtvrtletí                │
├──────────────────────────────────────┤
│ CO TO UDĚLÁ          [za rok ▾]      │  overline + range dropdown
│ ▲ Nezaměstnanost          +0,18 p.b. │  list rows 32: direction glyph + indicator + value (bad colour)
│ ▲ Inflace                 +0,06 p.b. │
│ ▼ Nerovnost               −0,15      │  (good colour)
│ Rozpočet                  −12,4 mld. │
├──────────────────────────────────────┤
│ KDO TO CHCE                          │
│ Nízkopříjmoví   ◀──────┼───▶  +0,21  │  diverging bars (approval delta)
│ Podnikatelé     ◀───┼          −0,30 │
│ …                                    │
├──────────────────────────────────────┤
│ ! Jen vedoucí strana vlády může      │  warn status block (if applicable), inset warn-tint
│   měnit politiky.                    │
├──────────────────────────────────────┤
│ [Vrátit]              [Použít ↵]     │  sticky footer: ghost Revert (Esc) + Primary Apply (Enter)
└──────────────────────────────────────┘
```

- Apply is the **ink primary**, not yellow (yellow is reserved for End quarter). After applying, the slider's pending stripes show
  the scheduled change and a toast confirms "Minimální mzda → 7,0 od Q1 2023".
- The "Co to udělá" list replaces `PolicyEffectsPanel`'s infographic: same data, as rows with glyph, name, value; hovering a row
  highlights that indicator on the map (the map is visible next to the inspector, which is the point of docking).
- Indicator inspector: overline "UKAZATEL", title, big value `figure-xl` + delta; chart (3 years, single series ink, 160 tall) with
  range segmented control; "Proč se to hnulo" list (top contributors last quarter, ± values, click selects source node);
  "Ovlivňuje" list.
- Voter group inspector: share, approval with trend chart, "Co se jim líbí / nelíbí" (top 5 policies each), membership note.
- Party inspector (from Politics page or compass): seats, poll trend, position, relation to the player, demands.

## 4. Start-of-quarter briefing and decisions (modal M 560, or L 800 when there are 2+ decisions)

```
┌──────────────────────────────────────────────────────────────┐
│ ČTVRTLETNÍ BRIEFING · Q3 2022                          [×]   │  × disabled while decisions are pending
│ Lidová unie odešla z vlády                                   │  title = the most important headline (generated)
├──────────────────────────────────────────────────────────────┤
│ ROZHODNUTÍ (1)                                               │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ ⚑ Občanské hnutí nabízí koalici                          │ │  decision card (inset surface-2, radius 12, pad 16)
│ │ Získali byste 50 mandátů a většinu 117/200. Chtějí:      │ │
│ │ ┌───────────────────────┐ ┌───────────────────────┐      │ │
│ │ │ Přijmout              │ │ Odmítnout             │      │ │  option cards (D4 dilemma style), 2-3 per row
│ │ │ + většina 117/200     │ │ − menšinová vláda     │      │ │  each lists consequences with ▲▼ rows
│ │ │ − Daň z příjmu ≤ 21 % │ │ + bez ústupků         │      │ │  selected option: ink border + accent-tint fill
│ │ └───────────────────────┘ └───────────────────────┘      │ │
│ └──────────────────────────────────────────────────────────┘ │
│ CO SE ZMĚNILO                                                │
│ ▲ Inflace 2,8 % (+0,1)   ▼ Nálada −0,19 (−0,01)   ▲ Deficit…│  3-column grid of delta rows (2 in M width)
│ ZPRÁVY                                                       │
│ • Lidová unie ztrácí trpělivost: Důchody ≥ 10 % HDP …   (!)  │  news rows: icon by type, status pill for ultimatums
│ • Lidová unie dává ultimátum (4 Q): …               [Ultimátum]│
│ • …                                       Zobrazit vše (12) → │
├──────────────────────────────────────────────────────────────┤
│                                   [Na mapu]  [Pokračovat ↵]  │  Continue = primary, enabled when all decisions are chosen
└──────────────────────────────────────────────────────────────┘
```

- Order: decisions → what changed → news (max 5, rest behind "Zobrazit vše", which opens the News page).
- A decision option is a selectable card (radio semantics, arrow keys move, Enter confirms).
- Compact: modal width `min(560, W - 32)`, option cards stack vertically, the changed grid becomes 1 column.
- No decisions and fewer than 3 news items: skip the modal, show a toast with "Zobrazit" instead (less interruption).

## 5. Politics page (stage page; replaces the Politics glass panel)

Regular (stage 1048 x 668 with inspector closed), page pad 24, 12-col grid, gap 12:

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ Politika                                           [Parlament|Průzkumy|Koalice] │  title + segmented anchors (scroll to)
├──────────────────────────────────────┬──────────────────────────────────────────┤
│ PARLAMENT                (6 col)     │ PRŮZKUMY                  (6 col)        │
│        ●●●●●●●●●●●●●                 │ Občanské hnutí ★  ███████████▏ 25,8 % 50 │  seat bars + threshold line
│     ●●●●●●●●●●●●●●●●●●●              │ Dělnická levice   ██████▏      16,8 % 32 │
│   ●●●●●●●●●  117  ●●●●●●●●           │ Občanská pravice  ████▏        12,2 % 21 │
│  ●●●●●●●●●  / 200 ●●●●●●●●●          │ Sociální aliance  ████▏ (vy)   11,3 % 25 │
│  ▔▔▔▔▔▔▔▔▔▔ vláda ▔▔▔▔▔▔               │ …                    ┆5 %              │
│ legend (gov first, divider, opp.)    │ [graf vývoje →]                          │
├──────────────────────────────────────┼──────────────────────────────────────────┤
│ KOALICE / SESTAVENÍ VLÁDY (6 col)    │ KOMPAS                    (6 col)        │
│ status: Vláda Lidové unie · 117      │  ┌────────────────────┐                  │
│ partner rows: loyalty meter, demands │  │   ·        ●       │                  │
│ [formation: offers / invite buttons] │  │ ●     ◉(vy)    ●   │  square 1:1       │
│                                      │  └────────────────────┘                  │
├──────────────────────────────────────┴──────────────────────────────────────────┤
│ POŽADAVKY A ULTIMÁTA (12 col): table rows: party · demand · deadline pill · status│
└─────────────────────────────────────────────────────────────────────────────────┘
```

- Wide: three columns — Parliament (4) · Polls (4) · Compass (4); then Coalition (6) + Demands (6).
- Compact: one column in the order Parliament, Coalition (with pending decisions on top), Polls, Compass, Demands. Hemicycle width =
  column width; compass max 320.
- Coalition formation (offers, accept/reject, invite) lives in the Coalition card; pending decisions are also mirrored in the briefing.
- Clicking a party anywhere opens the party in the inspector.

## 6. Other pages

All pages: `surface` background, pad 24 (16 compact), title row 48 (`title` + right-aligned page actions), content max width 1200.

- **Přehled (Dashboard)** — replaces Overview. Stat tiles (4.11) grid: 6 columns wide, 4 regular, 3 compact (tile min 168);
  rows: macro (GDP, growth, unemployment, inflation, deficit, debt), society (mood, health, education, crime, environment,
  inequality), politics (party polls mini-chart tile 2 cols wide, partner loyalty, voter groups mini). Click → chart modal (XL).
  Below: "Události" (events) as a list.
- **Politiky (Policies)** — replaces the 39 cards. Toolbar: search field (240, `/`), category segmented control with counts,
  sort dropdown (Kategorie / Název / Změna / Náklady). Table grouped by category (group header rows): columns Name (icon +
  name), Value (`data` + unit + a 64 px meter in category colour), Target (if pending: "→ 7,0 v Q1 23" with stripes chip),
  Budget (± mld./rok), Popularity (mini diverging bar). Row click selects the policy → inspector with the slider. Compact hides
  Budget and Popularity columns. Keyboard: Up/Down through rows, Enter focuses the slider.
- **Voliči (Voters)** — table of groups: share, approval bar with trend, likes/dislikes (top 2 chips each), cynicism; select →
  inspector. Above the table a 100 % stacked bar of the electorate by group.
- **Strana (Your party)** — tabs (4.3): Přehled (capital, members, stats, compass mini) · Program (platform: compact policy sliders
  with priority stepper per row, grouped by category) · Sliby (promises before elections: list with status pills, add button) ·
  Vztahy (relations: party rows with meter) · Akce (actions as cards with cost and button).
- **Zprávy (News)** — reverse-chronological feed grouped by quarter (sticky quarter headers), filter chips (Vše / Vláda / Strany /
  Ekonomika / Ultimáta), unread dot. Briefing archive.
- **Chart modal** — XL modal: title, range segmented control, series toggles, chart full width (min height 320), crosshair readout.

## 7. Start screen with display settings

Full window, `canvas` background with the map's dot grid and a faint static web drawing (code-drawn, ink 6 %) on the right half.

Regular 1280 x 720:

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│  OpenPolitics                                                                    │  display 32, top-left at (64, 64)
│  Politická simulace. Veďte stranu, udržte většinu.                               │  body ink-2
│                                                                                  │
│  ┌ NOVÁ HRA ───────────────────────────────┐  ┌ NASTAVENÍ ZOBRAZENÍ ───────────┐ │
│  │ Scénář                                  │  │ Režim okna                     │ │
│  │ ┌──────────────────┐ ┌────────────────┐ │  │ [Okno|Bez rámečku|Celá obr.]   │ │
│  │ │ Česko 2015       │ │ Ekonomický     │ │  │ Velikost rozhraní     110 %    │ │
│  │ │ koaliční vláda ● │ │ sandbox        │ │  │ ──────◯────────  80–140 %      │ │
│  │ └──────────────────┘ └────────────────┘ │  │ [✓] Automaticky podle okna     │ │
│  │ Jazyk [Čeština|English]                 │  │ [ ] Omezit animace             │ │
│  │ Volební práh  5 %  [−][ 5 ][+]          │  │ Náhled: 1280 × 720 logicky     │ │
│  │ Seed  [ 650 ] [⟳]   (Pokročilé ▸)       │  └────────────────────────────────┘ │
│  │                    [Načíst]  [▶ Začít ↵]│                                     │
│  └─────────────────────────────────────────┘                                     │
│  Všechna data jsou ilustrativní, strany jsou smyšlené.            v0.x · F11     │
└──────────────────────────────────────────────────────────────────────────────────┘
```

- New game card 7 columns (min 440), display card 4 columns (min 300), gap 24; both `surface`, radius 16, pad 24, hairline border.
- Scenario choice = selectable cards (radio), not a dropdown. "Začít" is the yellow accent CTA here (the only one on this screen).
- Display settings apply live (window mode via `DisplayServer.WindowSetMode`, UI scale via `ContentScaleFactor` with the formula in
  1.2); the preview line shows the resulting logical size and class ("kompaktní / běžné / široké").
- The same Display card is reused in the in-game Settings modal (M).
- Compact: cards stack in one column (scroll), title block shrinks to `title`.
- Wide: content max width 1200, centred.

## 8. Mapping from the current client

| Current (client-godot) | New home | Notes |
|---|---|---|
| `Main.BuildHud` top bar (date, 6 indicators, capital, save, menu, 4Q, End quarter) | **Top bar** | Save moves into the menu (Ctrl+S). Indicator priority/overflow rules 2.1. |
| `Main.BuildDock` (bottom dock) | **Left rail nav** | Delete the dock. |
| `Main.BuildPanel` floating glass panel host | **Stage pages** + **Inspector** | Delete `AppTheme.Glass`, the panel slide-in and the map inset. |
| `OverviewScreen` (stats + chart tiles, events) | **Přehled** page | Tiles become stat tiles 4.11. |
| `PoliciesScreen` (39 cards, category filter, inspector, slider, indexation, apply) | **Politiky** page table + **Inspector** (policy) | `PolicySlider` replaces the HSlider; indexation becomes a dropdown in the inspector. |
| `PolicyEffectsPanel` (infographic) | Inspector "Co to udělá" + "Kdo to chce" | Same data from the core preview. |
| `VotersScreen` | Rail Approval block (always) + **Voliči** page + voter inspector + map lens | |
| `PoliticsScreen` (hemicycle, polls, compass, coalition formation) | **Politika** page | Hemicycle 4.20, compass 4.21. |
| `PartyScreen` (capital, platform + priority, promises, relations, actions) | **Strana** page with tabs | Platform uses the compact policy slider. |
| `NewsScreen` | **Zprávy** page + This quarter (inspector, wide) + briefing | |
| `TurnPopup` (quarterly briefing) | **Briefing modal** (section 4) | Decisions first, option cards. |
| `ChartDialog`, `Chart`, `Charts` | **Chart modal** + chart styling 4.19 | `Charts.PaletteColor` → chart palette 1.7. |
| `WorldMap` + node detail card | **Stage map** + **Inspector** (indicator/policy) | Delete the internal `_card`; selection goes to the inspector. |
| `Hemicycle`, `CompassView` | Restyled per 4.20 / 4.21 | |
| `Icons` + `IconBadge` | Keep pictograms, new stroke rules; delete tinted badge circles | |
| `StartBackdrop` + start form | **Start screen** (section 7) | Add display settings card. |
| `DisplaySettings` | Add window mode (3 options), auto factor, reduced motion | Formula 1.2. |
| `AppTheme` | Rewrite with the Ledger tokens | Theme built once in code; see README build order. |

Delete outright: the bottom dock, glass panels and `Glass()` style, the cyan accent and navy palette, `MapGlow`, the start screen's
floating dots backdrop animation, `IconBadge` circles, the policy card grid, centred "Continue" cyan button styling.
