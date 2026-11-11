using Godot;
using OprGodot.Hud;

namespace OprGodot.Mock;

/// <summary>
/// A stand-in 3D table for the HUD mockups: a 6'x4' board (1 unit = 1 inch), a few terrain pieces,
/// three objectives with their 3" rings, and two small AoF armies as bases with upright tokens.
/// Not the real table (that is port step 3) - just enough board for the HUD to sit over.
/// </summary>
public partial class MockTable : Node3D
{
    public const float W = 72, D = 48;

    public sealed record Placed(SampleUnit Unit, bool Mine, Vector3 Centre, List<Vector3> Models);
    public readonly List<Placed> Units = new();
    public Camera3D Camera { get; private set; } = null!;
    public bool ShowLabels { get; set; } = true;
    public readonly Dictionary<string, Label3D> Labels = new();

    public override void _Ready()
    {
        Environment();
        Board();
        Terrain();
        Objective(new Vector3(-18, 0, 2), HudKit.Friendly);
        Objective(new Vector3(4, 0, 0), new Color(0.85f, 0.88f, 0.9f));
        Objective(new Vector3(24, 0, -2), HudKit.Enemy);

        const string he = "AoF-HumanEmpire", orc = "AoF-Orcs";
        Add(he, "Marksmen", true, new(-6, 0, 14));
        Add(he, "Infantrymen", true, new(7, 0, 16));
        Add(he, "Knights", true, new(19, 0, 12));
        Add(he, "Battle Master", true, new(1, 0, 19.5f));
        Add(he, "Empire Artillery", true, new(-17, 0, 19));
        Add(orc, "Iron Shooters", false, new(-11, 0, -3));
        Add(orc, "Iron Warriors", false, new(3, 0, -8));
        Add(orc, "Brute Orc Boss", false, new(3, 0, -13.5f));
        Add(orc, "Iron Tusk-Beast Riders", false, new(18, 0, -7));
        Add(orc, "Swamp Crawler", false, new(26, 0, -15));

        Camera = new Camera3D { Fov = 42, Current = true };
        AddChild(Camera);
        Look(new Vector3(-2, 50, 50), new Vector3(-1, 0, 3));
    }

    public void Look(Vector3 from, Vector3 at, float fov = 42)
    {
        Camera.Position = from;
        Camera.Fov = fov;
        Camera.LookAtFromPosition(from, at, Vector3.Up);
    }

