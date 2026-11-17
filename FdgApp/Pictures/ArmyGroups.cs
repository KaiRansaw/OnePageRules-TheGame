using System.Text.Json;
using FDG;

namespace FdgRaylib.Pictures;

/// <summary>
/// Army Forge's own grouping of the army books (Kai, 2026-11-16: the army picker grid is "grouped like Army Forge"):
/// each official book's <c>raceGroup</c> ("Humans", "Elves", ... - "Unknown" when Army Forge gives none), in Army
/// Forge's book order, per game. Read from <c>Assets/Books/race-groups.json</c>, written by
/// <c>tools/army-groups/fetch_race_groups.py</c> straight from the Army Forge API. Groups are shown in plain ascending
/// name order, as Army Forge's own pages do (same rule as <see cref="Quest.QuestDataSet.FactionGroups"/>).
/// </summary>
public static class ArmyGroups
{
    public const string Unknown = "Unknown";

    public sealed record Group(string Name, IReadOnlyList<string> Books);

    private static Dictionary<string, List<(string Name, string Group, string About)>>? _data;
    private static readonly object Gate = new();

    private static Dictionary<string, List<(string Name, string Group, string About)>> Data()
    {
        lock (Gate)
        {
            if (_data != null) return _data;
            _data = new Dictionary<string, List<(string, string, string)>>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(AppAssets.AssetsFolder, "Books", "race-groups.json");
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty sys in doc.RootElement.GetProperty("systems").EnumerateObject())
                    _data[sys.Name] = sys.Value.EnumerateArray()
                        .Select(b => (b.GetProperty("name").GetString() ?? "", b.GetProperty("group").GetString() ?? Unknown,
                            b.TryGetProperty("about", out JsonElement a) ? a.GetString() ?? "" : "")).ToList();
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { }
            return _data;
        }
    }

    /// <summary>Army Forge's raceGroup for a book in a game; <see cref="Unknown"/> when it isn't listed.</summary>
    public static string GroupOf(EGameSystem system, string book) =>
        Data().TryGetValue(GameSystemInfo.Slug(system), out var list) && list.FirstOrDefault(b => b.Name == book) is { Name: not null } hit && hit.Name.Length > 0
            ? hit.Group : Unknown;

    /// <summary>
    /// Army Forge's description of the army ("background", ASCII-folded by the fetch script); "" when Army Forge has none
    /// (a few books, e.g. Wormhole Daemons) or the book isn't listed.
    /// </summary>
    public static string AboutOf(EGameSystem system, string book) =>
        Data().TryGetValue(GameSystemInfo.Slug(system), out var list) && list.FirstOrDefault(b => b.Name == book) is { Name: { Length: > 0 } } hit
            ? hit.About : "";

    /// <summary>
    /// The given books folded into Army Forge's groups: groups in name order, books in Army Forge's order inside each
    /// (books Army Forge doesn't list go last in their group, by name).
    /// </summary>
    public static IReadOnlyList<Group> Arrange(EGameSystem system, IEnumerable<string> books)
    {
        Data().TryGetValue(GameSystemInfo.Slug(system), out var list);
        list ??= new();
        int Order(string name) { int i = list.FindIndex(b => b.Name == name); return i < 0 ? int.MaxValue : i; }
        return books.Distinct()
            .GroupBy(b => GroupOf(system, b))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Group(g.Key, g.OrderBy(Order).ThenBy(b => b, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }
}
