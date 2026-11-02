# 435 — Mission Setup and the difficulty bands

**Goal.** Build the campaign's Mission Setup step as one screen (your force, their force, the map, go),
with a difficulty setting that sizes the bot rival's force — and wire #434's Underdog Bonus into it, since
the Bonus shipped the night before with nothing calling its seeding.

Spec: `claude/campaign-flow-spec.md` section 5, signed off by Kai 2026-11-01.

---

## Notes

### 2026-11-02 — built and verified

**What was already there, and what the gap actually was.**

Most of the mission-launch machinery existed on `StrikeTeamScreen`: a "Who is coming" checkbox block, a
`StartMission` that wrote a `.fdgmission` copy holding only the coming units, and `PrepareEnemyForMission`
which picked the enemy's force after the player's. The gap was not that the pieces were missing — it was
that **two of the three decisions this screen is supposed to show were being made invisibly**:

1. `CampaignBot.PickWhatToBring(enemy, cap)` was handed the books' mission cap and it fills whatever number
   it is given. So **every campaign mission was played at the hardest legal setting** — the equivalent of
   Legendary, every time, with nothing on screen saying so and no way to choose otherwise.
2. The map was whatever the lobby happened to be set to, three screens later.

And the launch walked through the Host Server dialog and the lobby's table settings, which Kai's flow spec
removes outright: *"clicking on start mission will open straight to the mission screen"*, *"The Table is
gone from the campaign flow."*

### The difficulty bands

**A house rule** — the campaign books have no difficulty setting at all. They have one mission cap and
assume a human opposite you who decides what to bring. Recorded as house rule B9 + the C-section note.

The player picks first (`PrepareEnemyForMission`'s existing doc explains why that order is the only one
that terminates), so their total is the floor and the rival's legal window is `[yours - cap, yours + cap]`.
The four settings sit at **-5, 0, +3, +5 underdog points** of gap.

**Measured in underdog points, which is the whole trick.** `ArmyCapOverLowest / UnderdogPer` is exactly 5
in all four books (325/65, 125/25, 250/50, 100/20) — the constant #433 named `UnderdogPointsAcrossTheCap`
for this. So the extremes land exactly on each book's own cap in every system with **no per-system table**
and no invented arithmetic. That identity is what `TheExtremeBandsAreExactlyTheBooksOwnCap` pins.

