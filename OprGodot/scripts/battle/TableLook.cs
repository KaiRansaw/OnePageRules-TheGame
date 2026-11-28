using FDG;
using System;
using System.Collections.Generic;
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
            ETerrainType.Dangerous => "Dang", ETerrainType.Unstable => "Unst", ETerrainType.Climbable => "Climb", ETerrainType.SolidBuilding => "Bldg", _ => f.ToString(),
        }));


    /// <summary>
    /// The room the table stands in (Kai, 2026-11-14), from the player's Settings - null for the plain dark background.
    /// <paramref name="system"/> is the game being played, for the Auto choice.
    /// </summary>
    public static FdgRaylib.Terrain.TableRooms.Room? RoomFor(EGameSystem? system) =>
        FdgRaylib.Terrain.TableRooms.Pick(FdgRaylib.Config.UserConfig.Current.View.Room, system);

    private static readonly Dictionary<string, Texture2D?> Panoramas = new();

    private static Texture2D? Panorama(FdgRaylib.Terrain.TableRooms.Room room)
    {
        string file = FdgRaylib.Terrain.TableRooms.FileFor(room);
        if (Panoramas.TryGetValue(file, out var have)) return have;
        Image? img = File.Exists(file) ? Image.LoadFromFile(file) : null;
        return Panoramas[file] = img == null || img.IsEmpty() ? null : ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// The background, ambient light, sun and rim light the battle is played under. With a <paramref name="room"/>, its
    /// 360-degree photo is drawn all around the table instead of the dark background; the table's own lighting stays the
    /// same, so the rules colours read the same in every room.
    /// </summary>
    /// <summary>Takes away what <see cref="AddLighting"/> put under <paramref name="parent"/> (Settings changed - Kai,
    /// 2026-11-18: settings take effect the moment you leave the Settings screen).</summary>
    public static void RemoveLighting(Node parent)
    {
        foreach (string n in new[] { "Environment", "Sun", "Rim" })
            if (parent.GetNodeOrNull(n) is { } node) { parent.RemoveChild(node); node.QueueFree(); }
    }

    public static void AddLighting(Node parent, FdgRaylib.Terrain.TableRooms.Room? room = null, string? horizon = null, float tableW = 72, float tableD = 48, bool battlefield = false, string? sky = null)
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.018f, 0.026f, 0.03f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.45f, 0.55f, 0.6f), AmbientLightEnergy = 0.45f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, GlowEnabled = true, GlowIntensity = 0.35f,
        };
        if (room != null && Panorama(room) is { } pano)
        {
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            env.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = pano } };
            env.BackgroundEnergyMultiplier = Mathf.Clamp(FdgRaylib.Config.UserConfig.Current.View.RoomBrightness, 0.2f, 1.5f);
        }
        if (room == null && battlefield)
            Battle.Horizon.SkyAndHaze(env, FdgRaylib.Terrain.MapAtmosphere.HorizonFor(horizon), tableW, tableD, sky);
        parent.AddChild(new WorldEnvironment { Environment = env, Name = "Environment" });
        var sun = new DirectionalLight3D { LightEnergy = 1.2f, LightColor = new Color(1f, 0.95f, 0.88f), ShadowEnabled = true, Name = "Sun" };
        sun.RotationDegrees = new Vector3(-58, 32, 0);
        parent.AddChild(sun);
        var rim = new DirectionalLight3D { LightEnergy = 0.3f, LightColor = new Color(0.45f, 0.75f, 0.85f), Name = "Rim" };
        rim.RotationDegrees = new Vector3(-25, 200, 0);
        parent.AddChild(rim);
    }

    /// <summary>
    /// The floor under the table, the wooden table edge, and the felt with its inch grid (every 6" and 12" stronger).
    /// <paramref name="mat"/> is the map's battle mat (<c>wh-mat:&lt;id&gt;</c>): its picture replaces the felt, with the
    /// same grid drawn over it; a computer without the picture gets the felt.
    /// </summary>
    public static void AddBoard(Node parent, float w, float d, string? mat = null, bool floor = true, string? horizon = null, bool battlefield = false, string? ground = null, string? scenery = null)
    {
        // The battlefield (Settings -> Around the table; battles and the Map Maker only): no table edge or floor - the land
        // runs on to the horizon.
        if (battlefield && parent is Node3D p3) Battle.Horizon.AddLand(p3, w, d, FdgRaylib.Terrain.MapAtmosphere.HorizonFor(horizon), ground, scenery);
        // The dark floor under the table; left out in a room, whose own floor shows instead.
        if (floor && !battlefield) Mesh(parent, new PlaneMesh { Size = new Vector2(800, 800) }, Mat(new Color(0.03f, 0.04f, 0.045f)), new Vector3(0, -3, 0));
        if (!battlefield) Mesh(parent, new BoxMesh { Size = new Vector3(w + 2, 3, d + 2) }, Mat(new Color(0.09f, 0.075f, 0.06f), 0.6f), new Vector3(0, -1.55f, 0));
        // The map's mat: a downloaded picture, the plain felt, or the horizon's own ground (no mat picked, on the battlefield,
        // or a picture this computer doesn't have) so the play area and the land are one surface.
        ImageTexture? picture = MatTexture(mat, w, d);
        bool useGround = mat == FdgRaylib.Terrain.MapAtmosphere.GroundMat
            || (battlefield && picture == null && mat != FdgRaylib.Terrain.MapAtmosphere.FeltMat);
        if (useGround)
        {
            Battle.Horizon.AddTableGround(parent, w, d, FdgRaylib.Terrain.MapAtmosphere.HorizonFor(horizon), ShowGrid ? GridOnly(w, d) : null, ground);
            return;
        }
        Texture2D cloth = picture ?? Felt(w, d);
        Mesh(parent, new PlaneMesh { Size = new Vector2(w, d) }, new StandardMaterial3D { AlbedoTexture = cloth, Roughness = 0.95f }, new Vector3(0, 0.01f, 0));
    }

    private static readonly Dictionary<string, ImageTexture> MatCache = new();

    /// <summary>A battle mat's picture stretched over a w x d table with the inch grid on it, or null when it isn't on this computer.</summary>
    public static ImageTexture? MatTexture(string? mat, float w, float d)
    {
        if (FdgRaylib.Terrain.WarhammerTerrain.MatFile(mat) is not { } file) return null;
        string key = $"{file}|{w}|{d}|{ShowGrid}";
        if (MatCache.TryGetValue(key, out var have)) return have;
        Image? img = Image.LoadFromFile(file);
        if (img == null || img.IsEmpty()) return null;
        // A mat drawn the other way round from the table is turned to fit, as Tabletop Simulator lays it.
        if ((img.GetWidth() > img.GetHeight()) != (w >= d)) img.Rotate90(ClockDirection.Clockwise);
        int ppi = Mathf.Clamp(2048 / (int)Mathf.Max(1, Mathf.Ceil(Mathf.Max(w, d))), 12, 32);
        int iw = (int)Mathf.Ceil(w) * ppi, ih = (int)Mathf.Ceil(d) * ppi;
        img.Convert(Image.Format.Rgba8);
        img.Resize(iw, ih, Image.Interpolation.Bilinear);
        Grid(img, w, d, ppi, 0.6f);
        if (MatCache.Count > 8) MatCache.Clear();
        return MatCache[key] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>The inch grid alone on a see-through picture, to lay over a surface that has no grid of its own.</summary>
    public static ImageTexture GridOnly(float w, float d)
    {
        const int ppi = 8;
        var img = Image.CreateEmpty((int)Mathf.Ceil(w) * ppi, (int)Mathf.Ceil(d) * ppi, false, Image.Format.Rgba8);
        img.Fill(new Color(1, 1, 1, 0));
        Grid(img, w, d, ppi, 1.6f);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Settings -> Display -> "Table grid" (Kai, 2026-11-27: "the grid is always on at all times in every mode").
    /// Every table surface - felt, battle mat, the horizon's ground - draws its grid through <see cref="Grid"/>, so this
    /// one switch covers battles, the Map Maker table and every lab / preview table.</summary>
    public static bool ShowGrid => FdgRaylib.Config.UserConfig.Current.View.ShowGrid;

    /// <summary>The inch grid over a table picture (every 6" and 12" stronger); <paramref name="strength"/> scales it.
    /// Draws nothing while Settings' "Table grid" is off.</summary>
    private static void Grid(Image img, float w, float d, int ppi, float strength)
    {
        if (!ShowGrid) return;
        int iw = img.GetWidth(), ih = img.GetHeight();
        for (int x = 0; x <= (int)w; x++)
        {
            float a = (x % 12 == 0 ? 0.2f : x % 6 == 0 ? 0.1f : 0.035f) * strength;
            int px = Mathf.Min(x * ppi, iw - 1);
            for (int y = 0; y < ih; y++) img.SetPixel(px, y, img.GetPixel(px, y).Lerp(Colors.White, a));
        }
        for (int z = 0; z <= (int)d; z++)
        {
            float a = (z % 12 == 0 ? 0.2f : z % 6 == 0 ? 0.1f : 0.035f) * strength;
            int py = Mathf.Min(z * ppi, ih - 1);
            for (int x = 0; x < iw; x++) img.SetPixel(x, py, img.GetPixel(x, py).Lerp(Colors.White, a));
        }
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
        Grid(img, w, d, ppi, 1f);
        return ImageTexture.CreateFromImage(img);
    }

    public static StandardMaterial3D Mat(Color c, float rough = 0.85f, Color? emit = null, float emitE = 1f)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough };
        if (emit is { } e) { m.EmissionEnabled = true; m.Emission = e; m.EmissionEnergyMultiplier = emitE; }
        if (c.A < 1) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return m;
    }

    // Performance sweep (2026-11-15): materials and meshes the table makes once per MODEL were identical for every
    // model of a side (the base rim, the dark base top, the token body, the selection ring). Each unique material
    // is its own render state, so a 6000-point table drew ~2700 calls. These caches hand out ONE material / mesh per
    // look. Only for things nobody changes after creation - a material that is recoloured later (objective
    // markers) keeps using Mat().
    private static readonly Dictionary<(Color, float, Color?, float, bool, bool), StandardMaterial3D> SharedMats = new();
    private static readonly Dictionary<string, Mesh> SharedMeshes = new();

    /// <summary>One shared, never-changed material per look (see <see cref="SharedMats"/>).</summary>
    public static StandardMaterial3D SharedMat(Color c, float rough = 0.85f, Color? emit = null, float emitE = 1f,
        bool unshaded = false, bool noDepthTest = false)
    {
        var key = (c, rough, emit, emitE, unshaded, noDepthTest);
        if (SharedMats.TryGetValue(key, out StandardMaterial3D? m) && GodotObject.IsInstanceValid(m)) return m;
        m = Mat(c, rough, emit, emitE);
        if (unshaded) m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        m.NoDepthTest = noDepthTest;
        SharedMats[key] = m;
        return m;
    }

    /// <summary>One shared mesh per shape and size (<paramref name="key"/> names both).</summary>
    public static Mesh SharedMesh(string key, Func<Mesh> make)
    {
        if (SharedMeshes.TryGetValue(key, out Mesh? mesh) && GodotObject.IsInstanceValid(mesh)) return mesh;
        return SharedMeshes[key] = make();
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
        Mesh(parent, SharedMesh($"baserim:{rad}", () => new CylinderMesh { TopRadius = rad + 0.05f, BottomRadius = rad + 0.07f, Height = 0.08f }),
            SharedMat(team, 0.4f, team, 1.3f), new Vector3(0, 0.05f, 0));
        Mesh(parent, SharedMesh($"basetop:{rad}", () => new CylinderMesh { TopRadius = rad * 0.94f, BottomRadius = rad, Height = 0.2f }),
            SharedMat(new Color(0.08f, 0.09f, 0.1f), 0.5f), new Vector3(0, 0.15f, 0));
    }

    /// <summary>A rectangular base (<paramref name="w"/> across, <paramref name="d"/> front to back - along the model's
    /// facing), drawn like <see cref="AddBase"/>: a rim in the seat colour, a dark top.</summary>
    public static void AddRectBase(Node parent, float w, float d, Color team)
    {
        Mesh(parent, SharedMesh($"rectrim:{w}x{d}", () => new BoxMesh { Size = new Vector3(w + 0.12f, 0.08f, d + 0.12f) }),
            SharedMat(team, 0.4f, team, 1.3f), new Vector3(0, 0.05f, 0));
        Mesh(parent, SharedMesh($"recttop:{w}x{d}", () => new BoxMesh { Size = new Vector3(w * 0.97f, 0.2f, d * 0.97f) }),
            SharedMat(new Color(0.08f, 0.09f, 0.1f), 0.5f), new Vector3(0, 0.15f, 0));
    }

    /// <summary>Height of the top of <see cref="AddBase"/> - where a model's feet go.</summary>
    public const float BaseTop = 0.25f;
}
