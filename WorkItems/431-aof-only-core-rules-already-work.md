# 431 — The five AoF-only core rules already work; pin it

**Goal.** Implement Banner, Musician, Sergeant, Brace(X) and Parry(X) — the five core rules Age of
Fantasy and AoF: Skirmish have and Grimdark Future does not.

**Outcome:** nothing to implement. They already work. What was missing was **evidence**, which is now 11
tests. Closed 2026-11-01.

---

## 2026-11-01

### The false alarm, and how I walked into it

`claude/fdg-todo.md` listed these five under the per-system core-rules gap, as rules "AoF and Skirmish
add that GF does not have at all". I checked `CoreRuleCatalog`, found all five absent, counted their
appearances in the bundled books (68-81 files each), and reported to Kai that every Age of Fantasy and
Skirmish game had been played with five real rules doing nothing. He picked it as the next job.

**It was wrong.** Each of the five is fully defined in the book data - real hooks, conditions, effects -
and the books' definitions reach play:

- `ListCompiler.Compile` copies `book.RuleDefinitions` onto the compiled army;
- `GameBootstrap.BuildRuleResolver` starts from `CoreRuleCatalog.CreateResolver()` and then calls
  `ArmyListRuleResolution.RegisterEmbeddedDefinitions` for **every** slot's army.

That is the #059 embedded-rule design, the same path every faction-specific rule in the game rides. The
five are absent from the core catalog because the core catalog is Grimdark Future's set and GF does not
have these rules. Absent from it is correct.

**What I did wrong:** I checked the built-in catalog, found a hole, and stopped. I did not read the
resolution path before reporting. This is the second stale-to-do entry to send me somewhere in one day -
the first was the "Harassing and Guerrilla are missing their once-per-round limit" line, fixed since #391
and re-reported twice before that. The lesson is already written in the archive and I did not apply it:
**read the code before trusting the list.**

### What was genuinely missing

Nothing proved any of this, which is exactly why the entry could look like a hole indefinitely and why
nobody could settle it in a minute. So the slice became the test that was missing.

`FdgRaylib.Tests/AoFOnlyCoreRulesTests` — 11 cases:

1. **The five are not in the built-in catalog, and that is correct.** Pinned so the next reader sees at
   once that it is deliberate.
2. **Every book that uses the rule also defines it** (5 cases, over every bundled AoF and Skirmish book,
   checking unit rules, weapon rules and upgrade options). A book that used one without defining it
   would drop the rule at launch, and that unit really would not have it — so this is the assertion that
   would catch the gap if it ever appeared.
3. **A real army built from a bundled book resolves the rule at launch** (5 cases). Compiles an army
   from a real book as the Army Forge does, builds the resolver the game builds (core catalog, then that
   army's embedded definitions), resolves the rule by name, and asserts it comes back with actual
   behaviour rather than an inert marker. The rule's own definition decides scope and whether it takes a
   number, so a book refresh that changed `Parry(X)` to a plain `Parry` could not fail this for the wrong
   reason.

Sabotaged by making `ListCompiler` stop copying the book's definitions: all five of case 3 fail. The
chain is genuinely under test, not merely agreeing with the conclusion.

### Note on the first attempt at case 3

The first version looked for a unit carrying the rule on its base profile and skipped all five - these
rules arrive through **upgrade options**, not base unit rules. A skipped test reporting green is worth
less than no test, and it only showed up because the run printed 6 cases where 11 were written. Worth
remembering: check the case COUNT, not just the colour.

## Outcome

Closed with no production change. The to-do entry was rewritten so it cannot be read as a gap a third
time.
