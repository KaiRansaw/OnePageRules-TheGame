# 419 - The casting window, and the two Quest Horde books

**Goal:** three rules items from #418's open list that need no decision from Kai: the once-per-activation
casting gate, the Blast per-unit cap, and the AoF: Quest Horde book (never read against the engine).

**Raised by:** Kai, 2026-10-31 - "still at work, what else can we do to make progress", so work that
needs neither his input nor a playtest.

---

## Notes

### 2026-10-31 - the enemy AI audit, and the first slice of the Solo & Co-Op rules (B17)

**Kai's direction, which narrowed the whole item:** "I don't need a bot controlling any hero's. I just need
the bots to correctly run enemy armies in every game mode correctly for their game mode." Recorded in the
to-do as the priority. The hero AI from B16 stays (it is what lets a mission be verified at all) but it is
not the goal, and nothing should surface it to the player.

**Where the spec lives.** The 3.6.0 Horde books say the enemy side "can either be controlled by another
player or by the AI (**using the Solo & Co-Op rules**)". Kai then added `GF - Solo & Co-Op Rules v3.6.0`
and `GFF - Solo & Co-Op Rules v3.6.0`, and confirmed there are none for the Quest systems - right, their
core rules carry their own flowchart. Read both in full. They are word for word identical except four
clauses: **Blast** targeting (GF: multi-model without Tough first; GFF: multi-model first), **Deadly**
targeting (GF: single-model with Tough first; GFF: single-model first), and **Artillery** and
**Transport**, which only GF has. So AoF and AoFS cannot be assumed to match - still not uploaded.

**What the audit found.** The by-the-book bot followed almost none of it. Two findings mattered most:

- **Shooting had no notion of distance.** `AiChooseRangedAttackResolver` ordered by cover, then by how many
  of its own models could shoot. The book says closest, always. So the bot would shoot across the table
  past a unit at 6" whenever the far one was in the open or more guns could see it.
- **Activation was army build order.** The activation request went through a `DerivedRequestAdapter` onto
  `AiSelectionResolver`, which takes the first option. The book wants a D3 for one of three table sections,
  the next section along if that one is empty, a random unit from it, and Shaken units last. Predictable
  activation is a real weakness - a player who can name the next unit to move can stand outside its reach
  all game.

**Shipped in this slice:** `SoloAiTargeting` (closest, not-yet-activated, open-before-cover, wired into the
ranged resolver and reused for melee), `SoloAiActivationOrder` + `SoloRulesActivationResolver` (the D3
section order, Shaken last, on its own seeded stream at salt 3 so it does not shift the objective or
terrain streams and re-roll the pinned benchmarks), and `AiCombatMath`, which is the geometry and
expected-wounds estimate extracted out of `StarQuestAiBrain` so the two brains share one copy - the Quest
brain keeps its old names as forwards, so no caller changed.

**Built, tested, and deliberately NOT wired yet:** `SoloAiBrain`, the book's five-step decision tree.
Twelve tests, one per branch. It is not driving units because that needs the action and movement
resolvers, which is where B16's hero AI cost three wrong guesses - worth its own slice rather than bolting
it on at the end of this one. Recorded here and in the to-do rather than left implied, because a brain
nothing calls is easy to mistake for a finished feature.

The Tactician and Strategist are untouched on purpose: they are the #191 challenge ladder, our own
invention, and they should not be bent toward the book's AI.

Engine 4,086 -> **4,117** (31 tests across the three new fixtures). App 8,750 unchanged. Build **0.31**.
Sabotage-checked: dropping the distance key, the Shaken filter, or step 3's attended-objective test fails
six tests between them. A played GF Horde game still runs to round 10.

**Next, in order:** wire the decision tree into the action and movement resolvers; then AI deployment and
objective placement (3 groups, D3 sections, closest objective, out of bad terrain, 6 squares); then the
per-rule clauses, including the four that differ between GF and Firefight; then Challenge Bonuses, which
map neatly onto a difficulty setting. Also newly in the project and unread: both Mission Cards books.

### 2026-10-31 - a hero-side AI, and three priority calls I got wrong before playing it (B16)

