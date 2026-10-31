# 418 - AoF: Quest's player side

**Goal:** #417 imported AoF: Quest's 46 enemy books and left the player side missing, which is why the
system stayed unselectable. Give AoF: Quest heroes: its own roster, its own classes, its own Hero
Builder and campaign screens, and a party that is stamped as its own system.

**Raised by:** Kai, 2026-10-31 - picked "AoF: Quest heroes" as the next build, because it is the last
whole game mode with nothing playable in it.

---

## Notes

### 2026-10-31 - the roster: 46 factions, 161 heroes, from id 7

Army Forge serves AoF: Quest as **two ids under one index slug**, exactly as it serves Star Quest, and
the payloads are indistinguishable:

| | id 7 - this import | id 8 - #417's enemy books |
|---|---|---|
| Beastmen roster | **5 heroes** | 20 units |
| Sample profile | Ndoli Beast Lord 45pts Q6 D6 | Ndoli Warrior 35pts Q4 D4 |

`StarQuestHeroImporter` was a static class with `Slug` and `GameSystem` as private consts; it now takes
a `QuestSystem` record (slug + id + labels) and `--import-starquest` takes `--quest-system`, defaulting
to Star Quest so #415's command line still means what it did. Ran against id 7: **46 of 48 factions
(two are OPR's empty placeholders), 161 heroes, 276 upgrade packages, 2,935 priced options.**

### 2026-10-31 - the static singleton was the real blocker, not the data

`StarQuestData` held one roster behind a `_loaded` flag with the asset folder and both file names
inlined, so **one process could only ever hold one Quest dataset** and the first screen to touch it
decided which system the whole app meant. Same shape as `GameWideConstants` and `HordeRules`, and the
same failure mode: nothing announces it.

Now `QuestDataSet` - an instance per system, cached, lock-guarded, carrying its own folder, file names
and faction-group map. `StarQuestData` survives as a thin Star Quest view over it (about 30 call sites
and three fixtures call it), documented as "new code should take the dataset or the system".

Everything else that assumed one system, fixed in the same pass:

- **`HeroBuilderScreen.GameType` was a `const "GF Star Quest"`** that five other files reached into -
  the save folder, the campaign screen, the lobby's party picker, the headless party loader and a
  fixture. Now `QuestGameTypes.For(system)`, and the screen takes the system in its constructor.
- **The lobby's party picker always loaded Star Quest's folder**, ignoring the lobby's own system, so
  an AoF: Quest lobby would have offered Star Quest heroes and then refused the party built from them.
- **`QuestArmy.Build` stamped every party `grimdark-future-star-quest`** and preferred Star Quest books
  for faction rules. Both now take the system - an AoF: Quest party is an `age-of-fantasy-quest` army
  and reads its rules from AoF: Quest's books. This is the #414 bug one system along, and it would have
  presented identically: a greyed-out LAUNCH button blaming the player's army.
- **`PartyLoader.Resolve` generated Star Quest heroes whatever system asked**, found by running the
  scripted path rather than by reading it.

### 2026-10-31 - what the rules file can and cannot say

`StarQuestRules.json` claims Army Forge as its source but **no code writes it** - it was authored by
hand from scraped content, and `StarQuestImportedRulesAgreementTests` documents the two-sources
position. There is no quest-content endpoint: `/api/rules/common/{id}` returns 39 special rules and
nothing about classes, skills or items, and twelve other plausible paths 404.

What the Star Quest dataset does carry is **both systems' naming**: every class has a `baseName`, and
the base names are the fantasy ones - Monk, Wizard, Rogue, Barbarian, Bard, Paladin, Fighter, Ranger,
Cleric, Druid, Sorcerer, Warlock. Star Quest renames 12 of the 13 (Fighter -> Soldier, Wizard ->
Psychic). The 180 skills are already keyed on those base class names. So `AoFQuestRules.json` is the
shared Quest ruleset at its generic naming, generated rather than retyped, with `sqName` kept on each
class for traceability.

**Three things are genuinely not available, and are empty rather than guessed:** AoF: Quest's shop
consumables, its injury table and its level-1 hero stat line all live in the AoF: Quest **Campaign
Builder**, which OPR serves through no endpoint and which is not among the project's books. The status
conditions ARE available - typed word for word from `AoFQ - Core Rules v2.0.1.pdf` - and the base hero
stat line is carried over from Star Quest and marked as carried. `AoFQuestHeroDataTests` pins the two
empty lists so that "empty" cannot quietly become "invented", and fails the day the book arrives.
**Ask Kai for the AoFQ Campaign Builder PDF** and these three can be done properly.

### 2026-10-31 - it already plays headlessly

`--headless --all-ai --game-system aof-quest --game-mode star-quest-horde --party auto:3 --army2
"armies/AoF Quest/Beastmen - enemy collection.fdgarmy" --seed 7` builds a party of **Monk / Wizard /
Rogue from Saurians, 235pts**, plays **13 rounds and 3 waves** against the AoF: Quest enemy book, and
ends cleanly ("the heroes fell during wave 3"). So the data and the plumbing are proven in a played
game, not only in fixtures.

16 new tests, and the two that carry the weight were sabotage-checked: making `QuestDataSet.For` return
the Star Quest set for both systems (the pre-#418 behaviour) fails 8 of 16, and rewriting the roster
with enemy-shaped data fails exactly `TheRosterIsTheHeroDataset_NotTheEnemyBooks`.

### Still open

- **AoF: Quest is still not lobby-selectable**, and that is now a RULES decision rather than missing
  data. Its book is `AoFQ - Core Rules v2.0.1.pdf` - **the 2.0.1 generation** - while the engine
  implements Star Quest's **2.1.0** for all the shared Quest machinery. The differences the engine
  actually models: power costs (extra action, extra skill and revive are **2** in 2.0.1 against 3 in
  2.1.0), a failed hero skill (**pay 1 power and it still resolves** in 2.0.1; optional and it fails
  in 2.1.0), a failed cast (**buy it back for X*2 power** in 2.0.1; final in 2.1.0), melee reach
  (**2" horizontal / 4" vertical** against a flat 3"), Scout (**12"** against 9"), and Villain (**12"
  and optional** against 6" and mandatory). Blast's spread and Fear's morale-test reading match what
  #414 already built. So making it selectable needs an AoF: Quest profile in `CoreRulesProfile` plus
  per-system Quest power costs - the next slice, and until it exists a selectable AoF: Quest would play
  Star Quest's numbers, which is the thing CLAUDE.md forbids.
- **Whether OPR has published AoFQ 2.1.0** is unconfirmed - a web check found nothing either way, and
  the project holds only 2.0.1. If Kai drops a newer book in, this slice gets shorter.
- **The AoF faction groups are a guess at information design** (Humans / Elves / Dwarves & Halflings /
  Greenskins / Undead / Chaos / Beasts). One map in `QuestDataSet`, trivially changed, and Kai's call.
- **12 skills keep Star-Quest-specific names** (Psychic Focus, Techno-Bolts, Sentinel Meditation...)
  because their generic names are not in any data available here. Recorded in the rules file's own
  `notes`.
- **The engine's `QuestItems`, injury and treasure tables are one shared GF-worded set**, like
  `CoreRuleCatalog`. The AoF: Quest shop will read Star Quest's items until its own are available.
