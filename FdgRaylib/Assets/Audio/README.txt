YOUR AUDIO FOLDER
=================

Drop sound files in here and the game plays them. No code, no restart needed for most of them
(Settings -> Sound -> Sound slots... -> Rescan picks up new files straight away).

HOW TO NAME THEM
  Each line below is a slot. Its name is where the file goes:
    announcer/round-3      ->  announcer/round-3.ogg  (one file)
                           or  announcer/round-3/<any names>.ogg  (several - one is picked at random,
                               never the same one twice in a row; for music, a shuffled playlist)
  Formats: .ogg (best), .mp3, .wav.
  Your file wins over the game's built-in sound for the same slot.
  An empty slot uses the slot named after "else", or stays silent.

These files are yours: they stay on this computer and are never pushed to GitHub.

MUSIC  (Settings -> Sound -> Music)
  music/menu-grimdark                    Menus while the Grimdark Future look is on.  [has a built-in sound]
  music/menu-fantasy                     Menus while the Age of Fantasy look is on.  [has a built-in sound]
  music/lobby-grimdark                   A Grimdark Future game's lobby, before launch.  else music/menu-grimdark
  music/lobby-fantasy                    An Age of Fantasy game's lobby, before launch.  else music/menu-fantasy
  music/battle-grimdark                  In battle - Grimdark Future, Firefight, Star Quest.
  music/battle-fantasy                   In battle - Age of Fantasy, Skirmish, Quest.
  music/battle-final-round-grimdark      The last round of a Grimdark Future battle.  else music/battle-grimdark
  music/battle-final-round-fantasy       The last round of an Age of Fantasy battle.  else music/battle-fantasy
  music/horde-grimdark                   Horde Mode battles (Grimdark Future games).  else music/battle-grimdark
  music/horde-fantasy                    Horde Mode battles (Age of Fantasy games).  else music/battle-fantasy
  music/quest-grimdark                   Star Quest missions.  else music/battle-grimdark
  music/quest-fantasy                    AoF: Quest missions.  else music/battle-fantasy
  music/campaign-grimdark                Campaign screens between battles (Grimdark Future games).  else music/menu-grimdark
  music/campaign-fantasy                 Campaign screens between battles (Age of Fantasy games).  else music/menu-fantasy
  music/victory                          The game-over screen when your side won (plays once).
  music/defeat                           The game-over screen when your side lost (plays once).
  music/draw                             The game-over screen after a draw, or when watching bots (plays once).  else music/victory
  music/army/<army name>                 An army's own battle music, played when YOU field that army (e.g. music/army/alien-hives/). Beats the battle slots above; lower case, spaces as -.

ANNOUNCER  (Settings -> Sound -> Announcer)
  announcer/battle-start                 The battle begins (round 1 starts).
  announcer/deployment                   Deployment starts.
  announcer/round                        A new round starts (any round without its own numbered line).
  announcer/round-1                      Round 1 starts.  else announcer/round
  announcer/round-2                      Round 2 starts.  else announcer/round
  announcer/round-3                      Round 3 starts.  else announcer/round
  announcer/round-4                      Round 4 starts.  else announcer/round
  announcer/round-5                      Round 5 starts.  else announcer/round
  announcer/round-6                      Round 6 starts.  else announcer/round
  announcer/round-7                      Round 7 starts.  else announcer/round
  announcer/round-8                      Round 8 starts.  else announcer/round
  announcer/round-9                      Round 9 starts.  else announcer/round
  announcer/round-10                     Round 10 starts.  else announcer/round
  announcer/final-round                  The last round starts (plays instead of the numbered line).
  announcer/your-turn                    Play passes to you from another player (hot-seat and online games).
  announcer/objective-seized             Your side takes an objective.
  announcer/objective-lost               An enemy takes one of your objectives.
  announcer/enemy-unit-destroyed         An enemy unit is wiped out.
  announcer/unit-lost                    One of your units is wiped out.
  announcer/hero-down                    Star Quest / Quest: one of your heroes goes down.  else announcer/unit-lost
  announcer/unit-routed                  A unit fails morale and routs.
  announcer/wave-incoming                Horde Mode: a new wave arrives.
  announcer/objective-destroyed          Horde Mode: an objective is destroyed.
  announcer/prolonged-battle             Prolonged Battle: the fight goes on for another round.
  announcer/victory                      Your side wins.
  announcer/defeat                       Your side loses.
  announcer/draw                         The game ends in a draw.

GAME CUES  (Settings -> Sound -> Game cues)
  cues/dice                              Dice are rolled.  [has a built-in sound]
  cues/battle-start                      The battle begins.  [has a built-in sound]
  cues/round-over                        A round ends.  [has a built-in sound]
  cues/objective-seized                  An objective is taken.  [has a built-in sound]
  cues/objective-lost                    You lose an objective.  [has a built-in sound]
  cues/unit-destroyed                    A unit is wiped out.  [has a built-in sound]
  cues/morale-shaken                     A unit fails morale and is Shaken.  [has a built-in sound]
  cues/morale-routed                     A unit fails morale and routs.  [has a built-in sound]
  cues/victory                           The game ends and your side won (or bots finished).  [has a built-in sound]
  cues/defeat                            The game ends and your side lost.  [has a built-in sound]

AMBIENCE  (Settings -> Sound -> Ambience)
  ambience/war                           Grimdark Future battles: wind with distant rumbles of war.  [has a built-in sound]
  ambience/wind                          Age of Fantasy battles: wind.  [has a built-in sound]
  ambience/rain                          Added on rain, heavy rain and storm maps.  [has a built-in sound]

MENU SOUNDS  (Settings -> Sound -> Menu sounds)
  menu/hover                             The mouse or controller cursor moves onto a button. Empty: the game's own soft tick.
  menu/click                             A button is pressed. Empty: the game's own click.