    // ------------------------------------------------------------------ world
    private void Environment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.018f, 0.026f, 0.03f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.45f, 0.55f, 0.6f), AmbientLightEnergy = 0.4f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, GlowEnabled = true, GlowIntensity = 0.35f, GlowBloom = 0.0f,
            FogEnabled = true, FogLightColor = new Color(0.05f, 0.08f, 0.09f), FogDensity = 0.004f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { LightEnergy = 1.25f, LightColor = new Color(1f, 0.95f, 0.88f), ShadowEnabled = true };
        sun.RotationDegrees = new Vector3(-58, 32, 0);
        AddChild(sun);
        var rim = new DirectionalLight3D { LightEnergy = 0.35f, LightColor = new Color(0.45f, 0.75f, 0.85f) };
        rim.RotationDegrees = new Vector3(-25, 200, 0);
        AddChild(rim);
    }

    private static StandardMaterial3D Mat(Color c, float rough = 0.85f, Color? emit = null, float emitE = 1f)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough };
        if (emit is { } e) { m.EmissionEnabled = true; m.Emission = e; m.EmissionEnergyMultiplier = emitE; }
        if (c.A < 1) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return m;
    }

    private MeshInstance3D Mesh(Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        if (rotDeg is { } r) mi.RotationDegrees = r;
        AddChild(mi);
        return mi;
    }

    private void Board()
    {
        // Floor round the table, then the table edge and the mat with its inch grid and deployment strips.
        Mesh(new PlaneMesh { Size = new Vector2(600, 600) }, Mat(new Color(0.03f, 0.04f, 0.045f)), new Vector3(0, -3, 0));
        Mesh(new BoxMesh { Size = new Vector3(W + 2, 3, D + 2) }, Mat(new Color(0.09f, 0.075f, 0.06f), 0.6f), new Vector3(0, -1.55f, 0));

        const int ppi = 16;
        var img = Image.CreateEmpty((int)W * ppi, (int)D * ppi, false, Image.Format.Rgba8);
        var felt = new Color(0.115f, 0.14f, 0.125f);
        img.Fill(felt);
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < 9000; i++)
        {
            int x = rng.RandiRange(0, img.GetWidth() - 1), y = rng.RandiRange(0, img.GetHeight() - 1);
            img.SetPixel(x, y, felt.Darkened(rng.RandfRange(0.02f, 0.12f)));
        }
        void Band(int y0, int y1, Color tint)
        {
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < img.GetWidth(); x++)
                    img.SetPixel(x, y, img.GetPixel(x, y).Lerp(tint, 0.10f));
        }
        Band(0, 12 * ppi, HudKit.Enemy);
        Band(img.GetHeight() - 12 * ppi, img.GetHeight(), HudKit.Friendly);
        for (int x = 0; x <= W; x++)
        {
            float a = x % 12 == 0 ? 0.22f : x % 6 == 0 ? 0.11f : 0.04f;
            for (int y = 0; y < img.GetHeight(); y++)
            {
                int px = Mathf.Min(x * ppi, img.GetWidth() - 1);
                img.SetPixel(px, y, img.GetPixel(px, y).Lerp(Colors.White, a));
            }
        }
        for (int y = 0; y <= D; y++)
        {
            float a = y % 12 == 0 ? 0.22f : y % 6 == 0 ? 0.11f : 0.04f;
            int py = Mathf.Min(y * ppi, img.GetHeight() - 1);
            for (int x = 0; x < img.GetWidth(); x++)
                img.SetPixel(x, py, img.GetPixel(x, py).Lerp(Colors.White, a));
        }
        var mat = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(img), Roughness = 0.95f };
        Mesh(new PlaneMesh { Size = new Vector2(W, D) }, mat, new Vector3(0, 0.01f, 0));
    }

    private void Terrain()
    {
        // Forest (cover) the Iron Shooters stand in.
        Mesh(new CylinderMesh { TopRadius = 5.5f, BottomRadius = 5.8f, Height = 0.2f }, Mat(new Color(0.16f, 0.19f, 0.12f)), new Vector3(-11, 0.1f, -3));
        var trunk = Mat(new Color(0.25f, 0.18f, 0.12f));
        var leaves = Mat(new Color(0.12f, 0.24f, 0.14f));
        foreach (var (x, z, h) in new[] { (-14.5f, -5.5f, 5f), (-8f, -6.5f, 6f), (-15f, -0.5f, 5.5f), (-7.5f, 0.5f, 4.5f), (-11.5f, 1.5f, 6.2f) })
        {
            Mesh(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.35f, Height = 1.6f }, trunk, new Vector3(x, 0.9f, z));
            Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 1.5f, Height = h }, leaves, new Vector3(x, 1.4f + h / 2, z));
        }

        // Ruins: two broken L-walls.
        var stone = Mat(new Color(0.42f, 0.42f, 0.40f), 0.9f);
        Mesh(new BoxMesh { Size = new Vector3(8, 3.5f, 0.7f) }, stone, new Vector3(-24, 1.75f, 8));
        Mesh(new BoxMesh { Size = new Vector3(0.7f, 2.2f, 5) }, stone, new Vector3(-27.6f, 1.1f, 5.8f));
        Mesh(new BoxMesh { Size = new Vector3(6, 2.6f, 0.7f) }, stone, new Vector3(14, 1.3f, -18));
        Mesh(new BoxMesh { Size = new Vector3(0.7f, 3.6f, 6) }, stone, new Vector3(16.6f, 1.8f, -15.3f));
        Mesh(new BoxMesh { Size = new Vector3(1.4f, 1.0f, 1.2f) }, stone, new Vector3(12f, 0.5f, -15f), new Vector3(0, 25, 12));

        // Hill and a stretch of low wall.
        var hill = Mesh(new SphereMesh { Radius = 5, Height = 10 }, Mat(new Color(0.17f, 0.19f, 0.14f)), new Vector3(29, -0.3f, 14));
        hill.Scale = new Vector3(1.1f, 0.3f, 0.8f);
        var wood = Mat(new Color(0.33f, 0.26f, 0.18f));
        for (int i = 0; i < 4; i++)
            Mesh(new BoxMesh { Size = new Vector3(2.8f, 1.1f, 0.5f) }, wood, new Vector3(-2 + i * 3f, 0.55f, 6 + (i % 2) * 0.3f), new Vector3(0, i * 4 - 6, 0));
    }

    private void Objective(Vector3 at, Color colour)
    {
        Mesh(new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.2f }, Mat(colour, 0.4f, colour, 1.6f), at + new Vector3(0, 0.1f, 0));
        Ring(at, 3f, colour, 0.07f, 1.2f);
        var lbl = new Label3D { Text = "OBJECTIVE", FontSize = 40, PixelSize = 0.02f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = colour, OutlineSize = 10, Position = at + new Vector3(0, 1.3f, 0), Font = HudKit.Bold };
        if (ShowLabels) AddChild(lbl); else lbl.Free();
    }

    /// <summary>A thin glowing ring on the table: objective ranges, movement ranges, selections.</summary>
    public MeshInstance3D Ring(Vector3 at, float radius, Color colour, float thickness = 0.08f, float glow = 2f)
    {
        var mesh = new TorusMesh { InnerRadius = radius - thickness, OuterRadius = radius + thickness, Rings = 96, RingSegments = 6 };
        var mi = Mesh(mesh, Mat(colour, 0.3f, colour, glow), at + new Vector3(0, 0.06f, 0));
        mi.Scale = new Vector3(1, 0.25f, 1);
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return mi;
    }

    /// <summary>A flat see-through disc: how far a unit can move.</summary>
    public void Area(Vector3 at, float radius, Color colour)
    {
        var mi = Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.02f, RadialSegments = 96 },
            Mat(colour with { A = 0.06f }, 1f, colour, 0.08f), at + new Vector3(0, 0.04f, 0));
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Ring(at, radius, colour, 0.08f, 1.4f);
    }

    /// <summary>A glowing line along the table - line of fire from shooter to target.</summary>
    public void Beam(Vector3 from, Vector3 to, Color colour)
    {
        from.Y = to.Y = 1.0f;
        float len = from.DistanceTo(to);
        var mi = Mesh(new BoxMesh { Size = new Vector3(0.12f, 0.12f, len) }, Mat(colour, 0.3f, colour, 3f), (from + to) / 2);
        mi.LookAt(to, Vector3.Up);
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private void Add(string book, string name, bool mine, Vector3 centre)
    {
        SampleUnit u = MockData.Unit(book, name);
        var models = new List<Vector3>();
        float baseD = Mathf.Max(u.Base, 1f);
        float step = baseD + 0.6f;
        int perRow = Mathf.Min(u.Models, 5);
        int rows = (u.Models + perRow - 1) / perRow;
        float facing = mine ? 1 : -1;
        for (int i = 0; i < u.Models; i++)
        {
            int r = i / perRow, c = i % perRow;
            models.Add(centre + new Vector3((c - (perRow - 1) / 2f) * step, 0, (r - (rows - 1) / 2f) * step * facing));
        }

        Color team = mine ? HudKit.Friendly : HudKit.Enemy;
        var baseMat = Mat(new Color(0.08f, 0.09f, 0.1f), 0.5f);
        var rimMat = Mat(team, 0.4f, team, 1.4f);
        var bodyMat = Mat(team.Darkened(0.45f).Lerp(new Color(0.55f, 0.55f, 0.55f), 0.25f), 0.55f);
        bool big = u.Tough >= 6;
        foreach (var p in models)
        {
            float rad = baseD / 2;
            Mesh(new CylinderMesh { TopRadius = rad + 0.06f, BottomRadius = rad + 0.08f, Height = 0.08f }, rimMat, p + new Vector3(0, 0.05f, 0));
            Mesh(new CylinderMesh { TopRadius = rad * 0.94f, BottomRadius = rad, Height = 0.2f }, baseMat, p + new Vector3(0, 0.15f, 0));
            if (big)
                Mesh(new BoxMesh { Size = new Vector3(baseD * 1.6f, 1.8f, baseD * 2.2f) }, bodyMat, p + new Vector3(0, 1.15f, 0));
            else if (name.Contains("Artillery"))
            {
                Mesh(new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.45f, Height = 2.4f }, bodyMat, p + new Vector3(0, 0.8f, 0), new Vector3(70 * facing, 0, 0));
                Mesh(new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.2f }, baseMat, p + new Vector3(0.9f, 0.6f, 0), new Vector3(0, 0, 90));
                Mesh(new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.2f }, baseMat, p + new Vector3(-0.9f, 0.6f, 0), new Vector3(0, 0, 90));
            }
            else
            {
                float h = u.Hero ? 1.55f : u.Base > 1.05f ? 1.3f : 1.05f;
                Mesh(new CapsuleMesh { Radius = rad * 0.42f, Height = h }, bodyMat, p + new Vector3(0, 0.25f + h / 2, 0));
                Mesh(new SphereMesh { Radius = rad * 0.3f, Height = rad * 0.6f }, bodyMat, p + new Vector3(0, 0.3f + h, 0));
                if (u.Hero) Mesh(new BoxMesh { Size = new Vector3(0.08f, 2.4f, 0.08f) }, Mat(HudKit.Gold, 0.3f, HudKit.Gold, 0.6f), p + new Vector3(0.45f, 1.4f, 0));
            }
        }

        float top = big ? 3.6f : 2.9f;
        var label = new Label3D
        {
            Text = name.ToUpperInvariant(), Font = HudKit.Bold, FontSize = 44, PixelSize = 0.016f, OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = team.Lightened(0.35f), NoDepthTest = true,
            Position = centre + new Vector3(0, top + (rows - 1) * 0.3f, 0), OutlineModulate = new Color(0, 0, 0, 0.85f),
        };
        if (ShowLabels) { AddChild(label); Labels[name] = label; } else label.Free();
        Units.Add(new Placed(u, mine, centre, models));
    }

    public Placed Find(string name) => Units.First(p => p.Unit.Name == name);

    /// <summary>The radius that holds every model of the unit, for selection rings.</summary>
    public static float Footprint(Placed p) =>
        p.Models.Max(m => new Vector2(m.X - p.Centre.X, m.Z - p.Centre.Z).Length()) + Mathf.Max(p.Unit.Base, 1f) / 2 + 0.5f;

    /// <summary>Screen position of a table point, for HUD markers that point at units.</summary>
    public Vector2 ToScreen(Vector3 p) => Camera.UnprojectPosition(p);

    /// <summary>A small icon floating over a unit, left of its name: Shaken, already activated.</summary>
    public void Mark(string unit, string icon, Color colour, int slot = 0)
    {
        var l = Labels[unit];
        var s = new Sprite3D
        {
            Texture = HudKit.IconTex(icon), PixelSize = 0.0028f, Modulate = colour, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = l.Position + new Vector3(slot * 1.6f - 0.8f, 1.4f, 0),
        };
        AddChild(s);
    }
}
