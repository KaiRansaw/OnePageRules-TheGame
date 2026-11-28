using FdgRaylib.Terrain;
using Godot;
using static FdgRaylib.Terrain.TerrainLooks;

namespace OprGodot.Battle;

/// <summary>
/// Real surfaces for flat terrain, and the order every piece's look is chosen in (Kai, 2026-11-27: "I don't want any AI
/// assets remaining in the game, other than your code").
///
/// Water, swamps, lava, fields and hills used to be flat colours drawn in code, and anything without a model fell back to
/// plain boxes. Now:
///  - surfaces are CC0 photo materials (ambientCG, 3DTextures.me - terrain/surfaces/License.txt) laid on the piece's own
///    shape by shaders/terrain_surface.gdshader: rippling water over a mud bed, flowing lava cooling to crust at its edge,
///    mud with murky pools for swamps, tilled soil under crops, grass and dirt on hills;
///  - every piece is dressed from the real free model packs: Kai's Terrain Workshop piece if one fits, else the catalogue
///    look for its kind (<see cref="TerrainLooks"/>), else the nearest kind's look - never a made-up shape.
/// </summary>
public static partial class TerrainDresser
{
    public enum ESurface { None, Water, MurkyWater, Lava, Magma, Mud, Soil, Grass, DarkGrass, GrassyDirt, MossyGround }

    private const string SurfaceDir = "res://terrain/surfaces/";
    private static Shader? _surfaceShader;
    private static readonly Dictionary<string, ShaderMaterial> _surfaceMats = new();
    private static readonly Dictionary<string, Texture2D?> _surfaceTex = new();

