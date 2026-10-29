# 406 — Armies must carry their own game system's stats, not be shared across systems

**Status:** open. Foundation / framework only - stats and book data come later.
**Raised by:** Kai, 2026-10-29, immediately after M2 made Firefight lists importable.
**Decisions taken:** Kai, 2026-10-29 (below). Build the framework now; fill the data in later.

## The problem

OPR publishes a **separate army book per game system**. The same named unit can differ in points,
unit size, available units and upgrade options between Grimdark Future and GF: Firefight.

Kai's own examples (Alien Hives):

| | Grimdark Future | GF: Firefight |
|---|---|---|
| Assault Grunts | 105 pts for 10, combine to 20 | 30 pts for 3, combine to 6 |
| Hive Lord (Hero) | available | **not in the book at all** |
| Snatcher Lord | same points, stats and weapon stats as GFF | same base, but **upgrade costs differ** and some upgrades differ (e.g. Hive Protector) |

So it is **not** a wholesale different dataset. The pattern is: same core profiles much of the time,
but different **unit sizes**, **points**, **which units exist**, and **upgrade options/costs**. A
conversion by scaling points is therefore impossible - the differences are editorial, not arithmetic.

M2 (2026-10-29) made `gff`/`aofs` importable by mapping them onto their parent system's slug:

```
["gff"]  = GameSystems.GrimdarkFuture
["aofs"] = GameSystems.AgeOfFantasy
```

That unblocked Firefight testing but **throws away which system the list is for**, so an imported
Firefight army is stamped `grimdark-future` and is offered in a plain GF lobby with the wrong points.

## Decisions (Kai, 2026-10-29)

1. **Lobbies are strict. No warnings, no "allow anyway".** Each game type shows ONLY its own lists:
   - **GF** -> only GF lists
   - **GF: Firefight** -> only GFF lists
   - **AoF** -> only AoF lists
   - **AoF: Skirmish** -> only AoFS lists
2. **Horde inherits from its parent game type**, it is not a system of its own:
   - GF Horde uses GF lists/stats; GFF Horde uses GFF; AoF Horde uses AoF; AoFS Horde uses AoFS.
   - So the army source keys off the **game system**, not the game mode.
3. **In-game army builder for every system: build the framework now, fill the data later.** Kai wants
   the scaffolding in place (per-system books, the builder screen able to target a system) without
   committing to stat values yet.
4. **Do not finalise stats or book contents yet.** OPR is currently in a **beta** reworking unit stats,
   so anything entered now is likely to change. Build so that swapping the data later is cheap -
   data-driven, not hard-coded.
5. **Importing stays as-is for now.** It works, and it is how armies get in while the builder is built.
6. **Armies imported before the fix get re-imported**, not migrated. They are stamped
   `grimdark-future` with no record of their origin, so there is nothing honest to migrate from -
   paste the share links again once the system fix lands.
7. **The builder asks which game system first**, then offers only that book's units, sizes and
   upgrades. Not "build once, flag it legal in several systems" - unit sizes and costs genuinely
   differ per book, so one army cannot be correct in two systems at once.

## Open question - still to check

- **Do GF: Star Quest and AoF: Quest have their own unit stats for the ENEMY army?** Heroes are built
  in the Hero Builder from `Assets/StarQuest/` and are unaffected. But the enemy force in a Star Quest
  game is a GF army file today. Check GFSQ Core Rules v2.0.1 / AoFQ Core Rules v2.0.1 before assuming
  that is right. If Star Quest has its own enemy book, it becomes a fifth/sixth system here.

## What the framework has to do

1. **Record the real source system on the army file.** `ArmyListFile.GameSystem` must distinguish
   `gff` / `aofs` (and `gfsq` / `aofq` if the check above says so) from `gf` / `aof` instead of
   collapsing them. **This is the foundation - everything else follows from it.**
2. **`EAllowedGameSystems` grows a member per system**, and `GameSystemInfo.ArmySource` returns the
   exact system for each `EGameSystem` - with Horde modes inheriting their parent's system per
   decision 2. Today it answers `GrimdarkFuture` for both `GrimdarkFuture` and `GFFirefight`.
3. **Bundled books become per-system.** `MatchBundledBook(faction, gameSystem)` must match the exact
   system; `Assets/Books/` gains the Firefight / Skirmish books. Until a book exists for a system,
   attach **nothing** rather than the wrong system's rules.
4. **The army builder targets a system.** The builder and Army Forge screens pick which system's book
   they are building against, and stamp it on the saved file. Unit size ranges, points and upgrade
   lists all come from that book - so per-system differences are data, not code.
5. **Armies already imported under M2** get re-imported (decision 6). Do not write a migration.

## Do NOT do

- **No points-scaling conversion between systems.** The differences include units that do not exist in
  one book and upgrades that differ in kind, not just cost. A conversion would be invented data.
- **Do not revert M2's import mapping before the replacement is in.** It is the only way Firefight
  armies get into the game right now, and test plan sessions 7 and 8 depend on it.
- **Do not hard-code stat values.** OPR's beta will change them.

## Notes

- 2026-10-29: raised, then scoped with Kai's decisions the same day. M2's mapping is a deliberate,
  documented stopgap - the comment on `OprListImporter.SupportedGameSystems` should be updated when
  this lands.
