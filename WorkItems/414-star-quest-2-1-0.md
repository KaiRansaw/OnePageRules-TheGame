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

### 2026-10-30 - casting: power before the roll, and no rescue after it

v2.1.0 Caster(X), verbatim: *"Once per activation, before attacking, spend as many tokens as the spell's
value to try casting one or more spells (only one try per spell). Pick a target and roll one die, and on
4+ resolve the spell's effect. Heroes may spend any number of **tokens/Power** before rolling to get +1 to
the roll per token/Power, or to give an enemy Caster within 18" -1 to their roll per token/Power."* The
POWER REFERENCE repeats it: *"Heroes with Caster may spend X Power to get +X to casting rolls or give
enemies -X to casting rolls."*

v2.0.1 said tokens only before the roll, and then: *"Whenever a hero fails to cast a spell, it may spend
X*2 power to count as having cast it instead."* So the reshaping is the same one the failed hero skill got
in the note below - a retroactive rescue becomes a commitment made before the die is thrown.

**The retroactive rescue was never implemented** (searched: no code path spends power on a failed cast,
and no prompt follows the roll). So there was nothing to delete; what was missing is the Power half.
`QuestCastPowerTests.NoRescueAfterTheRoll` pins the absence anyway, because the next person reading an old
book would otherwise add it as a missing feature.

