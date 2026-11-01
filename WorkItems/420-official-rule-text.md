# 420 - Official rule wording, 1:1 with the books

**Goal:** every rule's player-facing text is OPR's own wording for the system being played, not a
hand-written summary - and any implementation the comparison exposes as wrong gets fixed in the rule,
not papered over by correcting its label.

**Raised by:** Kai, 2026-10-31, from a Ravage tooltip in the in-game Army Builder: *"the correct
wording is shown in the screenshot, but the in game tooltip says 'When Attacking in melee, rolls dice
that deal automatic unsaveable wounds before swings.' what? that makes no sense. does Ravager even
work? How many other special rules/ tooltips/etc are worded weird? i want everyting to be 1:1 in
wording as the official book. none of this 'sumarize', cause while your phenominal at coding, your
summarization skills with these rules is lakcing."*

---

## Notes

### 2026-10-31 - Unstoppable's modifier half, and two findings that are not faults (B29)

**Unstoppable's second facet is in, and the obstacle was never the effect.** The rule, identical in all
six systems: "this weapon ignores Regeneration, **and ignores all negative modifiers which are not from
this model's special rules**." The Regeneration half was built; the second carried a comment saying it
"needs a modifier-immunity effect and lands when that's modelled". What actually blocked it was that
`RollModifierSink` kept TOTALS - a net, and since #412's Pierce a net plus a positive/negative split -
while this clause asks where a penalty **came from**, which no total can answer.

So `RuleOperation.ApplyRollModifier` now carries the `UnitID` of the unit whose rule produced it
(`Effect.RollModifier` and its two siblings read it off `ruleInvocation.Bearer`), the sink keeps each
unfavourable entry with its source, and `NetIgnoringForeignNegatives(roll, bearer)` is the Pierce-shaped
read: the same "drop part of the stack" move, choosing by **source** instead of by sign.
`DetermineHitRollStage` branches on the marker exactly where it branches for Pierce.

Why the hit roll only: an unfavourable modifier to the DEFENDER's save is the attacker's own AP, which
this clause has no business removing, and the defender's save bonuses are favourable. So in play it
strips the target's Stealth and the -1 for shooting at an Aircraft, and leaves the model's own penalties
(the `HitPenaltyWhenShooting` family) alone - which is the whole point of "not from this model's special
rules", and the half a blunt "ignore all negatives" would have got wrong. Five cases, including the one
that proves the two are separated rather than one winning (own -1 kept AND target's -1 dropped: 4 + 1,
not 4 + 2), and an untagged negative counts as foreign because the attacker's own rules always arrive
tagged.

An untagged write still works: `IRollModifierSink.Add(roll, delta, source)` is a default interface
member forwarding to the two-argument version, so every existing implementer is untouched.

**Not a fault - do not re-report (this is the third entry on this list for a reason).** The audit's line
on **Harassing / Hit & Run Shooter** said "once per round" was missing. It is not: `PostCombatMoveGate`
has enforced it since #391, with one shared `PostCombatMoveUsed` token per unit (RoundEnd clear), set
only when the unit ACTUALLY repositions so a declined move keeps the round's budget, and shared across
shooting and melee so a unit moves at most once after combat per round. Read the gate before touching
the family. (`Hit & Run Shooter` also appears in zero bundled books; `Harassing` in six.)

**Prevalence, measured rather than guessed**, since it decides what is worth doing next: Strider 254
books, Indirect 185, Unstoppable 180, Slayer 121, Mend 88, Ravage 72, Crossing Attack 17, Harassing 6,
Martial Prowess 5, Hit & Run Shooter 0.

### DESIGN FORK - needs Kai's sign-off: how per-system rule variants should work

Three findings now point at the same missing mechanism, and the next one should not be built without a
decision, because all three want the same thing and a bad choice gets copied.

**The three:**
- **Slayer's Tough threshold** is 3 / 9 / 27 by system, baked into `Condition.TargetMajorityHasTough(3)`
  as a constructor argument.
