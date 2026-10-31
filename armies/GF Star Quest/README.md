# GF: Star Quest - enemy collections

One `.fdgarmy` per Star Quest faction. These are the **enemy** side of a Star Quest game: pick one in
the lobby's army column, and the party you pick in the party picker is your side.

## Why they are so big

A Star Quest enemy army is not a list in the ordinary sense - it is a **pool**. `StarQuestHorde.DeployWave`
draws from the units still in reserve *or already destroyed* (they come back) until the wave's points
budget is spent:

- Horde: wave `N` is worth `N * partyPoints / 4`, doubled for a boss fight.
- Mission: `difficulty * partyPoints / 4`, doubled for a boss wave.

Against a 300pt party that reaches about **1500 pts at wave 10**. A collection smaller than that does not
fail loudly - the wave logs *"the enemy collection is empty, nobody arrives"* and simply does not happen.
So each collection is the whole roster at base model count, which is both the deepest pool and the
widest variety of enemies.

A few books have genuinely small rosters (Worker Unions is 880 pts over 11 units). That is the book's
shape, not a bake fault; those factions thin out in the late waves.

## Regenerating them

```bash
dotnet run --project FdgRaylib/FdgRaylib.csproj -- \
  --make-collections "FdgRaylib/Assets/Books/GF Star Quest" "armies/GF Star Quest"
```

Re-run it after re-importing the Star Quest books. The bake drops the editable session (the picks plus a
full snapshot of the source book) because the book is already bundled next door - that is 2.6MB of
collections instead of 17MB. The cost is that a collection cannot be reopened in the Army Forge; it is
generated *from* a book, so re-run the command instead of editing one.

Pinned by `FdgRaylib.Tests/StarQuestCollectionShippedDataTests.cs`.
