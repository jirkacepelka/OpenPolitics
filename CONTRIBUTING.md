# Contributing

Thanks for helping! Code, docs, identifiers and commit messages are in English; the game is localized (cs, en).

- Build and test: `dotnet build && dotnet test`.
- The core is written test-first: add a failing test, then the code.
- Keep commits small, each with one clear purpose.
- Content lives in data packs (`data/`). Validate with
  `dotnet run --project tools/OpenPolitics.Cli -- validate <pack dir>`.
- Never use real names of party members or politicians, and never copy text, data or art from
  other games (Democracy 4, Hearts of Iron IV, Millennium Dawn). Mechanical ideas as inspiration are fine.
- Mods are data only; code execution from packs is out of scope.
