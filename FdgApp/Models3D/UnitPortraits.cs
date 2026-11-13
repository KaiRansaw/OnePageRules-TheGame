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
    public static void Forget() { _scanned = false; _library = null; }

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
