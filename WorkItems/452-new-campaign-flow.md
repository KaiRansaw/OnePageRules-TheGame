# 452 — New campaign: build your army, then the enemy, name both

**Status:** done (app `476e957`, build 0.84, patch B68). **Spec:** campaign-flow-spec 3a-3c.

## Kai's answers, 2026-11-03

Both ways to start (build fresh / an army you have); you build the enemy too, with a "Pick one for me"
button; the campaign's opening journal note asked for but skippable.

## Built

Setup (game, how it ends, losses, optional story, your army: build or pick) → Name your army → enemy:
**Build Enemy Army** or **Pick one for me** → Name enemy army → **Finish**.

- Nothing touches disk before Finish. Finish writes the enemy first, then the player's file carrying the
  link and the opening note; a failed player save deletes the enemy it would orphan.
- "Pick one for me" draws from the game's folder armies that pass `CannotStart`, never the list the
  player started from; disabled with a reason when there is none.
- The campaign screen resets to its list whenever it is shown, which would have thrown away a half-made
  campaign on return from the Forge. The Forge callbacks set a flag that keeps the wizard. Verified by
  removing it: 2 tests fail.
- The spec draws the naming steps as popups; built as steps in the screen's own panel instead (layout,
  rental code, same content). Enemy armies are now named by the player rather than "X (enemy of Y)".

## Tests

`FdgRaylib.Tests/CampaignCreationFlowTests.cs` (6): the whole flow through the real wizard with armies
built in the real Forge (only the hop between screens simulated, as Program.cs does it) - linked files,
both carry the end condition, opening note, campaign folder; skipping the note; pick-one-for-me never
returns your own list and leaves the original file untouched; name rules; cancelling the Forge returns to
the wizard; an ordinary return still lands on the list. The existing call-site guard
(`CampaignCreationCallSiteTests`) still sees exactly two wired `CampaignRules.Start` calls.
