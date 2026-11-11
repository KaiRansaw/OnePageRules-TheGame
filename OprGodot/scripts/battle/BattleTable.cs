using FDG;
using FdgRaylib.Game;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// The 3D table of a live game (port step 3): the board at the engine's table size, every terrain piece as a
/// block of its real outline and height, the objectives with their 3" rings coloured by who holds them, and
/// every model as a base + upright token in its player's colour, following the engine's positions each frame.
/// Table inches are world units: x across, the engine's z runs away from the first player (world -z).
/// </summary>
public partial class BattleTable : Node3D
{
    public GameSession Session { get; set; } = null!;
    public float W { get; private set; } = 72;
    public float D { get; private set; } = 48;
    public Camera3D Camera { get; private set; } = null!;

    private readonly Dictionary<ModelID, ModelView> _models = new();
    private readonly Dictionary<UnitID, Label3D> _labels = new();
    private readonly Dictionary<UnitID, Sprite3D[]> _marks = new();
    private readonly List<(IObjective Objective, MeshInstance3D Disc, MeshInstance3D Ring, StandardMaterial3D Mat)> _objectives = new();
    private readonly HashSet<ITerrain> _terrainBuilt = new();
    private Node3D _terrainRoot = null!, _modelRoot = null!;

    /// <summary>Enemy units whose name shows over them (hovered or pinned).</summary>
    public readonly HashSet<UnitID> ShowEnemyNames = new();
    public UnitID? Selected { get; set; }

    private sealed class ModelView
    {
        public required Node3D Root;
        public required StandardMaterial3D Body;
        public required StandardMaterial3D Rim;
        public required IModel Model;
        public required IUnit Unit;
    }

    /// <summary>Table point (inches) -> world.</summary>
    public Vector3 World(float x, float z, float y = 0) => new(x - W / 2, y, D / 2 - z);
    public Vector3 World(Position p) => World(p.x, p.z, p.y);

    public override void _Ready()
    {
        W = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
        D = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
        Environment();
        Board();
        _terrainRoot = new Node3D { Name = "Terrain" };
        _modelRoot = new Node3D { Name = "Models" };
        AddChild(_terrainRoot);
        AddChild(_modelRoot);
        Camera = new Camera3D { Fov = 44, Current = true, Far = 2000 };
        AddChild(Camera);
    }

    public override void _Process(double delta)
    {
        // The engine thread changes the table while this reads it; a collection that changed mid-read is
        // simply read again next frame.
        try
        {
            SyncTerrain();
            SyncObjectives();
            SyncModels((float)delta);
        }
        catch (InvalidOperationException) { }
        SyncGhosts();
    }

    // ------------------------------------------------------------------ ghosts (move previews, ranges)

    /// <summary>A preview disc on the table: where a model would end up, or a range ring.</summary>
    public readonly record struct Ghost(float X, float Z, float Radius, Color Colour, bool Ring = false);

    private readonly object _ghostLock = new();
    private List<Ghost> _ghosts = new();
    private List<(Vector2 From, Vector2 To, Color Colour)> _ghostLines = new();
    private bool _ghostsDirty;
    private Node3D? _ghostRoot;

    /// <summary>Replaces the preview layer (any thread). Empty lists clear it.</summary>
    public void SetGhosts(IEnumerable<Ghost> ghosts, IEnumerable<(Vector2 From, Vector2 To, Color Colour)>? lines = null)
    {
        lock (_ghostLock)
        {
            _ghosts = ghosts.ToList();
            _ghostLines = lines?.ToList() ?? new();
            _ghostsDirty = true;
        }
    }

    public void ClearGhosts() => SetGhosts(Array.Empty<Ghost>());

