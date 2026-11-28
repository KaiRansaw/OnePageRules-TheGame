using System;
using System.Linq;
using FDG;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// The endless battlefield (Kai, 2026-11-18: "look off into the distance and the landscape is never ending, but the
/// playable area stays the same"). With Settings -> Around the table = Battlefield there is no table edge: the ground
/// runs on past the mat, rises into hills and then mountains, and fades into haze under the theme's sky. The mat (the
/// playable area) is untouched and a thin line marks its edge.
/// <para>
/// Each generator theme (Generic, Hospitable, Wasteland, Death) has a folder <c>res://horizon/&lt;Theme&gt;/</c> with
/// <c>sky.exr</c> (or .hdr/.jpg/.png - a 360-degree sky) and <c>ground.jpg</c> (+ <c>ground_normal.jpg</c>), all CC0.
/// A theme without its own pictures borrows Hospitable's, tinted to its colours, with a painted sky.
/// </para>
/// </summary>
public static class Horizon
{
    /// <summary>How far the land runs, in table inches. Beyond the haze it is sky.</summary>
    public const float Reach = 1600f;
    private const float FlatMargin = 36f; // flat ground all round the table before the hills start

    private sealed record Theme(Color Tint, Color Haze, Color SkyTop, Color SkyHorizon, Color Ground, float Hills, float Mountains);

    private static Theme Of(TerrainBook.ETheme t) => t switch
    {
        TerrainBook.ETheme.Hospitable => new(new Color(0.62f, 0.64f, 0.56f), new Color(0.72f, 0.8f, 0.86f), new Color(0.32f, 0.5f, 0.78f), new Color(0.75f, 0.82f, 0.88f), new Color(0.36f, 0.42f, 0.24f), 30f, 110f),
        TerrainBook.ETheme.Wasteland => new(new Color(0.72f, 0.58f, 0.42f), new Color(0.78f, 0.68f, 0.52f), new Color(0.55f, 0.52f, 0.48f), new Color(0.85f, 0.74f, 0.56f), new Color(0.5f, 0.42f, 0.3f), 25f, 90f),
        TerrainBook.ETheme.Death => new(new Color(0.36f, 0.27f, 0.25f), new Color(0.35f, 0.16f, 0.12f), new Color(0.12f, 0.05f, 0.06f), new Color(0.55f, 0.2f, 0.1f), new Color(0.2f, 0.15f, 0.14f), 40f, 140f),
        _ => new(new Color(0.58f, 0.57f, 0.5f), new Color(0.7f, 0.74f, 0.76f), new Color(0.4f, 0.5f, 0.62f), new Color(0.74f, 0.77f, 0.78f), new Color(0.38f, 0.38f, 0.3f), 30f, 100f),
    };

    /// <summary>True when Settings has the battlefield (not a room, not the plain background).</summary>
    public static bool On => FdgRaylib.Config.UserConfig.Current.View.Room is FdgRaylib.Terrain.TableRooms.Battlefield or FdgRaylib.Terrain.TableRooms.Tabletop;

    /// <summary>The table look (Kai, 2026-11-25): the land sits <see cref="TableHeight"/> below the mat and a wooden table
    /// with a rim and legs holds the play area.</summary>
    public static bool Tabletop => FdgRaylib.Config.UserConfig.Current.View.Room == FdgRaylib.Terrain.TableRooms.Tabletop;

    public const float TableHeight = 30f;

