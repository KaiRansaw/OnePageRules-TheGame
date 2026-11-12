using Newtonsoft.Json;

namespace FdgRaylib.Models3D;

/// <summary>
/// Which ForceOrg catalogue models stand for each Grimdark Future unit (<c>Assets/Models3D/gf-unit-models.json</c>,
/// built by <c>tools/unit-models/build_map.py</c> from onepagefan.wiki's "Unit Equivalences" tables). Units are named
/// as in the current GF army books; <see cref="Entry.AlsoIn"/> gives the same unit's name in Firefight and Star Quest.
/// Models are named by catalogue id and name only - no links.
/// </summary>
public sealed class UnitModelMap
{
    public const string RelativePath = "Models3D/gf-unit-models.json";

    public sealed class ModelRef
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = "";
    }

    public sealed class Alias
    {
        [JsonProperty("system")] public string System { get; set; } = "";
        [JsonProperty("unit")] public string Unit { get; set; } = "";
    }

    public sealed class Entry
    {
        [JsonProperty("book")] public string Book { get; set; } = "";
        [JsonProperty("unit")] public string Unit { get; set; } = "";
        /// <summary>The Warhammer model(s) the wiki names, as written there.</summary>
        [JsonProperty("warhammer")] public string Warhammer { get; set; } = "";
        [JsonProperty("alsoIn")] public List<Alias> AlsoIn { get; set; } = new();
        /// <summary>More models matched than were kept (the list is capped): worth a look in the Asset Lab.</summary>
        [JsonProperty("tooBroad")] public bool TooBroad { get; set; }
        [JsonProperty("models")] public List<ModelRef> Models { get; set; } = new();
    }

    [JsonProperty("units")] public List<Entry> Units { get; set; } = new();

    /// <summary>Every book that has at least one unit with models, A-Z.</summary>
    public IEnumerable<string> Books => Units.Select(u => u.Book).Distinct().OrderBy(b => b, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<Entry> ForBook(string book) => Units.Where(u => u.Book == book).OrderBy(u => u.Unit, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The unit list. With no <paramref name="path"/> (the game's own list), Kai's hand matches are laid over it - the
    /// shipped <see cref="ModelMatches"/> file, then this computer's (<paramref name="matchesRoot"/>, default the
    /// UnitModels folder). A list read from a given path is returned as the file has it.
    /// </summary>
    public static UnitModelMap Load(string? path = null, string? matchesRoot = null)
    {
        bool own = path == null;
        path ??= Path.Combine(AppAssets.AssetsFolder, RelativePath);
        var map = JsonConvert.DeserializeObject<UnitModelMap>(File.ReadAllText(path)) ?? new UnitModelMap();
        if (own)
        {
            ModelMatches.Load(ModelMatches.ShippedPath).ApplyTo(map);
            ModelMatches.Load(ModelMatches.LocalPath(matchesRoot)).ApplyTo(map);
        }
        return map;
    }

    /// <summary>
    /// Where one model of one unit is saved: <c>UnitModels/&lt;Book&gt;/&lt;Unit&gt;/&lt;Model name&gt; (&lt;id&gt;).glb</c>.
    /// The id keeps two catalogue models with the same name apart, and keeps the name stable between downloads.
    /// </summary>
    public static string FileFor(string root, string book, string unit, ModelRef model) =>
        Path.Combine(root, SafeName(book), SafeName(unit), $"{SafeName(model.Name)} ({model.Id}).glb");

    /// <summary>A name made safe for a Windows file or folder name.</summary>
    public static string SafeName(string s)
    {
        var bad = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }));
        string clean = new string(s.Select(c => bad.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        clean = string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.', ' ');
        return clean.Length == 0 ? "_" : clean;
    }
}
