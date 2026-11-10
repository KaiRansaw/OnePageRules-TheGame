using FDG.Quest;

namespace FdgRaylib.Quest;

/// <summary>
/// Where Star Quest campaigns are saved: <c>heroes/GF Star Quest/campaigns/&lt;name&gt;.fdgcampaign</c>, beside the
/// party's hero files.
/// </summary>
public static class CampaignPaths
{
    public static string FolderFor(string gameType)
    {
        string folder = Path.Combine(HeroPaths.FolderFor(gameType), "campaigns");
        try { Directory.CreateDirectory(folder); } catch { /* read-only: saving reports it */ }
        return folder;
    }

    public static string FileFor(string gameType, string name) =>
        Path.Combine(FolderFor(gameType), Path.GetFileNameWithoutExtension(HeroPaths.FileNameFor(name)) + QuestCampaign.FileExtension);

    public static List<(string Path, QuestCampaign Campaign)> LoadAll(string gameType)
    {
        var list = new List<(string, QuestCampaign)>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(FolderFor(gameType), "*" + QuestCampaign.FileExtension))
            {
                QuestCampaign? c = null;
                try { c = QuestCampaign.FromJson(File.ReadAllText(file)); } catch { }
                if (c != null) list.Add((file, c));
            }
        }
        catch { }
        return list.OrderBy(e => e.Item2.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static (string Path, QuestCampaign Campaign)? Find(string gameType, string name) =>
        LoadAll(gameType).Cast<(string, QuestCampaign)?>().FirstOrDefault(e => e!.Value.Item2.Name == name);

    public static bool Save(string path, QuestCampaign campaign)
    {
        try { File.WriteAllText(path, campaign.ToJson()); return true; }
        catch { return false; }
    }

    public static QuestHero? LoadHero(string file)
    {
        try { return File.Exists(file) ? QuestHero.FromJson(File.ReadAllText(file)) : null; }
        catch { return null; }
    }

    public static bool SaveHero(string file, QuestHero hero)
    {
        try { File.WriteAllText(file, hero.ToJson()); return true; }
        catch { return false; }
    }

    // ── One campaign per hero (Batch E) ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The unfinished campaign this hero file belongs to, or null. Kai, 2026-11-07: "A hero belongs to ONE
    /// campaign." A campaign that is complete lets its heroes go - there is nothing left for them to play.
    /// </summary>
    public static QuestCampaign? CampaignOf(string gameType, string heroFile, IEnumerable<QuestCampaign>? campaigns = null)
    {
        string key = Key(heroFile);
        foreach (QuestCampaign c in campaigns ?? LoadAll(gameType).Select(e => e.Campaign))
            if (!c.Finished && c.HeroFiles.Any(f => Key(f) == key)) return c;
        return null;
    }

    private static string Key(string path)
    {
        try { return Path.GetFullPath(path).ToUpperInvariant(); }
        catch { return path.ToUpperInvariant(); }
    }

    // ── Saved campaign plans ("Save as a campaign file" / "Start from a saved campaign") ──────────────

    /// <summary><c>heroes/&lt;game&gt;/campaign plans</c>, beside the campaigns folder.</summary>
    public static string PlansFolderFor(string gameType)
    {
        string folder = Path.Combine(HeroPaths.FolderFor(gameType), "campaign plans");
        try { Directory.CreateDirectory(folder); } catch { /* read-only: saving reports it */ }
        return folder;
    }

    public static string PlanFileFor(string gameType, string name) =>
        Path.Combine(PlansFolderFor(gameType), Path.GetFileNameWithoutExtension(HeroPaths.FileNameFor(name)) + QuestCampaign.PlanExtension);

    /// <summary>Every saved plan for this Quest game, by name.</summary>
    public static List<(string Path, QuestCampaign Plan)> LoadPlans(string gameType)
    {
        var list = new List<(string, QuestCampaign)>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(PlansFolderFor(gameType), "*" + QuestCampaign.PlanExtension))
            {
                QuestCampaign? c = null;
                try { c = QuestCampaign.FromJson(File.ReadAllText(file)); } catch { }
                if (c != null && c.Chapters.Count > 0) list.Add((file, c));
            }
        }
        catch { }
        return list.OrderBy(e => e.Item2.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Writes the campaign's structure (no party, no progress) as a plan file named after it.
    /// Returns the path, or null if it could not be written.</summary>
    public static string? SavePlan(string gameType, QuestCampaign campaign)
    {
        string path = PlanFileFor(gameType, campaign.Name);
        try { File.WriteAllText(path, campaign.ToPlan().ToJson()); return path; }
        catch { return null; }
    }
}
