# 464 — Battle Report rework (the words half)

**Status:** done 2026-11-05 (build 1.04, B88).

Goal (fdg-todo, Kai): shorter plainer report lines; items / poison / fire / search tokens / random events
read as what happened, with what they are and the rolls behind them on hover; one consolidated wound
breakdown on the attack card hover; a dotted underline on item names.

## Found on arrival
Most of the *wording* had already been done in the Quest QA passes (3.14-3.20): "Hero throws X!", "fails
to fight off Poison", the discovery/pickup collapse. What was missing was the HOVER: the text promised
"the roll is on the hover" but no beat carried it.

## Built
- `BannerBeat.Terms` (`ReportTerm(Name, Text)`) and `BannerBeat.Details`; `Announce(..., terms, details)`;
  `QuestActions.Toast(..., terms, details, rules)`. Serialised with the beat, so networked clients get them.
- Items used/thrown, discovered, picked up: the item name hovers its description (`QuestTreasure.ItemTerms`).
- Poison: "Poison" hovers what it does; the line hovers the roll, the wound, models lost.
- Fire: "<Unit> burns in flames." (hover: the fire marker, the dice, models lost); "The flames die out."
- Search hazard: "A hazard! <Hero> is <Condition>." - no token prefix; condition hovers its effect; roll on
  the line.
- Random events (Quest and campaign): "<flavour> <Event name>!" - flavour fixed per event number; the name
  hovers the effect; the line hovers the roll / event number. The campaign runner's own record line is
  unchanged (still logged; tests read it).
- Reckless: "<Hero>: Reckless - deals +1 wound." / "takes +1 wound.", skill text on hover.
- DiceTray: terms drawn with a DOTTED underline (rule names keep the solid one); the hovered word's own
  text (a line can name several now); the line's details tooltip; attack cards open their hover with the
  arithmetic - attacks -> hits (needed), saves (needed), ignored, wounds, models lost.
- Tests: `Tests/BattleReportWordingTests.cs`.

## Not done
- Other systems' everyday lines (standard shooting/melee cards) were not re-worded - they are already the
  one-sentence card form; tell me any that read badly.
