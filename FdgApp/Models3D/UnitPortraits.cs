namespace FdgRaylib.Models3D;

/// <summary>
/// Unit portraits photographed from the downloaded 3D models (Kai, 2026-11-12: an in-game way for players to get
/// pictures easily; angle "A", full body three-quarter, picked on the Portrait Angle Review page).
///
/// <para>
/// One picture per model FILE (Kai, 2026-11-12: "make it so that the portrait matches what model variety we choose"):
/// a unit shows the portrait of the look picked for it in the army builder, found exactly the way the table finds the
/// model to stand up (<see cref="ModelFor"/>), so the portrait and the table always agree. No look picked, or a look
/// this player hasn't downloaded = the unit's first version, as on the table. Saved as
/// <c>UnitModels/_Portraits/&lt;Book&gt;/&lt;Unit&gt;/&lt;model file name&gt;.png</c>, mirroring the model folders; the
/// underscore keeps the model library from reading it as an army. A unit is found through <see cref="UnitModelLibrary.FilesFor"/>, so
/// Firefight and Star Quest names (and allied units) reach the same portrait as their Grimdark Future unit.
/// </para>
/// </summary>
public static class UnitPortraits
{
    public const string FolderName = "_Portraits";

    private static UnitModelLibrary? _library;
    private static bool _scanned;

    /// <summary>The downloaded models, scanned once per run; <see cref="Forget"/> after new downloads or portraits.</summary>
    public static UnitModelLibrary? Library
    {
        get
        {
            if (_scanned) return _library;
            _scanned = true;
            try
            {
                UnitModelMap? map = null;
                try { map = UnitModelMap.Load(); } catch (Exception) { }
                _library = UnitModelLibrary.Scan(UnitModelPaths.Root, map);
            }
            catch (Exception) { _library = null; }
            return _library;
        }
    }

    /// <summary>Forgets the scan so the next lookup sees new files.</summary>
    public static void Forget() { _scanned = false; _library = null; _chosen = null; }

    private static (string Book, string Unit) Folders(string modelFile)
    {
        string unitDir = Path.GetDirectoryName(Path.GetFullPath(modelFile))!;
        return (Path.GetFileName(Path.GetDirectoryName(unitDir)!), Path.GetFileName(unitDir));
    }

    /// <summary>Where the portrait of this model file goes: _Portraits/&lt;Book&gt;/&lt;Unit&gt;/&lt;file&gt;.png.</summary>
    public static string PathFor(string modelFile, string? root = null)
    {
        var (book, unit) = Folders(modelFile);
        return Path.Combine(root ?? UnitModelPaths.Root, FolderName, book, unit, Path.GetFileNameWithoutExtension(modelFile) + ".png");
    }