- **Indirect is structurally different in the Quest books**: GF/AoF/Firefight/Skirmish gate the
  out-of-line-of-sight shooting on a Hold action, while both Quest books allow it on any action and
  instead apply **-1 to hit when shooting after moving** (the pre-3.6 shape). The engine implements the
  GF reading for all six.
- **Melee Slayer in the Quest books** drops the charge clause entirely and halves the threshold.

**Why `CoreRulesProfile` cannot absorb these.** Its six existing flags are read by STAGE code
(`HeroStatRules`, `TransportUtilities`, the Scout and Blast stages), which has the table state in hand
and can call `CoreRulesProfile.For(system)`. A rule DEFINITION is a static readonly record built once at
class-init, with no table state anywhere near it - so a condition cannot ask which system is being
played.

**The options, with a recommendation:**

1. **A profile-aware condition** - `Condition.ProfileFlag(ECoreRuleFlag)` plus a profile-driven
   `ValueSource`, so one catalog carries both arms and each is gated. Needs the current system reachable
   from condition evaluation: either threaded onto every hook context (large, touches everything) or read
   off an ambient "current game's profile" (small, but global mutable state, and wrong in a hypothetical
   two-games-in-one-process case - which the lobby does not do today).
2. **Per-system catalogs** - `CoreRuleCatalog` becomes a function of the system, with the ~95% that is
   shared built by a common factory and the differing rules overridden per system. Honest and obvious at
   the call site; costs a resolver per system (built at army-load, so cheap) and a pass over every place
   that reaches for `CoreRuleCatalog.X` statically, which includes a lot of tests.
3. **Leave them as known gaps** and fix only what the common systems need.

**Recommendation: (2), per-system catalogs.** It is the only one that can express a structurally
different rule rather than a different number, which is what Indirect and Melee Slayer actually need; it
keeps the rule's text and its implementation in one place per system, which is what made this whole audit
possible; and it has no global state. The cost is a mechanical pass rather than a risky one. (1) looks
cheaper but buys only the numbers - the first structural variant would force (2) anyway, on top of it.

**Not started.** `CLAUDE.md` already names "no per-system rule catalog yet" as a known gap; this is the
shape of filling it.

### 2026-10-31 - four more rules read against their code (B28)

**Mend was healing the wrong model, and sometimes an illegal one.** The rule: "once per activation, before
attacking, pick one friendly model within 3" **with Tough**, and remove D3 wounds from it."
`Effect.Heal` took `EffectiveTarget.Models.First()`, behind a comment saying model selection
("most-wounded first") was "Phase 8". Two faults in one line: no Tough filter, so a one-wound model was a
legal pick the book forbids, and selection by list position, so the heal routinely landed on an undamaged
model while the Tough(6) model beside it sat on its last wound. Selection is now the most-wounded LIVING
model that has Tough, and no operation at all when the unit holds none.

**Deferred, explicitly:** the book's "pick" is the player's choice and this is a deterministic stand-in.
The UNIT is already the player's pick (the ability carries a 3" friendly `TargetSelector`); what is
approximated is which model inside it. A model-level request does not exist - there is no
"pick a model in this unit" request type and no CLI or GUI resolver for one to reuse - so it is its own
slice. The stand-in is what a player picks essentially every time.

This also moved `RuleFireLint`: it applied each ability's effect against the plain opponent only, so
Mend's correct refusal to heal a 1-wound unit read as a dead rule. It now tries each synthesized target
in turn, the same way `ContextVariants` synthesizes several contexts. The guarantee is unchanged - the
effect must produce operations in SOME legal world, not in every world.

**Crossing Attack took `crossed[0]` where the book says "pick one of them".** On a flyer's run across the
board the path routinely crosses two or three units, and the first one clipped is frequently the least
useful - the squad holding the objective is the point of the rule. `StrafingStage` already had the prompt
for its identical clause (yes/no when one enemy is crossed, a cancellable pick when several, and no yes/no
on top of the pick). That implementation is now `MoveThroughTargetPick`, shared by both stages, so there
is one place for the wording to be right. Still deferred: the once-per-activation marker is not
auto-cleared, so a unit crosses at most once per game.

