# Credits - assets in the Godot build

| What | Where | Licence |
|---|---|---|
| Icons (`icons/*.svg`) | game-icons.net - by Lorc, Delapouite and sbed | CC BY 3.0 - credit required, shown in-game on a Credits page |
| Hologram UI frames (`ui/holo/gf`, recoloured gold in `ui/holo/aof`) | Wenrexa - Free / More UI Hologram Interface | CC0 (`ui/holo/LICENSE.txt`) |
| Sci-Fi Minimalism UI (`ui/minimal/gf`, recoloured warm in `ui/minimal/aof`) | Wenrexa - Assets Free: UI Sci-Fi Minimalism #01 | CC0 (`ui/minimal/LICENSE.txt`) |
| Rajdhani (`fonts/Rajdhani-*.ttf`) | Indian Type Foundry, via Google Fonts | SIL Open Font License 1.1 (`fonts/OFL-Rajdhani-Exo2.txt`) |
| Exo 2 (`fonts/Exo2.ttf`) | Natanael Gama, via Google Fonts | SIL Open Font License 1.1 |
| Cinzel (`fonts/Cinzel-*.woff2`) - Age of Fantasy headings | The Cinzel Project Authors (Natanael Gama), via Fontsource | SIL Open Font License 1.1 (`fonts/OFL-Cinzel-AlegreyaSans.txt`) |
| Alegreya Sans (`fonts/AlegreyaSans-*.woff2`) - Age of Fantasy text | Juan Pablo del Peral / Huerta Tipografica, via Fontsource | SIL Open Font License 1.1 (`fonts/OFL-Cinzel-AlegreyaSans.txt`) |
| DejaVu Sans (`fonts/DejaVuSans.ttf`) | DejaVu fonts | Bitstream Vera / public-domain licence |
| Terrain models (`terrain/nature, castle, town, industrial, space, survival`) | Kenney (kenney.nl) - Nature, Castle, Fantasy Town 2.0, City Industrial 2.0, Space and Survival kits | CC0 1.0 - each folder has its `License.txt` |
| Terrain models (`terrain/village`) | Quaternius (quaternius.com) - Medieval Village MegaKit [Standard], textures shrunk to 512 px | CC0 1.0 (`terrain/village/License.txt`) |

Icons were recoloured (black background removed so they can be tinted in code); CC BY allows changes.

Terrain models are copied out of the packs by `tools/terrain/bundle_terrain.py` (only the pieces the game uses). The
Nature Kit's teal/salmon colours are turned green/brown at run time (`TerrainDresser.Naturalise`).
