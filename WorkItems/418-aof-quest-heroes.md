# 418 - AoF: Quest's player side

**Goal:** #417 imported AoF: Quest's 46 enemy books and left the player side missing, which is why the
system stayed unselectable. Give AoF: Quest heroes: its own roster, its own classes, its own Hero
Builder and campaign screens, and a party that is stamped as its own system.

**Raised by:** Kai, 2026-10-31 - picked "AoF: Quest heroes" as the next build, because it is the last
whole game mode with nothing playable in it.

---

## Notes

### 2026-10-31 - the flavour pass, and the coverage hole it exposed (B14)

The last of the vocabulary: the **36 random events**. Same two-dice table in both Campaign Builders -
same codes, same order, same effects - with **21 of 36 renamed** and 15 word for word identical.
`QuestEventNames` holds 17 of the renames; the other four (61 Elixir Pouch, 63 Bomb Bag, 64 Gold Stash,
65 Potion Box) are treasure-table entries, so `QuestItemSkin` owns those names and this skin defers to
it - one owner per string. The four events that hand something over now carry `{item:...}` / `{money:N}`
placeholders in their effect text, filled per game, so the sentence a player reads is the item that
lands in the pack. `QuestChallenges` gained `EventName` / `EventsFor` / `EventTextFor` taking an
`EGameSystem`, and all 10 player-facing reads pass `context.Settings.GameSystem`.

App side: `GameMasterHub.GameSystem` exposes the running game's system so `GameMasterPanel` can cache
per-system event, label and item lists, and the Hero Builder reads "g"/"gold" from `QuestItemSkin`.

**The lesson, which is worth more than the slice:** these names were wrong since B12 and no test could
have caught it, because **AoF: Quest Horde had no played-game coverage at all** - the whole suite ran
Quest as Star Quest. Everything that makes an AoF: Quest game an AoF: Quest game travels on one field,
`Settings.GameSystem`, and nothing was checking a real game carried it. Added
`AoFQuestHordeIntegrationTests`: three seeds, every optional challenge forced on, 19-31 rounds each,
mirroring `StarQuestHordeIntegrationTests` down to the seeds so a failure in one and not the other
isolates the system. It also reads the party's items back and refuses Star Quest names - vacuous today
(the bots never walk onto a search token) and written up as such in the file, but a real guard the day
the AI collects.

Engine 4,043 -> **4,055** (+9 event-name pins, +3 played games), app 8,750 unchanged. Build **0.28**.

### 2026-10-31 - the roster: 46 factions, 161 heroes, from id 7

Army Forge serves AoF: Quest as **two ids under one index slug**, exactly as it serves Star Quest, and
the payloads are indistinguishable:

| | id 7 - this import | id 8 - #417's enemy books |
|---|---|---|
| Beastmen roster | **5 heroes** | 20 units |
| Sample profile | Ndoli Beast Lord 45pts Q6 D6 | Ndoli Warrior 35pts Q4 D4 |