    private static Texture2D? Tex(string name)
    {
        if (_surfaceTex.TryGetValue(name, out var t)) return t;
        string path = SurfaceDir + name + ".jpg";
        return _surfaceTex[name] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>The material for a surface (shared per surface and depth).</summary>
    public static ShaderMaterial SurfaceMaterial(ESurface s, float depth = 0.5f)
    {
        string key = $"{s}|{depth:0.##}";
        if (_surfaceMats.TryGetValue(key, out var have)) return have;
        _surfaceShader ??= GD.Load<Shader>("res://shaders/terrain_surface.gdshader");
        var m = new ShaderMaterial { Shader = _surfaceShader };
        void Ground(string pic, float tile, Color? tint = null)
        {
            m.SetShaderParameter("mode", 0);
            m.SetShaderParameter("albedo_tex", Tex(pic));
            m.SetShaderParameter("normal_tex", Tex(pic + "_normal"));
            m.SetShaderParameter("tile_inches", tile);
            m.SetShaderParameter("tint", tint ?? Colors.White);
        }
        switch (s)
        {
            case ESurface.Water or ESurface.MurkyWater:
                m.SetShaderParameter("mode", 1);
                m.SetShaderParameter("albedo_tex", Tex("mud"));
                m.SetShaderParameter("water_normal", Tex("water_normal"));
                m.SetShaderParameter("tile_inches", 8f);
                m.SetShaderParameter("depth", depth);
                m.SetShaderParameter("flow", 0.03f);
                m.SetShaderParameter("water_colour", s == ESurface.Water ? new Color(0.16f, 0.36f, 0.46f) : new Color(0.24f, 0.28f, 0.14f));
                m.SetShaderParameter("tint", s == ESurface.Water ? Colors.White : new Color(0.85f, 0.9f, 0.7f));
                break;
            case ESurface.Lava or ESurface.Magma:
                string molten = s == ESurface.Lava ? "lava_flow" : "lava_crust";
                m.SetShaderParameter("mode", 2);
                m.SetShaderParameter("albedo_tex", Tex(molten));
                m.SetShaderParameter("emission_tex", Tex(molten + "_emission"));
                m.SetShaderParameter("normal_tex", Tex(molten + "_normal"));
                m.SetShaderParameter("crust_tex", Tex("lava_crust"));
                m.SetShaderParameter("tile_inches", 7f);
                m.SetShaderParameter("glow", s == ESurface.Lava ? 1.6f : 2.6f);
                m.SetShaderParameter("flow", 0.02f);
                break;
            case ESurface.Mud: Ground("mud", 5f, new Color(0.9f, 0.88f, 0.8f)); break;
            case ESurface.Soil: Ground("grassy_dirt", 5f, new Color(1.05f, 0.95f, 0.85f)); break;   // turned earth, a little grass
            case ESurface.Grass: Ground("grass", 5f); break;
            case ESurface.DarkGrass: Ground("dark_grass", 5f); break;
            case ESurface.GrassyDirt: Ground("grassy_dirt", 6f); break;
            case ESurface.MossyGround: Ground("mossy_ground", 7f, new Color(0.85f, 0.82f, 0.78f)); break;
        }
        return _surfaceMats[key] = m;
    }

    /// <summary>The surface a look lies on (None: the models stand straight on the mat, Kai 2026-11-18).</summary>
    public static ESurface SurfaceOf(Look look, string kind)
    {
        bool grim = look.Theme == ETheme.Grimdark;
        if (look.Kinds.Contains("Lava")) return grim ? ESurface.Magma : ESurface.Lava;
        if (look.Kinds.Any(k => k.StartsWith("Lake") || k.StartsWith("River"))) return grim ? ESurface.MurkyWater : ESurface.Water;
        if (look.Kinds.Contains("Swamp")) return ESurface.Mud;
        if (look.Kinds.Contains("Field")) return ESurface.Soil;
        if (look.Layout == ELayout.Mound)
            return look.Id switch { "grassy-hill" => ESurface.Grass, "rocky-hill" => ESurface.GrassyDirt, _ => ESurface.MossyGround };
        return ESurface.None;
    }

    /// <summary>How deep a water piece looks: deep lakes and rivers darker, shallow ones show their bed.</summary>
    public static float DepthOf(string kind) => kind.EndsWith("Deep") ? 0.85f : 0.35f;

    /// <summary>
    /// A surface filling the outline, <paramref name="lift"/> over the mat, its outer <paramref name="band"/> inches fading
    /// into the mat (vertex alpha = distance from the edge). Any shape - concave outlines are triangulated, then each
    /// triangle split so the fade has vertices to live on.
    /// </summary>
    public static MeshInstance3D? SurfacePatch(Node3D root, Vector2[] poly, ShaderMaterial mat, float lift, float band = 0.8f)
    {
        int[] tris = Geometry2D.TriangulatePolygon(poly);
        if (tris.Length < 3) return null;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        void V(Vector2 p)
        {
            float a = Mathf.Clamp(EdgeDistance(poly, p) / Mathf.Max(0.05f, band), 0f, 1f);
            st.SetColor(new Color(1, 1, 1, a));
            st.SetNormal(Vector3.Up);
            st.AddVertex(new Vector3(p.X, lift, p.Y));
        }
        void Split(Vector2 a, Vector2 b, Vector2 c, int depth)
        {
            if (depth == 0 || (a.DistanceTo(b) < band * 0.6f && b.DistanceTo(c) < band * 0.6f && c.DistanceTo(a) < band * 0.6f))
            {
                // Faces up (clockwise seen from above - Godot's front face) whichever way the outline runs; a back face would
                // flip the up normal and light the surface from below.
                if ((b - a).Cross(c - a) < 0) (b, c) = (c, b);
                V(a); V(b); V(c);
                return;
            }
            Vector2 ab = (a + b) / 2, bc = (b + c) / 2, ca = (c + a) / 2;
            Split(a, ab, ca, depth - 1); Split(ab, b, bc, depth - 1); Split(ca, bc, c, depth - 1); Split(ab, bc, ca, depth - 1);
        }
        for (int i = 0; i + 2 < tris.Length; i += 3)
            Split(poly[tris[i]], poly[tris[i + 1]], poly[tris[i + 2]], 4);
        var mi = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(mi);
        return mi;
    }

    /// <summary>A swamp: wet mud, with two to four murky pools in it (reeds and plants come from the look).</summary>
    private static void SwampFloor(Node3D root, Vector2[] poly, Random rng)
    {
        SurfacePatch(root, poly, SurfaceMaterial(ESurface.Mud), BaseHeight * 0.5f, 1.0f);
        float area = Mathf.Abs(Area(poly));
        int pools = Math.Clamp((int)(area / 18f), 2, 4);
        foreach (Vector2 c in Points(poly, pools, 2.5f, 1.2f, rng))
        {
            float r = Mathf.Clamp(Mathf.Min(EdgeDistance(poly, c) * 0.8f, 0.8f + (float)rng.NextDouble() * 1.4f), 0.5f, 2.2f);
            var pool = Enumerable.Range(0, 14).Select(i =>
            {
                float a = i / 14f * Mathf.Tau;
                float k = r * (0.75f + 0.25f * (float)rng.NextDouble());
                return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * k;
            }).ToArray();
            SurfacePatch(root, pool, SurfaceMaterial(ESurface.MurkyWater, 0.4f), BaseHeight * 0.8f, 0.5f);
        }
    }

    // ------------------------------------------------------------------ which look a piece gets

    /// <summary>Kinds to fall back to when a theme has no look for a kind - the nearest real thing.</summary>
    private static readonly Dictionary<string, string[]> Nearest = new()
    {
        ["Hazard"] = new[] { "Rubble" },
        ["Wreck"] = new[] { "Rubble", "Ruins" },
        ["Mountain"] = new[] { "Hill", "Rubble" },
        ["Hill"] = new[] { "Mountain", "Rubble" },
        ["Barricade"] = new[] { "Wall" },
        ["Wall"] = new[] { "Barricade" },
        ["Building"] = new[] { "Ruins" },
        ["Ruins"] = new[] { "Building", "Rubble" },
        ["Swamp"] = new[] { "LakeShallow" },
        ["Field"] = new[] { "Forest" },
    };

    /// <summary>
    /// The piece's look from the real model packs - its own kind's catalogue look (the map's pick if it made one), else
    /// the nearest kind's, else rubble. Only a Tactical Phase marker (a rules marker, no terrain) is a plain disc.
    /// </summary>
    public static void DressAuto(Node3D parent, IReadOnlyList<Vector2[]> polygons, float height, string kind, string? lookId,
        ETheme theme, int seed, string meta)
    {
        if (kind == "Marker")
        {
            foreach (Vector2[] poly in polygons.Where(p => p.Length >= 3))
                if (BattleTable.Prism(poly, 0.03f) is { } disc)
                    TableLook.Mesh(parent, disc, TableLook.Mat(new Color("#aabe5a"), 0.9f), Vector3.Zero);
            return;
        }
        if (Dress(parent, polygons, height, kind, lookId, theme, seed, meta)) return;
        foreach (string near in (Nearest.TryGetValue(kind, out var list) ? list : Array.Empty<string>()).Append("Rubble"))
            if (Dress(parent, polygons, height, near, null, theme, seed, meta)) return;
    }
}
