# 449 — Fix: campaign win recovery matched nothing in a real file

**Status:** done (engine `16d92a4`, app `ef5b4e7`, build 0.82, patch B66). Fixes #446.

Found 2026-11-05 while building the journal record (#450), which reads the same `AfterGame` output.

## The two bugs, both in #446's `CampaignRules.RecordOf`

1. **It never matched.** The backfill counted history lines starting `"Won the mission on "`. That is
   the start of a *log* line. `AfterGame` adds ONE history line per mission, `"Match N: won - <log>"`,
   so the log text is never at the start. Every campaign saved before 0.79 recovered **0 wins**.
2. **It forgot.** The backfill only runs while `Wins == 0`. The first mission played under 0.79+ set
   `Wins` to 1, so a campaign with three earlier wins read as **1 win** from then on, permanently.

## Why the tests passed

`CampaignRecordTests.PlayMission` called the real `AfterGame` **and then also** did
`History.AddRange(log)` - adding the raw log lines, which do start "Won the mission on". The real game
never does that. The sabotage check in #446 reworded the log line, which changed what the helper added,
so it failed as expected and gave false confidence. The project's recurring lesson again: *a test that
builds the state itself cannot tell you whether the game produces it.*

## Fix

- `HistoryLine(match, won, log)` writes the history line; `IsWonHistoryLine` reads it (`^Match \d+: won - `),
  side by side. The format is unchanged since the first campaign code (git: `a795d2d`, 2026-10-24), so
  this is safe for every campaign file that exists.
- `AfterGame` folds the recovered count into `Wins` before counting the new mission.
- The test helper no longer touches history. Two new tests use a **hand-written** history in the on-disk
  format, so they do not depend on today's writer at all.

**Verified by putting each bug back:** the old matching fails 3 tests; removing the stamp fails 1.

Full engine run: one red, `NetworkedGamePlaythroughTests.HostAndTwoClients_PlayAWholeGame_ToAResult` -
the intermittent network test recorded in #432, #435, #444. Passed twice run alone.
