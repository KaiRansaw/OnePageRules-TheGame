# 422 - Rule clauses the audit found, fixed

**Goal.** Carry out the fixes from #420's clause-by-clause audit - the ones where the engine's behaviour
and the book's sentence disagreed. Filed as its own number because it is implementation work against a
list, where #420 is the comparison and its ledger.

**Status:** eight rules fixed (B27-B29). The audit's remaining entries are either recorded as deferred in
`WorkItems/420` or blocked on the per-system design fork written up there.

---

## Notes

### 2026-11-01 - where the detail lives

**The dated notes for every fix in this item are in [`WorkItems/420`](420-official-rule-text.md)**, under
the B27, B28 and B29 entries, because each one is the other half of an audit finding recorded there and
splitting a finding from its fix would make both harder to read. This file exists so the `#422` in the
source comments resolves to something, and so the list below is in one place.

**Fixed (B27):**
- **Slayer** - the shooting arm was gated on `DistanceGreaterThan(9)` where the book says "when using
  **Hold actions** and shooting". Wrong in both directions: AP(+2) granted on a move-and-shoot at 20",
  denied on a Hold at 5".
- **Ravage** - "once per round" was running as once per *melee*, so a unit that charged on its own
  activation and was charged later in the round rolled its full dice twice.
- **Aircraft** - "adding 30" to its total move **(even if Shaken)**". A Shaken Aircraft hovered
  motionless; it now makes its forced Advance and still gets no attack.
- **Transport** - the boarding band was 1" against the book's explicit "getting within 2" to enter".

**Fixed (B28):**
- **Mend** - "pick one friendly model within 3" **with Tough**". `Effect.Heal` took `Models.First()`
  with no Tough filter, so the heal landed on an undamaged model while the Tough(6) model beside it sat
  on its last wound.
- **Crossing Attack** - "**pick one of them**" was `crossed[0]`. Now shares Strafing's prompt through
  `MoveThroughTargetPick`.
- **Martial Prowess** - missing BOTH riders: "stops being Fatigued when activated again" and the
  one-third-of-the-army round cap. `Effect.Reactivate` had carried both since #197.

**Fixed (B29):**
- **Unstoppable** - "ignores all negative modifiers **which are not from this model's special rules**",
  the facet left unbuilt because the modifier stack kept totals and the clause asks about a modifier's
  *source*. `ApplyRollModifier` now carries the producing unit.

**Not a fault, recorded so nobody re-reports it:** Harassing / Hit & Run Shooter is NOT missing its
once-per-round limit - `PostCombatMoveGate` has enforced it since #391.
