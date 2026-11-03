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

    /// <summary>
    /// Writes <paramref name="layout"/> and returns the path it ended up at. A new map gets a file
    /// named after it (numbered if that name is taken). An existing map is overwritten in place -
    /// unless it has been RENAMED, in which case the file is renamed to match.
    ///
    /// <para>
    /// That last part is Kai's bug from 2026-11-02: renaming a map changed the name inside the file
    /// and nothing else, so the folder on disk kept saying "New Map" forever. Showing the map's own
    /// name in the lists (which is the other half of the fix) would have hidden that, but a Maps
    /// folder whose filenames disagree with every name in the game is its own trap the first time a
    /// map is copied or sent to somebody.
    /// </para>
    ///
    /// <para>
    /// The rename is best-effort and never destructive: the new content is written FIRST, and if the
    /// old file cannot be removed afterwards the save has still succeeded - worst case there are two
    /// copies, which is recoverable, where a delete-then-write could lose the map outright.
    /// </para>
    /// </summary>
    public static string Save(TerrainLayoutFile layout, string? existingPath)
    {
        string path = existingPath ?? NewPathFor(layout.Name);

        if (existingPath != null && WantsRenaming(existingPath, layout.Name))
        {
            string renamed = NewPathFor(layout.Name);
            try
            {
                File.WriteAllText(renamed, TerrainLoader.SerializeToJson(layout));
                File.Delete(existingPath);
                return renamed;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Could not place the renamed copy, or could not clear the old one. Fall through and
                // save over the original: the map is kept under its old filename, carrying its new
                // name inside, which is exactly where this was before and not a lost map.
                Console.WriteLine($"Could not rename the map file: {e.Message}");
            }
        }

        File.WriteAllText(path, TerrainLoader.SerializeToJson(layout));
        return path;
    }

    /// <summary>
    /// Whether a saved map's filename no longer matches its name. Compared case-insensitively and
    /// against the SANITISED name, so a map called "Hill 203: Dawn" - whose colon cannot be in a
    /// filename - does not try to rename itself on every single save.
    /// </summary>
    internal static bool WantsRenaming(string existingPath, string name)
    {
        string wanted = Sanitize(name);
        if (wanted.Length == 0) return false;
        return !string.Equals(Path.GetFileNameWithoutExtension(existingPath), wanted,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A map name turned into a filename, the same way on every platform.
    ///
    /// <para>
    /// Deliberately NOT <see cref="Path.GetInvalidFileNameChars"/> alone, which answers for the machine
    /// it is running on: a colon is legal on Linux and illegal on Windows, so a map named "Hill 203:
    /// Dawn" saves fine here and cannot be written at all on the machine it is sent to. Maps are files
    /// people copy around, so the scrub is the union - Windows' rules, which are the strict ones -
    /// plus whatever else the host dislikes.
    /// </para>
    /// </summary>
    internal static string Sanitize(string name)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (char c in "<>:\"/\\|?*") invalid.Add(c);
        for (char c = (char)0; c < (char)32; c++) invalid.Add(c);

        string safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        // Trailing dots and spaces are legal in the string and illegal at the end of a Windows filename.
        safe = safe.TrimEnd('.', ' ');

        // And the device names, which are reserved whatever extension follows: a map called "CON"
        // cannot be written on Windows at all.
        string stem = safe.Contains('.') ? safe[..safe.IndexOf('.')] : safe;
        if (ReservedNames.Contains(stem)) safe = "_" + safe;

        return safe;
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static string NewPathFor(string name)
    {
        string safe = Sanitize(name);
        if (safe.Length == 0) safe = "Map";
        string folder = FolderPath;
        string path = Path.Combine(folder, safe + TerrainLayoutFile.EXTENSION_WITH_PERIOD);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{safe} ({n}){TerrainLayoutFile.EXTENSION_WITH_PERIOD}");
        return path;
    }
}
