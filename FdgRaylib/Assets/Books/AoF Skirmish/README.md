# AoF Skirmish army books

Filled 2026-10-31 (#412): 46 books, imported whole from Army Forge at OPR 3.6.0.

Before that this folder was empty and an AoF: Skirmish army played on **Age of Fantasy's** numbers,
which is wrong data rather than an approximation - OPR publishes the same faction once per system and
the versions differ in unit names, unit sizes, points and which units exist at all.

Regenerate with `--rebuild-books <cacheDir> <booksRoot>`; see the project doc
`claude/army-forge-refresh-status.md`. **Never hand-edit a `.fdgbook`** - the rebuild regenerates each
one whole and would eat the edit. Curation goes in `GdfRuleSupplement.json`, `AofRuleSupplement.json`,
an `AofBookOverrides/` file or `BookRenames.json`.
