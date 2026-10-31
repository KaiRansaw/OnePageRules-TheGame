# 412 - Getting every unit's data right for 3.6, across all four game systems

**Goal:** every unit's points, name, size, weapons and upgrades correct for OPR 3.6, and correct
*per game system* - Grimdark Future, GF: Firefight, Age of Fantasy, AoF: Skirmish each have their own
book, and the same unit genuinely differs between them (#406).

**Raised by:** Kai, 2026-10-30. **3.6 is the full release, not a beta** (Kai, same day), so the data is
worth importing now.

**The plan and runbook:** `claude/army-forge-refresh.md`. Groundwork for fetching: `WorkItems/411`.

---

## The state of the data, measured 2026-10-30

- **87 bundled books: 47 Grimdark Future, 40 Age of Fantasy.**
- **Zero Firefight books. Zero Skirmish books.**
- Versions: 79 stamped `OPR v3.5.3`, 8 `OPR v3.5.2`.

So this is two jobs, and the second one is the bigger surprise:

1. **Refresh the 87** from 3.5.x to 3.6.
2. **Fetch the books that were never downloaded at all** - Firefight and Skirmish. Until they exist, a
   Firefight army plays on Grimdark Future points and unit sizes, which is exactly the fault #406
   records (Alien Hives Assault Grunts: 105 pts for 10 in GF, 30 pts for 3 in Firefight; the Hive Lord
   is not in the Firefight book at all).

## 2026-10-31 - Star Quest: its own books, the 2.1.0 AI, and a rule that could never fire

**Star Quest now plays on Star Quest's books.** 54 enemy books imported under
`FdgRaylib/Assets/Books/GF Star Quest/`, `GameSystems.GFStarQuest` added, and
`GameSettings.ArmySource` moved off Grimdark Future's. #406 left "does Star Quest publish its own
enemy book?" open and defaulted to GF's; it does, and the points are on a different scale -
Alien Hives' Grunt Veteran is 40pts in GF against 100pts here, so every wave and mission was buying
roughly 2.5x the enemy its budget was meant to pay for. Pinned by
`Tests/GameSystemArmySourceTests.cs`, including the negative (a GF army must fail a Star Quest
lobby's filter) and the AoF: Quest gap (falls back to AoF, and must stay unselectable while it does).

**The index lists 56 books, not 54, and that is correct.** `Gangs of New Eden` and
`Wormhole Daemons` return HTTP 200 with **0 units and 0 rules** from Army Forge (checked
2026-10-31). Nothing was dropped on import - those two have no Star Quest data upstream yet. Worth
re-checking on the next refresh rather than treating 54 as the final number.

**AI step 1 on the 2.1.0 wording.** "In line of sight **or within 12" but not in line of sight**."
Under 2.0.1 breaking line of sight made the whole enemy force lose interest and walk to its
objective, which is the single biggest reason Star Quest played as hide-and-seek. An unseen enemy
still cannot be *shot* (the shoot target is still drawn from the visible list, deliberately) but can
be charged, so the usual answer for a hero behind a wall is now to come round the corner after it.
`Tests/StarQuestAiPlanTests.cs` covers both halves - inside 12" unseen is engaged, past 12" unseen is
still ignored - plus "line of sight has no distance limit", so widening step 1 cannot quietly narrow
it.

**AI step 2's "or Hold and shoot if in range" was already the behaviour.** The action resolver picks
SHOOT over MOVEMENT when the target is already within range, which is a Hold. Left where it is and
documented there rather than given a new plan kind: `EQuestAiMove.Hold` routes to the solo bot and is
excluded from the shoot-anyway fallback, so a `HoldAndShoot` member would have been a refactor with a
regression risk and no behaviour change. Recorded as a deliberate non-change, not as done-by-accident.

**The Villain aura has never been able to fire, in any game, ever.** This is the find of the pass.
The engine looked for a rule named `"Villain"`. **No book has one.** Every Star Quest enemy book
instead defines a rule *named* `"Hero"` whose description is Villain's text verbatim - "AI units
within 6" use the Villain's Qua for Morale Tests if it's better than their own, as long as the
Villain isn't Shaken" - and puts it on its boss profiles (Alien Hives: Grunt Veteran, Snatcher Lord,
Prime Warrior, Hive Lord; 4 of 41 units). Verified against live Army Forge data for Alien Hives, Orc
Marauders, Human Defense Force and Robot Legions. So the rule had a radius, a Shaken check, a
better-Quality check and a log line, and its first condition was unsatisfiable.

`QuestChallenges.VillainRuleNames` now holds both spellings and `IsVillain` is the one lookup. It is
read as Villain only inside `VillainMorale`, whose single caller is gated on
`QuestActions.IsQuestGame`, so Grimdark Future's own Heroes - same marker rule, used for unit-joining
and force org - are untouched. `Tests/VillainAuraTests.cs` (14 tests) pins the radius, the name
mapping, Shaken, better-Quality-only, own-side-only, party heroes (which carry `Hero` too), reserve,
dead and two-Villain cases. `FdgRaylib.Tests/VillainCarrierShippedDataTests.cs` pins the *link*: a
name the engine matches must be a name the shipped books carry. **That link is the assertion that was
missing** - "Villain is implemented" and "the books parse" were both true the whole time.

**A lesson worth the space:** a rule that is fully implemented and entirely unreachable looks exactly
like a rule that works. Nothing in play, and nothing in the engine suite, could tell the difference.
Any rule whose trigger is a *name* needs one test that the name exists in the data.

**The radius was edited back to 12f twice while this was being written** - once with a `// SABOTAGE`
marker, once bare, both times leaving the doc comment above it reading 6". Restored to 6f with the
2.1.0 text quoted in the file. 12f is not a harmless default: it is 2.0.1's radius under 2.1.0's
documentation, and no one watching a morale test can tell which radius produced it.

**Still open from this pass:** the Star Quest books stamp `"version": "OPR v3.6.0"` in their
metadata, but Star Quest's ruleset is 2.1.0 - cosmetic, and only visible if a book's version is ever
shown. AoF: Quest's own books (Army Forge id 7 heroes / id 8 enemies, 48 of them) are still not
imported.

Engine 3,886 / 0 (3 skipped). Villain carrier fixture 5 / 0.

## 2026-10-30 - the audit tool

Engine 3,740 / 0. UI 2,997 / 0. Smoke exits 0. Pushed to `campaign-360`.

### `ArmyBuilding/BookDiff.cs` + `--book-diff <A> <B> [report.md]`

Compares two sets of books unit by unit and writes a Markdown report: points, quality, defense, unit
size, weapons, rules, every upgrade option and its price, and an upgrade section's shape (which decides
how many times an option may be taken, so a change there is a real rules change even at the same price).

