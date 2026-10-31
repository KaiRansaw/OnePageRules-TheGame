# 419 - The casting window, and the two Quest Horde books

**Goal:** three rules items from #418's open list that need no decision from Kai: the once-per-activation
casting gate, the Blast per-unit cap, and the AoF: Quest Horde book (never read against the engine).

**Raised by:** Kai, 2026-10-31 - "still at work, what else can we do to make progress", so work that
needs neither his input nor a playtest.

---

## Notes

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
