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
    public static bool On => FdgRaylib.Config.UserConfig.Current.View.Room == FdgRaylib.Terrain.TableRooms.Battlefield;

    /// <summary>Points <paramref name="env"/> at the theme's sky and haze. Call after <see cref="TableLook.AddLighting"/>.</summary>
    public static void SkyAndHaze(Godot.Environment env, TerrainBook.ETheme theme, float tableW, float tableD)
    {
        Theme th = Of(theme);
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        Texture2D? pano = Picture(theme, "sky");
        env.Sky = new Sky
        {
            SkyMaterial = pano != null
                ? new PanoramaSkyMaterial { Panorama = pano, EnergyMultiplier = theme == TerrainBook.ETheme.Hospitable || HasOwn(theme, "sky") ? 1f : 0.8f }
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
        float near = Mathf.Max(tableW, tableD) * 2.5f + 60f;
        env.FogEnabled = true;
        env.FogMode = Godot.Environment.FogModeEnum.Depth;
        env.FogLightColor = th.Haze;
        env.FogDensity = 1f;
        env.FogDepthBegin = near;
        env.FogDepthEnd = Reach * 0.85f;
        env.FogDepthCurve = 1.6f;
        env.FogSkyAffect = 0f;
        env.FogAerialPerspective = 0f; // sky-tinted haze samples the panorama's dark lower half: plain haze colour instead
    }

    /// <summary>The land round a <paramref name="tableW"/> x <paramref name="tableD"/> table centred on the origin.</summary>
    public static void AddLand(Node3D parent, float tableW, float tableD, TerrainBook.ETheme theme)
    {
        Theme th = Of(theme);
        var root = new Node3D { Name = "Horizon" };
        parent.AddChild(root);

        var noise = new FastNoiseLite { Seed = 1793 + (int)theme, Frequency = 0.0022f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
        var ridges = new FastNoiseLite { Seed = 31 + (int)theme, Frequency = 0.0011f, FractalType = FastNoiseLite.FractalTypeEnum.Ridged, FractalOctaves = 3 };
        float hw = tableW / 2, hd = tableD / 2;
        float Height(float x, float z)
        {
            // Distance from the table's edge (0 on the mat), so the flat ground hugs any table shape.
            float dx = Mathf.Max(Mathf.Abs(x) - hw, 0), dz = Mathf.Max(Mathf.Abs(z) - hd, 0);
            float d = Mathf.Sqrt(dx * dx + dz * dz) - FlatMargin;
            if (d <= 0) return -0.03f;
            float hills = Mathf.SmoothStep(0, 260, d) * th.Hills * (0.5f + 0.5f * noise.GetNoise2D(x, z));
            float mountains = Mathf.SmoothStep(450, 1100, d) * th.Mountains * Mathf.Max(0, ridges.GetNoise2D(x, z) + 0.15f);
            return -0.03f + hills + mountains;
        }

        // One square grid out to Reach; finer near the table so the first slopes are smooth.
        var xs = Steps(hw, Reach);
        var zs = Steps(hd, Reach);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float Tile = 60f; // the ground picture repeats every 5 feet
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
        var mat = new StandardMaterial3D
        {
            AlbedoColor = th.Tint, VertexColorUseAsAlbedo = true, Roughness = 0.95f,
            AlbedoTexture = Picture(theme, "ground"), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (mat.AlbedoTexture == null) mat.AlbedoColor = th.Ground;
        if (Picture(theme, "ground_normal") is { } normal) { mat.NormalEnabled = true; mat.NormalTexture = normal; mat.NormalScale = 0.6f; }
        TableLook.Mesh(root, st.Commit(), mat, Vector3.Zero).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        // The playable edge: a thin light line round the mat.
        Border(root, tableW, tableD);
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
