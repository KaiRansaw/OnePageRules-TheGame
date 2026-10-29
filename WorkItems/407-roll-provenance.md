# 407 — Every roll should be able to say why it came out that way

**Status:** open, next up. Agreed with Kai 2026-10-29 as the FIRST half of the Battle Report rework.
**Bucket:** PERMANENT (engine records and exposes new data - survives the Godot port).

## The problem

Kai, QA Notes, on Vengeful Strike: *"i am unsure if the modifiers are correctly being applied."* He
has put the rest of 3.13b/3.13c **on standby** until he can tell.

That is not a wording problem. The game rolls dice and shows the result, but does not show its work -
the base target number, which modifiers applied, where each came from, and the final number. So there
is no way to verify a skill short of reading the code, and we have ~120 skills built that Kai cannot
confirm are doing anything.

The same gap sits under several other open QA items: is Reckless adding its wound, did the cover
bonus apply, is Crippled's -1 to melee real (QA 3.31 says the card does not show it at all).

## What to build

The engine should carry, for each roll it makes, a small record of how the target number was reached:

- the base value (Quality / Defense / the stat being tested),
- each modifier as a named entry: **what it was** (+1 / -1 / AP(+1) / re-roll), **where it came from**
  (skill name, condition, cover, weather, rule), and whether it actually applied or was suppressed,
- the final number rolled against, and the dice that came up.

Then the Battle Report hover shows that list instead of only the outcome. The hover already shows both
sides' dice (U1), so this extends an existing surface rather than inventing one.

**Order matters:** do the engine recording first, then surface it. The recording is permanent; the
panel that draws it is rental.

## Why this one first

- It unblocks Kai's own testing of every skill already built - he is currently blocked on 3.13b/3.13c.
- It answers "is this rule firing?" once, for every rule, instead of one bug report at a time.
- `--trace-rules` already narrates hook evaluation to the Debug channel (see CLAUDE.md), so the engine
  may already know most of this - **check what that produces before building anything new.** The work
  may be exposing existing data rather than recording it afresh.

## Then (the visible half, work item TBD)

Kai's wording spec from QA Notes - shorter lines, the *"Hero throws \<Item\>!"* standard with a dotted
underline and a ~1/3s hover popup, Poison / Fire / search-token phrasings. Those are quick once the
data is there, and are tracked in `claude/fdg-todo.md`.

## Notes

- 2026-10-29: agreed with Kai. He chose the engine half over the visible wording changes, on the
  argument that it is what lets him verify the ~120 skills rather than guess.
