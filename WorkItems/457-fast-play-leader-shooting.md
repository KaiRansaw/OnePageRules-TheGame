# 457 — Fast-Play: Leader-Based Shooting

**Status:** done (same commits as #456, build 0.88, patch B72).

"only measure line of sight and distances from the leader model, to the closest model from the target
unit. If the leader has line of sight, then all weapons that are in range of the target from the leader's
position may shoot ... models can freely shoot as if they were in the leader's position."

- `FastPlay.ShootingOrigin(unit, settings)`: the leader when shooting is on and it lives; else null.
- `ChooseRangedAttackStage.BuildAttacksForEnemyUnit` tests every model's weapon from the leader's position
  and sight (shared cache). `HasAnyFireableTarget`, the occluded-copy trim, and the AI all read that output.
- `ShotEligibility.UnitSeesUnit` (Occlusion gate) and `CoverMajority.Evaluate` (Cover stage + targeting
  preview) take an optional measure-from model.

## Judgement calls (house-rules.md C)
Cover judged from the leader; a leader killed out of turn means the unit shoots the ordinary way.

## Not covered
The attack animation's shot lines (`AttackBeatPositions`) and the GUI's targeting/movement previews still
draw per-model lines - presentation only, the rules decision is the stage's. The Tactician's own private
sight estimates (MacroActionGenerator etc.) still assume per-model sight; they only rank options, legality
is the stage's.

## Tests
Six cases in `ChooseRangedAttackStageTests` (leader sees -> all fire; leader blocked -> none; range from the
leader; off; Firefight ignores the switch; dead leader -> ordinary) and one in `CoverMajorityTests`.
