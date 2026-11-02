# 440 - Dense and Unstable terrain (AoF: Quest only)

**Status:** done (2026-11-01).
**Came from:** `claude/rules-audit.md` finding 8. Kai approved it with a scoping rule: *"If the other
books don't include these 2 extra terrain features, only add them to Quest."*

---

## Goal

AoF: Quest's Beginners Guide v2.1.0 defines four terrain types the engine has no equivalent for. Two of
them need no elevation and are the ones built here:

- **Dense** - *"Units may draw line of sight into and out of dense terrain, but not through it to targets
  on the other side."*
- **Unstable** - *"If any model in a unit moves over 6" when moving into or through unstable terrain,
  then it counts as Dangerous terrain."*

**Steep** and **Climbable** are the other two; both need elevation, which is parked by Kai's own call
until the Godot port.

---

## Scope: ONE system, not "the Quest systems"

Kai's rule was "if the other books don't include them". They do not, and the check mattered:

- **GF: Star Quest Core Rules v2.1.0 was read end to end.** Its terrain section lists **Cover, Difficult
  and Dangerous** and nothing else. So the obvious grouping - "the Quest systems", the pairing used
  everywhere else in this codebase for `QuestRulesProfile` and `CoreRulesProfile` - is **wrong here**.
- The four standard books list the same three.
- So `QuestTerrainTypes.AppliesTo` is `system == EGameSystem.AoFQuest`, and a test asserts it is false
  for the other five, naming Star Quest specifically.

**Provenance worth knowing:** the only current source is AoF: Quest's **Beginners Guide**, because the
project has no AoFQ Core Rules at 2.1.0 (a gap CLAUDE.md already records). The guide states these as
rules rather than suggestions and the version policy lists it as current, so it governs - but "our only
source is a beginners guide" is the kind of thing to say out loud rather than discover later.

---

## What shipped

**`ETerrainType.Dense` and `ETerrainType.Unstable`**, both documented with the book's own wording.

**Dense, in `TerrainData.EvaluateSightLine`.** Blocks only when the line crosses the piece with BOTH ends
outside it:

```
Dense && DoesPathIntersectZone(a, b) && !IsPointWithinZone(a) && !IsPointWithinZone(b)
```

That is the whole rule, and it is the thing a plain `Blocking` flag cannot express. Checked after
`Blocking`, so a piece carrying both is simply Blocking - a wall with undergrowth is still a wall.

**Unstable, in `MovementUtilities.DoesPathCrossDangerousTerrain`.** An Unstable piece counts as Dangerous
only when `GetTotalMoveDistance(move) > UNSTABLE_TERRAIN_SAFE_MOVE_INCHES` (6"). Folded into that method
rather than its call sites because it is the one chokepoint the movement executor AND the GUI's move
preview already ask - so the preview warns about unstable ground and the executor tests for it without
either knowing the type exists.

**Per-system application in `QuestTerrainTypes`**, applied at every place a pool entry becomes a
`TerrainData` - auto layout, loaded layout file, alternating placement, points placement. A Quest table
set up one way and not another is the defect that only shows up in whichever mode nobody tested.

---

## Two things that would have shipped broken

**1. The executor pre-filters terrain to Dangerous before the distance check runs.**

`RollDangerousTerrain` built its list as `relevantTerrain.Where(t => t.HasFlag(Dangerous))` and passed
that to `DoesPathCrossDangerousTerrain`. So the new Unstable branch would have been handed a list with no
Unstable piece in it - **correct code, fed nothing.** The filter now includes Unstable, with a comment
saying why it is wider than its name.

And the sibling method `RollActivationDangerousTerrain` deliberately does **not**: that is the "or that
activate in it" half of the rule, and Unstable is conditional on *moving* over 6". A unit that starts its
activation standing in rubble has not moved, so it tests for nothing. Both sites carry the reasoning.

**2. The piece mapping is keyed on NAME, which rots silently.**

The guide keys its mapping on what a piece IS - *"Forests - Difficult + Cover + Dense"*, *"Ruins - Cover
+ Unstable"* - and the engine's flags cannot tell a forest from a ruin, since both are Cover + Difficult.
So name matching is the faithful reading rather than a shortcut, but a renamed pool piece would silently
stop getting its type. `EveryMappedPieceName_StillExistsInThePool` walks the real pool and fails by name
if one goes missing; sabotaging one name ("Forest" -> "Forrest") fails it.

My first draft of the name lists was guessed ("Woods", "Jungle", "Thicket", "Ruins") and **none of those
pieces exist** - the real names are Forest, Ancient wood, Copse, Ruined building, Cathedral shell,
Collapsed wall, Rubble. Caught by listing the pool instead of trusting the guess, which is the same
lesson as everything else today.

**Deliberately unmapped**, and commented so nobody thinks they were missed: the guide's third mapping is
*"Rivers - Difficult (if shallow) / Unstable (if deep)"* and the pool has no deep river (Stream and Marsh
are shallow, Water pool is impassible, Sunken mire is already Difficult + Dangerous, which is stricter
than Unstable). "Crater", "Crater rim" and "Wreckage" are not in the guide's mapping at all - reasonable
guesses, which is exactly why they are left alone.

---

## Verification

- Engine **4,551 passed / 0 failed**, app 8,800, build clean, headless smoke exits 0.
- **31 tests** in `QuestTerrainTypesTests`: the system gate, the name mapping and its pool guard, Dense's
  four sight-line cases plus Cover survival and Blocking precedence, and Unstable's threshold at 5" / 6"
  / 6.5" / 12" with real Dangerous terrain pinned as unconditional.
- **Sabotage-checked 4/4**: Dense behaving as plain Blocking; Unstable losing its 6" threshold; the
  per-system gate opening for everyone; a mapped name renamed out of existence.

---

## Outcome

Done. Dense and Unstable play in AoF: Quest and nowhere else, forests screen sight lines without
blinding the units inside them, and running through rubble is a risk that walking through it is not.

**Left open:** Steep and Climbable, both waiting on elevation.
