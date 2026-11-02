# 441 — Command Group obituary in the battle report, with hoverable rule names

**Goal.** When a Banner or Musician leaves the table, say so in the Battle Report and say who just lost
the buff. Any rule name in a report line is highlighted, hovers its own book wording after a third of a
second, and can be clicked to keep the card open so several can be read side by side.

Asked for by Kai, 2026-11-01, as the follow-up to #439 (which built the picks but said nothing when the
bearer died — a player could lose a buff on two units with no indication it had happened).

---

## Notes

### 2026-11-01 — built, verified, committed

**The three lines.**

| When | Line |
|---|---|
| A bearer dies and units were relying on it | `Spearmen with Banner died! Archers and Militia lose benefits from Banner!` |
| A bearer dies and nothing was relying on it | `Spearmen with Banner died!` |
| A unit that was being covered dies | `Archers receiving Banner died!` |

**Driven by the grants, not by a list of systems — and this is a deliberate departure from how Kai
described it.** He asked for the long form in "Firefight/Skirmish" and the short form in "GF/AoF". But
the picks exist in **four** systems, not two: Star Quest and AoF: Quest print the same wide Banner
(#439). Hard-coding his two names would have left the two Quest systems silently on the short form,
which is the exact shape of bug CLAUDE.md warns about. So the code asks *"did anything actually lose a
buff"*, which produces his short form in GF and AoF (they have no picks at all, so the answer is always
no), his long form in Firefight and Skirmish, and the right thing in the two he did not name. Flagged to
him in the patch notes rather than done quietly.

Two smaller judgement calls, both recorded in `claude/house-rules.md`:

- **An already-dead pick is not named.** It has nothing left to lose, and the books' own wording is
  about "picked models **that are deployed**".
- **Unit-level, not model-level.** Kai, this session: *"just keep it to 'When the unit dies'. It's close
  enough for now."* The books say *"if this model is killed"*; a Banner on a joined hero therefore
  survives a bit longer here than it should. Logged, not silently absorbed.

**Timing is the whole trick.** `CommandGroupObituary.Announce` runs in `UnitDestructionNotifier`
**before** `TokenClearService.ClearForDestroyedOwner`, because the sweep is what removes the grants —
after it there is nothing left to name. It matches on the same `OwnerUnitID` the sweep matches on, so
the units the line names are exactly the ones about to lose the buff and the two cannot drift apart.
Reorder the two calls and every long line degrades to the short one, naming nobody, with no error
anywhere — so there is a test that goes through the real `NotifyUnitDestroyed` seam specifically to
catch that.

### The hover, and the book wording

`PresentationBeat.ReferencedRules` is new: a beat lists the rule names appearing verbatim in its text,
and the front end highlights them (gold, underlined — not colour alone) and hovers the wording. The
engine deliberately does no formatting; a beat is the same object on a networked client.

The wording comes from `OfficialRuleText.Text(name, systemSlug)` — Army Forge's own text for **that
game system** (#420), not a paraphrase and not GF's wording reused. That matters here more than
anywhere: Banner is the rule whose text genuinely differs between the six systems, which is what #439
was about. The renderer supplies the lookup because it is the only thing that knows which system is
being played; before a game starts the lookup is null and there is simply no tooltip.

Hover delay reuses `TableTooltipOverlay.HoverDelaySeconds` (0.35s) rather than inventing a second one —
Kai asked for "1/3 seconds", the table tooltips already wait that long, and two different waits in one
UI reads as a bug.

### Pinned rule cards (Kai, same session, mid-build)

> "Once that tooltip is up, if you click on the highlighted word itself, the tooltip will stay up. You
> could then move the cursor off the highlighted word, and go hover over something else to make another
> tooltip appear."

Click a highlighted word while its tooltip is up and it becomes a card that stays — draggable, with a
close button, several open at once. Clicking the same word again takes its card away, so one gesture is
both the on and the off; the hover tooltip is suppressed for an already-pinned rule so there is never a
floating duplicate next to its own card. The tooltip carries a "Click to keep this open." line, because
nothing else would tell a player the gesture exists.

Drawn by `DiceTray.DrawPinnedRuleCards()`, called **after** the report window has ended, from both the
standalone tray and the bottom-tabs path. Inside the report they would be clipped to the panel and could
not be dragged out of it. Cleared on `Reset()` with the rest of the report.

Rental code (ImGui drawing), implemented plainly per Kai's instruction — *"Keep it basic for now, since
I'm going to work on ui later."*

### Verification

- Full `dotnet build` clean; engine suite **4,564 passed** (was 4,551 — 13 new); app suite **8,800**
  passed, unchanged; headless smoke exits 0 with the expected result line.
- **8 sabotage checks, 8 caught**, each by the test that claims it:
  1. Stop skipping already-dead picks → `AnAlreadyDeadPick_IsNotNamed`
  2. Ignore which bearer placed the grant → `TheLine_NamesOnlyThisBearersPicks`
  3. Announce *after* the token sweep → `TheWholeMessageDependsOnRunningBeforeTheTokenSweep`
  4. Hard-code "Banner" instead of walking the conferring list → `Musician_GetsTheSameLines`
  5. Forget the singular/plural → `OneCoveredUnit_ReadsAsSingular` (+4 others)
  6. Drop the rule name from the beat → `EveryLine_CarriesTheRuleNameForTheHover`
  7. `Headline` instead of `Toast` → `TheLine_DoesNotStopTheGame`
  8. Stop logging (beat only) → `TheSameLine_AlsoGoesToTheLog`

### Deferred, explicitly

- **Model-level revocation** (above) — Kai's call, "close enough for now".
- **Kai's "Z being what the buff did"** from the original request: the line names the rule, and the
  rule's effect is one hover away in OPR's own words. Spelling the mechanical effect out a second time
  inside the line would be a paraphrase of book text, which #420 exists to stop.
- **Only the first rule name in a line is hovered.** Every line this feature produces names exactly one.
  A line naming two would highlight both and hover the first.

## Outcome

Done. Patch note B44, build 0.59.
