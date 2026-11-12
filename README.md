# OpenPolitics

An open source political simulation inspired by Democracy 4, extended with coalition
government, intra-party politics and opposition. You lead a party in a real country
(first Czechia, then the USA; start year around 2015), look for a majority and keep your
partners on board.

**Status:** early development. Milestones M0 (foundation) and M1 (simulation core) are in progress.
Czech version: [README.cs.md](README.cs.md).

## Design in one paragraph

A deterministic C# simulation core (`core/`) reads declarative data packs (`data/`) and exposes a
narrow API. Clients (a Godot 4 .NET UI later, a headless CLI now) only ask the core for state and
send it player actions. There is no universal GDP ceiling: values use real units, influences change
rates, and limits come from the model (capital, labour, productivity, budget, inflation, debt).

## Build and run

Requires the .NET SDK 10 (see `global.json`).

```bash
dotnet build
dotnet test
dotnet run --project tools/OpenPolitics.Cli -- --version
```

## Play (text client)

```bash
dotnet run --project tools/OpenPolitics.Cli -- play --packs data/base --lang en
```

You run an illustrative Czechia-like country in the sandbox scenario: change taxes and spending (`set 1 26`), press
Enter to advance a quarter, and use `why unemployment`, `info vat`, `voters`, `values` and `?` to understand what is
going on. `save FILE` / `load FILE` keep a game. Use `--lang cs` for Czech. Politics (elections, coalitions) arrive in
milestone M2; a graphical Godot client in M3.

## Data is illustrative

All data packs use approximate, **illustrative** numbers for real countries and **fictional** parties
and politicians. They are not statistics and not predictions.

## License

Code: MIT ([LICENSE](LICENSE)). Data packs: CC BY-SA 4.0 ([data/LICENSE](data/LICENSE)).
No text, data, art or names from Democracy 4, Hearts of Iron IV or Millennium Dawn are used.

## Playing

- Text client: `dotnet run --project tools/OpenPolitics.Cli -- play --packs data/base --lang en` (Czechia 2015, coalition government; `?` for help).
- Godot client: see [client-godot/README.md](client-godot/README.md) (Godot 4.7 .NET, shares the core and the texts in the packs).
- Headless balancing: `batch --politics --strategy ignorer|keeper|random|extreme|activist` (see `docs/decisions/`).
