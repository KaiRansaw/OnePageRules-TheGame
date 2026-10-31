# AoF: Quest - enemy collections

One `.fdgarmy` per AoF: Quest faction, 46 of them. These are the **enemy** side of an AoF: Quest game,
the fantasy twin of `armies/GF Star Quest`.

## They are not playable yet

AoF: Quest cannot be picked in the lobby, and these files are why that is only half a gap. The enemy
data is here and correct; what is missing is the **player** side - an AoF: Quest party is a group of
individually built heroes, and that roster is a separate Army Forge dataset (id 7) with no Hero Builder
path yet. See `WorkItems/417`.

They are still worth having checked in now: they are the proof that all 46 books compile, and the moment
a party exists the enemy side is ready.

## Why they are so big

A Quest enemy army is not a list in the ordinary sense - it is a **pool**. Each wave draws from the units
still in reserve *or already destroyed* (they come back) until the wave's points budget is spent:

- Horde: wave `N` is worth `N * partyPoints / 4`, doubled for a boss fight.
- Mission: `difficulty * partyPoints / 4`, doubled for a boss wave.

Against a 300pt party that reaches about **1500 pts at wave 10**. A collection smaller than that does not
fail loudly - the wave logs *"the enemy collection is empty, nobody arrives"* and simply does not happen.
So each collection is the whole roster at base model count, which is both the deepest pool and the widest
variety of enemies.

A few factions genuinely have small rosters (Treasure Hunters compiles to 765 pts over 11 units). That is
the book's shape, not a bake fault - padding it would mean inventing units.

## Re-baking them

After a book re-import, from the repo root:

```bash
dotnet run --project FdgRaylib/FdgRaylib.csproj -- \
    --make-collections "FdgRaylib/Assets/Books/AoF Quest" "armies/AoF Quest"
```

The points here are AoF: **Quest** points, which run about 3x Age of Fantasy's for the same model because
a Quest game buys single models rather than squads. If a re-bake ever produces collections priced like an
Age of Fantasy army, the books behind them were fetched under the wrong Army Forge game-system id -
`AoFQuestShippedDataTests` fails with the book's name when that happens.
