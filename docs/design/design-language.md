# OpenPolitics design language: "Ledger"

Version 1, 2026-11-15. Applies to the Godot client (`client-godot/`). Companion files: `layout.md` (screens),
`design-system.html` (visual proof), `README.md` (decisions, build order, risks).

## 0. Point of view

OpenPolitics is a game about reading a country. The interface should feel like the **briefing papers on a minister's desk**,
not like a sci-fi cockpit: warm paper, black ink, one highlighter. Every number is set like a figure in a financial paper
(tabular, aligned, never glowing). Colour is information, never decoration: if something is coloured, it either belongs to a
policy category, a party, a chart series, or it is good/bad news.

Three rules that define the look and that every screen must obey:

1. **Paper and ink.** Light warm surfaces, near-black warm ink, hairline borders. No gradients, no glow, no blur, no glass.
2. **One highlighter.** The signature accent is ballot yellow `#FFD23F`. It marks *the one thing that matters now*: the End
   quarter button, the selected node, the selected row, the slider target. Never more than one large yellow area on screen.
3. **Figures first.** Numbers are the heroes: big, tabular, semibold, ink. Labels are small and quiet. Units are smaller than
   the figure they belong to.

What we deliberately do **not** do (this is what made the last two versions read as "2005 pretending to be modern"):
navy surfaces, cyan accents, glow, glassmorphism, floating panels with deep blur shadows, gradients on buttons, icon badges
inside tinted circles everywhere, all-caps bold titles, centre-aligned dialogs with a giant cyan button.

### Why light, for a game played for hours

- Data-heavy reading (tables, charts, long Czech sentences) is faster and less tiring in positive polarity (dark text on light),
  as long as the surface is not pure white and not too bright. Our base is warm off-white (L* ~97) and the map ground is a
  darker stone (L* ~92), which cuts glare by roughly 15 % against `#FFFFFF`.
- It is the genre's own vocabulary: Democracy 4's main screen is light grey with white policy icons. We keep that familiarity
  and make it precise instead of busy.
- It instantly separates us from the sea of dark-navy strategy UIs, which is exactly the user's complaint.
- A warm-graphite "Night" token set exists (section 1.9) for a later theme toggle; it is not the default and is not blue.

---

## 1. Colour

All values are sRGB hex. Godot: `new Color("FAF9F6")`. Alpha variants are written `#RRGGBBAA`.

### 1.1 Surfaces (4 levels)

| Token | Hex | Use |
|---|---|---|
| `canvas` | `#ECE9E2` | The map ground, the app background behind pages, sunken containers (segmented control track). |
| `surface` | `#FAF9F6` | Default panel / page / card / modal fill. Charts always sit on this. |
| `surface-2` | `#F2F0EB` | Inset areas inside a panel, hover fill of rows and secondary buttons, table header. |
| `surface-3` | `#E6E3DC` | Pressed fill, slider/meter track, disabled button fill. |
| `field` | `#FFFFFF` | Text inputs and number fields only (a white field on paper reads as "type here"). |
| `ink-surface` | `#1A1917` | Inverse surfaces: tooltips, toasts, the primary button. |

### 1.2 Borders

| Token | Hex | Use |
|---|---|---|
| `line-subtle` | `#E0DDD5` | Hairline between rows, card borders, dividers. |
| `line` | `#D3CFC6` | Default control border (secondary button, input, dropdown). |
| `line-strong` | `#B9B4A9` | Hover border, axis lines, tick marks. |
| `focus` | `#1A1917` | 2 px focus ring (see 9). |

### 1.3 Text (ink)

| Token | Hex | On `surface` | On `surface-2` | On `canvas` | Use |
|---|---|---|---|---|---|
| `ink` | `#1A1917` | 16.7 : 1 | 15.4 : 1 | 14.5 : 1 | Figures, titles, body. |
| `ink-2` | `#57534C` | 7.3 : 1 | 6.7 : 1 | 6.3 : 1 | Secondary text, descriptions, unselected tabs. |
| `ink-3` | `#6B665E` | 5.4 : 1 | 5.0 : 1 | 4.7 : 1 | Captions, axis labels, overlines, units. |
| `ink-disabled` | `#A9A49A` | 2.4 : 1 | | | Disabled labels only (exempt from AA). |
| `ink-inverse` | `#F5F2EB` | 15.7 : 1 on `ink-surface` | | | Text on ink surfaces. |
| `ink-inverse-2` | `#B8B2A7` | 8.4 : 1 on `ink-surface` | | | Secondary text in tooltips/toasts. |

All text tokens pass WCAG AA (4.5 : 1) on every surface they are allowed on. `ink-3` is the floor: nothing readable is lighter.

### 1.4 Signature accent: ballot yellow

| Token | Hex | Use |
|---|---|---|
| `accent` | `#FFD23F` | Fill of the End quarter button; selected-node halo; slider target thumb when dragging; toast action text on ink. |
| `accent-hover` | `#FFC81A` | Hover of accent fills. |
| `accent-pressed` | `#F2B900` | Pressed accent fill. |
| `accent-tint` | `#FFF1BF` | Selection highlight: selected list row, selected policy in a table, the "now" quarter column. |
| `accent-ink` | `#1A1917` | Text/icons on any accent fill (12.2 : 1 on `accent`, 15.6 : 1 on `accent-tint`). |

Rules: yellow is **never** text or a thin line on paper (1.4 : 1, invisible). Any yellow shape on paper carries a 1 px `ink` outline
or sits under ink text. At most one accent-filled control per screen (End quarter). `accent-tint` may appear in several rows
because it means "selected".

### 1.5 Semantic

| Token | Hex | Tint (bg) | Contrast on `surface` | Paired glyph (colour is never alone) |
|---|---|---|---|---|
| `good` | `#1E7A46` | `#E3F0E6` | 5.1 : 1 | ▲ for "up and good", ▼ for "down and good", `+` |
| `bad` | `#C8352B` | `#F8E2DE` | 5.0 : 1 | ▲/▼ same shapes, `−` |
| `warn` | `#A15200` | `#F6E8D6` | 5.4 : 1 | `!` triangle |
| `info` | `#57534C` (= `ink-2`) | `#F2F0EB` | 7.3 : 1 | `i` circle |

