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
