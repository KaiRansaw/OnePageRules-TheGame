# 468 - The book files' rule summaries vs OPR's wording

**Kai, 2026-11-19 (bug hunt, "Clear them out").** Every rule definition in the 288 bundled books carried a
`description`. For 5,205 of them OPR publishes its own wording for that game (`OfficialRuleText.OwnText`), and
3,608 of those said something else: a summary written beside the rule's effects when the rule was built.
Since 2.86 the hovers show OPR's wording, but the engine still reads `Definition.Description` directly in a few places
(ability offers, token tips, the Raylib hovers), so the summaries were still on screen there.

**Done (Build 2.90):** each of those 3,608 descriptions now holds OPR's wording for its game, replaced in place
(nothing else in a book file changed). `BookDescriptionsShippedDataTests` fails if a bundled definition OPR publishes
wording for ever says anything else again - re-run the fix after a re-harvest of the rule text.

**What this file keeps:** the summaries were the only record of what each effect was BUILT to do. Where a summary and
OPR's wording disagree in substance, the effect may be wrong, not just the words. The ledger beside this file
(`468-book-summaries-vs-opr.tsv`) lists every distinct pair before it was overwritten: 1,379 rows over
358 rule names, 751 of them with different NUMBERS in the two texts (sorted first).

**Found while doing it - a real one:** Age of Fantasy *Disintegrate* is built as "AP(+2) against units with Defense 2+"
(every unit), where OPR's 3.6.0 wording is "when attacking units where most models have Tough(3) or higher".

**Not done - the audit:** read the "numbers differ: yes" rows against each rule's effects and fix the effects that are
behind the book. Many rows are harmless (a helper rule that is one half of a "pick one effect" rule; 9 inches vs 9"/6",
already handled by the 2.69 audit; auras whose summary is shorter). Engine work, PERMANENT, with a test per fix.

## The audit (Kai: "Audit them next", 2026-11-19)

Of the 751 rows with different numbers, 197 are harmless kinds (helper halves of pick-one rules, inch spellings
already covered by 2.69, aura summaries that are just shorter). The other 554 rows over 206 names were read:
187 are targeted buffs / debuffs / marks, 180 are auras (engine conferral matches the books), and 46 names by hand.

**Batch 1 (Build 2.92):**
- "Pick one / up to N" before-attack abilities now take as many targets as the text says (210 definitions).
- "Only one use per game." is once per game (154).
- Enemy picks "in line of sight" need line of sight (15).
- Guards: `BeforeAttackPicks_TakeAsManyUnitsAsTheBookSays`, `OnlyOneUsePerGame_IsOncePerGame`,
  `EnemyPicksInLineOfSight_NeedLineOfSight`.

**Batch 2 (Build 2.92):**
- "May roll +1 attack" (Bloodborn, Clan Warrior, Predator Fighter/Shooter, Primal, Royal Warrior) is an extra attack
  die, not a hit that lands outright.
- Tough gates use each game's number (3 / 9 / 27): Disintegrate (and its Defense 2+ gate replaced by "most models
  Tough"), Obliterate, Melee Slayer, Ranged Slayer. Quest Melee Slayer works in any melee, not only charges; Ranged
  Slayer needs the unit not to have moved.
- Destructive: AP(+1) vs units above half strength. Great Sergeant AP(+2). Shatter: 6s get AP(+3).
- Entrenched: -1 to be hit from over 12" away by shooting, only if the unit has not moved this round.
- Defense Buff: +1 Defense on its next trigger (it was lending Entrenched).
- Frenzy / Growth markers stop at the book's max and step; Quest Frenzy / Growth markers are removed when the next
  wave of reinforcements deploys (`QuestWaveMarkers`, engine test `QuestWaveMarkersTests`).
- GF Fortified Growth gains its marker at round start, not while Shaken.
- Guards: `ToughGates_UseTheNumberTheBookPrints`, `MayRollPlusOneAttack_IsAnExtraAttackDie_NotALandedHit`,
  `FrenzyAndGrowthMarkers_StopAtTheBooksMaximum`. Rule fire-lint's tough opponent is now Tough(27).

**Still open:**
- Vengeance (GF / AoF): should give as many tokens as the unit had models with the rule.
- Extended Buff Range, Quest and Skirmish variants ("within 6\" of a Hero", "only one use per game").
- "Nearest" picks stay the existing house rule (the player is trusted). Dangerous Terrain Debuff (Immediate) is
  built as an immediate test, which matches the text.
