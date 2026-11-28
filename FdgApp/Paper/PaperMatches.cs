using FdgRaylib.Models3D;
using Newtonsoft.Json;

namespace FdgRaylib.Paper;

/// <summary>
/// Which paper figures stand for which unit (Kai, 2026-11-27: the sheets aren't named, he matches them by hand), made in
/// the Asset Lab's Match paper models screen. Saved straight away to <c>UnitModels/_Paper/paper-matches.json</c>; "Send
/// my settings to GitHub" ships it (with the pictures) as Assets/PaperModels/paper-matches.json, so every download
/// stands the same figures.
///
/// <para>A unit is named by its game system, army book and unit name. A unit of a main game (Grimdark Future, Age of
/// Fantasy) also lists its names in the smaller games that share the book (<see cref="GfBookUnits.AliasesFor"/>:
/// Firefight / Star Quest, AoF: Skirmish / AoF: Quest), so one match covers all three.</para>
/// </summary>
public sealed class PaperMatches
{
    public const string FileName = "paper-matches.json";

    public sealed class UnitMatch
    {
        [JsonProperty("system")] public string System { get; set; } = "";
        [JsonProperty("book")] public string Book { get; set; } = "";
        [JsonProperty("unit")] public string Unit { get; set; } = "";
        /// <summary>Figure ids ("&lt;sheet&gt;/&lt;n&gt;"), in the order they were given - a squad mixes them in turn.</summary>
        [JsonProperty("figures")] public List<string> Figures { get; set; } = new();
        [JsonProperty("alsoIn")] public List<UnitModelMap.Alias> AlsoIn { get; set; } = new();
    }

    [JsonProperty("_comment")]
    public string Comment { get; set; } = "Kai's paper model matches from the Asset Lab (Match paper models): which cut-out figures (sheet/number) stand for each unit.";

    [JsonProperty("units")] public List<UnitMatch> Units { get; set; } = new();

    public static string LocalPath(string? root = null) => Path.Combine(root ?? PaperStore.LocalRoot, FileName);
    public static string ShippedPath => Path.Combine(PaperStore.ShippedRoot, FileName);

    public static PaperMatches Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonConvert.DeserializeObject<PaperMatches>(File.ReadAllText(path)) ?? new PaperMatches();
        }
        catch (JsonException) { }
        return new PaperMatches();
    }

    public void Save(string path)
    {
        Units.RemoveAll(u => u.Figures.Count == 0);
        Units.Sort((a, b) => string.Compare(a.System + "\u0001" + a.Book + "\u0001" + a.Unit, b.System + "\u0001" + b.Book + "\u0001" + b.Unit, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    public UnitMatch? Find(string system, string book, string unit) =>
        Units.FirstOrDefault(u => u.System == system && u.Book == book && u.Unit == unit);

    /// <summary>Gives figures to a unit (added after the ones it has; one already there stays where it is).</summary>
    public void Assign(string system, string book, string unit, IEnumerable<string> figureIds, IEnumerable<UnitModelMap.Alias>? alsoIn = null)
    {
        var u = Find(system, book, unit);
        if (u == null) Units.Add(u = new UnitMatch { System = system, Book = book, Unit = unit });
        foreach (string id in figureIds) if (!u.Figures.Contains(id)) u.Figures.Add(id);
        foreach (var a in alsoIn ?? Enumerable.Empty<UnitModelMap.Alias>())
            if (!u.AlsoIn.Any(x => x.System == a.System && x.Unit == a.Unit)) u.AlsoIn.Add(new UnitModelMap.Alias { System = a.System, Unit = a.Unit });
    }

    /// <summary>Takes figures off one unit.</summary>
    public void Unassign(string system, string book, string unit, IEnumerable<string> figureIds)
    {
        if (Find(system, book, unit) is not { } u) return;
        var gone = figureIds.ToHashSet();
        u.Figures.RemoveAll(gone.Contains);
        if (u.Figures.Count == 0) Units.Remove(u);
    }

    /// <summary>Takes figures off every unit (they were deleted, or "Unmatch" on the picture grid).</summary>
    public int UnassignEverywhere(IEnumerable<string> figureIds)
    {
        var gone = figureIds.ToHashSet();
        int n = 0;
        foreach (var u in Units) n += u.Figures.RemoveAll(gone.Contains);
        Units.RemoveAll(u => u.Figures.Count == 0);
        return n;
    }

    /// <summary>The units a figure is matched to.</summary>
    public IEnumerable<UnitMatch> UnitsOf(string figureId) => Units.Where(u => u.Figures.Contains(figureId));

    /// <summary>Whether any unit has this figure.</summary>
    public bool IsMatched(string figureId) => Units.Any(u => u.Figures.Contains(figureId));

    /// <summary>
    /// The shipped matches with this computer's laid over them: a unit both name takes this computer's list (a later
    /// change here wins); units only one has are kept.
    /// </summary>
    public static PaperMatches Merged(PaperMatches shipped, PaperMatches local)
    {
        var o = new PaperMatches();
        foreach (var u in shipped.Units) if (local.Find(u.System, u.Book, u.Unit) == null) o.Units.Add(u);
        o.Units.AddRange(local.Units);
        return o;
    }
}