    /// <summary>Points <paramref name="env"/> at the theme's sky and haze. Call after <see cref="TableLook.AddLighting"/>.</summary>
    public static void SkyAndHaze(Godot.Environment env, TerrainBook.ETheme theme, float tableW, float tableD, string? sky = null)
    {
        Theme th = Of(theme);
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        // The map's sky (Kai, 2026-11-18): one of the style's sky pictures, the first when none is picked.
        var skies = Skies(theme);
        var chosen = skies.FirstOrDefault(k => k.Id.Equals(sky ?? "", StringComparison.OrdinalIgnoreCase)) ?? skies.FirstOrDefault();
        Texture2D? pano = chosen != null ? Load(chosen.Color) : null;
        env.Sky = new Sky
        {
            SkyMaterial = pano != null
                ? new PanoramaSkyMaterial { Panorama = pano, EnergyMultiplier = 1f }
                : new ProceduralSkyMaterial
                {
                    SkyTopColor = th.SkyTop, SkyHorizonColor = th.SkyHorizon,
                    GroundBottomColor = th.Ground.Darkened(0.4f), GroundHorizonColor = th.SkyHorizon,
                    SunAngleMax = 30f,
                },
        };
        env.BackgroundEnergyMultiplier = 1f;
        // A new sky: forget the clear-day values Weather remembered for the old one.
        foreach (string m in new[] { "clear_sky", "clear_haze" }) if (env.HasMeta(m)) env.RemoveMeta(m);
        // Haze: clear over the table, thickening out to the horizon so the land never visibly ends. Depth fog is measured
        // from the camera, which sits up to ~2 table-widths back, so the haze starts well past that.
        float near = Mathf.Max(tableW, tableD) * 1.5f;
        env.FogEnabled = true;
        env.FogMode = Godot.Environment.FogModeEnum.Depth;
        env.FogLightColor = th.Haze;
        env.FogDensity = 1f;
        env.FogDepthBegin = near;
        env.FogDepthEnd = near + 690f;
        env.FogDepthCurve = 1.6f;
        env.FogSkyAffect = 0f;
        env.FogAerialPerspective = 0f; // sky-tinted haze samples the panorama's dark lower half: plain haze colour instead
    }

    /// <summary>
    /// The map's light (Kai, 2026-11-18: "edit the light of the map"): the sun's height, turn, brightness and warmth, and
    /// the sky picture's turn. Finds the scene's "Sun" and "Environment" under <paramref name="world"/>. Clears what the
    /// weather remembered as the clear-day light, so bad weather (added after) dims the NEW light.
    /// </summary>
    public static void ApplyLight(Node world, FdgRaylib.Terrain.MapAtmosphere.Light l)
    {
        if (world.GetNodeOrNull<DirectionalLight3D>("Sun") is { } sun)
        {
            sun.RotationDegrees = new Vector3(-l.SunHeight, l.SunTurn, 0);
            sun.LightEnergy = 1.2f * l.Brightness;
            Color neutral = new(1f, 0.95f, 0.88f);
            sun.LightColor = l.Warmth >= 0 ? neutral.Lerp(new Color(1f, 0.72f, 0.45f), l.Warmth) : neutral.Lerp(new Color(0.72f, 0.84f, 1f), -l.Warmth);
            if (sun.HasMeta("clear_energy")) sun.RemoveMeta("clear_energy");
        }
        if (world.GetNodeOrNull<WorldEnvironment>("Environment") is { Environment: { } env })
        {
            if (!env.HasMeta("base_ambient")) env.SetMeta("base_ambient", env.AmbientLightEnergy);
            env.AmbientLightEnergy = (float)env.GetMeta("base_ambient") * Mathf.Lerp(1f, l.Brightness, 0.7f);
            env.SkyRotation = new Vector3(0, Mathf.DegToRad(l.SkyTurn), 0);
            if (!env.HasMeta("base_sky")) env.SetMeta("base_sky", env.BackgroundEnergyMultiplier);
            env.BackgroundEnergyMultiplier = (float)env.GetMeta("base_sky") * Mathf.Lerp(1f, l.Brightness, 0.5f);
            foreach (string m in new[] { "clear_ambient", "clear_sky" }) if (env.HasMeta(m)) env.RemoveMeta(m);
        }
    }

