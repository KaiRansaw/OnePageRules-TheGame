# 443 — Audit every army book against the LIVE official Army Forge

**Goal.** Check our 288 bundled books against the source OPR actually serves, rather than against the
PDFs (which lag it) or against our own snapshot (which is what made Lustbound look verified while it was
wrong in 14 books). Then fix whatever is stale, and leave the check repeatable.

Kai asked for this on 2026-11-01 after spotting that "Army Forge" meant two different things in our
conversations, and asking whether that ambiguity was the source of the stat inconsistencies.

---

## Notes

### 2026-11-01 — done, pushed (build 0.61)

**The naming, first, because it caused the question.** Two different things were both called
"Army Forge": the **official Army Forge** (`army-forge.onepagerules.com`, OPR's own builder and its
API) and the **Army Forge screen** in our game, named after it. From now on: "official Army Forge" for
theirs, "the Army Forge screen" for ours.

**That ambiguity was not the cause of the stat problems**, and the mechanism matters. The in-game screen
has no stats of its own — it loads the bundled `.fdgbook` files, which were imported *from* the official
API. So "checked against Army Forge" was never a circular check against our own screen.

**But something close to circular was happening.** The official site was blocked from the workspace, so
"checked against Army Forge" in practice meant *checked against our bundled copy of it* — official data,
but a snapshot from one date, and only as good as the importer that made it. That is exactly how
Lustbound sat wrong in 14 books with a test confidently pinning the wrong number.

**The block is gone** as of this session, which is what made this audit possible at all.

### What the audit actually found

288 books, all six game systems, compared against live official data. **Thirteen books were stale, in
three clusters:**

| Where | What changed |
|---|---|
| **Shadow Stalkers** (AoF, AoF Skirmish, AoF Quest) | Great Screecher **+10pts** (155→165 in AoF, 330→340 in AoF Quest); Hex Banshee **35→40**; Hex Banshee Staff **10→15** (AoF) and **-5→0** (Skirmish) |
| **Light Gunship**, the five Brothers books, in **Grimdark Future and GF Star Quest** | Strafing Interceptor AA-Cannon's range **halved, 30"→15"**, with its price cut to match (20→5, 75→25, 85→30 depending on book) |
| **Shadow Stalkers** again | gained **Combat Possession**, a rule OPR has added since our import |

Two things worth noting about that result:

- **The Combat Possession reference is new, not latent.** Checked: the old books referenced it nowhere,
  so this was OPR adding content, not a rule resolving to nothing. Verified rather than assumed, because
  the latter would have been a live bug of the Villain shape.
- **Firefight's copies of the Gunship were already current.** The rebalance hit Grimdark Future and
  Star Quest and not Firefight — which is per-mode data behaving exactly as it should, and a quiet piece
  of evidence that the per-system split is now genuinely working.

Everything else — every unit's points, size, quality, defense, weapons, rules, and all 48,155 upgrade
options and their prices — matches the official builder exactly.

### The tooling was broken, and that is the bigger finding

The first run reported **48,170 added and 48,170 removed** across 288 books, with 2 changes. Symmetric
and enormous, which is a matching artifact rather than staleness, so it got measured rather than
believed:

- **Section ids are regenerated on every API response.** 0 of 3,672 matched across Grimdark Future
  alone. `BookDiff` keyed each option as `sectionId + "/" + optionId`, so it paired nothing.
- **Option ids are perfectly stable.** 48,155 of 48,155 found on both sides.

So the key is the option id. Options now use it, with an occurrence suffix for the one unit that reuses
ids across two identically labelled sections (Vampiric Undead's Werewolf Champion, in all three fantasy
systems); sections pair by label plus occurrence, since 19% of units repeat a section label ("Upgrade
with one", up to three times).

**This matters beyond this audit.** `BookDiff` was built in #412 to review the book refresh — "the honest
way to check 1,817 units without reading 1,817 units" — and it could not actually do that: the real
differences were buried in 96,340 noise lines. With the fix the same comparison reported **30 real
differences and no noise**, which is how all three clusters above were found. The old code would have
reported the first run's "2 changed" and hidden the Gunship rebalance entirely.

A false premise stated confidently in a doc comment is worse than no comment. The class doc now records
the measurement instead.

### Two traps the script records

- **Diff per system folder, never the whole tree.** OPR publishes the same faction name once per game
  mode, so a whole-tree diff pairs Grimdark Future's "Blood Brothers" against Firefight's and reports
  every upgrade in both as changed. The first attempt did exactly this and every book appeared twice in
  the summary with identical counts, which is what gave it away.
- **A short or non-200 response must be reported, not imported.** An empty body would otherwise import
  as a book with no units.

### Lustbound, finally closed

The one outstanding question for Kai's Army Forge trip is answered, live, for every book that prints the
rule in either system — Giant Tribes Lust Disciples, Daemons of Lust, Lust Disciples, both systems, all
3.6.0:

> **Lustbound** — "Moves +1" when using Advance, and +2" when using Rush/Charge."

No shooting range anywhere. So the two per-book overrides that invented +4" (and +8" with the Enhanced
form) were authored fiction and are deleted, along with the now-empty
`AofBookOverrides/Giant Tribes Lust Disciples.json` and the dead `Lustbound Boost` /
`Lustbound Boost Aura` pair that no book has ever referenced. All **14** books now match.

The existing test covered the six books that happened to be right and explicitly excluded the four that
were wrong, pending this check. It is replaced by `EveryShippedLustbound_IsPurelyMovement`, which walks
every bundled book — the assertion that would have caught the four, since a per-book override is applied
last and wins (#375 C9), so a correct supplement proves nothing about a book that redefines the rule.

### One thing checked and found safe

Replacing a bundled book regenerates its section ids, and `BuilderList.SectionId` stores them —
`ListCompiler` *throws* on an id it cannot find. That looked like it would break every saved army with
an upgrade. It does not: `BuiltArmyFile.Book` embeds a full snapshot of the source book, deliberately
("decision 6"), so a saved army re-opens against the book it was authored with and never against a newer
catalog. Worth having checked, and worth knowing before the full #412 refresh.

### Verification

- Full `dotnet build` clean; engine **4,607 passed**, app **8,804 passed** (net -2: six lint cases lost
  with the six deleted rule definitions, +3 from the rules the refreshed books gained, +1 new test — all
  four accounted for rather than waved through); headless smoke exits 0.
- **The audit re-run after the fixes reports 0 differences in all six systems** — 288 of 288 books
  identical to the official builder. That is the real verification here.
- 1 sabotage check on the new test: putting the shooting range back into one book's Lustbound →
  `EveryShippedLustbound_IsPurelyMovement` fails and names the book.

### Still open

- **The periodic re-run.** `scripts/audit-against-army-forge.sh` is repeatable but nobody runs it on a
  schedule. It cannot be a CI test — it needs network access and it compares against a moving target —
  so it wants to be a habit, or a scheduled task once the game is further along.
- **The two hero rosters are not covered.** Star Quest's 205 heroes (id 9) and AoF: Quest's 161 (id 7)
  live in their own datasets with their own importers (`--import-starquest`), so this audit does not
  touch them. Same job, different tool; worth doing next.

## Outcome

Done. Patch note B46, build 0.61. Engine `6f97fb1`, superproject `020580a`.
