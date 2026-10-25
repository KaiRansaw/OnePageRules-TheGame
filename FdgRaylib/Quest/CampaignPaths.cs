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
}
