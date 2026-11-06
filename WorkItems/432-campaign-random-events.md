# 432 - Campaign random events happen on the table

**Goal.** The campaign books roll a random event at the start of every round. The 36-entry table has
been data in `CampaignEvents` since the campaign engine landed (#410) and **nothing ever rolled it**.
Make it happen in a played game.

**Status:** done, 2026-11-01. Engine-side only; no app change.

---

## 2026-11-06 - the last three (12, 32, 43): all 36 resolve

Hits: `CampaignEventRunner.DealHits` - Defense per hit, granted Defense modifiers folded in (area bonus
refreshed first), no AP, failures through `CasualtyPresentation.ApplyUnitWounds`. Thick Clouds: a
`TokenType.ThickClouds` token on every unit for the round carrying the quarter as a new
`TokenPayload.TableArea` (registered in `SaveTypeRegistry`), read by `RangeRuleQueries.InThickClouds` in
`EffectiveRange` (halves the final range). Saved with the game, unlike the four area events' store.

## 2026-11-06 - the four position events (36, 44, 45, 46)

`StateMachine/CampaignEventAreas.cs`. The runner picks the area (a 3" circle round a random marker, or a
random quarter) and records it for the round in a per-store table (host-side, cleared with the round's
temporary terrain; not saved - a mid-round save/reload loses it). The bonus is checked where it applies:
`DetermineSaveRollsNeededStage` and `MoraleUtilities` call `RefreshRollBonus` just before reading granted
modifiers (old area bonus off, one on per area the unit is inside now); `ActivationStartStage` calls
`OnActivationStart` for Psychic Fog ("activates whilst"), which grants the existing Hastened rule for the
round. "Within 3"" = a living model's centre within 3" (the other objective events' measure); "fully
within" = every living model's whole base inside the quarter. Not stored as terrain on purpose: a
typeless terrain piece would count for "within 1" of terrain" rules (Grounded). 33 of 36 now resolve;
12, 32, 43 still deferred. Tests in `CampaignEventRunnerTests` (6 new).

## 2026-11-01 - built

The book: *at the beginning of each round, every player rolls one die, and on a 5+ a random event
happens*, read off a 36-entry table as two digits (two more dice). Three new engine files:

- `Campaign/CampaignEventTargeting.cs` - **picking what an event happens to.** Split from the applying
  because this is where the mistakes live and the other half needs a running game to test. "One random
  unit" has to mean a living unit of a player who is actually in the game; "one random non-Shaken unit"
  has to come up **empty** rather than pick a Shaken one when every unit is Shaken; "one random captured
  objective marker" must not pick a loose one. Each is a filter, every filter can be empty, and an empty
  filter means the event does nothing - which is the book's own answer, not an error.
- `StateMachine/CampaignEventRunner.cs` - the roll and the 36 branches.
- `Rules/Dispatch/CampaignEventRuleCatalog.cs` - two movement rules the table needs and no book has
  ("Campaign event: Hastened" / "Slowed"), registered through `CoreRuleCatalog.All`.

Wired in at `StartOfRoundExtraActionStage` (after reserves) and `ReconcileObjectivesStage` (the
end-of-round terrain clear). Guarded on **a unit with a campaign sheet**, the same test the Hexer
counter uses - an ordinary game pays one scan of the table per round and nothing else.

### 29 of the 36 resolve; the other 7 say so

Seven are not applied yet, and **each returns a log line admitting it** rather than firing silently. An
event that announces itself and then quietly does nothing is worse than one that admits it. Pinned by a
test per code, so implementing one fails that test - which is the reminder to come back and move it,
rather than an inert branch nobody revisits.

| Code | Event | Why not |
|---|---|---|
| 12, 32 | Explosive Trap, Solar Flare | Deal **hits**, which need the save-and-wound child pipeline. That pipeline only exists inside an attack stage (`BeforeAttackActionStage` seeds it); a round-start stage has no way to drive it. |
| 43 | Thick Clouds | **Halves** weapon ranges. `Effect.RangeModifier` adds and subtracts inches; there is no multiplier, and "-12 inches" instead would be a different rule. |
| 36, 44, 45, 46 | Psychic Pool, Psychic Fog, Shifting Illusions, Support Robots | Conditional on **position** - "whilst any unit is within 3\" of the marker" / "fully within the quarter". A grant handed out at round start is wrong both ways: a unit that walks in later would not get it, and one that leaves would keep it. Needs re-evaluating as each unit activates, which is its own slice. |

### Three decisions worth keeping

**1. The area events are real terrain, not a per-unit grant.** "The area counts as Dangerous terrain"
becomes a `TerrainData` - a 3" circle around a marker, or a whole table quarter - typed Dangerous or
Difficult. That is what the book describes, and the engine's movement already asks the terrain where it
is, so a unit that walks in later is affected and one that leaves is not, with nothing re-evaluating
anything. It is also why `ClearTemporaryTerrain` exists: the pieces are real terrain and **nothing else
would ever take them away**, so an event that said "until the end of the round" would otherwise leave a
minefield on the table for the rest of the game. Matched by name prefix (`"Campaign event: "`), and
there is a test that the map's own terrain survives the sweep - clearing a real wood would be
catastrophic and invisible until somebody played a campaign mission on a map that had one.

**2. The four hit-roll events grant a gated RULE, not a hit-modifier token.** This was a real defect in
the first version, caught by reading it back. A hit-roll modifier token is a single `ERollKind.Hit`
value with **nothing in it that says melee or shooting** - so "-1 to hit rolls in melee" granted that
way would also penalise the unit's shooting, which is a different and strictly worse rule than the one
the table printed. The catalog already has the four gated definitions (`HitBonusInMelee`,
`HitPenaltyInMelee`, `HitBonusWhenShooting`, `HitPenaltyWhenShooting`), each a hook with an `IsMelee`
condition the hit stage evaluates **per attack**; all four of the table's hit events are exactly +/-1,
which is exactly what those rules are, so nothing is lost. Defense and Morale stay tokens, because
those stages do read granted modifier tokens and carry no melee/shooting axis. An amount the catalog
cannot express returns false and the caller says so, rather than quietly applying the wrong rule.

**3. Treasury and VP rewards ride a unit's sheet out of the game.** The army's treasury is not on the
table, so events 61-66 write `StrikeUnit.EventPoints` / `EventVp` / `EventXp` and the after-game
write-back folds them in - the same route #430's spell count and first-blood stamp take. `AfterGame`
clears all three with the kills, so a recovery save cannot carry a reward into the next mission twice.

### The dice-modelling bug the table had all along

`CampaignEvent` had `IsD3` plus `Amount`, which **cannot distinguish "2D3" (two dice summed, 2-6) from
"2 x D3" (one die, 2/4/6)** - and the doc comment claimed multiplier while the only reading the code
supported was the other one. Four of the eight rolled entries were wrong either way. Split into
`D3Dice` (how many dice to sum) and `Amount` (what to multiply the sum by), so 11/31 are 1D3, 12/32 are
2D3, 64 is D3 x 25pts and 65/66 are D3. Two tests in `CampaignPreGameTests`.

### Coverage

`Tests/CampaignEventRunnerTests.cs`, 48 cases. Includes: the 5+ trigger itself, each branch's
no-legal-target line, the terrain sweep leaving real terrain alone, the four hit events granting the
right gated rule (and **no** generic Hit token), every round effect being gone after the round-end
sweep, and an arithmetic check that 29 of 36 are applied, counted from the table rather than written
down. Granted rule names are resolved against the live catalog resolver inside the assertion helper, so
a grant naming a rule nobody registered fails rather than being dropped silently at the hook.

**Sabotage-checked, 7 of 7 caught:** the melee rule swapped for the shooting one; the terrain sweep's
filter removed; BecomeShaken's non-Shaken filter removed; `RollAmount` stopped rolling;
`IsCampaignGame` always true; the opponent pick allowed to choose the holder; `ShouldFire` on 4+.

`Tests/CampaignEventInAPlayedGameTests.cs`, 2 cases, is the **wiring** - and it is the test that
matters most, because **every one of the 48 above would still pass if the call in
`StartOfRoundExtraActionStage` were deleted.** That is the lesson already written down twice in this
suite (#424, then #429/#430): a test that calls the new code itself proves the code and nothing about
whether the game reaches it. So it plays whole AI-vs-AI games over the real server and bus, with the
ordinary test armies given a `StrikeUnit` on each `UnitFileEntry` - which is all a campaign army is,
since the sheet rides the unit from the file into play - and reads the event lines out of the log a
front end would receive. Reading the log needs a decorator, because `ComputerPlayerController`
deliberately throws log messages away; the game itself is untouched and genuinely AI-driven. The
negative half is the other case: the same games without sheets must fire **nothing**, because a random
event in an ordinary game would be visible to every player in it, and the guard preventing it is one
line nothing else tested end to end. Sabotage-checked both ways: deleting the round-start call fails
the campaign case, and `IsCampaignGame` returning true fails the ordinary-game case.

### A false red worth recording: the networked playthrough timeout

The first full-suite run after this slice came back **4,424 passed, 1 failed** -
`HostAndTwoClients_PlayAWholeGame_ToAResult` (#425) blowing its 180s in-test game timeout. It passed in
isolation, twice, and the measurement says why: that fixture is 2m12s of wall clock against **8s of
CPU**, because every decision for a remote seat is a real socket round trip. On a quiet box each test is
about 66s, so the detector sat barely 3x over the real runtime and the parallel load of the full suite
pushed the three-player case past it. Nothing to do with #432, which returns before touching the dice in
a non-campaign game and so cannot shift the dice stream a networked game replays.

Raised to 240s, with `CancelAfter` moved 300s -> 420s so a slow-but-healthy join phase cannot trip
NUnit's own cancellation instead of the assertion's clearer message, and the measurement written into
the field's doc comment. **This is the same fixture that twice looked hung in an earlier session and had
two healthy runs killed** - quiet log plus quiet CPU is what a correct networked test looks like here,
and that is now recorded where the next person will read it.

Two of my own test bugs found on the first run, both arithmetic: `Index` short-circuits at one
candidate **without spending a die**, so a dice script that budgeted a pick roll fed the wrong face to
the D3 (reward of 25 instead of 75); and the deferred-seven assertion was case-sensitive against a
capital "Not applied yet".

## Outcome

Rolled, applied and logged every round of a campaign mission, in all four campaign systems. Nothing
fires in an ordinary game. The seven deferred entries are recorded here and in `claude/fdg-todo.md`,
not dropped.
