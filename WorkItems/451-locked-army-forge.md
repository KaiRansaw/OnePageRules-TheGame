# 451 — Locked Army Forge for campaign armies

**Status:** done (engine `1514c42`, app `476e957` - shipped in the same app commit as #452; see below), build 0.84.
**Spec:** `claude/campaign-flow-spec.md` section 3b.

## Built

- `ArmyForgeScreen.CampaignBuild` (system, title, numbers, OnContinue, OnCancel) and
  `BeginCampaignBuild`: one campaign game's books only, an empty list, the treasury as the points limit.
- Removed in campaign mode, per the spec: the points-limit field, the game-system dropdown, the
  "Army Forge" title bar. Also not offered: Save, Load, Import Link - a campaign army is handed back, not
  saved, and an imported list could be another game's (recorded here so it is not a silent cut).
- Header: `Your Army  -  Grimdark Future  -  640 / 750 pts spent  -  minimum 600  -  Heroes 1/1`.
- Continue, bottom right, gated by `CampaignRules.CannotStart` - the same rule as the start-from-a-folder-
  army path, so the two cannot disagree. Leaving either way resets the Forge to an ordinary empty list.
- `GameSystems.CampaignBookSlug` (engine): null for both Quest games on purpose (two datasets each).

## Tests

`FdgRaylib.Tests/ArmyForgeCampaignBuildTests.cs` (14) over the REAL bundled library for all four games:
only that game's books, treasury as budget, Quest refused, empty/over-treasury blocked, Continue hands
over book + selections and the campaign accepts it, reset after Continue and after Back, header text.

## Note on the commit

The repo's pre-commit hook (correctly) refused the first app commit because #452's new test file was
still untracked; the following commit then carried both pieces. Content is right and pushed; the #451
description lives here rather than in that commit message. History on the shared branch not rewritten.
