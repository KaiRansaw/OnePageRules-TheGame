using Newtonsoft.Json;

namespace FdgRaylib.Terrain;

/// <summary>
/// Built-in terrain, hand-built pieces (Kai, 2026-11-20/21: "hand-built pieces", designed "inside the game", in two
/// styles - "Let's put both in the game ... so people can have options"). A piece is a finished design for one terrain
/// kind: a footprint and the models placed on it, built in the Asset Lab's Terrain Workshop and reused wherever a map
/// puts that kind. Plan: project doc <c>claude/built-in-terrain-plan.md</c>.
///
/// <para>Pieces are files in Assets (<see cref="Folder"/>, one JSON per piece), so they ship with the game and Kai's
/// "Send my settings to GitHub" carries the ones he makes (<see cref="FdgRaylib.Models3D.SharedSettingsSync"/>).</para>
/// </summary>
public static class TerrainPieces
{
    /// <summary>The two model sets (Settings: Terrain style).</summary>
    public const string Stylised = "stylised", Realistic = "realistic";
    public static readonly string[] Styles = { Stylised, Realistic };

    /// <summary>Which games a piece suits: Grimdark Future games, Age of Fantasy games, or both (nature).</summary>
    public const string ForGf = "gf", ForAof = "aof", ForBoth = "both";

    public const string RelativeFolder = "TerrainPieces";
    private static string? _folder;
    /// <summary>Where pieces live. Settable so tests can point it at a scratch folder.</summary>
    public static string Folder { get => _folder ?? Path.Combine(AppAssets.AssetsFolder, RelativeFolder); set { _folder = value; Reload(); } }

    public sealed class Item
    {
        /// <summary>The model, relative to the game's terrain folder (res://terrain/): "stylised/nature/models/Pine_3.gltf".</summary>
        [JsonProperty("model")] public string Model { get; set; } = "";
        /// <summary>Where it stands, inches from the footprint's middle (+x along the width, +z along the depth).</summary>
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("z")] public float Z { get; set; }
        /// <summary>Raised (or sunk, negative) off the ground, inches.</summary>
        [JsonProperty("y")] public float Y { get; set; }
        /// <summary>Turned about the vertical, degrees.</summary>
        [JsonProperty("turn")] public float Turn { get; set; }
        /// <summary>Inches per model unit (0.7 = a model made in metres at 28mm scale).</summary>
        [JsonProperty("scale")] public float Scale { get; set; } = 0.7f;