    /// <summary>The land round a <paramref name="tableW"/> x <paramref name="tableD"/> table centred on the origin.</summary>
    public static void AddLand(Node3D parent, float tableW, float tableD, TerrainBook.ETheme theme, string? ground = null, string? scenery = null)
    {
        Theme th = Of(theme);
        var root = new Node3D { Name = "Horizon" };
        parent.AddChild(root);

        var noise = new FastNoiseLite { Seed = 1793 + (int)theme, Frequency = 0.0022f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
        var ridges = new FastNoiseLite { Seed = 31 + (int)theme, Frequency = 0.0011f, FractalType = FastNoiseLite.FractalTypeEnum.Ridged, FractalOctaves = 3 };
        float hw = tableW / 2, hd = tableD / 2;
        bool table = Tabletop;
        float drop = table ? TableHeight : 0.03f, margin = table ? 60f : FlatMargin;
        float Height(float x, float z)
        {
            // Distance from the table's edge (0 on the mat), so the flat ground hugs any table shape.
            float dx = Mathf.Max(Mathf.Abs(x) - hw, 0), dz = Mathf.Max(Mathf.Abs(z) - hd, 0);
            float d = Mathf.Sqrt(dx * dx + dz * dz) - margin;
            // Under a table the ground rolls a little, so it doesn't read as a floor.
            float ripple = table ? 2.5f * noise.GetNoise2D(x * 3f, z * 3f) : 0f;
            if (d <= 0) return -drop + ripple;
            float hills = Mathf.SmoothStep(0, 260, d) * th.Hills * (0.5f + 0.5f * noise.GetNoise2D(x, z));
            float mountains = Mathf.SmoothStep(450, 1100, d) * th.Mountains * Mathf.Max(0, ridges.GetNoise2D(x, z) + 0.15f);
            return -drop + ripple + hills + mountains;
        }

        // One square grid out to Reach; finer near the table so the first slopes are smooth.
        var xs = Steps(hw, Reach);
        var zs = Steps(hd, Reach);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < xs.Count; i++)
        for (int j = 0; j < zs.Count; j++)
        {
            float x = xs[i], z = zs[j], y = Height(x, z);
            float shade = Mathf.Clamp(1f - y / (th.Mountains * 1.4f), 0.55f, 1f); // high ground a little darker (rock)
            st.SetColor(new Color(shade, shade, shade));
            st.SetUV(new Vector2(x / Tile, z / Tile));
            st.AddVertex(new Vector3(x, y, z));
        }
        int n = zs.Count;
        for (int i = 0; i < xs.Count - 1; i++)
        for (int j = 0; j < zs.Count - 1; j++)
        {
            int a = i * n + j, b = (i + 1) * n + j, c = (i + 1) * n + j + 1, d = i * n + j + 1;
            // Godot's front faces wind clockwise seen from the front (here: from above).
            st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
            st.AddIndex(a); st.AddIndex(c); st.AddIndex(d);
        }
        st.GenerateNormals();
        st.GenerateTangents();
        TableLook.Mesh(root, st.Commit(), Ground(theme, ground), Vector3.Zero).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        // The playable edge: a thin light line round the mat - or, on a table, the table's own rim.
        if (table) { AddTable(root, tableW, tableD); AddSurroundings(root, tableW, tableD, theme, Height, scenery); }
        else Border(root, tableW, tableD);
        LandHeight = Height;
    }

    /// <summary>The land's height at a world point (the last land built) - the Map Maker's scenery tool stands pieces on it.</summary>
    public static Func<float, float, float> LandHeight { get; private set; } = (_, _) => 0f;

    public const string SceneryNode = "Surroundings";

    /// <summary>
    /// The scenery round the table: the map's own (FdgApp <see cref="FdgRaylib.Terrain.MapScenery"/>: placed by hand in the
    /// Map Maker, or generated from its set, density and seed). Each piece is sized to its height in inches (the packs'
    /// units vary) and stood on the land. Piece i is a child named "Scenery_i" (the Map Maker picks pieces by it).
    /// </summary>
    private static void AddSurroundings(Node3D root, float w, float d, TerrainBook.ETheme theme, Func<float, float, float> height, string? scenery)
    {
        var holder = new Node3D { Name = SceneryNode };
        root.AddChild(holder);
        var pieces = FdgRaylib.Terrain.MapScenery.Parse(scenery).Resolve(theme.ToString(), w, d);
        for (int i = 0; i < pieces.Count; i++)
            if (ScenePiece(pieces[i], height) is { } m) { m.Name = $"Scenery_{i}"; holder.AddChild(m); }
    }