Kai picked this from four options as the next build: "the biggest single lever on how much Claude can
verify without Kai", and the to-do had ranked it first for the same reason.

**The gap.** `StarQuestAiBrain` is the ENEMY's flowchart and says so - `UsesQuestAi` excludes heroes - so
a bot-run party fell through to the generic solo bot, which plays Grimdark Future: it walks at enemies and
at `IObjective` markers. A Quest mission is won by standing **within 1"** of a `QuestMarker` and taking the
Objective skill, then leaving by a table corner; loot comes from standing within 1" of a search token.
**None of those three things is an `IObjective`**, so the solo bot could not see any of them. #414 had
already taught the bot to TAKE its free Skill action, which was the other half - but a Skill action taken
8" from the nearest marker just reports "No objective marker within 1"."

`QuestHeroAi` is the policy. OPR has no AI for the heroes (they are the player's side), so the order is a
decision, not a reading: deliver what it carries -> leave -> work the live objective marker -> pick up a
token within 6" -> no plan, and the solo bot fights. Wired in by two small hooks: the movement resolver
walks to the plan's goal, and the action resolver substitutes Move for a Charge the solo bot wanted while
the hero still has somewhere to be. The Skill latch, the shoot-if-in-range rule and the decline latch all
stay where they already work.

**Measured, eight played missions, same eight seeds throughout:**

| | before | after |
|---|---:|---:|
| Primary objective completed | 0 | 6 |
| Secondary objective completed | 0 | 3 |
| **Mission finished and left by a corner** | **0** | **4** |

Heroes also collect loot in Horde now - 11 reveals and pickups in one game, where the old comment in the
Horde fixture read "the bots never pick them up". That also makes B14's AoF: Quest item-name guard live:
the party really does finish those games holding items, so a Star Quest name among them would fail.

**Three of the four priority calls were wrong, and only playing it showed each one.** Worth recording as a
pattern, not three incidents - every one of them looked obviously right in the file:

1. **Tokens above leaving.** A party finished both objectives by round 19, then spent six rounds wandering
   between two unopened tokens while reinforcements arrived every other round, and died holding the loot.
   Leaving is what banks the XP.
2. **One carry destination instead of four.** A carried marker goes to a delivery marker (primary 6), an
   extraction marker (secondary 6), the party's own corner (primary 3) or **any** corner (secondary 3).
   Only the two that are markers on the table were handled, so a hero that picked up a Capture marker had
   no plan at all, fell back to the solo bot, and went looting with the objective in its hands. Four of
   eight missions drew Capture or Escort and every one stalled there.
3. **No cap on the token walk.** This one broke Horde Mode. With tokens chased at any range the party
   stopped fighting: it crossed the table for loot, never closed with anything, so it neither killed
   enemies nor died. Waves then piled up until a seed that finished in **under a second** could not finish
   in **ten minutes**. I spent three wrong guesses on it (a stay-in-place fallback, then a once-per-
   activation latch on the Charge substitution - both real guards, both kept) before instrumenting the
   resolver and finding only 438 hero decisions across 28 rounds. It was never looping. It was a different,
   much longer game. `TokenDetourInches = 6f` fixed it: 45 Horde integration tests went from failing after
   4 minutes to passing in 12 seconds.

The lesson I want to keep: when a change makes a game slow, **count the decisions before assuming a
loop**. Three guesses cost more than one `Console.Error.WriteLine` would have.

Also fixed two assertions my own change falsified - both said "the bots never pick them up" - rather than
leaving them green by accident.

Engine 4,062 -> **4,086** (24 tests in `QuestHeroAiTests`, brain and movement). App 8,750 unchanged.
Build **0.30**. Sabotage-checked: removing the detour cap or the cut-losses rule fails three tests each.

**Deliberately not done, recorded rather than dropped:** the party does not spread over several markers
(every hero walks to the nearest, which for objectives is often right and for tokens is wasteful); no
notion of holding Horde's objective markers at the end of a wave for the 2 XP each; and the remaining
mission losses are attrition, not navigation - all six failures were "every hero went down", to between
two and nine waves of reinforcements, which is evidence for Kai's open "is Star Quest too hard" question
rather than an AI defect.

### 2026-10-31 - the casting window: one per activation, in every current book

`Caster(X)` was **"at any point before attacking"** in GF 3.5.1 and Quest 2.0.1, and #234 built the gate
to match: anything up to the first attack was fair game, so cast -> move -> cast a different spell was
legal and `ChooseAction_AfterMoving_StillOffersCast` pinned it.

Checked all six current books. **Every one of them** - GF, AoF and AoFS 3.6.0, and both Quest systems at
2.1.0 - now reads:

> "**Once per activation**, before attacking, spend as many tokens as the spell's value to try casting
> one or more spells (only one try per spell)."

So this is not a per-system difference, it is one engine change. One window per activation; several
spells inside it; leaving it is final.

`IUnitActionContext.CastingWindowClosed` is the gate. What closes it:

| | Closes the window | Why |
|---|---|---|
| Move **after** a cast | yes | the cast-move-cast pattern the new wording outlaws |
| Move **before** any cast | no | the window is "before attacking", not "before moving" |
| Cast, then cast again | no | that IS "one or more spells" inside the one window |
| Attack | yes, always | and see below |

**The extra action was the trap.** Star Quest's extra action calls `BeginQuestExtraAction`, which clears
`HasMoved` and `HasAttacked` so the hero can act again - and `HasAttacked` was the *only* thing stopping
a late cast. A hero could therefore attack, pay 3 power, and get a second casting window out of it. An
extra action buys an action, not an activation, so the flag is deliberately not cleared there. Pinned by
`QuestExtraAction_DoesNotReopenTheCastingWindow`.

Enforced in both places, matching the existing `HasAttacked` pattern: `ChooseActionStage.GetCanCast`
(so Cast stops being offered) and `CastSpellStage.Enter` (because the stage is reachable directly).

**A test that passed for the wrong reason, twice.** The stage-level test needed a control - "a second
different spell in the same window DOES get cast" - and the obvious setups both faked it:

- two **Foe** damage spells: the first kills the lone test enemy, so the second is refused for want of a
  target. Looked like the window working.
- two **Friend** damage spells: affinity points the 1-hit spell at the caster, which is a single model,
  so the caster kills itself. Also looked like the window working.

Only `SelfBuffSpell` (adds a rule, kills nothing) gives a real control. Both versions passed with the
gate deleted; the current pair fails when it is. Worth remembering as the shape of the error, not just
this instance: a test whose subject can die proves nothing about why it stopped.

Engine 4,055 -> **4,062** (7 tests). Sabotage-checked: deleting the move-closes-window line fails the
menu test and the stage test and leaves the control green.

### 2026-10-31 - AoF: Quest Horde v2.0.0 read: no new mechanics, one table disagreement

#416 read the four 3.6.0 Horde books; the two Quest Horde books are their own documents and had never
been read. Read both in full and put them side by side: **they are the same book twice.** Preparation,
mission-end conditions, high-score formula, the four-step structure, Set Priority, wave size, corner
deployment, objective damage, alertness, XP rewards, the shop, new search tokens, boss fights, injuries,
the alertness/time/weather tables, the treasure table and all 36 events - every number and every clause
matches. Only the vocabulary differs, and that is what B13 and B14 fixed.

A useful by-product: this book is an **independent second source** for B14's event names. All 21 renames
and all 15 shared names match the Campaign Builder exactly, which is the check the B12 mistake deserved.

Checked the engine against the books' numbers - all correct:

| Rule | Book | Engine |
|---|---|---|
| Final wave | 10 | `FinalWave = 10` |
| Alertness to end a wave | 6 | `AlertnessToEndWave = 6` |
| Alertness gain | +1, or +2 with no enemies on table | `AlertnessGain` |
| Wave size | X * 25% of party points | `wave * partyPoints / 4` |
| Hero deployment | within 6" of table centre | `HeroDeployRadii` |
| Wave deployment | within 6" of a random corner | `WaveCornerRadii` |
| Objective damage | 3 markers to destroy, 3" | `ObjectiveDamageToDestroy = 3` |
| Wave XP | (2 per seized, +1 each if priority seized) x wave | `WaveXp` |
| XP split | as evenly as possible | `Split` |
| High score | total party XP x last wave finished | `PartyXp * WavesFinished` |
| Random events | on wave deploy, not per round | rolled in `StartWave` |
| Time of day / weather | switch at the start of the next wave | `AdvanceTimeAndWeather` in `StartWave` |
| Boss fights | waves 4, 7, 10 at double points | `OfferBossFight` |

**The one disagreement, and it is a decision rather than a bug: the levelling table.**

| | Horde Mode v2.0.0 (both systems) | Campaign Builder v2.1.0 (both systems) |
|---|---|---|
| Level 2 | 2 XP | 3 XP |
| Level 5 | 12 XP | 18 XP |
| Level 10 | 50 XP | 75 XP |
| Level 15 | 122 XP | 168 XP |
| Level 4 offers | +2 Tou / +1 Qua | +2 Tou / +2 Pow |
| Level 8 offers | +2 Tou / +2 Pow | +2 Tou / +1 Qua |

The engine uses the Campaign Builder's curve everywhere (corrected onto it on 2026-10-30; the Horde
curve is the one it used to have). So **Horde levels heroes about a third slower than its own book
says**, and hands out +1 Qua four levels later.

