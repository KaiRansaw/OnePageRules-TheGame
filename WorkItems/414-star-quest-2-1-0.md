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

### 2026-10-30 - the once-per-game mechanism: what is actually missing

**The headline finding: the engine DOES have a per-unit once-per-game offer.** Three separate docs say
it does not - `Campaign36RuleCatalog`'s class comment, every one of the eight "not wired" skill
descriptions, and the `RuleCatalogLintTests` allowlist reasons - and all of them are **out of date**.

`Cost.OncePerGame` (`Rules/Foundation/Cost.cs:70`) is a first-class cost on `ActivatedAbility`, and
`RuleEvaluator` implements the whole gate (`:992` affordability, `:1007` payment):

```
UsedMarker(ruleName) => new TokenType("AbilityUsed:" + ruleName)      // RuleEvaluator.cs:1032
Cost.OncePerGame     => GrantTokenToUnit(bearer, new Token(marker, 1, ManualOnly()))
```

So the ledger is a per-rule token with a `ManualOnly` clear trigger - it serializes, saves, and takes
back like any other token, and nothing sweeps it at end of round. It is already used by shipped core
rules (`CoreRuleCatalog.cs:1757`, `:1779`, `:2091`, `:2183`) and is covered by integration tests that
pin declining *without* spending the use (`AmbushRedeployRuleIntegrationTests`,
`InquisitorialAgentRuleIntegrationTests`, `StormRuleIntegrationTests`).

**So the nine do not share one gap. They split in two:**

**Group A - four are ordinary activated abilities** that nobody ever wrote: Explorer (ignore Difficult
and Dangerous terrain), Idol (ignore becoming Shaken), Berserker (ignore becoming Fatigued),
Sharpshooter (+12" range when shooting). Each needs the right `EHookID` and an `Effect` that exists;
the once-per-game part is free. `Morale_OnShakenApplied` (`EHookID.cs:305`) is already there for Idol.

**Group B - five are one missing feature, not five:** Tactician (melee), Strategist (shooting),
Mastermind (force an ENEMY re-roll when targeted) and `Trait: Elite` all say **"re-roll one roll of any
kind"**, and the engine has no *player-offered* re-roll. What it has is passive: `RerollSink`
(`Rules/Dispatch/RerollSink.cs`) folds rule-requested re-rolls into two flags - "re-roll saves at or
above N" (Bane/Mischievous/Scrapper) and "re-roll a failed morale test" (Fearless) - which the stages
then apply automatically. There is no path for "ask the player whether to spend a limited resource on
this particular roll".

Elite is the precedent and it is deliberately narrow: `MoraleUtilities.cs:150` calls
`HordeExperience.TryUseElite` (`HordeStages/HordeExperience.cs:214`), which checks a bespoke
`TokenType.HordeEliteUsed` and **auto-spends it with no prompt**, only on a failed morale test.

**Kai's call, 2026-10-30, on the one question that is his:** a re-roll offer is made **only when it
would help** - on a roll that actually failed, never on a successful one, and Idol/Berserker only when
the Shaken/Fatigue is about to land. He rejected both "ask every time it could apply" (a hero with
"re-roll any roll" would be asked on every roll it makes all game) and "arm it in advance" (committing
before seeing the dice is strictly worse for a re-roll). This also brings Elite into line with his
standing rule that the game never auto-picks - its current silent auto-use becomes a prompt.

**The three seams a re-roll offer has to reach**, all small focused stages:
- hit rolls - `ShootStage/FireStage/RollToHitStage/RollToHitStage.cs`, right after
  `SubsetAtOrAbove` / `SubsetBelow` split the roll and before the attack beat
- save rolls - `ShootStage/FireStage/RollToSaveStage/RollToSaveStage.cs`
- morale - `MainUnitActionStage/MoraleUtilities.cs:147`, where Elite already sits

**One wrinkle worth writing down:** dice results are float-counted per side (`DiceResults` wraps a
`float[] perSideValues`) because the game supports probabilistic "average dice" as well as real ones.
So "re-roll one die" cannot be done by mutating a die - it is: roll one fresh decisive die, and if it
meets the threshold, rebuild the results with one count moved from a failing side to a passing one.
`DiceResults(float[] perSideValues, int sideMin)` is public, so this is straightforward, but it is the
part most likely to be got subtly wrong, and it needs a test per stage.

**Scope split, said out loud rather than cut quietly:** Group A is being done first - it is four of the
nine, it rides on a mechanism that already works and is already tested, and it carries no new
architecture. Group B is a separate slice: one shared "offer a re-roll on a failure" helper plus three
stage call sites, and **Mastermind is the odd one even within it**, because it spends the hero's use to
force a re-roll on the *opponent's* roll rather than re-rolling the hero's own. Mastermind lands last.

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
