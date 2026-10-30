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
