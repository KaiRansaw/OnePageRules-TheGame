# 467 — Campaign map (territories between missions)

**Status**: in-progress
**Related**: #410 (campaign engine), project docs `claude/campaign-map-spec.md` (the agreed design) and `claude/house-rules.md`

## Goal
Kai's agreed end goal for the army campaigns (GF, GFF, AoF, AoFS): a map of regions fought over between missions -
a star system for the sci-fi games, a realm of regions and roads for fantasy - with 1-3 self-running rivals, paid or
fought-for land, region perks, starred regions worth VP, two-win capitals, and region types that set the battlefield.
Done when a map campaign can be started, played turn by turn in the Godot build with the player's battles as real
missions, saved and resumed, and won or lost.

The OPR campaign books have no map rules: every rule here is a house rule.

## Plan (agreed with Kai 2026-11-14)
1. **Engine** - map model + file, generator, rules, turn resolution, rival orders. **Done 2026-11-14.**
2. Rivals running their own armies between turns (CampaignBot spending, claims vs units). **Done 2026-11-15.**
3. Godot screen: the map, region card, turn report; player battles through Mission Setup; garrison missions pay nothing.
   **Done 2026-11-15 (Build 2.26).**
4. Region battlefields: a terrain mix per region type.

## Notes (newest first)

### 2026-11-15 - step 3, the screens (Build 2.26)
- Mockup "Map Campaign Screens" approved (Kai: rivals may share factions; Rest is the default order; the Journal keeps
  every turn's report).
- Engine: `StrikeTeamInfo.NoMissionReward` (AfterGame pays nothing; write-back skips the special objective) and
  `MapUnderdogBonus` (UnderdogPool.Seed adds it, players only); `MapRivalArmy` (auto-built rivals and garrisons,
  `Adopt` re-points sheets' TeamFile); `CampaignBot.IsHeroEntry` public; generator retries layouts until nobody
  borders on turn 1 (Medium/Large: 0 of 100 maps in the test, was 24).
- FdgApp: `MapCampaignSession.PrepareBattle / CollectBattle / BattleTitle / LaterPlayerBattles / ReloadArmies`,
  `RivalBooks / BuildRival`; `ArmyCampaignSession` draft map settings, rival reroll / use-my-army, `FinishMap`, map rows.
- Godot: `MapCampaignScreen` (+ `MapView`), wizard Classic/Map, list detail + OPEN THE MAP, Camp BACK TO THE MAP,
  BattleScreen CONTINUE TO THE MAP, Mission Setup garrison text. ScriptedRun `mapdemo:<n>` / `mapdo:<order>`.
- Not tested end to end in a played GUI game here (xvfb is too slow for a whole battle); the write-back half is covered
  by `AGarrisonBattle_IsSetUp_PaysNothing_AndLandsOnTheMap` and the AfterGame / underdog tests.

### 2026-11-15 - step 2, rivals run themselves + the files
- `CampaignMapUpkeep`: `Apply` (a turn's off-table change into StrikeTeamInfo), `LandReserve` (next claim's price while
  neutral land is reachable), `RivalBetweenTurns` (CampaignBot.ResolveChoices + Spend with the reserve held back).
- `FdgApp/Quest/MapCampaignSession`: Start (every army as a campaign army, rivals as bots, Endless type so the map
  decides the end; map beside the player's file, `StrikeTeamInfo.CampaignMapFile`), Open, States/Vp/Outcome,
  BeginTurn / Step / RecordBattle, SaveAll. `ArmyCampaignSession.CampaignFiles` now includes the map and every rival.
- Rivals' purchases reach the turn report only once the player has met them (fog).
- Not yet: off-screen rival-vs-rival battles cost no casualties or XP (they only pay the book's rewards); Forge and
  Medical perks still to wire into the Camp (step 3).
- Tests: 5 `CampaignMapUpkeepTests` (sabotage-checked: no reserve fails), 7 `MapCampaignSessionTests`.

### 2026-11-14 - step 1, the engine
- `FDG.Campaign.Map`: `CampaignMap` (+ .fdgmap JSON file), `CampaignMapGenerator`, `CampaignMapRules`,
  `CampaignMapTurn` (+ `CampaignMapBot`). `StrikeTeamInfo.CampaignMapFile` marks a map campaign on the player's army.
- Turn resolution is a pure replay from `CampaignMap.Pending` (orders, the armies' books at turn start, the player's
  results so far) with dice seeded from map seed + turn, so a reload mid-turn replays the same turn. It stops at
  every player battle (`MapTurnResult.WaitingFor`); the mission pays the player's battles, the map pays everything
  off the table (`MapTurnResult.Changes`: claims, rest, Hive income, rival-vs-rival rewards).
- Generator: planar, connected, degree 2-4 where the layout allows; capitals spread by road distance, never adjacent;
  every capital a Hive and every starting region a Forge, so nobody starts ahead; Fortresses starred.
- 25 tests in `CampaignMapTests` (generation over 150 seeds, every rule, siege/knock-out, clash, rolls, replay after
  save/load, 60 whole campaigns played out by bots).
- Simulated pacing (bots for everyone, coin-flip battles, 100 campaigns each): first army-vs-army battle on turn
  ~1.3 (Small) / ~1.5-3 (Medium) / 2-5 (Large); player battles ~1.3 per turn; campaigns last ~7-22 turns. With 3
  rivals the rivals win by VP far more often than the coin-flipping player - expected, but worth watching in play.

## Decisions
- **Rival armies are auto-built** (Kai, 2026-11-15): each rival gets a random army book of the game and builds its own
  starting army from the treasury; any rival can be swapped for an army the player built.
- **Map campaigns live inside Campaign** (Kai, 2026-11-15): the wizard's first step asks Classic (one rival) or Map
  campaign; both show in the same campaign list.
- Garrison battles pay no treasury/VP (else the book's loss payout beats paying) - Kai's spec.
- A taken capital stays starred (worth 1 VP to its new holder).
- Claim cost uses at least 1 region held, so an army never pays 0.

## Outcome
(open)