**Deliberately uneven.** Even quarters would put "about equal" a third of a cap *below* the player, and
equal forces are not an arbitrary point in that window — they are what the core rules ask for, so
Intermediate sits exactly there. Below equal there is only one setting worth having ("give me a comfortable
win"); above it there are two.

**Difficulty and the Underdog Bonus turn out to be the same number read twice.** A band expressed in
underdog points *is* what #434 hands the player at that setting: Legendary gives the rival a full cap more
and gives the player five points to spend on it; Intermediate gives neither side anything. So the scale is
self-balancing rather than a flat handicap — and the bot-never-gets-points house rule is what makes the
easy end easy, since at Beginner the rival is five points behind and is given nothing for it.
`EachBandPaysTheUnderdogPointsItsNameImplies` pins the pairing in all four books, so a band changed outside
`UnderdogPer` cannot break it silently.

**Explicitly NOT Quest's difficulty.** GF: Star Quest and AoF: Quest have a published 1-4 difficulty whose
wave budget is 25% of the party's points per level. That is right for Quest and wrong here: this
campaign's cap is flat, so at 2,000 points a percentage band proposes forces the books refuse. The four
*names* are shared so a player does not learn two vocabularies; nothing else is.
`NoBandEverExceedsTheCap_AtAnyArmySize` is the guard, and sabotaging a band into a percentage fails it.

**The index line for this item described a "Custom Point Ranges" toggle for percentage bands.** That was
the pre-spec design; the 2026-11-01 conversation dropped it for the reason above. Index line corrected.

### A bug in the picker, found by giving it a low target

`PickWhatToBring` took the first unit **unconditionally** — `if (bringing.Count > 0 && ...)` — sorted
most-experienced-first, so "at least one unit always comes" meant "the rival's biggest veteran always
comes". Harmless while the number was always the cap; the moment an easy setting hands it a small target
it fields a 300-point character against a 100-point force. Now the target binds on every unit and the
fallback takes the **cheapest** unit it owns. (`WhenNothingFits_TheCheapestUnitComes_NotTheVeteran`.)

Randomize is the same method with the order shuffled — Fisher-Yates driven by the game's own
`Func<int,int> roll` delegate rather than a `Random` of its own, so a seeded run reproduces. Null delegate
keeps the deterministic veterans-first order, which is what an unattended run and a replayed save want.

### Wiring the Underdog Bonus — not where it was expected to go

#434 left seeding to "the Mission Setup screen, which knows both forces". It turned out the screen is the
wrong place: it knows the two forces but **not the slot ids they end up in**, which are assigned in the
lobby. So `UnderdogPool.SeedFromSlots` seeds from the seats themselves at launch, called once from
`FDGServer.BuildContextAndLaunch` behind the same fresh-launch flag as the creation rules (a resume brings
the pool, and what has been spent of it, back with the progress record).

Two consequences worth recording:

- **The pool is now a property of what is on the table, not of what a screen intended**, and the seeding
  step lives on the launch path where it cannot be left unwired a second time.
- **Which seats are bots is read from the controller, not from `StrikeTeamInfo.IsBot`.** Those can differ —
  a campaign army opened hot-seat by a second human, a player's army handed to a bot — and the house rule
  is about whether a human can judge when to spend a point, which only the controller answers.
  `slot.Controller is ComputerPlayerController` is also how `FDGServer` already identifies bots for the
  Game Master, so there is one answer to that question rather than two.

### Launching without the lobby

Start Mission opens a host silently (`HostModal.TryCreateSilently`, which goes through the same
`CreateServer` so the bind-failure check, UPnP, listing and config write stay in one place), seats both
campaign armies the way the campaign screen already did, and sets `LobbyScreen.PendingAutoLaunch`. The
lobby's `Draw` consumes that **before** `DrawScaled`, so the lobby is never drawn on the way through — one
frame of it is still a visible flash of a screen the flow says is gone.

**Still launching through the lobby, deliberately.** A second launch route would be a second place for
army loading, bot seating, the launch gate and the save hook to drift out of step. The #153 catalog-warning
modal is skipped (a campaign army was built in this app from a bundled book and was legal enough to start
a campaign with, and the campaign flow has no screen for that dialog); hard blockers still stop it, and
when they do the player lands in the lobby with the greyed LAUNCH button, which is the only thing that can
explain what is wrong with an army.

A failed port falls back to showing the Host Server dialog, which is the only recovery there is for that.

### Kai's "are we loading just the units we chose?"

Settled and now visible. `WriteMissionFile` keeps only the ticked, fieldable units, and the screen shows
both sides' totals against the cap so the number on screen is the number that deploys. Every sheet in the
copy keeps its id and its `TeamFile`, so the after-game write-back lands on the real campaign army.

### Sabotage checks: 9, one of which exposed a real gap in the tests

| Sabotage | Caught by |
|---|---|
| Drop the `SeedFromSlots` call | `APlayedCampaignMission_GivesTheSmallerPlayerTheirUnderdogPoints` |
| Let a bot hold a pool | `APlayedCampaignMission_NeverGivesTheBotUnderdogPoints` |
| Seed a non-campaign army | `AnOrdinaryGame_HasNoUnderdogBonusAtAll` — **after a test fix** |
| Flatten every band to equal | `TheBandsRiseAcrossTheWholeScale` |
| Ignore the target in the picker | 4 tests |
| Restore the unconditional first pick | `WhenNothingFits_TheCheapestUnitComes_NotTheVeteran` |
| Make the shuffle a no-op | `Randomize_PicksAcrossTheRoster_AndStillRespectsTheTarget` |
| `Unset` means Legendary | `UnsetMeansAnEvenMatch_NotLegendary` |
| A band as a percentage of your points | 4 tests, incl. `NoBandEverExceedsTheCap_AtAnyArmySize` |

**The third one walked past the suite on the first attempt**, and it is worth writing down because the
cause is a shape that has now cost this project several times. The ordinary-game test left the two test
armies at their own point costs — which happen to be close together — so it passed whether or not the
campaign guard existed. **The thing keeping that log clean was the armies being similar, not the guard.**
The fixture now forces both games a full mission cap apart, so only the guard can keep it quiet, and the
sabotage fails.

Also worth noting: the first version of the played-game test asserted 5 underdog points for a 350-vs-1000
mission and failed with **10**. The code was right — 650 pts at 65 a point *is* 10 — and the test had set
up an illegal mission. The Bonus rule has no ceiling of its own; keeping forces legal is Mission Setup's
job. The test now uses the widest legal gap (675 vs 1000 = one full cap = exactly 5).

### Verification

- Full `dotnet build` clean; engine **4,645 passed** (was 4,629 — 17 new, see below), app suite green,
  headless smoke exits 0.
- 14 new `MissionDifficultyTests` (bands + picker), 3 new `UnderdogInAPlayedGameTests` (the wiring).
- The one red in the full run is `NetworkedGamePlaythroughTests.HostAndTwoClients_PlayAWholeGame_ToAResult`,
  the known flake carried from the previous session (4 red / 4 green there, uncorrelated with any change).
  Checked rather than assumed, because this slice edits `FDGServer`: it passes in isolation, and
  `SeedFromSlots` is a no-op for that test's ordinary armies (no `StrikeTeam`, so every slot is skipped and
  the entrant count never reaches 2).

