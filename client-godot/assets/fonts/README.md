# Fonts

The client uses **Inter 4.1** by Rasmus Andersson (github.com/rsms/inter), bundled here as `InterVariable.ttf` (roman, axes `wght`
100-900 and `opsz`). Licence: SIL Open Font License 1.1, see `Inter-LICENSE.txt` next to it; the OFL allows bundling and
redistribution with the game as long as the licence travels with the font.

`AppTheme` loads the font in this order:

1. `res://assets/fonts/InterVariable.ttf` (the imported resource; `InterVariable.ttf.import` is committed like every Godot import file),
2. the raw file from disk (a fresh checkout before the editor has imported it),
3. static `Inter-Regular/Medium/SemiBold/Bold.ttf` if someone prefers those,
4. a system font: Inter, Noto Sans, Open Sans, Fira Sans, DejaVu Sans.

Weights and features are set per text role with `FontVariation` (`wght` 400/600/700, `cv05`/`cv08` always, `tnum` for figures).
The layout leaves room for the wider fallback faces, so the game still works if the file is removed.
