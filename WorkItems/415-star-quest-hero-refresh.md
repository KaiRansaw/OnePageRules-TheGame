# 415 — Star Quest hero roster refreshed to OPR 3.6.0, and made repeatable

**Goal.** Bring `FdgRaylib/Assets/StarQuest/StarQuestHeroes.json` — the Hero Builder's whole roster —
onto the current OPR data, and leave behind a command that does it again next time instead of a
one-off script nobody can find.

**Scope limit set by Kai (2026-10-30):** OPR **official** books only. The index is fetched with
`filters=official`, so community books never enter the file.

**Numbering.** Filed as 412 on a clone branched off `main`, which did not have 409-414. Renumbered to
415 on rebase onto `campaign-360`, where 412 is the book refresh and audit. See
`WorkItems/Reconciliations.md`.

---

## 2026-10-30 — done

### The id trap, and the thing CLAUDE.md had wrong

Star Quest is **two** Army Forge game systems, not one:

- **id 9, `grimdark-future-star-quest`** — the units a player can *be*. 8 units for Alien Hives, all
  Heroes, Q6 D6, Grunt Veteran 55pts.
- **id 10, `grimdark-future-star-quest-ai`** — the units a player *fights*. 41 units, Q5 D5, the same
  Grunt Veteran at 100pts, Hive Lord at 980pts.

CLAUDE.md recorded "Star Quest=10" and said AoF: Quest's id was unknown. Both are now fixed there, with
the full nine-id map verified against the live API — **7 / 8** are AoF: Quest's hero and AI halves, the
same split. Had the hero roster been rebuilt from id 10, every playable hero would silently have been
given the enemy's profile.

