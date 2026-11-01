# 430 — The special objectives are actually scored

**Goal.** Score the six special objectives of Campaign Rules v3.6.0 at the end of a mission and pay the
treasury bonus. From `claude/fdg-todo.md`: *"scoring a special objective during a game"*.

**Status:** done 2026-11-01.

---

## 2026-11-01

### The gap

The objective was **rolled and then never checked**. The campaign screen showed the player what they had
drawn, the mission was played, and nothing ever asked whether they had done it — so
`CampaignNumbers.SpecialObjectiveBonus` could not be earned at all. The whole step existed except its
last line.

### Four are on the table; two are not

| Objective | The book | Where the answer is |
|---|---|---|
| **Headhunter** | kill one or more enemy Heroes | the campaign sheets, which have counted hero kills per unit since the campaign existed |
| **Dominator** | one unit fully within each table quarter at the end | model positions |
| **Intimidator** | most of your units inside the enemy deployment zone at the end | the zones — which needed **#429** |
| **Strategist** | control most objectives at the end | the markers' own owners |
| **Hexer** | successfully cast four or more spells by the end | **nowhere** — a resolved cast leaves no trace |
| **Annihilator** | be the first player to destroy an enemy unit | **nowhere** — the finished table records that units died, never in what order |

So two of the six are not readable from a finished game at all, and that is what made this engine work
rather than a UI pass: they are tallied **on the units' own campaign sheets** as the game runs -
`StrikeUnit.SpellsCast` from the one point in `CastSpellStage` that knows a cast succeeded, and
`StrikeUnit.DrewFirstBlood` from `FirefightCampaign.OnUnitDestroyed`, which already writes that sheet at
exactly that point.

### Why the sheets and not the game's progress record

The first version put both on `GameProgressData`, and that was wrong for a reason worth writing down:
**the progress record replicates to every client when it is written.** Counting a spell there meant a
mid-activation replication of the whole record on every cast of every ordinary game, to feed an objective
only a campaign can have — a real cost for nothing. The first-blood stamp was worse: it wrote from inside
the attack pipeline.

The sheets have neither problem. They ride the unit, the kill path already writes them, a non-campaign
unit has none (so an ordinary game pays one null check), and the host does both the scoring and the
write-back so nothing needs replicating at all. There is a test for the free case as well as the counted
one.

`AfterGame` clears both with the kills, so a recovery save - which serializes the live table, sheets and
all - cannot carry one mission's spell count or first-blood stamp into the next.

### Judgement calls

- **Intimidator with more than two players.** "The enemy deployment zone" is singular and the book
  assumes two armies. Read as **any** opposing team's zone: a unit standing in one of three enemy zones
  has done what the objective describes, and picking one enemy to count would make the result depend on
  an arbitrary choice. In `claude/house-rules.md`.
- **"Most" is strictly more than anyone else.** A tie on objectives is not control, and half your units
  in the enemy zone is not most. Both are what the English says; both were worth a test, because the
  off-by-one reading is the natural one to write.
- **"Fully within" really means fully.** A unit with a foot in two quarters holds neither, so a single
  straddling unit cannot be credited a quarter it is half in.
- **Can't-be-scored is reported, not failed.** An objective this scorer cannot decide (an old save with
  no deployment zones; a name not in the book's table) comes back with a Detail saying so rather than
  claiming the player failed it. "You did not do it" and "nobody knows" are different answers and the
  difference belongs in the log the player reads.

### Where it runs

`StrikeTeamFiles.WriteBack`, straight after `AfterGame`: the result is logged either way, the bonus is
paid through `CampaignPreGame.CompleteSpecialObjective` so the treasury write stays in one place, and the
objective is cleared so a new one is rolled before the next mission.

### Tests

`CampaignObjectiveScoringTests` — 22 cases: Hexer's bar and that it counts only your own casts;
Annihilator going to whoever holds the stamp and the loser being told an opponent took it rather than
that nothing died; Hexer summing the whole army's casters; Headhunter off the
sheets, including a hero killed by a unit that then died itself; Dominator needing all four quarters,
a straddling unit holding none, enemy units not counting, and the quarters being drawn on **the table the
game was actually played on** (Firefight's 48x48 against Grimdark Future's 72x48 — the wrong one puts
the dividing line in the wrong place); Intimidator's "most", your own zone not counting, and the
could-not-be-scored path; Strategist's tie, unheld markers, and holding nothing; plus a guard that every
objective in the book's table is one the scorer knows, so a renamed entry cannot silently become
unearnable. The two in-game counters are each pinned by a test that runs the REAL code path -
`CastSpellStage` itself, and `OnUnitDestroyed` over a store of campaign units - and sabotaging either
fails exactly its own test. `OnUnitDestroyed_StampsTheFirstKillOfTheGame_AndOnlyTheFirst` also pins the
mistake the obvious implementation makes: stamping the killer on every kill rather than only the first.

## Outcome

Done. The bonus can be earned, and the one objective that cannot always be decided says so instead of
guessing.
