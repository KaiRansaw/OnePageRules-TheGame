# 456 — Fast-Play: switches and unit leaders

**Status:** done (engine `ca45061`, app `fbd7739`, build 0.88, patch B72). With #457 in the same commits.
**Source:** GF and AoF Advanced Rulebooks 3.6.0, p17. Identical wording except the recommendation:
4000pts (GF) / 3000pts (AoF). Firefight and Skirmish Advanced books do not print Fast-Play.

## Kai's calls
- 2026-11-03: Fast-Play as the book has it, instead of a bigger table, for big games.
- 2026-11-05: "Game picks, I can change it" for leaders; build movement, melee and shooting first, with
  Simplified Terrain as its own later job (it is really the Solid Buildings rules - garrisoned terrain).

## Built
- `GameSettings.FastPlayMovement/Melee/Shooting` (off by default; a pre-#456 save plays as it was).
  `FastPlay` gates them to GF and AoF.
- `UnitData.LeaderModelId` (serialized); `IUnit.LeaderModelId` default member returning null.
- `PickUnitLeadersStage` after the Command Group picks: `FastPlay.AutoPick` for every unit, then one
  `PickUnitLeadersRequest` per player. GUI (drop-down per unit + Confirm), CLI (unit=model, blank/EOF keeps
  the suggestions) and AI (keeps the suggestions) resolvers, all registered.
- Wound order in three tiers - rank and file, leader, joined hero - in `AssignWoundsResults` (generalising
  #006's hero gate) and `WoundAllocation.Order` (so the Tactician prices the same order).
- Lobby: Advanced Rules section live; view models carry the flags (`SetFastPlay`, host only); UserConfig
  remembers them. Unit card shows the leader.

## Judgement calls (claude/house-rules.md section C)
The auto-pick heuristic; leader before a joined hero in the wound order.

## Tests
`FastPlayLeaderTests` (13), `FastPlayInAPlayedGameTests` (4, real AI-vs-AI games through the server; unbinding
the stage fails 2). Existing hero / Tough / wound-packet suites unchanged and green.
