using Newtonsoft.Json;

namespace FdgRaylib.Models3D;

/// <summary>
/// Kai's own model-to-unit matches, made in the Asset Lab's Match models screen: catalogue models given to an OPR unit
/// the wiki's tables didn't cover, and automatic matches taken away because they were wrong. Laid over
/// <see cref="UnitModelMap"/> when it loads, so the downloader, Choose models and the battle table all follow them.
///
/// <para><b>Two copies, like the facing file.</b> Every change is saved straight away to <see cref="FileName"/> in the
/// UnitModels folder (this computer only). "Save matches for everyone" tells Kai to say so; Claude then commits that
/// file as <see cref="ShippedRelativePath"/>, and everyone's downloads use the same matches. The shipped copy is laid
/// on first and the local one over it, so a later local change wins, and laying the same file on twice changes
/// nothing.</para>
///
/// <para>Per unit it keeps two lists: models <see cref="UnitMatch.Add"/>ed and model ids <see cref="UnitMatch.Remove"/>d.
/// A model is in at most one of them: giving a removed model back takes it off the remove list, and removing an added
/// one takes it off the add list (and still lists it as removed, in case an automatic match or the shipped file also
/// had it).</para>
/// </summary>
public sealed class ModelMatches
{
    public const string FileName = "model-matches.json";
    public const string ShippedRelativePath = "Models3D/model-matches.json";

    public sealed class UnitMatch
    {
        [JsonProperty("book")] public string Book { get; set; } = "";
        [JsonProperty("unit")] public string Unit { get; set; } = "";
        [JsonProperty("add")] public List<UnitModelMap.ModelRef> Add { get; set; } = new();
        [JsonProperty("remove")] public List<int> Remove { get; set; } = new();
        /// <summary>The unit's name in Firefight / Star Quest, so a hand match shows there too.</summary>
        [JsonProperty("alsoIn")] public List<UnitModelMap.Alias> AlsoIn { get; set; } = new();
    }

    [JsonProperty("_comment")]
    public string Comment { get; set; } =
        "Kai's model matches from the Asset Lab (Match models). Laid over gf-unit-models.json: 'add' gives a catalogue model to the unit, 'remove' takes one away.";

    [JsonProperty("units")] public List<UnitMatch> Units { get; set; } = new();

    /// <summary>How many models were given or taken away in all.</summary>
    [JsonIgnore] public int Changes => Units.Sum(u => u.Add.Count + u.Remove.Count);

    public static string LocalPath(string? root = null) => Path.Combine(root ?? UnitModelPaths.Root, FileName);
    public static string ShippedPath => Path.Combine(AppAssets.AssetsFolder, ShippedRelativePath);

    /// <summary>The file at <paramref name="path"/>, or an empty set when there is none (or it can't be read).</summary>
    public static ModelMatches Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonConvert.DeserializeObject<ModelMatches>(File.ReadAllText(path)) ?? new ModelMatches();
        }
        catch (JsonException) { }
        return new ModelMatches();
    }

    public void Save(string path)
    {
        Units.RemoveAll(u => u.Add.Count == 0 && u.Remove.Count == 0);
        foreach (var u in Units) if (u.Add.Count == 0) u.AlsoIn.Clear(); // only a unit given models needs its other names
        Units.Sort((a, b) => string.Compare(a.Book + "\u0001" + a.Unit, b.Book + "\u0001" + b.Unit, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    private UnitMatch For(string book, string unit)
    {
        var u = Units.FirstOrDefault(x => x.Book == book && x.Unit == unit);
        if (u == null) Units.Add(u = new UnitMatch { Book = book, Unit = unit });
        return u;
    }

    /// <summary>Gives a catalogue model to a unit (<paramref name="alsoIn"/>: its name in the other games).</summary>
    public void Assign(string book, string unit, UnitModelMap.ModelRef model, IEnumerable<UnitModelMap.Alias>? alsoIn = null)
    {
        var u = For(book, unit);
        foreach (var a in alsoIn ?? Enumerable.Empty<UnitModelMap.Alias>())
            if (!u.AlsoIn.Any(x => x.System == a.System && x.Unit == a.Unit)) u.AlsoIn.Add(new UnitModelMap.Alias { System = a.System, Unit = a.Unit });
        u.Remove.Remove(model.Id);
        if (u.Add.All(m => m.Id != model.Id)) u.Add.Add(new UnitModelMap.ModelRef { Id = model.Id, Name = model.Name });
    }

    /// <summary>Takes a model away from a unit (an automatic match or one given here).</summary>
    public void Unassign(string book, string unit, int id)
    {
        var u = For(book, unit);
        u.Add.RemoveAll(m => m.Id == id);
        if (!u.Remove.Contains(id)) u.Remove.Add(id);
    }

    /// <summary>Lays these matches over the unit list: removes first, then adds; a unit the list doesn't have yet is added to it.</summary>
    public void ApplyTo(UnitModelMap map)
    {
        foreach (var m in Units)
        {
            var e = map.Units.FirstOrDefault(x => x.Book == m.Book && x.Unit == m.Unit);
            if (e == null)
            {
                if (m.Add.Count == 0) continue;
                map.Units.Add(e = new UnitModelMap.Entry { Book = m.Book, Unit = m.Unit, Warhammer = "(matched by hand)" });
            }
            e.Models.RemoveAll(r => m.Remove.Contains(r.Id));
            foreach (var a in m.AlsoIn)
                if (!e.AlsoIn.Any(x => x.System == a.System && x.Unit == a.Unit)) e.AlsoIn.Add(new UnitModelMap.Alias { System = a.System, Unit = a.Unit });
            foreach (var r in m.Add)
                if (e.Models.All(x => x.Id != r.Id)) e.Models.Add(new UnitModelMap.ModelRef { Id = r.Id, Name = r.Name });
        }
    }
}
