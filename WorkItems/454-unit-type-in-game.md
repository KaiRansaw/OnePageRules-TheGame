# 454 — Renamed campaign units show their type in game

**Status:** done (engine `f876c71`, app `51a4b1e`, build 0.86, patch B70). Closes the gap #453 left.

Kai, 2026-11-05: *"the renamed units ... show the name but not the unit type. I need to know which unit
type the unit is."*

- `UnitData.CampaignTypeIfRenamed` - the book type from the campaign sheet the unit already carries in play
  (`StrikeUnitJson`, the route kills have always used), null when the name is the type or the unit is not a
  campaign unit. Cached against the sheet's JSON string reference.
- Shown: under the name on the pinned unit card and the quick hover (`TableTooltipOverlay`), under the name
  in both army-list views (`ArmyListOverlay`), and in the label over the models: "Vargas Squad (Assault Grunts)".
- Not changed: the battle log and target pickers still show only the name. The card is one hover away from
  any of them. Recorded in case Kai wants it there too.

Tests: `CampaignUnitTypeInPlayTests` (6), through the real Start + Rename. Engine 4768 -> 4774. Drawing not
covered (no test draws ImGui).
