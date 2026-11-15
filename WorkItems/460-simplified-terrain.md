# 460 — Fast-Play: Simplified Terrain (every piece a Solid Building)

**Status**: done 2026-11-15 (slice 1 Build 2.31, slice 2 Build 2.32).
**Source**: GF / AoF Advanced Rulebooks 3.6.0 p17 (Simplified Terrain) and p33 (Solid Buildings). Firefight and
Skirmish Advanced books don't print Fast-Play.

## Goal
With Simplified Terrain on, every terrain piece is a solid building: units enter and leave, capacity X, inside =
Cover, shoot in and out, charging a unit inside targets one unit (defender picks).

## Notes (newest first)
- 2026-11-15 slice 2 (Kai: everyone strikes; bots enter for objectives):
  - `SolidBuildings.Touches / EngagedThrough / InMeleeRange / ForObjective`. ChooseActionStage + ChooseMeleeDefenderStage
    use InMeleeRange; DetermineInRangeAttackers/Defenders take every living model in a building fight; PileInStage skips.
  - `ChooseMeleeDefenderStage.DefenderPicksInsideBuilding`: the defending player picks among its units inside.
  - `MovementUtilities.ValidateChargeReach` accepts a model within 2" of a building the enemy holds.
  - `Ai/Solo/SoloCoOpBuildingEntry` - grid layout inside the footprint, validated with the stage's own rules; used by
    SoloCoOpMovementResolver for Rush/Advance to objective.
  - Tests: 3 more in `SolidBuildingsTests`.
- 2026-11-15 slice 1 (Kai: "2 then 1"; capacity by size, every side firing ports, no destruction/collateral):
  - `ETerrainType.SolidBuilding`; `FDG.SolidBuildings` (Simplified stamp, Capacity 6/11/21 by footprint area,
    Spaces = Transport rule, MoveProblem, MayCross). Stamped in `PlaceTerrainStage.WithQuestTypes`.
  - Pieces are Impassible|Dense|Cover; `MovementUtilities.ValidatePaths(buildingEntry: true)` lets a model cross the
    building it starts or ends in - only DefinePathStage and Godot's MovePlanner pass it, so bots never enter.
  - `DefinePathStage.BuildingProblem` (+ MovePlanner.Problem): whole unit or none, capacity, enemy-held, too-big
    models, leave within 6", no hopping. CoverMajority counts buildings.
  - Tests: `Tests/SolidBuildingsTests.cs` (rules + GF/AoF played games with SoloCoOp bots).

## Outcome
Done. Left: a friendlier "SolidBuilding" name in Godot terrain tooltips (cosmetic); the book's optional Building
Destruction / Collateral Damage (Kai: not now).

## Decisions
- Kai 2026-11-15: capacity by size; every side has firing ports; Building Destruction / Collateral Damage not now;
  everyone strikes in a building fight; bots enter only for objectives.
