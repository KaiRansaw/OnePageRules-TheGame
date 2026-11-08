using System.Text;
using FDG;
using FDG.Quest;
using FDG.StateMachine;

namespace FdgRaylib.Quest;

/// <summary>
/// Batch E - the Quest campaign journal's words: a mission's "What happened" lines (written from the game's
/// own record, never typed) and the exported text file. Pure, so tests pin what it says. Money is the
/// system's own (credits / gold).
/// </summary>
public static class QuestJournal
{
    public static readonly string[] Difficulties = { "Beginner", "Intermediate", "Expert", "Legendary" };

    public static string DifficultyName(int d) => d is >= 1 and <= 4 ? Difficulties[d - 1] : "-";

    /// <summary>A primary objective's name on the book's table, or "Roll when it starts" for 0.</summary>
    public static string PrimaryName(int n) =>
        n is >= 1 and <= 6 ? StarQuestMissionRules.PrimaryNames[n] : "Roll when it starts";

    public static string SecondaryName(int n) =>
        n is >= 1 and <= 6 ? StarQuestMissionRules.SecondaryNames[n] : "Roll when it starts";

    /// <summary>"What happened" for one played mission, a line per kind of thing, only the lines that apply.</summary>
    public static List<string> RecordLines(CampaignMissionRecord r, EGameSystem system)
    {
        string money = QuestItemSkin.CurrencySuffix(system);
        var lines = new List<string>
        {
            r.Outcome == "success" ? "Mission complete." : "The heroes fell - the chapter restarts.",
            $"Primary: {(r.Primary > 0 ? StarQuestMissionRules.PrimaryNames[Math.Clamp(r.Primary, 1, 6)] : "-")} - {(r.PrimaryDone ? "completed" : "not completed")}."
                + (r.Secondary > 0
                    ? $" Secondary: {StarQuestMissionRules.SecondaryNames[Math.Clamp(r.Secondary, 1, 6)]} - {(r.SecondaryDone ? "completed" : "failed")}."
                    : " Secondary: never came up."),
        };
        if (r.KnockedOut.Count > 0) lines.Add("Knocked out: " + string.Join(", ", r.KnockedOut) + ".");
        if (r.Injuries.Count > 0) lines.Add("Injuries: " + string.Join(", ", r.Injuries) + ".");
        if (r.Xp.Count > 0) lines.Add("XP: " + string.Join(", ", r.Xp) + ".");
        if (r.Loot.Count > 0) lines.Add("Loot: " + string.Join(", ", r.Loot) + ".");
        if (r.Money.Count > 0) lines.Add($"{QuestItemSkin.CurrencyWord(system).Substring(0, 1).ToUpperInvariant()}{QuestItemSkin.CurrencyWord(system).Substring(1)}: "
                                         + string.Join(", ", r.Money.Select(m => m + money)) + ".");
        if (r.Events.Count > 0) lines.Add("Events: " + string.Join(", ", r.Events) + ".");
        if (r.Challenges.Count > 0) lines.Add("Challenges: " + string.Join(", ", r.Challenges) + ".");
        lines.Add($"Difficulty: {DifficultyName(r.Difficulty)}. Rounds: {r.Rounds}." + (r.PlayedAt.Length > 0 ? $" Played {r.PlayedAt}." : ""));
        return lines;
    }

    /// <summary>The whole journal as plain text: the story, then every chapter and mission with the plan,
    /// the game's record of each time it was played, and the player's own account.</summary>
    public static string Export(QuestCampaign c, EGameSystem system, IEnumerable<string> partyNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{c.Name} - {GameSystemInfo.DisplayName(system)} campaign journal");
        sb.AppendLine("Party: " + string.Join(", ", partyNames));
        sb.AppendLine();
        sb.AppendLine("THE CAMPAIGN");
        sb.AppendLine(c.Story.Trim().Length > 0 ? c.Story.Trim() : "(no story written)");
        foreach (CampaignChapter ch in c.Chapters)
        {
            sb.AppendLine();
            string state = ch.Done ? "done" : c.Active == ch ? $"in progress ({ch.MissionsWon} of {ch.Missions} won)" : "not started";
            sb.AppendLine($"CHAPTER {ch.Number} - {ch.Missions} mission{(ch.Missions == 1 ? "" : "s")} - {state}"
                          + (ch.Difficulty > 0 ? $" - {DifficultyName(ch.Difficulty)}" : ""));
            if (ch.Theme.Trim().Length > 0) sb.AppendLine("Theme: " + ch.Theme.Trim());
            if (ch.EnemyName.Trim().Length > 0) sb.AppendLine("Enemy force: " + ch.EnemyName.Trim());
            for (int m = 0; m < ch.MissionPlan.Count; m++)
            {
                CampaignMission mission = ch.MissionPlan[m];
                sb.AppendLine();
                sb.AppendLine($"  Mission {m + 1} - planned primary: {PrimaryName(mission.Primary)}; secondary: {SecondaryName(mission.Secondary)}");
                for (int r = 0; r < mission.Records.Count; r++)
                {
                    sb.AppendLine(mission.Records.Count > 1 ? $"  What happened (attempt {r + 1}):" : "  What happened:");
                    foreach (string line in RecordLines(mission.Records[r], system)) sb.AppendLine("    " + line);
                }
                if (mission.Notes.Trim().Length > 0)
                {
                    sb.AppendLine(mission.Records.Count > 0 ? "  Your account:" : "  Briefing:");
                    foreach (string line in mission.Notes.Trim().Split('\n')) sb.AppendLine("    " + line.TrimEnd('\r'));
                }
            }
        }
        if (c.History.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("HISTORY");
            foreach (string line in c.History) sb.AppendLine("- " + line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The structure roll as the screen states it. Star Quest's is the GFSQ Campaign Builder v2.1.0's own
    /// sentence. The AoFQ Campaign Builder v2.1.0 is not in this repository, so for AoF: Quest the screen
    /// states the roll the game makes without quoting a book it can't show.
    /// </summary>
    public static string BookStructureLine(EGameSystem system) => system == EGameSystem.AoFQuest
        ? "The game rolls D3+2 chapters, each of D3+1 missions."
        : "Book (GFSQ Campaign Builder v2.1.0): \"Each campaign is made up to D3+2 chapters, with chapters having D3+1 missions.\"";
}
