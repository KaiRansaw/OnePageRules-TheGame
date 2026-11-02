# 434 — The campaign Underdog Bonus

**Goal.** Build the books' Underdog Bonus, which has been carried as an unused number since mission setup
was written. Prerequisite for the difficulty bands (#435) and for the whole campaign flow spec'd on
2026-11-01: without it, an enemy allowed to outgun you is just a mission you lose.

---

## Notes

### 2026-11-02 — built, verified, pushed (build 0.63)

**The rule**, worded identically in all four Campaign Rules v3.6.0 books:

> Any army that has a point value lower than at least one other gets an underdog bonus. You get 1
> underdog point for every 65pts difference between your army and the one with the highest value. Each
> underdog point can be spent to modify all dice in a friendly or enemy roll by +1 or -1, but only one
> point can be spent per roll.

65 in Grimdark Future, 25 in Firefight, 50 in Age of Fantasy, 20 in Skirmish — each exactly a fifth of
that book's mission cap, so **the widest legal gap between two armies is always worth exactly five
underdog points.** Pinned per system, because a shared rate is the mistake this codebase keeps making.

**Campaign only.** The core rules ask for *"two armies of equal point cost"* and have no underdog rule at
all. So a standard game has an empty pool, nothing is ever offered, and the seeding call belongs on the
campaign's mission-setup path rather than in game start-up.

### What already existed, and what didn't

`CampaignPreGame.UnderdogPoints` was built and tested when the rest of mission setup was, carrying a note
that said exactly what was missing: *"the arithmetic is here and tested so the rule is recorded
correctly; nothing offers the spend yet."* This slice is the spend.

### How it is built

**The pool** rides `GameProgressData` as `Dictionary<int,int>` (slot id → points left), following
`Horde`'s score and #429's deployment zones:

- carried forward by `WriteProgress`, so the per-turn snapshot does not erase it mid-mission
- with a **pending hop**, because the pool is computed at mission setup, *before* the main phase writes
  the first progress record — without it the pool would never reach the game at all
- keyed by slot id, not `PlayerID`, because a Guid-keyed dictionary needs a converter to survive a save
- exposed read-only on `IGameProgress.UnderdogPoints` so the front end can show it

**The spend** is `UnderdogSpend.ConsumeNet`, called from the four roll kinds: **Hit**
(`DetermineHitRollStage`), **Save** (`DetermineSaveRollsNeededStage`), **Morale** (`MoraleUtilities`) and
**Cast** (`CastSpellStage`).

**Modelled closely on `TargetMarkerSpend`**, which already does "ask the owner whether to spend a
resource on this roll" — same call shape from inside a roll stage just before the threshold is computed,
same skip-when-nothing-to-spend, same return-a-net-the-stage-folds-in. A second mechanism for the same
job would be a second thing to keep right.

- **+1 on the underdog's own roll, −1 on an enemy's.** Both returned as a delta to the *rolling* unit,
  which is the sign convention the stages already use for granted modifiers. The enemy half is the one
  easy to leave unbuilt, since the happy path never needs it — so it has its own test.
- **The offer goes to the seat holding the points, not to the roll's owner.** The natural way to write
  this is wrong half the time and silently drops the enemy-roll half of the rule.
- **One point per roll, and the cap lives in `UnderdogSpend`** — the pool knows how many a seat has, the
  roll knows how many it may take, and only that method sees both.
- **"Don't spend" is the first option**, the opposite of `TargetMarkerSpend`'s spend-all-first ordering
  and for the opposite reason: a marker is use-it-or-lose-it, an underdog point is a scarce decision.
  Without this, every headless sweep burns all five points on the first five rolls of every mission.
- Skipped while fatigued in melee, like every other modifier there: only unmodified 6s hit, so a point
  spent would buy nothing.
- Offered on the cast roll *after* the cost is spent, so a browsed-and-cancelled spell never burns one.

### The house rule, and where it is enforced

**Kai's call, 2026-10-30: underdog points go to the player only, never a bot.** The books give them to
any army worth less than another, so this is a deliberate deviation — a bot cannot judge which roll is
worth a point, and five invisible free +1s would make a mission harder in a way the player could neither
see nor plan around.

Enforced in `UnderdogPool.Seed`, **not at the prompt**. A bot with no pool has nothing to spend, so no
roll stage needs to know what a bot is and no future caller can reintroduce the behaviour by forgetting a
check.

### Two sabotage checks walked past the suite, and what that exposed

Worth recording, because both were real gaps in the tests rather than near-misses:

1. **Removing the one-point-per-roll cap entirely kept all 21 tests green.** With one player plus a bot,
   only one seat ever holds points, so the loop returns after the first spend either way. The cap is only
   observable with **two non-bot seats both holding points** — a 3+ army campaign, which is what the
   books are written for (*"campaigns are played with two or more players"*). There is now a three-army
   test, and the sabotage fails it.
2. **Removing the empty-pool guard also kept everything green** — because the per-seat "no points left"
   check inside the loop still refuses. So that guard is a **fast path, not the correctness**. Rather than
   bend a test around it, the comment now says what it is, so nobody later mistakes it for the thing
   holding the behaviour up.

### Verification

- Full `dotnet build` clean; engine **4,629 passed** (was 4,607 — 22 new), app **8,804 passed**,
  headless smoke exits 0.
- **6 sabotage checks**: give the bot a pool → `ABotIsNeverGivenPoints`; ask the roll's owner →
  `TheOfferGoesToTheUnderdog`; always +1 → `OnAnEnemyRoll_TheUnderdogGetsMinusOne`; drop the cap →
  `OnlyOnePointPerRoll_AcrossTwoSeatsThatBothHavePoints`; spend-first ordering →
  `NotSpendingIsTheDefault`; and the empty-pool guard, which is documented as a fast path rather than
  pinned.

### Not done, deliberately

- **Seeding is not wired to a real mission yet.** `UnderdogPool.Seed` takes the forces as brought and the
  bot flag, and nothing calls it: the campaign's Mission Setup screen — where the player picks their
  force and then the enemy's — is the thing that knows both, and it does not exist yet
  (`claude/campaign-flow-spec.md`). So in a played game today the pool is always empty and nothing is
  offered. **That is the next step, and it is the first thing to build on the campaign screens.**
- **No UI for the pool.** `IGameProgress.UnderdogPoints` is there for it; the prompt itself goes through
  the ordinary `StringSelectionRequest` path, so it already works in the CLI and the GUI resolver.
- **Whether a prompt per roll is the right feel is untested in play.** It follows the existing
  precedent, which is the honest first version, and it costs nothing once the pool is empty. If it turns
  out to be noisy, the alternative is an arm-the-next-roll button instead of a prompt — worth playing
  before deciding.

## Outcome

Done bar the wiring. Engine `326c4ed`, superproject `53e2647`. Build 0.63.
