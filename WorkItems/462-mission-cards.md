# 462 — Mission Cards

**Status:** slice 1 done 2026-11-05 (build 1.00, B84) - the two fixed-deck games. Slice 2 (the six
custom-deck games) open.

Source: GF / GFF / AoF / AoFS Mission Cards v3.6.0 (rules text identical in all four); the 36 cards from
Kai's screenshots (2026-11-05), plus the two per-book differences he gave: 65 Giant Slayer 1 VP is Tough(12)+
in GF/AoF, Tough(9)+ in GFF/AoFS; 64 Assassinate 2 VP needs 3+ Hero models in GF/AoF, 2+ in GFF/AoFS. Some
books name cards differently - Kai: use the names given.

## Done (slice 1)
- `Missions/MissionCards.cs` - catalog (36 cards, per-system 64/65), `MissionCardState` (on
  `GameProgressData.MissionCards`, carried forward + pending hop like Underdog).
- `Missions/MissionCardRules.cs` - what each card asks, pure, over a `MissionBoard`.
- `Missions/MissionCardHooks.cs` - `MissionCardRoundHooks` (deal / round-1 redraw / score / discard),
  `MissionCardEvents` (kills with cause, morale fails, spells cast, melee wins), final objectives.
- `GameSettings.MissionCardGame` (Off / Battle of Wits / Total Domination); standard mode, four systems.
- Six objectives, no D3+2 roll (a map's fixed objectives only if it has exactly six).
- Victory on VP (+1 per objective held at the end); teams pool VP.
- Lobby "Mission Cards" combo (Table section); UserConfig host default.
- Mission tab: VP, cards with hover text, scored list.
- Tests: `Tests/MissionCardsTests.cs` (catalog, every card family, four played games).

## Decisions
- Bots score cards automatically but don't play towards them (Kai, 2026-11-05). Their only card choices
  are "keep" (first option).
- Tied completed cards: the player picks which scores (Kai, 2026-11-05, B85); a bot takes the first.
- Judgement calls (house-rules C): this-round counting, killer-less deaths credited to the victim's enemies,
  rout = destroyed in melee, side control, "within" = any model, Total Domination tie = card stays and
  nobody scores, lowest-score discard skipped on a tie for lowest.

## Not done
- Slice 2: Direct Challenge, Strategic Ruse, Planned Gambit, Coordinated Recovery, Logistical Supremacy,
  Tactical Paragons (18-card custom decks, hand of 5, hidden cards).
- Bots chasing cards.
- Roll & Play (two dice instead of a deck) - not needed in a video game; the deck shuffle is the same odds.
