using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// The note on a Quest lobby's Game tab: how big the enemy waves are. Each Quest mode sizes them its own way, so the
/// note follows the mode (bug hunt 2026-11-27 run 4: Quest Horde showed the mission's "difficulty" note, and Horde has
/// no difficulty).
/// </summary>
public static class QuestLobbyText
{
    public static string GameNote(EGameMode mode) => mode == EGameMode.StarQuestHorde
        // Horde Mode v2.0.0 (both Quest games), "2. Deploy Wave".
        ? "A party has no points limit. Wave Size: The total point value of all enemy units in a wave is X*25% of the "
          + "total point value of the heroes, where X is the current wave number."
        : "A party has no points limit. The book sizes each enemy wave from the heroes' total points and the difficulty, "
          + "which you pick when the mission starts.";
}
