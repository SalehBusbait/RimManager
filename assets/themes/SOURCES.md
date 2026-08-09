# Design handoff v2 — provenance

The accepted second-generation design system (ten themes, theme-bound accents,
per-theme logos). **Master lives in the Claude Design project**
`e06496a8-64ae-439a-bfa8-490d7d025d87` (claude.ai/design); this folder is the
implementation snapshot of the text deliverables, taken 2026-08-07 after the
fix round was verified (all six requested fixes landed — see
`docs/design_brief_2/SEND2/FIXES_PROMPT.md` for what was asked).

Snapshotted here: `README.md` (roster, collision resolutions, derivation rules,
main-window spec, logo spec — its inline value tables are replaced by a declared
pointer to tokens.css, the same generated output) · `SCREENS.md` (every other
surface) · `implementation-notes.md` (Avalonia specifics, the no-renames token
map) · `theme/tokens.css` (the ten generated dictionaries — copied and
integrity-checked: 10 blocks × 62 declarations, spot values verified).

Upstream only (fetch from the Design project when the phase needs them):
the five `*.dc.html` mockup canvases (the VISUAL ground truth — they break ties
against all prose including the files here), `theme/themes.js` (same values as
tokens.css plus per-theme collision notes, which README's table also carries),
`theme/tokens-table.md` (duplicates the README table), `support.js` (mockup
runtime, not design), and the twenty `logos/badge-*.svg` / `logos/lockup-*.svg`
(needed at the brand-pipeline phase).
