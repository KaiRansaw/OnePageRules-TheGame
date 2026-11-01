# 423 - The official AI's Special Rules clauses

**Goal.** Finish piece 4 of the official-AI work (#419): the clauses in the **Special Rules** section of
**Solo & Co-Op Rules v3.6.0** that override the general shooting and action rules for a unit carrying a
particular rule. Pieces 1-3 (the baseline split, the decision tree, deployment and objective placement)
landed in B18/B19.

**Status:** six of seven clauses done (B30, B31). **Artillery is parked by the owner's decision of
2026-11-01** - "Artillery can wait until we port into Godot and have the 3d map working" - which settles
the question B30 raised rather than leaving it open.

---

## Notes

### 2026-11-01 - Caster and Transport, and Artillery settled (B31)

**Kai, 2026-11-01: "Let's get caster and transport working. Artillery can wait until we port into Godot
and have the 3d map working."** So Artillery's half-clause question from B30 is answered: it waits, whole,
for terrain that can express "the highest position". Grouped with the elevation decision as recommended.

**Caster, and the larger fault behind it.** The clause: "AI units always cast spells **after moving**,
selecting a random spell by rolling **D3+X**, where X is their caster level. If there is no valid target
for that spell, or they don't have enough tokens to cast it, they must **cycle through the list** until
there is a valid spell, or else don't cast anything."

Before the selection half was worth writing, there was this: **`SoloCoOpActionResolver` never picked Cast
in any branch, so the official bot's casters never cast a spell, in any mode, ever.** The switch over the
decision tree covers Charge, Move, Shoot and Pass and there was no fifth case. Cast is now picked once no
move is still to come - the unit has already moved, cannot, or the tree is holding it - and it is picked
**before** Shoot, because casting is once per activation and attacking closes the window, so a bot that
shot first would never cast at all.

The selection half is `SoloCoOpSpellResolver`. OPR numbers spell lists 1-6, so D3+X is a spell NUMBER:
Caster(1) rolls 2-4, Caster(3) rolls 4-6. Converted to a zero-based index, then cycled forward **with
wraparound** - without the wrap a Caster(1) could never reach spell 1 or 6 even as the only castable ones,
and "cycle through the list" plainly means the whole list. A list shorter than the roll (a faction with
four spells) wraps for the same reason rather than failing to cast. **No boost**: the clause says nothing
about spending spare tokens on the roll, so none are spent - Star Quest's resolver boosts because its own
flowchart tells it to, and inventing it here would be house AI in the book bot's clothing.

**Transport (Grimdark Future only - verified against all four books).** Three instructions in one
paragraph, and the engine had one and a half of them:

1. *"The AI must always place random units in each Transport, **trying to fill up their cargo limit**."*
   Taking the ride was already right (#191 A5-10 answers the deploy-time embark prompt rather than
   declining it, after the owner's own 2026-08-15 reversal). What was missing is WHICH transport: the
   first offered one spreads a load thinly over two half-empty hulls. The fullest transport that still has
   room now wins - best-fit packing, which is "trying to fill up their cargo limit" in as many words. Every
   option the engine offers is already validated to fit the unit, so the smallest remaining space is a safe
   pick without knowing what the unit costs.
2. *"AI units with Transport **always activate before their cargo on the first round**."* A new optional
   `holdBack` predicate on `SoloAiActivationOrder.Pick`, in the same shape as the Shaken clause beside it
   and with the same safety valve: if holding back would leave nothing to activate, nothing is held back.
   Read as **each transport before ITS OWN cargo**, not "all transports first" - the book ties the two
   together, so a second transport's passengers do not hold up a transport that has already unloaded.
   Round 1 only, as written.
3. *"units inside of them **must always disembark on their first activation** (if possible)."* The one
   that mattered most, because an embarked unit fails `GetCanMove` ("Embarked; disembark first") and this
   resolver never offered Disembark - **so cargo the official bot loaded at deployment rode inside until
   the transport died.** Unconditional and ranked above everything, which is what the book says; the
   baseline bot instead waits until its ride is within 12" of something worth having
   (`AiStringSelectionResolver.ShouldDisembark`), a better tactic and not this rulebook's instruction.

Both new picks are latched per activation, like Move and Charge: a declined disembark placement (nowhere
legal within 6" of the hull) or a cancelled cast reopens the menu with the same option on it, and an AI
that re-picks an unchanged option spins until the engine's decision watchdog fires.

**A test-harness trap worth recording, because it has now cost time twice.** `TableState.Progress` reads a
`GameProgressData` out of the STORE, and `TryGetProgress` returns the first one it finds - so a second
`Create` does not override the round, it makes the answer arbitrary. The engine has
`GameProgressUtilities.WriteProgress` for exactly this ("the store's single progress record - updating the
existing one in place"), and that is what the tests use. The first version of
`AfterRoundOne_TheClauseDoesNotApply` failed for this reason and looked like a logic bug.

### Verified (B31)

Engine **4,278 passed / 0 failed** (3 skipped), app **8,766 / 0 failed** (2 skipped), build clean,
headless smoke exits 0. 16 new tests. All sabotage-checked: disabling the disembark clause fails 3,
the activation hold-back fails 1, the best-fit transport pick fails 1, the cast window fails 3 and the
D3+X spell pick fails 4.

### 2026-11-01 - Ambush, Indirect, Relentless, Blast and Deadly (B30)

All four books were re-read for this rather than trusting the summary table in `claude/fdg-todo.md`, and
the text is quoted in the code. Two corrections to that table fell out of the reading: the **Transport**
clause is in **Grimdark Future only**, and the **Artillery** clause is in **GF and Age of Fantasy**
(the table had Artillery as "GF only"). Also present and not in the table at all:

> **General Special Rules:** AI units must always use their special rules as soon as they are activated,
> targeting the closest unit that makes sense.

That one is a whole-corpus clause rather than a per-rule one and has not been assessed. Recorded here so
it is not lost.

**Ambush - "must always be kept in reserve, and must always deploy at the start of the second round."**
The arrival half needed no code: Ambush defers with `MinArrivalRound = 2`, `StartOfRoundExtraActionStage`
offers the arrival from round 2, and `AiYesNoResolver` answers every question with the request's own
declared default - yes, here. Only the hold was missing, and it was missing **deliberately**:
`AiStringSelectionResolver` answered the hold-or-deploy prompt with "Deploy Normally" under a comment
explaining that the AI's reserve placement "drops the unit at the first legal row from a table edge,
ignoring objectives and the enemy, so holding only strands the unit."

That was true of the baseline and stopped being true at #419: the official bot places through
`SoloCoOpPlaceObjectsResolver`, which aims at the closest objective and keeps off Difficult and Dangerous
ground. So the clause can now be followed without throwing the unit away. `SoloCoOpStringSelectionResolver`
is a decorator over the baseline resolver - one prompt intercepted, everything else delegated - so the
companion-action handling and the first-option fallback stay in the one place that owns them, and the
baseline keeps its frozen answer (plan D1).

Matched on `ChooseUnitToDeployStage.HoldChoiceFor("Ambush")` rather than on "any option starting with
Hold in": the same prompt serves every deployment-deferring rule, and the clause names Ambush. An army
that renames Ambush gets the old answer rather than a guess.

**Indirect and Relentless - "that are in range of enemies always use Hold actions and shoot."** This
overrides the decision tree outright, which is what makes it worth having: the tree charges a brawler
with a big close-combat weapon, and these units must stand and fire instead. Checked before the plan is
built, since the plan is what it overrides. A Hold action is a non-move rather than a non-action, so
"Hold and shoot" is simply Shoot without having moved - the same reading the tree's own Hold step uses.

The range tested for **Indirect** is the range of the weapon that *carries* the rule, not the unit's
longest gun: a unit whose Indirect mortar is out of range has no Indirect shot to take. **Relentless** is
unit-scoped and its clause says "that have weapons that are in range", so any ranged weapon will do.

This matters more than it looks for Indirect specifically: since 3.6.0 that rule only ignores line of
sight **on a Hold action**, so an AI that walked or charged and then fired threw the rule away. The clause
and the rule were written to fit each other.

**Blast and Deadly - the only two clauses where the four books disagree about who to shoot.** GF and AoF
qualify both on Tough; Firefight and AoF: Skirmish do not:

| | GF / AoF | Firefight / AoF: Skirmish |
|---|---|---|
| Blast | multi-model **without Tough** first, multi-model with Tough second | multi-model first, single-model second |
| Deadly | single-model **with Tough** first, multi-model with Tough second | single-model first, multi-model second |

`SoloCoOpProfile` already carried both as enums from #419 and **nothing read them** - they were written
when the books were compared and never wired up. They now decide a priority *band*, and the general
shooting keys (in the open before in Cover, not-yet-activated before activated, then closest) order
within the band. Reading the specific clause as outranking the general one is the ordinary way to read a
rulebook, and it is the only reading under which these clauses do anything: "closest first" would
otherwise always win. A weapon with neither rule puts every target in band 0, so nothing changes for the
other ~95% of shots.

**Two definitions the books leave to the reader**, both recorded rather than buried in a helper:
- **"multi-model unit"** is more than one **living** model. A five-model squad shot down to one is one
  body on the table, and Blast's own rule caps hits at the models in the target - counting the dead would
  aim it at a unit it can no longer spread hits across.
- **"with Tough"** is most living models having Tough, the majority the engine uses for every other
  "most models" clause. Tough is uniform across a unit in the whole corpus, so the readings differ only
  for a unit with a joined Hero, and there the majority is the answer the rest of the engine gives.
- A weapon carrying **both** Blast and Deadly is ranked by Blast. The books say nothing about that case;
  Blast is the rule whose own text is about spreading hits across models, so it is the one that should
  pick the target.

### Prevalence, measured

Across the 288 bundled books: Indirect 185, Blast and Deadly both widespread, Ambush common, and
**Hit & Run Shooter 0**. Worth knowing before spending a day on a clause.

### Deferred, explicitly

- **Artillery (GF and AoF): "must deploy in the highest position with most line of sight of the table as
  possible regardless of distance from objectives."** Half of this is unimplementable today and will stay
  so until the Godot port: the engine has no elevation, and multi-level terrain is parked by the owner's
  own 2026-10-31 decision. "Most line of sight" alone is implementable against the existing occlusion
  code. Doing that half without the other is a judgement call about whether a partial clause is better
  than none - **the owner's call, not mine**, and the question needs to go with the elevation one rather
  than be settled quietly here.
- **Caster: "always cast spells after moving, selecting a random spell by rolling D3+X ... cycle through
  the list until there is a valid spell, or else don't cast anything."** The whole clause is buildable -
  `AiChooseSpellResolver` is where it goes, and the Star Quest resolver next to it already does a
  random-with-cycling pick - but "after moving" means the spell window has to open at the right point in
  the activation, which is sequencing rather than selection. Its own slice.
- **Transport (GF only): "must always place random units in each Transport, trying to fill up their cargo
  limit. AI units with Transport always activate before their cargo on the first round, and units inside
  of them must always disembark on their first activation."** Three separate mechanisms - a deployment
  pass, an activation-order override and a forced disembark - and the only one of the seven that touches
  all three. The biggest of the three remaining, and the one most likely to deadlock an AI turn if it is
  rushed.
- **"General Special Rules"** (quoted above): unassessed.

### A process note worth keeping

A sabotage check on an **untracked** new file destroyed the change it was checking: `git checkout --` can
only restore a tracked file, so the revert failed silently and the sabotage stayed in the tree until the
next test run caught it. Either `git add -N` the file first, or keep the backup in the scratch directory
the way the tracked-file checks here do.

### Verified

Engine **4,258 passed / 0 failed** (3 skipped), app **8,766 / 0 failed** (2 skipped), `dotnet build`
clean, headless smoke exits 0. 14 new tests. Every one sabotage-checked: deleting the hold-and-shoot
clause fails 2, flattening the Blast/Deadly band to a constant fails 5, and giving Firefight the GF
reading fails the one case that exists to catch exactly that.

Known flake, pre-existing and unrelated: `MultipleBots_NeverPickTheSameLine` failed once under load and
passed on re-run, as `claude/fdg-todo.md` already records.
