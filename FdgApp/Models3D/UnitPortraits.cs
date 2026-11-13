namespace FdgRaylib.Models3D;

/// <summary>
/// Unit portraits photographed from the downloaded 3D models (Kai, 2026-11-12: an in-game way for players to get
/// pictures easily; angle "A", full body three-quarter, picked on the Portrait Angle Review page).
///
/// <para>
/// One picture per unit, of the unit's FIRST model file - the same one the table falls back to when a player doesn't
/// have the look an army picked - so everybody's portrait for a unit matches what they see on their table. Saved as
/// <c>UnitModels/_Portraits/&lt;Book&gt;/&lt;Unit&gt;.png</c>, mirroring the model folders; the underscore keeps the
/// model library from reading it as an army. A unit is found through <see cref="UnitModelLibrary.FilesFor"/>, so
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

    /// <summary>Where the portrait for the unit whose model file this is goes: _Portraits/&lt;Book&gt;/&lt;Unit&gt;.png.</summary>
    public static string PathFor(string modelFile, string? root = null)
    {
        string unitDir = Path.GetDirectoryName(Path.GetFullPath(modelFile))!;
        string unit = Path.GetFileName(unitDir);
        string book = Path.GetFileName(Path.GetDirectoryName(unitDir)!);
        return Path.Combine(root ?? UnitModelPaths.Root, FolderName, book, unit + ".png");
    }

    /// <summary>The model file a unit's portrait is taken of: its first version, or null with no download.</summary>
    public static string? SubjectFor(UnitModelLibrary library, string faction, string unit)
    {
        var files = library.FilesFor(faction, unit);
        return files.Count > 0 ? files[0] : null;
    }

    /// <summary>The saved portrait for a unit of this army, or null (no model downloaded, or no portrait made yet).</summary>
    public static string? Find(string faction, string unit)
    {
        if (Library is not { } lib || SubjectFor(lib, faction, unit) is not { } model) return null;
        string p = PathFor(model);
        return File.Exists(p) ? p : null;
    }

    /// <summary>Every unit folder under UnitModels with at least one model: (book, unit, its first model file).</summary>
    public static List<(string Book, string Unit, string Model)> Units(string? root = null)
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
                string? first = Directory.EnumerateFiles(unitDir).Where(UnitModelPaths.IsModel)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (first != null) list.Add((book, Path.GetFileName(unitDir), first));
            }
        }
        return list;
    }
}
