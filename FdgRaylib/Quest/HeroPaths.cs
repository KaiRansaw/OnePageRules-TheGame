using FDG.Quest;

namespace FdgRaylib.Quest;

/// <summary>
/// Where Star Quest heroes are saved: a "heroes" folder beside the "armies" folder (so both live in the
/// project folder when run from Visual Studio), with one sub-folder per game type ("heroes/GF Star Quest").
/// One <c>.fdghero</c> file per hero, named after the hero.
/// </summary>
public static class HeroPaths
{
    public const string DirectoryName = "heroes";

    public static string FolderFor(string gameType)
    {
        string root = ArmyPaths.FolderPath is { } armies
            ? Path.Combine(Path.GetDirectoryName(armies.TrimEnd(Path.DirectorySeparatorChar))!, DirectoryName)
            : Path.Combine(AppContext.BaseDirectory, DirectoryName);
        string folder = Path.Combine(root, gameType);
        try { Directory.CreateDirectory(folder); } catch { /* read-only: Save reports it */ }
        return folder;
    }

    /// <summary>A file name made from the hero's name (letters, digits, spaces, - and _ only).</summary>
    public static string FileNameFor(string heroName)
    {
        string safe = new string(heroName.Trim().Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "Hero";
        return safe + QuestHero.FileExtension;
    }

    /// <summary>Every saved hero of a game type, by name.</summary>
    public static List<(string Path, QuestHero Hero)> LoadAll(string gameType)
        => LoadAllIn(FolderFor(gameType));

    /// <summary>
    /// Every readable hero in one folder, by name. Split out from <see cref="LoadAll"/> for #413's
    /// <c>--party</c>, which is handed a folder path directly rather than a game type - a headless run
    /// may be pointed at any folder of hero files, not only the one this machine saves into.
    /// </summary>
    public static List<(string Path, QuestHero Hero)> LoadAllIn(string folder)
    {
        var list = new List<(string, QuestHero)>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*" + QuestHero.FileExtension))
            {
                QuestHero? h = null;
                try { h = QuestHero.FromJson(File.ReadAllText(file)); } catch { }
                if (h != null) { h.SourceFile = file; list.Add((file, h)); }
            }
        }
        catch { }
        return list.OrderBy(e => e.Item2.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
