# 416 - Horde Mode read against all four of its books

**Goal:** Grimdark Future's Horde Mode had never been checked against its rulebook the way Star Quest
was in #414. Audit it, and in particular answer the question #414 kept raising: **do the four systems'
Horde books differ, and is the engine playing one version everywhere?**

**Raised by:** this session, 2026-10-31, after #414 found four separate Star Quest rules that were fully
implemented, fully tested and completely unable to fire (Villain matched a rule name no book uses; Scout
deferred to a pass Star Quest never runs; the once-per-army limit matched a name OPR renamed in 3.6;
Blast's spread clause was simply absent). Horde was the one mode nobody had looked at that way.

---

## Notes

### 2026-10-31 - the four books, and the one rule the engine got wrong

Read word for word: `GF - Horde Mode v3.6.0.pdf`, `6ae38f581001bd87ff130047_AoF - Horde Mode v3.6.0.pdf`,
`GFF - Horde Mode v3.6.0.pdf`, `6ae390bee93e0b2aef494b45_AoFS - Horde Mode v3.6.0.pdf`.

**Almost all of Horde Mode is identical across the four.** 12 rounds, three rounds per wave, five
objectives (centre plus each quarter centre), priority set at the start of rounds 1/4/7/10, waves at
4/7/10, rewards at the end of 3/6/9/12, three damage tokens to destroy a marker, the same six-trait
table rolled on a D6, 5 XP per level, a 30 XP cap, the same three mission-end conditions, and the same
high-score formula. The engine's single ruleset is right about every one of those.

What actually differs:

| | GF | AoF | GFF | AoFS |
|---|---|---|---|---|
| Army points per side | 2000 | 1500 | 800 | 600 |
| Table | 6'x4', 15+ terrain | 6'x4', 15+ | 4'x4', 20+ | 4'x4', 20+ |
| Deployment radius | 9" | 9" | 6" | 6" |
| Reward / priority bonus | 40 / +20 | 40 / +20 | 30 / +15 | 30 / +15 |
| Upgrade + recruit radius | 9" | 9" | 6" | 6" |
| **XP tiers** | **3 / 5 / 8** | **3 / 5 / 8** | **3 / 5** | **3 / 5** |

`HordeRules.Configure` already handled the radii and the rewards. **The XP table it did not.** GF and AoF
both list three tiers - *"+3 XP: Killed a unit / +5 XP: Killed a Hero unit / +8 XP: Killed a Hero and its
unit"* - and **GFF and AoFS list the first two and stop.** There is no "Hero and its unit" line in either
skirmish book. The engine awarded 8 in all four games, so a Firefight or Skirmish unit that wiped out a
joined hero's squad earned a third of a level more than the book gives it. Fixed:
`HordeRules.AwardsHeroAndUnitXp`, read by `HordeExperience.XpForKill`, pinned per system.

### 2026-10-31 - the audit's headline finding was NOT a bug, and that is worth more than a fix

The audit reported, with a clean repro, that `HordeRoundHooks.AwardWavePoints` re-derives "seized" as a
momentary test - a defending model within 3" and no enemy within 3", *at the instant of the reward* -
instead of reading `ObjectiveData.OwnerID`, which `ReconcileObjectivesStage` sets and logs **three lines
earlier in the same stage entry**. Two definitions of "seized", side by side, one of them apparently
ignored. It read exactly like the dead-code bugs #414 kept turning up.

**The book says the momentary test is correct:**

> **Point Rewards:** At the end of rounds 3, 6, 9, and 12 **if any defending unit is within 3" of an
> objective marker while enemies aren't**, then that objective counts as seized.
> - Horde Mode v3.6.0, identical wording in all four books

"if any defending unit is within 3"" is present tense and is evaluated when the reward pays out. The core
rules' objective control is the sticky one - *"Markers stay under the player's control even after
leaving"* - and that is what the standard mission scores on and what `OwnerID` records. Two rules for two
jobs, and they are meant to disagree. Reading `OwnerID` here would pay the defenders for ground they no
longer hold.

So the fix was a test, not a change: `HordePerSystemRulesTests` now pins the momentary reading with the
book quote beside it, including the discriminating case - a defender that seized a marker earlier and
walked away scores **nothing**, while `OwnerID` still records it. The next person to read that code will
find the answer instead of the question. `AwardWavePoints` had **no tests at all** before this, which is
exactly what let the ambiguity stand.

Three things that scene needed, each of which silently zeroes the reward if missed, and all three now
written down in the fixture: the defending side is "whoever is in **slot 0**"
(`HordeRules.DefendingTeamNumber` reads player slots, not teams), a slot counts only when **filled**
(`IsFilled` is "has a controller"), and only the **numbered** `ObjectiveData` ctor creates the priority
binding - `SetHordePriority` is a documented no-op without it.

### Still open from the audit

- **Ambush cannot fire in Horde.** The hold-in-reserve offer lives in `ChooseUnitToDeployStage`, part of
  the standard `DeploymentStage`, which `HordeStateMachineBuilder` does not include;
  `HordeDeploymentStage` places every unit directly. A defending unit with Ambush just stands at the
  centre. Engine, medium.
- **Scout cannot fire in Horde** either, for the same reason, and the #414 widening is Star-Quest-only by
  design. GF's Scout is "within 9" of their deployment zone" and in Horde the defenders' zone is "within
  9" of the table centre", so the reading is available - but the book does not address it and inventing a
  18"-from-centre Scout deployment is a rules decision, not a fix. Engine, small, **needs a call**.
- **The shop has no executable path outside the GUI.** The headless registry deliberately uses
  `AiHordeShopResolver`, which buys nothing, and `ScenarioFile` has no `gameMode` field - so
  `HordeShop.ApplyOrder`, `ApplyUpgrade` and `HordeReinforce.Apply` can never run in an automated game.
  Engine (test tooling), medium.
- **The skirmish books put reinforcing at 9" while upgrades and recruiting are 6".** Everything else
  scales 9 -> 6 between the scales, so this is very likely an editing slip in OPR's own books rather than
  an intended difference; the engine uses one shop radius. Recorded rather than implemented.
- **Per-system army points are not applied.** The books specify 2000 / 1500 / 800 / 600 per side; the
  lobby takes its own points value for both. A default, not a rule the engine breaks.
- **`HordeRules.CornerForRoll` and `ChoosePriority` are dead** - tested but never called, the production
  paths inline the same logic. Two green tests pinning nothing the game runs.

Also worth recording, because it is the opposite of what #414 found: **the Horde code does no
book-name matching at all.** Traits are namespaced `"Trait: X"` and hero detection is reference equality
against `CoreRuleCatalog.Hero`, so the Villain-class and renamed-rule-class failures are structurally
impossible in this mode.
