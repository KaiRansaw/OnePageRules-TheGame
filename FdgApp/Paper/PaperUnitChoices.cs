using FDG;
using FdgRaylib.Models3D;
using FdgRaylib.Pictures;

namespace FdgRaylib.Paper;

/// <summary>
/// What the Match paper models screen offers to match against: every game system, its army books in Army Forge's
/// faction groups, each book's units - and, for a unit of a main game, its names in the smaller games that share the
/// book (Grimdark Future -> Firefight / Star Quest, Age of Fantasy -> Skirmish / Quest), so one match covers all three.
/// </summary>
public sealed class PaperUnitChoices
{
    public sealed record GameChoice(string Name, string Folder, EGameSystem System, bool Main);

    /// <summary>The six games, main games first (their matches carry to the smaller games).</summary>
    public static readonly GameChoice[] Games =
    {
        new("Grimdark Future", "Grimdark Future", EGameSystem.GrimdarkFuture, true),
        new("Age of Fantasy", "Age of Fantasy", EGameSystem.AgeOfFantasy, true),
        new("GF Firefight", "GF Firefight", EGameSystem.GFFirefight, false),
        new("GF Star Quest", "GF Star Quest", EGameSystem.GFStarQuest, false),
        new("AoF Skirmish", "AoF Skirmish", EGameSystem.AoFSkirmish, false),
        new("AoF Quest", "AoF Quest", EGameSystem.AoFQuest, false),
    };

    private readonly string _booksFolder;
    private readonly Dictionary<string, SortedDictionary<string, List<string>>> _books = new();
    private Dictionary<string, SortedDictionary<string, List<string>>>? _others;

    public PaperUnitChoices(string? booksFolder = null) => _booksFolder = booksFolder ?? Path.Combine(AppAssets.AssetsFolder, "Books");

    /// <summary>A game's books -> units (read once).</summary>
    public SortedDictionary<string, List<string>> Books(GameChoice g)
    {
        if (!_books.TryGetValue(g.Name, out var b)) _books[g.Name] = b = GfBookUnits.Load(Path.Combine(_booksFolder, g.Folder));
        return b;
    }

    /// <summary>A game's books in Army Forge's faction groups.</summary>
    public IReadOnlyList<ArmyGroups.Group> Groups(GameChoice g) => ArmyGroups.Arrange(g.System, Books(g).Keys);

    public IReadOnlyList<string> Units(GameChoice g, string book) => Books(g).TryGetValue(book, out var u) ? u : Array.Empty<string>();

    /// <summary>A main game's unit's names in the other games (none for a smaller game's own unit).</summary>
    public List<UnitModelMap.Alias> AliasesFor(GameChoice g, string book, string unit)
    {
        if (!g.Main) return new();
        _others ??= GfBookUnits.LoadOtherGames(_booksFolder);
        // Only this family's smaller games: GF's books never alias into AoF's (bar the shared daemon names, which are
        // different armies there).
        var family = g.System == EGameSystem.GrimdarkFuture ? new[] { "GF Firefight", "GF Star Quest" } : new[] { "AoF Skirmish", "AoF Quest" };
        return GfBookUnits.AliasesFor(book, unit, _others.Where(kv => family.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    /// <summary>Units whose name holds <paramref name="search"/>, in every book of the game (book, unit), A-Z.</summary>
    public IEnumerable<(string Book, string Unit)> Search(GameChoice g, string search)
    {
        string k = search.Trim();
        if (k.Length == 0) yield break;
        foreach (var (book, units) in Books(g))
            foreach (string u in units)
                if (u.Contains(k, StringComparison.OrdinalIgnoreCase) || book.Contains(k, StringComparison.OrdinalIgnoreCase)) yield return (book, u);
    }
}
