# M0 foundation design

Status: **draft for user approval** · Gate: CI green locally + both specs approved.

## Repository layout

```text
OpenPolitics.sln
core/          OpenPolitics.Core        simulation, no Godot types
core.tests/    OpenPolitics.Core.Tests  xUnit
tools/         OpenPolitics.Cli         validate | run | batch
data/base/     base pack (manifest + YAML + localization cs/en)
data/examples/ sample-mod, broken-mod (negative validator test)
client-godot/  README only until M3
docs/          specs/, decisions/, modding/
.github/       workflows/ci.yml, issue templates
```

## Data pack format

- `manifest.yaml`: `id`, `version`, `dependencies`, `load_after`, `license`, `illustrative`.
- Folders: `values/ policies/ voter_groups/ events/ countries/ electoral_systems/ scenarios/ localization/{cs,en}.yaml`.
- Merge rule: packs apply in load order; the later one wins. Redefining an existing id requires `override: true`, otherwise
  the validator errors. Event conditions use a small sandboxed expression language; packs contain no executable code.

## Validator (`OpenPolitics.Cli validate <pack...>`)

Checks typed deserialization, dangling references, zero-delay cycles, missing localization keys (cs and en), unit/range
sanity, duplicate ids without `override`. Errors are readable and include file and line. A JSON Schema is generated for
editor hints. Exit code is non-zero on errors; CI runs it on `data/base` and expects failure on `data/examples/broken-mod`.

## CI

`.github/workflows/ci.yml`: restore, build, test, `validate data/base`, short `batch`. Ubuntu, Windows, macOS matrix
(determinism check across platforms). Until the GitHub repo exists, the same commands run locally.

## Open-source files

README (EN + CZ), LICENSE (MIT), `data/LICENSE` (CC BY-SA 4.0), CONTRIBUTING, CODE_OF_CONDUCT, issue templates,
`docs/modding/` with the format description and the sample mod. Commits are small, in English.
