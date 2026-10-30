# 414 - Star Quest rebased onto GFSQ v2.1.0

**Goal.** Star Quest is built explicitly against **GFSQ Core Rules v2.0.1** (the version is named in the
source comments of `QuestActions.cs`, `StarQuestAi.cs`, `QuestHero.cs`, `QuestCampaign.cs`). v2.1.0
changes numbers the player feels every turn, plus the progression table behind every reward. Bring the
whole mode onto 2.1.0.

Scope, from `claude/campaign-rules-delta.md` and the two books in the project (GFSQ Core Rules v2.1.0,
GFSQ Campaign Builder v2.1.0):

- Power costs: extra action, extra skill action and reviving go 2 -> 3.
- A failed hero skill does nothing unless you choose to spend 1 power (reverses the 2.0.1 reading).
- Casting reshaped: spend tokens/power BEFORE rolling, no retroactive save.
- Melee reach a flat 3"; fatigue after any melee the unit attacked in; dangerous terrain per unit;
  falling and jumping rewritten; Strider halves jumping dice.
- AI steps 1 and 2 widened; Villain aura 12" -> 6" and mandatory; AI Groups 1"/3".
- The levelling table, the reward arithmetic and the injury table.

## Notes

### 2026-10-30 - progression table, and two QA notes closed by testing rather than fixing

Kai's pick for this session was "mission rewards + the boss-fight offer", aimed at QA 6.x ("every hero
got 5c and 0xp"), QA 4.1 / 5.5 ("boss fight never offered") and QA 5.2 ("reinforcements don't arrive").

**The reward arithmetic was already correct and already pinned.** `StarQuestMissionRules.HazardPay` is
`5 * difficulty` and `Xp` is `2*d` for the primary plus `d` for the secondary, matching the Campaign
Builder, with the boss bonus added in the victory stage. `MissionNumbers_FollowTheCampaignBuilder` has
asserted two of those since the mode was built.

**What was actually wrong was the table the rewards feed:** `QuestProgression` still held the **v2.0.1**
levelling curve.

| | v2.0.1 (was) | v2.1.0 (now) |
|---|---|---|
| XP per level | 0, 0, 2, 4, 8, 12, 18, 24, 32, 40, 50, 60, 82, 94, 108, 122 | 0, 0, 3, 6, 12, 18, 27, 36, 48, 60, 75, 90, 108, 126, 147, 168 |
| Level 4 offers | +2 Tou / **+1 Qua** | +2 Tou / **+2 Pow** |
| Level 8 offers | +2 Tou / **+2 Pow** | +2 Tou / **+1 Qua** |

That is a different curve end to end, not a tweak - level 2 cost 2 XP instead of 3 and level 15 cost 122
instead of 168, so heroes levelled roughly a third faster than the book intends and the gap widened at
every tier. Levels 4 and 8 were also swapped, which handed out **+1 Qua - the strongest improvement on
the list - four levels early.** `HeroBuilderScreen`'s "next level at X XP" line reads the same array, so
it was showing the old numbers too and is fixed by the same change.

**QA 6.x (0 XP) does not reproduce.** `StarQuestMissionRewardTests` runs the real
`StarQuestMissionVictoryStage` and reads each hero's own sheet afterwards: credits **and** XP both land,
at every difficulty, with and without the secondary, with and without a destroyed boss wave, and a hero
on 12 XP comes out level 4. Most likely cause of the original report: `HeroesAtOneCorner` gates
Mission End on `PrimaryDone` (correctly - the book requires the primary before you may leave and
collect), and that gate is now pinned by a test, so a party cannot walk into a corner on round 1 and
collect hazard pay for nothing. If Kai sees 0 XP again it is a different bug and these tests say where
it is not.

**QA 4.1 / 5.5 (boss fight never offered) is not a bug either.** Boss Fights is an **optional challenge**
("players may choose any of the following optional rules"), so with it switched off at mission set-up
there is no offer at all, by design. `OfferBossFight` works: off -> never asked, on -> asked, default
**no** (it makes the game harder, so an unanswered prompt must not spring a double wave), and a decline
with `askOnce` records `BossFight = -1` so the question does not return every round. All four pinned.

**QA 5.2 (reinforcements don't arrive) did not reproduce either** - see `WorkItems/413`; a headless
mission deployed them five times and the arriving unit acted each round after.

**New: `--challenges`** (`all`, or a loose comma-separated list). The challenges are picked from a menu
at set-up and an automated answerer takes the first option - "Start" - so **every scripted run before
this played with none of them on**, and the boss fight in particular was unreachable. It drives
`QuestChallenges.ForceForTests`, whose doc comment now says why setting it also means yes to the boss
fight: both callers have nobody at the keyboard, and the offer's own default is no, so otherwise the
challenge could be on and the wave would still never arrive. Verified: a mission run with `all` fires
random events, rolls Alarmed/Time of Day/Weather, and applies the Alarmed extra alertness.

**One engine wart noticed, not fixed:** `GetIsOnBattlefield` treats a model at exactly `(0,0)` as not
placed, and corner 1 of a Star Quest table **is** the origin - so a unit standing exactly on corner 1
reads as off the table. The reward tests use `NearCorner` instead, which is the real case anyway
(heroes gather *within 6"* of a corner), but a hero placed precisely on that corner in play would not
count towards Mission End. Worth a look if Kai ever reports an exfiltration that refused to trigger.

**Tests added** (`StarQuestMissionRewardTests`, 39 cases): hazard pay and objective XP at every
difficulty; the full v2.1.0 XP curve and its level boundaries written out; levels 4 and 8 against the
book; the victory stage writing pay and XP to every hero's sheet; hazard pay landing with nothing
completed; the boss bonus present and absent; a failed mission paying nothing; a level-up on the new
thresholds; the Mission End gate on the primary and its deliberate indifference to the secondary; and
the four boss-offer cases.

Suites: engine 3,781 passed / 0 failed, UI 3,026 passed / 0 failed, headless smoke exits 0.

**Not started:** everything else in the goal above - power costs, the optional failed-skill payment,
casting, melee 3", fatigue, dangerous terrain, falling, jumping, the AI steps and the Villain aura. The
power-cost strings are still visibly wrong in a play log ("Revive a hero (2 power)").

## Outcome

Open. The progression table and the reward/boss-offer coverage are done; the behaviour changes that make
up the bulk of 2.1.0 are not started.
