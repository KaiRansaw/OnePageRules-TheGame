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