    private void SyncGhosts()
    {
        List<Ghost> ghosts;
        List<(Vector2 From, Vector2 To, Color Colour)> lines;
        lock (_ghostLock)
        {
            if (!_ghostsDirty) return;
            _ghostsDirty = false;
            ghosts = _ghosts;
            lines = _ghostLines;
        }
        _ghostRoot?.QueueFree();
        _ghostRoot = new Node3D { Name = "Ghosts" };
        AddChild(_ghostRoot);
        foreach (Ghost g in ghosts)
        {
            var mat = Mat(g.Colour with { A = g.Ring ? 0.9f : 0.45f }, 0.5f, g.Colour, 1.2f);
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.NoDepthTest = g.Ring;
            Mesh mesh = g.Ring
                ? new TorusMesh { InnerRadius = g.Radius - 0.08f, OuterRadius = g.Radius + 0.08f, Rings = 96, RingSegments = 6 }
                : new CylinderMesh { TopRadius = g.Radius, BottomRadius = g.Radius, Height = 0.12f };
            Mesh(_ghostRoot, mesh, mat, World(g.X, g.Z, 0.08f));
        }
        foreach (var (from, to, colour) in lines)
        {
            Vector3 a = World(from.X, from.Y, 0.1f), b = World(to.X, to.Y, 0.1f);
            float len = a.DistanceTo(b);
            if (len < 0.05f) continue;
            var mat = Mat(colour with { A = 0.8f }, 0.5f, colour, 1.2f);
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            var mi = Mesh(_ghostRoot, new BoxMesh { Size = new Vector3(0.12f, 0.04f, len) }, mat, (a + b) / 2);
            mi.LookAt(b, Vector3.Up);
        }
    }

