# 458 — Fast-Play: Leader-Based Melee

**Status:** part 1 done 2026-11-05 (build 0.94, patch B78). Part 2 open (below).

Source: GF and AoF Advanced Rulebook 3.6.0, p17 (identical wording):
"When a unit wants to charge, only measure distances from the leader and move it into base contact first.
Once the leader has been moved, you must place all other models in the unit in base contact with enemy
models, or as close as possible in any way you like, as long as you maintain unit coherency. Then all
models from the target that are not in base contact may move in the same way. When fighting in melee, all
models always count as being in range of the enemy, regardless of their position, so everyone can always
strike with all weapons. When separating units by 1" after melee, first separate the leader, and then place
all other models around the leader in coherency, over 1" away from other units."

## Done (part 1)
- **Everyone strikes:** `DetermineInRangeAttackersStage` / `DetermineInRangeDefendersStage` - once any
  model is in range, the whole living unit is (`MeleeRangeUtilities.AllLiving`). The log line now counts
  living models ("N of M living models in melee range").
- **The rest of the charger is placed for you:** `PileInStage` runs the pile-in placement for the charging
  unit first (toward the target, no distance cap, coherency kept, leader held still via `holdStill`),
  then the target's ordinary pile-in with no 3" cap ("in the same way").
- `PileInUtilities.ComputePileInMoves` gained `maxDistanceInches` and `holdStill`; defaults unchanged.
- `FastPlay.UnmeasuredMoveInches` = 1000 (finite only so the search keeps finite numbers).
- Lobby toggle live.

## Open (part 2)
- **The charge itself is still measured per model.** The player (or AI) moves the unit with normal budgets;
  the rule says only the leader's distance counts. A charge that reaches with a non-leader but not the
  leader still counts as a charge. Needs the leader-only budget shared with #459 (Leader-Based Movement),
  so it is done there.
- **Separating by 1" after melee, leader first** - the disengage move in `ConsolidateStage`. Same
  leader-then-place mechanism as #459; done with it.

## Tests
`PileInTests` (no cap reaches contact; leader held), `FastPlayInAPlayedGameTests.
WithFastPlayMelee_EveryMeleeHasTheWholeUnitInRange` (two seeds, asserts a melee happened; sabotage-checked).
