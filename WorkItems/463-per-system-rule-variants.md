# 463 — Per-system special-rule definitions

**Status:** done 2026-11-05 (build 1.03, B87). Follow-ups listed under "Not done".

Goal: the rest of the per-system core rules. `CoreRuleCatalog` is Grimdark Future's wording; find every
catalog rule another system defines differently and give that system its own reading.

## Method
Diffed every catalog rule (101) against `BuiltInAssets/OfficialRuleText.json` (Army Forge, harvested
2026-10-31) across all six systems. 16 rules differ. No bundled book embeds its own definition for any of
them (checked all 288 books), so every army was getting GF's catalog version.

## Already handled before this item (verified, unchanged)
Blast, Fear, Scout, Caster (Quest), Hero aura, Transport caps - all in `CoreRulesProfile`.

## Built
- `Rules/Dispatch/SystemRuleVariants.cs` - definitions that replace the catalog's by name for one system;
  `CoreRuleCatalog.CreateResolver(system)`; `GameBootstrap.BuildRuleResolver(slots, system)` from
  FDGServer (new game + resume) and the scenario compiler.
  - **Slayer**: Tough(3) GF/AoF, **Tough(9)** GFF/AoFS, **Tough(27)** both Quest.
  - **Indirect** (both Quest): no Hold requirement, **-1 to hit after moving**.
  - **Furious Buff**: 1 unit GF/AoF; **up to 3** AoFS and AoFQ ("nearest" not enforced - house rule);
    **once per game, up to 3** GFSQ.
  - **Fanatic**: AoF = AP(+1) in melee when charging for the Hero **and its unit** (aura); AoFS = the Hero
    alone. GF/GFF/GFSQ keep the after-deployment reposition.
- **Counter-Attack** (AoFS/AoFQ only): "unless this unit is Fatigued" was missing.
- `CoreRulesProfile.FlyingKeepsTerrainItStartsIn` (GFF/AoFS/Quest): Flying still takes the Dangerous test
  for terrain it starts its move / activation in.
- `CoreRulesProfile.HordeEliteIsPerRound` (GFF/AoFS): Elite once per round, not once per game.
- Tests: `Tests/SystemRuleVariantsTests.cs` (16, each with a book guard against the Army Forge text).

## Not done (recorded, not dropped)
- **Flying + Difficult terrain it starts in** (GFF/AoFS/Quest): the 6" cap should apply. Not built - the
  AI planners read the waiver directly, and a cap the planner doesn't know would hand the engine moves it
  rejects. Needs the planner change alongside.
- **Jumping** (Flying auto-passes, Strider halves dice): the engine has no jumping/falling (parked with
  elevation), so there is nothing to attach to.
- **AoF: Quest Fanatic**: Army Forge has no text for it; its 3 books keep GF's reposition. Needs a source.
- **Quest Ambush** (no alternation, no can't-seize clause): Quest never uses the standard Ambush
  arrival path (enemy pools arrive through the waves' AI rule), so there is nothing to change today.
- **Tough** wording (assignment clause absent in skirmish/Quest): not a behaviour difference the audit
  could substantiate; unchanged.
- **Hero joining units** in GFF/AoFS/Quest: open question with Kai (fdg-todo).
- **Elite** still only re-rolls a morale test, not "one roll of any kind" (existing scope, all systems).

### 2026-11-14 (build 2.00)
- Flying + Difficult terrain it starts in: `MovementRuleQueries.IgnoresDifficultTerrain(ctx, unit)` at the six engine sites; `FlyingStartsInDifficultTests`. Tactician goal-scoring keeps the plain check (no game system there); moves are planned from the request flag.
