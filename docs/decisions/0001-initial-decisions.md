# ADR 0001 – Initial technical decisions

Date: 2026-11-12 · Status: proposed (pending user review together with the specs)

## Environment facts (verified 2026-11-12)

- .NET 10 is the current LTS (support until Nov 2028). .NET 8 and .NET 9 reached end of support on 2026-11-10.
- Latest stable Godot is 4.7.2 (Aug 2026). Godot 4.8 (dev, stable expected Q4 2026) requires .NET 10 or later.
  The .NET requirement of Godot 4.7.x was **not confirmed** from documentation; verify before M3.
- Installed locally: .NET SDK 10.0.404 in `~/.dotnet`. Godot is not installed (not needed before M3).

## Decisions

1. **Core target framework: `net10.0`.** LTS, and what Godot 4.8 requires. If M3 starts on Godot 4.7 and it needs
   net8, we multi-target `net10.0;net8.0` for `core/` (no API changes needed; core uses no new-only APIs).
2. **Data format: YAML** (YamlDotNet). Human-readable, comments, good C# support. JSON Schema generated for editor hints.
3. **Licenses:** code MIT, data packs CC BY-SA 4.0.
4. **Turn:** one quarter (`dt = 0.25` years), start year 2015; both set by the scenario.
5. **Majority size:** country parameter (Czechia: simple majority, 101 of 200). Used from M2.
6. **Repository:** local git, working name OpenPolitics (namespace `OpenPolitics.*`). Public GitHub repo only after the user
   approves the specs and the name.
7. **Bounded vs unbounded values:** economic and social values use real units and have no ceiling. Attitudes
   (voter satisfaction, party loyalty) are bounded to [-1, 1] by definition, since they are not economic quantities.
