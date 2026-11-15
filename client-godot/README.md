# OpenPolitics Godot client

A Godot 4 (.NET) client for the C# core. It only uses the core's narrow API (`PackLoader`, `Simulation`,
`Game.State/Apply/EndTurn/Save`) and the shared texts in `core/Presentation`.

## Requirements

- Godot **4.7.2 .NET** edition (checked with the .NET 10.0 runtime; the project targets `net10.0`).
- .NET SDK 10 (`global.json` pins it). If it lives outside the usual place set `DOTNET_ROOT`.

## Run

Open `client-godot/` in the Godot editor and press F5, or from a shell:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
godot --path client-godot
```

Packs are read from `data/base` (override with the environment variable `OPENPOLITICS_PACKS`). Saves go to Godot's
`user://saves` directory. The start screen offers the political scenario (Czechia 2015, coalition government) and the economy sandbox,
in Czech or English.

## The interface

Laid out like Democracy 4 (the user's reference; spec in `docs/design/addendum-democracy4.md`, decision record 0012). Everything is
drawn in code; the only bundled asset is the Inter font.

- **Top bar** (white, 76 px; 60 in small windows): key figures as a small chrome label over a bold value (Income, Expenditure,
  Deficit, Debt, then GDP, Growth, Unemployment, Inflation, Mood - as many as fit, the rest behind "+N"; click one for its chart), a
  clock with a progress bar to the next election, a row of square chrome tabs (Map, Voters, Charts, Budget, Politics, Your party, News,
  Menu) and the green **Play** button (End quarter) with a narrow **x4** for four quarters. Play turns orange while an offer waits for
  your answer and then opens the briefing instead.
- **Popularity panel** (left, chrome): overall approval, a sort strip (name / size / happiness), every voter group with a happiness bar
  and a lighter band for its share of the electorate. Click a group to turn the map into its lens (what it cares about is coloured by how
  it likes it, everything else fades); the lens chip on the map offers the group's details. Government seats with the majority mark at the
  bottom. In a compact window the panel is a narrow strip of icons and mini bars that opens as an overlay.
- **Map** (the rest of the window): every policy and indicator as a disc sized by importance, packed into three sectors (taxes,
  spending, regulation) separated by dashed lines from a hub that shows political capital. Policies are chrome discs whose ring shows the
  slider position (a white dot marks a scheduled change); indicators are grey discs whose ring is green / orange / red by how the value
  compares with the start of the term. No labels at rest: hover a node for its name and value and its influences (thick green/red arrows
  with moving dashes). The layout is recomputed for every window shape, so the field is always full.
- **Dialogs** open centred over the washed-out map (full-window sheets in a compact window): square corners, a 1 px dark border, a chrome
  CLOSE in the corner, action buttons attached under the bottom edge. Esc closes the top one.
  - *Policy*: description, implementation delay and popularity bars, a stat block (category, value in force, time to effect, scheduled
    change, indexation for spending), effect rows for voter groups and indicators (a dark track with a diverging green/red fill; with no
    change dialled in it shows what the policy acts on), the big slider with ticks, the value in force as a ghost disc and stripes for a
    scheduled change, cost and budget change, the political-capital box, and **Apply changes / Cancel change / Revert changes**.
  - *Indicator*: value and its change, a chart with a range switch, why it moved last quarter, what it influences (click a row to jump).
  - *Voter group*, *party*: mood or poll with a chart, what they like, demands, relations.
  - *Voters*, *Charts*, *Budget*, *Politics* (parliament half-circle, polls with the threshold mark, compass, coalition and government
    formation), *Your party* (tabs: overview, platform with a compact slider and a priority stepper per policy, promises, relations,
    actions and stances), *News*.
- **Briefing** after each quarter (and a welcome at the start), styled after Democracy 4's intro modal: big title, decisions first (accept
  or decline an offer, form a government, running ultimatums), six key indicators as pills with traffic bars, the news, two full-width
  buttons.
- **Start screen**: scenario cards, language, seed, the display card, load and start.
- Keyboard: Space = End quarter, M = map, 1-6 = the screens, Esc = close / clear the lens, Ctrl+S = quick save, F11 = full screen.

## Display settings and responsiveness

The window is freely resizable (minimum 960 x 540). The interface is laid out in logical pixels for 1280 x 720 and scaled with
`canvas_items` + `expand`, so other shapes get more room rather than bars. `DisplaySettings` adds an automatic factor (small windows get a
smaller logical viewport instead of smaller text, never below 960 x 540), the player's interface size (80-140 %), the window mode (window,
borderless, full screen; F11 toggles) and "reduce motion" (no moving dashes). All of it is on the start screen's display card and in the
in-game menu, applies live and is saved in `user://display.cfg`.

`Layout` turns the logical size into size classes - compact < 1100 <= regular < 1600 <= wide, short < 640, tall >= 860 - and raises
`Changed` when a class flips; the frame is rebuilt then (sidebar strip, top-bar sizes, dialog sheets).

## Smoke test

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh            # headless
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh /tmp/shots # windowed, writes PNGs
```

## Look and feel

Flat and high-contrast: white and light-grey surfaces, one saturated chrome colour (`AppTheme.Chrome`, swap it in one line) for the
sidebar, tabs, buttons, bars and policy nodes, a green Play, traffic-light green / orange / red for good and bad, dark tracks, square
corners, no shadows, no gradients. One `Theme` is built in code with type variations (`T<Role>` for text roles, `ButtonSecondary`,
`ButtonGhost`, `ButtonPlay`, `ButtonSeg`, `ButtonTab`, `ButtonCard`, `Dialog`, `Sheet`, `Inset`, `Card`, `Warn`, ...). About 75 pictograms are
drawn with primitives (`Icons.cs`); every policy, indicator and voter group gets one from its id.

**Fonts**: Inter 4.1 is bundled in `assets/fonts/InterVariable.ttf` (SIL OFL, licence next to it; see `assets/fonts/README.md`). Weights
and OpenType features (tabular figures for numbers) are set per role with `FontVariation`. Without the file the client falls back to
Noto Sans / Open Sans / Fira Sans / DejaVu Sans and still lays out.

Check changes with `scripts/client-smoke.sh <dir>`: with `RES=WxH` (and `UISCALE=1.3`) it renders into an offscreen viewport of exactly that
window size, so a tiling window manager cannot change the shape, and writes PNGs of the start screen, the map (plain, hovered, lens), every
dialog and screen, a chart and the briefing.