Horde Mode is on v2.0.0 and the Campaign Builder on v2.1.0, so the most likely explanation is OPR not
having updated the Horde PDF - and B12's lesson was exactly this: a difference between books of
different ages is usually an age difference, not a rules difference. But levelling speed is something
Kai feels in play, so it was his call rather than a silent pick.

**Decided (Kai, 2026-10-31): one table everywhere.** The Campaign Builder v2.1.0 curve stays in Horde
too, so no code change. If OPR updates the Horde PDF and it still disagrees, this is the line to
revisit - and `QuestProgression.XpForLevel` is one array, so a per-mode table would be a small change
if it ever becomes real.

### 2026-10-31 - Blast's per-unit cap: not a bug, a reading Kai already picked

The 2.1.0 wording is "each hit is multiplied by X. Hits must be split evenly between the target and all
enemy units within 3" of any model from the target (players pick how), **but each unit may only take up
to as many hits as models in it**", and `BlastSpreadRules` instead caps the MULTIPLIER at the models in
the whole cluster. That is deliberate and documented in the file: Kai was shown both sets of numbers on
2026-10-31 and picked the cluster-capped reading, because the literal per-unit cap makes Blast *worse
than no Blast* against a small unit.

The 2.1.0 books add two worked examples, and they do not settle it - they contradict each other:

