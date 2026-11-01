# 427 — No Permadeath, as a toggle when creating a campaign

**Goal.** The Campaign Rules v3.6.0 "No Permadeath" option. Kai's answer when asked where it should
live, 2026-11-01: *"Have it be a toggle when creating a campaign."*

**Status:** done 2026-11-01.

---

## 2026-11-01

### The rule

Identical in all four books: a unit that would be killed *"is not killed, but instead loses all of the
XP it earned for that match, and must sit out of the next D3 matches"*.

Three things have to hold, and each is its own way to get it wrong:

1. the unit stays in the army;
2. it banks **nothing** from that match — not even the XP for what it killed before it fell, or the
   match would be a net gain;
3. it is unavailable for a counted number of missions, rather than permanently or not at all.

Everything already earned survives: levels, traits, injuries, talents and the points they cost. That
is the difference between a safety net and an undo button.

### Where it lives

- `StrikeTeamInfo.NoPermadeath` — fixed when the campaign is created, as Kai asked. A campaign does not
  change its mind halfway through, and the option changes what a loss means.
- `StrikeUnit.MissionsOut` — missions still to sit out. Always zero in a permadeath campaign, where the
  unit is simply gone.
- `CampaignRules.Kill` — the one place a killing blow is resolved, for both a regular's 1 and a Hero's
  Dead, branching on the option.
- `CampaignRules.CanBringToMission` / `CannotBringToMission` — the gate, and the reason to show.

### Two orderings that matter

**The count-down runs before this match's casualties.** A unit sitting out is not on the table, so it
never appears in a match's results; the top of the next after-game is the only place it can move. Run
the other way round, a D3 of 1 would expire the same evening it was earned and the unit would never
actually miss a mission.

**An empty `Bringing` list means "the whole army comes"**, which is the state a brand new campaign army
is in — so the recovering filter had to go into `StartMission`'s selection, not only into the
checkboxes. Ticking a box is not what keeps a unit at home.

### Judgement call

A Hero reaches death through "Dead" on the injury table, and **the knock-out replaces that result**
rather than stacking an injury on top of it: no injury trait, no points off. The book replaces the
death, and Dead is the death. Recorded in `claude/house-rules.md` section C.

### Tests

`FutureOfDarkGrimness/Tests/CampaignNoPermadeathTests` — 14 cases: the permadeath baseline beside it,
survival + sitting out, the D3 over all six faces, every XP from that match lost including kills, what
was already earned kept (XP, level, traits, cost), the Hero's Dead becoming a knock-out with no injury,
the count-down over three matches, the knock-out keeping its full D3, and nothing ever sitting out in
an ordinary campaign. Sabotaged two ways (XP let through; D3 one short): 10 of 14 fail.

### Surfaced in the screen

The toggle on the New view with a line of plain English either way; "(No Permadeath)" in the camp
header so the player knows which campaign they are in; "RECOVERING - out for N more mission(s)" on the
roster row; and the unit greyed with its reason under "Who is coming".

## Outcome

Done.
