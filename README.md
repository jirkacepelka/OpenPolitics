# OpenPolitics

An open source political simulation in the spirit of Democracy 4, with coalition government. You lead a party in a real
country (first Czechia, starting in 2015), win a majority, keep your partners on board and run the economy quarter by quarter.

Parties and politicians are **fictional**, the numbers are **illustrative**, not statistics or predictions.
Czech version: [README.cs.md](README.cs.md). Status: early development.

## Play

Download the build for your system from the [latest release](../../releases/latest), unzip and run. Nothing to install.
The builds are not code-signed, so Windows and macOS warn on first start; the release notes say how to get past it.

## Run from source

Needs the .NET SDK 10 and, for the graphical client, the Godot 4.7.2 .NET editor.

```bash
dotnet test                                                          # tests
dotnet run --project tools/OpenPolitics.Cli -- play --packs data/base --lang en   # text client
GODOT=/path/to/godot scripts/play.sh                                 # graphical client
GODOT=/path/to/godot scripts/export.sh linux                         # release build (linux, windows or macos)
```

Pushing a tag such as `v0.1.0` builds and publishes the Linux, Windows and macOS packages.

## How it is built

- `core/`: deterministic C# simulation, no Godot types. Reads data packs and exposes a small API.
- `data/`: data packs (YAML): indicators, policies, voter groups, parties, electoral systems, scenarios, cs/en texts.
  Mods are data only, see [docs/modding](docs/modding/format.md).
- `client-godot/`: the Godot client. `tools/`: command-line client, validator and balance batches.
- Design decisions are in [docs/decisions](docs/decisions).

## License

Code: MIT ([LICENSE](LICENSE)). Data: CC BY-SA 4.0 ([data/LICENSE](data/LICENSE)). The Inter font is under the SIL OFL.
No text, data, art or names from Democracy 4, Hearts of Iron IV or Millennium Dawn are used.
