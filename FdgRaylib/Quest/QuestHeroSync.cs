using FDG;
using FDG.Quest;

namespace FdgRaylib.Quest;

/// <summary>
/// After a Star Quest game, each hero's sheet (XP, level, improved stats, items, injuries) is written back to the
/// hero file it came from, so the hero keeps what it earned. For a campaign mission the result is also recorded in
/// the campaign: a won mission moves the chapter on (wounds and power carry over), a finished chapter clears the
/// heroes' campaign state, and a failed mission restarts the chapter. Only the computer that made the party can
/// do this (the files have to be on this computer).
/// </summary>
public static class QuestHeroSync
{
    private static readonly HashSet<string> WrittenThisGame = new();
    private static ITableState? _lastTable;

    public static void WriteBack(ITableState table)
    {
        if (!ReferenceEquals(table, _lastTable)) { WrittenThisGame.Clear(); _lastTable = table; }
        var heroes = new List<QuestHero>();
        foreach (IUnit unit in table.Units.Objects)
        {
            if (unit is not UnitData data || data.QuestHeroJson == null) continue;
            QuestHero? hero = QuestHero.FromJson(data.QuestHeroJson);
            if (hero?.SourceFile == null || !WrittenThisGame.Add(hero.SourceFile)) continue;
            if (!File.Exists(hero.SourceFile)) continue;
            heroes.Add(hero);
        }

        // Campaign missions: record the result once per campaign, then update the heroes' campaign state.
        bool mission = table.Progress.GameMode == EGameMode.StarQuestMission;
        foreach (var group in heroes.Where(h => mission && h.CampaignName.Length > 0 && h.CampaignDifficulty > 0 && h.LastOutcome.Length > 0)
                     .GroupBy(h => h.CampaignName))
        {
            // #414: belt-and-braces, and known to be so. The per-hero guard above has already claimed
            // every SourceFile in this game, so a second WriteBack for the same table finds no heroes and
            // never reaches this loop - deleting this line changes no behaviour today. It is kept because
            // it is the only thing that would hold if the per-hero guard ever moves, and removing it
            // would leave the double-record protection resting on a guard that is about something else.
            // The GroupBy, not either guard, is what makes a party of four heroes record one mission.
            if (!WrittenThisGame.Add("campaign:" + group.Key)) continue;
            if (CampaignPaths.Find(group.First().GameType, group.Key) is not { } found) continue;
            QuestCampaign campaign = found.Campaign;
            string outcome = group.Any(h => h.LastOutcome == "success") ? "success" : "failed";
            bool chapterEnded = campaign.RecordMission(outcome);
            CampaignPaths.Save(found.Path, campaign);
            foreach (QuestHero hero in group)
            {
                hero.LastOutcome = "";
                if (!chapterEnded) continue;
                // Finished chapter: heroes are fresh again and out of the chapter. Failed: fresh for the restart.
                hero.WoundsTaken = 0;
                hero.PowerLeft = -1;
                if (outcome == "success") { hero.CampaignName = ""; hero.CampaignDifficulty = 0; }
            }
        }

        foreach (QuestHero hero in heroes)
        {
            try
            {
                hero.Missions++;
                File.WriteAllText(hero.SourceFile!, hero.ToJson());
            }
            catch
            {
                // Read-only file: the hero simply doesn't keep this game's progress.
            }
        }
    }
}
