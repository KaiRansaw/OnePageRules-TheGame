namespace FdgRaylib.Models3D;

/// <summary>
/// Which downloaded model files stand for a unit on the table: <c>UnitModels/&lt;Book&gt;/&lt;Unit&gt;/*.glb</c>, as the
/// Download models window saves them. Found by the army's faction and the unit's name, ignoring case and punctuation.
/// Firefight and Star Quest name some units differently ("Assault Grunt" for GF's "Assault Grunts"); the unit list's
/// <see cref="UnitModelMap.Entry.AlsoIn"/> names lead those to the GF folder. A unit from an allied book (an army that
/// mixes books) is looked for in every book when its own army's book has no such unit.
/// </summary>
public sealed class UnitModelLibrary
{
    // normalised book -> normalised unit -> files, A-Z
    private readonly Dictionary<string, Dictionary<string, List<string>>> _byBook = new();
    // normalised unit name in any game -> (book, GF unit) folders it maps to
    private readonly Dictionary<string, List<(string Book, string Unit)>> _aliases = new();

    public int FileCount { get; private set; }

    public static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Reads the folder once (a game does this at start). <paramref name="map"/> adds the other games' names.</summary>
    public static UnitModelLibrary Scan(string root, UnitModelMap? map = null)
    {
        var lib = new UnitModelLibrary();
        if (Directory.Exists(root))
        {
            foreach (string bookDir in Directory.EnumerateDirectories(root))
            {
                string book = Path.GetFileName(bookDir);
                if (book.StartsWith('_')) continue; // _cache, _Samples, _Dropped
                var units = new Dictionary<string, List<string>>();
                foreach (string unitDir in Directory.EnumerateDirectories(bookDir))
                {
                    var files = Directory.EnumerateFiles(unitDir).Where(UnitModelPaths.IsModel)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                    if (files.Count == 0) continue;
                    units[Norm(Path.GetFileName(unitDir))] = files;
                    lib.FileCount += files.Count;
                }
                if (units.Count > 0) lib._byBook[Norm(book)] = units;
            }
        }
        if (map != null)
            foreach (var e in map.Units)
                foreach (var a in e.AlsoIn)
                {
                    string k = Norm(a.Unit);
                    if (!lib._aliases.TryGetValue(k, out var list)) lib._aliases[k] = list = new();
                    list.Add((e.Book, e.Unit));
                }
        return lib;
    }

    /// <summary>The model files for a unit of this army, or an empty list (the table then draws its token).</summary>
    public IReadOnlyList<string> FilesFor(string faction, string unit)
    {
        string b = Norm(faction), u = Norm(unit);
        if (Find(b, u) is { } direct) return direct;
        // Another game's name for the unit (Star Quest's singular, Firefight's own wording).
        if (_aliases.TryGetValue(u, out var targets))
            foreach (var (book, gfUnit) in targets.OrderByDescending(t => Norm(t.Book) == b))
                if (Find(Norm(book), Norm(gfUnit)) is { } viaAlias) return viaAlias;
        // An allied unit from another book: take it when exactly one book has a unit by that name.
        var others = _byBook.Where(kv => kv.Key != b && kv.Value.ContainsKey(u)).Select(kv => kv.Value[u]).ToList();
        return others.Count == 1 ? others[0] : Array.Empty<string>();
    }

    private List<string>? Find(string book, string unit) =>
        _byBook.TryGetValue(book, out var units) && units.TryGetValue(unit, out var files) ? files : null;
}
