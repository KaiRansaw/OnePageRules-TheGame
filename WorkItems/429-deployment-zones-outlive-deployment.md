# 429 — The deployment zones have to outlive deployment

**Goal.** Record each team's deployment zone for the whole game, instead of discarding it the moment
deployment ends.

**Status:** done 2026-11-01.

---

## 2026-11-01

### The gap

The zones lived only on the `DeploymentContext`, which is thrown away when deployment finishes. So
**from round one onwards nothing in the game could say where anyone had deployed.**

That is not cosmetic. It has at least three customers:

- the campaign's **Intimidator** special objective — *"have most of your units inside the enemy
  deployment zone at the end of the game"* — which could not be scored at all;
- the **Solo AI's three sections**, which the book draws along the AI's own deployment edge. `WorkItems/424`
  records reading them as "the next band along, wrapping, across the table's longer axis" *precisely
  because the real edge was not recorded*;
- any future mission that scores a zone.

### What changed

- `GameProgressData.DeploymentZones` — `Dictionary<int, ZoneRect>?`, by `ITeam.TeamNumber`.
- `ZoneRect` — a deployment zone as four numbers. **Deliberately not a
  `DataBinding<RectangularZone>`**: the zone objects are created for deployment and nothing guarantees
  their lifetime afterwards, while four floats per team survive a save, a reload and a networked
  catch-up with no reference to keep valid.
- `GameProgressUtilities.SetDeploymentZones` / `DeploymentZones` / `DeploymentZoneOf`.
- `ChooseMapSideStage` writes them as it sets them on the context.

### The ordering that was easy to get wrong

The zones are chosen **during deployment, before the main phase has written any progress record**. So
`SetDeploymentZones` parks them in a `ConditionalWeakTable` and `WriteProgress` folds them in — the same
hand-off `QuestProgress` already uses for Star Quest state written during set-up. Without it the first
snapshot of the game would have erased them in round one, and **every test that set the zones directly
would still have passed.** There is a test for exactly that hand-off, and another for the per-round
snapshots that follow.

### Tests

`DeploymentZoneRecordTests` — 6 cases. The headline one plays a **whole AI-vs-AI game** and asserts the
zones are still there at the end, which is the only way to tell whether the game reaches the recorder at
all; that is the lesson from #424, where a test passed with the feature disconnected. Plus: the save and
reload round trip, the pre-progress-record hand-off, the per-round snapshots, null (not empty) when
nothing was recorded, and a second full game that proves nothing in the kill path throws when
there are no campaign sheets to stamp.

### One flake observed, and what it probably is

A single engine-suite run failed once with one unnamed failure while two test suites and a build were
competing for the machine; two clean runs either side of it (4,374 passed, 2.5 minutes) and a targeted
re-run found nothing. The likeliest candidate is this item's own full-game test: it waits up to 120s for
an AI-vs-AI game that normally finishes in about a second, so under heavy enough load the wait is the
thing that fails, not the game. Recorded rather than dismissed - if it reappears, that timeout is the
first place to look, and raising it is the fix rather than hunting a phantom.

## Outcome

Done. The zones are part of the game's recorded state, and #430's Intimidator can be scored.
