using Newtonsoft.Json;

namespace FdgRaylib.Models3D;

/// <summary>
/// Every unit of every bundled Grimdark Future army book, by book - so the Match models screen can list the units that
/// have no model yet, not just the ones the wiki's tables matched. Firefight and Star Quest units find their models
/// through the GF unit (<see cref="UnitModelMap.Entry.AlsoIn"/>), so GF's books are the list to match against.
/// </summary>
public static class GfBookUnits
{
    private sealed class BookShape
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("units")] public List<UnitShape> Units { get; set; } = new();
    }

    private sealed class UnitShape
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
    }

    public static string DefaultFolder => Path.Combine(AppAssets.AssetsFolder, "Books", "Grimdark Future");

    /// <summary>
    /// The other games whose units use a main game's models, by the system name the unit list uses: Firefight and Star
    /// Quest use Grimdark Future's, AoF: Skirmish and AoF: Quest use Age of Fantasy's (Kai, 2026-11-13). A book is
    /// found by its name, and the two families name their books differently (bar the daemon books, which share).
    /// </summary>
    public static readonly (string System, string Folder)[] OtherGames =
    {
        ("GF Firefight", "GF Firefight"),
        ("GF Star Quest", "GF Star Quest"),
        ("AoF Skirmish", "AoF Skirmish"),
        ("AoF Quest", "AoF Quest"),
    };

    /// <summary>The Age of Fantasy books' folder: the AoF models (Stormvault) are matched against these.</summary>
    public static string AofFolder => Path.Combine(AppAssets.AssetsFolder, "Books", "Age of Fantasy");

    /// <summary>Every Grimdark Future and Age of Fantasy book (book -> units), for the Match models screen.</summary>
    public static SortedDictionary<string, List<string>> LoadAll(string? gfFolder = null, string? aofFolder = null)
    {
        var all = Load(gfFolder);
        foreach (var (book, units) in Load(aofFolder ?? AofFolder))
            all[book] = all.TryGetValue(book, out var had)
                ? had.Concat(units).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : units;
        return all;
    }

    /// <summary>Whether a book is an Age of Fantasy one (its units take Age of Sigmar models).</summary>
    public static bool IsAof(string book, string? aofFolder = null) => AofBooks(aofFolder).Contains(book);

    private static HashSet<string>? _aofBooks;
    private static HashSet<string> AofBooks(string? folder)
    {
        if (folder == null && _aofBooks != null) return _aofBooks;
        var set = new HashSet<string>(Load(folder ?? AofFolder).Keys, StringComparer.OrdinalIgnoreCase);
        if (folder == null) _aofBooks = set;
        return set;
    }

    /// <summary>Firefight's and Star Quest's books (system -> book -> units), for <see cref="AliasesFor"/>.</summary>
    public static Dictionary<string, SortedDictionary<string, List<string>>> LoadOtherGames(string? booksFolder = null)
    {
        booksFolder ??= Path.Combine(AppAssets.AssetsFolder, "Books");
        return OtherGames.ToDictionary(g => g.System, g => Load(Path.Combine(booksFolder, g.Folder)));
    }

    /// <summary>
    /// A GF unit's name in Firefight and Star Quest: the same army book, and a unit with the same name or the same
    /// name made singular ("Shooter Grunts" -> Star Quest's "Shooter Grunt"). The rule the shipped unit list was built
    /// with (tools/unit-models/build_map.py), so a hand match links up the same way an automatic one does.
    /// </summary>
    public static List<UnitModelMap.Alias> AliasesFor(string book, string unit, Dictionary<string, SortedDictionary<string, List<string>>> others)
    {
        var found = new List<UnitModelMap.Alias>();
        foreach (var (system, books) in others)
        {
            if (!books.TryGetValue(book, out var units)) continue;
            string k = Key(unit), ks = Key(StemAll(unit));
            string? hit = units.FirstOrDefault(u => Key(u) == k || Key(StemAll(u)) == ks);
            if (hit != null) found.Add(new UnitModelMap.Alias { System = system, Unit = hit });
        }
        return found;
    }

    private static string Key(string s) => new string(s.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9').ToArray());

    private static string StemAll(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ').Select(Stem));

    private static string Stem(string w)
    {
        foreach (var (suf, rep) in new[] { ("ies", "y"), ("ves", "f"), ("es", "e"), ("s", "") })
            if (w.Length > 4 && w.EndsWith(suf, StringComparison.Ordinal)) return w[..^suf.Length] + rep;
        return w;
    }

    /// <summary>Book name -> its unit names (A-Z, each once). Unreadable files are skipped.</summary>
    public static SortedDictionary<string, List<string>> Load(string? folder = null)
    {
        var all = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        folder ??= DefaultFolder;
        if (!Directory.Exists(folder)) return all;
        foreach (string f in Directory.EnumerateFiles(folder, "*.fdgbook"))
        {
            BookShape? b;
            try { b = JsonConvert.DeserializeObject<BookShape>(File.ReadAllText(f)); }
            catch (JsonException) { continue; }
            if (b == null || b.Name.Length == 0) continue;
            all[b.Name] = b.Units.Select(u => u.Name).Where(n => n.Length > 0).Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return all;
    }
}
