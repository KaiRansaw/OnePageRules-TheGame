# 406 — Armies must carry their own game system's stats, not be shared across systems

**Status:** open. Foundation only - nothing built yet.
**Raised by:** Kai, 2026-10-29, immediately after #M2 made Firefight lists importable.

## The problem

OPR publishes a **separate army book per game system**. The same named unit has different points,
different weapon profiles and different special rules in Grimdark Future, GF: Firefight and
GF: Star Quest. They are not the same data scaled - they are different entries.

`OprListImporter` (M2, 2026-10-29) made `gff` and `aofs` share lists importable by mapping them onto
their PARENT system's slug:

```
["gff"]  = GameSystems.GrimdarkFuture
["aofs"] = GameSystems.AgeOfFantasy
```

That was the minimum change to unblock Firefight testing, and it works in the sense that the list
imports and is pickable. **But it throws away which system the list is actually for.** Consequences
Kai hit straight away:

- An imported Firefight army is stamped `grimdark-future`, so it also shows up in a plain Grimdark
  Future lobby, where its points and profiles are wrong for the game being played.
- Points totals do not match what Army Forge shows, because the per-unit costs came from one system's
  book and the game treats them as another's.
- Weapons and abilities differ from the book the player actually built against.

The bundled book snapshots under `FdgRaylib/Assets/Books/` have the same shape of problem:
`MatchBundledBook(faction, gameSystem)` matches on the parent slug, so a Firefight army can get
Grimdark Future rule definitions attached to it.

## What the fix has to do

1. **Record the real source system on the army file.** `ArmyListFile.GameSystem` needs to distinguish
   `gff` / `aofs` / `gfsq` from `gf` / `aof`, rather than collapsing them. This is the foundation -
   everything else follows from it.
2. **Make the lobby filter honest.** `GameSystemInfo.ArmySource` currently answers
   `EAllowedGameSystems.GrimdarkFuture` for both `GrimdarkFuture` and `GFFirefight`. Each game type
   should accept only armies built for it. `EAllowedGameSystems` grows members to match.
3. **Keep the bundled books per system.** `MatchBundledBook` must match the exact system, and
   `Assets/Books/` needs the Firefight / Skirmish books alongside the existing ones, or attach nothing
   rather than attach the wrong book's rules.
4. **Decide what happens to armies already imported** under the M2 behaviour. They are stamped
   `grimdark-future` and there is no way to tell from the file which system they came from. Options:
   leave them (they are wrong only if used in the wrong mode), or re-import. Kai's call - do not
   silently migrate.
5. **Star Quest** uses its own hero data (`Assets/StarQuest/`) and the Hero Builder, so heroes are not
   affected. The ENEMY army in a Star Quest game is a GF army file, and that is correct per the rules -
   worth confirming against GFSQ Core Rules v2.0.1 rather than assuming.

## Open questions for Kai

- Should a Firefight lobby refuse a Grimdark Future army outright, or allow it with a warning? Refusing
  is correct; allowing is friendlier if he wants to reuse a list he has not rebuilt yet.
- Is it worth an in-game army builder for Firefight / Skirmish, or is importing from Army Forge enough?
  (He flagged in QA Notes that there is no in-game builder for those systems.)

## Do NOT do

- Do not try to convert an army between systems by scaling points. The entries genuinely differ; a
  conversion would be invented data.
- Do not revert M2's import mapping before the replacement is in. It is the only way Firefight armies
  get into the game at all right now, and sessions 7 and 8 depend on it.

## Notes

- 2026-10-29: raised. M2's mapping is a deliberate, documented stopgap - see the comment on
  `OprListImporter.SupportedGameSystems`, which should be updated when this lands.
