# OpenPolitics

Open source politická simulace ve stylu Democracy 4, s koaličním vládnutím. Vedete stranu v reálné zemi (nejdřív Česko,
start v roce 2015), získáváte většinu, držíte partnery a čtvrtletí po čtvrtletí řídíte ekonomiku.

Strany a politici jsou **fiktivní**, čísla **ilustrativní**, nejsou to statistiky ani předpovědi.
Anglická verze: [README.md](README.md). Stav: raný vývoj.

## Hraní

Stáhněte si sestavení pro svůj systém z [posledního vydání](../../releases/latest), rozbalte a spusťte. Nic se neinstaluje.
Sestavení nejsou podepsaná, takže Windows a macOS při prvním spuštění varují; postup je v poznámkách k vydání.

## Ze zdrojů

Potřebuje .NET SDK 10 a pro grafického klienta Godot 4.7.2 .NET.

```bash
dotnet test                                                          # testy
dotnet run --project tools/OpenPolitics.Cli -- play --packs data/base --lang cs   # textový klient
GODOT=/cesta/ke/godot scripts/play.sh                                # grafický klient
GODOT=/cesta/ke/godot scripts/export.sh linux                        # sestavení (linux, windows nebo macos)
```

Tag jako `v0.1.0` sestaví a zveřejní balíčky pro Linux, Windows a macOS.

## Jak je to postavené

- `core/`: deterministické simulační jádro v C#, bez Godotu. Čte datové balíčky a nabízí malé API.
- `data/`: datové balíčky (YAML): ukazatele, opatření, skupiny voličů, strany, volební systémy, scénáře, texty cs/en.
  Mody jsou jen data, viz [docs/modding](docs/modding/format.md).
- `client-godot/`: klient v Godotu. `tools/`: příkazová řádka, validátor a vyvažovací dávky.
- Rozhodnutí o návrhu jsou v [docs/decisions](docs/decisions).

## Licence

Kód: MIT ([LICENSE](LICENSE)). Data: CC BY-SA 4.0 ([data/LICENSE](data/LICENSE)). Písmo Inter je pod SIL OFL.
Nepoužíváme texty, data, grafiku ani názvy z Democracy 4, Hearts of Iron IV ani Millennium Dawn.