    /// <summary>
    /// A Quest hero's own portrait (Kai, 2026-11-23: "a custom portrait for that character"):
    /// _Portraits/_Heroes/&lt;faction&gt;__&lt;hero name&gt;.png. Keyed by the hero's name, which is also its unit's name on the
    /// table, so the battle shows it too.
    /// </summary>
    public static string HeroPath(string faction, string heroName, string? root = null)
    {
        static string Safe(string s) => string.Concat(s.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(root ?? UnitModelPaths.Root, FolderName, "_Heroes", Safe(faction) + "__" + Safe(heroName) + ".png");
    }

    /// <summary>The hero's own portrait file, or null when none was taken.</summary>
    public static string? FindHero(string faction, string heroName)
    {
        if (string.IsNullOrWhiteSpace(heroName)) return null;
        string p = HeroPath(faction, heroName);
        return File.Exists(p) ? p : null;
    }

    /// <summary>Build 1.87 made one portrait per unit (_Portraits/&lt;Book&gt;/&lt;Unit&gt;.png); still shown until redone.</summary>
    private static string OldPathFor(string modelFile, string? root = null)
    {
        var (book, unit) = Folders(modelFile);
        return Path.Combine(root ?? UnitModelPaths.Root, FolderName, book, unit + ".png");
    }

    /// <summary>
    /// The model file that stands for a unit with this look - the same rule the battle table uses: a look naming its own
    /// folder ("Faction/Unit/file", a Quest hero's) first, else the unit's own files, the picked one or the first version.
    /// </summary>
    public static string? ModelFor(UnitModelLibrary library, string faction, string unit, string? look)
    {
        if (ModelLooks.Folder(look) is { } own)
            return ModelLooks.Pick(library.FilesFor(own.Faction, own.Unit), own.File);
        return ModelLooks.Pick(library.FilesFor(faction, unit), look);
    }

    /// <summary>The look a unit's portrait shows: the first one picked for any of its models (null = first version).</summary>
    public static string? LookOf(IEnumerable<string?>? looks) => looks?.FirstOrDefault(l => !string.IsNullOrEmpty(l));

    /// <summary>
    /// The saved portrait for a unit of this army wearing this look, or null. A look with no portrait made yet falls
    /// back to the unit's first version's portrait, so a unit with any portrait never drops to the icon.
    /// </summary>
    public static string? Find(string faction, string unit, string? look = null)
    {
        if (Library is not { } lib || ModelFor(lib, faction, unit, look) is not { } model) return null;
        foreach (string? f in new[] { model, ModelFor(lib, faction, unit, null) })
        {
            if (f == null) continue;
            if (PathFor(f) is var p && File.Exists(p)) return p;
            if (OldPathFor(f) is var old && File.Exists(old)) return old;
        }
        return null;
    }

    // ---- Chosen portraits (Kai, 2026-11-14)

    public const string ChosenFile = "chosen.json";

    private static HashSet<string>? _chosen;

    private static string ChosenPath(string? root) => Path.Combine(root ?? UnitModelPaths.Root, FolderName, ChosenFile);

    private static HashSet<string> Chosen(string? root = null)
    {
        if (_chosen != null && root == null) return _chosen;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string p = ChosenPath(root);
            if (File.Exists(p))
                foreach (string s in System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(p)) ?? new()) set.Add(s);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        if (root == null) _chosen = set;
        return set;
    }

    private static string Key(string modelFile, string? root) =>
        Path.GetRelativePath(Path.Combine(root ?? UnitModelPaths.Root, FolderName), PathFor(modelFile, root)).Replace('\\', '/');

    /// <summary>
    /// Records that this model's portrait was made on purpose (Army Forge -> Choose looks -> Make portrait, or the Asset
    /// Lab's portrait maker) - Kai, 2026-11-14: "choose which model you want THEN make the portrait that the game then uses
    /// for that model variant". A chosen portrait is shown for its version ahead of the official picture.
    /// </summary>
    public static void MarkChosen(string modelFile, string? root = null)
    {
        var set = Chosen(root);
        if (!set.Add(Key(modelFile, root))) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ChosenPath(root))!);
            File.WriteAllText(ChosenPath(root), System.Text.Json.JsonSerializer.Serialize(set.OrderBy(s => s).ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static bool IsChosen(string modelFile, string? root = null) => Chosen(root).Contains(Key(modelFile, root)) && File.Exists(PathFor(modelFile, root));

    /// <summary>The portrait made on purpose for exactly the version this look stands for (no falling back to another
    /// version), or null. Portraits made in bulk before 2.20 are not "chosen" - the official picture stays ahead of them.</summary>
    public static string? FindChosen(string faction, string unit, string? look = null)
    {
        if (Library is not { } lib || ModelFor(lib, faction, unit, look) is not { } model) return null;
        return IsChosen(model) ? PathFor(model) : null;
    }

    /// <summary>
    /// Every model file under UnitModels: (book, unit, file), A-Z. <paramref name="firstOnly"/> keeps one per unit, the
    /// first version - the same file the table falls back to.
    /// </summary>
    public static List<(string Book, string Unit, string Model)> Models(bool firstOnly = false, string? root = null)
    {
        root ??= UnitModelPaths.Root;
        var list = new List<(string, string, string)>();
        if (!Directory.Exists(root)) return list;
        foreach (string bookDir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string book = Path.GetFileName(bookDir);
            if (book.StartsWith('_')) continue;
            foreach (string unitDir in Directory.EnumerateDirectories(bookDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                // The same order the library uses, so "first" is the same file the table falls back to.
                var files = Directory.EnumerateFiles(unitDir).Where(UnitModelPaths.IsModel).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                foreach (string f in firstOnly ? files.Take(1) : files) list.Add((book, Path.GetFileName(unitDir), f));
            }
        }
        return list;
    }
}