Arrows show **direction** (up/down), colour shows **desirability**. Inflation rising is a red ▲, unemployment falling is a green ▼.
Under deuteranopia good vs bad keeps ΔE76 ≈ 10, and the glyph plus sign carry the meaning anyway.

### 1.6 Policy categories

| Category | Token | Hex | Tint | Glyph | Contrast on `surface` / `canvas` |
|---|---|---|---|---|---|
| Taxes and contributions | `cat-tax` | `#8B3D8C` (plum) | `#F1E4F0` | `%` in a circle | 6.3 / 5.5 |
| Spending | `cat-spend` | `#0D6E78` (petrol) | `#DDEDEE` | coin-out | 5.7 / 4.9 |
| Regulation | `cat-reg` | `#8A6410` (ochre) | `#F3EAD3` | scales | 5.1 / 4.4 (graphics only on canvas) |

Validated with the Machado 2009 CVD simulation: minimum pairwise ΔE76 = 68 (normal), 27.5 (protan), 10.5 (deutan), 16.3 (tritan).
Category is always shown with the glyph as well (map sector label, chip, table group header).

Indicators (the ~25 "state of the country" nodes) have no category colour: they are `ink` on `surface`, and their ring shows
state (`good`/`bad`/`line-strong` for neutral).

### 1.7 Categorical chart palette (8 series)

For multi-series charts only, always drawn on `surface` (`#FAF9F6`). Single-series charts are drawn in `ink`.

| # | Name | Hex | Contrast on `surface` |
|---|---|---|---|
| 1 | Cobalt | `#1D3F9E` | 8.8 |
| 2 | Amber | `#D9730D` | 3.1 |
| 3 | Emerald | `#0B9A5E` | 3.4 |
| 4 | Orchid | `#C860B0` | 3.5 |
| 5 | Umber | `#86561C` | 5.9 |
| 6 | Aubergine | `#5A2E7A` | 9.5 |
| 7 | Cornflower | `#3F7FE0` | 3.8 |
| 8 | Oxblood | `#7E1F33` | 9.4 |

All 8 meet the 3 : 1 non-text contrast of WCAG 1.4.11 on `surface`. Minimum pairwise ΔE76 (Machado CVD, severity 1.0):
**22.1 normal, 15.4 protan, 22.9 deutan, 17.6 tritan** (validator: Python script in the scratchpad, method in README). Series
order is fixed: the first series of a chart is always Cobalt, etc. Lines also get direct end labels (≤ 4 series) or marker
shapes (circle, square, triangle, diamond, then repeat hollow) when printed small.

### 1.8 Party colours (fictional parties, fixed by id)

Parties are coloured from the chart palette with a fixed mapping so a party is the same colour everywhere (hemicycle, polls,
compass, charts). Do not derive it from the sorted index any more (`Ui.PartyColor`): use this table, and fall back to the
palette order for parties a mod adds.

| Party id | Colour | Hex |
|---|---|---|
| `base:citizens_movement` | Cornflower | `#3F7FE0` |
| `base:social_alliance` | Amber | `#D9730D` |
| `base:civic_right` | Cobalt | `#1D3F9E` |
| `base:peoples_union` | Umber | `#86561C` |
| `base:workers_left` | Oxblood | `#7E1F33` |
| `base:homeland_first` | Aubergine | `#5A2E7A` |
| `base:green_wave` | Emerald | `#0B9A5E` |
| `base:open_net` | Orchid | `#C860B0` |

The player's party is never given a special colour: it is marked by a 2 px `ink` ring (compass), an `ink` underline (lists) and
the word "you"/"vy".

### 1.9 Night set (provisional, not v1)

Warm graphite, not navy: `canvas #121110`, `surface #1B1A18`, `surface-2 #242220`, `surface-3 #2E2C29`, `field #121110`,
`line-subtle #2F2D2A`, `line #3D3A36`, `line-strong #57534C`, `ink #F2EFE8`, `ink-2 #BDB7AC`, `ink-3 #948E83`. Accent stays
`#FFD23F`. Semantic and category colours get +12 L* (good `#4FB27A`, bad `#EF6B5E`, tax `#C27BC3`, spend `#3FA7B1`, reg `#C9A044`).
Ship light first; build the theme so these are a token swap.

---

## 2. Typography

### 2.1 Family

**Inter 4.x** (Rasmus Andersson, SIL Open Font License 1.1). One family, everything.

- Bundle `InterVariable.ttf` (roman, axes `wght` 100-900 and `opsz` 14-32) into `client-godot/assets/fonts/`. Optional: `InterVariable-Italic.ttf`
  (not needed in v1). Source: github.com/rsms/inter, release `Inter-4.1.zip`, folder `InterVariable.ttf`. If a static set is preferred:
  `Inter-Regular.ttf`, `Inter-Medium.ttf`, `Inter-SemiBold.ttf`, `Inter-Bold.ttf` from `extras/ttf/`.
- Why Inter: complete Latin Extended-A (all Czech diacritics designed, not composed), true tabular figures (`tnum`), slashed zero
  option, disambiguated `Il1` via `cv05`/`cv08`, and an optical-size axis so big figures tighten automatically. It is the
  typeface of the product tools the user names as "modern" (Linear, Vercel-era UI), and it renders cleanly at 11-14 px, which a
  dense simulation needs.
- Godot: one `FontFile` loaded from the TTF, then one `FontVariation` per role with `VariationOpentype` = `{"wght": 400|500|600|650}`
  and `OpentypeFeatures` = `{"cv05": 1, "cv08": 1}` for all text, plus `{"tnum": 1}` for figures. Antialiasing gray, hinting light,
  subpixel positioning auto, MSDF off (UI is scaled by `canvas_items`, which re-rasterises).

Features always on: `cv05` (lowercase l with tail), `cv08` (upper-case I with serifs). Together they make "Il1" unambiguous in
policy names and numbers. Features on for any figure: `tnum` (tabular), `case` off, `zero` off (slashed zero looks like code).

