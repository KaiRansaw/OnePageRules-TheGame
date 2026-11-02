# 438 - Banner's combat-only morale gate, Lustbound's real numbers, and a stall detector for the networked tests

**Status:** done (B41, 2026-11-01).
**Came from:** `claude/rules-audit.md` finding 3, approved by Kai on 2026-11-01 ("3 4 first then the
others"). Findings 11 and 12 were discovered while doing it and are recorded, not built.

---

## Goal

Make a Banner's +1 apply only to morale tests triggered by shooting or melee, as every current book and
Army Forge's own text require, in all six systems.

---

## 2026-11-01 - notes

### What shipped

**1. The trigger reason reaches the rules (engine).**
`EMoraleTrigger` { `ShootingOrMelee`, `OtherSource` } + `IHasMoraleTrigger` capability.
`PreMoraleTestContext(IUnit, EMoraleTrigger)` - its own doc comment already said "grow it (e.g. the
test's source) when a rule reads more", which is exactly what happened.
`Condition.MoraleTriggeredByCombat` (`moraleTriggeredByCombat` in the supplement vocabulary).
`MoraleUtilities.TakeMoraleTest` takes the trigger as a **required** parameter, deliberately with no
default: the four defects in #437 all hid behind silent fall-through to a sensible-looking default, and
the audit's lesson was to make the compiler ask. Cost: 5 production call sites + ~19 test call sites,
all mechanical, and the compiler found every one.

Call sites, labelled from the books:

| Site | Trigger | Why |
|---|---|---|
| `RollForMoraleStage` | `ShootingOrMelee` | the melee loser's test |
| `MoraleUtilities.ResolveWoundDrivenMorale` | `ShootingOrMelee` | wounds come from shooting or melee |
| `CastSpellStage` (`MoraleTestThen`) | `OtherSource` | a spell forced it |
| `GameOperationServices.MoraleTestThen` | `OtherSource` | a unit rule forced it (Mind Control, Fatigue Debuff) |
| `QuestActions.Morale` | `OtherSource` | every caller is a non-combat Quest event |

`QuestActions.Morale` hard-codes `OtherSource` rather than taking a parameter, because all six of its
callers are non-combat events (advanced-skill nerve checks, Dread, Enchantment, treasure). Its doc
comment says plainly that a Quest caller which IS combat must not use that helper. Judgement call: a
parameter on 6 call sites all passing the same value is noise.

`RuleFireLint` probes **both** triggers on that hook - probing only combat would hide a rule gated the
other way, and probing only the other would report Banner itself as a rule that never fires.

**2. The data (app).** `GdfRuleSupplement.json`'s Banner: `always` -> `moraleTriggeredByCombat`, two
lines. Then **114 books rebaked** with `--apply-rules`.

**3. Lustbound corrected** - see finding 12. `Lustbound` Rush/Charge 3 -> 2, `Enhanced Lustbound`
Rush/Charge 1 -> 2 so the pair still totals Army Forge's stated +4". 10 of 14 books fixed; the 4 whose
per-book override redefines Lustbound deliberately left for Kai.

**4. The networked playthrough tests no longer fail on a busy box.** Replaced the flat
`Task.WhenAny(ended, Task.Delay(GameTimeout))` with a progress watch over each seat's `ITableState`
(round, activating unit, units left to activate, objective scores, model count, positions, wounds).
Fails only after 45s with **nothing on the table changing**.

### Three mistakes of mine, recorded because each cost a cycle

1. **"Finding 3 is blocked - the 114 books can't be rebaked here."** False. `--apply-rules` re-embeds
   definitions from the local supplements, offline and idempotently (verified: md5 unchanged when
   re-applied with no supplement change). I had a true fact (the gateway blocks Army Forge) doing no
   work, and never looked for a local path. **A conclusion that something is blocked needs the same
   evidence standard as a finding.**
2. **The first stall detector was blind.** I tapped `ILogMessageUI`, which the test had been passing
   `null` for. It receives **nothing**: log lines are routed per player slot and an AI-driven seat
   discards them - the same trap that bit `CampaignEventInAPlayedGameTests` earlier the same day.
   Caught by the `Finish` guard I had added for exactly this ("a watch that never saw a change makes the
   detector vacuous in the dangerous direction"). That guard is now permanent and is the reason this is
   not a patch note claiming a fix that wasn't. `ITableState.Progress`'s own summary says to read it
   "instead of parsing round info out of the log or banner stream" - which was the right answer all along.
3. **The first rebake script did not mirror the production bake chain.** It resolved per-book overrides
   by current name only, so the three books OPR renamed to "Daemons of Lust" in 3.6.0 silently lost
   `Rift Daemons of Lust.json`. Caught by diffing for changes I could not explain; reverted and redone
   with `OldNamesFor`. **A rebake script that does not reproduce `Program.cs`'s supplement order and
   rename fallback is not a rebake.**

Also: two sabotage checks initially "passed" because `dotnet test --no-build` reads the assets copied
into `bin/` at build time. An asset sabotage needs a rebuild before the run, or it tests nothing.

### What the full app suite caught, and why it matters more than the bug

Two existing `BoostRuleCompositionTests` (#197) cases failed on the Lustbound change. They pinned
**+1/+3** rising to **+2/+6** via a `Lustbound Boost` rule - **pre-3.6.0 numbers from a pre-3.6.0 rule.**
Army Forge has no `Lustbound Boost` today (it is `Enhanced Lustbound`), and no book references Boost or
its Aura.

So the +3 was not somebody's typo. It was right, once, and the 3.6.0 refresh added the new definitions
beside the old ones without retiring them - leaving a test that passed against stale data while the
current book said something else. **Nothing in either suite compared any rule's numbers to the book**,
so there was no mechanism by which this could be noticed.

Both cases now drive the live `Lustbound` + `Enhanced Lustbound` pair and assert +2/+4, which
corroborates the fix a second way - through the real evaluator and movement sinks rather than through the
JSON - and keeps #197's composition guard standing on a rule that books can actually reach. The old
numbers are named in the comment as the record of the bump, which the version policy explicitly wants.

### Verification

- Engine **4,482 passed / 0 failed** (3 skipped), up from 4,474 - and the 3-player networked test passed
  **inside the full suite under parallel load**, which is the actual proof the stall detector fixed the
  thing it was built for.
- App suite green; build clean.
- **Sabotage-checked 5/5**, each reverted after:
  1. Banner back to `always` in the supplement -> `Supplement_Banner_IsGatedOnCombatTriggeredMorale` fails.
  2. `MoraleTriggeredByCombat` hard-coded true -> 2 engine tests fail.
  3. `ResolveWoundDrivenMorale` mislabelled `OtherSource` -> `TheWoundDrivenPath_CountsAsCombat` fails.
  4. Lustbound Rush back to 3 -> both Lustbound supplement tests fail.
  5. One real Banner carrier ungated -> `EveryShippedBanner_CarriesTheCombatGate` fails **by book name**.

### Tests added (17)

`FutureOfDarkGrimness/Tests/BannerMoraleTriggerTests.cs` (7) - the condition, the trigger surviving the
real morale engine, both call-site directions, and `Motivated` staying unconditional.
`FdgRaylib.Tests/BannerShippedDataTests.cs` (10) - the supplement's Banner, **every** shipped Banner
across all 288 books, and Lustbound base/total as a sum plus six embedded book copies.

Both halves are needed and the fixture comments say why: the bug was in data, so the behaviour tests
would have passed against the broken books all along; and a data test alone would not notice a call site
feeding the right rule the wrong trigger.

---

## Outcome

Done. A Banner is worth nothing against a spell, a unit rule or a campaign random event (B39's events
were about to make that much less of an edge case than I claimed when I deferred it), and worth its +1
when a unit loses a melee or is shot to half strength. Lustbound is a movement rule in 10 of 14 books.
The networked tests measure progress instead of wall clock.

**Left open, deliberately and recorded:** finding 11 (Hold the Line / Hive Bond / Battle-Hardened need
the attacker's distance, bundled with the existing Motivator/Leader to-do), finding 12's four override
books, and `Lustbound Boost` as dead data.