**One mechanism, two uses** - they are the same question asked twice:

- `--book-diff <old books> <new books>` - did the refresh do what it should, and what moved? This is how
  1,817 units become reviewable.
- `--book-diff <GF books> <GFF books>` - are the systems really carrying their own data? **A Firefight
  book that comes out identical to its Grimdark Future namesake is the signal that something fell back
  to the wrong data.** The report says "Nothing differs at all" in words for exactly that case, rather
  than printing an empty page.

Matching rules, and why:
- **Units by name.** Ids are per-book, so the same unit in two systems has two different ids and only
  the name joins them.
- **Upgrade options by id within a unit.** That is what OPR keeps stable across versions, and what a
  price is keyed to. (The same option id costs differently on different units, which is why the key is
  the pair.)
- **An unpriced option reads as "no published price", never 0 pts** - a 0 there looks like a free
  upgrade and would quietly misprice an army.

12 tests, including both of Kai's own Alien Hives examples as cases.

### Skirmish game-system ids - NOT VERIFIED

`ArmyForgeBookService` now knows `grimdark-future-firefight = 3` and `age-of-fantasy-skirmish = 5`.
GDF=2 and AoF=4 were verified against the live API; **these two are OPR's documented ordering taken on
trust**, because the site is refused at Claude's gateway.

Mitigations rather than a guess left to rot:
- `--book-system-id <slug>=<id>` overrides either, so a wrong one is a one-flag fix, not a code change.
- `--book-urls` marks an unverified system's lines, so it is visible before anything is fetched.
- **Verify with Kai's own example:** import one Firefight book and check Alien Hives Assault Grunts read
  30 pts for 3, not 105 for 10. If they read 105, the id is wrong and the fetch quietly returned the
  Grimdark Future book.

## Still to do

- Fetch and import, on Kai's machine (`claude/army-forge-refresh.md`).
- Verify the two skirmish ids.
- **Where do Firefight and Skirmish books live on disk?** The army folders are per game type already
  (`armies/GF Firefight`), but `Assets/Books` is one flat folder with the system recorded inside each
  file. Decide before importing ~87 more books into it.
- #406's framework work: the lobby showing only its own system's lists, and the in-game builder asking
  which system first. The data arriving is what makes that worth finishing.
- Re-import armies that were imported before the system fix - they are stamped `grimdark-future` with
  no record of their origin, so there is nothing honest to migrate.
