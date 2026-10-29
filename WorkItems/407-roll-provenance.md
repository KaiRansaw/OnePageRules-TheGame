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

## Investigation, 2026-10-29 - most of the plumbing already exists

Checked before building anything, per the note below. Findings:

- **`--trace-rules` is NOT reusable here.** `RuleTrace` narrates hook evaluation as free text into the
  Debug log channel. It proves a rule fired, but it is prose, it is not attached to a particular roll,
  and it cannot be hung off a hover. Useful for manual debugging, not for this.
- **The evaluator ALREADY knows the source rule.** `RuleEvaluator` has
  `EvaluateAllNamed` / `EvaluateAllNamedLive`, both returning
  `IReadOnlyList<(RuleOperation Op, string RuleName)>` - the per-frame UI queries use them. So the
  rule name is available at dispatch; nothing new needs recording at the rules layer.
- **`RollModifierSink` is where provenance is thrown away.** It folds every operation into
  `Dictionary<ERollKind, int>` - a single net integer per roll kind. `Add(roll, delta)` takes no
  source, and `ApplyFrom(IEnumerable<RuleOperation>)` takes the un-named operations. That is the one
  choke point: by the time the stage reads `Net(roll)`, "+1 from Vengeful Strike" has become "+1".
- **Each operation can already describe itself.** `RuleOperation.Describe()` is abstract and every
  operation implements it ("added -1 to Hit rolls"), with a documented phrasing contract. The
  evaluator pairs it with bearer + rule for the game log. So the text for each line is in hand.

### The shape of the change

1. `RollModifierSink` keeps a list of entries (roll kind, delta, source rule, bearer) alongside the
   running total, and exposes it. `Net(roll)` keeps working unchanged, so nothing that reads it breaks.
2. `Add` takes an optional source; `ApplyFrom` gains an overload taking the `(Op, RuleName)` pairs the
   named evaluator already returns.
3. The six call sites switch to the named evaluation and pass it through:
   - `DetermineHitRollStage` (hit rolls) - do this one first, end to end.
   - `RollToHitStage` (saves), `SyntheticHitResolution` (saves),
     `MoraleUtilities` (morale), `ShootingForecast` (the pre-roll forecast),
     `Ai/Tactician/CombatMath` (bot maths - may not need it at all; check before touching).
4. The breakdown rides along to the Battle Report hover, which already shows both sides' dice (U1).
5. The same pattern then applies to `WoundModifierSink` and `MovementModifierSink` - wounds and the
   move-distance question Kai raised separately (QA 3.31: Crippled's -1 to melee is not shown
   anywhere).

**Start with hit rolls only.** One vertical slice, per CLAUDE.md - prove the whole path from rule to
hover, then repeat for saves, morale and wounds.

## Notes

- 2026-10-29: agreed with Kai. He chose the engine half over the visible wording changes, on the
  argument that it is what lets him verify the ~120 skills rather than guess.