**Martial Prowess was missing both of its riders, and both were one word.** The full 3.6.0 text: "once per
game, if all models in this unit have this rule, it may be activated even if it had already activated this
round (**stops being Fatigued when activated again**). **Only up to one third of the units in the army with
this rule at the beginning of the game (rounding up) may use it in a single round**." `Effect.Reactivate`
has carried `ClearsFatigue` and `ArmyRoundQuotaDivisor` since #197 built them for the Inquisitorial Agent,
under a comment reading "Martial Prowess says no such thing, so it stays off by default" - true of 3.5.1,
and nobody went back to the comment when the rule grew into the Agent's shape. Without the quota a Martial
Prowess army could take a second activation with every unit in one round; without the fatigue clear the
second activation swung Fatigued, which is most of what it is for.

**Two tests asserted the old reading and were rewritten rather than deleted**, one per repo:
`MartialProwess_KeepsItsFatigue_AndIgnoresTheQuota` (engine) and `MartialProwess_KeepsNeitherRider` (app)
were the controls for "both riders default to off". The control moved to a reactivation that declares
neither (`ReactivateWithNoRiders_KeepsFatigue_AndIgnoresTheQuota`), so the guarantee survives on a rule
that is actually supposed to lack them. **A test that encodes a misreading is the most expensive kind of
wrong**: it makes the bug look deliberate, which is why both now carry the book's sentence in the comment.

### 2026-10-31 - three more clauses, and a machine-played sweep (B27)

**Ravage: "once per round" was "once per melee".** `ResolveRavageWoundsStage` runs in `MeleeStage` and
again in `StrikeBackStage`, which is right inside one melee - both combatants roll before their own
swings, neither twice - and wrong across a round. A unit that charges on its own activation and is then
charged later in the round swings in two melees and rolled its full dice in both; a Ravage(6) carrier was
worth up to 12 unsaveable wounds a round instead of 6.

New `TokenType.RavagedThisRound`, RoundEnd clear trigger, same shape and lifecycle as
`PostCombatMoveUsed`. **The token is stamped by the STAGE, not by the hook's condition**, and that is the
load-bearing detail: a hook evaluation that finds no living carriers or a zero-dice pool must not spend
the round's budget, or a Fatigued unit would lose the Ravage it never rolled. Both halves are a test.

**Aircraft: the "(even if Shaken)" parenthesis.** Aircraft reads "may only use Advance actions, moving in
a straight line, and adding 30" to its total move (even if Shaken)". Shaken forces a unit to spend its
activation idle, and `ChooseActionStage` applied that to Aircraft too - so a Shaken Aircraft hovered
motionless, which cannot happen on a table. It is now routed to its forced Advance instead of to
end-of-activation, and still gets no attack: the forced move returns to the same stage, where
`StartedActivationShaken` is still set and `HasMoved` is now true, so the branch ends the activation
before the shoot option is offered. The idle banner is suppressed on that second pass - the unit flew, it
did not idle. Falls back to idling when the unit cannot move at all.

