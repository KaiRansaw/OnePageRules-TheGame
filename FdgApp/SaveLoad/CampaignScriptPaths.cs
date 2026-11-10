using FDG;
using FDG.Campaign;

namespace FdgRaylib.SaveLoad;

/// <summary>
/// #465 - where saved campaign plans live: a "Campaigns" folder beside the Maps folder. Each is a
/// <see cref="CampaignScript"/> (.fdgcampaign) - a campaign's missions and notes, playable again with
/// fresh armies, or sent to someone.
/// </summary>
public static class CampaignScriptPaths
{
    public const string DirectoryName = "Campaigns";

    public static string FolderPath
    {
        get
        {
            string maps = MapPaths.FolderPath;
            string root = Path.GetDirectoryName(Path.GetFullPath(maps)) ?? AppAssets.Root;
            string path = Path.Combine(root, DirectoryName);
            try { Directory.CreateDirectory(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return path;
        }
    }

    /// <summary>Every saved plan for <paramref name="system"/>, by name.</summary>
    public static IReadOnlyList<(string Path, CampaignScript Script)> List(EGameSystem system)
    {
        var found = new List<(string, CampaignScript)>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(FolderPath, "*" + CampaignScript.Extension))
            {
                try
                {
                    if (CampaignScript.FromJson(File.ReadAllText(file)) is { } script && script.GameSystem == system)
                        found.Add((file, script));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not list campaign plans: {e.Message}");
        }
        return found.OrderBy(f => f.Item2.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Saves a script named after the campaign (overwriting the same name). Returns the path, or null.</summary>
    public static string? Save(CampaignScript script)
    {
        string safe = string.Concat((script.Name.Length > 0 ? script.Name : "Campaign")
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string path = Path.Combine(FolderPath, safe + CampaignScript.Extension);
        try { File.WriteAllText(path, script.ToJson()); return path; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not save the campaign plan: {e.Message}");
            return null;
        }
    }

    /// <summary>A map's path from its portable name, or null when this computer has no such map.</summary>
    public static string? MapFor(string? name) =>
        string.IsNullOrEmpty(name) ? null
            : MapPaths.List().FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase));
}
