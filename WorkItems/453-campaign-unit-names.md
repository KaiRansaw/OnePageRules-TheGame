# 453 — Campaign unit names

**Status:** done (engine `20053dd`, app `1a5a695`, build 0.85, patch B69). **Spec:** campaign-flow-spec 3d.

Pre-filled with the book's name (as before), renamed on the camp screen. Name large with the type small
underneath; a roster row whose name is not its type shows the type too.

- `StrikeUnit.UnitType`, set by `Start` and `Recruit`. Older units: loadout roster entry, else name.
- `CampaignRules.Rename`: non-empty, unique in the army ignoring case, <= 40 chars, ASCII-folded, one line;
  fixes the type onto the sheet first.
- `UnitFileEntry.Name` is what the whole game reads and units are matched by sheet id, so a rename reaches
  the log, battle report and hovers with no other change. Upgrades recompile stats only; name survives
  (tested).

## Not done - recorded in claude/fdg-todo.md

- **The type is not shown in-game.** A renamed unit's hover in a game shows its name only. No in-game
  screen reads campaign sheets yet, so this is its own piece of work.
- **Unit names in other game modes** - the spec scopes names to campaigns for now.

Tests: `FutureOfDarkGrimness/Tests/CampaignUnitNameTests.cs` (8). Engine 4760 -> 4768.
