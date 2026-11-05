# 446 — Campaign detail panel, and a win/loss record to put in it

> **2026-11-05: the history backfill described below was broken** - it never matched a real history line, and it forgot recovered wins after the next mission. Its test passed only because the test wrote the history itself. Fixed in **#449**; read that file before trusting anything below about the backfill.

**Status:** done (engine `f6d4a1d`, app `4446d45`, build 0.79, patch B63).
**Spec:** `claude/campaign-flow-spec.md` section 2. Follows #445, which built section 3a.

## Goal

Section 2's right-hand panel: the selected campaign shown beside the list, with both army names, both
VP, **both treasuries including the rival's unspent balance**, the next mission number, the win/loss
record, and who is sitting out injured.

Before this the campaign list was a full-width list and all of that was only reachable by opening the
campaign into its three-column camp screen.

## The part that was not just drawing

**Wins were not recorded anywhere.** `StrikeTeamInfo.Matches` counted missions played and nothing counted
how many were won, so the record simply did not exist as data. That made this slice engine work rather
than a layout pass.

`StrikeTeamInfo.Wins` is incremented in `AfterGame`'s win branch; losses are `Matches` minus it.

### The migration problem, and the shared constant

A new counter reads back as **0 on every campaign already on disk**, including one that has won five
missions. Three options were on the table: show an incomplete record, backfill from history, or accept
being wrong. Backfilling is honest and cheap because `AfterGame` already writes a line per mission.

`CampaignRules.RecordOf` therefore returns the stored count, and when it is 0 while `Matches > 0` counts
the winning history lines instead. Safe to run in that case even for an army that genuinely won nothing —
there are then no lines to find and the answer is 0 either way.

**The real risk is not the backfill, it is drift.** If the line `AfterGame` writes and the string
`RecordOf` looks for stop agreeing, every pre-#446 campaign silently shows 0W and nothing fails. So:

- the prefix is a shared `internal const string WonPrefix`, used by both, and
- `CampaignRecordTests.AWinWrittenByAfterGameIsFoundByTheBackfill` drives a **real** `AfterGame`, blanks
  the counter to imitate an older file, and asserts the backfill recovers the right number.

A constant alone would not have been enough — it stops the two *literals* drifting, but not somebody
rewriting the log line inline. **Verified by sabotage:** the log line was reworded as a literal
(`"Victory on ..."`), that one test failed with the message naming the cause, and the file was restored
and confirmed with `grep -q SABOTAGE` before re-running.

## The rival's record is real, not a placeholder

Checked rather than assumed: `StrikeTeamFiles`' write-back groups the table's units by their sheet's
`TeamFile` and calls `AfterGame` for **every** campaign army that played, bot included, with a `won`
computed per group from `result.WinnerPlayers`. So the bot's `Wins` increments the same way the player's
does, which is what makes a two-sided scoreboard worth drawing.

## Decisions

- **Both names as the heading** — "Iron Reavers vs The Hollow Choir". The spec asks for it and the reason
  holds: a campaign is a rivalry, and "Campaign 3" tells you nothing.
- **The rival's unspent treasury is on screen.** The spec is emphatic and the reason is worth repeating:
  the bot earns treasury by the book whether or not it spends it, so off-screen it banks quietly and then
  one mission the difficulty jumps for no reason the player can see.
- **Nonsense counts clamp rather than draw.** A campaign file is hand-editable and this is on screen, so
  more wins than missions clamps to the missions played and a negative mission count cannot become
  negative losses.
- **The camp screen's CAMPAIGN block from #445 stays.** The two overlap on VP deliberately: the list
  panel is for choosing between campaigns, the camp screen for playing one, and the score belongs in
  both. Not worth factoring into a shared widget while the drawing code is rental.

## Tests

`FutureOfDarkGrimness/Tests/CampaignRecordTests.cs` (8) — a new campaign has no record; a win counted; a
loss not counted as a win; a mixed five-mission run; the backfill-vs-writer guard above; a genuinely
winless army; hand-edited nonsense clamped; the rival keeping its own record.

Engine 4730 → 4738, app 8854 (unchanged — the panel is drawing code and nothing in the suite draws an
ImGui frame). Headless smoke exits 0.

## Deliberately not done

- **No test covers the panel itself**, because nothing in the suite draws ImGui. The data it reads is
  tested; the drawing is not. Recorded rather than papered over. (The `DisabledScopeBalanceTests` scope
  check does cover the new `BeginChild`/`EndChild` pair.)
- **No per-campaign "last played" date.** Not asked for, and nothing records one.
- **Sections 3b/3c/3d and 4** still untouched — the locked Army Forge, name-then-build-the-rival, unit
  names, and making the between-missions screen say whose books you are doing.
