# 428 — A bot campaign army with its own treasury

**Goal.** Give a campaign a standing enemy that levels, recruits, spends and remembers losing. From
`claude/fdg-todo.md`: *"a bot army with its own treasury (without it the campaign has no arc - you farm
an opponent who never improves)"*.

**Status:** done 2026-11-01, rules side. One limitation recorded below rather than guessed at.

---

## 2026-11-01

### The design question Kai left open

Asked how the bot's campaign should grow, Kai answered **"Unsure"**, so this was the engine's call.

**The call: the bot plays the book straight, and the book's own rubber band does the balancing.** Same
treasury rewards from the same table, same prices, same casualty rolls. No difficulty multiplier, no
free points, no hidden handicap in either direction.

That is not a dodge. The rubber band is printed and it is deliberate:

- the **loser of a mission is paid about twice the winner** (Grimdark Future: 300 against 150), so
  whoever is behind banks faster;
- every mission army is capped at `ArmyCapOverLowest` **over the smallest army present**, so whoever is
  ahead cannot field all of it.

A campaign played straight self-corrects. Any curve invented here would have been worse and less
explicable.

Two places the bot deliberately does not behave like a human, both bounds on itself rather than
advantages: it **spends rather than hoards** (a bot sitting on an unspent 2,000 pts is the no-arc
failure in a different costume), and it **will not build past what it could ever bring**
(`CampaignBot.RosterCeiling` = biggest rival + the mission cap), because buying units to leave at home
is growth on a sheet that changes nothing on a table.

### Shape

- `Campaign/CampaignBot.cs` — the bot's steps between missions: take the choices waiting on its sheets
  (deterministically, first option — a bot that rolls its traits is no more interesting to play against,
  and determinism means a campaign replays the same from the same save), buy a Hero if it has none, then
  bodies cheapest-first under the ceiling, pick what to bring (**most experienced first**), and roll its
  own special objective.
- `StrikeTeamInfo.EnemyArmyFile` — the player's campaign army remembers its enemy. The enemy's own block
  carries `IsBot` instead.
- Creation makes the enemy: another army from the same game's folder, started with the same treasury
  under the same rules, picked at random so two campaigns on one folder do not face the same enemy.
- `StartMission` prepares both sides. **The player chooses first and the cap is measured against their
  choice** — the only order that terminates, since the book's cap is "the lowest army plus N" and with
  both sides choosing at once each limit depends on the other. Not an advantage: the number is the same
  either way round, it just has to be a number before anyone can obey it.
- `LobbyScreen.PendingCampaignEnemyArmy` — the lobby seats a bot and hands it that army.
- `StrikeTeamFiles.WriteBack` runs the bot's between-missions step after `AfterGame`, so it is spending
  the reward it just earned. **Nothing new was needed to pay the bot**: the write-back groups table units
  by their sheets' `TeamFile`, so a bot campaign army was already a first-class campaign army to it.
- The campaign screen hides bot armies from the player's list (they are the opponent, not something to
  manage) and shows the enemy's units, points, treasury, VP and total levels on the camp screen — a
  campaign whose opponent improves **invisibly** is indistinguishable from one whose opponent does not
  improve at all.

### The test that found something

`CampaignArcTests` plays **ten and twenty mission campaigns** and looks at where the two armies end up,
because "the rubber band balances it" is a claim and an unverified claim is an opinion.

Its first version asserted the bot's whole **roster** stayed under the roster ceiling. **It failed:
1,165 pts against a 1,000 pt ceiling.** The test was wrong, not the bot. The ceiling bounds what the bot
will *buy*, measured when it buys, and two things move it afterwards — level-ups make a unit cost more
with nothing bought, and the player's army shrinks when it loses units for good, dropping the ceiling
below what the bot already owned.

**The invariant that actually governs a game is what the bot BRINGS**, every mission, against the book's
cap. A bot may own a stable of veterans it cannot field; it may not put them all on the table. The tests
now assert that per mission over twenty missions, which is also what would catch a slow leak.

Measured over ten missions the player wins every time (the worst case for the bot): **600 pts → ~1,165
pts, 0 XP → ~97 XP**. The arc is real.

### Known limitation, recorded rather than guessed at

Those same ten missions take the bot's treasury past **2,500 pts**, because the ceiling stops it buying
bodies long before it runs out of money. So it behaves as an army that is **always exactly as big as the
rules let it be**, holding a reserve it spends the moment the player's own army grows.

That follows from the cap rather than from a mistake, but a human with 2,500 spare points would buy
**upgrades** — turning the reserve into quality rather than quantity. Choosing which upgrade is good is a
real sub-problem (the economy can price any loadout; nothing yet judges one), so it is filed in
`claude/fdg-todo.md` rather than guessed at here.

### Tests

- `CampaignBotTests` — 17 cases: never acts for a player's army, spends, hero first, bodies after, never
  into debt, reports a no-op, stops at the ceiling, the ceiling arithmetic per book, survives a bookless
  army, resolves and drains pending choices, brings veterans first, stays under the cap, always brings
  one, leaves recovering units home, and its special objective (including never handing Hexer to an army
  with no caster). Sabotaged two ways — ignore the ceiling, drop the veterans-first sort — and exactly
  the two matching tests fail.
- `CampaignArcTests` — 7 cases over 10 and 20 mission campaigns, as above.

## Outcome

Done. The enemy now levels, recruits, spends and comes back. Hand-verification is a campaign actually
played, which only Kai can do.
