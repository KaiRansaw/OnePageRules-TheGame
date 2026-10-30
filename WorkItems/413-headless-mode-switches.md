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

### 2026-10-30 (second pass) - `--party`, and `--all-ai` on the lobby path

The follow-up named below ("Scope deliberately cut"): Star Quest's local side is a **party**, not an
army list, so the flags alone launched it and nothing more.

**`--party`** gives slot 0 a party, three ways (`FdgRaylib/Cli/PartyLoader.cs`):

| Form | What it plays |
|---|---|
| `--party` / `--party auto` / `--party auto:4` | A party generated from the shipped Army Forge data |
| `--party a.fdghero b.fdghero` (or comma-separated) | Those heroes |
| `--party "heroes/GF Star Quest"` | Every hero in that folder |

On a Quest game system or a Star Quest game mode the flag is **optional** - `CliApp.WantsParty()` routes
slot 0 through the party loader anyway, because a Quest game cannot be played with an army list at all
and there is no army-shaped default worth falling back to.

Decisions worth keeping:

- **The generated party is built from the real data, not hand-written statblocks.** `StarQuestData`
  already ships the Army Forge factions, heroes, classes and skills, so the party is three Human Defense
  Force base heroes given Soldier / Sentinel / Psychic, each with its class feature and its level-1
  skills. A party of classless statblocks would play and would exercise none of the ~120 skills. There
  is still a statblock fallback for a build whose `Assets/StarQuest` folder is missing, and it says so.
- **Deterministic**, so two scripted runs are comparable. Pinned by a test.
- **A loaded hero forgets its `SourceFile`.** `QuestHeroSync` keys off it, and these runs exist to crash
  on purpose - a scripted party must not be able to write XP, levels or injuries back over Kai's saved
  heroes. Headless never calls `WriteBack` today; this makes it impossible rather than merely unlikely.
- **Too many heroes is an error, not a truncation** (#413's standing rule: a run that quietly plays
  something other than what was named makes every result it prints meaningless). The message lists the
  heroes so the caller can pick 4.
- **`--party` with `--army1` is refused** - they both set slot 0.
- **Quest systems select the Star Quest AI themselves** unless `--ai-profile` says otherwise. The lobby
  offers "Add Star Quest AI" and nothing else on a Quest game, so a scripted run taking the solo-rules
  bot would have been testing the wrong opponent.

**`--all-ai` now works on the ordinary headless lobby path**, not only `--scenario`. This is what turns
Star Quest from launchable into testable, and it is the more important half of the two:

> Every CLI resolver answers with its EOF default, which is the **first** option offered. A
> human-slot party therefore takes "Move" or the first skill every activation and never leaves its
> deployment - no objective held, no mission completed, so no reward, boss fight or exfiltration can
> ever be reached.

Slot 0 is built the way `ScenarioLauncher.BuildResume` builds an all-AI slot: an AI registry from
`AiProfileFactory.BuildRegistry`, a `ScenarioAiPlayerController`, and the human game left unwired
(it owns no player, and `AssignInterfaces` throws building the chat endpoint in that state). The
narrative comes out through slot 0's controller `logSink`.

**Measured, each playing to a real result:**

| Run | Outcome |
|---|---|
| GF: Star Quest - Horde, generated party, human slot | 7 rounds, fell in wave 2 (EOF defaults; heroes stand still) |
| GF: Star Quest - Horde, `--all-ai` | **51 rounds, 9 waves held, 90 XP, high score 810** |
| GF: Star Quest - Mission, `--all-ai` | **39 rounds**, 5 reinforcement waves, mission failed |
| GF: Star Quest - Mission, human slot | 6 rounds, 1 reinforcement wave, mission failed |
| `--party "heroes/GF Star Quest"` | 2 heroes, 115 pts, two different factions |
| `--party auto:4` | 4 heroes, 155 pts, four classes |
| GF: Firefight - Horde, `--all-ai` | defenders held 12 rounds, high score 540 |
| Grimdark Future - Standard, `--all-ai` | tie, 4 rounds |
| `--party` + `--army1` / missing file / 1 hero / 5 heroes / `auto:9` | each refused, naming the reason |

Suites: engine 3,742 passed, UI 3,026 passed (29 new), headless smoke exits 0.

**Two findings from playing it.**

1. **Reinforcements DO arrive and DO act.** QA 5.2 says alertness hit 6, the splash said reinforcements
   incoming, and no models were placed. In a headless Mission run reinforcements deployed **five times**
   (`rolled 3 corner 3`), and the arriving unit activated on the following round each time. So the
   engine half works and QA 5.2 is either a drawing/visibility problem or something narrower than the
   note describes - worth re-testing on Kai's machine before anyone goes looking in the deployment code.
   One oddity noticed in passing, not chased: a freshly arrived reinforcement reports "has already moved"
   on its first activation, which may well be deliberate (arriving spends its move, as Ambush does).
2. **Injury rolls vary.** QA 3.33 wondered whether the injury table is really being rolled, after two
   heroes both got Blinded Eye. Two failed missions here produced Spinal Injury / Smashed Leg / Chest
   Wound and then Crushed Spirit / Broken Arm / Smashed Leg - six draws, six different entries. Not
   proof of a fair roll, but no sign of a stuck table.

**The remaining gap, said out loud:** `StarQuestAi` is the **enemy** flowchart - shoot or charge the
nearest hero, else head for the nearest objective. It has no Discover, no objective interaction and no
skill use, so bot-played heroes **fight** a mission but never **complete** one. Star Quest Horde is fully
reachable this way (its XP comes from holding objectives, which the move-to-objective step achieves - 90
XP over 9 waves). Star Quest **Mission** rewards, the boss-fight offer and exfiltration are still out of
reach, because all three sit behind "all objectives complete". Checking those needs either a hero-side AI
that pursues objectives and search tokens, or unit tests written straight against the reward arithmetic
from the Campaign Builder 2.1.0 spec. The second is much cheaper and is the right next move.

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

Open. All flags are in and verified, including `--party` and `--all-ai` on the lobby path, so every game
mode can now be played headlessly to a real result. What remains is not a flag: a bot-played party fights
a Star Quest **Mission** but cannot complete its objectives, so mission rewards, the boss-fight offer and
exfiltration stay unreachable from a played game. See the gap note above.
