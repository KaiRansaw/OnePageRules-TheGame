# 442 — The Hold the Line family's distance band, and the Enhanced double bonus

**Goal.** Four rules read *"+1 to Morale Test rolls when shot/charged from over 9"/6" away"* and the
engine gave the bonus unconditionally. Gate them, and stop their Enhanced forms adding a second +1 on
top.

Audit finding 11, the last substantial one from the 2026-11-01 rules audit. Logged as three rules; it
turned out to be four, plus three Enhanced forms, plus a live double bonus.

---

## Notes

### 2026-11-01 — built, verified, pushed (build 0.60)

**What the books say**, identical in every system that prints them (Army Forge's verbatim text, #420,
corroborated by the core books):

> If most models in this unit have this rule, they get +1 to Morale Test rolls when shot/charged from
> over 9"/6" away.

Four rules share that sentence word for word — **Hold the Line** (139 unit entries, all six systems),
**Hive Bond** (104, the three Grimdark systems), **Battle-Hardened** (32, the four skirmish/adventure
systems) and **Valiant** (published in all six, used by no bundled unit). Three more are worded as
removing the restriction: *"they **always** get +1 to Morale Test rolls **from** Hold the Line (instead
of only when shot/charged from over 9"/6" away)."*

**No per-system split.** Worth stating plainly, because this codebase's default assumption is the
opposite: all six systems print the same sentence, with the same two numbers. A future reader should not
go looking for a system whose band is different.

### Four defects, not one

1. **The band did not exist.** All three bundled rules fired on `always`, so every carrier got +1 on
   every morale test at any range.
2. **No "most models" gate** on Hold the Line or Hive Bond — a single model handed the bonus to its
   whole unit. Only Battle-Hardened had it. (Hive Bond's shipped description even said *all* models,
   which is a third wording; the book says *most*.)
3. **The Enhanced forms stacked.** Because the base rule was unconditional, `Enhanced Hold the Line`'s
   own +1 landed on top of it: a unit with both rolled morale at **Quality-2**. 27 unit entries across
   six systems carry both. This is the one a player would actually have felt.
