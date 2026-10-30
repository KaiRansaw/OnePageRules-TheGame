# 408 — AoF has moved to v3.6.0, and AoF: Skirmish is NOT Firefight with fantasy armies

**Status:** open, blocks AoF: Skirmish and AoF: Quest.
**Raised by:** Kai added the AoF / AoFS v3.6.0 rulebooks, 2026-10-29. Read before building.

## Two separate findings

### 1. Age of Fantasy is on v3.6.0; everything built here targets 3.5.1

`OprListImporter.SupportedVersionPrefix` is `"3.5"` and refuses any army book outside it - deliberately,
because importing a newer book into an older engine plays by the wrong rules silently. Army Forge will
now be serving 3.6 books for Age of Fantasy, so **AoF army imports will be refused outright** until the
engine is rebased onto 3.6 for that system.

Grimdark Future is apparently still on 3.5.1, so the two systems are now on **different ruleset
versions at the same time**. The version gate is per-book, so this is survivable - but
`SupportedVersionPrefix` is a single constant for the whole game and will have to become per-system.

### 2. AoF: Skirmish is a different game from GF: Firefight

The plan assumed "AoF: Skirmish is Firefight's ruleset with Age of Fantasy armies", so it would be
mostly wiring. **That is wrong.** From AoFS Core Rules v3.6.0:

| | GF: Firefight (3.5.1, built) | AoF: Skirmish (3.6.0) |
|---|---|---|
| Wounds | **Wound markers**: last model collects markers, rolls 1d6 + markers, 6+ Knocked Out, lower = Shaken | **No markers at all** - normal wounds, model dies at Tough |
| Morale from wounds | None. Army-level test at end of round when down to half its UNITS | Standard: unit tests when wounds leave it at half or less |
| Melee result | - | Compare wounds taken this activation; loser tests; **Routs** if it fails while at half or less |
| Table | 4'x4' | 4'x4' (same) |
| Coherency | 1" / 6" | 1" / 6" (same) |
| Points | 300 | 300 to start, 600 full match |
| Objectives | D3+2, 4 rounds | D3+2, 4 rounds (same) |

So AoFS keeps the *skirmish scale* (table, coherency, points) but uses the **standard** wound and
morale rules, not Firefight's wound-effect system. It is closer to plain Age of Fantasy on a small
table than it is to Firefight.

**Consequence:** AoF: Skirmish is NOT free wiring. It is a game mode whose rules differ from both of
its neighbours, and it needs its own pass.

### Also new in AoFS 3.6, not in the engine

- **Jumping and Falling** - models jump 1"-6" gaps on 2+ per inch, and falling costs wounds. The engine
  currently answers "there are no jumping rolls in this game" and greys out skills that need it (Leap,
  Agility Injector). 3.6 puts jumping in the core rules, so that answer stops being true for AoFS.
- **Command Groups** - Sergeant (AP+1), Musician (+1" moves for its unit and 2 picked units), Banner
  (+1 to Morale Tests from shooting or melee). One model per army. Not implemented.
- **Brace(X)**, **Thrust**, **Shred**, **Parry(X)**, **Surge**, **Spell Amplifier** in the special-rules
  reference - check each against the implemented catalogue before assuming it exists.

## What to do, in order

1. **Read AoF Core Rules v3.6.0** and diff it against the 3.5.1 Age of Fantasy the engine plays. The
   base system may have moved too, which would affect the already-built AoF mode, not just Skirmish.
2. **Make the version gate per-system** so GF can stay on 3.5.1 while AoF moves to 3.6 - otherwise
   bumping one breaks the other.
3. **Then** scope AoF: Skirmish properly, as its own rules pass rather than a wiring job.
4. Decide what happens to the already-built Age of Fantasy mode if 3.6 changed it.

## Do NOT

- Do not widen `SupportedVersionPrefix` to accept 3.6 globally. That would let 3.6 Grimdark Future
  books in too, and play them by 3.5.1 rules without saying so.
- Do not build AoF: Skirmish as a copy of Firefight. The wound and morale systems are different.

## Notes

- 2026-10-29: raised on reading AoFS Core Rules v3.6.0. The AoFS Horde Mode and AoF Horde Mode 3.6
  books are also in the project and unread.