    /// <summary>One scenery piece, built and placed (null when its model can't load).</summary>
    public static Node3D? ScenePiece(FdgRaylib.Terrain.MapScenery.Piece p, Func<float, float, float> height)
    {
        if (PieceModels.Instance($"stylised/nature/models/{p.Model}.gltf") is not { } m) return null;
        float tall = ModelHeight(m);
        if (tall <= 0.001f) { m.QueueFree(); return null; }
        float k = p.Height / tall;
        m.Scale = new Vector3(k, k, k);
        m.Position = new Vector3(p.X, height(p.X, p.Z) - 0.5f, p.Z);
        m.RotationDegrees = new Vector3(0, p.Yaw, 0);
        return m;
    }

    /// <summary>
    /// The wooden table (Kai, 2026-11-25, from Niemandsland's look): a dark frame round the mat with a lip standing
    /// <see cref="RimHeight"/> above it (outside the play area, so it never touches a model), a deep apron under the
    /// top and four legs down to the ground.
    /// </summary>
    public static void AddTable(Node3D root, float w, float d)
    {
        const float Rim = 1.6f, Top = 3f, Apron = 5f, Leg = 3.5f;
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.085f, 0.08f), Roughness = 0.55f, Metallic = 0.05f };
        var table = new Node3D { Name = "Table" };
        root.AddChild(table);
        void Box(Vector3 size, Vector3 at, Material m)
        {
            var mi = TableLook.Mesh(table, new BoxMesh { Size = size }, m, at);
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        }
        float ow = w + 2 * Rim, od = d + 2 * Rim;
        // The lip: four rails, their tops RimHeight above the mat, in the table's planks.
        Material edge = Planks();
        Box(new Vector3(ow, RimHeight + Top, Rim), new Vector3(0, (RimHeight - Top) / 2, -(d / 2 + Rim / 2)), edge);
        Box(new Vector3(ow, RimHeight + Top, Rim), new Vector3(0, (RimHeight - Top) / 2, d / 2 + Rim / 2), edge);
        Box(new Vector3(Rim, RimHeight + Top, d), new Vector3(-(w / 2 + Rim / 2), (RimHeight - Top) / 2, 0), edge);
        Box(new Vector3(Rim, RimHeight + Top, d), new Vector3(w / 2 + Rim / 2, (RimHeight - Top) / 2, 0), edge);
        // The table itself: Kai's medieval table (2026-11-26), stretched to fit; the plain boxes if it can't load.
        if (TableModel(table, ow + 3f, od + 3f, -0.04f)) return;
        Box(new Vector3(w, Top, d), new Vector3(0, -Top / 2 - 0.02f, 0), wood);
        Box(new Vector3(ow - 3, Apron, od - 3), new Vector3(0, -Top - Apron / 2, 0), wood);
        float legH = TableHeight + 4f - Top - Apron;
        float lx = ow / 2 - 4f, lz = od / 2 - 4f;
        foreach ((float sx, float sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            Box(new Vector3(Leg, legH, Leg), new Vector3(sx * lx, -Top - Apron - legH / 2, sz * lz), wood);
    }

    // "Medieval table" by zeein4004 on CGTrader (Royalty Free, free for commercial use): 204 x 112 cm, 84 cm tall, its
    // top at the model's highest point. Kai downloaded it, 2026-11-26. Credits page lists it.
    private const string TableDir = "res://terrain/table/";
    private const float ModelW = 204.02f, ModelD = 111.93f, ModelH = 83.9f;
    private static Mesh? _tableMesh;
    private static StandardMaterial3D? _tableMat, _planks;

    /// <summary>The table model stretched to <paramref name="w"/> x <paramref name="d"/>, its top at
    /// <paramref name="topY"/> and its feet <see cref="TableHeight"/> below. False when the model isn't there.</summary>
    public static bool TableModel(Node3D parent, float w, float d, float topY)
    {
        _tableMesh ??= ResourceLoader.Exists(TableDir + "medieval_table.obj") ? ResourceLoader.Load<Mesh>(TableDir + "medieval_table.obj") : null;
        if (_tableMesh == null) return false;
        _tableMat ??= new StandardMaterial3D
        {
            AlbedoTexture = Tex("medieval_table_base.jpg"),
            NormalEnabled = true, NormalTexture = Tex("medieval_table_normal.png"),
            AOEnabled = true, AOTexture = Tex("medieval_table_orm.jpg"), AOTextureChannel = BaseMaterial3D.TextureChannel.Red,
            RoughnessTexture = Tex("medieval_table_orm.jpg"), RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green, Roughness = 1f,
            MetallicTexture = Tex("medieval_table_orm.jpg"), MetallicTextureChannel = BaseMaterial3D.TextureChannel.Blue, Metallic = 1f,
        };
        var mi = new MeshInstance3D
        {
            Name = "TableModel", Mesh = _tableMesh, MaterialOverride = _tableMat,
            Scale = new Vector3(w / ModelW, TableHeight / ModelH, d / ModelD),
            Position = new Vector3(0, topY - TableHeight, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        parent.AddChild(mi);
        return true;
    }

    /// <summary>The table's planks, for the rim and the trays' edges.</summary>
    public static Material Planks()
    {
        if (_planks != null) return _planks;
        Texture2D? t = Tex("medieval_table_planks.jpg");
        _planks = t == null
            ? new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.13f, 0.11f), Roughness = 0.45f }
            : new StandardMaterial3D { AlbedoTexture = t, Uv1Triplanar = true, Uv1Scale = new Vector3(0.05f, 0.1f, 0.05f), Roughness = 0.7f };
        return _planks;
    }

    private static Texture2D? Tex(string file) =>
        ResourceLoader.Exists(TableDir + file) ? ResourceLoader.Load<Texture2D>(TableDir + file) : null;

    /// <summary>A model's height in its own units (the tallest point of its meshes).</summary>
    public static float ModelHeight(Node3D model)
    {
        float top = 0f, bottom = float.MaxValue;
        void Walk(Node n, Transform3D t)
        {
            Transform3D here = n is Node3D n3 ? t * n3.Transform : t;
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                Aabb b = here * mi.Mesh.GetAabb();
                top = Mathf.Max(top, b.End.Y);
                bottom = Mathf.Min(bottom, b.Position.Y);
            }
            foreach (Node c in n.GetChildren()) Walk(c, here);
        }
        Walk(model, Transform3D.Identity);
        return bottom == float.MaxValue ? 0f : top - Mathf.Min(bottom, 0f);
    }

    /// <summary>How far the table's lip stands above the mat.</summary>
    public const float RimHeight = 1.2f;

    /// <summary>UV size of the land mesh (only the plain fallback colour uses UVs; the ground shader works in world inches).</summary>
    public const float Tile = 60f;

    /// <summary>One repeat of the ground picture, in table inches: miniature scale, so a twig is smaller than a base.</summary>
    public const float DetailInches = 3f;

    /// <summary>Kai, 2026-11-18 (camera "4"): a long lens on the battlefield - flatter, like a real field filmed from afar.
    /// Rooms and the plain table keep the old 44 degrees.</summary>
    public static float Lens => On ? 30f : 44f;

    /// <summary>How much further back the camera sits for the same view with <see cref="Lens"/> (1 with the old lens).</summary>
    public static float DistanceScale => Mathf.Tan(Mathf.DegToRad(22f)) / Mathf.Tan(Mathf.DegToRad(Lens / 2f));

    /// <summary>Camera "4"'s haze: it starts about where the camera looks (the table) and thickens over the next ~60 feet,
    /// so the land beyond the table falls away into distance. Depth haze is measured from the camera, so it follows the
    /// camera's distance - zooming out never fogs the table. Call whenever the camera moves.</summary>
    public static void FollowCamera(Node world, float cameraDistance)
    {
        if (!On || world.GetNodeOrNull<WorldEnvironment>("Environment") is not { Environment: { FogEnabled: true } env }) return;
        env.FogDepthBegin = cameraDistance;
        env.FogDepthEnd = cameraDistance + 690f;
    }

    /// <summary>One ground picture in a style's folder: <see cref="Id"/> is its file name without the extension
    /// ("ground", "ground2", "ground-mud"), <see cref="Name"/> what the Map Maker shows.</summary>
    public sealed record GroundPic(string Id, string Name, string Color, string? Normal);

    private static readonly Dictionary<string, List<GroundPic>> GroundLists = new();

    /// <summary>
    /// The grounds in <c>res://horizon/&lt;Theme&gt;/</c> (Kai, 2026-11-18: "can each one have multiple grounds?" - yes):
    /// every picture whose name starts "ground" (ground.jpg, ground2.jpg, ground-mud.jpg...), each with an optional
    /// "_normal" partner. A style with none borrows Hospitable's (tinted to the style).
    /// </summary>
    public static IReadOnlyList<GroundPic> Grounds(TerrainBook.ETheme theme)
    {
        List<GroundPic> own = GroundsIn(theme.ToString());
        return own.Count > 0 || theme == TerrainBook.ETheme.Hospitable ? own : GroundsIn(nameof(TerrainBook.ETheme.Hospitable));
    }

    private static List<GroundPic> GroundsIn(string folder) => PicturesIn(folder, "ground", true);

    /// <summary>The pictures in <c>res://horizon/&lt;folder&gt;/</c> whose names start with <paramref name="prefix"/>
    /// ("ground", "sky"), each with its "_normal" partner when <paramref name="normals"/>. A plain file list, so a picture
    /// dropped into the folder shows up the next time the game runs from the project - no re-import needed.</summary>
    private static List<GroundPic> PicturesIn(string folder, string prefix, bool normals)
    {
        string key = folder + "|" + prefix;
        if (GroundLists.TryGetValue(key, out var have)) return have;
        string dir = $"res://horizon/{folder}";
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // An exported game lists "x.jpg.import" / ".remap" rather than the picture itself - strip those back off.
        foreach (string f in DirAccess.DirExistsAbsolute(dir) ? DirAccess.GetFilesAt(dir) : Array.Empty<string>())
            files.Add(f.EndsWith(".import", StringComparison.OrdinalIgnoreCase) ? f[..^7] : f.EndsWith(".remap", StringComparison.OrdinalIgnoreCase) ? f[..^6] : f);
        var exts = new[] { ".jpg", ".jpeg", ".png", ".webp", ".exr", ".hdr" };
        var list = new List<GroundPic>();
        foreach (string f in files)
        {
            string ext = System.IO.Path.GetExtension(f).ToLowerInvariant(), stem = System.IO.Path.GetFileNameWithoutExtension(f);
            if (!exts.Contains(ext) || !stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || stem.EndsWith("_normal", StringComparison.OrdinalIgnoreCase)) continue;
            string? normal = normals ? files.FirstOrDefault(n => System.IO.Path.GetFileNameWithoutExtension(n).Equals(stem + "_normal", StringComparison.OrdinalIgnoreCase)) : null;
            list.Add(new GroundPic(stem, PictureName(stem, prefix), $"{dir}/{f}", normal != null ? $"{dir}/{normal}" : null));
        }
        list.Sort((a, b) => Order(a.Id).CompareTo(Order(b.Id)) is var c && c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        return GroundLists[key] = list;

        int Order(string id) => id.Equals(prefix, StringComparison.OrdinalIgnoreCase) ? 1
            : int.TryParse(id[prefix.Length..], out int n) ? n : 1000;
    }

    /// <summary>A style's skies: <c>sky.exr</c>, <c>sky2.hdr</c>, <c>sky-sunset.exr</c>... (360-degree pictures). A style
    /// with none gets its painted sky.</summary>
    public static IReadOnlyList<GroundPic> Skies(TerrainBook.ETheme theme) => PicturesIn(theme.ToString(), "sky", false);

    /// <summary>Forget the folder lists (the Battlefield window's "Look again" after adding pictures).</summary>
    public static void Rescan() { GroundLists.Clear(); }

    /// <summary>"ground" -> "Ground 1", "ground3" -> "Ground 3", "ground-dry grass" -> "Dry grass" (skies the same way).</summary>
    public static string GroundName(string id) => PictureName(id, "ground");

    private static string PictureName(string id, string prefix)
    {
        string label = char.ToUpperInvariant(prefix[0]) + prefix[1..];
        if (id.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return label + " 1";
        string rest = id[prefix.Length..];
        if (int.TryParse(rest, out int n)) return label + " " + n;
        rest = rest.TrimStart('-', '_', ' ').Replace('_', ' ').Replace('-', ' ');
        return rest.Length == 0 ? id : char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    /// <summary>The grounds a map asks for: "" = the style's first; an id; or ids joined by '+' (up to three, mixed).
    /// Ids this computer's style doesn't have are skipped; nothing left = the first ground.</summary>
    public static List<GroundPic> Pick(TerrainBook.ETheme theme, string? spec)
    {
        var all = Grounds(theme);
        var picked = (spec ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => all.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).OfType<GroundPic>()
            .DistinctBy(g => g.Id).Take(3).ToList();
        if (picked.Count == 0 && all.Count > 0) picked.Add(all[0]);
        return picked;
    }

    /// <summary>The style's ground (<c>shaders/ground.gdshader</c>): the map's ground or mix of up to three, tinted to the
    /// style, laid out by world position so the table and land join up.</summary>
    public static Material Ground(TerrainBook.ETheme theme, string? spec = null)
    {
        Theme th = Of(theme);
        List<GroundPic> picks = Pick(theme, spec);
        if (picks.Count == 0) return new StandardMaterial3D { AlbedoColor = th.Ground, Roughness = 0.95f, VertexColorUseAsAlbedo = true };
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ground.gdshader") };
        mat.SetShaderParameter("tint", th.Tint);
        // Kai, 2026-11-18 (picked "E" from the comparison): the ground at life-size scale - one repeat every 3" of table,
        // two samples mixed and big soft patches over it, so it reads as real land and not wallpaper.
        mat.SetShaderParameter("detail_inches", DetailInches);
        mat.SetShaderParameter("mix_layers", true);
        mat.SetShaderParameter("macro", Macro);
        mat.SetShaderParameter("layers", picks.Count);
        string[] slots = { "detail", "detail2", "detail3" };
        bool normals = false;
        for (int i = 0; i < picks.Count; i++)
        {
            mat.SetShaderParameter(slots[i], Load(picks[i].Color));
            if (picks[i].Normal is { } n && Load(n) is { } nt) { mat.SetShaderParameter(slots[i] + "_normal", nt); normals = true; }
        }
        mat.SetShaderParameter("has_normal", normals);
        return mat;
    }

    /// <summary>A picture by its res:// path: the imported copy, or - for a picture just dropped into the folder and not
    /// imported yet - read straight from the file.</summary>
    public static Texture2D? LoadPicture(string path)
    {
        if (Pictures.TryGetValue(path, out var t)) return t;
        if (ResourceLoader.Exists(path)) return Pictures[path] = GD.Load<Texture2D>(path);
        Texture2D? raw = null;
        try
        {
            var img = new Image();
            if (img.Load(ProjectSettings.GlobalizePath(path)) == Error.Ok)
            {
                img.GenerateMipmaps();
                raw = ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception) { raw = null; }
        return Pictures[path] = raw;
    }

    private static Texture2D? Load(string path) => LoadPicture(path);

    private static NoiseTexture2D? _macro;
    /// <summary>Soft seamless noise for the ground's big patches and for mixing its two detail samples.</summary>
    public static NoiseTexture2D MacroNoise => Macro;
    private static NoiseTexture2D Macro => _macro ??= new NoiseTexture2D
    {
        Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.012f, FractalOctaves = 3, Seed = 4242 },
    };

    /// <summary>The map's (first) ground picture alone (for the mat picker and the worn-ground halos), or null.</summary>
    public static Texture2D? GroundPicture(TerrainBook.ETheme theme, string? spec = null) =>
        Pick(theme, spec) is { Count: > 0 } p ? Load(p[0].Color) : null;

    /// <summary>The play area drawn in the horizon's own ground (Kai, 2026-11-18: "make the terrain and the playable space be
    /// the same texture"), lined up with the land round it so there is no seam, with the faint inch grid over it.</summary>
    public static void AddTableGround(Node parent, float w, float d, TerrainBook.ETheme theme, Texture2D? grid, string? ground = null)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void V(float x, float z) { st.SetColor(Colors.White); st.SetUV(new Vector2(x / Tile, z / Tile)); st.SetNormal(Vector3.Up); st.AddVertex(new Vector3(x, 0.01f, z)); }
        float hw = w / 2, hd = d / 2;
        V(-hw, -hd); V(hw, -hd); V(hw, hd);
        V(-hw, -hd); V(hw, hd); V(-hw, hd);
        st.GenerateTangents();
        TableLook.Mesh(parent, st.Commit(), Ground(theme, ground), Vector3.Zero);
        if (grid == null) return;   // Settings -> "Table grid" off
        TableLook.Mesh(parent, new PlaneMesh { Size = new Vector2(w, d) }, new StandardMaterial3D
        {
            AlbedoTexture = grid, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        }, new Vector3(0, 0.015f, 0));
    }

    /// <summary>Grid lines from -reach to reach: every 6" across the table and just past it, widening outwards.</summary>
    private static List<float> Steps(float half, float reach)
    {
        var pos = new List<float> { 0 };
        float x = 0, step = 6f;
        while (x < reach)
        {
            x += step;
            pos.Add(Mathf.Min(x, reach));
            if (x > half + FlatMargin) step = Mathf.Min(step * 1.12f, 36f);
        }
        var all = pos.Skip(1).Select(p => -p).Reverse().ToList();
        all.AddRange(pos);
        return all;
    }

    private static void Border(Node3D root, float w, float d)
    {
        const float Width = 0.35f, Lift = 0.02f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(float x0, float z0, float x1, float z1)
        {
            st.AddVertex(new Vector3(x0, Lift, z0)); st.AddVertex(new Vector3(x1, Lift, z0)); st.AddVertex(new Vector3(x1, Lift, z1));
            st.AddVertex(new Vector3(x0, Lift, z0)); st.AddVertex(new Vector3(x1, Lift, z1)); st.AddVertex(new Vector3(x0, Lift, z1));
        }
        float hw = w / 2, hd = d / 2;
        Quad(-hw - Width, -hd - Width, hw + Width, -hd);
        Quad(-hw - Width, hd, hw + Width, hd + Width);
        Quad(-hw - Width, -hd, -hw, hd);
        Quad(hw, -hd, hw + Width, hd);
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.93f, 0.85f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        TableLook.Mesh(root, st.Commit(), mat, Vector3.Zero);
    }

    private static readonly Dictionary<string, Texture2D?> Pictures = new();

    private static bool HasOwn(TerrainBook.ETheme theme, string what) => Find(theme.ToString(), what) != null;

    /// <summary>A theme's picture, or Hospitable's when the theme has none of its own yet.</summary>
    private static Texture2D? Picture(TerrainBook.ETheme theme, string what)
    {
        string key = theme + "/" + what;
        if (Pictures.TryGetValue(key, out var have)) return have;
        string? path = Find(theme.ToString(), what) ?? (what == "sky" ? null : Find(nameof(TerrainBook.ETheme.Hospitable), what));
        return Pictures[key] = path != null ? GD.Load<Texture2D>(path) : null;
    }

    private static string? Find(string theme, string what)
    {
        foreach (string ext in new[] { ".exr", ".hdr", ".jpg", ".png", ".webp" })
        {
            string p = $"res://horizon/{theme}/{what}{ext}";
            if (ResourceLoader.Exists(p)) return p;
        }
        return null;
    }
}
