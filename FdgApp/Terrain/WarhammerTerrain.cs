using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FDG.ArmyBuilding;
using FdgRaylib.Models3D;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FdgRaylib.Terrain;

/// <summary>
/// The optional Warhammer terrain for Grimdark Future (Kai, 2026-11-14): Raikoh's "Warhammer Map Builder" Tabletop
/// Simulator mod (Steam Workshop 3372253478) - about 235 terrain models in six ready-made sets (ruins, barricades, walls,
/// craters, woods, Ork and T'au structures...).
///
/// <para><b>Built on the player's computer, never shipped</b> - community uploads, personal use, like the ForceOrg and
/// Stormvault unit models. The Asset Lab reads the mod's save (the player's own Tabletop Simulator Workshop folder, or
/// Steam's copy) and writes <see cref="FileName"/> into the UnitModels folder in the ForceOrg catalogue's shape, so the
/// existing downloader and model assembler work on it unchanged: one "group" per terrain set, one model per piece.</para>
///
/// <para><b>Where the pieces are.</b> The mod keeps them inside each set tile's script, as the JSON its "Place" button
/// spawns (<c>objectJSONs = { [[{...}]], ... }</c>), not as table objects. That JSON also places every piece on the
/// mod's table, so <see cref="Layout"/> keeps where each set puts its pieces - groundwork for turning the sets into
/// Map Maker maps later.</para>
///
/// <para><b>Names.</b> Most pieces are named "Barricade : Obstacle : Light cover, Defense line..." - the name, then
/// Warhammer's terrain keywords (sometimes coloured with TTS tags like <c>[00BFFF]...[-]</c>). Those are split into
/// <see cref="Piece.Name"/> and <see cref="Piece.Keywords"/>: the keywords say what the piece is for (cover, obscuring,
/// difficult), which is what the Terrain Lab and the map maker need. Unnamed pieces are numbered within their set.</para>
/// </summary>
public static class WarhammerTerrain
{
    public const string WorkshopId = "3372253478";
    public const string FileName = "warhammer-terrain.json";
    public const string ModelsFolderName = "Terrain - Warhammer";
    public const string Source = "Warhammer Map Builder by Raikoh (Tabletop Simulator, Steam Workshop 3372253478)";

    /// <summary>Ids sit above Stormvault's (100M-1000M) and ForceOrg's (counted from 1), so all three can be merged.</summary>
    public const int IdBase = 1_100_000_000;
    public const int GroupBase = 5001;

    public static string DefaultPath => Path.Combine(UnitModelPaths.Root, FileName);
    public static string ModelsFolder => Path.Combine(UnitModelPaths.Root, ModelsFolderName);