`StarQuestHeroImporter` was a static class with `Slug` and `GameSystem` as private consts; it now takes
a `QuestSystem` record (slug + id + labels) and `--import-starquest` takes `--quest-system`, defaulting
to Star Quest so #415's command line still means what it did. Ran against id 7: **46 of 48 factions
(two are OPR's empty placeholders), 161 heroes, 276 upgrade packages, 2,935 priced options.**

### 2026-10-31 - the static singleton was the real blocker, not the data

`StarQuestData` held one roster behind a `_loaded` flag with the asset folder and both file names
inlined, so **one process could only ever hold one Quest dataset** and the first screen to touch it
decided which system the whole app meant. Same shape as `GameWideConstants` and `HordeRules`, and the
same failure mode: nothing announces it.

Now `QuestDataSet` - an instance per system, cached, lock-guarded, carrying its own folder, file names
and faction-group map. `StarQuestData` survives as a thin Star Quest view over it (about 30 call sites
and three fixtures call it), documented as "new code should take the dataset or the system".

Everything else that assumed one system, fixed in the same pass:

- **`HeroBuilderScreen.GameType` was a `const "GF Star Quest"`** that five other files reached into -
  the save folder, the campaign screen, the lobby's party picker, the headless party loader and a
  fixture. Now `QuestGameTypes.For(system)`, and the screen takes the system in its constructor.
- **The lobby's party picker always loaded Star Quest's folder**, ignoring the lobby's own system, so
  an AoF: Quest lobby would have offered Star Quest heroes and then refused the party built from them.
- **`QuestArmy.Build` stamped every party `grimdark-future-star-quest`** and preferred Star Quest books
  for faction rules. Both now take the system - an AoF: Quest party is an `age-of-fantasy-quest` army
  and reads its rules from AoF: Quest's books. This is the #414 bug one system along, and it would have
  presented identically: a greyed-out LAUNCH button blaming the player's army.
- **`PartyLoader.Resolve` generated Star Quest heroes whatever system asked**, found by running the
  scripted path rather than by reading it.

### 2026-10-31 - what the rules file can and cannot say

`StarQuestRules.json` claims Army Forge as its source but **no code writes it** - it was authored by
hand from scraped content, and `StarQuestImportedRulesAgreementTests` documents the two-sources
position. There is no quest-content endpoint: `/api/rules/common/{id}` returns 39 special rules and
nothing about classes, skills or items, and twelve other plausible paths 404.

What the Star Quest dataset does carry is **both systems' naming**: every class has a `baseName`, and
the base names are the fantasy ones - Monk, Wizard, Rogue, Barbarian, Bard, Paladin, Fighter, Ranger,
Cleric, Druid, Sorcerer, Warlock. Star Quest renames 12 of the 13 (Fighter -> Soldier, Wizard ->
Psychic). The 180 skills are already keyed on those base class names. So `AoFQuestRules.json` is the
shared Quest ruleset at its generic naming, generated rather than retyped, with `sqName` kept on each
class for traceability.

**Three things are genuinely not available, and are empty rather than guessed:** AoF: Quest's shop
consumables, its injury table and its level-1 hero stat line all live in the AoF: Quest **Campaign
Builder**, which OPR serves through no endpoint and which is not among the project's books. The status
conditions ARE available - typed word for word from `AoFQ - Core Rules v2.0.1.pdf` - and the base hero
stat line is carried over from Star Quest and marked as carried. `AoFQuestHeroDataTests` pins the two
empty lists so that "empty" cannot quietly become "invented", and fails the day the book arrives.
**Ask Kai for the AoFQ Campaign Builder PDF** and these three can be done properly.

### 2026-10-31 - it already plays headlessly

`--headless --all-ai --game-system aof-quest --game-mode star-quest-horde --party auto:3 --army2
"armies/AoF Quest/Beastmen - enemy collection.fdgarmy" --seed 7` builds a party of **Monk / Wizard /
Rogue from Saurians, 235pts**, plays **13 rounds and 3 waves** against the AoF: Quest enemy book, and
ends cleanly ("the heroes fell during wave 3"). So the data and the plumbing are proven in a played
game, not only in fixtures.

16 new tests, and the two that carry the weight were sabotage-checked: making `QuestDataSet.For` return
the Star Quest set for both systems (the pre-#418 behaviour) fails 8 of 16, and rewriting the roster
with enemy-shaped data fails exactly `TheRosterIsTheHeroDataset_NotTheEnemyBooks`.

### Still open

- **AoF: Quest is still not lobby-selectable**, and that is now a RULES decision rather than missing
  data. Its book is `AoFQ - Core Rules v2.0.1.pdf` - **the 2.0.1 generation** - while the engine
  implements Star Quest's **2.1.0** for all the shared Quest machinery. The differences the engine
  actually models: power costs (extra action, extra skill and revive are **2** in 2.0.1 against 3 in
  2.1.0), a failed hero skill (**pay 1 power and it still resolves** in 2.0.1; optional and it fails
  in 2.1.0), a failed cast (**buy it back for X*2 power** in 2.0.1; final in 2.1.0), melee reach
  (**2" horizontal / 4" vertical** against a flat 3"), Scout (**12"** against 9"), and Villain (**12"
  and optional** against 6" and mandatory). Blast's spread and Fear's morale-test reading match what
  #414 already built. So making it selectable needs an AoF: Quest profile in `CoreRulesProfile` plus
  per-system Quest power costs - the next slice, and until it exists a selectable AoF: Quest would play
  Star Quest's numbers, which is the thing CLAUDE.md forbids.
- **Whether OPR has published AoFQ 2.1.0** is unconfirmed - a web check found nothing either way, and
  the project holds only 2.0.1. If Kai drops a newer book in, this slice gets shorter.
- **The AoF faction groups are a guess at information design** (Humans / Elves / Dwarves & Halflings /
  Greenskins / Undead / Chaos / Beasts). One map in `QuestDataSet`, trivially changed, and Kai's call.
- **12 skills keep Star-Quest-specific names** (Psychic Focus, Techno-Bolts, Sentinel Meditation...)
  because their generic names are not in any data available here. Recorded in the rules file's own
  `notes`.
- **The engine's `QuestItems`, injury and treasure tables are one shared GF-worded set**, like
  `CoreRuleCatalog`. The AoF: Quest shop will read Star Quest's items until its own are available.

### 2026-10-31 - the rules split, and AoF: Quest becomes selectable

`QuestRulesProfile` is the seam: per-system hero mechanics, deliberately separate from
`CoreRulesProfile` (which answers "how does this special RULE read here" - Scout's procedure, Fear's
consumer, Blast's spread) because this answers "what do the hero mechanics cost and how do they
resolve", which is Quest-only. Both books quoted in the file. What differs:

| | Star Quest (GFSQ 2.1.0) | AoF: Quest (AoFQ 2.0.1) |
|---|---|---|
| Extra action / extra skill | 3 power | **2** |
| Revive another hero | 3 power | **2** |
| Failed hero-skill test | **may** pay 1 power; the skill **fails** if you decline | **must** pay 1 power and the skill **resolves** |
| Failed cast | final | **may spend X*2 power to count it as cast** |
| Melee reach | flat 3" | **2" horizontal / 4" vertical** |
| Scout | within 9" of its zone | within **12"** |
| Villain aura | 6", mandatory | **12", optional** |
| Pass a Str/Dex/Wil test, ignore Shaken | 2 power | 2 power (agree) |

Four of those were engine behaviour, not numbers:

- **The failed hero-skill test** now branches on `FailedSkillPaymentIsOptional`. Under 2.0.1 there is
  nothing to ask and a hero skill **cannot fail** - which is the behaviour Q3 deleted for Star Quest,
  and is still AoF: Quest's rule. Pinned by a discriminating pair in `QuestFailedSkillPaymentTests`:
  same roll, same hero, different book; Star Quest asks and a declined skill fails, AoF: Quest takes
  the power without asking and the skill resolves.
- **The failed cast buy-back** had never been implemented at all (Q4 recorded its absence and
  `QuestCastPowerTests` pins it for Star Quest). Added for AoF: Quest only, offered after the die is
  shown so the player decides knowing what it rolled, defaulting to no when the hero cannot pay in
  full - overspending power costs a wound per missing point, and a prompt nobody answers must not
  injure a hero over a spell it never chose to rescue.
- **Melee reach** went from a compile-time `const` to a value `ConfigureTable` sets per game. Q5's pin
  asserted reach is NOT per-system, and its stated reason was that no book wanted the split any more;
  AoF: Quest's does, so the test was rewritten rather than repaired (its claim is unchanged in spirit:
  flat 3" everywhere unless the system's own book keeps the split).
- **The priced labels.** Q3 made the cost appear once so a button could not lie about a price. With two
  systems the labels have to be built per game, so `QuestRulesProfile` owns them and both the offer
  site and the match site read the same string. The revive SKILL's name lost its price - its catalog
  entry is built at class-load time with no game to ask - and the price moved to the description, which
  `DescriptionIn(context)` fills in per game. `QuestPowerCostTests` now asserts the prices for **both**
  systems, which is a stronger claim than it made before.

`CoreRulesProfile` also stopped sending AoF: Quest to the full-scale reading, which it had been doing
by default: its book's Scout, Fear and Blast clauses were read line by line and all three match Star
Quest's, so it shares that profile - the systems' NUMBERS differ, their special-rule readings do not.

**AoF: Quest is now selectable in the lobby**, and `GameSystemArmySourceTests` flipped rather than
being deleted, since "is it selectable" is the question a future reader will have. A played game:
19 rounds against the Dwarves enemy collection, high score 6, no errors. The played game proves the
mode runs; the fixtures are what prove the two books are being obeyed separately, because the
difference between them is mostly a prompt a bot answers the same way either way.

### 2026-10-31 (later) - AoF: Quest is on 2.1.0 after all, and the split collapsed

Kai added `AoFQ - Beginners Guide v2.1.0` and `AoFQ - Campaign Builder v2.1.0`. **AoF: Quest is on the
2.1.0 generation**, and it matches Star Quest on every one of the six things this work item had just
split:

| | AoFQ 2.0.1 (what the split was built from) | AoFQ **2.1.0** (what the game is) |
|---|---|---|
| Extra action / revive | 2 power | **3 power** |
| Failed hero skill | must pay; skill resolves | **may** pay; skill **fails** if declined |
| Failed cast | may be bought for X*2 power | **final** |
| Melee reach | 2" horizontal / 4" vertical | **flat 3"** |
| Scout | 12" | **9"** |
| Villain aura | 12", optional | **6", mandatory** |

So the split was right about the book it had and wrong about the game. `QuestRulesProfile` now holds
**one** Quest profile, quoted from the 2.1.0 books, with `For(system)` keeping the switch because OPR
versions its systems one book at a time and this is where the next divergence goes. The failed-cast
buy-back was **deleted** rather than left behind a flag nobody sets - an unreachable rule is the exact
thing this codebase keeps getting bitten by - and the 2.0.1 wording is recorded in the table above in
case a narrative campaign ever pins a party to the older book. Melee reach stays a per-game FIELD rather
than going back to a `const`: the rule has now changed twice, and a const is a decision that something
can never be per-game.

Three tests flipped rather than being deleted, because "do these two systems agree?" is the question a
future reader will have: the failed-skill discriminating pair now asserts agreement (and that a skill
CAN fail in both, which is the half 2.0.1 did not have), Scout widens by 9" in both, and Q5's "melee
reach is not per-system" claim is true again.

**The lesson, written where the next person will hit it:** a per-system difference is only real while
both systems' CURRENT books say so. Reading one book and finding a difference is not the same as the
game having one.

### 2026-10-31 (later) - the thing the new books DID give: items, injuries and gold

The 2.1.0 Campaign Builder turned out to settle two of the three gaps this item had recorded, and expose
a fault nobody had seen:

- **Injuries:** the six-entry table (Chest Wound / Blinded Eye / Smashed Leg / Broken Arm / Spinal
  Injury / Crushed Spirit) is in `AoFQuestRules.json` now, typed from the book.
- **The level-1 hero stat line** is still not printed in any AoF: Quest book, but carrying Star Quest's
  is now well founded rather than a guess: both systems are on 2.1.0 and share every mechanic it feeds.
- **The shop's item list with costs and tiers is still unavailable** - and it turns out no OPR book
  prints it for EITHER Quest system. Star Quest's 33 consumables came from Army Forge's own quest data,
  and there is no endpoint for it. So `consumables` stays empty, deliberately.
- **AoF: Quest heroes were finding Med Kits and credits.** The Hidden Treasure tables in the two
  Campaign Builders are the same table roll for roll, reskinned - Injector Pouch is Elixir Pouch,
  Grenade Bag is Bomb Bag, Kit Box is Potion Box, Credits Stash is Gold Stash, and the nine items inside
  them pair up one to one. The engine had only Star Quest's, so a fantasy party was pulling sci-fi
  gear out of the ground. `QuestItemSkin` maps the names per system; `QuestItems.Find` resolves either
  vocabulary to the one definition that carries the rules, so a hero keeps the name it found and saved
  heroes from before this keep working; and money is "15g" in AoF: Quest against "15c" in Star Quest,
  parsed either way. Proved in a played mission: the log reads Elixir Pouch / Bomb Bag / Gold Stash.
  **The effects are an inference** - no book prints them, so each AoF name is wired to the Star Quest
  item in the same table slot - and that is written into `QuestItemSkin` rather than hidden.

### Still open after the 2.1.0 books

- **The 36 random-event NAMES are still Star Quest's** in an AoF: Quest game (Psychic Fog where the
  book says Mystic Fog, Haste Stims where it says Haste Berries, and so on). The effects are identical
  in both books and the two events that hand out an item or money are skinned, so this is flavour text
  only - but it is 36 strings read straight from the AoFQ Beginners Guide when someone wants it.
- **Casting is "once per activation, before attacking"** in both 2.1.0 books; the engine has the 2.0.1
  gate. Now confirmed for both systems rather than one.
- **AI group coherency is 1" / 3"** in both 2.1.0 books; the engine runs Grimdark Future's 1" / 9".
- **Blast's per-unit cap**: AoFQ 2.1.0 words it as "Hits must be split evenly between the target and all
  enemy units within 3" of any model from the target (players pick how), **but each unit may only take up
  to as many hits as models in it**". #414 built the spread from Star Quest's shorter wording - worth
  checking the per-receiver cap is enforced, with this text in hand.
- **The AoF: Quest Horde book** (`AoFQ - Horde Mode v2.0.0.pdf`) has still never been read against the
  engine; #416 covered the four 3.6.0 Horde books, and the two Quest Horde books are their own documents.
