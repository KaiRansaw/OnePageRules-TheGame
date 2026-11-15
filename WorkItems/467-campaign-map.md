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
2. Rivals running their own armies between turns (CampaignBot spending, claims vs units).
3. Godot screen: the map, region card, turn report; player battles through Mission Setup; garrison missions pay nothing.
4. Region battlefields: a terrain mix per region type.

## Notes (newest first)

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
- Garrison battles pay no treasury/VP (else the book's loss payout beats paying) - Kai's spec.
- A taken capital stays starred (worth 1 VP to its new holder).
- Claim cost uses at least 1 region held, so an army never pays 0.

## Outcome
(open)
