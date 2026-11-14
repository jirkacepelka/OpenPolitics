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

A full-screen **map of the country** is always behind the game, in the spirit of Democracy 4: the indicators of the country in the middle,
the policies around them (tax, spending, regulation in three colours), a curved arrow for every effect. Hover or click a node to light up what it
touches and, in a card on the right, why it moved last quarter.

- **Top bar** (Cities: Skylines style): date, six live indicators with a trend arrow (click one for its chart), political capital, save, menu,
  *4 quarters* and the big *End quarter* button.
- **Dock** at the bottom: Overview, Policies, Voters, Politics, Your party, News. One floating glass panel opens at a time; the dock icon
  of Politics shows a red dot while you have a decision to make.
- **Start-of-quarter briefing**: after each quarter a popup lists the news and the decisions that wait (an offer to join a coalition with accept
  and reject, a government to form, ultimatums).
- **Policies**: a slider per policy in real units, a *follows GDP / inflation / fixed amount* switch for spending, and an infographic of what the
  highlighted move would do (arrows on the values it pushes, the voters it pleases or annoys, the budget).
- **Charts**: tiles on the overview (GDP real and nominal, growth, unemployment, inflation, deficit, debt, mood, party polls, partner loyalty,
  voter groups, health, education, crime, environment, inequality); click for a large chart with a 1 / 3 / 5 year or all-time range and a
  crosshair that reads values.
- **Your party**: stats, a square compass (economic left-right, liberal-authoritarian), the platform with a position and a priority slider per policy,
  promises before an election, relations, actions.
- **Start screen**: language, scenario, seed and the electoral threshold (0-10 %).

## Smoke test

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh            # headless
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh /tmp/shots # windowed, writes PNGs
```

## Look and feel

Redesigned after a design critique (five rounds of screenshots; notes in decision record 0010): Fira Sans (falls back to the system font if it is not
installed), a dark palette with lighter floating panels, about 40 vector pictograms drawn in code (every policy, indicator and voter group gets one
from its id), a Cities: Skylines-style top bar and toolbar, policy cards with a category filter and an inspector, a parliament half-circle on the
Politics panel. There are no image assets to maintain. Check changes with `scripts/client-smoke.sh <dir>` and look at the PNGs.