### 2.2 Weights

400 Regular (body), 500 Medium (labels, controls), 600 SemiBold (headings, figures), 650 (display, via variable axis; static
fallback: Bold 700). No other weights. Never use 700+ for body-size text.

### 2.3 Type scale (logical px at the 1280 x 720 reference)

| Role | Size / line | Weight | Tracking | Features | Use |
|---|---|---|---|---|---|
| `display` | 32 / 36 | 650 | -0.02 em (-0.64 px) | opsz auto | Start-screen title, big hemicycle count. |
| `figure-xl` | 28 / 32 | 600 | -0.02 em | tnum | Stat tile value, slider readout. |
| `title` | 20 / 26 | 600 | -0.01 em | | Page title, modal title. |
| `figure-l` | 17 / 22 | 600 | -0.01 em | tnum | Top-bar indicator values. |
| `heading` | 15 / 20 | 600 | 0 | | Card title, section heading, briefing item title. |
| `body` | 14 / 20 | 400 | 0 | | Default text, descriptions, news. |
| `body-strong` | 14 / 20 | 500 | 0 | | Emphasis inside body, list row primary text. |
| `label` | 13 / 16 | 500 | 0 | | Buttons, tabs, inputs, menu items, chips. |
| `data` | 13 / 16 | 500 | 0 | tnum | Numbers in tables and rows. |
| `caption` | 12 / 16 | 400 | 0 | tnum where numeric | Captions, axis labels, meta, map labels. |
| `overline` | 11 / 14 | 600 | +0.06 em | | Section overlines and HUD labels, UPPERCASE. |
| `micro` | 11 / 14 | 500 | 0 | tnum | Delta chips, tick labels, badges. Never smaller than 11. |

Rules:
- Units are set one role smaller and in `ink-3`: "5,42 **bil. Kč**" → figure in `figure-l`, unit in `caption`.
- Czech number formatting: thousands separator narrow no-break space (U+202F), decimal comma, percent with a no-break space
  (`4,2 %`). English: comma thousands, decimal point, `4.2%`. Minus is U+2212 `−`, not hyphen (tabular width matches digits).
- Max line length: 72 characters (≈ 520 px at `body`) in descriptions, briefings and news. Wider columns get side padding.

### 2.4 Czech text rules

- Czech is ~20-30 % longer than English and has long compounds ("Pravidla volebního systému", "Ekologická regulace podniků").
  Every label slot is designed for the Czech string. Test with the longest localisation key.
- Labels wrap to **2 lines** before they truncate. Truncation (ellipsis) only in the map and in table cells, and always with a
  tooltip carrying the full text.
- Use `AutowrapMode.WordSmart` and set `Language = "cs"` on labels when the UI is Czech so Godot's ICU line breaker hyphenates
  and breaks correctly; never break inside a number+unit (use U+00A0 between them) or after a one-letter preposition
  (`v`, `k`, `s`, `z`, `o`, `u`, `a`, `i`): the text pipeline should replace the space after them with U+00A0 (Czech typographic rule).
- UPPERCASE overlines: check that háčky and čárky on capitals (Č, Ř, Ž, Ú) fit the line box: overline line height is 14 px for
  11 px type, which is enough for Inter's accents. Do not letter-space other text.
- No italic for emphasis (Czech italics with diacritics get muddy at 12 px). Use weight 500.

---

## 3. Space, grid, shape

### 3.1 Spacing scale (logical px)

`2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 48, 64`. Named: `s-1` 4, `s-2` 8, `s-3` 12, `s-4` 16, `s-5` 20, `s-6` 24, `s-8` 32.
Defaults: control inner padding 8 x 12; card padding 16; panel/page padding 24 (16 in compact); gap between cards 12; gap
between label and control 6; gap between form rows 16.

### 3.2 Grid

- 4 px base grid. All component heights are multiples of 4: 24 (chip), 28 (dense control, segmented item), 32 (default control),
  36 (row, CTA), 40 (large CTA, toast), 52 (top bar).
- Page content uses a 12-column fluid grid with 12 px gutters and 24 px outer margin (16 in compact). Cards span 3/4/6/12 columns.

### 3.3 Radii

| Token | px | Use |
|---|---|---|
| `r-xs` | 4 | Checkbox, small badges, tick labels. |
| `r-sm` | 6 | Segmented item, menu item hover, tooltip. |
| `r-md` | 8 | Buttons, inputs, dropdowns, list-row hover, chips with text are `pill`. |
| `r-lg` | 12 | Cards, stat tiles, the inspector inner sections. |
| `r-xl` | 16 | Modals, drawer (inner corner only), popovers. |
| `pill` | 999 | Chips, badges, switch, meters. |

Nested radius rule: inner radius = outer radius - padding (a card with 12 radius and 4 padding has 8-radius children).

### 3.4 Borders vs tonal elevation vs shadow

- **Level 0** `canvas`: map, app background. No border.
- **Level 1** docked regions (top bar, left rail, inspector, pages): `surface`, separated from neighbours by a 1 px `line-subtle`
  **border on the shared edge only**. No shadow. Docked things do not float.
- **Level 2** cards inside a region: `surface` with 1 px `line-subtle` border (on `surface` pages) or `surface-2` fill without border
  (inside cards). No shadow.
- **Level 3** floating: popovers, dropdown lists, tooltips, the compact-mode drawer: shadow allowed.
- **Level 4** modal: scrim + shadow.

Shadows (Godot StyleBoxFlat supports one shadow; values chosen for that):

| Level | `shadow_color` | `shadow_size` | `shadow_offset` |
|---|---|---|---|
| popover / dropdown | `#1A191724` (14 %) | 16 | (0, 6) |
| tooltip | `#1A191733` (20 %) | 8 | (0, 3) |
| drawer (compact inspector) | `#1A19171F` (12 %) | 24 | (-6, 0) |
| modal | `#1A191738` (22 %) | 40 | (0, 16) |
| segmented selected item | `#1A19171A` (10 %) | 2 | (0, 1) |

No other shadows. No coloured shadows. No glow, ever.

### 3.5 Icons (code-drawn stroke pictograms, `Icons.cs`)