- "2 Attacks and Blast(3) against a unit with 2 models. **Each hit is multiplied by 2**, so the target
  takes 4 hits." - the multiplier is capped at the target's model count, and 4 hits land on a 2-model
  unit, which the per-unit cap forbids. This example is carried over verbatim from GF 3.5.1.
- "1 Attack and Blast(6) against a 2-model unit within 3" of three 1-model units. The hit is multiplied
  by **6**, and the first unit takes 2 hits, the other three take 1 each." - the multiplier is NOT
  capped, and the per-unit caps apply. 5 hits land of 6 rolled.

The engine's cluster cap gives 2/1/1/1 for the second example - the same distribution the book prints,
by a different route - and matches the first example exactly. They diverge elsewhere: 2 hits of Blast(3)
on a 2-model target beside a 1-model unit is 3/3 under the cluster cap and 2/1 under the per-unit cap.

So: no change without Kai, since it is his earlier decision and a real gameplay difference. Written up
for him with those numbers.

## Outcome

Casting window done and pushed (build 0.29). The Horde audit found no missing mechanics, which is the
answer the item was raised to get, and it independently confirmed #418's event names. Neither of the two
things that looked like bugs was one: Blast's cap is Kai's earlier decision and still matches both of the
2.1.0 worked examples' printed outcomes, and the Horde levelling table is settled - one table everywhere.
Nothing left open here.
