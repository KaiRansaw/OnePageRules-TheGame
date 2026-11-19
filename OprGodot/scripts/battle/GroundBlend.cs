using Godot;

namespace OprGodot.Battle;

/// <summary>
/// Terrain sitting IN the ground, not on it (Kai, 2026-11-18). Round each piece's footprint, on the battlefield only:
/// <list type="bullet">
/// <item><b>shade</b> - the ground a little darker under and just round the piece (contact shadow);</item>
/// <item><b>dirt</b> - a ring of worn, trodden ground in the battlefield's own ground picture, ragged at the edge;</item>
/// <item><b>tufts</b> - small grass tufts and stones along the footprint edge, at life-size scale.</item>
/// </list>
/// Looks only: nothing here changes the footprint the rules use.
/// </summary>
public static class GroundBlend
{
    /// <summary>Which layers are drawn (letters s, d, t). BACKBURNER (Kai, 2026-11-18): off until the built-in terrain set
    /// is finalised, then Kai picks from the comparison. Never applied to Warhammer models (callers skip them). The worn
    /// ground still needs its lighting fixed (some halos came out near-black - their triangles face down).</summary>
    public static string Layers = "";

    public static void Add(Node3D holder, IReadOnlyList<Vector2[]> polygons, string kind, int seed, FDG.TerrainBook.ETheme theme)
    {
        if (!Horizon.On || Layers.Length == 0) return;
        string k = kind.ToLowerInvariant();
        if (k.Contains("lake") || k.Contains("river") || k.Contains("water") || k.Contains("lava")) return;
        var rng = new Random(seed);
        foreach (Vector2[] poly in polygons.Where(p => p.Length >= 3))
        {
            if (Layers.Contains('d')) Halo(holder, poly, 3.5f, 0.85f, Dirt(theme), 0.018f);
            if (Layers.Contains('s')) Halo(holder, poly, 2.8f, 0.26f, Shade, 0.022f);
            if (Layers.Contains('t')) TerrainDresser.EdgeTufts(holder, poly, rng);
        }
    }

    /// <summary>The footprint filled at <paramref name="alpha"/>, fading to nothing <paramref name="reach"/> inches out.</summary>
    private static void Halo(Node3D holder, Vector2[] poly, float reach, float alpha, Material mat, float lift)
    {
        Vector2 c = poly.Aggregate(Vector2.Zero, (a, b) => a + b) / poly.Length;
        Vector2 Out(Vector2 v) { Vector2 d = v - c; float l = d.Length(); return l < 0.001f ? v : v + d / l * reach; }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void V(Vector2 p, float a) { st.SetColor(new Color(1, 1, 1, a)); st.SetNormal(Vector3.Up); st.AddVertex(new Vector3(p.X, lift, p.Y)); }
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            V(c, alpha); V(a, alpha); V(b, alpha);
            V(a, alpha); V(Out(a), 0); V(Out(b), 0);
            V(a, alpha); V(Out(b), 0); V(b, alpha);
        }
        var mi = TableLook.Mesh(holder, st.Commit(), mat, Vector3.Zero);
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static Material? _shade;
    private static Material Shade => _shade ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.02f, 0.025f, 0.015f), VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static readonly Dictionary<FDG.TerrainBook.ETheme, Material> DirtMats = new();
    private static Material Dirt(FDG.TerrainBook.ETheme theme)
    {
        if (DirtMats.TryGetValue(theme, out var m)) return m;
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ground_blend.gdshader") };
        mat.SetShaderParameter("detail", Horizon.GroundPicture(theme));
        mat.SetShaderParameter("detail_inches", Horizon.DetailInches);
        mat.SetShaderParameter("noise", Horizon.MacroNoise);
        return DirtMats[theme] = mat;
    }
}
