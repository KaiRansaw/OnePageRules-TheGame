using Newtonsoft.Json;

namespace FdgRaylib.Models3D;

/// <summary>
/// The ForceOrg Tabletop Simulator catalogue (workshop 3753014527): 7,069 Warhammer 40k models, each a list of
/// parts - a mesh (OBJ) link, a skin link, a tint and a place relative to its parent part. Units: 1 = 1 inch.
///
/// <para>
/// Personal use only - community uploads - so the file stays on the player's own computer, in the UnitModels
/// folder (git-ignored), and the repo never carries it. <see cref="UnitModelMap"/> says which of its models stand
/// for which OPR unit; <see cref="ModelDownloader"/> fetches the parts.
/// </para>
/// </summary>
public sealed class ForceOrgCatalog
{
    public const string FileName = "forceorg-models.json";

    public sealed class Part
    {
        [JsonProperty("mesh")] public string Mesh { get; set; } = "";
        [JsonProperty("texture")] public string Texture { get; set; } = "";
        [JsonProperty("color")] public float[] Color { get; set; } = { 1, 1, 1 };
        [JsonProperty("pos")] public float[] Pos { get; set; } = { 0, 0, 0 };
        [JsonProperty("rot")] public float[] Rot { get; set; } = { 0, 0, 0 };
        [JsonProperty("scale")] public float[] Scale { get; set; } = { 1, 1, 1 };
        /// <summary>Index of the parent part in the model's list; -1 = the model itself.</summary>
        [JsonProperty("parent")] public int Parent { get; set; } = -1;
    }

    public sealed class Model
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("group")] public int Group { get; set; }
        [JsonProperty("parts")] public List<Part> Parts { get; set; } = new();
    }

    private sealed class FileShape
    {
        [JsonProperty("source")] public string Source { get; set; } = "";
        [JsonProperty("groups")] public Dictionary<int, string> Groups { get; set; } = new();
        [JsonProperty("models")] public List<Model> Models { get; set; } = new();
    }

    public string Source { get; private init; } = "";
    /// <summary>The mod's own folders, by number: a few keywords each ("warrior, tyranid, cannon, hive").</summary>
    public IReadOnlyDictionary<int, string> Groups { get; private init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, Model> ById { get; private init; } = new Dictionary<int, Model>();

    public static ForceOrgCatalog Load(string path)
    {
        var f = JsonConvert.DeserializeObject<FileShape>(File.ReadAllText(path)) ?? new FileShape();
        return new ForceOrgCatalog { Source = f.Source, Groups = f.Groups, ById = f.Models.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First()) };
    }

    /// <summary>Where the catalogue is kept: the UnitModels folder.</summary>
    public static string DefaultPath => Path.Combine(UnitModelPaths.Root, FileName);
}
