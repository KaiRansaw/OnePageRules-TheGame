# 461 — Advanced deployment styles (18)

**Status:** GF: Firefight + AoF: Skirmish done 2026-11-05 (build 0.98, patch B82). GF and AoF waiting on
Kai's screenshots of their own pages.

Source: GFF / AoFS Advanced Rules 3.6.0 pp19-21 (Kai: the two books print the same diagrams). The PDF text
names the styles only; the shapes came from Kai's screenshots. Square diagram, 6x6 grid, 4'x4' table
(core rules) -> 8" a square; every edge lands on a 6" step; Front Line comes out at the core 12" (the
scale check).

## Done
- `PolygonZone` (convex parts, a RectangularZone like MultiRectZone) for the triangles and wedges.
- `AdvancedDeployment`: the 18 shapes (A = top of the diagram), the three D6 family rolls, names.
- `GameSettings.DeploymentStyle` (Core default); lobby "Deployment" combo in Table (host, GFF/AoFS only),
  saved as a host default.
- `ChooseMapSideStage`: a custom map's zones win; then the style for 2-sided games; random rolls one die
  in lockstep and logs "Deployment style: rolled N - X".
- Footprint-exact placement for odd zones (`PlacementUtilities.IsBaseWithinZone`), Scout's forward zone
  for polygons (swept hull), zone drawing for multi-rect and polygon zones (it drew MultiRectZone as its
  bounding box before - a latent bug for L-shaped map zones), zone pick hit-test on the real shape.

## Not done / notes
- GF and AoF: their pages are their own (6'x4'); `Available` says no until they arrive.
- Games with 3+ sides keep the core layout (the styles are two-sided).
- The lobby map preview doesn't draw the style's zones yet.
- A PolygonZone isn't kept by the save file - harmless: zones exist only during deployment, every machine
  builds its own, and saving starts at round 1.

## Tests
`AdvancedDeploymentTests` (no overlap, balanced families equal area, Front Line = 12", spot checks against
the diagrams, slanted-edge footprint, scaling, rolls, availability); `FastPlayInAPlayedGameTests.
EveryDeploymentStyle_DeploysAndPlaysToTheEnd` (all 18, AI deploys and plays out) and the random roll.
