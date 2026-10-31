# GF Star Quest army books

The ENEMY and bot side of a Star Quest table: 54 books, imported whole from Army Forge at OPR 3.6.0
on 2026-10-31 (#412).

**Not the player's side.** A Star Quest party is individually built heroes, not an army, and they come
from a different Army Forge dataset with different stats for the same unit - that roster lives in
`FdgRaylib/Assets/StarQuest/` and is built in the Hero Builder. Alien Hives' Grunt Veteran is 55pts
Q6 D6 as a playable hero and 100pts Q5 D5 as an enemy, across rosters of 8 units and 41.

**And not Grimdark Future either**, which is what the game served before these existed: that same Grunt
Veteran is 40pts in Grimdark Future against 100pts here, so every wave and mission priced off the
Grimdark Future book was buying about two and a half times the enemy its points were meant to pay for.

Regenerate with `--rebuild-books <cacheDir> <booksRoot>`; see the project doc
`claude/army-forge-refresh-status.md`. **Never hand-edit a `.fdgbook`** - the rebuild regenerates each
one whole and would eat the edit. Curation goes in a supplement or override file.
