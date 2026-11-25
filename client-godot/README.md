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
`user://saves` directory. The new-game screen offers the political scenario (Czechia 2015, coalition government) and the economy sandbox;
for the political scenario you pick your party (decision 0013). The language (Czech or English) is chosen in the settings.

## The interface

Laid out like Democracy 4 (the user's reference; spec in `docs/design/addendum-democracy4.md`, decision record 0012). Everything is
drawn in code; the only bundled asset is the Inter font.

- **Top bar** (76 px; 60 in small windows): key figures as a small uppercase label over a semibold figure (Income, Expenditure,
  Deficit, Debt, then GDP, Growth, Unemployment, Inflation, Mood - as many as fit, the rest behind "+N"; click one for its chart), a
  clock with a progress bar to the next election, a row of icon tabs (Map, Voters, Charts, Budget, Politics, Your party, News,
  Menu) and the violet **Play** button (End quarter) with a narrow **x4** for four quarters. Play turns orange while an offer waits for
  your answer and then opens the briefing instead.
- **Popularity panel** (left, graphite): overall approval, a sort strip (name / size / happiness), every voter group with a happiness bar
  and a lighter band for its share of the electorate. A **Mood / Support for <party>** toggle (decision 0015) switches the rows to the share of
  each group that would vote for the player's party, with the change since last quarter (default: Support outside the lead, Mood as leader);
  the overall figure then shows the party's poll. Click a group to turn the map into its lens (what it cares about is coloured by how
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
  - *Voter group*, *party*: mood or poll with a chart, what they like, demands, relations; the group dialog also shows the group's support for
    the player's party and its change.
  - *Voters*, *Charts*, *Budget*, *Politics* (parliament half-circle, polls with the threshold mark, compass, coalition and government
    formation; for the leader of a sitting government a **Reshuffle the coalition** card - partners to drop, seated outside parties to bring in
    with the demands they ask for, the seats the new coalition would hold, what the parties would answer, cost and cooldown, and the confirm
    button; the parliament card has a button that scrolls to it; an open **proposal** card sits at the top), *Your party* (tabs: overview with a
    tooltip on every stat tile - capital, organisation, credibility, cohesion, fiscal blame - and a **How it works** section built from the
    engine's numbers; platform with the programme's goals and demands (met / not met, weight) above a compact slider and a priority stepper per
    policy; promises - what the party will do in the next term, judged at the election that ends it (the quarters left, how many of the two
    promises before this election are used, whether breaking it would cost you); relations; actions with an explanation per card: the campaign
    card says how many points of support the campaign wins at once, and one **Propose a policy** card for every party that does not lead
    (policy picker, slider, who decides - the leader's likely answer for a partner, public for the opposition -, the points of support per group,
    the credibility cost, Propose · 3 / · 2); for a junior partner an **Ultimatum to the leader** card (decision 0016: demand or programme
    position, deadline 2-6 quarters, the leader's likely answer, the party's leverage and the stakes, Give the ultimatum · 4; while it runs its
    status and Withdraw); the overview also has a **Fiscal credit** tile next to fiscal blame; stances), *News*. After an action the message says how far the party's support moved
    ("+2.1 pts among Middle class"). The policy dialog notes when a high deficit or debt softens the voters' dislike of a cut.
- **Briefing** after each quarter (and a welcome at the start, and after loading a save), styled after Democracy 4's intro modal: big title,
  then every open decision (decision 0015, `Decisions.Open`), each answered inline: accept or decline an offer; as formateur the suggested
  coalition with its agreements and seats - **Form this coalition** - or **Adjust**, the negotiation view of decision 0016 (per seated party an
  invite box, a satisfaction bar and its answer - accepts / counters / refuses -, the reason with numbers and the price breakdown, its demands
  with acceptable / negotiable / red line / met pills and a half-way slider for negotiable ones, the portfolios it values most, concessions
  0-2, what would fix a refusal, its counter terms with **Take its terms**, and **Send the offer**, three offers a quarter); as an invited
  party a **Counter-offer** section under the offer (one of your demands and/or a portfolio, the formateur's likely answer, **Join on these
  terms**); the own ultimatum's countdown and the leader's answer with **Withdraw**; a
  **Proposal** card (Support / Oppose); an ultimatum to the leader's government with what can be done about it (deliver: opens the policy;
  court the partner; reshuffle; dismiss); the window for promises with a **Promise** button per goal. Then six key indicators as pills, the
  news, two buttons in the footer. The Politics badge counts exactly these decisions; only an offer blocks the quarter. **x4** stops at the
  first quarter that brings an urgent decision.
