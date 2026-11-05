# 448 — Campaign journal: a note for the campaign, and one per mission

**Status:** done (engine `15bef53`, app `34cb6ba`, build 0.81, patch B65).

## Goal

Kai, 2026-11-03 (first asked 2026-11-01): *"I want to be able to keep notes on every mission, and an
overarching note for the entire campaign. It'll be to keep notes of the story, like a journal entry or
something... to give campaigns more of a story feel than just 'do this to get this'."* He also said he was
*"unsure of how I want to add that"* - so this builds the data properly and the cheapest screen that
works, for him to react to in play.

Built ahead of the rest of campaign creation (3b-3d) because his 2026-11-03 answers have creation ask for
the campaign's opening note - which needs the field to exist.

## What was built

- `StrikeTeamInfo.Journal` (campaign page) and `StrikeTeamInfo.MissionNotes` (`CampaignMissionNote`
  per mission that has one, kept in mission order).
- `CampaignJournal` - `Clean`, `SetCampaignNote`, `SetNote` (blank removes the entry), `NoteFor`,
  `Missions` (every played mission + the next one + any mission that already has a note), `IsPlayed`.
- A Journal button on the camp screen opening a three-pane page: campaign note | mission list | the
  selected mission's note. Played missions read as an account, the next as a briefing.
- The list's detail panel shows the first 280 characters of the campaign note.
- Fixed in passing: the camp header printed the enum ("PointLimited campaign"); it now uses
  `CampaignStanding.Describe()`.

## Decisions

- **Not in `History`.** History is the game's log, and `RecordOf` (#446) reads it back to recover older
  campaigns' wins - a journal line beginning "Won the mission on" would have counted as a win. Tested.
- **ASCII-folded on the way in** via `OprBookImporter.AsciiFold`: the font has Basic Latin + Latin-1
  only (CLAUDE.md), so a pasted em dash or curly quote would show as '?'. Accented letters lose their
  accents too, which is the fold's existing behaviour - recorded in case Kai minds.
- **Committed on focus loss / page change / leaving, not per keystroke** - cleaning trims trailing
  whitespace, so a per-keystroke commit would eat the space before the next word.
- **Before AND after** are both allowed (the next mission always has a page), rather than asking Kai to
  choose up front.

## Still open for Kai (recorded in claude/fdg-todo.md)

Whether the game should **pre-fill** anything from what happened (who died, events, result), and whether
he wants an **exportable** campaign log. Neither built - both are his call.

## Tests

Engine `CampaignJournalTests` (13), app `CampaignJournalFileTests` (1, through the real campaign file).
Engine 4738 -> 4751, app 8864 -> 8865. Smoke exits 0. The page's drawing is not covered (no test draws
an ImGui frame); its new `BeginChild`/`EndChild` pairs are covered by `DisabledScopeBalanceTests`.