    // ------------------------------------------------------------------ world
    private void Environment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.018f, 0.026f, 0.03f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.45f, 0.55f, 0.6f), AmbientLightEnergy = 0.45f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, GlowEnabled = true, GlowIntensity = 0.35f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { LightEnergy = 1.2f, LightColor = new Color(1f, 0.95f, 0.88f), ShadowEnabled = true };
        sun.RotationDegrees = new Vector3(-58, 32, 0);
        AddChild(sun);
        var rim = new DirectionalLight3D { LightEnergy = 0.3f, LightColor = new Color(0.45f, 0.75f, 0.85f) };
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

    private static MeshInstance3D Mesh(Node parent, Mesh mesh, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        parent.AddChild(mi);
        return mi;
    }

    private void Board()
    {
        Mesh(this, new PlaneMesh { Size = new Vector2(800, 800) }, Mat(new Color(0.03f, 0.04f, 0.045f)), new Vector3(0, -3, 0));
        Mesh(this, new BoxMesh { Size = new Vector3(W + 2, 3, D + 2) }, Mat(new Color(0.09f, 0.075f, 0.06f), 0.6f), new Vector3(0, -1.55f, 0));

        const int ppi = 12;
        int iw = (int)Mathf.Ceil(W) * ppi, ih = (int)Mathf.Ceil(D) * ppi;
        var img = Image.CreateEmpty(iw, ih, false, Image.Format.Rgba8);
        var felt = new Color(0.115f, 0.14f, 0.125f);
        img.Fill(felt);
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < iw * ih / 30; i++)
            img.SetPixel(rng.RandiRange(0, iw - 1), rng.RandiRange(0, ih - 1), felt.Darkened(rng.RandfRange(0.02f, 0.12f)));
        for (int x = 0; x <= (int)W; x++)
        {
            float a = x % 12 == 0 ? 0.2f : x % 6 == 0 ? 0.1f : 0.035f;
            int px = Mathf.Min(x * ppi, iw - 1);
            for (int y = 0; y < ih; y++) img.SetPixel(px, y, img.GetPixel(px, y).Lerp(Colors.White, a));
        }
        for (int z = 0; z <= (int)D; z++)
        {
            float a = z % 12 == 0 ? 0.2f : z % 6 == 0 ? 0.1f : 0.035f;
            int py = Mathf.Min(z * ppi, ih - 1);
            for (int x = 0; x < iw; x++) img.SetPixel(x, py, img.GetPixel(x, py).Lerp(Colors.White, a));
        }
        Mesh(this, new PlaneMesh { Size = new Vector2(W, D) }, new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(img), Roughness = 0.95f }, new Vector3(0, 0.01f, 0));
    }

    // ------------------------------------------------------------------ terrain
    private void SyncTerrain()
    {
        foreach (ITerrain t in Session.Table.Terrain.Objects)
        {
            if (!_terrainBuilt.Add(t)) continue;
            Color c = TerrainColour(t.TerrainType);
            float h = t.HeightInches > 0.05f ? t.HeightInches : t.TerrainType.HasFlag(ETerrainType.Blocking) ? 3f : 0.25f;
            foreach (var poly in ZoneOutlines.Polygons(t.Shape))
                if (Prism(poly.Select(p => new Vector2(p.X - W / 2, D / 2 - p.Z)).ToArray(), h) is { } mesh)
                {
                    var mi = Mesh(_terrainRoot, mesh, Mat(c, 0.9f), Vector3.Zero);
                    mi.SetMeta("terrain", t.Name);
                }
        }
    }

    /// <summary>An outline extruded to <paramref name="height"/>: the terrain piece as a block.</summary>
    private static ArrayMesh? Prism(Vector2[] outline, float height)
    {
        if (outline.Length < 3) return null;
        int[] tris = Geometry2D.TriangulatePolygon(outline);
        if (tris.Length == 0) return null;
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        for (int i = 0; i < tris.Length; i += 3)
            foreach (int k in new[] { tris[i], tris[i + 2], tris[i + 1] })
                st.AddVertex(new Vector3(outline[k].X, height, outline[k].Y));
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
            Vector3 a0 = new(a.X, 0, a.Y), b0 = new(b.X, 0, b.Y), a1 = new(a.X, height, a.Y), b1 = new(b.X, height, b.Y);
            Vector3 n = (b0 - a0).Cross(Vector3.Up).Normalized();
            st.SetNormal(n);
            foreach (Vector3 v in new[] { a0, b0, b1, a0, b1, a1 }) st.AddVertex(v);
            st.SetNormal(-n);
            foreach (Vector3 v in new[] { a0, b1, b0, a0, a1, b1 }) st.AddVertex(v);
        }
        return st.Commit();
    }

    public static Color TerrainColour(ETerrainType type)
    {
        if (type.HasFlag(ETerrainType.Dangerous)) return new Color(0.55f, 0.25f, 0.18f);
        if (type.HasFlag(ETerrainType.Blocking) || type.HasFlag(ETerrainType.Impassible)) return new Color(0.40f, 0.40f, 0.42f);
        if (type.HasFlag(ETerrainType.Cover) && type.HasFlag(ETerrainType.Difficult)) return new Color(0.13f, 0.27f, 0.15f);
        if (type.HasFlag(ETerrainType.Cover)) return new Color(0.45f, 0.36f, 0.25f);
        if (type.HasFlag(ETerrainType.Difficult)) return new Color(0.33f, 0.30f, 0.20f);
        return new Color(0.42f, 0.42f, 0.42f);
    }

    // ------------------------------------------------------------------ objectives
    private void SyncObjectives()
    {
        foreach (IObjective o in Session.Table.Objectives.Objects)
        {
            if (_objectives.Any(x => ReferenceEquals(x.Objective, o))) continue;
            var mat = Mat(Colors.White, 0.4f, Colors.White, 1.4f);
            var disc = Mesh(this, new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.2f }, mat, World(o.Position.x, o.Position.z, 0.1f));
            var ring = Mesh(this, new TorusMesh { InnerRadius = 2.93f, OuterRadius = 3.07f, Rings = 96, RingSegments = 6 }, mat, World(o.Position.x, o.Position.z, 0.06f));
            ring.Scale = new Vector3(1, 0.25f, 1);
            ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _objectives.Add((o, disc, ring, mat));
        }
        foreach (var (o, _, _, mat) in _objectives)
        {
            Color c = o.OwnerID is { } owner ? SeatColour(owner) : new Color(0.88f, 0.9f, 0.92f);
            mat.AlbedoColor = c;
            mat.Emission = c;
        }
    }

    // ------------------------------------------------------------------ models
    public Color SeatColour(PlayerID id)
    {
        if (Session.SeatOf(id) is not { } seat) return Colors.White;
        Rgb c = PlayerColorCodes.ColorFor(seat.ColourCode);
        return Color.Color8(c.R, c.G, c.B);
    }

    private void SyncModels(float delta)
    {
        var live = new HashSet<ModelID>();
        foreach (IUnit unit in Session.Table.Units.Objects)
        {
            bool mine = Session.IsMine(unit.PlayerID);
            Vector3 sum = Vector3.Zero;
            int placed = 0;
            float top = 0;
            foreach (IModel m in unit.Models)
            {
                Position p = m.Position;
                bool onTable = !(p.x == 0 && p.z == 0) && m.RemainingWounds > 0;
                if (!onTable) continue;
                live.Add(m.ID);
                if (!_models.TryGetValue(m.ID, out ModelView? view))
                {
                    view = MakeModel(m, unit);
                    _models[m.ID] = view;
                    view.Root.Position = World(p);
                }
                Vector3 target = World(p);
                view.Root.Position = view.Root.Position.Lerp(target, Mathf.Min(1f, delta * 10f));
                sum += target;
                placed++;
                top = Mathf.Max(top, m.BaseRadiusInches > 1.4f ? 3.6f : 2.6f);
            }

            bool shaken = unit.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken);
            bool done = UnitActivation.HasActivated(Session.Table.Progress, unit);
            if (placed > 0)
            {
                Label3D label = Label(unit, mine);
                label.Position = sum / placed + new Vector3(0, top, 0);
                bool selected = Selected is { } s && s.Equals(unit.ID);
                label.Visible = mine || ShowEnemyNames.Contains(unit.ID) || selected;
                label.Modulate = (selected ? HudKit.Lime : SeatColour(unit.PlayerID).Lightened(0.35f)) with { A = done ? 0.55f : 1f };
                Sprite3D[] marks = Marks(unit);
                marks[0].Visible = done;
                marks[1].Visible = shaken;
                marks[0].Position = label.Position + new Vector3(-0.8f, 1.3f, 0);
                marks[1].Position = label.Position + new Vector3(0.8f, 1.3f, 0);
            }
            else if (_labels.TryGetValue(unit.ID, out Label3D? gone))
            {
                gone.Visible = false;
                foreach (Sprite3D mk in Marks(unit)) mk.Visible = false;
            }
        }
        foreach (ModelID dead in _models.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _models[dead].Root.QueueFree();
            _models.Remove(dead);
        }
    }

    private ModelView MakeModel(IModel m, IUnit unit)
    {
        Color team = SeatColour(unit.PlayerID);
        var root = new Node3D();
        _modelRoot.AddChild(root);
        float rad = Mathf.Max(0.4f, m.BaseRadiusInches);
        var rim = Mat(team, 0.4f, team, 1.3f);
        var body = Mat(team.Darkened(0.45f).Lerp(new Color(0.55f, 0.55f, 0.55f), 0.25f), 0.55f);
        Mesh(root, new CylinderMesh { TopRadius = rad + 0.05f, BottomRadius = rad + 0.07f, Height = 0.08f }, rim, new Vector3(0, 0.05f, 0));
        Mesh(root, new CylinderMesh { TopRadius = rad * 0.94f, BottomRadius = rad, Height = 0.2f }, Mat(new Color(0.08f, 0.09f, 0.1f), 0.5f), new Vector3(0, 0.15f, 0));
        bool big = m.TotalWounds >= 6 || rad > 1.4f;
        if (big)
            Mesh(root, new BoxMesh { Size = new Vector3(rad * 1.3f, 1.8f, rad * 1.6f) }, body, new Vector3(0, 1.15f, 0));
        else
        {
            float h = m.TotalWounds >= 3 ? 1.5f : 1.05f;
            Mesh(root, new CapsuleMesh { Radius = rad * 0.42f, Height = h }, body, new Vector3(0, 0.25f + h / 2, 0));
            Mesh(root, new SphereMesh { Radius = rad * 0.3f, Height = rad * 0.6f }, body, new Vector3(0, 0.3f + h, 0));
        }
        return new ModelView { Root = root, Body = body, Rim = rim, Model = m, Unit = unit };
    }

    private Label3D Label(IUnit unit, bool mine)
    {
        if (_labels.TryGetValue(unit.ID, out Label3D? l)) return l;
        l = new Label3D
        {
            Text = unit.Name.ToUpperInvariant(), Font = HudKit.Bold, FontSize = 44, PixelSize = 0.016f, OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineModulate = new Color(0, 0, 0, 0.85f),
        };
        AddChild(l);
        _labels[unit.ID] = l;
        return l;
    }

    private Sprite3D[] Marks(IUnit unit)
    {
        if (_marks.TryGetValue(unit.ID, out Sprite3D[]? m)) return m;
        Sprite3D Make(string icon, Color c) => new()
        {
            Texture = HudKit.IconTex(icon), PixelSize = 0.0028f, Modulate = c, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false,
        };
        m = new[] { Make("checked-shield", HudKit.Dim), Make("broken-shield", Palette.WoundsRed) };
        foreach (Sprite3D s in m) AddChild(s);
        _marks[unit.ID] = m;
        return m;
    }

    // ------------------------------------------------------------------ picking

    /// <summary>The unit whose model is under the screen point, if any.</summary>
    public IUnit? UnitAt(Vector2 screen)
    {
        IUnit? best = null;
        float bestD = float.MaxValue;
        foreach (ModelView v in _models.Values)
        {
            if (Camera.IsPositionBehind(v.Root.Position)) continue;
            Vector2 s = Camera.UnprojectPosition(v.Root.Position + new Vector3(0, 0.6f, 0));
            Vector2 edge = Camera.UnprojectPosition(v.Root.Position + new Vector3(Mathf.Max(0.5f, v.Model.BaseRadiusInches), 0.6f, 0));
            float r = Mathf.Max(10f, s.DistanceTo(edge) * 1.3f);
            float d = s.DistanceTo(screen);
            if (d < r && d < bestD) { bestD = d; best = v.Unit; }
        }
        return best;
    }

    /// <summary>The table point (inches) under the screen point, or null off the table.</summary>
    public Vector2? TableAt(Vector2 screen)
    {
        Vector3 from = Camera.ProjectRayOrigin(screen), dir = Camera.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 1e-4f) return null;
        float t = -from.Y / dir.Y;
        if (t < 0) return null;
        Vector3 p = from + dir * t;
        float x = p.X + W / 2, z = D / 2 - p.Z;
        if (x < 0 || z < 0 || x > W || z > D) return null;
        return new Vector2(x, z);
    }

    /// <summary>The terrain piece under the screen point (on the table), if any.</summary>
    public ITerrain? TerrainAt(Vector2 screen) =>
        TableAt(screen) is { } p ? Session.Table.Terrain.Objects.FirstOrDefault(t => t.Shape.IsPointWithinZone(new FDG.Float2(p.X, p.Y))) : null;

    /// <summary>Middle of a unit on the table (world), for the camera to look at.</summary>
    public Vector3? UnitCentre(IUnit unit)
    {
        var placed = unit.Models.Where(m => !(m.Position.x == 0 && m.Position.z == 0) && m.RemainingWounds > 0).ToList();
        if (placed.Count == 0) return null;
        Vector3 sum = Vector3.Zero;
        foreach (IModel m in placed) sum += World(m.Position);
        return sum / placed.Count;
    }
}
