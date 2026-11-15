# Addendum: the Democracy 4 pivot (supersedes the Ledger palette and frame)

2026-11-15. After reading the Ledger spec the user found it "still very unclear/cluttered and AI-like" and supplied five
Democracy 4 screenshots as the reference (intro modal, main map, two policy dialogs). The client is built to **Democracy 4's
structure and visual weight**, with our own code-drawn pictograms and no portraits or D4 art. What survives from Ledger is the
machinery: display scaling and size classes (`layout.md` §1.2), theme type variations instead of per-node overrides, the policy slider
logic, the chart rules (gridlines, crosshair, direct labels) and the fixed party colours. Everything about the look and the frame
below replaces `design-language.md` §1, §3.3-3.4, §4.1, §4.6, §4.15, §4.22 and `layout.md` §1.1, §2, §3, §4.

## Structure

| Region | Size | What it is |
|---|---|---|
| Top bar | 76 (regular/wide), 60 (compact), white, flat | Key figures (small grey label over a bold value: Income, Expenditure, Deficit, Debt, then GDP, Growth, Unemployment, Inflation, Mood as width allows, the rest behind a "+N" overflow), a clock and a wide progress bar to the next election (or the 20-year horizon), a row of square icon tabs (Map, Voters, Charts, Budget, Politics, Party, News, Menu) and the green square **Play** (End quarter) with a narrow **x4** beside it. |
| Sidebar | 288 (wide), 248 (regular), 56 strip (compact) | The chrome-coloured **Popularity** panel: overall approval bar, sort strip (name / size / happiness), every voter group as a row (white name over a thin bar filled by happiness, a lighter band behind the name for the group's share of the electorate). Clicking a group turns the map into that group's lens. Government seats at the bottom. In compact the panel becomes a strip of vertical mini bars that opens as an overlay. |
| Map | the rest, edge to edge | `#F4F4F2` field. Every node is a disc sized by importance. Policies: chrome fill, white pictogram, ring showing the slider position. Indicators: light grey disc, dark pictogram, a traffic-light ring showing how the value compares with the start of the term. Three sectors (Taxes, Spending, Regulation) separated by dashed radial lines from a central hub that shows political capital (`x12`). Nodes are packed over the whole field by a relaxation layout recomputed for every window shape. **No labels at rest**: the name appears as a dark tag on hover or selection; influence arrows (thick, green/red, moving dashes) only for the hovered/selected node. |
| Dialogs | centred, square corners, 1 px dark border, white | The map behind is washed out with a white veil. Header: grey disc with pictogram + bold title; a square chrome CLOSE in the top-right corner. Action buttons (Apply changes, Cancel change, Revert changes) hang attached under the bottom edge on the right. In compact the dialog becomes a full-window sheet that scrolls. |

Clicking a policy opens the **policy dialog** (replaces the docked inspector): description and two labelled bars
(Implementation delay / implementation progress in chrome, Popularity with voters in traffic colours) on the left, a stat block on
the right (category, time to effect, budget effect, indexation, scheduled change), effect rows for voter groups and indicators
(`icon + name … long dark track with a diverging fill … (signed number)`), and at the bottom the big slider with the value readout
above it, Cost/Income under it and the political-capital box on the right. Indicators open an indicator dialog (value, chart, why it
moved, what it influences). Voters, Charts, Budget, Politics, Party and News open as large dialogs in the same style.

The **briefing** follows the intro modal: big centred title, a paragraph, decisions, a 3 x 2 grid of grey pills (chrome circle with
pictogram, uppercase label, traffic bar), the news, two full-width chrome buttons.

## Tokens (`AppTheme`)

| Token | Hex | Use |
|---|---|---|
| `Chrome` | `#1C8BE0` | The one saturated colour: sidebar, tabs, buttons, policy nodes, bars, the hub. Swap here to re-colour the game. |
| `ChromeDark` | `#1570BA` | Hover/pressed chrome, sidebar row hover. |
| `ChromeLight` | `#58ADEC` | Share bands in the sidebar, ghost target thumb. |
| `ActiveTab` | `#262626` | The open tab (contrasting fill). |
| `Play` / `PlayDark` | `#62B81E` / `#4E9A14` | End quarter. |
| `MapBg` | `#F4F4F2` | Map field. |
| `Surface` | `#FFFFFF` | Top bar, dialogs. |
| `Panel` / `Panel2` | `#EDEDED` / `#E0E0E0` | Pills, insets, indicator discs. |
| `Track` / `TrackLight` | `#4A4A4A` / `#D5D5D5` | Effect bar tracks (dark), meter tracks (light). |
| `Border` | `#2B2B2B` | 1 px dialog border. |
| `Hairline` | `#DCDCDC` | Separators. |
| `Ink` / `Ink2` / `Ink3` | `#1E1E1E` / `#4A4A4A` / `#767676` | Text. |
| `Good` / `Warn` / `Bad` | `#5DB92B` / `#F29A12` / `#EC4A22` | Traffic lights (fills); darker text variants `#3A8A14` / `#A86600` / `#C93A15`. |

Type: one sans family (bundled Inter if present, else Noto Sans / Open Sans / Fira Sans / DejaVu Sans), weights 400/600/700, tabular
figures for numbers, small labels in uppercase with +1 px tracking. Corners: 0 on chrome, dialogs and buttons; pills and bars are
fully round. No shadows (dialogs too), no gradients, no glass, no yellow highlighter.
