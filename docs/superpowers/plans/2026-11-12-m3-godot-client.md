# M3 Godot Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (native; the user chose no approval stops).

**Goal:** A playable Godot 4 (.NET) client for the C# core: start screen, overview, policies with sliders, voters, politics (polls, formation, coalition), party actions, an influence graph with "why did this change", a news log, save/load, Czech and English.

**Architecture:** The client only talks to the core's narrow API (`PackLoader`, `Simulation`, `Game.State/Apply/EndTurn/Save`). All shared text and number formatting lives in `core/Presentation` (moved out of the CLI) so both clients say the same things. UI is built in code (small `.tscn` files only attach scripts), one `Tab` class per screen, one `Session` object owning the `Game`.

**Tech Stack:** Godot 4.7.2 mono (in `~/.local/godot`, checked: runs on .NET 10.0.15 and builds with `net10.0`), C#, the core project by project reference.

**Spec:** the brief (`~/Downloads/Politická simulace ... .md`), section "M3 Klient": Godot UI, influence graph, coalition panel, first scenario. Gate: a first-time player plays the first turns without help and the Czech campaign is playable to the end.

## Global Constraints

- No Godot types in `core/`. The client project references `core/OpenPolitics.Core.csproj` (net10.0).
- Packs are read from a directory (default `../data/base`, override with env `OPENPOLITICS_PACKS`); the language is chosen at the start screen (cs or en).
- Numbers shown in real units (via `Fmt`), never normalised; voter moods as words and bars.
- A `--smoke` run (`godot --headless --path client-godot -- --smoke`) plays scripted quarters through every screen and exits 0 or 1; a windowed run with `--screenshots <dir>` writes PNGs of every tab.
- Never push or create remote repos. Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Tasks

1. **Presentation in core.** Move `Localizer` and `Fmt` from `tools/OpenPolitics.Cli/Play` to `core/Presentation` (public, namespace `OpenPolitics.Core.Presentation`), add `Mood` (`Overall(groups, satisfaction)`, `Label(double)` returning the `ui.mood.N` key) and update the CLI and tests. Test: existing suite stays green; `MoodTests`.
2. **Skeleton.** `Session` (loads packs, owns the `Game`, language, last report, event history, save/load to `user://saves`), `Theme` helper, start screen (scenario list, language, seed), main layout (header with date and End quarter / 4 quarters buttons, `TabContainer`), `--smoke` plumbing. Smoke: start both scenarios and end 4 quarters.
3. **Overview, Policies, Voters tabs.** Overview: key figures, growth, mood, last quarter's events; Policies: grouped rows with `HSlider` in real units, pending marker, tooltip from `desc.*`, applies on release; Voters: groups with size, mood word and bar.
4. **Politics and Party tabs.** Politics: poll bars with government markers, seats, phase text, formation controls (auto proposal, draft with invite/agree, offer accept/reject, early election), coalition panel (loyalty bar and word, commitments with progress, ultimatums). Party: resources, relations, platform, measures with stance buttons, actions (campaign with group and effort, organise, court, criticise, platform shift), the three opposition goal bars.
5. **Influence graph and news.** Graph tab: values as nodes on a circle, edges coloured by sign with thickness by strength, click a value to highlight neighbours and list last quarter's contributions (the "why"); News tab: history of events with quarter stamps.
6. **Verification and docs.** `scripts/client-smoke.sh`, screenshots of every tab (windowed run), `client-godot/README.md` (how to run: `DOTNET_ROOT`, Godot path), update root README/docs.