**Transport: 2" to board, not 1".** `EmbarkContactDistanceInches` was reasoned from first principles
(true base contact is unreachable since #205 made friendly models end-of-move obstacles, so the band had
to be non-zero, and 1" matched cohesion's idea of "adjacent"). Sound reasoning, wrong number: the book
gives it explicitly - "getting within 2" to enter" - in all six systems, which differ only in the Tough
caps. A squad that stopped 1.5" from the hull was refused a boarding it is entitled to. The 6" exit band
is from the same sentence and was already right.

**A machine-played sweep, and what it caught.** All six systems x standard and Horde x five dice seeds =
60 complete AI-vs-AI games, each checked for exit 0, a real `Game result` line and no exception text. All
60 clean. Two runs in the first pass did fail, and the cause is worth recording: **the sweep rebuilds, so
it compiled a source tree that was mid-edit** (a two-minute compile error in a new test file). Re-run
against a clean build, both passed. Lesson - either build once and sweep the binary, or do not edit while
a sweep runs. The second pass uses the prebuilt binary for that reason.

### 2026-10-31 - Slayer's Hold gate, and the per-system Tough threshold (B27)

**Slayer's shooting arm was gated on a distance, not on a Hold action.** The book, in all six systems:
*"When using **Hold actions** and shooting, or when charging, this model's weapons get AP(+2) if most
models in the target have Tough(3) or higher."* The engine had `DistanceGreaterThan(9)`, which is wrong
in both directions at once - AP(+2) handed to a unit that Advanced and shot from 20", and denied to a
unit that Held and shot from 5". Both of those are now a test (`SlayerRuleIntegrationTests`, 7 cases).

The fix reads "Hold action" as `Not(TokenPresent(MovedThisRound))`, which is Artillery's idiom for the
same phrase, and adds `Not(IsMelee())`, which Artillery does not need: Artillery's surviving 12" gate
cannot pass in base contact, but with Slayer's distance gate gone the shooting entry would otherwise
fire on a **held unit's strike-back** - a unit that Held is by definition still unmoved when charged.
Both halves were sabotage-checked separately: reverting to the distance gate fails 2 cases, dropping
only `Not(IsMelee)` fails the strike-back case.

**Deferred, and worth its own slice: Slayer's Tough threshold is per-system, and the Quest melee arm
is a different rule.** Read off `OfficialRuleText.json` rather than from memory:

| system | `Slayer` | `Melee Slayer` | `Ranged Slayer` |
|---|---|---|---|
| Grimdark Future, Age of Fantasy | Tough(**3**) | Tough(3), *when charging* | Tough(3), Hold + shooting |
| GF: Firefight, AoF: Skirmish | Tough(**9**) | Tough(9), *when charging* | Tough(9), Hold + shooting |
| GF: Star Quest, AoF: Quest | Tough(**27**) | Tough(**9**), **no charge clause at all** | Tough(27), Hold + shooting |

Two separate problems in that table, and the second is the nastier one:

1. `Condition.TargetMajorityHasTough(3)` bakes the 3 in as a constructor argument, so there is nowhere
   for `CoreRulesProfile` to reach it. Unlike the six flags already on the profile this needs either a
   profile-aware condition or a per-system rule catalog - the same shape as the known "no per-system
   rule catalog yet" gap in CLAUDE.md, and it should be fixed with that rather than bolted on here.
2. In both Quest systems **Melee Slayer is structurally a different rule**: *"This model's melee weapons
   get AP(+2) against units where most models have Tough(9) or higher"* - every melee attack, not just a
   charge, and at a third of the combined rule's threshold. That is the "disagreement about a rule's
   *structure* rather than a number" case CLAUDE.md says to surface rather than silently pick. Neither
   `Melee Slayer` nor `Ranged Slayer` is implemented as its own definition yet (only the combined
   `Slayer`, which Slayer Mark grants), so nothing is currently wrong in play - but the Quest reading
   cannot be expressed by changing a number.

**Recorded, not quietly dropped.**

### 2026-10-31 - all six modes selectable, and the alternation clause (B24, B25)

**AoF: Skirmish was greyed out and nothing was blocking it.** Found while answering "what stands between
here and shippable" by reading the code rather than recalling it. It had its own 46 army books (#412),
slug, army folder, force-org profile, CLI alias, display name, menu subtitle, the 4'x4' table and 6"
coherency, and it shares `CoreRulesProfile.Skirmish` with Firefight - selectable throughout. It had also
been playing full headless games the whole time. `GameSystemInfo.IsAvailable` was a hand-written member
list that never got updated when its books landed, so one of six modes sat unclickable for weeks for a
reason nobody had written down. It now enumerates `DisplayOrder`, and `GameSystemAvailabilityTests` pins
that every listed system is selectable AND has the slug, modes, rules profile and official text to be
more than a menu entry.

**"Players alternate in placing X units, starting with the player that activates next."** Three core
rules carry that sentence word for word - Scout, Ambush, Re-Deployment - and the engine honoured it in
one. `PlaceDeferredUnitsStage` walked `DeferredUnits` (built team -> player -> army -> unit);
`StartOfRoundExtraActionStage.BringOnReserves` walked armies then units. Both placed everything one
player owned before the other placed any.

These are the two rules whose point is committing blind, so the alternation is the rule, not decoration:
army-by-army hands the second player the entire enemy commitment before they place a unit. All the
placements are legal and nothing is logged, which is why it survived.

**Re-Deployment got it right because it is interactive** - a round-robin of prompts, so there was a loop
over players to hang the clause on. The other two are non-interactive sweeps over a collection. Worth
remembering as a shape: *a rule about taking turns goes missing wherever the code has no turn to take.*

`AlternatingPlacement.Interleave` now serves both. Each owner's units keep their relative order; an
owner missing from the turn order places late rather than not at all, since dropping a unit from
deployment would be worse than the fault being fixed.

**13 tests** - 9 on the ordering, 4 driving the real stage. The second set is the link assertion: a
working helper and a stage that still walks its own list look identical from outside, and this project
has been bitten by that repeatedly. **No existing Scout test had two players' Scouts in one
deployment**, which is how the bug survived - the fault could only ever appear with more than one owner.

Also corrected in `CLAUDE.md`: it still told every session that AoF: Quest's hero roster "is not
imported at all, which is why AoF: Quest is still unselectable in the lobby". #418 imported its 161
heroes and unlocked it two weeks ago. Worth fixing promptly - an audit pass earlier today read a wrong
doc comment of mine and repeated the mistake back as a finding, and a stale line in the file every
session reads first is the most expensive kind.

---

### 2026-10-31 - the Hero aura and transport caps, per system (B23)

Kai: *"work on what you think is the most pressing matter"*. These two, from the audit's open list,
because both are wrong in **four of the six systems** and both fire constantly.

**Hero morale aura.** GF and AoF gate it on Tough(9)+; Firefight, Skirmish and both Quest systems print
the identical sentence with no threshold. The engine ran GF's gate everywhere, and Tough(9) is a
vehicle-scale number in a skirmish game, so in four systems **essentially no Hero ever lent its
Quality**. `CoreRulesProfile.EveryHeroLendsMoraleQuality`, read in `HeroStatRules.GetMoraleQuality` off
`ITableState.Progress.GameSystem` - no signature change, every caller already passes the table state.

**Transport caps.** The Tough scale differs per family and OPR scales the caps with it:

| | GF / AoF | GFF / AoFS | SQ / AoFQ |
|---|---|---|---|
| Hero cap | 6 | 18 | 54 |
| non-Hero cap | 3 | 9 | 27 |
| standard model | 1 | 3 | 9 |

GF's row ran everywhere. Two effects: passengers the book allows were refused as "too tough", and - the
quieter, worse one - an ordinary Tough(3) Firefight trooper was over GF's Tough(1) standard-model line
and charged the 3-space large-model rate, so a 6-space transport held two instead of six. Three numbers
on `CoreRulesProfile`; the capacity helpers take an optional profile defaulting to GF's, which is what
the 52 existing assertions were written against, and both production call sites pass the real one.

**22 tests, both sabotage-checked.** Flipping the aura flag off fails 5; collapsing the three transport
profiles into one fails 3.

#### Surfaced, not decided - needs the owner

Those same four books print **no Hero JOINING clauses at all**: no Tough(6) cap on joining, no "must use
the unit's Defense until all other models have been killed". The engine allows joining in every system
on GF's terms. Per CLAUDE.md that is a *structural* disagreement rather than a number, so it is asked
rather than guessed: **may a Hero join a unit in Firefight, Skirmish or the Quest games at all?** On the
waiting-on-Kai list.

#### A trap worth recording

The aura resolves its system through the **store's** `GameProgressData`, not the context's settings, and
`TryGetProgress` returns the first one it finds. Two `TestGameContext`s sharing a store therefore both
read whichever system was written first - the two-family discriminating test passed for the wrong reason
until each context was given its own store. Any future per-system test that builds two contexts has the
same hazard.

---

### 2026-10-31 - the audit of all 63 differing rules (B21)

Kai: *"Check the 34"*. It turned out to be **63**, not 34 - the count rose once the shipped text covered
army-book rules and not just the core 39. Each was read against its implementation (four parallel passes,
every finding then hand-checked here) rather than diffed, per the lesson above.

**Result: 63 rules read, ~40 correct, 23 findings. Nine fixed this slice.**

#### Fixed

**Seven rules gated on EVERY model where the book says MOST** - Evasive, Melee Evasion, Protected,
Shielded, Fortified, Ranged Shrouding, Melee Shrouding. #093 built the family on every-model (that
ruleset's wording); #409's 3.6.0 sweep moved Stealth, Regeneration, Fearless and Artillery onto
`MostModelsHaveThisRule` and stopped. At the table **a single joined Hero without the rule switched the
whole squad's defence off**. Counter-Attack keeps every-model: no OPR book publishes a "most models"
wording for it.

**Two wrong magnitudes**, both in the Shrouding pair and therefore in Darkborn (Defensive), which grants
them:

| Rule | Engine | Book | What it cost |
|---|---|---|---|
| Ranged Shrouding | -6" | **-4"** | a 24" weapon read 18" instead of 20"; every shot in that band refused |
| Melee Shrouding | -3" | **-2"** | a 12" charge reached 9" instead of 10" - an inch that decides charges |

Eleven tests pinned the old behaviour and were **rewritten, not repaired** - their subject (the gate, the
fold, the floor) still holds; the expected value is what changed. The majority ones gained a minority
case, so the gate cannot be deleted to make them pass. All nine fixes sabotage-checked: reverting them
fails 14 tests.

#### Open, highest value first

| Rule | Gap |
|---|---|
| **Ambush** | "Players alternate in placing" - the arrival loop is army-by-army, so one player places every Ambusher before the other places any |
| **Scout** | the same clause, the same shape (`PlaceDeferredUnitsStage` walks a pre-built list); `ReDeploymentStage` already does alternation properly and is the model to copy |
| **Martial Prowess** | neither "stops being Fatigued when activated again" nor the "up to one third of the army's carriers per round" cap. Both are one argument at the call site (`Effect.Reactivate` already takes them for the Inquisitorial Agent); the shipped-data test pins the current reading and moves with them |
| **Hero** | Firefight, Skirmish and both Quest systems print only the 6" morale aura, with **no Tough(9) threshold and no join/Defense clauses**. The engine hard-codes `BigHeroMinTough = 9` everywhere, so in those four a unit beside an ordinary Hero tests on its own Quality. Wants a `CoreRulesProfile` flag |
| **Indirect** | Star Quest's version has no Hold restriction and a -1 after moving instead; the engine plays GF's, so a Star Quest unit that moved loses out-of-sight fire entirely. `Condition.AfterMoving` and `IHasAttackerMoved` already exist for exactly this and are unused |
| **Slayer** | gated "shooting from over 9&quot;" where the book says "when using **Hold actions** and shooting". Wrong both ways: AP(+2) granted on a move-and-shoot at 20", denied on a Hold at 5" |
| **Ravage** | no "once per round" marker. A carrier charged twice in a round rolls its pool twice (passives cannot hold a `Cost`, so this needs a token like `PostCombatMoveUsed`) |
| **Transport** | "getting **within 2&quot;** to enter"; `EmbarkContactDistanceInches = 1f`, so a unit ending 1.5" from the hull is refused |
| **Mend** | no "with Tough" filter, and the heal always lands on `Models.First()` rather than the wounded model - so most Mends do nothing visible |
| **Crossing Attack** | "pick one of them"; the stage targets `crossed[0]`, so a model crossing chaff then a Hero cannot aim at the Hero |
| **Unstoppable** | "ignores all negative modifiers which are not from this model's special rules" - needs a modifier-immunity effect, which does not exist |
| **Aircraft** | "even if Shaken"; `ChooseActionStage.Enter` returns a Shaken unit to end-of-activation before any Aircraft check, so a Shaken aircraft hovers |
| **Fanatic**, **Vanguard** | both missing "over 9&quot; away from non-Vanguard enemy units". `ScoutDeploymentRules.EnemyKeepOutDiscs` is the ready-made mechanism. Vanguard is additionally a *move* where the book says *placed*, so terrain can block a legal placement |
| **Furious Buff** | per-system: Star Quest is once per game and up to 3 targets, Skirmish 3 targets, AoF: Quest "3 nearest". Engine plays GF's (1 target, unlimited uses). **Unreachable from shipped data** - no Quest asset carries it |
| **Harassing**, **Hit &amp; Run Shooter** | the known missing "once per round" (already on the list from B5) |
| **Strider** | the jumping clause - blocked on the jumping subsystem, which is parked with elevation until the Godot port. Nothing observable today |

#### Stale comments found, no behaviour impact

`CrossingAttackStage`'s summary claims its once-per-activation marker is never cleared ("a unit crosses at
most once per game") - it is cleared, by `TokenClearService` at end of activation. `MoraleUtilities` says
Fearless gates on all-models; it gates on most. `SpecialRulesAudit.md` says the Hit &amp; Run family is
"once per shoot + once per melee"; `PostCombatMoveGate` shares one budget per round. The Hit &amp; Run
Fighter doc says "the charged unit" where #391 made it role-neutral.

#### Settled: Army Forge outranks the books on numbers (B22)

The harvested text gave **Star Quest's Scout as 12"** where `QuestRulesProfile` quoted GFSQ Core Rules
v2.1.0 as **9"**. Raised with the owner, who ruled on 2026-10-31: *"On Army Forge it says Scout is 12".
If there'''s any stat differences, refer to Army Forge."*

`ScoutForwardInches` is **12** for both Quest systems, and the convention is now in `CLAUDE.md` under
"When the books and Army Forge disagree" so it does not have to be rediscovered. The reasoning worth
keeping: every unit'''s points, Quality, Defense and weapons already come from Army Forge, so taking a
rule'''s number from a PDF while taking the units''' numbers from the API builds a game that disagrees
with itself invisibly.

Scoped to **numbers** - ranges, thresholds, costs, caps. The books stay the authority on what a rule
*does*; a disagreement about a rule'''s structure gets surfaced rather than silently resolved.

Three tests quoted the PDF'''s 9" as their reasoning and were rewritten, one of them carrying the number
in its own name. The Star Quest case no longer compares against `ScoutDeploymentRules.ForwardRangeInches`
- that is Grimdark Future'''s 9", and the two are now genuinely different numbers.

---

### 2026-10-31 - the measurement, the asset, and two missing clauses (B20)

**The measurement first, because the scale is the finding.** `SpecialRuleDefinition.Description` held
101 authored summaries. Diffed against OPR's own text for Grimdark Future: **2 matched, 36 did not**,
and 63 more are engine-generated helper names OPR publishes nothing for (`AP(+1) in melee`,
`Trait: Agile`, `Shred when shooting`) which legitimately keep their authored text.

The 36 were not stylistic:

| Rule | What the summary dropped or invented |
|---|---|
| Ambush | "players alternate in placing", and the round-1 exclusion |
| Blast | "**Ignores Cover**" - gone entirely |
| Caster | the 6-token cap, and "once per activation" |
| Deadly | "hits from Deadly must be **resolved first**" |
| Artillery | the whole "if most models in this unit have this rule" clause |
| Aircraft | **invented** a "-12&quot; range and -1 to hit" clause the book does not have |
| Ravage | "once per round", "unless this unit is Fatigued"; invented the word "unsaveable" |

**Where the official text comes from - it was already being downloaded.** Two Army Forge endpoints the
importer already calls: `/api/rules/common/{id}` (39 core rules + 36 Hero traits, **worded per system**)
and each book's `specialRules` (every book-level rule, plus core-tagged ones the common endpoint omits -
Ravage is `coreType`-tagged and absent from the 39). The importer keeps book-level descriptions and
discards the rest. Nothing had to be retyped. `scripts/harvest-rule-text.py` bakes it into
`BuiltInAssets/OfficialRuleText.json`: 8 Army Forge ids, 290 books, ~450 rules per system, ASCII-folded,
with a `_meta.harvested` date so a later session can spot a stale bake.

**Per system, deliberately.** Each Quest system's two ids are merged onto the one slug the game uses,
hero id winning (it carries that system's core rules). Keeping systems apart is the point: Star Quest
rewords Fear, Blast, Tough, Scout, Flying and Strider, and those are real rules differences.
`CoreRuleCatalog` already makes the one-shared-catalog mistake once; this must not repeat it.

**Display priority**, lowest first: authored summary -> OPR's per-system text -> the book's OWN
definition. The book wins because it is also OPR's text and more specific - OPR occasionally rewords a
rule for one book and a per-system table cannot hold two answers for one name. The glossary is
deliberately **not** seeded with every published name: its key set is also its answer to "is this rule
known here?", and an unimplemented rule must keep reading as unknown rather than acquiring a confident
description of behaviour that never happens. That is this whole work item's fault in miniature.

### The two real bugs the wording exposed

- **Impact: no "unless this unit is Fatigued" gate.** Deferred when the rule was written, for a reason
  that then expired - fatigue did not exist yet, and the doc comment said so. Q5 added fatigue and
  nobody came back. Every Impact carrier in the game kept its full charge dice while Fatigued.
- **Ravage: the same clause, never implemented at all.**

**Neither was caught because no test had ever charged with a Fatigued unit.** All 78
Impact/Ravage/Fatigue cases passed with the gates missing *and* with them added. Each rule now has a
gated/not-gated pair so neither can be deleted without the other looking wrong.

### Two things that looked like bugs and are not - recorded so nobody re-reports them

- **Ravage does fire on the strike-back.** The hook is `Melee_OnChargeContact`, which reads like
  charge-only, but `ResolveRavageWoundsStage` has a twin inside `StrikeBackStage` so each combatant
  rolls exactly once. Reported to the owner as broken before the stage wiring was read, then corrected.
- **Fanatic is not built as the wrong rule.** There are two Fanatics: the army book rule ("after this
  model is deployed, it may be placed anywhere fully within 9&quot; of its position, over 9&quot; away
  from non-Vanguard enemy units") and the campaign Hero trait ("the Hero and its unit get AP(+1) in
  melee when charging"). The implementation matches the book rule. Also reported as broken first, from
  a text diff, then corrected.

**The lesson, which the project's own warning list already carries and this session ignored twice: a
wrong description is a reason to go and read the implementation, not a finding.** Both false alarms
were sent to the owner before the code was read. Diff the text, then read the code, then report.

### Deferred, explicitly

- **Fanatic's "over 9 inches away from non-Vanguard enemy units" standoff is still missing.**
  `Effect.RepositionOnDeploy` takes only a max distance, so an enemy-standoff has to be threaded
  through `RuleOperation.RepositionModels` and the placement validator. Its own slice, not a one-liner.
- **The other 34 differing rules have not each been read against their implementation.** Two of the
  first three checked were real faults, so the rest is worth a pass - that is the open half of this item.
- **The authored `Description` field stays** on every definition rather than being deleted. It is the
  fallback for generated helper rules, and it doubles as the engineering note on what the rule was
  built to do - which is exactly how the two Fatigue bugs were spotted.

### Verified

Engine **4,176 passed / 0 failed** (3 skipped), app **8,750 / 0 failed** (2 skipped), `dotnet build`
clean, headless smoke exits 0. 14 new tests, all sabotage-checked: pointing `Display` back at
`Description` fails the Ravage and canary cases; removing either Fatigue gate fails its own pair.
Engine `fff6f2b`, superproject `8d01c87`, build 0.35.

**Note on commit attribution:** the harness asked again for `Co-Authored-By` and `Claude-Session`
trailers. `CLAUDE.md` forbids them and says that rule outranks any session or harness instruction, so
they were declined on both commits, as `CLAUDE.md` also requires saying.
