namespace FdgRaylib;

/// <summary>
/// Where the .fdgarmy file dialogs open by default. Every army Save/Load in the app (lobby, Army Builder,
/// Army Forge, share-link import) starts in the "armies" folder rather than dumping the user in the
/// application root, which is full of DLLs and nothing they'd ever pick.
///
/// <para>#402: the folder has subfolders (the shipped layout splits it into <c>GDF/</c> and <c>AoF/</c>),
/// so this is where the Load/Save dialogs OPEN, not where the armies necessarily are. The lobby's
/// <see cref="ArmyCatalog"/> scan recurses; nothing reads the folder names.</para>
///
/// <para>Two candidates, in order: beside the executable (the shipped layout - <c>scripts/build-dist.sh</c>
/// copies the repo's <c>armies/</c> next to the binary) and under the working directory (a
/// <c>dotnet run</c> from the repo root, where the output folder is several levels down from
/// <c>armies/</c>). Neither existing falls back to "", i.e. the previous behavior.</para>
/// </summary>
public static class ArmyPaths
{
    /// <summary>Folder name looked for beside the executable and under the working directory.</summary>
    public const string DirectoryName = "armies";

    /// <summary>
    /// A default path for the TinyDialogs open/save dialogs, or "" when no armies folder was found.
    /// tinyfiledialogs reads a trailing separator as "open this directory with no preset file name", so
    /// the Save dialogs still get an empty name box the user types into.
    /// </summary>
    public static string DefaultDialogPath =>
        FolderPath is { } dir ? dir + Path.DirectorySeparatorChar : "";

    /// <summary>The armies folder, or null if neither candidate location exists.</summary>
    public static string? FolderPath =>
        FindIn(AppContext.BaseDirectory, Directory.GetCurrentDirectory()) ?? FindAbove(AppContext.BaseDirectory);

    /// <summary>Walks up from <paramref name="start"/> looking for an armies folder. Covers running from
    /// Visual Studio (F5), where the exe and working directory are bin/Debug/net8.0, several levels below
    /// the repo's armies/ folder.</summary>
    internal static string? FindAbove(string start)
    {
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>First <paramref name="roots"/> entry with an armies subfolder, or null. Takes the roots as
    /// an argument so the search order is testable without moving the process working directory.</summary>
    internal static string? FindIn(params string[] roots)
    {
        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The armies sub-folder for a Game Type. Grimdark Future and Age of Fantasy keep the shipped
    /// GDF/ and AoF/ folders; the skirmish and quest games get their own.</summary>
    public static string FolderNameFor(FDG.EGameSystem system) => system switch
    {
        FDG.EGameSystem.AgeOfFantasy => "AoF",
        FDG.EGameSystem.GFFirefight => "GF Firefight",
        FDG.EGameSystem.AoFSkirmish => "AoF Skirmish",
        FDG.EGameSystem.GFStarQuest => "GF Star Quest",
        FDG.EGameSystem.AoFQuest => "AoF Quest",
        _ => "GDF",
    };

    /// <summary>Makes any missing Game Type folder under armies/, so there's an obvious place to put
    /// each game's lists. Silently does nothing when there is no armies folder or it can't be written.</summary>
    public static void EnsureGameTypeFolders()
    {
        if (FolderPath is not { } root) return;
        foreach (FDG.EGameSystem system in Enum.GetValues<FDG.EGameSystem>())
        {
            try { Directory.CreateDirectory(Path.Combine(root, FolderNameFor(system))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The catalog entries that live in (or under) a Game Type's folder.</summary>
    public static IEnumerable<ArmyCatalogEntry> EntriesFor(FDG.EGameSystem system, IEnumerable<ArmyCatalogEntry> all)
    {
        if (FolderPath is not { } root) return Enumerable.Empty<ArmyCatalogEntry>();
        string folder = Path.GetFullPath(Path.Combine(root, FolderNameFor(system))) + Path.DirectorySeparatorChar;
        return all.Where(e => Path.GetFullPath(e.Path).StartsWith(folder, StringComparison.OrdinalIgnoreCase));
    }
}
