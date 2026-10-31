# 417 - AoF: Quest's own army books

**Goal:** AoF: Quest was the last system still playing on a parent system's data. Import its own enemy
army books from Army Forge, wire the system to them, and say exactly what is still missing.

**Raised by:** Kai, 2026-10-31 - "Import the AoF Quest books", straight after #414/#416 finished the Star
Quest and Horde passes. #412 had left AoF: Quest as a recorded gap with the id already identified.

---

## Notes

### 2026-10-31 - the enemy books, 46 of them

Army Forge serves AoF: Quest the same way it serves Star Quest: **two game-system ids under one index
slug**, with the same faction names and indistinguishable payloads. Verified live against Beastmen
(uid `TciwNI3AOMXAM-dr`) before importing anything:

| | id 7 `age-of-fantasy-quest` | id 8 `age-of-fantasy-quest-ai` |
|---|---|---|
| What it is | the player's **hero roster** | the **enemy** army books |
| Beastmen roster | 5 units | 20 units |
| Sample unit | Ndoli Beast Lord 45pts Q6 D6 | Ndoli Warrior 35pts Q4 D4 |

Imported **id 8**: the index lists 48 books, two are OPR's empty parent placeholders with no units
(Rift Daemons and one other), one is renamed-away in `BookRenames.json`, so **46 books** were baked into
`FdgRaylib/Assets/Books/AoF Quest/` and 46 full-roster enemy collections into `armies/AoF Quest/`.

**The data is genuinely its own, and that was measured rather than assumed.** AoF: Quest prices per
model at roughly 3x Age of Fantasy, because a Quest game buys individual models instead of squads:

| | Age of Fantasy | AoF: Quest |
|---|---|---|
| Beastmen Hakemba | 40pts | 110pts |
| Beastmen Heavy Chariot | 135pts | 395pts |
| Dwarves Berserker Lord | 55pts | 140pts |
| Orcs Brute Orc Boss | 95pts | 255pts |
| Orcs Great Tusk-Beast Guard | 165pts for 3 | 160pts for 1 |

So the pre-#417 fallback was doing to AoF: Quest exactly what #412 found had been done to Star Quest:
serving a book whose numbers are about a third of what the mode's points are meant to buy.

### 2026-10-31 - the bake shipped 19 dead rule names, and only one test noticed

The first bake produced 46 books that loaded, compiled, deployed and played - and in which **19 rule
names resolved to nothing**: `Ethereal` (23 references), `Vinci Tech` (25), `Retreating Strike` (14),
`Drakesworn` (11), `Precision Feat` (8), the whole `Grounded` family, `Traversal`, `Great Banner` /
`Great Musician` / `Great Sergeant`, `Good Fighter`, `Bloodthirsty Fighter` and more. A Goblin Fanatic
with Traversal that could not traverse; an Ethereal unit that was not ethereal.

Cause: `--rebuild-books` decides which rule supplements to apply from a `bool fantasy` that listed Age of
Fantasy and AoF Skirmish by name. AoF: Quest is a fantasy system and was not on it, so
`AofRuleSupplement.json` and the per-book overrides were never applied - the GDF supplement alone has
none of those rules. One line, now with a comment saying a new fantasy system is not optional.

Worth recording as a class of failure, because it is the same one #414 kept finding from the other end:
**the data was present, structurally valid and inert.** `EveryRuleReferenceResolves` was the only thing
between that and a shipped build.

### 2026-10-31 - what the import moved, and the one upstream quirk

The corpus went 242 books -> 288, which moved 20 census pins in the ShippedData fixtures (the churn
`ShippedBooks`' own comment warns about). All re-taken; the structural assertions beside each of them
passed untouched, which is the signal that the growth is the only thing that changed. Two populations
grew with genuinely new carriers rather than new editions of old ones: Regenerative Strength picked up
the Avatar of Flame and the Hydra (fantasy units with no GF edition, so a third army joined that
fixture's list), and Surprise Attack picked up the Desert Titan, now the largest carrier in the corpus
at 24 references.

One new entry on the unreachable-Replace-target allowlist: **Venom-Dragonfly**, on AoF: Quest's Wood
Elves. Confirmed against the raw Army Forge payload before allowlisting it - the Eternal Guardian
Ancient carries a Master Great Hammer and a Stomp, and OPR's shared package hangs a "Replace
Venom-Dragonfly" section on it anyway with a null `replaceWhat`. Same quirk as the eight already there,
and AoF: Quest is simply the first fantasy system in that fixture's scope.

### Still open

- **The player side does not exist.** AoF: Quest heroes are Army Forge **id 7** - a separate dataset, 5
  units per faction at hero prices - and there is no AoF equivalent of `Assets/StarQuest/`
  (`StarQuestHeroes.json` + `StarQuestRules.json`), no AoF hero classes/skills/shop items, and no Hero
  Builder path for them. `StarQuestData` is a static singleton over one folder with a hand-written
  faction-group map, so this is a real slice, not a second import run. **AoF: Quest therefore stays
  unselectable in the lobby** - pinned, with that reason, in
  `GameSystemArmySourceTests.AoFQuest_TakesItsOwnBooks_AndIsStillNotSelectable`.
- **AoFQ: Quest's core rules are not read yet.** `AoFQ - Core Rules v2.0.1.pdf` is in the project docs
  and has not been compared against the engine the way Star Quest's was in #414. `CoreRulesProfile`
  exists now and has a `StarQuest` profile; whether AoF: Quest shares it or needs its own is unknown.
- **Age of Fantasy Regiments (id 6) is still absent entirely** - no slug, no books, not in the enum.
