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

## Screens

Overview (key figures, history charts, last quarter), Policies (sliders in real units), Voters, Politics (polls,
government formation, coalition loyalty and commitments), Your party (resources, relations, campaign, criticism, stances,
platform), Influence graph (click a node: what it influences and why it changed), News.

## Smoke test

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh            # headless
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh /tmp/shots # windowed, writes PNGs
```