### 2026-11-02 (later) - renamed, and the rewards settled

**The names collided with Quest, and that was the whole problem.** The four settings were
Beginner/Intermediate/Expert/Legendary, borrowed from the Quest Campaign Builder so a player would not
have to learn two vocabularies. It did the opposite. Kai lost track of which difficulty belonged to which
game, and said so plainly: *"i think i had multiple idea in my head at once and now im lost."* Reading it
back, "Legendary" meant **200% waves** in Quest and **one mission cap above you** here - one word for two
unrelated mechanics, which made them read as one mechanic. Renamed on his call to
**Easy / Normal / Hard / Brutal**; Quest keeps its published four.

A test now pins that none of the four shares a name with `StarQuestMissionRules.DifficultyNames`, so the
collision cannot come back by someone "tidying up" the vocabulary.

**Save compatibility was a real hazard, not a theoretical one.** The enum is stored by NAME
(`RuleJson.Options` carries a `JsonStringEnumConverter`), so a campaign army written by the build pushed
an hour earlier would have failed to deserialize **entirely** - not lost its difficulty, lost the whole
file. `MissionDifficultyJsonConverter` reads the old names, the new names and the numbers, and anything
unrecognised as `Unset` rather than throwing.

**And it had to go on the PROPERTY, not the enum.** System.Text.Json resolves a property-level
`[JsonConverter]` ahead of `JsonSerializerOptions.Converters`, so the shared `JsonStringEnumConverter` won
over the type-level attribute and threw anyway. Found by the test failing, not by reasoning about
precedence - worth recording, because the attribute on the enum *looks* like it is doing the job.

### The rewards, and how two answers pinned each other

Kai's answers, 2026-11-02: **(a)** scale by the setting's level, the way the Quest Campaign Builder does
(*"Get 5c times the chosen Difficulty level"*); **Normal is the normal payout**, and *"Easy is scaled
down, so rewards are scaled down as well"*; **the loss payout does not scale**; difficulty stays **per
mission**; **Challenge Bonuses yes, after the campaign screens**.

Those first two look independent and are not. If Normal is level 2 and pays the book's own figure, then
one level is worth half of it - so the scale falls out with no number invented anywhere:

| | Win treasury (GF) | VP |
|---|---|---|
| Easy | 75 | 1 |
| **Normal** | **150 - the book's own figure** | **2** |
| Hard | 225 | 3 |
| Brutal | 300 | 4 |