- Grid: drawn in a 24 x 24 unit box, live area 20 x 20 (2 unit padding). Rendered at 12, 16, 20 or 24 px; scale the stroke
  with the size: **1.5 px at 16**, 1.25 px at 12, 1.75 px at 20, 2 px at 24 (i.e. stroke = 2 units of the 24 grid scaled, min 1.25).
- Round caps, round joins (`DrawPolyline` with `antialiased: true`; draw round caps as circles of the stroke diameter at ends).
  Corner radius inside pictograms 2 units. Circles are 32-segment polylines.
- One colour per icon, inherited from the text colour of its context (`ink`, `ink-2`, category colour, `ink-inverse`). No filled
  backgrounds behind icons (delete `IconBadge`'s tinted circle); the only filled shapes are small accents ≤ 20 % of the glyph
  area (e.g. a dot).
- Metaphors stay literal and politics-neutral (coin, scales, factory, house, cross, leaf, graduation cap).

---

## 4. Components

Values below are logical px and map 1:1 to Godot `StyleBoxFlat` properties: `bg` = `BgColor`, `border` = `BorderWidth*` +
`BorderColor`, `radius` = `CornerRadius*`, `pad` = `ContentMargin*` as (left/right, top/bottom), `shadow` as in 3.4. Text styles
reference section 2.3. Every interactive component has these states; unspecified state = same as default.

Universal **focus** style (Godot `focus` stylebox, drawn over the control): `draw_center = false`, border 2 `ink`,
radius = control radius + 2, `ExpandMargin*` = 2 (left/right/top/bottom). Shown for keyboard focus only (Godot 4 shows focus on
click too; set `FocusMode = All` but hide the ring on mouse click by tracking last input type, see README risks).

Universal **disabled**: text `ink-disabled`, icon `ink-disabled`, fill as noted, no hover, `MouseDefaultCursorShape = Forbidden`
on hover tooltip explaining why ("Only the leading party can change policy").

### 4.1 Buttons (height 32, `label` text, icon 16 with 6 gap)

| Variant | Default | Hover | Pressed | Disabled |
|---|---|---|---|---|
| **Primary** (ink) | bg `#1A1917`, text `#F5F2EB`, radius 8, pad (14, 8) | bg `#34322E` | bg `#000000` | bg `#E6E3DC`, text `#A9A49A` |
| **Secondary** | bg `#FAF9F6`, border 1 `#D3CFC6`, text `#1A1917` | bg `#F2F0EB`, border `#B9B4A9` | bg `#E6E3DC`, border `#B9B4A9` | bg `#FAF9F6`, border `#E0DDD5`, text `#A9A49A` |
| **Ghost** | bg transparent, text `#57534C` | bg `#1A19170D`, text `#1A1917` | bg `#1A19171A` | text `#A9A49A` |
| **Destructive** | bg `#FAF9F6`, border 1 `#C8352B66`, text `#C8352B` | bg `#F8E2DE`, border `#C8352B` | bg `#F2CFC9` | as secondary disabled |
| **Destructive confirm** (inside a confirm modal only) | bg `#C8352B`, text `#FFFFFF` (5.3 : 1) | bg `#B02D24` | bg `#962620` | as primary disabled |
| **Accent CTA** (End quarter only) | bg `#FFD23F`, border 1 `#1A1917`, text `#1A1917` weight 600 size 14, radius 8, pad (16, 10), height 40 | bg `#FFC81A` | bg `#F2B900`, content offset y +1 | bg `#F2F0EB`, border `#D3CFC6`, text `#A9A49A`, label "Rozhodněte nejdřív" |

Sizes: `sm` 28 (pad 10, 6; text 12), `md` 32 (default), `lg` 40 (pad 16, 10; text 14). Min width 64. Shortcut hint inside the
button: `micro` text in a 18 px tall keycap (bg `#1A19170F` on light buttons, `#FFFFFF26` on ink, radius 4, pad (5, 1)).

**Icon button**: 32 x 32 (28 dense), ghost style, radius 8, icon 16 centred, `TooltipText` mandatory. Toggled-on state: bg
`#1A19171A`, icon `ink`.

### 4.2 Segmented control (filters, ranges: "All / Taxes / Spending / Regulation", "1 r / 3 r / 5 r / Vše")

- Track: bg `#ECE9E2`, radius 8, pad 2, height 32.
- Item: height 28, radius 6, pad (12, 0), `label` text `#57534C`. Hover: text `#1A1917`, bg `#1A19170A`.
- Selected item: bg `#FAF9F6`, text `#1A1917` weight 600, shadow `#1A19171A` size 2 offset (0, 1), border 1 `#1A19170F`.
- Counts inside items: `micro` `ink-3` after a 6 px gap ("Daně 8").
- Godot: `HBoxContainer` of `Button` with `ToggleMode` + a shared `ButtonGroup`, wrapped in a `PanelContainer` with the track box.
- Keyboard: Left/Right moves selection; the group is one tab stop.

### 4.3 Tabs (page-level sections inside a page, e.g. Party: Platform / Promises / Relations / Actions)

Text tabs, height 36, `label` 13 weight 500 `ink-2`, gap 20, no background. Selected: `ink`, weight 600, 2 px `ink` underline
at the bottom edge spanning the text width. Hover: `ink`. Row has a 1 px `line-subtle` bottom border. Do not use Godot's default
`TabContainer` look: set `tab_selected`/`tab_unselected`/`tab_hovered` to empty `StyleBoxEmpty` plus a custom underline draw, or
build tabs as a segmented-like `HBox` of flat buttons.

### 4.4 Dropdown (`OptionButton`) and its popup (`PopupMenu`)

- Closed: as Secondary button, height 32, pad (12, 8) with right pad 32; chevron 12 px `ink-3` drawn at right 12 (theme icon
  `arrow`). Text left-aligned. Hover/pressed as Secondary. Open: border `#1A1917`.
- Popup panel: bg `#FAF9F6`, border 1 `#D3CFC6`, radius 12, pad 4, shadow popover (3.4). Min width = button width.
- Item: height 32, pad (10, 0), `label` text `ink`; hover bg `#F2F0EB` radius 6 (`hover` stylebox); selected item shows a 16 px
  check icon in `ink` at the left, text weight 600; disabled item `ink-disabled`. Separator: 1 px `line-subtle` with 4 px vertical
  margin. Section label: `overline` `ink-3`, pad (10, 8, 10, 4).
- PopupMenu theme constants: `v_separation` 0, `h_separation` 8, `item_start_padding` 10, `item_end_padding` 10,
  `icon_max_width` 16. Font `label`.

### 4.5 Number input / stepper (replaces Godot `SpinBox` arrows)

Composite: `[−] [ value field ] [+]`, total height 32.
- Field: `LineEdit` bg `#FFFFFF`, border 1 `#D3CFC6`, radius 0 in the middle, `data` text right-aligned, pad (10, 8), unit suffix
  inside in `ink-3`. Hover border `#B9B4A9`. Focus border `#1A1917` + focus ring. Invalid: border `#C8352B`, helper text `bad`.
- Step buttons: 32 x 32, bg `#FAF9F6`, border 1 `#D3CFC6`, icon 12 (minus/plus) `ink-2`; left button radius (8, 0, 0, 8), right
  (0, 8, 8, 0). Hover bg `#F2F0EB`. Press-and-hold repeats after 400 ms every 60 ms.
- Keyboard: Up/Down step, Shift = ×10, Enter commits, Esc reverts.

### 4.6 The policy slider (hero control)

Anatomy, total height 64 (readout row 28 + gap 8 + track row 20 + ticks 8):

```
 Minimální mzda                                   7,0  /10    ▲ +3,0
 ──────────────────────────────────────────────────────────────────────
 ████████████▒▒▒▒▒▒▒▒▒▒▒▒◯━━━━━━━━━━━━━━━┆┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄
 0          ▲ teď 4,0   cíl 7,0               dosažitelné do 8,5     10
```

- **Track**: height 6, radius 3, fill `surface-3 #E6E3DC`, full width.
- **Reachable band**: the part of the track the player can afford with current political capital (Democracy 4's segmented ring,
  made linear): track colour `#D3CFC6` inside the band; outside the band the track is drawn with 2 px dots `#D3CFC6` every 6 px
  (unreachable). The thumb cannot be dragged beyond the band; trying shows a tooltip "Chybí 1,5 politického kapitálu".
- **In force fill**: from min to the *current in-force value*, category colour (`cat-tax/spend/reg`), radius 3.
- **Pending change**: between current and target, diagonal 45° stripes 2 px category colour at 45 % alpha on `surface-3`
  (Godot: draw a clipped rect then lines every 5 px), so "scheduled but not yet in effect" is visible without colour alone.
- **Current marker**: 2 x 14 px `ink` tick centred on the track at the in-force value, with `micro` label "teď 4,0" below in `ink-3`.
- **Target thumb**: 20 px circle, fill `#FFFFFF`, border 2 `#1A1917`, shadow `#1A191733` size 4 offset (0, 1). Hover: grows to 22
  (120 ms). Pressed/dragging: fill `#FFD23F` (the highlighter), size 22. Focus: focus ring around the thumb (radius 13, 2 px ink, gap 2).
  Disabled: fill `#F2F0EB`, border `#B9B4A9`.
- **Ticks**: minor ticks 1 x 4 px `#B9B4A9` every 10 % below the track (8 px below centre); major ticks 1 x 6 px `#8F897F` at
  min, mid, max with `micro` labels `ink-3` (tnum). Policies with natural units (retirement age 60-70) tick at whole units.
- **Readout** (top row): policy name `heading` left; right side value `figure-xl` 28 tnum `ink` + unit `caption` `ink-3`; then a
  delta chip (4.7) against the in-force value (`+3,0`, good/bad colour only if the effect on the *player's goal* is known, else neutral).
- **Cost line** under the slider (caption, `ink-2`): "Změna stojí 2,0 kapitálu · rozpočet −12,4 mld. Kč/rok · účinek za 2 čtvrtletí".
- Interaction: click on the track jumps the target; drag moves it; keyboard Left/Right = 1 step (policy `step`), Shift = 10 steps,
  PageUp/PageDown = 10 %, Home/End = band limits; Esc resets target to in-force; Enter = Apply.
- Godot: a custom `Control` (`PolicySlider : Control`) with `_Draw()`; do not skin `HSlider` (it cannot draw band, stripes, two
  markers). Min width 240, preferred 320+, height 64.

The **platform slider** (Your party) uses the same control in a compact 40 px variant: no readout row, value in a right-aligned
`data` label, and a second thin `ink-3` marker showing the government's current value.

### 4.7 Toggle switch and checkbox

- **Switch**: 32 x 18, radius 9. Off track `#D3CFC6`, on track `#1A1917`. Knob 14 px `#FFFFFF`, shadow `#1A191733` size 2,
  inset 2. Hover: track off `#B9B4A9`, on `#34322E`. Disabled: track `#E6E3DC`, knob `#F2F0EB`. Animate knob 120 ms.
  Godot: `CheckButton` with `checked`/`unchecked` icons drawn as `ImageTexture` generated at startup, or a custom control.
- **Checkbox**: 16 x 16, radius 4, border 1.5 → use 2 px border `#8F897F` (Godot integer widths; 2 at the 16 box looks right at
  scale ≥ 1), fill `#FFFFFF`. Checked: fill `#1A1917`, check mark 1.75 px `#FFFFFF`. Indeterminate: 8 x 2 bar. Hover border `#57534C`.
  Label `body` 6 px to the right; the whole row is the hit target (min height 28).
- **Radio**: same as checkbox, fully round; selected = 6 px ink dot inside a 2 px ink ring.

### 4.8 Chip, pill, badge

- **Filter chip** (toggle): height 24, radius pill, pad (10, 0), bg `#F2F0EB`, border 1 `#E0DDD5`, text `label` 12 `ink-2`.
  Hover border `#B9B4A9`. Selected: bg `#1A1917`, text `#F5F2EB`, no border. Optional leading 12 px glyph.
- **Category chip**: height 22, bg category tint, text category colour weight 600 size 12, leading 12 px category glyph.
- **Status pill**: height 22, bg semantic tint, text semantic colour 12 weight 600, leading glyph (▲ ▼ ! i). E.g. "! Ultimátum 4 Q".
- **Count badge**: min 18 x 18, radius 9, pad (5, 0), bg `#1A1917`, text `micro` 11 weight 600 `#F5F2EB`. Attention variant
  (pending decisions): bg `#C8352B`.
- **Delta chip**: no background; ▲/▼ glyph 8 px + `micro` value in semantic colour; neutral (unknown desirability) `ink-3`.
  In dense HUD spots a tinted variant (bg semantic tint, radius 4, pad (4, 1)).

### 4.9 Card

bg `#FAF9F6`, border 1 `#E0DDD5`, radius 12, pad 16. Header: `heading` title + optional right-aligned ghost icon button / link;
gap 12 to content. Interactive card hover: border `#B9B4A9`, bg `#FFFFFF`. Pressed: bg `#F2F0EB`. Selected: border 1 `#1A1917`
and a 3 px `#FFD23F` bar inset on the left edge (drawn as a second StyleBox or `_Draw`). Inset card (card inside a card):
bg `#F2F0EB`, no border, radius 8, pad 12.

### 4.10 List row and table

- Row height 36 (dense 32), pad (12, 0), hairline `line-subtle` between rows (not boxes around each row).
- Columns: primary text `body-strong`, secondary `caption` `ink-3`, numbers `data` right-aligned with tnum.
- Hover: bg `#F2F0EB`, radius 8 (inset 4 px from the container edges). Selected: bg `#FFF1BF`, text `ink`, a 2 px `ink` bar on the
  left edge. Focus: focus ring inset (expand margin -2).
- Table header: height 28, bg `#F2F0EB`, `overline` `ink-3`, sortable headers show a 10 px chevron; sticky on scroll.
- Group header row (e.g. category in the policy table): height 32, category glyph + `heading` 13 weight 600 in category colour
  + count `micro` `ink-3`, no fill, 1 px `line` under it.

### 4.11 Stat tile (dashboard)

Card variant, pad 14 (16 wide), min width 168, height 112 (regular), radius 12.
- Row 1: `overline` label `ink-3` (e.g. "NEZAMĚSTNANOST") + right-aligned 16 px icon `ink-3`.
- Row 2: value `figure-xl` 28 tnum + unit `caption` `ink-3`, baseline-aligned.
- Row 3: delta chip + `caption` "za čtvrtletí".
- Sparkline: bottom 28 px, full width minus padding, 1.5 px `ink` line, 6 % `ink` area, last point 4 px `ink` dot; goal band (if the
  indicator has a target range) as a `good` tint band behind.
- Hover: as interactive card + the sparkline gains a crosshair. Click: opens the chart modal.

### 4.12 Meter / progress / approval bar

- **Meter**: height 6, radius 3, track `#E6E3DC`, fill by meaning (`ink` neutral, semantic, category, party). Value label `data`
  at the right, 8 px gap. Thin variant height 4 for dense lists.
- **Approval bar** (voter groups, diverging): width flexible (min 96), height 8, radius 4. Centre tick 1 x 12 `ink` at 0.
  Fill from centre to value: right `good`, left `bad`. Under it, a 2 px bar in `ink-3` at 35 % showing the group's share of the
  electorate (D4's grey bar). Value `data` right: "+0,23" with ▲/▼ trend glyph against last quarter.
- **Seat bar** (polls): height 10, radius 5, party colour; threshold line 1 px `ink` dashed at the electoral threshold with
  `micro` label "5 %".
- **Indeterminate progress** (End quarter computing): 2 px bar at the bottom edge of the top bar, `ink`, sliding 40 % segment, 900 ms loop.

### 4.13 Tooltip

bg `#1A1917`, text `#F5F2EB` `caption` 12/16, secondary lines `#B8B2A7`, radius 6, pad (10, 7), max width 300 (wrap), shadow
tooltip (3.4). Delay 450 ms for controls, 0 ms for chart crosshair and map nodes. Offset 8 px from the cursor/anchor, flips at
viewport edges. Rich tooltips (map node hover) may contain a mini meter and a figure in `figure-l` `ink-inverse`. Godot: theme
type `TooltipPanel` + `TooltipLabel`, or custom `_MakeCustomTooltip`.

### 4.14 Scrollbar

Thickness 10 hit area, visible grabber 4 px wide (6 px on hover/drag), radius 3, colour `#1A191733`, hover `#1A191759`, drag
`#1A19178C`. Track transparent; no arrows. Godot `VScrollBar`: `scroll` = `StyleBoxEmpty` with content margins 3, `grabber` =
`StyleBoxFlat` radius 3 with `ExpandMargin` 0 and content margin left/right 3, `grabber_highlight`/`grabber_pressed` likewise.
Scroll containers fade the top/bottom 12 px of content when more is available (draw a gradient strip of the surface colour
over the content edge; one `_Draw` per container).

### 4.15 Dialog / modal

- Scrim: `#1A1917` at 40 % (`#1A191766`), click closes non-blocking modals.
- Panel: bg `#FAF9F6`, no border, radius 16, shadow modal (3.4). Widths: S 440, M 560, L 800, XL `min(1120, viewport - 64)`.
  Max height `viewport - 64`; body scrolls, header and footer stay.
- Header: pad (24, 20, 24, 12): optional `overline` (e.g. "ČTVRTLETNÍ BRIEFING · Q3 2022") + `title`; close icon button 32 at
  top-right 12/12.
- Body: pad (24, 0, 24, 20).
- Footer: pad (24, 16), top 1 px `line-subtle`, buttons right-aligned, primary rightmost, gap 8. Destructive actions left-aligned.
- Esc closes (unless blocking: a decision is required); Enter triggers the primary.

### 4.16 Toast

bg `#1A1917`, radius 10, height 40, pad (14, 0), `label` text `#F5F2EB`, leading 16 px icon (semantic colour, lifted for ink:
good `#5CC48A`, bad `#F07A6E`, warn `#F2A65A`), optional action as text button in `#FFD23F` weight 600. Bottom-centre, 16 px above
the bottom edge of the map region; stack upward with 8 gap; max 3. Auto-dismiss 4 s (8 s with action); hover pauses.

### 4.17 Divider

1 px `line-subtle`. Labelled divider: `overline` `ink-3` text with 8 px gap and the hairline filling the rest. Vertical
divider in toolbars: 1 x 20 px `line-subtle`, 12 px margin each side.

### 4.18 HUD indicator chip (top bar)

No box. Hit area pad (10, 6), radius 8, hover bg `#1A19170A`, pressed `#1A191714`, focus ring.
- Line 1: `overline` label `ink-3` ("HDP", "RŮST", "NEZAMĚSTNANOST" → in compact use short labels from the table in layout.md).
- Line 2: value `figure-l` 17 tnum `ink` + unit `caption` `ink-3` + delta chip (▲ 0,1).
- Wide only: 40 x 16 sparkline (1.25 px `ink-2`) to the right of the value.
- Alert state (value outside the healthy range): a 6 px dot `bad` before the label (shape + colour, plus tooltip text).
- Click: opens the chart modal for that indicator. Number change animates (section 5).

### 4.19 Charts

On `surface` only. Plot area pad: left = widest y label + 8, bottom 20, top 8, right 8 (+ end labels width).
- Gridlines: horizontal only, 1 px `#E6E3DC`, 4-6 lines at nice values. Zero line 1 px `#8F897F`. No vertical gridlines, no plot
  border, no background fill.
- Axis labels: `micro` 11 tnum `ink-3`; y labels right-aligned to the plot; x labels: quarters as "Q1 23", years when > 3 years.
- Series: 2 px lines, round joins; single series `ink`; multi series chart palette order. Area under a single series: `ink` 6 %.
  Projection/forecast: same colour, 2 px dashed 4/3.
- Events: elections as a vertical 1 px dashed `#B9B4A9` line with `micro` label at the top; the current quarter as a
  `accent-tint` column behind the last point.
- Crosshair: vertical 1 px `#1A191766`, dots 6 px with 2 px `surface` ring on each series, tooltip (4.13) listing series with colour
  swatch 8 x 2, value `data`.
- Legend: ≤ 4 series → direct labels at the line end (`caption` in series colour, weight 600, collision-nudged); else a legend row
  above the plot: swatch 12 x 3 radius 1.5 + `caption` `ink-2`, gap 12, clicking toggles a series.
- Range control: segmented control top-right ("1 r · 3 r · 5 r · Vše").

### 4.20 Hemicycle (parliament)

- Semicircle of seat dots, rows computed for N seats (200 now) so that the outer radius fills the card width minus 16 and dot
  diameter = 0.72 × pitch (gap ≥ 2 px). At 360 px width: dot 7, pitch ~9.5.
- Colour: party colour. Government parties full colour; opposition parties full colour too but the government block gets a 3 px
  `ink` arc under its seats (outside the semicircle, radius + 6) so the coalition reads as one shape regardless of hue.
- Majority marker: 1 px `ink` dashed radial line at the majority seat, `micro` label "101 = většina".
- Centre: `display` 32 tnum "117" + `caption` "/ 200 · vláda" under it, `good` ▲ if majority, `bad` if not ("bez většiny").
- Hover a dot: party tooltip (name, seats, share). Hover a legend row: dim other parties to 25 %.
- Legend under it: rows of colour dot 10 px + name `body-strong` + seats `data` right; government parties first, separated by a
  labelled divider "Opozice".

### 4.21 Political compass

- Square, aspect 1:1, size = card content width (min 220, max 360). bg `#F2F0EB`, radius 8.
- Axes: 1 px `#B9B4A9` through the centre; grid 1 px `#E0DDD5` at 25 % steps; axis end labels `overline` `ink-3`
  ("LEVICE" / "PRAVICE" / "AUTORITÁŘSKÉ" / "LIBERÁLNÍ") inside the square at 8 px from edges.
- Party: 10 px dot in party colour with 2 px `surface` ring; label `caption` weight 500 `ink`, placed right of the dot or left if
  it would overflow, with a greedy collision pass (try right, left, above, below; else hide and rely on hover).
- Player party: 14 px dot with a 2 px `ink` outer ring + label weight 600 with "(vy)".
- Voter-weighted centre: 16 px dashed ring `ink-3`.
- Hover a party dot: the others dim to 35 %, show a 1 px line from the party to the player.

### 4.22 Map nodes (the web)

- **Policy node**: 30 px circle, fill `surface`, 1.5 px → 2 px ring track `#E0DDD5`, and a **level arc** 2 px in category colour
  from 12 o'clock clockwise proportional to the slider position. Pictogram 14 px in category colour. Pending change: a small
  6 px `ink` dot with `accent` fill at the arc end (target). Changed last quarter: 1 px `ink` outer ring at radius 19.
- **Indicator node**: 40 px circle, fill `surface`, 2 px ring in `good` / `bad` / `#B9B4A9` (neutral) by health. Pictogram 18 px
  `ink`. Value `micro` tnum under the label when zoom ≥ 1 or on hover.
- **Selected node**: 6 px `accent` halo ring outside the node with a 1 px `ink` outline (both drawn), plus the node raises above edges.
- **Hovered node**: ring `ink` 2 px; all unrelated nodes fade to 30 % and unrelated edges to 8 %.
- **Edges**: quadratic curves, default `#1A1917` at 10 %, width 1 (weak) / 1.5 (medium) / 2 (strong). On focus (hover/select):
  positive effect `good`, negative `bad`, alpha 85 %, with a 6 px arrowhead at 70 % of the curve and dash flow (dash 6, gap 6,
  speed proportional to strength, 24-60 px/s) like Democracy 4's moving arrows. Reduced motion: no flow, static arrowheads.
- **Labels**: `caption` 12 weight 500 `ink`, 2 lines max, max width 116 px; policies label outward from the ring (left side
  right-aligned, right side left-aligned, top/bottom centred), indicators label below. A 3 px `canvas` text outline
  (`outline_size` 3, `font_outline_color` = canvas) keeps labels legible over edges. Collision rules in layout.md 2.3.
- **Sector labels**: the three category arcs carry an `overline` label in the category colour with glyph at the arc's outer middle
  ("DANĚ A ODVODY 8", "VÝDAJE 18", "REGULACE 13").

---

## 5. Motion

Easing tokens (CSS cubic-bezier → Godot `Tween` transition/ease):

| Token | Curve | Godot |
|---|---|---|
| `ease-out` | (0.2, 0, 0, 1) | `TransitionType.Quart`, `EaseType.Out` |
| `ease-in` | (0.4, 0, 1, 1) | `TransitionType.Cubic`, `EaseType.In` |
| `ease-in-out` | (0.65, 0, 0.35, 1) | `TransitionType.Cubic`, `EaseType.InOut` |
| `spring-soft` | slight overshoot | `TransitionType.Back`, `EaseType.Out` (only thumb/knob) |

| Interaction | Duration | Easing |
|---|---|---|
| Hover fill/border change | 100 ms | ease-out (Godot: lerp in `_Process` or Tween on `modulate`; acceptable to snap) |
| Press | 60 ms in, 120 ms out | ease-out |
| Focus ring | instant | |
| Tooltip appear / hide | 120 ms fade + 4 px rise / 80 ms fade | ease-out / ease-in |
| Dropdown / popover open | 140 ms fade + scale 0.98 → 1 from anchor | ease-out |
| Drawer / inspector open | 220 ms slide + fade | ease-out; close 160 ms ease-in |
| Page switch (rail navigation) | 160 ms cross-fade + 8 px vertical shift of the new page | ease-out |
| Modal open | scrim 180 ms; panel 220 ms fade + scale 0.97 → 1 | ease-out; close 140 ms ease-in |
| Map refit (inspector opens) | 260 ms camera tween | ease-in-out |
| Map hover dim | 150 ms | ease-out |
| Number change (HUD, tiles) | 450 ms count from old to new value, tabular so width is stable; then delta chip fades in 200 ms | ease-out |
| Value flash | bg tint of the semantic colour at 35 % → 0 over 1200 ms behind the changed figure | ease-out |
| Switch knob / slider thumb grow | 120 ms | spring-soft |

**End-quarter sequence** (total ~1.2 s, never blocks input longer):
1. 0 ms: press. The CTA label changes to "Počítám…" with the 2 px progress bar along the top bar's bottom edge.
2. Simulation runs (async if possible). When it returns:
3. +0 ms: map edges that carried change pulse once (alpha 10 % → 40 % → 10 %, 500 ms); changed nodes get their "changed" ring.
4. +150 ms: HUD figures count to new values, staggered 40 ms left to right; delta chips appear.
5. +500 ms: the date block flips (old slides up 8 px and fades, new slides in, 240 ms).
6. +700 ms: the briefing modal opens (if it has content); otherwise a toast "Q4 2022 · 3 změny" with "Zobrazit" action.
Four-quarters: same sequence once at the end, with a compact summary briefing.

Reduced motion (setting "Omezit animace", default follows nothing in Godot, so expose it): all durations → 0 except fades
capped at 100 ms; no edge flow, no count-up (numbers swap, flash stays as a static 1 s tint).

---

## 6. Responsive rules (summary; full screen behaviour in layout.md)

The UI is laid out in logical px. The window is scaled uniformly (`canvas_items`, `expand`) from 1280 x 720.

- **Effective scale** = Godot's base fit × `f`, where `f` = auto factor × user UI scale (0.8-1.4). The auto factor keeps text
  physically legible in small windows by letting the logical viewport shrink toward 960 x 540 instead of shrinking glyphs
  (formula in layout.md 1.2). Result: logical width ranges 960-~2000, height 540-~1000.
- **Width classes** (logical width W): **compact** W < 1100 · **regular** 1100 ≤ W < 1600 · **wide** W ≥ 1600.
- **Height classes** (logical height H): **short** H < 640 · **normal** · **tall** H ≥ 860 (4:3 and 16:10 at larger sizes).
- Nothing is fixed-size: regions are containers with min/max widths; the map fills whatever remains and re-fits.
- Min hit target: 28 x 28 logical (32 preferred); spacing between adjacent targets ≥ 4.
- Max text line length 72 ch; max content width of a page column 1200 px, centred on wide screens.
- Text never scales independently of the UI: one scale for everything; never below 11 logical px.

---

## 7. Accessibility

- Contrast: all text tokens ≥ 4.5 : 1 on their allowed surfaces (tables in 1.3); large figures ≥ 3 : 1 even in tints; graphics
  (chart lines, meters, node rings) ≥ 3 : 1 against their background (chart palette 1.7; category colours on canvas ≥ 4.4).
- Colour is never the only signal: good/bad always with ▲▼/±; categories with glyphs; parties with names/labels; pending
  slider change with stripes; selection with yellow *and* an ink bar/outline; alerts with a dot shape and tooltip text.
- Focus: 2 px `ink` ring with 2 px gap on every focusable control (16.7 : 1). Visible on keyboard navigation; the focused map node
  shows the same halo as hover.
- Keyboard: Tab / Shift+Tab across regions in order top bar → rail → main → inspector; F6 jumps between regions; arrows within
  lists, segmented controls and the slider; Enter activates; Esc closes the topmost popup/drawer/modal, then clears map selection.
  Shortcuts: Space or Enter on the CTA = End quarter (with focus not in a text field), 1-7 = rail destinations, M = map,
  `/` = focus the policy search, [ and ] = previous/next quarter in charts. Shortcuts listed in tooltips and the menu.
- Map keyboard access: Tab into the map focuses the first policy; arrow keys move to the nearest node in that direction;
  Enter selects (opens inspector).
- Motion: "Omezit animace" setting (section 5). Text size: UI scale 80-140 %.
- Screen-reader support is out of scope for Godot 4.7 desktop builds beyond `TooltipText`; keep every icon-only control
  tooltipped so a future AccessKit pass has names.
