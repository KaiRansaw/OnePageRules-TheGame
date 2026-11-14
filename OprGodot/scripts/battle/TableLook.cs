using FDG;
using System.Linq;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// The table's look in one place - sky, light, felt and the inch grid - so the battle table and the Asset Lab show
/// a model under exactly the same conditions. Change the lighting here and both follow.
/// Table inches are world units.
/// </summary>
public static class TableLook
{
    /// <summary>The Rules view's colour for a piece (Map Maker and battle): red blocks, orange Dangerous, green difficult + cover, blue cover, yellow difficult.</summary>
    public static Color RulesColour(ETerrainType t) =>
        t.HasFlag(ETerrainType.Impassible) || t.HasFlag(ETerrainType.Blocking) ? new Color(0.86f, 0.27f, 0.24f)
        : t.HasFlag(ETerrainType.Dangerous) ? new Color(0.95f, 0.45f, 0.15f)
        : t.HasFlag(ETerrainType.Difficult) && t.HasFlag(ETerrainType.Cover) ? new Color(0.47f, 0.78f, 0.35f)
        : t.HasFlag(ETerrainType.Cover) ? new Color(0.35f, 0.78f, 0.86f)
        : t.HasFlag(ETerrainType.Difficult) ? new Color(0.94f, 0.75f, 0.27f)
        : new Color(0.55f, 0.55f, 0.6f);

    /// <summary>The Rules view's short label ("Diff+Cover").</summary>
    public static string RulesShort(ETerrainType t) => t == ETerrainType.None ? "Scenery"
        : string.Join("+", TerrainBook.RuleFlags.Where(f => t.HasFlag(f)).Select(f => f switch
        {
            ETerrainType.Impassible => "Imp", ETerrainType.Blocking => "Blocks", ETerrainType.Difficult => "Diff",
            ETerrainType.Dangerous => "Dang", ETerrainType.Unstable => "Unst", ETerrainType.Climbable => "Climb", _ => f.ToString(),
        }));


    /// <summary>The background, ambient light, sun and rim light the battle is played under.</summary>
    public static void AddLighting(Node parent)
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.018f, 0.026f, 0.03f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.45f, 0.55f, 0.6f), AmbientLightEnergy = 0.45f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, GlowEnabled = true, GlowIntensity = 0.35f,
        };
        parent.AddChild(new WorldEnvironment { Environment = env, Name = "Environment" });
        var sun = new DirectionalLight3D { LightEnergy = 1.2f, LightColor = new Color(1f, 0.95f, 0.88f), ShadowEnabled = true, Name = "Sun" };
        sun.RotationDegrees = new Vector3(-58, 32, 0);
        parent.AddChild(sun);
        var rim = new DirectionalLight3D { LightEnergy = 0.3f, LightColor = new Color(0.45f, 0.75f, 0.85f), Name = "Rim" };
        rim.RotationDegrees = new Vector3(-25, 200, 0);
        parent.AddChild(rim);
    }

    /// <summary>The floor under the table, the wooden table edge, and the felt with its inch grid (every 6" and 12" stronger).</summary>
    public static void AddBoard(Node parent, float w, float d)
    {
        Mesh(parent, new PlaneMesh { Size = new Vector2(800, 800) }, Mat(new Color(0.03f, 0.04f, 0.045f)), new Vector3(0, -3, 0));
        Mesh(parent, new BoxMesh { Size = new Vector3(w + 2, 3, d + 2) }, Mat(new Color(0.09f, 0.075f, 0.06f), 0.6f), new Vector3(0, -1.55f, 0));
        Mesh(parent, new PlaneMesh { Size = new Vector2(w, d) }, new StandardMaterial3D { AlbedoTexture = Felt(w, d), Roughness = 0.95f }, new Vector3(0, 0.01f, 0));
    }

    /// <summary>The felt picture: a speckled green cloth with the inch grid drawn in (12 pixels to the inch).</summary>
    public static ImageTexture Felt(float w, float d)
    {
        const int ppi = 12;
        int iw = (int)Mathf.Ceil(w) * ppi, ih = (int)Mathf.Ceil(d) * ppi;
        var img = Image.CreateEmpty(iw, ih, false, Image.Format.Rgba8);
        var felt = new Color(0.115f, 0.14f, 0.125f);
        img.Fill(felt);
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < iw * ih / 30; i++)
            img.SetPixel(rng.RandiRange(0, iw - 1), rng.RandiRange(0, ih - 1), felt.Darkened(rng.RandfRange(0.02f, 0.12f)));
        for (int x = 0; x <= (int)w; x++)
        {
            float a = x % 12 == 0 ? 0.2f : x % 6 == 0 ? 0.1f : 0.035f;
            int px = Mathf.Min(x * ppi, iw - 1);
            for (int y = 0; y < ih; y++) img.SetPixel(px, y, img.GetPixel(px, y).Lerp(Colors.White, a));
        }
        for (int z = 0; z <= (int)d; z++)
        {
            float a = z % 12 == 0 ? 0.2f : z % 6 == 0 ? 0.1f : 0.035f;
            int py = Mathf.Min(z * ppi, ih - 1);
            for (int x = 0; x < iw; x++) img.SetPixel(x, py, img.GetPixel(x, py).Lerp(Colors.White, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static StandardMaterial3D Mat(Color c, float rough = 0.85f, Color? emit = null, float emitE = 1f)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough };
        if (emit is { } e) { m.EmissionEnabled = true; m.Emission = e; m.EmissionEnergyMultiplier = emitE; }
        if (c.A < 1) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return m;
    }

    public static MeshInstance3D Mesh(Node parent, Mesh mesh, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>
    /// The model base the table draws under every model: a rim in the seat colour and a dark top. Shared with the
    /// Asset Lab, so a real model is judged on the same base it will stand on.
    /// </summary>
    public static void AddBase(Node parent, float radiusInches, Color team)
    {
        float rad = Mathf.Max(0.4f, radiusInches);
        Mesh(parent, new CylinderMesh { TopRadius = rad + 0.05f, BottomRadius = rad + 0.07f, Height = 0.08f }, Mat(team, 0.4f, team, 1.3f), new Vector3(0, 0.05f, 0));
        Mesh(parent, new CylinderMesh { TopRadius = rad * 0.94f, BottomRadius = rad, Height = 0.2f }, Mat(new Color(0.08f, 0.09f, 0.1f), 0.5f), new Vector3(0, 0.15f, 0));
    }

    /// <summary>Height of the top of <see cref="AddBase"/> - where a model's feet go.</summary>
    public const float BaseTop = 0.25f;
}
