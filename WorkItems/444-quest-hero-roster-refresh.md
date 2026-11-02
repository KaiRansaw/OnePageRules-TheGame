# 444 — Refresh both Quest hero rosters against the live official Army Forge

**Goal.** #443 audited the 288 army books and explicitly did not cover the two hero rosters. Cover them:
Star Quest's 205 heroes (official id 9) and AoF: Quest's 161 (id 7). These are the **player's own side of
the table**, so stale data there shows up faster than in an enemy book.

Kai asked for this immediately after #443 landed.

---

## Notes

### 2026-11-02 — done, pushed (build 0.62)

**The unit data was already current.** 54 factions / 205 heroes and 46 / 161, and **not one hero's**
points, quality, defense, tough, weapons, gear or rules changed. The totals came back exactly as the
importer expected them, which is itself the check that the right game system id was used — id 9 gives 205
playable heroes, id 10 would have given a roster of enemy profiles that loads and plays.

**Eight values were stale, and the report said nothing had changed.**

| Where | What |
|---|---|
| Shadow Stalkers, AoF: Quest | **Combat Possession shipped TRUNCATED** — *"Pick up to two friendly units within 12", which"*, cut off mid-sentence. It is a movement buff: +2" Advance, +4" Rush/Charge, once. |
| Shadow Stalkers, AoF: Quest | **Hex Banshee 15 → 20** in three hero upgrade packages |
| Jackals (SQ), Vampiric Undead (AoFQ) | **Mind Control**: *"Units may only be moved once per round"* → *"**Each** unit may ..."* |
| Soul-Snatcher Cults (SQ) | **Deep Hypnosis**: same rewording |
| Vampiric Undead (AoFQ) | **Seductive Invocation**: same rewording |

The Hex Banshee move is the same upgrade that went 35 → 40 in the Shadow Stalkers **army books** in
#443 — so the two halves of that faction moved together and we had caught only one of them.

### The report was blind, exactly like BookDiff was

`--import-starquest --diff` printed **"No hero changed in any faction that exists in both files"** while
those eight values moved underneath it. It compared **heroes only** — not a faction's own rule text, not
its spells, not its upgrade packages. A report that returns green without having looked is worse than no
report, and this is the second one found in two days (#443 was the first).

Fixed: it now reports rule and spell text added, removed or reworded, and upgrade packages added, removed
or changed with both shapes printed. Re-run against the pre-refresh backups it names all eight.

Also: the report was titled **"Star Quest heroes"** for both rosters. Corrected to the roster actually
imported, read off the `Source` line the importer stamps. That one matters more than it looks — the two
rosters are different datasets behind the same index slug, and a mislabelled report is how one gets
reviewed as the other.

### A real gap this surfaced, logged not built

OPR's rewording is a **clarification of a cap we do not implement at all**. The shipped Mind Control
implements `oncePerActivation` — the cap on the *caster* — and nothing implements *"each unit may only be
moved once per round in this way"*, the cap on the **target**. So today two casters, or the same caster
across two activations, or Mind Control plus Deep Hypnosis, can shove the same enemy unit around
repeatedly in one round.

It touches `Mind Control` (a rule, all six systems) plus the `Deep Hypnosis` and `Seductive Invocation`
spells. The shipped description even omits the sentence. Needs a token on the moved unit, cleared at
round end, checked before the move — engine work across the effect pipeline, so it is **its own slice**
and is in `claude/fdg-todo.md`, not bundled in here.

### Verification

- Full `dotnet build` clean; app **8,804 passed**; engine **4,607 passed**; headless smoke exits 0, and
  both Quest systems play a full mission to a result with the refreshed rosters (Star Quest 30 rounds,
  AoF: Quest 15).
- Hero counts re-checked against the importer's own expectations after the write (205 and 161), and the
  previously truncated Combat Possession text read back in full.
- The improved report was re-run against the pre-refresh backups and names every one of the eight
  changes — which is the only real check that the fix does anything.

### One thing left honestly unresolved

`HostAndTwoClients_PlayAWholeGame_ToAResult` is **intermittent in the full engine suite**: twice red at
~48s and twice green in this session, with nothing between the runs that it touches, and reliable on its
own at ~1m25s. It is marked `NonParallelizable` to match the other networking fixture, but **that is the
conventional first move, not a proven fix**, and the comment in the file says so rather than implying the
problem is solved. Raising `StallTimeout` is explicitly rejected there: that threshold exists to report a
real hang quickly, and padding it to absorb a scheduling hiccup trades away the one thing it is for. The
next step is to capture a red run's own failure message, which already prints the table's last progress.

## Outcome

Done. Patch note B47, build 0.62. Engine `202a9a0`, superproject `b93882a`.
