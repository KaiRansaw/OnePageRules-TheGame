# 445 — Campaign end condition: a VP target that can be won

**Status:** done (engine `c047036`, app `9d543cf`, build 0.78, patch B62).
**Spec:** `claude/campaign-flow-spec.md` section 3a, and section 6 on the two kinds of VP.

## Goal

Let a campaign be **won**. Before this, it could not be — `ECampaignType.Endless` was the only type
wired up and *both* creation call sites in `StrikeTeamScreen` passed it as a literal, so every campaign
ever created was Endless. VP accumulated and nothing read them.

Kai picked campaign screens as the next work on 2026-11-03, and this is the first slice of
campaign-flow-spec sections 1–4. It was chosen first because it is the campaign's win condition, and
because section 2's right-hand panel cannot show "campaign type and progress" until a campaign has one.

## What was already there (so this slice was small)

- `ECampaignType` with all four book types, `PointLimited` among them.
- `CampaignPreGame.IsOver(type, missionsPlayed, highestVp, missionLimit, vpLimit)` — already correct for
  `PointLimited`, and already tested in `CampaignPreGameTests`.
- `CampaignPreGame.RollCampaignLength(d6)` — the book's D3+4, already correct and clamped.
- `StrikeTeamInfo.Vp`, `.Matches`, `.EnemyArmyFile` (#428's standing enemy).

Missing was only: somewhere to store the target, a caller that passed it, something that read the
result, and a UI that asked.

## Decisions

**The target is stored on BOTH armies' blocks.** It is a property of the *campaign*, not of either army,
so two files that disagreed would make whichever one a reader opened the wrong answer. This is
deliberately unlike `Difficulty` and `EnemyArmyFile`, which are genuinely the player's own and stay
player-only. `Standing()` reads the player's rather than reconciling the pair — a `Max` of the two would
paper over a bug that wrote them out of step instead of letting a test catch it.

**A zero target is not a target.** Every campaign file saved before this field deserializes with
`VpTarget = 0`, and zero is a target that any VP total clears. The obvious implementation therefore ends
every existing campaign the moment its screen is opened. `HasTarget` gates on `type == PointLimited &&
target > 0`, and both suites have a named regression test for it.

**Both armies crossing at once on equal VP is a draw.** The books give no tiebreak, so inventing one
would be a house rule for a case that needs none.

**The mission- and week-limited types stay unoffered**, per spec section 3a. `Standing()` passes a
mission limit of 0 so a campaign saved as one of those keeps opening rather than ending on a limit
nothing ever set.

**Free choice of target, with the roll offered as a button** — the book rolls D3+4 (5–7). Already
recorded as house rule 2 in the spec's section 7; now in `claude/house-rules.md`.

## The call-site problem, and what was done about it

Both new test files call `CampaignRules.Start` themselves, so **both would pass in full while the screen
went on hard-coding `Endless`** — which is exactly the bug being fixed. This is the sixth-plus instance
of the project's recurring lesson: *a test that calls the new code cannot tell whether the game reaches
it.*

Creating a campaign runs through private screen state and reads the real armies folder, so there is no
seam to drive it from a test. `FdgRaylib.Tests/CampaignCreationCallSiteTests.cs` reads
`StrikeTeamScreen.cs` as source instead and fails if either `CampaignRules.Start` call loses its type or
its `vpTarget` — same approach as `DisabledScopeBalanceTests`, and for the same reason (nothing in the
suite draws an ImGui frame). It strips comments and string literals first, so prose mentioning
`ECampaignType.Endless` cannot trip it.

**Verified by sabotage:** one call site was reverted to the old hard-coded `Endless`, 2 of the 3 guards
failed, the file was restored and the restore confirmed with `grep -q SABOTAGE` before re-running.

## Tests

- `FutureOfDarkGrimness/Tests/CampaignStandingTests.cs` (14) — the rule: in progress, player wins,
  rival wins, overshoot, both crossing level and unlevel, Endless, no rival, old file with no target,
  the unoffered types, `Start` storing it on both armies, a negative target, and the D3+4 range.
- `FdgRaylib.Tests/CampaignEndConditionTests.cs` (4) — the target surviving the real campaign file, an
  Endless campaign round-tripping as Endless, both armies agreeing on disk, and a file with the field
  stripped back out still opening.
- `FdgRaylib.Tests/CampaignCreationCallSiteTests.cs` (3) — the call-site guards above.

Engine 4716 → 4730, app 8847 → 8854. Headless smoke exits 0.

## Deliberately not done

- **Nothing writes the result anywhere permanent.** A won campaign is derived from the two VP totals
  every time it is drawn, not stamped on the file. That is fine while the only consequence is a banner
  and a disabled button; a campaign that awarded something for winning would need a recorded result.
- **The campaign type cannot be changed after creation.** `Start` keeps the stored number even for an
  Endless campaign specifically so a future screen could offer that, but no screen does.
- **Section 2's full right-hand panel** (treasury for both armies including unspent balance, win/loss
  record, units sitting out) is not built. The VP half of it is. The rest is the next slice.
- **Sections 3b/3c/3d** — the locked Army Forge, the name-it-then-build-the-rival flow, and unit names —
  untouched. Creation still starts from an existing army in the system's folder.
