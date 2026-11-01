# 426 — The campaign serves all four systems, not just Firefight

**Goal.** Open the campaign to the four systems with a Campaign Rules v3.6.0 book of their own —
Grimdark Future, GF: Firefight, Age of Fantasy, AoF: Skirmish. Listed in `claude/fdg-todo.md` as
"campaign lobby entries for GF, AoF and Skirmish (the engine serves all four; only Firefight has a
button)".

**Status:** done 2026-11-01.

---

## 2026-11-01 — it was not a missing button

The todo read like a UI gap. It was not: **the after-game write-back paid every campaign army
Firefight's price list**, so the other three could not have been offered correctly even with a button.

`StrikeTeamFiles.WriteBack` called `StrikeTeamRules.AfterGame`, and `StrikeTeamRules.Numbers` is
hard-wired to `CampaignNumbers.Firefight`. A Grimdark Future campaign would have banked +80 for a win
and +160 for a loss instead of +150 and +300, charged +15/+25 a level instead of +25/+55, and done it
silently and permanently. The engine's own `OneLoopPaysEachBooksMissionReward` could not catch it,
because the mistake is in which numbers get passed, not in the loop that spends them.

Three more places were Firefight-only in the same way: the armies folder the campaign screen reads
(`armies\GF Firefight`), the price list the New screen quotes, and the system stamped onto a new
campaign army.

### What changed

- `StrikeTeamFiles`: `FolderFor(system)`, `LoadAll(system)`, `LoadAllSystems()` (all four, each entry
  carrying its folder's system) and `NewPath(system, name)`. New campaign armies are named
  `Campaign - <name>.fdgarmy` rather than `Strike Team - <name>.fdgarmy`; old files still load, the
  scan is a glob.
- `StrikeTeamFiles.SystemForPath` — **the folder decides**, see below.
- `WriteBack` pays `CampaignNumbers.For(SystemForPath(...))`, and its wording stopped saying
  "strike team" and "Firefight Campaign".
- `StrikeTeamScreen`: a game picker on the New view (re-scoping the starting-army list, since every
  mode has its own army-builder stats and a Firefight list is not a legal start for a GF campaign),
  per-system treasury text, the system shown on each row of the campaign list, and the mission cap
  measured only against campaign armies **of the same game** — a 300-pt Skirmish army was otherwise
  dragging a GF mission's limit down to 625 pts for an opponent it can never face.
- `MainMenuScreen`: "Firefight Campaign" is now "Campaign".
- `CampaignRules.Start` takes the option flags the next two work items need.

### The rule worth writing down: the folder decides the system

A campaign army lives in its own system's armies folder, which is also how the lobby decides which
armies a game of that system accepts. So the file's **location** is the one fact about it that cannot
go stale, and `SystemForPath` reads that first, falling back to the `StrikeTeam.GameSystem` block only
for a file kept somewhere else entirely.

That is also the migration path, and the reason it is not the other way round: a campaign army written
before `GameSystem` existed as a field deserializes to `GrimdarkFuture` (0) however it was really
played, so trusting the block would have paid an old Firefight strike team GF's rewards — exactly the
bug this item exists to fix, in reverse. `LoadAllSystems` stamps the folder's system onto the block as
it loads, so the correction persists the next time the file is saved.

### Tests

`FdgRaylib.Tests/CampaignFileSystemTests` — 13 cases: the folder read for each of the four systems and
for a subfolder, the folder beating a disagreeing block (with the reward number asserted, not just the
enum), the fallback outside any campaign folder, `NewPath` landing where the lobby will serve it, and
`CampaignSystems` being exactly the four — explicitly **not** Star Quest or AoF: Quest, which have
their own campaign book on their own screen. Sabotaged by making the block win: 5 of the 13 fail.

## Outcome

Done. All four books' campaigns are creatable and are paid their own book's numbers.
