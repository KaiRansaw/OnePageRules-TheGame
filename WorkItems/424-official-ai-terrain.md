# 424 - The official AI's Terrain clause

**Goal.** Piece 5 of the official-AI work (#419): the **Terrain** paragraph of **Solo & Co-Op Rules
v3.6.0**, which is its own section rather than one of the per-rule clauses #423 covered.

> **Terrain:** AI units must always try to **end their move into or behind Cover terrain** if possible,
> **moving around Difficult and Dangerous terrain**. AI units only move into Difficult or Dangerous
> terrain if there is an **objective inside of it**, if they are in **charge range of a unit inside of
> it**, or if they have a **special rule that ignores its effects**.

Word for word identical in all four books.

**Status:** done (B33).

---

## Notes

### 2026-11-01 - what was there, what was not, and two findings (B33)

**Measured before writing anything**, because `claude/fdg-todo.md` had this as two rows, one "needs
checking" and one "present":

- **"Moving around Difficult"**: genuinely present. `MovementPlanner` routes around Impassible always,
  and since #281 around Difficult too when a comparably-priced clear lane exists, with Strider units
  skipping the trigger. Nothing to do.
- **"and Dangerous"**: **absent entirely.** The word *Dangerous* appears nowhere in `MovementPlanner` or
  the terrain grid. The official bot walked its units through ground that makes them take a Dangerous
  Terrain Test, every time, in every mode.
- **"end their move into or behind Cover"**: absent. Nothing anywhere tried to choose WHERE a move stops.
- **"Always strike back when charged"** (the other row marked "needs checking", from the Melee clause):
  **already correct, no change.** `OfferStrikeBackStage` raises a `YesNoRequest` with
  `defaultAnswer: true`, and `AiYesNoResolver` answers every question with the request's own default. So
  the AI always strikes back. Recorded here so the row can be ticked rather than re-checked - that is the
  fourth "needs checking" row this AI work has resolved by reading rather than by changing code.

**Where the clause lives, and why not in the router.** `SoloCoOpTerrainPolicy` adjusts the **aim point**;
`MovementPlanner` is untouched. The planner is shared with the Tactician and with the frozen benchmark
baseline that plan D1 forbids changing, whereas choosing where to stop is the solo resolver's own
business. The adjusted point is then walked to by the **same validated mover as before**, so an
unreachable piece of cover costs nothing but the attempt - and a policy that cannot produce an illegal
move is worth more here than a cleverer router.

**How it works.** The plain destination is computed first (where the unit would stop with no terrain
preference), then rings of candidates are sampled around it at 1, 2, 3, 4 and 6 inches, eight directions
each. A candidate must be inside the plan's own slack and not inside Impassible terrain. Each is graded
on the clause's two instructions, and **only a terrain improvement moves the end point** - distance to
the goal is the tie-break among candidates that improve, never a reason on its own. That last part was a
bug in the first draft: with distance as a third ranking key the policy quietly pulled every unit a few
inches closer to its goal on an empty table, which is the mover's arithmetic, not this clause's.

**Three judgement calls, all recorded in `claude/house-rules.md`:**

1. **3" of slack** (`GoalSlackInches`). The books say only "if possible", and the five-step tree decides
   what is possible - so a candidate is rejected outright if it is more than 3" further from the plan's
   goal than stopping normally. One objective radius: far enough to reach the wood beside the marker, not
   far enough to abandon the marker.
2. **Bad ground outranks cover** where the two disagree. Cover is worth +1 to the enemy's save roll;
   Difficult halves the move that reached it and Dangerous rolls to wound the unit. The book gives no
   priority. Pinned by `CleanGroundBeatsDangerousCover`, so it cannot drift silently.
3. **"Behind cover" is measured against the nearest enemy only.** A Cover or Blocking piece between the
   end point and the closest enemy model counts. A point hidden from every enemy at once is a luxury the
   clause does not ask for, and "the nearest" is the one most likely to shoot.

Model **centres** decide whether a point is inside a piece, not inflated bases: a base clipping the edge
of a wood is a judgement a human makes at the table, and inflating by the radius would make a unit refuse
to stand anywhere near cover at all.

### A test that passed with the feature disconnected

The first version of the resolver-level test put the wood **on** the straight lane to the objective, so
the unit ended up in it whether the policy was wired in or not - and the sabotage check proved it: the
test passed with `PreferredEndPoint` replaced by the plain destination. Moving the wood **off** the lane
made it a real test, which then failed under the same sabotage.

Worth keeping because the eleven policy-level tests all passed under that sabotage too: they call the
policy directly, so none of them could see the wiring. **A unit test of a helper is not a test that the
helper is used.**

### Verified

Engine **4,300 passed / 0 failed** (3 skipped), app **8,766 / 0 failed** (2 skipped), build clean,
headless smoke exits 0, 36 more AI games across all six modes clean. 12 new tests, all
sabotage-checked: dropping the Dangerous check fails 3, disconnecting the policy fails the resolver-level
case.
