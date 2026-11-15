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
in Czech or English; for the political scenario you pick your party (decision 0013).

## The interface

Laid out like Democracy 4 (the user's reference; spec in `docs/design/addendum-democracy4.md`, decision record 0012). Everything is
drawn in code; the only bundled asset is the Inter font.

- **Top bar** (76 px; 60 in small windows): key figures as a small uppercase label over a semibold figure (Income, Expenditure,
  Deficit, Debt, then GDP, Growth, Unemployment, Inflation, Mood - as many as fit, the rest behind "+N"; click one for its chart), a
  clock with a progress bar to the next election, a row of icon tabs (Map, Voters, Charts, Budget, Politics, Your party, News,
  Menu) and the violet **Play** button (End quarter) with a narrow **x4** for four quarters. Play turns orange while an offer waits for
  your answer and then opens the briefing instead.
- **Popularity panel** (left, graphite): overall approval, a sort strip (name / size / happiness), every voter group with a happiness bar
  and a lighter band for its share of the electorate. Click a group to turn the map into its lens (what it cares about is coloured by how
  it likes it, everything else fades); the lens chip on the map offers the group's details. Government seats with the majority mark at the
  bottom. In a compact window the panel is a narrow strip of icons and mini bars that opens as an overlay.
- **Map** (the rest of the window): every policy and indicator as a disc sized by importance, packed into three sectors (taxes,
  spending, regulation) separated by dashed lines from a hub that shows political capital. Policies are violet-tinted discs whose ring shows the
  slider position (a white dot marks a scheduled change); indicators are grey discs whose ring is green / orange / red by how the value
  compares with the start of the term. No labels at rest: hover a node for its name and value and its influences (thick green/red arrows
  with moving dashes). The layout is recomputed for every window shape, so the field is always full.
- **Dialogs** open centred over the washed-out map (full-window sheets in a compact window): soft corners and a light shadow, a close
  button in the corner, actions in the footer (primary rightmost). Esc closes the top one.
  - *Policy*: description, implementation delay and popularity bars, a stat block (category, value in force, time to effect, scheduled
    change, indexation for spending), effect rows for voter groups and indicators (a dark track with a diverging green/red fill; with no
    change dialled in it shows what the policy acts on), the big slider with ticks, the value in force as a ghost disc and stripes for a
    scheduled change, cost and budget change, the political-capital box, and **Apply changes / Cancel change / Revert changes**.
    Changing a policy costs the leader political capital (decision 0013): the capital box shows what the dialled change costs, a soft green
    band on the slider marks what the capital pays for now, and **Apply changes · N** is disabled when the change costs more than you have.
  - *Indicator*: value and its change, a chart with a range switch, why it moved last quarter, what it influences (click a row to jump).
  - *Voter group*, *party*: mood or poll with a chart, what they like, demands, relations.
  - *Voters*, *Charts*, *Budget*, *Politics* (parliament half-circle, polls with the threshold mark, compass, coalition and government
    formation; for the leader of a sitting government a **Reshuffle the coalition** card - partners to drop, seated outside parties to bring in
    with the demands they ask for, the seats the new coalition would hold, what the parties would answer, cost and cooldown, and the confirm
    button; the parliament card has a button that scrolls to it), *Your party* (tabs: overview, platform with a compact slider and a priority stepper per policy, promises, relations,
    actions and stances), *News*.
- **Briefing** after each quarter (and a welcome at the start), styled after Democracy 4's intro modal: big title, decisions first (accept
  or decline an offer, form a government, running ultimatums), six key indicators as pills with traffic bars, the news, two full-width
  buttons.
- **Start screen**: scenario cards, the party cards of the political scenario (colour, name, starting role - leads the government, junior
  partner or opposition -, poll share and seats, a mini compass and its words; the role is explained under the cards), language, seed,
  the display card, load and start. The welcome briefing depends on the role.
- Keyboard: Space = End quarter, M = map, 1-6 = the screens, Esc = close / clear the lens, Ctrl+S = quick save, F11 = full screen.

## Display settings and responsiveness

The window is freely resizable (minimum 960 x 540). The interface is laid out in logical pixels for 1280 x 720 and scaled with
`canvas_items` + `expand`, so other shapes get more room rather than bars. `DisplaySettings` adds an automatic factor (small windows get a
smaller logical viewport instead of smaller text, never below 960 x 540), the player's interface size (80-140 %), the window mode (window,
borderless, full screen; F11 toggles), the window size in window mode (maximised "fit to the screen" by default, or 1280x720 up to
3840x2160 where it fits; the last size is remembered; borderless and full screen use the native resolution) and "reduce motion" (no moving dashes). All of it is on the start screen's display card and in the
in-game menu, applies live and is saved in `user://display.cfg`.

`Layout` turns the logical size into size classes - compact < 1100 <= regular < 1600 <= wide, short < 640, tall >= 860 - and raises
`Changed` when a class flips; the frame is rebuilt then (sidebar strip, top-bar sizes, dialog sheets).

## Smoke test

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh            # headless
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh /tmp/shots # windowed, writes PNGs
```

Besides every screen of every scenario it presses a party card on the start screen, plays the political scenario as an opposition party
(Civic Right), checks that the policy dialog refuses an unaffordable change and charges an affordable one, and asks a coalition reshuffle
from the Politics screen (shots `start-party`, `policy-cost`, `policy-cost-unaffordable`, `reshuffle`, `reshuffle-after`).

## Look and feel

Democracy 4's structure and clarity with a modern look of its own (decision record 0012, tokens in
`docs/design/addendum-democracy4.md`): warm neutrals with ink text, one signature colour (`AppTheme.Chrome`, violet; swap it in one line)
for the primary action, selection, policy rings and the slider, a graphite voter panel, soft radii, tonal elevation with a light shadow
only on floating surfaces, thin rings and bars, thin-stroke pictograms, eased motion ("reduce motion" turns it off). One `Theme` is built
in code with type variations (`T<Role>` for text roles, `ButtonSecondary`, `ButtonGhost`, `ButtonPlay`, `ButtonSeg`, `ButtonTab`,
`ButtonCard`, `Dialog`, `Sheet`, `Inset`, `Card`, `Warn`, ...). About 75 pictograms are drawn with primitives (`Icons.cs`).

**Fonts**: Inter 4.1 is bundled in `assets/fonts/InterVariable.ttf` (SIL OFL, licence next to it; see `assets/fonts/README.md`). Weights
and OpenType features (tabular figures for numbers) are set per role with `FontVariation`. Without the file the client falls back to
Noto Sans / Open Sans / Fira Sans / DejaVu Sans and still lays out.

Check changes with `scripts/client-smoke.sh <dir>`: with `RES=WxH` (and `UISCALE=1.3`) it renders into an offscreen viewport of exactly that
window size, so a tiling window manager cannot change the shape, and writes PNGs of the start screen, the map (plain, hovered, lens), every
dialog and screen, a chart and the briefing.
