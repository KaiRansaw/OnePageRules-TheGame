# 410 - Campaign Rules v3.6.0: one shared campaign engine

**Goal:** one campaign implementation driven by a per-game-system price list, serving Grimdark Future,
GF: Firefight, Age of Fantasy and AoF: Skirmish, replacing the hand-rolled v3.5.1 Firefight campaign.

**Source:** the four Campaign Rules v3.6.0 books, read 2026-10-30. The written-up comparison is the
project doc `claude/campaign-rules-delta.md`.

---

## 2026-10-30 - slice 1: numbers, tables and the after-game loop

Engine 3,691 passed / 0 failed. UI 2,997 passed / 0 failed. Headless smoke exits 0.

### The finding that shaped this

The four books are **the same document with four price lists**. Same six-step structure, same casualty
table, same injury/talent/trait tables, same six hero skill sets, same 36-entry random event table.
They differ only in numbers, plus two wording splits:

- GF and AoF carry a "+3 XP: Killed a Hero and its unit" line the skirmish books lack. It needs no
  code: +1 for the unit plus +2 for the Hero already comes to 3, so the line is a clarification that
  you don't claim both separately on top.
- GF and AoF hero skill traits read "the Hero **and its unit**"; GFF and AoFS read just "the Hero".
  Recorded as `CampaignNumbers.HeroSkillsCoverUnit`; not yet consumed, because none of the skills that
  would need it are wired up (see below).

So this is one engine with a number table, the same shape Horde Mode's per-system numbers already use.

### What landed

- **`Campaign/CampaignNumbers.cs`** - the price list per game system, plus `ECampaignType`.
  `MinimumSpend` is **derived as 80% of treasury** rather than stored: GFF prints 320 of 400, AoF 600
  of 750 and AoFS 240 of 300, all exactly 80%, while GF prints "spend at least 800pts" against a 750pt
  treasury, which is impossible. **Kai's call 2026-10-30: treat GF's 800 as an OPR typo and derive
  every minimum**, which gives GF 600.
- **`Campaign/CampaignTables.cs`** - the shared 3.6.0 roll tables: injuries, talents, level-up traits,
  the three skill-set pairs and their 18 skills, and the six special objectives.
- **`Campaign/CampaignRules.cs`** - `CannotStart`, `Start`, `AfterGame`, `AddXp`, `LevelUp`, the
  pending-choice flow (Specialist, Fast Learner, Lucky Break, Learned Lesson, skill-set pick). Dice
  come in as `Func<int>` so every table is testable.
- **`Rules/Dispatch/Campaign36RuleCatalog.cs`** - 27 rule definitions for the 3.6 traits.
- **`Campaign/StrikeTeamRules.cs`** - now a thin wrapper over `CampaignRules` with
  `CampaignNumbers.Firefight`, so the existing Firefight Campaign screen keeps working and is correct.
- **`StrikeTeamInfo`** gained `GameSystem`, `CampaignType` and `IsBot`.

### What this fixed

Every points number in the old Firefight campaign was wrong against the GFF book that arrived with 3.6:

| | before | after (GFF 3.6.0) |
|---|---|---|
| Starting army | 200pt cap, max 40pts carried over | 400pt treasury, spend at least 320, rest carried over |
| Win / loss reward | +20 / +40 | **+80 / +160** |
| Regular / hero level cost | +5 / +15 | **+15 / +25** |
| Hero injury / talent | -5 / +5 | **-15 / +15** |

### Deleted on purpose

`Rules/Dispatch/CampaignRuleCatalog.cs` (the v3.5.1 tables) and most of `StateMachine/FirefightCampaign.cs`.

This was not tidying. **Sixteen trait names survived the version change with a different effect** -
Prowler, Runner, Duellist, Fanatic, Berserker, Leader, Tactician, Mastermind, Chemist, Herbalist,
Suppressor, Destroyer, Motivated, Crazed and others. Leaving the 3.5.1 implementations registered would
have silently applied the wrong rule to a hero that rolled the new one: a hero with "Skill: Leader"
(3.6: +1 to Morale Tests in melee) would have got the 3.5.1 aura granting +1 morale to every friend
within 6". Ten names were dropped outright (Instigator, Vanguard, Scavenger, Climber, Hunter, Warden,
Bitter Rivalry, Horrible Scars, Arm Injury, Traumatized).

Call sites removed with it: `ChooseActionStage` (hero auras on activation), `DetermineHitRollStage`
(Mastermind / Suppressor / Crazed), `MoraleUtilities` (Motivated auto-pass), `FirefightRules`
(Herbalist wound-ignore and the Berserker / Chemist wound-roll modifier). All four are now ordinary
rule definitions or deliberate markers, so none needs an engine special case.

A strike team file written before this change keeps its old rule names and will report them as
unimplemented and inert rather than misapplying them. Firefight Campaign has never actually been
played (FC1 was blocked on army import until M2), so no real save is affected.

### Deliberately narrower than the book - 13 markers

Thirteen of the 3.6 hero skills are registered as named, described markers with no mechanical effect,
each with a reason on the `RuleCatalogLintTests` allowlist:

- **Once per game, no engine offer (8):** Explorer, Idol, Berserker, Sharpshooter, Healer, Tactician,
  Strategist, Mastermind. The engine has no general per-unit once-per-game offer - the same gap already
  recorded for `Trait: Elite`, whose re-roll only reaches the morale test. Fixing it once unlocks all
  eight plus Elite.
- **Morale tests don't record their trigger (2):** Motivator ("+1 to Morale Test rolls triggered by
  shooting") and Leader ("+1 ... in melee").
- **A melee strike-back doesn't record being charged (1):** Duellist ("AP(+1) in melee when charged").
- **The Defense roll doesn't know whether the attacker moved (2):** Suppressor and Destroyer. Both want
  AP(+1), which is a modifier on the target's Defense roll at `Shooting_OnHitRollComplete`, and that
  hook's context carries no `IHasAttackerMoved`. The catalog fire-lint caught this - both were written
  as real hooks first and failed.

The other fourteen (four injuries, five talents, Prowler, Runner, Fanatic, Herbalist, Chemist) are real
rule definitions and fire.

### Not in this slice

The whole pre-game half and steps 5-6, all still to do:

- Campaign types other than Endless (Kai's call: Endless only for now), Parallel Play, Late Joiners.
- Per-mission army selection capped `ArmyCapOverLowest` over the lowest army.
- Underdog points. Deferred by Kai's call - they need two or more real armies, and the first build is
  one human army vs a bot.
- Special objectives: the table is in `CampaignTables`, nothing rolls or scores it.
- Random events: the 36-entry table is not written yet.
- Upgrade Armies (buy/sell) and Recruit Units (one Hero per mission, max 3; the 5-XP veteran recruit).
- Playing on past round 4 until a winner; conceding; No Permadeath.
- A bot army with its own treasury and an automatic spender, which the one-vs-bot build needs or the
  campaign has no arc.
- No UI at all yet - the Firefight Campaign screen still drives everything through `StrikeTeamRules`.
