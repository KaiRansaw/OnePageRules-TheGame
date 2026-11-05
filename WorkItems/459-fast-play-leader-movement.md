# 459 — Fast-Play: Leader-Based Movement

**Status:** done 2026-11-05 (build 0.97, patch B81). Also closes #458's leader-only charge.

Source: GF / AoF Advanced Rulebook 3.6.0, p17: "When a unit wants to move, only measure distances from the
leader, and move it first. Once the leader has been moved, you must place all other models in the unit
around the leader in any way you like, as long as you maintain unit coherency. No distances are measured
for any other models except the leader, so you may freely move them to their new position."

## Done
- `FastPlay.FreeMoveLeader` (Movement or Melee on, leader alive) -> `DefineMovementPathRequest.MeasuredModelId`;
  `LeaderOnlyMovement` = Movement on.
- `PlayerBudgetFor`: unmeasured models have no distance limit. `BudgetFor` (the AI's) is unchanged on
  purpose - the AI clamps every model to it, so unlimited budgets would fling models across the table.
- Validation (`DefinePathStage`): player budgets; difficult-terrain cap counts only the leader under
  Movement (`measured` filter in `MovementUtilities.ValidatePaths`).
- Advance/Rush is decided by the leader's distance (`SubmitValidPathTemplate`).
- Leader-only charge (#458 part 2): `MovementUtilities.FastPlayChargeProblem`, shared by the stage and the
  GUI Done gate - a move that USED the freedom may not start a melee the leader did not reach; under
  Melee alone the freedom exists only for a charge the leader reaches. A move within ordinary budgets is
  never affected (keeps every AI move legal).
- GUI: player budgets everywhere, leader-only difficult clamp, panel line naming the measured leader.
- Lobby toggle live.

## Not done
- **Separating by 1" after melee, leader first** (#458's last clause): the disengage move is still the
  ordinary 1" for every model. That is a subset of what the book allows (every ordinary separation is a
  legal Fast-Play one), so nothing illegal happens - it only lacks the freedom. Needs the consolidation
  GUI to take per-model caps.
- The AI never takes the freedom (moves like normal). Legal, just not Fast.

## Tests
`FastPlayMovementTests` (budgets, charge rule both modes, ordinary move unaffected),
`FastPlayInAPlayedGameTests.WithEveryLeaderRuleOn_TheGameStillFinishes` (GF + AoF).