- **Main menu**: Continue (the newest save, only when there is one), New game, Load game, Settings, Quit; the map's discs fill the rest of the window.
- **New game**: three columns (a drop-down and one scrolling column in a small window). Left: the scenarios and the description of the chosen
  one. Middle: one row per party, largest first - colour, name, starting role (government, coalition, opposition), poll, seats and its place
  on the compass. Right: the chosen party in full (poll, seats, compass and its words, what the role means), the random seed and Start.
  Esc goes back to the menu. The welcome briefing depends on the role.
- **Settings** (main menu and in-game menu): language (main menu only), window mode and size, interface size, automatic scaling, reduce motion.
- **Load game**: the saved games, newest first, with scenario, party, the date in the game and when it was saved.
- Keyboard: Space = End quarter, M = map, 1-6 = the screens, Esc = close / clear the lens, Ctrl+S = quick save, F11 = full screen.

## Display settings and responsiveness

The window is freely resizable (minimum 960 x 540). The interface is laid out in logical pixels for 1280 x 720 and scaled with
`canvas_items` + `expand`, so other shapes get more room rather than bars. `DisplaySettings` adds an automatic factor (small windows get a
smaller logical viewport instead of smaller text, never below 960 x 540), the player's interface size (80-140 %), the window mode (window,
borderless, full screen; F11 toggles), the window size in window mode (maximised "fit to the screen" by default, or 1280x720 up to
3840x2160 where it fits; the last size is remembered; borderless and full screen use the native resolution) and "reduce motion" (no moving dashes). All of it is in the settings dialog (main menu and in-game menu), applies live and is saved in `user://display.cfg` together with the
language.

`Layout` turns the logical size into size classes - compact < 1100 <= regular < 1600 <= wide, short < 640, tall >= 860 - and raises
`Changed` when a class flips; the frame is rebuilt then (sidebar strip, top-bar sizes, dialog sheets).

## Smoke test

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh            # headless
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh /tmp/shots # windowed, writes PNGs
```

Besides every screen of every scenario it opens the main menu, the settings (and switches the language) and the load list, presses a party row on the new-game screen, plays the political scenario as an opposition party
(Civic Right), checks that the policy dialog refuses an unaffordable change and charges an affordable one, and asks a coalition reshuffle
from the Politics screen (shots `menu`, `settings`, `start-party`, `load`, `policy-cost`, `policy-cost-unaffordable`, `reshuffle`, `reshuffle-after`). Decision 0014: as the
opposition it waits for a proposal, answers it with the Support button of the briefing, makes a promise and announces a proposal, and opens the
overview, programme, promises and actions tabs (shots `proposal-briefing`, `proposal-answered`, `party0014-tab0..4`). Decision 0015: it
checks the Mood / Support toggle; as Citizens' Movement it forms the government from the briefing (Adjust, then Form this coalition), leads until a
partner gives an ultimatum and courts it from the briefing, promises from the briefing when the window opens, and checks that x4 stops at a
decision (shots `briefing-formation`, `briefing-ultimatum`, `briefing-promise`). Decision 0016: in the same formation it sends one round of talks with a
thinner offer plus a portfolio and takes a party's counter terms (shot `negotiation`); as People's Union (a junior partner) it gives an
ultimatum from the Actions tab, withdraws it from the briefing and, at the next formation, makes a counter-offer to an AI formateur's offer
(shots `ultimatum-card`, `ultimatum-card-open`, `briefing-own-ultimatum`, `briefing-counter`).

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
window size, so a tiling window manager cannot change the shape, and writes PNGs of the main menu, settings, new-game and load screens, the map (plain, hovered, lens), every
dialog and screen, a chart and the briefing.
