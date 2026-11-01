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
