# 450 — Journal: the game's record of each mission, and export

**Status:** done (engine `2618544`,
app `b531856`, build 0.83, patch B67). Follows #448.

Kai, 2026-11-05, answering the two questions #448 left open: **yes** to the game pre-filling the journal
from what happened, **yes** to exporting it.

## Built

- `CampaignMissionNote.Record` - the game's account of a mission. `AfterGame` writes it from the same
  log it folds into History (result + rewards, casualties, XP, level-ups, random events). The write-back
  adds "Fought against <name>." at the top and the special objective's result.
- `SetRecord` / `AddToRecord` / `RecordFor` in `CampaignJournal`; `SetNote` keeps an entry that still has
  a record when the player clears their page.
- Each mission page shows **WHAT HAPPENED** (read-only) above the player's own box.
- `CampaignJournal.Export` → plain ASCII text; **Export journal** writes `<campaign file> - Journal.txt`
  beside the campaign file, overwriting the last export.

## Decision: separate, not pasted in

"Pre-fill" could mean writing the facts INTO the player's text box. Built as a separate read-only record
instead, so the game never overwrites his writing and he cannot delete the facts by clearing his page.
Told Kai, with the literal version offered as a quick change if he prefers it.

## Limits, stated

- Missions played before 0.83 have no record (shown as "No record - played before the game kept one").
- A one-off opponent the lobby rolled has no campaign file to name it by: "Fought a one-off opponent."
- The write-back path (`StrikeTeamFiles.WriteBack`) needs a played table and is not unit-tested; the
  engine half is, through the real `AfterGame`. Verified by removing the record write: 2 tests fail.

Engine 4751 → 4760, app 8865 (unchanged). Smoke exits 0.