So a campaign played on Normal earns exactly what it earned before any of this existed, and an existing
campaign (difficulty `Unset`) resolves to Normal and is untouched. **Easy is the one setting that pays
less than the books do**, which is the only place this scale is not purely additive, and it is what Kai
asked for.

**The loss payout stays flat at every setting**, and the reason is worth keeping: the books pay the loser
roughly double the winner (300 against 150 in GF) *so that whoever is behind catches up*. Multiply that by
four on the hardest setting and losing becomes the profitable move - bank 1,200 for being beaten, field a
far bigger army next mission, and the books' rubber band pulls backwards. `MissionDifficulty.LosePoints`
exists as a method purely so that reason sits where somebody would otherwise "finish the job".

**Read off the army's own strike-team block, not passed in.** `CampaignRules.AfterGame` already had the
`StrikeTeamInfo` in hand, so no caller can pay the wrong rate by forgetting an argument - the same
reasoning as reading the bot flag from the controller rather than from a parameter.

**Left flat deliberately, and flagged rather than guessed:** the special-objective bonus and unit XP. Kai
specified the win and loss payouts; the objective bonus can be completed in a loss, so scaling it would
contradict "a loss does not scale", and XP is earned per unit action rather than per mission. Say so
rather than extend his answer.

### A sabotage check that passed, and a documented guard that was protecting nothing

`WinPoints` multiplies by the level and *then* halves, and the doc comment said why: halving first would
cost a point per level on a book with an odd payout. **Swapping the order passed every test.** All four
books print an even win payout (150, 80, 150, 60), so the two orders agree exactly and the "guard" was a
claim with nothing behind it. There is now a test using a synthetic 75pt book, where multiply-first keeps
Normal on 75 and halve-first drops it to 74 - the one thing this scale must never do. Third time this
class of problem has shown up (see B46's diff keys, B47's hero report); the pattern is **a comment
asserting a property that no test exercises**.

### Verification (second pass)

- Full `dotnet build` clean; engine **4,662 passed**, app **8,804 passed**, headless smoke exits 0.
- 17 more tests (29 total in `MissionDifficultyTests`): the reward table, Normal matching every book, the
  flat loss, the odd-payout ordering, the old-name reader, the round trip, the Quest name collision, and
  **five that drive the real `CampaignRules.AfterGame`** rather than the arithmetic - because a perfect
  scale that nothing consults is exactly how #434 shipped.
- **7 sabotage checks, 7 caught** (one after adding the odd-payout test): scale the loss; halve before
  multiplying; anchor the scale on Easy; ignore difficulty in the payout; drop the VP scaling; drop the
  old-name reader; reuse a Quest name.

### Not done, deliberately

- **Challenge Bonuses** - the four modes' own official difficulty, in the Solo & Co-Op Rules v3.6.0 books
  (the AI activating D3 units; +1 to hit when tied; +1 to hit and +1 Defense when losing). Kai: **yes,
  after the campaign screens.** It is a *behaviour* buff, so it stacks with the force sizing rather than
  replacing it. Its own work item when it comes.
- **Difficulty stays per mission**, not locked per campaign. Kai's call, knowing it is gameable now that
  rewards scale (play Easy, win, bank the money). Play it first; lock it only if it turns out to matter.

### Not done, deliberately
- **No campaign-screen panel for the difficulty** — it is set on Mission Setup and remembered on the
  player's `StrikeTeamInfo`, which is where the choice belongs. The campaign screen's right panel (spec
  section 2) is a separate slice.
- **Sections 1-4 of the flow spec** (two main-menu campaign buttons, the per-mode campaign screen, the
  locked Army Forge, unit names) are untouched. This slice is section 5 only.
- **Mission Setup is reached from the existing `StrikeTeamScreen`**, not from the new per-mode campaign
  screens, because those do not exist yet. Nothing about the screen depends on how it is reached.

## Outcome

Mission Setup is live, the Underdog Bonus does something in a played game, and the four settings now pay
different rewards under names that do not collide with Quest's. Builds 0.64 and 0.65.
