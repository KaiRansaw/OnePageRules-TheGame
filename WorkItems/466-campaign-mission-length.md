# 466 — Campaign missions: playing past round 4, and conceding

**Status**: done
**Related**: #410 (campaign 3.6.0), #432 (round-start random events), #462 (Mission Cards)

## Goal
The two "Play Mission" rules all four 3.6.0 campaign books (GF, GFF, AoF, AoFS) print word for word:
- **Mission Length:** "if after 4 rounds there is no winner, the game continues onto the next round until
  there is one winner."
- **Conceding:** "At the beginning of any round players may concede, removing all of their units from the
  table, which don't count as casualties for this mission."

Quest campaigns are out of scope: their Campaign Builders are their own books.

## 2026-11-06 — built

- `StateMachine/CampaignMissionLength.cs`: `Applies` (Standard mode, not Quest, a campaign sheet on the
  table), `PlaysOn`/`HasOneWinner` (the victory stage's arithmetic, read-only; Mission Cards preview the
  final objectives' +1 VP without scoring them), `RunRoundStart`/`Concede`, `HasConceded`, `InPlayers`,
  `TeamsStillIn`.
- **Concede is a button** (Kai, 2026-11-06, over a question every round). `ConcedeCheckRequest` is asked of
  every player still in at the top of each campaign round; the GUI answers at once from `ConcedeIntent`
  (the Mission tab's button, lives on `GuiResolverOverlay`), CLI and bot always false. It rides the
  ordinary request path, so it reaches the host from a networked client with no new message.
- Conceding: positions to the unplaced sentinel, then reserve (Ambush's leaving order, #309), pending
  arrival tokens dropped, new `TokenType.Conceded` on every unit (dead ones too, so an already-tabled
  player still reads as conceded). Alive, so `StrikeTeamFiles.WriteBack`'s casualty check (`!GetIsAlive`)
  passes them over. Their objectives go neutral. Arrivals (`BringOnReserves`, aircraft) skip Conceded.
- `StartOfRoundExtraActionStage` runs the check first thing after the round banner and has a new
  `ToVictoryCalculation` binding for when one side is left. `ReconcileObjectivesStage` plays on at the
  limit when `PlaysOn`; its objective scan now skips reserve units (their models sit at the sentinel).
- `VictoryCalculationStage`: a lone remaining side wins outright; otherwise conceded players are left out
  of the objective and VP tallies.
- Mission Cards' end-of-round discards ask `MoreRoundsAfter` instead of `round < limit`.
- UI: Mission tab Concede button (confirm popup, "Take it back" until the round starts); round label reads
  "ROUND 5 - extra round (no winner yet)" past the limit; Load Game shows "Round 5 (extra)".

## Decisions (house rules B18, B19 and section C)
- Cap at round 12 as a draw (no book limit; Horde's length).
- Bot never concedes; CLI can't.
- Conceding loses; held objectives go neutral; 3+ sides play on without the conceder.

## Outcome
Engine: `CampaignMissionLengthTests` (13) and `CampaignMissionLengthInAPlayedGameTests` (3: concede at
round 2 in an AI-vs-AI campaign game, an ordinary game never asks, eight seeds of campaign games never
tie before the cap and at least one runs past round 4).
Not verified by clicking: the Mission tab button in a GUI campaign mission.
