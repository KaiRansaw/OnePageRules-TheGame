using System.Text.Json;
using FDG;

namespace FdgRaylib.Pictures;

/// <summary>
/// A quick look at the bundled army books without loading them as books: which armies a game has, and the first few
/// unit names of one (for the picture tiles' made-from-units banner when Kai hasn't set a picture). Cached per game.
/// </summary>
public static class BookIndex
{
    public static string BookFolder(EGameSystem system) => system switch
    {
        EGameSystem.AgeOfFantasy => "Age of Fantasy",
        EGameSystem.AoFSkirmish => "AoF Skirmish",
        EGameSystem.AoFQuest => "AoF Quest",
        EGameSystem.GFFirefight => "GF Firefight",
        EGameSystem.GFStarQuest => "GF Star Quest",
        _ => "Grimdark Future",
    };

    public sealed record Book(string Name, IReadOnlyList<string> Units);

    private static readonly Dictionary<EGameSystem, List<Book>> Cache = new();
    private static readonly object Gate = new();

    /// <summary>Every bundled army book of a game (name + unit names in book order).</summary>
    public static IReadOnlyList<Book> Books(EGameSystem system)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(system, out var hit)) return hit;
            var list = new List<Book>();
            string folder = Path.Combine(AppAssets.AssetsFolder, "Books", BookFolder(system));
            if (Directory.Exists(folder))
                foreach (string f in Directory.GetFiles(folder, "*.fdgbook"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(f));
                        string name = doc.RootElement.GetProperty("name").GetString() ?? Path.GetFileNameWithoutExtension(f);
                        var units = new List<string>();
                        if (doc.RootElement.TryGetProperty("units", out JsonElement us) && us.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement u in us.EnumerateArray())
                                if (u.TryGetProperty("name", out JsonElement n) && n.GetString() is { Length: > 0 } un) units.Add(un);
                        list.Add(new Book(name, units));
                    }
                    catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
                }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Cache[system] = list;
            return list;
        }
    }

    /// <summary>Every army name in a family's three games (one picture each).</summary>
    public static IReadOnlyList<string> ArmiesOf(MenuPictures.EFamily family) =>
        (family == MenuPictures.EFamily.AgeOfFantasy
            ? new[] { EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest }
            : new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest })
        .SelectMany(s => Books(s).Select(b => b.Name)).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
}
