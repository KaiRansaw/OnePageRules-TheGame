using FDG.SaveLoad;
using FdgRaylib.Cli;

namespace FdgRaylib.SaveLoad;

/// <summary>
/// Where the map maker's maps live: a "Maps" folder, found the same way as the armies folder (beside the
/// executable, the working directory, or any folder above the executable - so a Visual Studio run finds
/// the repo's folder). If there is none yet it is made next to the armies folder (the repo root), or
/// beside the executable as a last resort. Maps are ordinary terrain layout files (.fdgterrain).
/// </summary>
public static class MapPaths
{
    public const string DirectoryName = "Maps";

    public static string FolderPath
    {
        get
        {
            string path = Find() ?? DefaultLocation();
            try { Directory.CreateDirectory(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return path;
        }
    }

    private static string DefaultLocation()
    {
        string? armies = ArmyPaths.FolderPath;
        string? root = armies != null ? Path.GetDirectoryName(Path.GetFullPath(armies)) : null;
        return Path.Combine(root ?? AppContext.BaseDirectory, DirectoryName);
    }

    private static string? Find()
    {
        foreach (string root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            string candidate = Path.Combine(root, DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Every saved map, sorted by name.</summary>
    public static IReadOnlyList<string> List()
    {
        try
        {
            return Directory.EnumerateFiles(FolderPath, "*" + TerrainLayoutFile.EXTENSION_WITH_PERIOD)
                .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not list maps: {e.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Writes <paramref name="layout"/> and returns the path. Overwrites
    /// <paramref name="existingPath"/> when editing a map; a new map gets a file named after it (with a
    /// number added if that name is taken).</summary>
    public static string Save(TerrainLayoutFile layout, string? existingPath)
    {
        string path = existingPath ?? NewPathFor(layout.Name);
        File.WriteAllText(path, TerrainLoader.SerializeToJson(layout));
        return path;
    }

    private static string NewPathFor(string name)
    {
        string safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        if (safe.Length == 0) safe = "Map";
        string folder = FolderPath;
        string path = Path.Combine(folder, safe + TerrainLayoutFile.EXTENSION_WITH_PERIOD);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{safe} ({n}){TerrainLayoutFile.EXTENSION_WITH_PERIOD}");
        return path;
    }
}
