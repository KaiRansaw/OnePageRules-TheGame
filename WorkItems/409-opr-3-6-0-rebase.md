# 409 — Rebase the whole game onto OPR v3.6.0

**Status:** sections A-F done on branch `rules-360` (engine + app). Engine 3,658 tests green, UI 2,997
green, headless smoke exits 0. Section G (the Campaign books and Star Quest 2.1.0) not started.
**Decision:** Kai, 2026-10-29 — "move to 3.6.0". Every game system goes to the current official
ruleset rather than staying on 3.5.1 or making it a per-match lobby setting.

**Supersedes the "what to do" half of [#408](408-aof-3-6-and-skirmish.md).** #408 was raised when
only the AoF/AoFS 3.6 books were known and GF was believed to still be on 3.5.1. Kai has since added
the GF, GFF, AoF and AoFS **3.6.0** core/horde/campaign books, plus GFSQ 2.1.0 — so there is no
"one system moved, the others didn't" problem to solve. #408's *findings* about AoF: Skirmish being
its own game (standard wounds and morale at skirmish scale, no wound markers) still stand and are
still that item's business; its instruction to keep the version gate per-system is dropped in favour
of the engine simply being 3.6.

Plain-language delta written up for Kai: project doc `claude/rules-3.6.0-delta.md`.

---

## Scope, in build order

### A. Core special rules that changed behaviour
All live in `Rules/Dispatch/CoreRuleCatalog.cs` with 3.5.1 wording today.

- [x] **Artillery** — four changes in one rule. Was: Hold-only; +1 to hit over 9"; enemies shooting
      from over 9" get -2. Now: **not** Hold-only (that restriction moved to Cumbersome); +1 to hit
      only **when the action is Hold** and the target is over **12"**; the incoming penalty is **-1**,
      at over **12"**, requires **most models** to have the rule, and only applies **if the unit has
      not moved this round**.
- [x] **Indirect** — the out-of-LoS targeting now only works **when using a Hold action**, and the
      `-1 to hit after moving` is **deleted**.
- [x] **Counter** — the `-1 total Impact rolls per model with Counter` clause is **deleted**.
      Striking first is now **optional** ("may") and is **switched off while Fatigued**.
- [x] **Scout** — deploy radius 12" -> **9"**, and new: must be **over 9" from non-Scout enemies**.
- [x] **Aircraft** — attackers now also take **-1 to hit** (on top of -12" range), and Aircraft
      **can't be moved into contact with**, not just "can't be charged".
- [x] **Fearless / Regeneration / Stealth** — gate moves from `AllModelsHaveThisRule` to
      `MostModelsHaveThisRule` (the condition already exists).
- [x] **Caster(X)** — casting is **once per activation**; the 18" token-feeding from nearby models
      **no longer requires line of sight**.
- [x] **Hero** — new morale aura: units **within 6" of a friendly Hero with Tough(9)+** may use that
      Hero's Quality for Morale Tests while the Hero isn't Shaken. Not in the engine at all.
- [x] **Flying** — explicitly ignores **Difficult and Dangerous** terrain.
- [x] **Takedown** — scoped to *this weapon's* attacks.
- [x] **Impact(X)** — resolves **before either unit attacks**; confirm engine ordering.
- [x] **Fear(X)** — reworded to "enemies count as having taken +X wounds"; same outcome.

### B. Special rules new in 3.6.0
- [x] **Cumbersome** — may only use Hold or Advance, and can't shoot when moving.
- [x] **Spell Amplifier** — +1 to casting rolls.
- [x] **Assault** — counts as both a ranged and a melee weapon; promoted to the core book (engine has
      it only in AoF supplement data).
- [x] **Transport(X)** — promoted to core with explicit capacity maths (Heroes up to Tough(6);
      non-Heroes up to Tough(3) occupy **3 spaces each**). Check against the engine's transport code.

### C. Core rules outside the rule catalogue
- [x] **Force Org** gains **max. 1 model or Tough per 20pts** (GF). GFF has its own set entirely:
      1 Hero per 400pts, 1+X copies per 400pts, 35% cap, 1 unit per 80pts, **3 total Tough per 40pts**.
- [x] **Difficult terrain** is a hard **6" cap** on the whole move, not a halving.
- [x] **Dangerous terrain** rolls **as many dice as the wounds needed to fully destroy the unit**.
- [x] **Cover** — most models fully inside Cover **or behind Sight Blockers**.
- [x] **Morale** wording: "half or less of its **total** size" (was "starting size"). Confirm the
      engine isn't measuring against something that drifts — matters once Horde reinforcement lands.

### D. Horde Mode 3.6.0 — a restructure, not a tweak
The mode stops counting **waves** and counts **rounds**.

- [x] Waves arrive at the **start of rounds 4, 7 and 10**; priority objective is set at the start of
      rounds **1, 4, 7 and 10**; point rewards at the **end of rounds 3, 6, 9 and 12**; the mission
      ends after **round 12**.
- [x] High score is points x **last round finished** (was last wave).
- [x] **Deploy Wave destroys Shaken enemies first**, then redeploys all destroyed enemies from the
      corners. Leaving an enemy Shaken now kills it at the wave break — a real tactical change.
- [x] **Reinforcing Units — new action.** A unit at **half or less** of its size/Tough, **within 9"
      of a seized objective**, may be reinforced for **half its total point value (round up)**,
      restoring **up to half** its models or wounds. The Horde shop has Recruit and Buy Upgrades only.
- [x] Point rewards are **40pts** per seized objective, **+20pts** for the priority objective (GF).
      AoFS/GFF use 30/15 and **6"** corner deployment instead of 9".
- [x] Traits: **Elite** drops to **once per game** (was once per round — this closes the open to-do
      "full Elite trait (re-roll one die of any kind once per round)"); **Resilient** is explicitly
      +1 to **Defense** rolls; **Specialist** confirms the melee/shooting split is correct.

### E. Firefight 3.6.0 numbers
- [x] 4'x4', 20+ terrain, 400pts start / 800pts full match.
- [x] **Coherency 1" / 6"**, not GF's 1" / 9". The engine keeps a uniform 1"/9"
      (`GameWideConstants`, set per game by `ConfigureTable`). 3.6.0 confirms 6" is right.
- [x] Wounds are per-model: a model dies once it has taken Tough wounds, no GF-style spillover.

### F. The version gate
`OprListImporter.SupportedVersionPrefix` is a single `"3.5"` constant and refuses anything else.

**Approach:** the engine's *target* becomes **3.6**, and the gate accepts a **set** of prefixes —
3.6 (primary) and 3.5 (legacy) — so Kai's existing `.fdgarmy` files and the bundled
`FdgRaylib/Assets/Books/` snapshots keep working. An import of a non-primary version warns, in plain
words, that the list was priced under an older book and is being played by 3.6 rules.

Do **not** silently accept everything: a book outside both prefixes still refuses.

### G. Books read but not yet scoped
- GF / GFF / AoF / AoFS **Campaign Rules v3.6.0** — four brand-new books. Campaign mode is the
  stated end goal; these need a proper read and their own work item before anything is built.
- **GFSQ Core 2.1.0** and **GFSQ Campaign Builder 2.1.0** — Star Quest campaign is already built
  from an earlier source; needs a read before touching.

---

## Outcome so far (2026-10-29)

### Already correct before the rebase - verified, not changed
Four of the items above turned out to be the engine already doing what 3.6 now says. Recorded because
"no diff" is a result, and the next reader should not re-audit them:

- **Horde's round structure.** The engine already counted rounds and derived waves from them: waves at
  rounds 4/7/10, priority at 1/4/7/10, rewards at 3/6/9/12, twelve rounds. 3.6 only changed what the
  SCORE multiplies by, not the shape of the mode.
- **Deploy Wave already swept Shaken enemies** back in with the destroyed ones.
- **Difficult terrain** was already a flat 6" cap (`DIFFICULT_TERRAIN_MOVE_CAP_INCHES`), and **Dangerous
  terrain** already rolled one die per wound needed to destroy each crossing model.
- **Cover** already required MOST models and already counted sight blockers, and **Firefight coherency**
  was already 1"/6" per system (`SKIRMISH_COHERENCY_INCHES`).
- **Aircraft** already had both the -1 to hit and the can't-be-moved-into-contact clause 3.6 spells out.

### Deferred deliberately (not cut silently)
- **Counter's "may" strike first** is implemented as automatic. Declining can only ever cost the
  defender models, so the prompt would add a decision point to every melee for an option nobody takes.
- **The big-Hero morale aura's "may"** is likewise resolved as "take the better Quality", for the same
  reason - a lower Quality is strictly better.
- **Elite's "re-roll one roll of ANY kind"** is still wired only into the morale test. 3.6 did not change
  that wording; it changed once-per-round to once-per-game, which IS done. Widening it needs a general
  per-unit re-roll offer the engine does not have.
- **Dangerous terrain's unit-level wording.** 3.6 says a UNIT rolls as many dice as it would take to
  destroy "them", where 3.5.1 was per-model. The engine rolls per crossing model, which is the standard
  reading and the gentler one. Flagged rather than changed: the harsher reading would have a single
  model clipping a crater roll the whole unit's wounds.
- **Quick Readjustment** (army-supplement data) was REMOVED rather than carried forward: its only job was
  to cancel Indirect's -1-after-moving, which 3.6 deleted, so left in place it would have paid out a
  free +1 to hit. On the list for the 3.6 book re-import.

### Needs OPR data the session could not reach
The bundled army books in `FdgRaylib/Assets/Books/` are 3.5 snapshots and are unchanged. Re-importing
them needs Army Forge, which is still refused at the claude.ai network gateway (403 on CONNECT to
`army-forge.onepagerules.com`). Until then the version gate accepts 3.5 books as legacy (section F), so
nothing breaks - the points are a patch behind, the rules are current.

---

## Notes

- 2026-10-29: sections A-F implemented. New engine files: `ScoutDeploymentRules`, `CumbersomeRules`,
  `AssaultRules`, `SpellCastAttemptContext`, `HordeReinforce`, `ForceOrgProfile`. New tests:
  `Opr360RuleChangeTests`, `HordeReinforceTests`, plus rewrites in the Artillery/Indirect/Counter/Scout/
  all-models suites and new Force Org cases. `EHookID.Casting_OnSpellCastAttempt` was declared but dead
  since #033; Spell Amplifier is the rule that finally wired it.
- 2026-10-29: opened. Kai supplied every 3.6.0 book plus GFSQ 2.1.0 as project files; the
  claude.ai network allow-list for `onepagerules.com` is still refused at the gateway (403 on
  CONNECT), so the PDFs are the only route in.
