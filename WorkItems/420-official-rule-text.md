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
