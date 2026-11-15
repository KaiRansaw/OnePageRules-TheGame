# 460 — Fast-Play: Simplified Terrain (every piece a Solid Building)

**Status**: slice 1 done 2026-11-15 (Build 2.31); slice 2 open.
**Source**: GF / AoF Advanced Rulebooks 3.6.0 p17 (Simplified Terrain) and p33 (Solid Buildings). Firefight and
Skirmish Advanced books don't print Fast-Play.

## Goal
With Simplified Terrain on, every terrain piece is a solid building: units enter and leave, capacity X, inside =
Cover, shoot in and out, charging a unit inside targets one unit (defender picks).

## Notes (newest first)
- 2026-11-15 slice 1 (Kai: "2 then 1"; capacity by size, every side firing ports, no destruction/collateral):
  - `ETerrainType.SolidBuilding`; `FDG.SolidBuildings` (Simplified stamp, Capacity 6/11/21 by footprint area,
    Spaces = Transport rule, MoveProblem, MayCross). Stamped in `PlaceTerrainStage.WithQuestTypes`.
  - Pieces are Impassible|Dense|Cover; `MovementUtilities.ValidatePaths(buildingEntry: true)` lets a model cross the
    building it starts or ends in - only DefinePathStage and Godot's MovePlanner pass it, so bots never enter.
  - `DefinePathStage.BuildingProblem` (+ MovePlanner.Problem): whole unit or none, capacity, enemy-held, too-big
    models, leave within 6", no hopping. CoverMajority counts buildings.
  - Tests: `Tests/SolidBuildingsTests.cs` (rules + GF/AoF played games with SoloCoOp bots).

## Slice 2 (open)
- Charging units inside: "only one unit inside of it is targeted, and the defender may choose which. To charge
  the building, simply move models in base contact with it." Needs melee range = touching the building.
- Bots entering buildings (planners keep them as walls today).
- Godot: a friendlier type name than "SolidBuilding" in terrain tooltips.

## Decisions
- Kai 2026-11-15: capacity by size; every side has firing ports; Building Destruction / Collateral Damage not now.