    public sealed class Piece
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("set")] public int Set { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = "";
        /// <summary>Warhammer's terrain keywords from the piece's name ("Obstacle: Light Cover, Defensible"), or "".</summary>
        [JsonProperty("keywords")] public string Keywords { get; set; } = "";
    }

    /// <summary>One piece placed by a set: TTS table units (1 = 1 inch), y-rotation in degrees.</summary>
    public sealed class Placement
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("z")] public float Z { get; set; }
        [JsonProperty("rotY")] public float RotY { get; set; }
    }

    /// <summary>
    /// One of the mod's battle mats (Kai, 2026-11-14): a picture of a table surface. In the mod each is a small tile
    /// whose "Apply Image" button paints the table with it; here a map can pick one (<see cref="MatPrefix"/> + id).
    /// Numbered in the order the mod lays the tiles out, row by row - they carry no names.
    /// </summary>
    public sealed class Mat
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("number")] public int Number { get; set; }
        [JsonProperty("url")] public string Url { get; set; } = "";
        [JsonIgnore] public string Name => $"Mat {Number}";
        [JsonIgnore] public string Look => MatPrefix + Id;
    }

    public const string MatPrefix = "wh-mat:";
    public const string MatsFolderName = "_mats";
    public static string MatsFolder => Path.Combine(ModelsFolder, MatsFolderName);

    public sealed class Result
    {
        public readonly List<Mat> Mats = new();
        public readonly Dictionary<int, string> Sets = new();
        public readonly List<ForceOrgCatalog.Model> Models = new();
        public readonly List<Piece> Pieces = new();
        public readonly Dictionary<int, List<Placement>> Layout = new();
    }

    /// <summary>Reads the mod's save (its JSON text) into sets, pieces and layouts.</summary>
    public static Result Parse(string modJson)
    {
        JObject save = JObject.Parse(modJson);
        var result = new Result();
        var seen = new HashSet<int>();
        if (save["ObjectStates"] is not JArray states) return result;

        // The set tiles carry no names (their picture shows it), so they are numbered left to right as the mod lays them out.
        var tiles = states.OfType<JObject>()
            .Select(o => (Obj: o, Blobs: SpawnBlobs((string?)o["LuaScript"] ?? "")))
            .Where(t => t.Blobs.Count > 0)
            .OrderBy(t => (float?)t.Obj["Transform"]?["posX"] ?? 0f)
            .ToList();

        int setNumber = 0;
        foreach (var (_, blobs) in tiles)
        {
            var placed = new List<Placement>();
            var setModels = new List<(JToken Obj, ForceOrgCatalog.Model Model)>();
            foreach (JToken blob in blobs) Walk(blob, setModels, placed, topLevel: true);
            if (setModels.Count == 0) continue;

            int group = GroupBase + setNumber++;
            result.Sets[group] = $"Terrain set {setNumber}" + SetTheme(blobs);
            int unnamed = 0;
            foreach (var (obj, model) in setModels)
            {
                if (!seen.Add(model.Id)) continue;
                (string name, string keywords) = SplitName((string?)obj["Nickname"] ?? "");
                if (name.Length == 0) name = $"Piece {++unnamed}";
                model.Name = name;
                model.Group = group;
                result.Models.Add(model);
                result.Pieces.Add(new Piece { Id = model.Id, Set = group, Name = name, Keywords = keywords });
            }
            result.Layout[group] = placed;
        }

        // The battle mats: tiles whose script paints the table with their own picture.
        var mats = states.OfType<JObject>()
            .Where(o => (string?)o["Name"] == "Custom_Tile" && ((string?)o["LuaScript"] ?? "").Contains("applyImage", StringComparison.Ordinal))
            .Select(o => (Url: ((string?)o["CustomImage"]?["ImageURL"] ?? "").Trim(), X: (float?)o["Transform"]?["posX"] ?? 0f, Z: (float?)o["Transform"]?["posZ"] ?? 0f))
            .Where(m => m.Url.Length > 0)
            .OrderByDescending(m => MathF.Round(m.Z)).ThenBy(m => m.X)
            .ToList();
        var seenMats = new HashSet<string>();
        foreach (var m in mats)
        {
            if (!seenMats.Add(m.Url)) continue;
            result.Mats.Add(new Mat { Id = StableId(new[] { m.Url }), Number = result.Mats.Count + 1, Url = m.Url });
        }
        return result;
    }

    /// <summary>Writes the catalogue (models in the ForceOrg shape, plus pieces and layouts). Returns how many pieces.</summary>
    public static int Write(Result r, string output)
    {
        var file = new
        {
            source = Source,
            note = "Personal use only - community uploads. Built on this computer by the Asset Lab; do not commit.",
            groups = r.Sets,
            models = r.Models,
            pieces = r.Pieces,
            layouts = r.Layout,
            mats = r.Mats,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonConvert.SerializeObject(file, Formatting.Indented));
        return r.Pieces.Count;
    }

    /// <summary>The <c>[[{...}]]</c> object JSONs in a tile script - what its "Place" button spawns.</summary>
    public static List<JToken> SpawnBlobs(string lua)
    {
        var list = new List<JToken>();
        int at = 0;
        while ((at = lua.IndexOf("[[{", at, StringComparison.Ordinal)) >= 0)
        {
            int end = lua.IndexOf("}]]", at, StringComparison.Ordinal);
            if (end < 0) break;
            try { list.Add(JToken.Parse(lua.Substring(at + 2, end - at - 1))); }
            catch (JsonException) { }
            at = end + 3;
        }
        return list;
    }

    // A piece is a Custom_Model; bags hold more; alternate looks (States) are pieces of their own.
    private static void Walk(JToken o, List<(JToken, ForceOrgCatalog.Model)> models, List<Placement> placed, bool topLevel)
    {
        if ((string?)o["Name"] == "Custom_Model" && o["CustomMesh"] is JObject)
        {
            var parts = new List<ForceOrgCatalog.Part>();
            Collect(o, parts, -1);
            if (parts.Count > 0)
            {
                int id = StableId(parts.Select(p => p.Mesh));
                models.Add((o, new ForceOrgCatalog.Model { Id = id, Parts = parts }));
                if (topLevel && o["Transform"] is JToken t)
                    placed.Add(new Placement
                    {
                        Id = id,
                        X = MathF.Round((float?)t["posX"] ?? 0, 3),
                        Z = MathF.Round((float?)t["posZ"] ?? 0, 3),
                        RotY = MathF.Round((float?)t["rotY"] ?? 0, 2),
                    });
            }
        }
        if (o["ContainedObjects"] is JArray contained) foreach (JToken c in contained) Walk(c, models, placed, false);
        if (o["States"] is JObject st) foreach (var kv in st) if (kv.Value != null) Walk(kv.Value, models, placed, false);
    }

    private static void Collect(JToken o, List<ForceOrgCatalog.Part> parts, int parent)
    {
        int idx = parent;
        string mesh = ((string?)o["CustomMesh"]?["MeshURL"] ?? "").Trim();
        if (mesh.Length > 0)
        {
            JToken? t = o["Transform"];
            float R(string k, float d) => MathF.Round((float?)t?[k] ?? d, 4);
            var c = o["ColorDiffuse"];
            parts.Add(new ForceOrgCatalog.Part
            {
                Mesh = mesh,
                Texture = ((string?)o["CustomMesh"]?["DiffuseURL"] ?? "").Trim(),
                Color = new[] { MathF.Round((float?)c?["r"] ?? 1, 4), MathF.Round((float?)c?["g"] ?? 1, 4), MathF.Round((float?)c?["b"] ?? 1, 4) },
                // Where the set put it on the TTS table is kept in the layout; the model itself sits at the origin.
                Pos = parent < 0 ? new float[] { 0, 0, 0 } : new[] { R("posX", 0), R("posY", 0), R("posZ", 0) },
                Rot = parent < 0 ? new float[] { 0, 0, 0 } : new[] { R("rotX", 0), R("rotY", 0), R("rotZ", 0) },
                Scale = new[] { R("scaleX", 1), R("scaleY", 1), R("scaleZ", 1) },
                Parent = parent,
            });
            idx = parts.Count - 1;
        }
        if (o["ChildObjects"] is JArray kids) foreach (JToken k in kids) Collect(k, parts, idx);
    }

    public static int StableId(IEnumerable<string> meshLinks)
    {
        byte[] h = SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("\n", meshLinks.Select(m => m.Trim()))));
        uint v = ((uint)h[0] << 24) | ((uint)h[1] << 16) | ((uint)h[2] << 8) | h[3];
        return IdBase + (int)(v % 900_000_000u);
    }

    // The mod tags each set's pieces with where they come from (tow_prov, desert_tiles, tow_lustria).
    private static string SetTheme(IEnumerable<JToken> blobs)
    {
        var tags = blobs.SelectMany(b => b["Tags"] is JArray a ? a.Select(x => (string?)x ?? "") : Enumerable.Empty<string>())
            .Where(t => t.Length > 0).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        return tags switch
        {
            null => "",
            "desert_tiles" => " (desert)",
            "tow_lustria" => " (jungle)",
            "tow_prov" => " (provinces)",
            _ => $" ({tags.Replace('_', ' ')})",
        };
    }

    /// <summary>
    /// "Barricade : Obstacle : Light cover, Defense line" -> ("Barricade", "Obstacle: Light cover, Defense line");
    /// "Tall T'au Structure | [CC99CC]Obstacle[-]   [00BFFF]Obscuring[-]" -> ("Tall T'au Structure", "Obstacle: Obscuring");
    /// "Ruin (Area Terrain)" -> ("Ruin", "Area Terrain"). TTS colour tags and runs of spaces go; text is ASCII (game text).
    /// </summary>
    public static (string Name, string Keywords) SplitName(string raw)
    {
        string s = Regex.Replace(raw, @"\[[0-9A-Fa-f]{6}\]|\[-\]|\[/?[bisu]\]", " ");
        s = OprBookImporter.AsciiFold(s);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        string name = s, keywords = "";
        int cut = s.IndexOfAny(new[] { ':', '|' });
        if (cut >= 0)
        {
            name = s[..cut].Trim();
            var rest = Regex.Split(s[(cut + 1)..], @"\s*[:|]\s*").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            // "Obstacle" / "Area Terrain" then the keyword list; a trailing note ("6\" between levels") is kept as it is.
            keywords = rest.Count switch
            {
                0 => "",
                1 => Kind(rest[0]),
                _ => rest[0] + ": " + string.Join(", ", rest.Skip(1)),
            };
        }
        else if (Regex.Match(s, @"^(.*?)\s*\(([^)]*)\)$") is { Success: true } m)
        {
            name = m.Groups[1].Value.Trim();
            keywords = m.Groups[2].Value.Trim();
        }
        return (name, keywords);
    }

    // "Obstacle Obscuring, Breachable" (no colon between them once the colour tags go) -> "Obstacle: Obscuring, Breachable".
    private static string Kind(string s)
    {
        foreach (string k in new[] { "Area Terrain", "Obstacle" })
            if (s.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase))
                return k + ": " + s[(k.Length + 1)..].Trim();
        return s;
    }

    /// <summary>Where Tabletop Simulator keeps this mod's save on this computer, if it is subscribed (or null).</summary>
    public static string? LocalModFile()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] docs =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "OneDrive", "Documents"),
            Path.Combine(profile, "Documents"),
            Path.Combine(profile, ".local", "share"), // Linux TTS keeps "Tabletop Simulator" here
            Path.Combine(profile, "Library", "Tabletop Simulator"),
        };
        foreach (string d in docs.Where(d => d.Length > 0))
        {
            foreach (string p in new[]
            {
                Path.Combine(d, "My Games", "Tabletop Simulator", "Mods", "Workshop", WorkshopId + ".json"),
                Path.Combine(d, "Tabletop Simulator", "Mods", "Workshop", WorkshopId + ".json"),
                Path.Combine(d, "Mods", "Workshop", WorkshopId + ".json"),
            })
                if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// The mod's save: this computer's Tabletop Simulator copy if there is one, else Steam's (the Workshop file details
    /// say where Steam keeps it - no account needed). Throws with a plain reason.
    /// </summary>
    public static async Task<string> FetchModJsonAsync(Action<string>? progress, CancellationToken ct)
    {
        if (LocalModFile() is { } local)
        {
            progress?.Invoke("Reading the mod from your Tabletop Simulator folder...");
            return await File.ReadAllTextAsync(local, ct);
        }
        progress?.Invoke("Asking Steam where the mod is...");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OnePageRules-TheGame/1.0 (Asset Lab)");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["itemcount"] = "1",
            ["publishedfileids[0]"] = WorkshopId,
        });
        using var resp = await http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", form, ct);
        if (!resp.IsSuccessStatusCode) throw new IOException($"Steam said {(int)resp.StatusCode}.");
        string url = (string?)JObject.Parse(await resp.Content.ReadAsStringAsync(ct))["response"]?["publishedfiledetails"]?[0]?["file_url"] ?? "";
        if (url.Length == 0) throw new IOException("Steam didn't say where the mod's file is. Subscribe to it in Tabletop Simulator and try again.");
        progress?.Invoke("Downloading the mod's file from Steam (about 7 MB)...");
        return await http.GetStringAsync(ModelDownloader.FetchAddress(url), ct);
    }

    /// <summary>Fetches the mod and writes the catalogue. Returns the piece count.</summary>
    public static async Task<int> BuildAsync(Action<string>? progress, CancellationToken ct, string? output = null)
    {
        string json = await FetchModJsonAsync(progress, ct);
        progress?.Invoke("Reading the terrain sets...");
        Result r = Parse(json);
        if (r.Pieces.Count == 0) throw new IOException("No terrain pieces found in the mod's file.");
        int n = Write(r, output ?? DefaultPath);
        progress?.Invoke($"Warhammer terrain list ready: {n} pieces in {r.Sets.Count} sets.");
        return n;
    }

    /// <summary>The catalogue as written, for the downloader (null when it isn't there or can't be read).</summary>
    public static (ForceOrgCatalog Catalogue, List<Piece> Pieces)? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;
        try
        {
            var pieces = JObject.Parse(File.ReadAllText(path))["pieces"]?.ToObject<List<Piece>>() ?? new();
            return (ForceOrgCatalog.Load(path), pieces);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The mats in the catalogue, or null when it was written before mats were read (2.14) or isn't there.</summary>
    public static List<Mat>? LoadMats(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;
        try { return JObject.Parse(File.ReadAllText(path))["mats"]?.ToObject<List<Mat>>(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The downloaded picture for a map's mat (<c>wh-mat:&lt;id&gt;</c>), or null when this computer hasn't got it.</summary>
    public static string? MatFile(string? look, string? folder = null)
    {
        if (look == null || !look.StartsWith(MatPrefix, StringComparison.Ordinal) || !int.TryParse(look[MatPrefix.Length..], out int id)) return null;
        folder ??= MatsFolder;
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, id + ".*").FirstOrDefault(f => !f.EndsWith(".part", StringComparison.Ordinal));
    }

    /// <summary>Where a mat's picture is saved; the extension follows what the file is (PNG or JPEG).</summary>
    public static string MatPathFor(Mat mat, byte[] head, string? folder = null) =>
        Path.Combine(folder ?? MatsFolder, mat.Id + (head.Length > 1 && head[0] == 0x89 && head[1] == (byte)'P' ? ".png" : ".jpg"));

    /// <summary>Where a piece's assembled model goes: <c>UnitModels/Terrain - Warhammer/&lt;set&gt;/&lt;name&gt;.glb</c>.</summary>
    public static string ModelPathFor(ForceOrgCatalog catalogue, Piece piece, string? root = null)
    {
        string set = catalogue.Groups.TryGetValue(piece.Set, out var g) ? g : "Terrain";
        string safe(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' ? '_' : ch)).Trim();
        return Path.Combine(root ?? ModelsFolder, safe(set), $"{safe(piece.Name)} [{piece.Id % 100000:00000}].glb");
    }
}
