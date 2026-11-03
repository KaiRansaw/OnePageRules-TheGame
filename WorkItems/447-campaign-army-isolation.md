# 447 — Campaign armies stay out of ordinary games, and get their own folder

**Status:** done (app `856b785`, build 0.80, patch B64). App-side only; the engine was not touched.

## Where it came from

Kai, 2026-11-03, answering a question about campaign army creation with something larger:

> *"Do both, but I want to have the Campaign Armies in a different folder than the Skirmish/Firefight,
> Quest/StarQuest, GF/AoF. Each setting (fantasy/scifi) and gamemode needs to have their own army/force
> folders, so NO Campaign armies will show up in any other game modes, and vice versa. And to make sure
> that each campaign list [doesn't] mix in with the other campaign settings."*

## It was a live bug, not untidiness

`ArmyCatalog.Scan` walks `armies/` with `SearchOption.AllDirectories`, and `ReadEntry` picked out
`name`, `faction`, `gameSystem`, `unattributedPoints` and `units` — **and never looked at
`strikeTeam`**. Campaign armies live in the system folders with a `Campaign - ` filename prefix and
nothing else distinguishing them, so:

- they appeared in the lobby's army picker, and
- Random Army could hand one to a **bot**.

Either one fields a list carrying XP, levels, injuries and a modified points cost that no book ever
priced, and the second quietly levels up an army in the middle of somebody's campaign. The inverse also
held loosely — an ordinary army has no sheets and cannot be used mid-campaign, which the campaign screen
already filtered for.

## The fix is two halves, and the split is the point

**The guarantee is the file's contents.** `ReadEntry` now notes a non-null `strikeTeam` property and
returns null. A campaign army is therefore invisible to ordinary games **wherever it sits** — covering
every campaign already on disk in the old location, and any a player moves by hand later.

**The folder is organisation.** New campaign armies go to `armies/Campaigns/<Game Type>/`, one subfolder
per campaign system. This is what Kai asked for and it earns its keep beyond tidiness: the Army
Builder/Forge load dialogs are OS file dialogs opening in a system folder, so the split keeps campaign
armies out of those too, which no content filter could do.

Relying on the path alone would have been wrong — every campaign army already on disk would have stayed
a bug until the player tidied up. Relying on the filter alone would have left the file dialogs showing
them.

## Nothing is moved on disk

`LoadAll` reads **both** a system's campaign folder and its ordinary folder (de-duplicated by full path);
only new armies are written to the new location. Campaigns already created keep working exactly where
they are.

This was a deliberate call against auto-migrating. Moving a player's files to tidy a layout risks doing
it to the wrong file, and the behaviour Kai actually asked for does not need it — the content filter
delivers that on its own. A "tidy these up for me" button is cheap to add later if he wants the layout
to match.

## Horde deliberately still shares the ordinary folder

Taken literally, *"each... gamemode needs to have their own army/force folders"* would split Army vs Army
from Horde. **It should not**, and this is a rules point rather than a preference: CLAUDE.md records that
Horde is a MODE, not a system, and shares that mode's army-builder stats — a Grimdark Future army is
legal in both GF Army vs Army and GF Horde. Splitting them would force every army to be built twice for
no rules reason.

So the axes are: **per system** (already, via the system folders) and **campaign vs not-campaign** (new).
Kai was told this in the reply rather than left to find it.

## The trap it nearly walked into

`SystemForPath` decides which Campaign Rules price list a campaign army is paid by **from the folder it
sits in** — that is #426's fix and the comment on it says the location is "the one fact that cannot go
stale". It matched `armies/<System>/` only, and `armies/Campaigns/GDF/` is **not** under `armies/GDF/`.

Left alone, every campaign written under the new layout would have fallen through to
`info?.GameSystem ?? EGameSystem.GFFirefight`. New campaigns do carry a `GameSystem` block so most would
have survived on the fallback — but a file without one would be paid **Firefight's rewards for a Grimdark
Future mission**, which is precisely the bug #426 existed to fix. Now checks both folders, with a test
per system pinning it **with no help from the file's contents** (`SystemForPath(path, null)`).

## Tests

`FdgRaylib.Tests/CampaignArmyIsolationTests.cs` (10):

- a campaign army in the **old** location is not catalogued (the load-bearing one)
- an ordinary army still **is** catalogued, with its name and points intact
- a campaign army in the new folder is not catalogued either (the two halves must agree, or one is inert)
- new campaign armies are written to the campaign folder
- each campaign system has its own folder, and none shares with its ordinary folder
- the campaign folder decides the system, per system, from the path alone
- the old location still decides the system too
- a campaign in **either** location is still listed on the campaign screen
- an ordinary army is not treated as a campaign (so it stays a legal starting army)
- the same file is never listed twice

App 8854 → 8864. Engine unchanged at 4738. Headless smoke exits 0.

**Verified by sabotage:** the `isCampaignArmy` filter was disabled, 2 of the 10 failed, the file was
restored and confirmed with `grep -q SABOTAGE`.

## Deliberately not done

- **No migration of existing files**, by choice — see above.
- **No test that the LOBBY's picker is empty of campaign armies.** Nothing in the suite draws an ImGui
  frame, so what is covered is `ReadEntry`, which is the one thing the picker and Random Army both read
  through. Recorded rather than glossed.
- **Quest systems are untouched.** Star Quest and AoF: Quest have their own campaign (the Quest Campaign
  Builder) with its own files and screens, and `CampaignSystems` is only the four with a Campaign Rules
  v3.6.0 book.
