# 455 — The player runs the enemy's books between missions

**Status:** done (engine doc `see git log --grep=#455`, app `df9f11e`, build 0.87, patch B71).
**Spec:** campaign-flow-spec section 4.

## The conflict this resolved

The spec (2026-11-01) says *"the bot never spends its own points"* - the player runs both armies'
economies. #428, built the same day, had the after-mission write-back call `CampaignBot.BetweenMissions`,
which resolved the bot's pending choices, recruited and spent its treasury. The two were never reconciled.
Asked Kai on 2026-11-05 with three options (player / bot / bot-by-default with a switch); he chose
**the player, as the spec says**.

## Built

- `StrikeTeamFiles.WriteBack` no longer calls `BetweenMissions` (removed with the now-unused
  `rivalValue`). Casualties, XP, VP and treasury still reach both armies through `AfterGame`.
- `CampaignBot.BetweenMissions` kept, with its tests, documented as unwired. `PickWhatToBring` (Mission
  Setup's force sizing) is unaffected.
- Camp screen: a Your army / Enemy army switch. On the enemy side the roster, unit panel (trait picks,
  upgrades, rename) and Recruit view work on the enemy's file; its columns are tinted, the roster heading
  says ENEMY, and a line says whose books are being done. On the player's side the switch shows the
  enemy's unspent treasury and how many choices wait on the enemy. Mission column and Journal stay the
  player's. Opening a campaign always lands on the player's side. One-new-Hero-per-mission is tracked per
  army.

## House rule

Recorded as B17 in claude/house-rules.md (spec section 7 item 4).

## Tests

`FdgRaylib.Tests/CampaignEnemyBooksTests.cs` (2): a source guard that `WriteBack` does not call
`BetweenMissions` (comments stripped), and the switch pointing the managed army at the enemy's file while
the campaign stays the player's, with the Hero allowance per side. App 8885 -> 8887.
