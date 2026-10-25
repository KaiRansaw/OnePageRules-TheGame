using FDG.SaveLoad;

namespace FdgRaylib.SaveLoad;

/// <summary>
/// Where saved games live. The Save dialog opens here and the Load Game screen lists every
/// <c>.fdgsave</c> in it (plus the automatic recovery saves from <see cref="RecoverySave"/>).
///
/// <para>The folder is "Saved Games". Like <see cref="ArmyPaths"/>, it is looked for beside the
/// executable, then in the working directory, then in each parent folder above the executable - so a
/// Visual Studio run (exe in bin/Debug/net8.0) still finds the repo's "Saved Games" folder. If none
/// exists, one is made beside the executable.</para>
/// </summary>
public static class SavePaths
{
    public const string DirectoryName = "Saved Games";

    /// <summary>The saved-games folder (created if it was missing). Never null.</summary>
    public static string FolderPath
    {
        get
        {
            string? found = Find();
            string path = found ?? Path.Combine(AppContext.BaseDirectory, DirectoryName);
            try { Directory.CreateDirectory(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return path;
        }
    }

    /// <summary>Default path for the Save dialog: the folder with a trailing separator, so the dialog
    /// opens inside it with an empty file name box.</summary>
    public static string DefaultDialogPath => FolderPath + Path.DirectorySeparatorChar;

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

    /// <summary>Every saved game the Load Game screen should offer, newest first: the saved-games folder
    /// (and its subfolders) plus the recovery saves.</summary>
    public static IReadOnlyList<string> ListSaves()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string f in Directory.EnumerateFiles(FolderPath, "*" + GameSaveFile.EXTENSION_WITH_PERIOD,
                         SearchOption.AllDirectories))
                files.Add(Path.GetFullPath(f));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not list saved games: {e.Message}");
        }
        foreach (string f in RecoverySave.List()) files.Add(Path.GetFullPath(f));

        return files.OrderByDescending(f => SafeWriteTime(f)).ToList();
    }

    public static DateTime SafeWriteTime(string path)
    {
        try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
    }
}
