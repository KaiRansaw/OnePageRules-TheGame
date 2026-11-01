# 433 - Per-system campaign event names, amounts, and Age of Fantasy's own cap

**Goal.** The campaign data that is shared when it should not be. Found by reading all four Campaign
Rules v3.6.0 books end to end at Kai's request, before starting work on mission difficulty - he asked
me to double-check his understanding of the army cap from the books rather than agree from memory.

**Status:** done, 2026-11-01. Engine only; no app change beyond the build stamp.

---

## 2026-11-01 - what the reading turned up

Kai's premise was right, verbatim in all four books: *"There are no point limits in campaign games, so
players may bring any number of their units to the match (must bring at least one unit), however their
total points may only be max. Xpts over the value of the army with the lowest points."* No roster cap;
the cap is on what you **bring**.

Checking the engine against that turned up three things it had wrong, all the same class of mistake -
**one system's data served to another**, which `CLAUDE.md` calls the single easiest thing here to get
catastrophically wrong.

### 1. Age of Fantasy was flying Grimdark Future's mission cap

`CampaignNumbers.AgeOfFantasy` was `= GrimdarkFuture`, a literal alias, commented "Identical to Grimdark
Future". It is not. Every number matches **except the two that matter most to a mission**: the AoF book
prints a **250pt** army cap and **50pts** per underdog point, against GF's 325 and 65.

So every Age of Fantasy campaign had a 30% wider mission cap than its own book allows, and would have
handed out one fewer underdog point at a given gap. Invisible in play, because a too-generous cap never
refuses anything - nobody would ever see the error, they would just be playing a slightly different game.

**The suite agreed with the bug.** `CampaignNumbersTests.EachBooksNumbers` asserted 325 and 65 for Age of
Fantasy, copied from Grimdark Future along with the alias, and `TheFourBooksShareTheirConstants` asserted
the two price lists were *equal*. A test that copies the production value is not a test. Both corrected,
and the equality assertion is now a **difference list** - AoF matches GF except these two - so adding a
number to one book and not the other fails.

Also added the cross-check that would have caught it alone: **`ArmyCapOverLowest / UnderdogPer` is
exactly 5 in all four books** (325/65, 125/25, 250/50, 100/20). That is almost certainly deliberate on
OPR's part, it means a difficulty scale built on the cap behaves identically in every system without
per-system tuning, and 250-with-65 or 325-with-50 fails it. Named `UnderdogPointsAcrossTheCap` because
#435 needs it.

### 2. AoF: Skirmish's treasury events were overpaying

Skirmish's book prints **Gold Stash = 15pts** and **Treasure Map = D3 x 15pts**. The other three print
25. `CampaignEvents.All` is one shared table with 25 hard-coded, so #432 - pushed the same morning -
shipped Skirmish campaigns paying 25.

The figure now lives in `CampaignNumbers.EventCredits` beside the rest of the per-system price list, and
the two entries' wording carries `{pts}` instead of a number, so one book's figure can never be baked
into a shared string. `CampaignEvent.Amount` keeps 25 as the canonical value.

### 3. Age of Fantasy campaigns were being told a Solar Flare had happened

The fantasy books rename **21 of the 36** events and leave 15 word for word. Explosive Trap is a *Magic
Trap*, Solar Flare a *Melting Curse*, a Robot Guide an *Animal Guide*, Credits Stash a *Gold Stash*.
`CampaignEvents` carried only Grimdark Future's names, with a comment saying the fantasy names "are not
worth a second table until AoF campaign has a screen of its own".

It has had one since #418, and #432 then put those names in front of players. **A note that defers work
until a condition is met does not notice when the condition is met** - worth remembering as a shape,
because the comment was accurate when written and wrong by the time it mattered.

New `Campaign/CampaignEventNames.cs`, same shape as `QuestEventNames` (#418) and `QuestItemSkin`.
AoF: Quest is included in `IsFantasy` even though its own campaign is the Campaign Builder's and never
reaches this table, so a stale save cannot read as science fiction.

## The two bugs the played-game test caught that nothing else would

`CampaignEventNameTests` (24 cases) covers the skin, and all of it would still pass if the runner never
consulted it. So `CampaignEventInAPlayedGameTests` gained a case that plays four real Age of Fantasy
campaign games and asserts **no science-fiction-only name reaches the log** - stated as a prohibition
rather than "a fantasy name appears", because which event fires is a dice roll and 15 of the 36 are named
identically in both books.

It failed on the first run, with `Campaign event 24: Anomalous Gale` in an Age of Fantasy game. The cause
was worth the whole test: `SystemOf` read the game system from
`GameProgressUtilities.TryGetProgress(...)?.Settings` and fell back to Grimdark Future - and **the
progress record does not exist yet at the start of round one**, which is exactly when the first event
can fire.

The same line had a second victim nobody had noticed: `TableSize` used the same path, so a
**Firefight** quarter event (41, 42) would be drawn on Grimdark Future's 72x48 table instead of
Firefight's 48x48 - a hazard covering a quarter of a table twice the real size. Both fixed by reading
`gameContext.Settings.GameSystem`, which is live and always present.

Neither was reachable by a unit test, because a test double has no progress record either and agreed
with the fallback. This is the third time in two days that "a test that calls the new code cannot tell
whether the game reaches it" has paid for itself (#424, #429/#430, now here).

## Coverage

`Tests/CampaignEventNameTests.cs`, 24 cases: the 21/15 split asserted against the map's own behaviour
rather than restated, both fantasy books agreeing on every name, both sci-fi books reading the canonical
table, spot-checks typed from the books, every name and wording ASCII-only (the font atlas bakes Latin-1,
and a name typed out of a PDF is exactly where a curly apostrophe arrives), no rename colliding with
another event's canonical name, names unique within each system, the treasury figures per book, the
wording carrying its own figure, no placeholder surviving the fill, and `Credits` leaving every
non-treasury amount alone. Plus the two corrected `CampaignNumbersTests` cases and the new ratio check.

**Sabotage-checked, 6 of 6 caught:** `IsFantasy` forced false (8 failures); Skirmish paying 25 (3);
Age of Fantasy's cap back to 325/65 (2); one rename entry deleted (2); the runner logging `fired.Name`
instead of the skinned name (**1 - the played-game test, and nothing else**); the points placeholder
never filled (2).

## Outcome

Age of Fantasy plays to its own 250pt cap, Skirmish pays 15pts for a Gold Stash, both fantasy systems
read their own event names, and a Firefight quarter event covers a Firefight quarter. #435's difficulty
bands are built on `ArmyCapOverLowest`, so this had to land first.