**What was already there:** the pre-roll token boost (#244) and the 18" hinder (#103), both complete.
This slice adds Power as a second currency to each.

`CastPower` (new, beside `SpellPurse`) answers "what Power may this unit put into a cast", and is
deliberately NOT another pool inside the purse: the purse pays the spell's **cost**, and the book never
lets Power pay that - Power buys modifiers only. Tokens are also lendable (Spell Accumulator) and refresh
every round, where Power is a mission-long budget refilled only by a Rest action.

**Both currencies are carried separately end to end, and that is the design decision worth recording.**
Summing them into one "sway points" number and spending tokens first would have been a third of the code:
one spinner, no new reply shape, no resolver churn. It is wrong because the two are not interchangeable
*to the player* - tokens are what a second spell this activation gets paid with, and Power is what revive,
extra actions and failed stat tests come out of. A picker that chose for them would quietly cost them the
other thing. So `ChooseSpellReply` gained `BoostPower` and `CastAssistRequest`'s reply became the
`CastAssistReply(Tokens, Power)` record (it was a bare `int`), which is what rippled through six
resolvers.

Three smaller things fell out of it, each a real behaviour change:

- **A caster holding no tokens is now asked to counter-spell.** `FindEligibleAssisters` skipped anything
  with an empty pool, which was right when tokens were the only currency. A hero with 4 Power and no
  tokens can counter under 2.1.0.
- **An enemy hero's Power counts toward the picker's hinder estimate**, which is what greys out boost past
  the point it can matter. Leaving it out would cap the player's boost early in exactly the situation
  where boosting matters most - a hero duel.
- **Overspend is clamped, never exhaustion.** `QuestActions.SpendPower` wounds a hero one per missing
  Power, which is correct when a rule charges more than it has; reachable from a boost spinner it would
  turn a fat finger or a misbehaving resolver into a dead hero. `CastPower.Clamp` is the guard and has its
  own test. Diseased ("takes as many wounds as Power they spend/lose") still applies, because the spend
  goes through `SpendPower` rather than decrementing the field.

GUI (RENTAL, one pass): the assist dialog keeps its original one-tap button list for every caster with no
Power, and becomes two steppers plus Confirm only when Power is available - a one-tap list cannot express
"2 tokens and 1 power". The spell picker gained a second stepper.

**The AI deliberately spends no Power.** For enemy casters that is the book (`Caster(X) [AI Units]` spends
tokens), and for a bot playing the hero side under `--all-ai` it is the same conservative stance the AI
already takes on token assists.

Verified: engine **3,811 / 0** (8 new tests), UI 3,026 / 0, `dotnet build` clean, headless smoke exit 0,
seeded all-AI Star Quest mission exit 0 over 103 rounds. The eight new tests were **sabotage-checked** -
stubbing `CastPower.Available` to 0 fails five of them, which is the right five (the other three are the
control, the non-hero guard and the no-rescue pin, all of which must pass either way).

**Also fixed, unrelated:** `LobbyBotGreetingTests.MultipleBots_NeverPickTheSameLine` is a wall-clock race
that intermittently saw 7 of 8 bot greetings in 5s on this 2-core box - it failed one run in five while
this slice was being verified, and has nothing to do with casting. Timeout raised to 20s. It costs nothing
on a healthy run, and a suite that fails one run in five trains everyone to re-run instead of reading.

**Not done in this slice:** "once per activation, before attacking" is still the old "at any point before
attacking" gate. Worth checking separately - `UnitActionContext` records spell attempts per spell name
already, so the question is whether a second *different* spell in one activation is still allowed (2.1.0
says one casting attempt sequence per activation, several spells inside it).

### 2026-10-30 - power costs on the v2.1.0 reference

Verified against the **POWER REFERENCE** section of GFSQ Core Rules v2.1.0 before changing anything,
because these are numbers the player pays every activation:

| | v2.0.1 (was) | v2.1.0 (now) |
|---|---|---|
| Any one additional action | 2 | **3** |
| Revive an Unconscious Hero | 2 | **3** |
| Count a failed Str/Dex/Wil test as passed | 2 | 2 |
| Ignore being Shaken from a failed Morale Test | 2 | 2 |
| A failed Hero Skill test | 1 | 1 |

An extra **Skill** action is "any one additional action", so it takes the extra-action cost - the two
already shared a branch in `ChooseActionStage`.

**The costs are now named constants on `QuestActions`** (`ExtraActionPower`, `RevivePower`,
`PassStatTestPower`, `IgnoreShakenPower`, `FailedSkillPower`) rather than literals. That is the
substantive part of the change, not tidying: each cost appeared **both in the rule and in the
player-facing label**, written out separately across five files - `QuestActions` (the option name, the
revive skill name, the revive description, two `SpendPower` calls, the stat-test prompt),
`ChooseActionStage` (two tooltips and a comment), `MoraleUtilities` (the Shaken prompt and its
`SpendPower`) and `UnitActionContext` (a doc comment). A sweep that caught the rule and missed a label
would leave the button quoting a price the game does not charge, which is worse than either being
wrong on its own.

`ReviveName` had to become `static readonly` rather than `const` to interpolate the constant. It is
looked up by name (`QuestSkill.Find(QuestSkill.ReviveName)`), so `QuestPowerCostTests` checks that the
lookup still resolves - that is the test that catches a half-finished rename.

**Verified in a played game**, not just by test: a headless Star Quest Horde run now prints
`Extra Action (3 power)`, `Extra Skill (3 power)`, `Revive a hero (3 power)` and
`Spend 3 power to take another action`.

**Next in this cluster, not done:** v2.1.0 made the 1-power payment for a failed hero skill
**optional** - fail, and you *may* pay to count it as passed, where 2.0.1 forced the payment and
resolved the skill either way. This build still forces it. That also settles QA 3.10 ("never
auto-spend power, always ask"), which is currently filed as a preference and is in fact a rules
requirement. *(Done - see the next note, below.)*

### 2026-10-30 - the failed hero skill became optional (and can now fail)

v2.1.0, verbatim: *"When using Hero Skills, take a test of that Skill's stat. If passed the effect is
resolved, and if failed you **may** spend 1 Power to count as having passed it instead."* v2.0.1 ended
that sentence *"...1 Power and then the effect is resolved"*.

So the old behaviour was wrong **twice**, and the second half is the part that matters:

1. It took the power without asking (QA 3.10 - Kai filed this as a preference; it is a rules
   requirement).
2. **A hero skill could never fail.** Fail the roll and the skill still happened, for a fee. That
   quietly removed the risk from every hero skill in the game and made the stat on the card
   decorative - a 6+ Wil skill and a 2+ Wil skill cost the same on average and both always worked.

`QuestActions.HeroSkillTest` now returns `Task<bool>`: on a fail it offers the payment via
`YesNoRequest` (default yes only while the hero can afford it - spending power a hero does not have
costs a wound per missing point, same shape as the stat-test and Shaken offers), and a decline returns
false. `QuestSkill.Use` honors that and skips `_resolve`.

**Fixing only the prompt would have been the trap.** Ask, then resolve regardless, and it looks correct
in play right up until someone declines - the prompt would be a lie. `QuestFailedSkillPaymentTests`
pins the fizzle itself (`AFailedTest_FizzlesWhenThePaymentIsDeclined_AndCostsNothing`) as the real
regression guard, plus the pass path asking nothing, the paid path charging exactly 1, and the
affordability default - that last one is what every automated answerer takes (bots, EOF-piped headless
runs, a dismissed dialog), so a broke hero must not be defaulted into wounding itself over a bad roll.

Probe skill is **Rally**: non-common, non-feat, Wil test, and its whole effect is removing the Shaken
token, so "did the skill resolve" is one observable flag with no table needed. Driven through
`QuestSkill.Use` rather than `HeroSkillTest` alone, because the decision that matters lives in the
caller.

Also corrected the `QuestSkill` class comment, which still described hero skills as always working.

Verified: engine 3,803 / 0 (one skipped by design), UI 3,026 / 0, `dotnet build` clean, standard
headless smoke exit 0, and a seeded all-AI Star Quest mission (`--party auto --all-ai --seed 414`)
exit 0 over 103 rounds. The AI happened not to fail a hero-skill test in that run, so the new branch
is covered by the fixture rather than by that log.

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

**Scope split, said out loud rather than cut quietly:** Group A is being done first - it rides on a
mechanism that already works and is already tested, and it carries no new architecture. Group B is a
separate slice: one shared "offer a re-roll on a failure" helper plus three stage call sites, and
**Mastermind is the odd one even within it**, because it spends the hero's use to force a re-roll on
the *opponent's* roll rather than re-rolling the hero's own. Mastermind lands last.

#### Correction, same day: Group A is TWO, not four

Mapping the hooks turned up a constraint that changes the count. **Only ten hooks ever offer an
activated ability** (`RuleFireLint.AbilityOfferingHooks`, `Rules/Dispatch/RuleFireLint.cs:261`); an
`ActivatedAbility` authored anywhere else is dead data the catalog fire-lint rejects. The terrain and
range hooks are passive-only, so the shape for both buildable skills is: offer at
`Activation_OnActivationStart`, grant a helper rule with `Effect.AddRule(..., ThisActivation)`, and put
the real passive on the helper. That is the pattern `SpeedFeatRuleIntegrationTests` already uses.

**Done (`CampaignOncePerGameSkillTests`, 8 cases):**
- **Explorer** -> grants `Explorer: Surefooted`, whose passive is
  `IgnoreTerrainEffects(ETerrainIgnoreScope.AllTerrain)` at `Movement_OnMoveThroughTerrain`. `AllTerrain`
  and not `DifficultOnly` matters: `DifficultOnly` is Strider and does **not** waive Dangerous, so the
  test asserts through `MovementRuleQueries.IgnoresAllTerrain`, which is the query that tells them apart.
- **Sharpshooter** -> grants `Sharpshooter: Long Shot`, whose passive is `RangeModifier(+12)` at
  `Shooting_OnRangeCheck`, asserted through `RangeRuleQueries.EffectiveRange` (the fold the shooting
  stage actually reads). Offering at `Activation_OnBeforeAttackAction` would read closer to the book's
  "when shooting", but abilities there are taken *instead of* attacking, so the skill would cancel the
  shot it is meant to extend.

Both helper rules are registered in `Campaign36RuleCatalog.All` so the resolver can find them, and a
test pins that neither appears on any hero skill table - otherwise a level-up could roll
"Explorer: Surefooted" as a skill.

**Still blocked, and NOT for the reason the old comments gave:**
- **Idol** ("may ignore becoming Shaken") - there is no cancellable Shaken hook.
  `Morale_OnShakenApplied` fires *after* `ApplyShaken` has already added the token and is not an
  offering hook, and **no Effect exists that removes or prevents a token** (`ConsumeToken` is
  unconditional, `ClearTokenOnRoll` is dice-gated). Note there is already a hardcoded precedent for
  exactly this shape in Star Quest - a prompt inside `MoraleUtilities.ApplyShakenWithPresentation` -
  which is the seam to generalise.
- **Berserker** ("may ignore becoming Fatigued") - worse: **no fatigue hook exists at all.**
  `FatigueUtilities.ApplyFatigued` fires nothing, and `CountsAsFatiguedInMelee` reads tokens directly
  with no rule dispatch, so either way that function has to change.

Both are small engine seams rather than rule authoring, and they belong with the Group B slice.
The lint allowlist entries for Explorer and Sharpshooter are removed; the remaining six had their
reason strings corrected, since every one of them claimed a gap that does not exist.

#### Group B deferred by Kai, 2026-10-30 - except Elite's prompt

Kai's call: the six remaining skills are **only reachable in Firefight Campaign, which he has not yet
played a mission of**, and the re-roll plumbing runs through the shooting path - the most load-bearing
code in the game, and the one T1 proved can break catastrophically and silently. Building for players
who cannot reach the feature, in the riskiest file, is the wrong trade this week. Deferred whole.

**One piece was carved out and done, because `Trait: Elite` is live** - it is a Horde trait a unit can
roll on any level-up, so unlike the campaign skills it is in play today. Two things were wrong with it;
this fixes the second:

1. It only covers the morale test, where the book says "one roll of any kind". **Unchanged** - that is
   the deferred breadth.
2. **It spent itself silently.** `TryUseElite` checked the gate and paid it in one call, so the first
   failed morale test of the game burned the unit's only re-roll, with no prompt and no log line the
   player would read as a decision. A unit happy to fail a morale test lost its re-roll anyway.

Split into `CanUseElite` (check, free) and `SpendElite` (pay), with a `YesNoRequest` between them in
`MoraleUtilities`. The request's `DefaultAnswer` is `true`, so every automated answerer - bots, the AI
resolvers, EOF-driven headless runs - behaves exactly as it did before, which is why the whole suite
stayed green without a single expectation changing.

`HordeTraitTests` gains the case that matters: **asking twice without accepting does not spend the
use.** That is the half that silently breaks if the two calls are ever merged back together. The
fixture's old name said "once per round", which 3.6 changed a patch ago; corrected to "once per game".

**Note for whoever does the rest:** the headless smoke (`printf "2\n2\n" | ... --headless`) takes no
seed, so its result varies run to run - it tied twice and lost once in a row here. Exit code 0 is the
criterion, not the outcome line. Worth knowing before anyone reads a changed result as a regression.

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
