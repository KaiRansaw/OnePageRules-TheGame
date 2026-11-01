# 421 - Army saving, the picker, and the folder scan that never refreshed

**Goal:** five changes the owner reported after playing with the Army Builder and Army Forge, all of
them about getting a list built, saved, found and loaded without fighting the app.

**Raised by:** Kai, 2026-10-31, from his own testing. Verbatim where it matters:
- *"In the Army Builder, remove the points limit option. The player can see how many points you have,
  so the limit isn't really needed when building armies."*
- *"when entering Army Forge, the Import popup always pops up. can we get rid of it? there's a button to
  import, so i don't want it auto popping up all the time."*
- *"Click Army Forge -> Choose Game mode -> Choose Faction -> Build Army -> Save -> In game window
  appears 'Name Army' -> you name army -> list is automatically saved into the folder for the game mode
  that was chosen."*
- *"I made a new army for GFF, saved it into .../armies/GF Firefight, but when loading into GFF create
  game screen and trying to choose an army, nothing comes up... I just did a rebuild of the game, and
  that seemed to have loaded the new army lists. I DO NOT want to have to reboot the game every time i
  want to make a new army."*
- *"in the Choose Army screen, the Army Lists don't show the set name for that list."*

**Bucket:** all five are PERMANENT. Only the look of the new name box is rented, and it was built plain
and left alone.

---

## Notes

### 2026-10-31 - all five, and the one that was a real bug

**The restart bug (4) was the only genuine fault, and the diagnosis was not what the symptom said.**
The owner blamed the rebuild; it was the relaunch. `LobbyScreen` held the catalog as a
`static readonly Lazy<ArmyCatalog>` - one scan per process - behind a comment explaining that
re-reading 12 MB of JSON per lobby would be waste "because the armies folder doesn't change under us".
That was true when the only way to add an army was to drop a file in before launching. The in-game Army
Forge made it false and nobody revisited the comment.

Worth noting what was **not** wrong, since the report suggested both: his folders were right
(`armies/GDF`, `armies/GF Firefight` are exactly where the lobby looks), and `armies/` is not copied to
the build output, so there was only ever one location. Nothing was misfiled - it just was not re-read.

`ArmyCatalogCache` keeps the caching and adds `RefreshIfChanged()`: a directory walk comparing every
list's path, size and write time against the last scan. That is what makes it safe for `OnShown` to ask
on **every** lobby entry, including every return from the army picker, which happens constantly - the
check is a stat walk, not a reparse. Writes call `Invalidate()` unconditionally, since the app already
knows.

**A bug in my own change, caught by its own test**: the first version read
`if (_current != null && _signature == now) return false`. `Invalidate` leaves `_current` null until
something next reads it, so after any invalidation every subsequent check reported a change - a method
the lobby runs on every entry would have rescanned forever. The signature has to decide alone; the
catalog's laziness already covers "nothing cached".

**Save (3) is correctness, not just convenience.** `ArmyPaths.EntriesFor` picks the lobby's list for a
Game Type **by the folder the file is in**, so a Firefight army written into `armies/GDF` is missing
from the Firefight picker and offered in the Grimdark Future one, where the launch gate refuses it.
Taking the destination from the army's own `GameSystem` removes a way to misfile a list rather than
asking the player not to. `GameSystemInfo.FromSlug` is the missing half of that mapping, derived from
`Slug()` rather than typed out again.

The folder comes from the **army**, not the screen's dropdown, so a list loaded for editing is re-saved
where it belongs even if the dropdown has moved on.

**The picker (5)** listed `ArmyCatalogEntry.Name`, the name stored *inside* the file. `ListCompiler`
falls back to the book's name when the list has none, so a folder of Forge-built lists read "Army,
Army, Army" while the files beside them had perfectly good names. It shows `FileName` now; `Name` still
forms the wire identity and still appears in the preview when it differs, so nothing was lost.

**The points limit (1)** never restricted anything - #003 makes this screen advisory, and an over-cap
list always saved and launched. It asked for a number in order to colour a total the player can read.
`ArmyListFile.PointsLimit` stays: saves carry it, the lobby has a real limit for a match, and Load Game
reads it to summarise a save.

**The import modal (2)** was #403's deliberate choice, reversed on the owner's say-so. The flag and the
`OpenPopup` path stay - an intentional open still needs them - so the two tests were **inverted rather
than deleted**: what they pin now is that nothing arms it behind the player's back.

### Caught on the way

`CalculatorTextSafetyTests` failed on the new name modal: `ImGui.TextDisabled` takes a **printf format
string**, so a `%` in a folder name would print stack garbage. A real bug in new code, caught by an
existing lint rather than by review. Switched to `UiText.Disabled`.

### Known limits, recorded rather than cut

- A list added from outside the app **while sitting in the lobby** is not seen until the lobby is left
  and re-entered, since the check runs on entry rather than on a timer. Watching the folder would cover
  it; it did not seem worth a `FileSystemWatcher` for a case that does not come up in play.
- The **Army Builder** screen still uses a file dialog to save. The owner's workflow named the Forge,
  and the Builder edits an existing file more often than it creates one. Its writes do invalidate the
  cache, so the restart bug is fixed for both.
- **Overwrite is silent.** Saving under a name that already exists replaces it with no prompt, the same
  as the old file dialog's own confirm would have. Worth a confirm if it ever bites.

### Verified

Engine **4,217 passed / 0 failed** (3 skipped), app **8,766 / 0 failed** (2 skipped), build clean,
headless smoke exits 0. 16 new tests; both logic halves sabotage-checked (breaking the change detection
fails 4, breaking the ordering fails 3). Engine `fe5fd44`, superproject `ed8f923`, build 0.42.
