using FDG;
using FdgRaylib.Models3D;
using Newtonsoft.Json;

namespace FdgRaylib.Terrain;

/// <summary>
/// The downloaded Warhammer terrain pieces, each matched to a terrain kind (Kai, 2026-11-14: "match the map assets to
/// what kind of terrain they are, and also be able to make maps with them"). Kai's calls: <b>the model is the piece</b>
/// (placed in the Map Maker, its rules from the kind it is matched to, its size from the model), matched on a grid
/// screen with guesses filled in, and the mod's five sets used as kits.
///
/// <para>Kept beside the models, in <c>UnitModels/Terrain - Warhammer/terrain-matches.json</c> - on the player's
/// computer, like the models. A map stores a piece's model as its Look, <c>wh:&lt;id&gt;</c>; a computer without that
/// model draws the plain stand-in for the piece's kind, and the rules are the same either way.</para>
/// </summary>
public sealed class WarhammerTerrainLibrary
{
    public const string FileName = "terrain-matches.json";
    public const string LookPrefix = "wh:";

    /// <summary>The kind for a piece with no rules at all - scatter that is only there to look at.</summary>
    public const string Decoration = "Decoration";

    /// <summary>The kinds a piece can be matched to: the book's list, plus decoration.</summary>
    public static IReadOnlyList<(string Id, string Name)> Kinds { get; } =
        TerrainBook.Kinds.Select(k => (k.Id, k.Name)).Append((Decoration, "Decoration (no rules)")).ToList();

