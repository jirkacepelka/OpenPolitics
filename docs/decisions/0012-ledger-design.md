# 0012 – Client look and frame: from the Ledger spec to the Democracy 4 structure

Status: accepted (2026-11-15). Supersedes the look-and-feel parts of 0010.

## Context

The user rejected the navy/cyan client twice ("looks like a 2005 game", "default Godot lightly reskinned", "layout nothing
special", must be fully responsive). A designer wrote the **Ledger** spec (`docs/design/README.md`, `design-language.md`,
`layout.md`, `design-system.html`, `shots/`): warm paper, ballot-yellow highlighter, docked inspector, ring-and-arcs map with a
label placer. Before any Ledger code was written the user looked at that proposal and said it was **still unclear/cluttered and
AI-like**, and sent five Democracy 4 screenshots as the starting point. The client was therefore built to Democracy 4's structure
and visual weight (`docs/design/addendum-democracy4.md`), keeping Ledger's machinery.

## Refinement: Democracy 4's structure, a modern look of our own

The user then asked for D4's clarity and airiness but "not so much like Democracy 4 ... a modern 2026 game from a big studio". The
structure stayed; the look was replaced (tokens in the addendum's "Refinement" section): violet signature colour on warm neutrals with
ink text, a graphite voter panel, soft radii and fine depth instead of square frames and flat saturated blue, actions in the dialog
footer, thin rings, bars and pictograms, eased hover / dialog / bar / map-focus motion. The table below describes the structure; where
it says "chrome", read "signature colour".

## Display bug: the game could not run at Full HD

Cause: the first-start window was capped at 1600 x 900 (`DisplaySettings.Apply` took min(1600, 92 % of the screen)) and
`project.godot` overrode the window to 1600 x 900; there was no way to pick a size. Fix: no cap (the override is now only a 1920 x 1080
fallback for the very first frame, `MaxSize` is cleared); the first start opens maximised ("Fit to the screen"); the display card offers
a window size (fit, or 1280x720 / 1366x768 / 1600x900 / 1920x1080 / 2560x1440 / 3840x2160 where they fit the screen, plus the current
size); borderless and full screen always use the screen's native resolution; the windowed size is remembered on close. Verified with
`RES=1920x1080` (logical 1280 x 720, scale 1.5, regular) and `RES=2560x1440` (logical 1280 x 720, scale 2): the UI fills the window.

## What was built

| Area | Result | Where |
|---|---|---|
| Tokens and theme | One signature colour `Chrome` (violet `#5B45E0`, swap in one line), warm neutrals, graphite panel, traffic lights, soft radii, fine depth on floating surfaces only. One `Theme` in code with type variations: `T<Role>` for twelve text roles, `ButtonSecondary/Ghost/Play/Danger/Seg/Tab/Card`, `Dialog/Sheet/Inset/Card/Warn/Toast/Sidebar/TopBar` panels; PopupMenu, OptionButton, LineEdit, CheckBox, scrollbars, tooltips and sliders all themed. | `AppTheme.cs` |
| Font | Inter 4.1 variable, bundled (SIL OFL, licence next to it), `.import` committed; `FontVariation` per role (wght 400/600/700, `cv05`/`cv08`, `tnum` for figures, +1 px tracking on uppercase labels). Fallback chain Inter → Noto Sans → Open Sans → Fira Sans → DejaVu Sans. | `AppTheme.cs`, `assets/fonts/` |
| Display | Ledger's formula: automatic factor (small windows shrink the logical viewport toward 960 x 540, never the text), UI scale 80-140 %, window mode (window / borderless / full screen, F11), reduce motion; saved in `user://display.cfg`, applied live, refitted on every resize. | `DisplaySettings.cs` |
| Size classes | `Layout`: compact < 1100 ≤ regular < 1600 ≤ wide, short < 640, tall ≥ 860, `Changed` event; the frame rebuilds on a class flip. | `DisplaySettings.cs`, `Main.cs` |
| Top bar | Key figures (Income, Expenditure, Deficit, Debt, GDP, Growth, Unemployment, Inflation, Mood) in priority order with "+N" overflow popup, term clock and progress bar, square chrome tabs, green Play and x4. Play turns orange and opens the briefing while an offer waits. | `TopBar.cs` |
| Popularity panel | Chrome sidebar: overall approval, sort strip, every group with happiness bar and share band, group lens on click, government seats; compact = icon strip that opens as an overlay. | `Sidebar.cs` |
| Map | Full-window field, three wedge sectors with areas proportional to their nodes, dashed separators, capital hub; relaxation layout recomputed for every window shape; nodes sized by degree; chrome policy discs with slider ring and scheduled-change dot; grey indicator discs with traffic ring (vs start of term, by voter preference); no labels at rest, hover tag; influence arrows with moving dashes for the focused node only; group lens fades the rest. | `WorldMap.cs` |
| Dialogs | Centred, white veil over the map, 1 px border, CLOSE in the corner, actions attached under the bottom edge; full-window sheets in compact or very short windows; fit to content. | `Dialog.cs` |
| Policy dialog | Replaces the docked inspector: description, implementation and popularity bars, stat block (category, in force, time to effect, scheduled, indexation), effect rows for groups and indicators (Game.PreviewPolicy; with no change, the policy's links and the groups' opinions), the custom `PolicySlider` (ticks, in-force ghost, stripes for a scheduled change, keyboard), cost and budget change, capital box, Apply / Cancel change / Revert. | `PolicyDialog.cs`, `PolicySlider.cs` |
| Other dialogs | Indicator (value, chart with range, why it moved, influences), voter group, party, chart (range, legend chips toggle series, crosshair). | `PolicyDialog.cs`, `ChartDialog.cs`, `Chart.cs` |
| Screens | Voters (electorate bar, rows with mood and likes/dislikes), Charts (tiles), **Budget** (new: income, expenditure, balance, debt, every spending and tax policy), Politics (hemicycle with government arc and majority line, polls with threshold mark, compass with greedy labels, coalition, formation), Your party (tabs; platform uses the compact slider and a priority stepper), News. | `Pages.cs`, `PoliticsScreen.cs`, `PartyScreen.cs` |
| Briefing | Democracy 4 intro-modal style: centred title, decisions first (accept/decline offer, form a government, ultimatums), six indicator pills with traffic bars, news, two full-width buttons; also a welcome at the start of a game. | `TurnPopup.cs` |
| Start screen | Washed map backdrop, scenario cards, language, seed with reroll, display card with live preview of the logical size, class and font, load (menu of saves) and the green Start. | `StartScreen.cs` |
| Icons | Stroke pictograms thickened; 31 added (map, sort, undo, fit, and 16 to separate look-alike nodes: CO2 cloud, sun, tree, chimney, cart, gavel, bricks, contract, pie, house-with-percent, diamond, banknote, bottle, threshold, elder, pram). Tinted icon badges deleted. | `Icons.cs` |
| Czech text | Decimal comma, true minus, `bil. Kč`/`mld. Kč`, ≥/≤, `Label.Language = "cs"`. | `Ui.Fix` |
| Smoke | Renders into an offscreen `SubViewport` of exactly `RES` with the window's scaling (a tiling window manager resized the real window to 945 x 971); PNGs of start, map (plain, hover, lens), policy / indicator / group dialogs, every screen, party tabs, chart, briefing. | `Smoke.cs`, `Main.cs`, `scripts/client-smoke.sh` |

Deleted: the bottom dock, the floating glass panel and `Glass()`, `MapGlow`, the navy/cyan palette, `IconBadge`, `HudChip`, the old
`IconButton`, the policy card grid, `OverviewScreen`, `PoliciesScreen`, `PolicyEffectsPanel`, the `Window`-based popups.

## Deviations from the Ledger spec and why

- **Palette, frame and map** follow the Democracy 4 structure with the refined look instead of Ledger (user's direct feedback). No yellow highlighter, no
  docked inspector, no ring-and-arcs map, no label placer at rest (labels were the clutter the user named). The label-placement idea
  survives in the compass.
- **No zoom/pan on the map**: the packed full-window layout shows every node at a usable size at 960 x 540 and up; zoom would add chrome.
- **Dashboard page** is replaced by the Charts tab (tiles) and the Budget tab, as in Democracy 4; the key figures live in the top bar.
- **Policies table** is gone: policies are reached on the map, in the Budget tab and from effect rows. The Ledger "Budget" and "Popularity"
  columns are covered by the Budget tab and the policy dialog's popularity bar.
- **Political capital cost of a policy change**: the core charges none, so the slider's "reachable band" is the full range and the capital
  box says so. The PolicySlider keeps the data model to add a band if the core ever prices changes.
- **News filters** were not built: news items are plain strings in the core and cannot be classified reliably.
- **End-quarter animation sequence** (count-up of figures, date flip) is not implemented; the only motion is the moving dashes, which
  "reduce motion" turns off.
- **Electoral threshold on the start screen**: dropped, it is a policy since 0011.

## Known gaps

- Smoke screenshots come from the offscreen viewport; popups that are separate windows (drop-down lists, the "+N" figure popup,
  tooltips) are not in them and were not checked visually at every size.
- The welcome dialog at game start is not covered by a screenshot (the smoke run turns popups off); its layout is the briefing's.
- Keyboard navigation of the map (arrow keys between nodes) and focus rings for map nodes are not implemented; dialogs and the slider
  work with the keyboard.
- Indicator "goodness" compares with the first recorded quarter and the voters' preferred direction; values nobody cares about get a
  grey ring. It is a client heuristic, not a core figure.
- The map layout is deterministic but not tuned per pack: a mod with many more nodes will shrink them toward the 14 px minimum radius.
