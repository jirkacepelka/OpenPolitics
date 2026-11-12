# OpenPolitics

Open source politická simulace inspirovaná hrou Democracy 4, rozšířená o koaliční vládnutí,
vnitrostranickou politiku a opozici. Vedete stranu v reálné zemi (nejdřív Česko, pak USA;
start kolem roku 2015), hledáte většinu a udržujete si partnery.

**Stav:** raný vývoj. Rozpracované jsou milníky M0 (základ) a M1 (simulační jádro).
Anglická verze: [README.md](README.md).

## Návrh ve zkratce

Deterministické simulační jádro v C# (`core/`) čte deklarativní datové balíčky (`data/`) a nabízí
úzké API. Klienti (později Godot 4 .NET, nyní headless nástroj v příkazové řádce) jen čtou stav
a posílají akce hráče. Neexistuje žádný univerzální strop HDP: hodnoty jsou ve skutečných jednotkách,
vlivy mění tempo změny a limity vznikají z modelu (kapitál, práce, produktivita, rozpočet, inflace, dluh).

## Sestavení a spuštění

Vyžaduje .NET SDK 10 (viz `global.json`).

```bash
dotnet build
dotnet test
dotnet run --project tools/OpenPolitics.Cli -- --version
```

## Data jsou ilustrativní

Všechny balíčky obsahují přibližná, **ilustrativní** čísla reálných zemí a **fiktivní** strany a politiky.
Nejsou to statistiky ani předpovědi.

## Licence

Kód: MIT ([LICENSE](LICENSE)). Datové balíčky: CC BY-SA 4.0 ([data/LICENSE](data/LICENSE)).
Nepoužíváme texty, data, grafiku ani názvy z Democracy 4, Hearts of Iron IV ani Millennium Dawn.