    /// <summary>What Kai decides about one piece. Sizes are measured from the model (inches) by the match screen.</summary>
    public sealed class Match
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("kind")] public string Kind { get; set; } = "";
        [JsonProperty("hidden")] public bool Hidden { get; set; }
        /// <summary>True once Kai has looked at it (a guess he left alone counts once he saves).</summary>
        [JsonProperty("checked")] public bool Checked { get; set; }
        [JsonProperty("w")] public float W { get; set; }
        [JsonProperty("d")] public float D { get; set; }
        [JsonProperty("h")] public float H { get; set; }
        /// <summary>The model's sculpted scenic base (Kai, 2026-11-18): null = the game decides (cuts a thin slab with the
        /// piece standing on it), true = always cut it off, false = keep it.</summary>
        [JsonProperty("cutBase", NullValueHandling = NullValueHandling.Ignore)] public bool? CutBase { get; set; }
    }

    /// <summary>A downloaded piece with its match.</summary>
    public sealed record Entry(int Id, int Set, string SetName, string File, string Keywords, string OriginalName, Match Match)
    {
        public string Name => Match.Name.Length > 0 ? Match.Name : OriginalName;
        public bool Measured => Match.W > 0 && Match.D > 0;
        public string Look => LookPrefix + Id;
    }

    public string Folder { get; }
    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyDictionary<int, string> Sets { get; }
    private readonly Dictionary<int, Entry> _byId;

    private WarhammerTerrainLibrary(string folder, List<Entry> entries, IReadOnlyDictionary<int, string> sets)
    {
        Folder = folder;
        Entries = entries;
        Sets = sets;
        _byId = entries.ToDictionary(e => e.Id);
    }

    public Entry? ById(int id) => _byId.TryGetValue(id, out var e) ? e : null;

    /// <summary>The model a map piece shows, from its Look (<c>wh:&lt;id&gt;</c>), when this computer has it.</summary>
    public Entry? ForLook(string? look) => IdOf(look) is { } id ? ById(id) : null;

    public static int? IdOf(string? look) =>
        look != null && look.StartsWith(LookPrefix, StringComparison.Ordinal) && int.TryParse(look[LookPrefix.Length..], out int id) ? id : null;

    /// <summary>
    /// The pieces that are on this computer: the terrain list the downloader wrote, each with its model file, and the
    /// matches saved so far (a guess for any piece not matched yet). Null when the terrain hasn't been downloaded.
    /// </summary>
    public static WarhammerTerrainLibrary? Load(string? catalogue = null, string? modelsFolder = null)
    {
        if (WarhammerTerrain.Load(catalogue) is not { } t) return null;
        string folder = modelsFolder ?? WarhammerTerrain.ModelsFolder;
        Dictionary<int, Match> saved = ReadMatches(Path.Combine(folder, FileName));
        // Kai's matches ship in the game (Assets/Models3D): a download gets them, and its own matches win over them.
        if (modelsFolder == null)
            foreach (var (id, m) in ReadMatches(ShippedPath))
                saved.TryAdd(id, m);
        var entries = new List<Entry>();
        foreach (WarhammerTerrain.Piece p in t.Pieces)
        {
            string file = WarhammerTerrain.ModelPathFor(t.Catalogue, p, folder);
            if (!File.Exists(file)) continue;
            Match m = saved.TryGetValue(p.Id, out var s) ? s : new Match { Kind = GuessKind(p.Name, p.Keywords) };
            // A guess nobody has checked yet follows the current guessing (it was only ever a guess).
            if (!m.Checked && !m.Hidden) m.Kind = GuessKind(p.Name, p.Keywords);
            string set = t.Catalogue.Groups.TryGetValue(p.Set, out var g) ? g : "Terrain";
            entries.Add(new Entry(p.Id, p.Set, set, file, p.Keywords, p.Name, m));
        }
        return new WarhammerTerrainLibrary(folder, entries, t.Catalogue.Groups.Where(g => entries.Any(e => e.Set == g.Key))
            .ToDictionary(g => g.Key, g => g.Value));
    }

    public string MatchesPath => Path.Combine(Folder, FileName);

    /// <summary>Kai's matches as shipped in the game - copied from his PC by <see cref="SharedSettingsSync"/>.</summary>
    public const string ShippedRelativePath = "Models3D/terrain-matches.json";
    public static string ShippedPath => Path.Combine(AppAssets.AssetsFolder, ShippedRelativePath);

    public void Save()
    {
        var file = new SortedDictionary<int, Match>(Entries.ToDictionary(e => e.Id, e => e.Match));
        File.WriteAllText(MatchesPath, JsonConvert.SerializeObject(file, Formatting.Indented));
    }

    private static Dictionary<int, Match> ReadMatches(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonConvert.DeserializeObject<Dictionary<int, Match>>(File.ReadAllText(path)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>
    /// A first guess at a piece's kind from its name and Warhammer keywords - Kai checks every one on the match screen.
    /// Warhammer's "Obscuring" blocks sight, "Light Cover" is cover, "Difficult ground" is difficult; the names say
    /// what the thing is. Unnamed pieces (most of set 1 and set 5) get "" - no guess.
    /// </summary>
    public static string GuessKind(string name, string keywords)
    {
        string n = (name + " " + keywords).ToLowerInvariant();
        bool Has(params string[] words) => words.Any(n.Contains);
        if (name.StartsWith("Piece ", StringComparison.Ordinal) && keywords.Length == 0) return "";
        if (Has("crater")) return "Rubble";
        if (Has("wood", "forest", "tree", "jungle", "copse")) return "Forest";
        if (Has("corpse", "debris", "statuary", "rubble")) return "Rubble";
        if (Has("barricade", "sandbag", "defense line", "tank trap", "barrier")) return "Barricade";
        if (Has("wall", "fence", "hedge")) return "Wall";
        if (Has("storey", "ruin", "breachable")) return "Ruins";
        if (Has("rock", "boulder", "cliff", "mountain")) return "Mountain";
        if (Has("bunker", "building", "house", "tower", "structure") && Has("obscuring")) return "Building";
        // A shipping container is a solid block you can't walk through (Kai, 2026-11-14) - a Building, not a barricade.
        if (Has("container")) return "Building";
        if (Has("structure", "obstacle", "crate")) return "Barricade";
        if (Has("hill")) return "Hill";
        if (Has("pond", "lake", "water")) return "LakeShallow";
        if (Has("lava")) return "Lava";
        if (Has("swamp", "marsh", "bog")) return "Swamp";
        return "";
    }

    /// <summary>The rules a matched kind gives (decoration: none).</summary>
    public static ETerrainType RulesFor(string kind) => TerrainBook.Kind(kind)?.Rules ?? ETerrainType.None;

    /// <summary>
    /// A map piece that IS this model (Kai: "the model is the piece"): its footprint is the model's measured size, its
    /// rules the kind it was matched to, its look the model. Unmeasured or unmatched pieces fall back to the kind's
    /// book size so a piece is never zero-sized.
    /// </summary>
    public static MapPiece PieceFor(Entry e, float x, float z)
    {
        TerrainBook.PieceKind? k = TerrainBook.Kind(e.Match.Kind);
        float w = e.Measured ? MathF.Max(0.5f, MathF.Round(e.Match.W * 4) / 4) : k?.Width ?? 4;
        float d = e.Measured ? MathF.Max(0.5f, MathF.Round(e.Match.D * 4) / 4) : k?.Depth ?? 4;
        return new MapPiece
        {
            Kind = e.Match.Kind.Length > 0 ? e.Match.Kind : Decoration,
            Name = e.Name,
            Rules = RulesFor(e.Match.Kind),
            Look = e.Look,
            Height = e.Match.H > 0 ? MathF.Round(e.Match.H * 4) / 4 : k?.Height ?? 1,
            X = x, Z = z, W = w, D = d,
            Points = MapPiece.PointsFor(w, d),
        };
    }

    /// <summary>
    /// Brings a map's Warhammer pieces up to date with their matches: a piece still carrying the rules of the kind it was
    /// placed as (not hand-edited) takes the kind - and so the rules - its model is matched to now. Kai, 2026-11-14:
    /// containers placed as barricades (an old guess) should be solid once they're matched as buildings. Returns how
    /// many changed.
    /// </summary>
    public int Refresh(MapDraft draft)
    {
        int changed = 0;
        foreach (MapPiece p in draft.Pieces)
        {
            if (ForLook(p.Look) is not { } e || e.Match.Kind.Length == 0 || e.Match.Kind == p.Kind) continue;
            if (p.Rules != RulesFor(p.Kind)) continue; // its rules were changed by hand: leave it
            p.Kind = e.Match.Kind;
            p.Rules = RulesFor(e.Match.Kind);
            changed++;
        }
        return changed;
    }

    /// <summary>Pieces that can go on a map: matched to a kind (decoration counts), measured, not hidden.</summary>
    public IEnumerable<Entry> Placeable => Entries.Where(e => !e.Match.Hidden && e.Match.Kind.Length > 0);

    /// <summary>
    /// Kai: "turn them into maps". The mod's sets are kits (their Place button lays the pieces out in a tray beside the
    /// table, not as a battle map), so a set becomes a map by dressing a generated table with that set's models: every
    /// piece of a kind the set has is swapped for one of the set's models of that kind, closest in size, its footprint
    /// becoming the model's. Pieces of a kind the set lacks keep their stand-in. Returns how many were swapped.
    /// </summary>
    public int DressFromSet(MapDraft draft, int set, Random rng)
    {
        var bySet = Placeable.Where(e => e.Set == set && e.Match.Kind != Decoration && e.Measured).GroupBy(e => e.Match.Kind)
            .ToDictionary(g => g.Key, g => g.ToList());
        int swapped = 0;
        for (int i = 0; i < draft.Pieces.Count; i++)
        {
            MapPiece p = draft.Pieces[i];
            if (!bySet.TryGetValue(p.Kind, out var pool) || pool.Count == 0) continue;
            float area = p.W * p.D;
            // Closest in size, with a little chance so a map of six ruins isn't one ruin six times.
            Entry pick = pool.OrderBy(e => MathF.Abs(e.Match.W * e.Match.D - area) * (0.75f + 0.5f * (float)rng.NextDouble())).First();
            MapPiece np = PieceFor(pick, p.X, p.Z);
            np.Angle = p.Angle;
            draft.Pieces[i] = np;
            draft.Clamp(np);
            swapped++;
        }
        return swapped;
    }
}
