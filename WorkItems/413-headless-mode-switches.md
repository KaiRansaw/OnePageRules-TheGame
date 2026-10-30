# 413 - Headless switches for every game system and mode

**Goal.** Let a headless run pick its game system, game mode, play style, points and army files from the
command line, so every mode can be played without a window - and so the person testing does not have to
be the only one who can reach Firefight, Skirmish, Horde, Star Quest or Game Master.

Kai asked for this directly (2026-10-30) after six patches stacked up untested: "Let me test things
myself" was the option he picked. The point of the item is not the flags; it is that the flags turn an
untestable pile into a testable one.

## Why it mattered immediately

**The very first scripted Firefight run crashed the game.** Not a flag bug - a real engine bug that had
been sitting in the shooting path the whole time:

`ApplyWoundsStage` projected the wound assignment's model order with `(IModel)p.Model`, where `p.Model`
is a `DataBinding<ModelData>`. Casting a class to an unrelated interface **compiles** - the compiler has
to allow it, since some subclass might implement it - and then throws `InvalidCastException` at runtime.
The line only runs on the skirmish branch, so:

> **No shot that landed a wound in GF: Firefight or AoF: Skirmish had ever resolved.** Every one of them
> faulted the state machine instead of killing a model.

Fixed to `(IModel)p.Model.GetValue()`, which is how the rest of that file reaches a model. Swept the
whole repo for the same shape - `(IModel)` and `(IUnit)` casts not going through `GetValue()` - and this
was the only one; the others cast off `ModelData`/`UnitData` values, which is correct.

The reason it survived: `FirefightRules.ApplyWounds` has an `order` parameter, the stage is the only
caller that passes it, and **no test had ever passed it**. The unit tests all called the 5-argument
overload.

## Notes

### 2026-10-30

**Flags added** (all optional; absent means "ask", exactly as before):

| Flag | Values |
|---|---|
| `--game-system` | `gf`, `gff`, `aof`, `aofs`, `gfsq`, `aofq` (+ aliases) |
| `--game-mode` | `standard`, `horde`, `star-quest-horde`, `star-quest-mission` |
| `--play-style` | `standard`, `game-master` |
| `--army-points` | any integer |
| `--army1` / `--army2` | path to a `.fdgarmy` |

Design decisions worth keeping:

- **A scripted run never blocks on a prompt.** If `Scripted` is set and a slot named no file, that slot
  silently takes the built-in test army. Before this, a run that named only `--army1` sat spinning on
  "Player 2 - choose army" until stdin hit EOF, which is noise in every log.
- **A named file that will not load is an error, never a fallback.** `ArmyLoader.LoadPath` returns null
  with a reason and `CliApp` throws naming the slot and the path. Quietly playing the two-unit test army
  instead of the army asked for would make every result meaningless.
- **Points default per system.** Skirmish systems take 300 unless `--army-points` says otherwise, since
  2000 is meaningless at skirmish scale.
- `AllowedGameSystems` is set from `GameSystemInfo.ArmySource(system)`, so a Firefight run accepts the
  armies Firefight should accept (this is #406's seam, reused rather than re-decided).

**Verified end to end**, each playing to a real result:

| Run | Outcome |
|---|---|
| GF: Firefight - Horde | defenders held 12 rounds, high score 2520 |
| GF: Firefight - Standard | AI win, 4 rounds |
| AoF: Skirmish - Standard | AI win, 4 rounds |
| AoF: Skirmish - Horde | defenders held 12 rounds |
| Grimdark Future - Horde | defenders held 12 rounds |
| Age of Fantasy - Standard | AI win, 4 rounds |
| GF: Star Quest - Horde / Mission | runs, heroes fall in wave 1 (see below) |
| Game Master | tie, 4 rounds |
| `--army-points 1000` | header reads 1000 pts |
| bad `--army1` path | throws naming the slot and path |

Suites: engine 3742 passed, UI 2997 passed, headless smoke exits 0.

**Scope deliberately cut, said out loud:** Star Quest wants a 3-hero **party**, not an army list. Handing
it a 1000-pt Grimdark Future army is why both Star Quest runs end in wave 1 - that is the flags doing
what they were told, not a new bug. A `--party` flag is the follow-up; until then Star Quest is
launchable headlessly but not meaningfully playable.

**Tests added** (`FirefightRulesTests`):

- `WoundEffects_HonourTheAssignmentsOrder_FromPendingWoundBindings` - builds the order from real
  `PendingWounds` the way the stage does, and checks the **nominated** model dies. It nominates the last
  model rather than the first, so a path that ignores the order fails instead of passing by luck. This is
  the first coverage `order` has ever had.
- `ADataBindingIsNotTheModelItHolds` - writes the trap down: `IModel` is not assignable from
  `DataBinding<ModelData>` but is from `ModelData`, so this cast can only ever fail at runtime and the
  compiler will not catch a repeat.

## Outcome

Open. Flags are in and verified; `--party` for Star Quest is the remaining piece.