This work item owns **id 9 only**. The AI side is the army-book job (#412).

### Why the file was stale

`StarQuestHeroes.json` was a snapshot taken **2026-10-22 against OPR 3.5.3**, by a script that was never
committed. The game moved to 3.6.0 in #409, so every hero in the Hero Builder was a version behind —
and unlike the `.fdgbook` catalog, nothing in the repo could regenerate it.

### The two new commands

- **`--import-starquest <out.json> [--cache <dir>] [--diff <report.md>]`**
  (`FdgRaylib/Import/StarQuestHeroImporter.cs`). Fetches the official Star Quest index, every book under
  `gameSystem=9`, and the 39 shared rules from `/api/rules/common/9`, then writes the file whole.
  `--cache` is read-through and write-through, so a second run needs no network.
- **`--diff`** (`FdgRaylib/Import/StarQuestHeroDiff.cs`) compares the previous copy against the new one:
  factions renamed / added / gone, and per-hero points, Quality, Defense, Tough, weapon, gear and rule
  moves. Factions are matched by **uid first**, so a rename reads as a rename rather than as one faction
  dying and another being born.

A wholesale rewrite is safe here for the same reason `--rebuild-books` is safe for the `.fdgbook`
catalog: **nothing in this file is hand-curated.** Every field is a function of the Army Forge response.
If anything ever IS hand-typed into it, that thing has to become a separate input file first, or the
next import will quietly eat it.

### The result

| | before | after |
|---|---|---|
| Factions | 53 (v3.5.3) | **54 (v3.6.0)** |
| Heroes | 199 | **205** |
| Upgrade packages | — | 393 |
| Upgrade options | — | 3,858, every one priced |

**Voidspace Stalkers** is new in 3.6.0 (5 heroes). **Nine factions were renamed** and matched by uid:
the four `Wormhole Daemons of X` dropped the prefix, and `Saurian Starhost` became `Saurian Starhosts`.

Both old and new names are kept in `StarQuestData.GroupByFaction`. **This is not a duplicate of
`BookRenames.json` / `BookFile.PreviousNames` from #412** — those map an OPR rename onto an existing
`.fdgbook` file so a rebuild can find it. `GroupByFaction` maps a faction *name* to the Hero Builder's
picker group (Humanity / Chaos / Xenos). Different inputs, different outputs, both still needed. The
importer itself carries no rename logic at all: it keys on uid, which never changes.

**Gangs of New Eden** and **Wormhole Daemons** are in the official index with **0 hero units** — OPR's
empty parent placeholders. Reported and skipped.

Essentially every hero moved. Two classes of change worth naming because they are not just points:

- **`Unique` became `Unique Hero`** — one rule in 3.6.0 where it used to be `Hero` + `Unique`. Nothing in
  the Quest code keys on either string, so no behaviour changed here. `ListValidator` does key on
  `"Unique"` for `.fdgbook` army lists; that belongs with #412.
- Weapons and gear were reworked across the board. Alien Hives' Grunt Veteran went from a single
  `Razor Claws (A2)` to `Spike-Shooter (12", A1, Assault)` + `Claws (A3)`.

### Which cost field, and why it matters

Each option's `costs[]` row carries three numbers. For one Alien Hives option, under `gameSystem=9`:
`exactCost 2.68`, `cost 5`, `questCost 10` — and under `gameSystem=2` the same row reads
`exactCost 6.04`, `cost 5`, `questCost 20`.

So `exactCost` is recomputed per game system and `cost` is it rounded to the nearest 5. Under
`gameSystem=9` the payload is **already** Star Quest-scaled — the unit costs in that same response are
the Star Quest ones — so **`cost` is the field that belongs with them**. `questCost` there is roughly 3x
`exactCost`, i.e. a second quest-scaling on top of the first; using it would price a 55pt hero's gun at
95pts. The 3.5.3 snapshot used `cost` too, so this is also continuity.

This is the one judgement call in the import. It is isolated in `StarQuestHeroImporter.CostOf`.

### Per-unit prices: packages are cloned, not flattened

One upgrade package can be shared by several heroes, and **53 options across the catalog cost different
amounts depending on which hero buys them** (Dark Prime Brothers' Combat Bike is 115 on one captain and
210 on another). The on-disk shape carries one cost per option, so a shared package that prices
differently per hero is emitted once **per hero** under a derived uid, `"<uid>@<heroId>"`, and that
hero's `upgrades[]` points at its own copy. Nothing downstream can tell: packages are only ever looked
up by uid.

### Two real data bugs the new tests caught

Both were present in the 3.5.3 snapshot as well — the import did not introduce them, it exposed them.

1. **Items that carry weapons lost those weapons.** A Combat Bike is an `ArmyBookItem` whose `content`
   holds a `Heavy Bike-Rifle` weapon plus Fast and Tough(9). The importer flattened the whole item into
   one gear string, so the rifle never reached the hero's weapon list — and the `"Replace Heavy
   Bike-Rifle"` section it unlocks became permanently unreachable, because `StarQuestData.HasTargets`
   could never find the target. **18 sections across 10 factions were dead this way** (bikes, mounts,
   rifle-arrays). Items now contribute their weapons **and** their gear line.
2. **Core rules had no wording.** A book defines its own special rules but not the shared ones its units
   lean on, so `Tough`, `Strider`, `Scout`, `Fearless`, `Flying`, `Fast` and friends had no description
   in any faction glossary — nothing for the Hero Builder to hover. `/api/rules/common/9` supplies all
   39 in **Star Quest's own wording**, which differs from Grimdark Future's (its `Tough` reads "once a
   model has taken as many wounds as its Tough value, it is killed" — the skirmish reading, and the same
   divergence the "Core rules differ per system" section of CLAUDE.md is about). Those are now folded
   into each faction's glossary, for the rules that faction actually uses.

### The guard test

`FdgRaylib.Tests/StarQuestHeroDataTests.cs`, 9 tests over the shipped file:

- the roster loads, 50+ factions, 190+ heroes
- every faction is on a 3.6.x version stamp
- every hero has an id, a positive cost, Q/D in 2..6, Tough >= 1 and at least one weapon
- every `upgrades[]` uid resolves to a package in the same faction
- every option is priced and grants something
- every `"Replace X"` section is reachable from some loadout (this is the test that found bug 1)
- all roster text is ASCII (the importer runs everything through `OprBookImporter.AsciiFoldJsonValues`)
- every rule a hero carries has a description (this is the test that found bug 2)
- no faction falls into the picker's "Other" bucket (this is what catches the next rename)

### Engine change

`OprBookImporter.AsciiFold` / `AsciiFoldJsonValues` went `internal` -> `public` so the app-side importer
can use the one fold instead of copying its table. No behaviour change.

### Verified on the `campaign-360` base

- `dotnet build` clean.
- Engine suite **3,828 passed / 0 failed**.
- App suite **3,051 passed / 0 failed** (3,042 at the base + the 9 new).
- Headless smoke exits 0.
- `AppVersion.BuildNumber` 0.02 -> **0.03**.

### Still open

- **Nobody has opened the Hero Builder and looked.** The data is checked by test, not by eye. Two or
  three heroes compared against the Army Forge site is the remaining step.
- **Existing hero files.** A saved hero keeps the stats it was created with — it does not re-read its
  base hero — so nothing breaks, but a hero built on 3.5.3 numbers now differs from a freshly built one
  of the same type. Worth a look before deciding whether that needs a migration.
- **AoF: Quest** is the same job on **id 7**, and has no data file at all yet.
- `ListValidator`'s `"Unique"` check against 3.6.0 `.fdgbook` data — belongs with #412.