        public Item Copy() => (Item)MemberwiseClone();
    }

    public sealed class Piece
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "New piece";
        /// <summary>The book's terrain kind it is made for (Forest, Ruins, Building...: <see cref="TerrainLooks"/> kinds).</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = "Forest";
        [JsonProperty("style")] public string Style { get; set; } = Stylised;
        [JsonProperty("game")] public string Game { get; set; } = ForBoth;
        /// <summary>The footprint it was built on, inches. A map's piece of another size stretches it a little.</summary>
        [JsonProperty("w")] public float W { get; set; } = 6;
        [JsonProperty("d")] public float D { get; set; } = 6;
        [JsonProperty("round")] public bool Round { get; set; }
        [JsonProperty("items")] public List<Item> Items { get; set; } = new();

        /// <summary>The look id a map stores for this piece.</summary>
        [JsonIgnore] public string LookId => LookPrefix + Id;

        public Piece Copy()
        {
            var p = (Piece)MemberwiseClone();
            p.Items = Items.Select(i => i.Copy()).ToList();
            return p;
        }

        public bool Suits(TerrainLooks.ETheme theme) =>
            Game == ForBoth || (theme == TerrainLooks.ETheme.Grimdark ? Game == ForGf : Game == ForAof);
    }

    public const string LookPrefix = "piece:";

    /// <summary>
    /// A single model placed on its own (Kai, 2026-11-23: "place the single models as I want in the map"): look
    /// "model:&lt;path&gt;|&lt;w&gt;|&lt;d&gt;|&lt;cx&gt;|&lt;cz&gt;" - the model's file (under the game's terrain folder), its own
    /// footprint in inches at the Workshop's scale, and where its middle sits from the file's origin. Played and drawn
    /// as a one-item piece, so it is the same for every player.
    /// </summary>
    public const string ModelPrefix = "model:";

    /// <summary>The Workshop's default model scale (model units -> inches).</summary>
    public const float ModelScale = 0.7f;

    public static string ModelLook(string path, float w, float d, float cx, float cz) =>
        FormattableString.Invariant($"{ModelPrefix}{path}|{w:0.###}|{d:0.###}|{cx:0.###}|{cz:0.###}");

    public static bool IsModelLook(string? look) => look != null && look.StartsWith(ModelPrefix, StringComparison.Ordinal);

    /// <summary>The single-model piece a "model:" look describes, or null when it isn't one.</summary>
    public static Piece? ModelPiece(string? look)
    {
        if (!IsModelLook(look)) return null;
        string[] parts = look![ModelPrefix.Length..].Split('|');
        if (parts.Length < 3) return null;
        float F(int i) => i < parts.Length && float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        string path = parts[0];
        return new Piece
        {
            Id = look, Name = Path.GetFileNameWithoutExtension(path).Replace('_', ' '), W = MathF.Max(0.25f, F(1)), D = MathF.Max(0.25f, F(2)),
            Items = new List<Item> { new() { Model = path, X = -F(3), Z = -F(4), Scale = ModelScale } },
        };
    }

    private static List<Piece>? _all;
    private static readonly object Gate = new();

    /// <summary>Every piece in Assets, by name.</summary>
    public static IReadOnlyList<Piece> All
    {
        get { lock (Gate) return _all ??= LoadAll(); }
    }

    public static void Reload() { lock (Gate) _all = null; }

    public static Piece? ById(string? lookOrId)
    {
        if (string.IsNullOrEmpty(lookOrId)) return null;
        if (ModelPiece(lookOrId) is { } single) return single;
        string id = lookOrId.StartsWith(LookPrefix, StringComparison.Ordinal) ? lookOrId[LookPrefix.Length..] : lookOrId;
        return All.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// The piece to draw for a map piece of this kind and size, in this style and game, or null (draw it the old way).
    /// The closest in size wins (footprint area, then shape); one more than half again too big or small is never used.
    /// Several close ones are picked by the piece's seed so a map's forests aren't all the same forest.
    /// </summary>
    public static Piece? Pick(string kind, string style, TerrainLooks.ETheme theme, float w, float d, bool round, int seed)
    {
        var fits = All.Where(p => p.Style == style && p.Suits(theme) && p.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
                                  && p.Items.Count > 0 && Stretch(p, w, d) is >= 0.66f and <= 1.5f)
            .OrderBy(p => MathF.Abs(MathF.Log(Stretch(p, w, d))) + (p.Round == round ? 0 : 0.3f)).ToList();
        if (fits.Count == 0) return null;
        float best = MathF.Abs(MathF.Log(Stretch(fits[0], w, d)));
        var close = fits.Where(p => MathF.Abs(MathF.Log(Stretch(p, w, d))) <= best + 0.25f).ToList();
        return close[(int)((uint)seed % (uint)close.Count)];
    }

    /// <summary>How much a piece is stretched to fill a w x d footprint (the larger of its two stretches, by size).</summary>
    public static float Stretch(Piece p, float w, float d)
    {
        float sw = w / MathF.Max(0.1f, p.W), sd = d / MathF.Max(0.1f, p.D);
        return MathF.Sqrt(sw * sd);
    }

    public static string NewId(string name)
    {
        string slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "piece";
        if (slug.Length > 40) slug = slug[..40];
        string id = slug;
        for (int n = 2; All.Any(p => p.Id == id); n++) id = slug + "-" + n;
        return id;
    }

    public static string PathOf(Piece p) => Path.Combine(Folder, p.Style, p.Id + ".json");

    /// <summary>Saves a piece (new or changed). Returns an error line, or null.</summary>
    public static string? Save(Piece p)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(p.Id)) p.Id = NewId(p.Name);
            foreach (string style in Styles) // a piece whose style changed moves folder
            {
                string old = Path.Combine(Folder, style, p.Id + ".json");
                if (style != p.Style && File.Exists(old)) File.Delete(old);
            }
            string path = PathOf(p);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(p, Formatting.Indented));
            Reload();
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    public static string? Delete(Piece p)
    {
        try
        {
            foreach (string style in Styles)
            {
                string f = Path.Combine(Folder, style, p.Id + ".json");
                if (File.Exists(f)) File.Delete(f);
            }
            Reload();
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    private static List<Piece> LoadAll()
    {
        var list = new List<Piece>();
        if (!Directory.Exists(Folder)) return list;
        foreach (string f in Directory.EnumerateFiles(Folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                if (JsonConvert.DeserializeObject<Piece>(File.ReadAllText(f)) is { } p && p.Id.Length > 0) list.Add(p);
            }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        return list.OrderBy(p => p.Style).ThenBy(p => p.Kind).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