4. **Three rules were silently inert**: `Valiant`, `Enhanced Valiant` and `Motivator` are published by
   OPR and defined nowhere in the engine. No bundled unit references them, so nothing was broken today —
   but the Villain bug (#420) was exactly this shape, so it is now a test rather than a note.

### The same failure shape as #440 and #441, for the third time in two days

`Condition.AttackedFromOverInches` and `IHasAttackOriginDistance` **already existed and were correct** —
built in #197 for the six Boost rules that share the "shoots or charges enemies over 9" away" wording,
with a careful doc explaining why a melee needs the charge's *launch* distance rather than the live one.
`PreMoraleTestContext` simply never implemented the capability. So the gate was unexpressible, and
nothing anywhere reported that: the rules fired, they just always fired.

Correct code fed nothing. #440 was Unstable terrain behind a Dangerous-only filter; #441 was a log tap
that received nothing in AI games; this is the third. **Worth watching for as a class.** The cheap check
is to ask what feeds a condition, not just whether the condition is right.

### How it is built

- `EMoraleTrigger` splits `ShootingOrMelee` into **`Shooting`** and **`Melee`**, because which of the two
  numbers applies depends on the cause. `Condition.MoraleTriggeredByCombat` still covers both, so
  Banner (#438) is untouched — pinned by a test that Banner fires on each of them and ignores distance
  entirely.
- `PreMoraleTestContext` carries `AttackerLaunchDistanceInches` and implements
  `IHasAttackOriginDistance`. **0 means "no launch distance applies"**, which is the honest answer for a
  non-combat test, for a melee the unit was not charged into, and for a charger that cannot be
  identified. All three fall outside the band, so the bonus is withheld rather than invented.
- `Condition.MoraleAttackedFromBeyond(9, 6)` expresses the paired band as **one** condition, for the
  same reason `AttackedFromOverInches` is one: the books state it as a single phrase. It declares *both*
  capabilities it reads, so `RuleValidator` refuses it on any hook that cannot feed it — which is the
  check that would have caught the original defect, had the condition existed to be validated.
- Every call site now states its cause and its distance. The melee-results test asks the combat context
  a new question, `ChargeLaunchDistanceAgainst(unit)`, which returns 0 when that unit was the charger or
  when nothing charged — distinct from the charge-origin distance `CombatMetadata` already carried,
  which follows `SwapCombatRoles` and is always measured against the current defender.
- `RuleFireLint`'s morale probe now covers all three triggers at both ends of the band. Probing inside
  the band would have reported all four of these rules as dead.

### The Enhanced forms, and the trap that nearly shipped

The Enhanced rules now carry **no morale effect at all**. They are markers; the base rule's band is
satisfied either by the real distance *or* by the unit carrying the matching Enhanced rule. That gives
exactly +1 in every case, and it matches the books' own grammar: the bonus comes *"from Hold the Line"*.

**The trap.** In several books the Enhanced rule is never on the unit statically — it arrives from
*Enhanced Hold the Line Aura* or *Enhanced Hold the Line Buff*, both of which grant it as a
`RuleGrant` token. A granted rule fires its own hooks perfectly well but is **invisible** to
`Condition.UnitHasRule`, which reads only static `RuleDefinitions`; `RuleEvaluator` walks grants in a
separate pass that never merges into that list. Gated on `UnitHasRule`, every aura-fed unit would have
lost its bonus at close range — the exact opposite of what the Enhanced rule is for, and a new bug
introduced by the fix. Caught by reading the re-bake's own output, which named three wrapper rules the
plan had not accounted for.

`Condition.UnitHasRuleOrGrant` is the answer, routed through the existing `RuleGrantQueries` helper so
"has" cannot come to mean two different things depending on which condition asked. **This is a latent
trap for any `unitHasRule` gate paired with an aura wrapper**, not just these two — worth a sweep.

Both Enhanced rules are allowlisted in the two fire-lints as markers. An allowlist entry alone only
asserts "this does nothing", so the *reader* is pinned separately:
`Supplement_TheEnhancedUnlock_SeesAnAuraGrant` proves the base rule still names it, the grant-aware way.
The two must be read together, and the allowlist is self-cleaning — it fails if the rule ever starts
firing again.

### Leader and Motivator: measured, not guessed

A substring grep said `Leader` appeared in **151** files, which would have made it the biggest rule in
this patch. It is referenced by **zero** units: every hit is a Halfling hero whose *name* is "Leader",
plus upgrade-group labels like "Leader (Flying)". Checking that before writing any code is the only
reason this patch did not ship a change to a rule nothing uses.

Both are also real per-system splits when they do arrive — *"The Hero **and its unit** get +1 to Morale
Test rolls in melee"* in GF/AoF versus *"The Hero gets ..."* in two others — so neither can be added
without the split. Recorded in the test's failure message so the information is where it will be needed.

### Also found, logged, not built

- **The conferring Command Group list is too short.** `CoreRulesProfile.ConferringCommandGroupRules` is
  `{ Banner, Musician }`, but *Enhanced Hold the Line Aura*, *Enhanced Hive Bond Aura*, *Bravery Aura*
  and several others read *"This model's unit and up to 2 friendly units picked before the start of the
  game get X"* in Firefight and Star Quest — the same wide form #439 built the pick stage for. Those
  rules currently confer on the bearer's unit only. **This is #439's scope and belongs in its ledger.**
- **The `...Aura` / `...Buff` wrapper family generally.** The same per-system wide/narrow split, and the
  Star Quest variants add *"nearest"* ("up to 2 **nearest** friendly units"), which the pick stage does
  not implement at all.
- **`Bravery`, `Headstrong` and `Motivated`** were checked and are genuinely unconditional. No change.

### Verification

- Full `dotnet build` clean; engine **4,607 passed** (was 4,564 — 43 new), app **8,806 passed**
  (was 8,800 — 6 new); headless smoke exits 0, and a played all-AI Firefight game reaches a result.
- **9 sabotage checks, 9 caught**:
  1. Inclusive band (`>=`) → `TheBand_UsesNineForShootingAndSixForCharging(Shooting, 9.0, False)`
  2. The two numbers swapped → `ChargedFromBeyondSix_EasesTheThreshold` (+4 others)
  3. `OtherSource` falling into the band → `ASpellDrivenTest_GetsNothing`
  4. Banner's gate narrowed to melee → `TheCondition_IsTrueOnlyForCombatMorale(Shooting, True)` (+3)
  5. `UnitHasRuleOrGrant` blind to grants → `TheEnhancedRule_CountsWhenItArrivesFromAnAura`
  6. Supplement reverted to `always` → `Supplement_EveryFamilyMember_IsGatedOnMostModelsAndTheBand`
  7. Enhanced given its +1 back → `Supplement_TheEnhancedRules_CarryNoBonusOfTheirOwn`
  8. The unlock reverted to `unitHasRule` → `Supplement_TheEnhancedUnlock_SeesAnAuraGrant`
  9. One book left behind by the re-bake → `EveryShippedFamilyRule_CarriesTheBand` (names the book)

## Outcome

Done. Patch note B45, build 0.60. Engine `bc972c6`, superproject `67d8f78`.
