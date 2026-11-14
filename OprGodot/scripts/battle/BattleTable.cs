using FDG;
using FdgRaylib.Config;
using FdgRaylib.Game;
using FdgRaylib.Models3D;
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
    private readonly Dictionary<ITerrain, List<Node>> _terrainBuilt = new();
    private bool _rulesView;

    /// <summary>
    /// The Map Maker's Rules view in battle (V): scenery flattened, every piece coloured by what it does and
    /// labelled with its rules. Rebuilds the terrain on the next frame.
    /// </summary>
    public bool RulesView
    {
        get => _rulesView;
        set
        {
            if (_rulesView == value) return;
            _rulesView = value;
            foreach (var nodes in _terrainBuilt.Values) foreach (Node n in nodes) n.QueueFree();
            _terrainBuilt.Clear();
        }
    }
    private Node3D _terrainRoot = null!, _modelRoot = null!;

    /// <summary>Enemy units whose name shows over them (hovered or pinned).</summary>
    public readonly HashSet<UnitID> ShowEnemyNames = new();
    public UnitID? Selected { get; set; }

    /// <summary>Units ringed in white (what a hovered battle-report line is about).</summary>
    public readonly HashSet<UnitID> Highlight = new();

    private sealed class ModelView
    {
        public required Node3D Root;
        public required StandardMaterial3D Body;
        public required StandardMaterial3D Rim;
        public required MeshInstance3D Ring;
        public required StandardMaterial3D RingMat;
        public required IModel Model;
        public required IUnit Unit;
        /// <summary>How tall it stands (inches): a downloaded model's own height, or the token's.</summary>
        public float Height = 1.6f;
    }

    /// <summary>Table point (inches) -> world.</summary>
    public Vector3 World(float x, float z, float y = 0) => new(x - W / 2, y, D / 2 - z);
    public Vector3 World(Position p) => World(p.x, p.z, p.y);

    /// <summary>
    /// Placeholder shot effect (Kai, 2026-11-11): a fading tracer from every firing model to the target, a volley at a
    /// time (up to three), with a small flash where it lands; melee is a red flash on each struck model. To be replaced
    /// by real projectile and swing effects (the beats carry an effect key for that).
    /// </summary>
    public void Shots(IReadOnlyList<Position> from, IReadOnlyList<Position> to, int volleys, bool melee, float life = 1.5f)
    {
        if (from.Count == 0 || to.Count == 0) return;
        var rng = new RandomNumberGenerator();
        int waves = Math.Clamp(volleys, 1, 3);
        for (int w = 0; w < waves; w++)
        {
            float delay = w * 0.16f;
            for (int i = 0; i < from.Count; i++)
            {
                Position f = from[i], t = to[(i + w) % to.Count];
                Vector3 a = World(f.x, f.z, 0.9f), b = World(t.x + rng.RandfRange(-0.3f, 0.3f), t.z + rng.RandfRange(-0.3f, 0.3f), 0.7f);
                if (melee) Flash(b, new Color(1f, 0.3f, 0.25f), 0.5f, delay, life);
                else
                {
                    Tracer(a, b, new Color(1f, 0.85f, 0.4f), delay, life);
                    Flash(b, new Color(1f, 0.6f, 0.2f), 0.3f, delay + 0.08f, life);
                }
            }
        }
    }

    private void Tracer(Vector3 a, Vector3 b, Color c, float delay, float life)
    {
        float len = a.DistanceTo(b);
        if (len < 0.1f) return;
        var mat = Mat(c, 0.5f, c, 3f);
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        var mi = Mesh(this, new BoxMesh { Size = new Vector3(0.08f, 0.08f, len) }, mat, (a + b) / 2);
        mi.LookAt(b, Vector3.Up);
        mi.Visible = false;
        Fade(mi, mat, c, delay, 0.35f * life);
    }

    private void Flash(Vector3 at, Color c, float size, float delay, float life)
    {
        var mat = Mat(c, 0.5f, c, 3f);
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        var mi = Mesh(this, new SphereMesh { Radius = size, Height = size * 2 }, mat, at);
        mi.Visible = false;
        Fade(mi, mat, c, delay, 0.3f * life);
    }

    private void Fade(MeshInstance3D mi, StandardMaterial3D mat, Color c, float delay, float time)
    {
        Tween tw = CreateTween();
        if (delay > 0) tw.TweenInterval(delay);
        tw.TweenCallback(Callable.From(() => mi.Visible = true));
        tw.TweenMethod(Callable.From<float>(v => mat.AlbedoColor = c with { A = v }), 1f, 0f, time);
        tw.TweenCallback(Callable.From(mi.QueueFree));
    }

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
        LoadUnitModels();
    }

    // ------------------------------------------------------------------ downloaded 3D models

    private UnitModelLibrary? _library;
    private readonly Dictionary<string, Lab.LoadedAsset?> _modelFiles = new();
    private readonly Dictionary<string, UnitModelSettings> _modelSettings = new();

    /// <summary>
    /// Finds the downloaded models for this game's units (UnitModels, see the Asset Lab) and loads, before the first
    /// frame, every file this game will need - each file once, shared by every model that uses it. A unit with no
    /// download keeps the token. Settings: "3D unit models" turns this off.
    /// </summary>
    private void LoadUnitModels()
    {
        if (!UserConfig.Current.View.ShowUnitModels) return;
        UnitModelMap? map = null;
        try { map = UnitModelMap.Load(); } catch (Exception) { }
        _library = UnitModelLibrary.Scan(UnitModelPaths.Root, map);
        if (_library.FileCount == 0) { _library = null; return; }
        try
        {
            foreach (IUnit unit in Session.Table.Units.Objects)
                for (int i = 0; i < unit.Models.Count; i++)
                    if (FileFor(unit, unit.Models[i]) is { } f) Asset(f);
        }
        catch (InvalidOperationException) { } // the engine thread changed the list; the rest load as they appear
    }

    /// <summary>The model file for one model: the look picked for it in the army builder, else the unit's first
    /// version (Kai, 2026-11-12: unpicked models all look the same).</summary>
    private string? FileFor(IUnit unit, IModel model)
    {
        if (_library == null) return null;
        // One rule for the table and the portraits, so a unit's picture is the version that stands here.
        return FdgRaylib.Models3D.UnitPortraits.ModelFor(_library, Session.SeatOf(unit.PlayerID)?.Faction ?? "", unit.Name, model.Look);
    }

    private Lab.LoadedAsset? Asset(string file)
    {
        if (_modelFiles.TryGetValue(file, out var a)) return a;
        a = Lab.AssetLoader.Load(file);
        if (a.Model == null) { GD.PrintErr($"Unit model not usable: {file}: {a.Error}"); a = null; }
        _modelFiles[file] = a;
        _modelSettings[file] = UnitModelSettings.Load(file) ?? new UnitModelSettings { HeightMm = 0 };
        return a;
    }

    /// <summary>Stands a downloaded model on a base already built in <paramref name="root"/>; false = none for it.</summary>
    private bool AddUnitModel(Node3D root, IModel m, IUnit unit, out float height)
    {
        height = 0;
        if (FileFor(unit, m) is not { } file || Asset(file) is not { Model: { } source }) return false;
        UnitModelSettings s = _modelSettings[file].Copy();
        if (s.BaseMm <= 0) s.BaseMm = 1; // feet on the game's base, whatever base the model was saved with
        var pivot = new Node3D { Name = "UnitModel" };
        root.AddChild(pivot);
        var body = (Node3D)source.Duplicate();
        pivot.AddChild(body);
        var (_, box) = Lab.ModelPlacement.Place(pivot, body, s);
        height = box.End.Y;
        return true;
    }

    public override void _ExitTree()
    {
        foreach (var a in _modelFiles.Values) a?.Model?.QueueFree();
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
        try { SyncOutlines(); } catch (InvalidOperationException) { }
        SyncGhosts();
    }

    // ------------------------------------------------------------------ unit outlines and weapon ranges

    /// <summary>
    /// Kai, 2026-11-11: a selected (or report-hovered) unit is ONE outline round the whole unit - not a ring per
    /// model, which made the table busy. The selected unit also shows its weapon ranges (one outline per range,
    /// named), all the time it is selected, not only while shooting.
    /// </summary>
    private string _outlineSig = "";

    /// <summary>
    /// Kai, 2026-11-11: while a unit is being moved, its ranges show only from where it is going (the move ghost
    /// draws those) - not also from where it stands.
    /// </summary>
    public bool HideSelectedRanges { get; set; }

    /// <summary>An enemy unit under the pointer: its ranged weapons' reach is drawn too (Kai, 2026-11-11).</summary>
    public UnitID? HoverUnit { get; set; }

    private void SyncOutlines()
    {
        IUnit? Find(UnitID? id) => id is { } x ? Session.Table.Units.Objects.FirstOrDefault(u => u.ID.Equals(x)) : null;
        IUnit? sel = Find(Selected), hover = Find(HoverUnit);
        if (hover != null && sel != null && hover.ID.Equals(sel.ID)) hover = null;
        var hl = Highlight.Count == 0 ? new List<IUnit>() : Session.Table.Units.Objects.Where(u => Highlight.Contains(u.ID)).ToList();
        static string Sig(IUnit u) => u.ID.ID + (u.GetIsOnBattlefield() ? "" : "off") + string.Concat(u.Models.Where(m => m.GetIsAlive())
            .Select(m => $"{m.Position.x:0.0},{m.Position.z:0.0};"));
        string sig = (HideSelectedRanges ? "moving|" : "") + (sel == null ? "-" : Sig(sel)) + "|" + (hover == null ? "-" : Sig(hover)) + "|" + string.Join("|", hl.Select(Sig));
        if (sig == _outlineSig) return;
        _outlineSig = sig;

        var lines = new List<GhostLine>();
        if (sel != null) lines.AddRange(Outline(sel, 0.35f, HudKit.Lime, 0.2f));
        foreach (IUnit u in hl.Where(u => sel == null || !u.ID.Equals(sel.ID))) lines.AddRange(Outline(u, 0.35f, Colors.White, 0.18f));
        SetGhosts("select", Array.Empty<Ghost>(), lines);

        // The selected unit's weapon ranges, and the hovered enemy's (in warm colours, so the two read apart).
        var (rl, rt) = sel != null && sel.GetIsOnBattlefield() && !HideSelectedRanges
            ? RangeShapes(sel.Models.Where(m => m.GetIsAlive()).Select(m => (m, new Vector2(m.Position.x, m.Position.z))), CoolRanges)
            : (new List<GhostLine>(), new List<GhostText>());
        SetGhosts("range", Array.Empty<Ghost>(), rl, rt);
        var (hlines, htexts) = hover != null && hover.GetIsOnBattlefield()
            ? RangeShapes(hover.Models.Where(m => m.GetIsAlive()).Select(m => (m, new Vector2(m.Position.x, m.Position.z))), WarmRanges)
            : (new List<GhostLine>(), new List<GhostText>());
        SetGhosts("hoverrange", Array.Empty<Ghost>(), hlines, htexts);
    }

    public static readonly Color[] CoolRanges = { HudKit.Teal, HudKit.Gold, new(0.8f, 0.55f, 1f), HudKit.Lime };
    public static readonly Color[] WarmRanges = { new(1f, 0.45f, 0.4f), new(1f, 0.7f, 0.35f), new(1f, 0.5f, 0.8f), new(0.95f, 0.85f, 0.5f) };

    /// <summary>
    /// Weapon ranges as outlines: one per distinct range, measured from the models that carry a weapon of that
    /// range, at the positions given (where they stand, or where a planned move would put them), named.
    /// </summary>
    public static (List<GhostLine> Lines, List<GhostText> Texts) RangeShapes(IEnumerable<(IModel Model, Vector2 At)> models, Color[] palette)
    {
        var list = models.ToList();
        var lines = new List<GhostLine>();
        var texts = new List<GhostText>();
        var ranges = list.SelectMany(x => x.Model.Weapons).Where(w => w.RangeInches > 0)
            .GroupBy(w => w.RangeInches).OrderBy(g => g.Key).ToList();
        for (int i = 0; i < ranges.Count; i++)
        {
            Color c = palette[i % palette.Length];
            float range = ranges[i].Key;
            Vector2[] hull = HullAt(list.Where(x => x.Model.Weapons.Any(w => w.RangeInches == range))
                .Select(x => (x.At, x.Model.BaseRadiusInches + range)).ToList());
            for (int k = 0; k + 1 < hull.Length; k++) lines.Add(new GhostLine(hull[k], hull[k + 1], c with { A = 0.85f }, 0.14f));
            if (hull.Length == 0) continue;
            Vector2 top = hull.OrderByDescending(p => p.Y).First();
            texts.Add(new GhostText(top.X, top.Y + 0.6f, $"{string.Join(", ", ranges[i].Select(w => w.Name).Distinct())} {range:0}\"", c));
        }
        return (lines, texts);
    }

    /// <summary>The convex outline of circles (centre, radius), closed.</summary>
    public static Vector2[] HullAt(List<(Vector2 At, float Radius)> circles)
    {
        if (circles.Count == 0) return Array.Empty<Vector2>();
        var pts = new List<Vector2>();
        foreach (var (at, r) in circles)
            for (int a = 0; a < 360; a += 8)
            {
                float rad = Mathf.DegToRad(a);
                pts.Add(at + new Vector2(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r));
            }
        return Geometry2D.ConvexHull(pts.ToArray());
    }

    // ------------------------------------------------------------------ the pointer on the table

    private MeshInstance3D? _cursor;

    /// <summary>
    /// Where the pointer is on the table (Kai, 2026-11-11: a marker so it is clear exactly what point a click
    /// means - placements had looked out of step with the mouse). Null hides it.
    /// </summary>
    public void ShowCursor(Vector2? at)
    {
        if (_cursor == null)
        {
            var mat = Mat(Colors.White with { A = 0.95f }, 0.5f, Colors.White, 1.5f);
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.NoDepthTest = true;
            _cursor = Mesh(this, new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.5f, Rings = 32, RingSegments = 6 }, mat, Vector3.Zero);
            Mesh(_cursor, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.05f }, mat, Vector3.Zero);
        }
        _cursor.Visible = at != null;
        if (at is { } p) _cursor.Position = World(p.X, p.Y, 0.12f);
    }

    /// <summary>The outline round a unit's models, <paramref name="pad"/> inches out from their bases.</summary>
    public static IEnumerable<GhostLine> Outline(IUnit unit, float pad, Color c, float width)
    {
        if (!unit.GetIsOnBattlefield()) yield break;
        var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
        Vector2[] hull = Hull(alive, pad);
        for (int k = 0; k + 1 < hull.Length; k++) yield return new GhostLine(hull[k], hull[k + 1], c, width);
    }

    /// <summary>The convex outline of circles of (base radius + extra) round each model, closed.</summary>
    private static Vector2[] Hull(List<IModel> models, float extra)
    {
        if (models.Count == 0) return Array.Empty<Vector2>();
        var pts = new List<Vector2>();
        foreach (IModel m in models)
        {
            float r = m.BaseRadiusInches + extra;
            for (int a = 0; a < 360; a += 8)
            {
                float rad = Mathf.DegToRad(a);
                pts.Add(new Vector2(m.Position.x + Mathf.Cos(rad) * r, m.Position.z + Mathf.Sin(rad) * r));
            }
        }
        return Geometry2D.ConvexHull(pts.ToArray());
    }

    /// <summary>The model under the screen point, or null.</summary>
    public IModel? ModelAt(Vector2 screen)
    {
        IModel? best = null;
        float bestD = float.MaxValue;
        foreach (ModelView v in _models.Values)
        {
            if (Camera.IsPositionBehind(v.Root.Position)) continue;
            Vector2 s = Camera.UnprojectPosition(v.Root.Position + new Vector3(0, 0.6f, 0));
            Vector2 edge = Camera.UnprojectPosition(v.Root.Position + new Vector3(Mathf.Max(0.5f, v.Model.BaseRadiusInches), 0.6f, 0));
            float r = Mathf.Max(10f, s.DistanceTo(edge) * 1.3f);
            float d = s.DistanceTo(screen);
            if (d < r && d < bestD) { bestD = d; best = v.Model; }
        }
        return best;
    }

    // ------------------------------------------------------------------ ghosts (move previews, ranges)

    /// <summary>A preview disc on the table: where a model would end up, where it stood, or a ring.</summary>
    public readonly record struct Ghost(float X, float Z, float Radius, Color Colour, bool Ring = false);

    /// <summary>A line on the table (a path, an arrow shaft, a range outline), in table inches.</summary>
    public readonly record struct GhostLine(Vector2 From, Vector2 To, Color Colour, float Width = 0.12f);

    /// <summary>A floating word on the table (a weapon's name on its range outline).</summary>
    public readonly record struct GhostText(float X, float Z, string Text, Color Colour, float Size = 0.03f);

    private sealed class Layer
    {
        public List<Ghost> Ghosts = new();
        public List<GhostLine> Lines = new();
        public List<GhostText> Texts = new();
        public bool Dirty = true;
        public Node3D? Root;
    }

    private readonly object _ghostLock = new();
    private readonly Dictionary<string, Layer> _layers = new();

    /// <summary>
    /// Replaces one preview layer (any thread) - "move" for the move tool, "range" for weapon reach, "report"
    /// for what a hovered report line is about. Layers draw independently; empty lists clear one.
    /// </summary>
    public void SetGhosts(string layer, IEnumerable<Ghost> ghosts, IEnumerable<GhostLine>? lines = null, IEnumerable<GhostText>? texts = null)
    {
        lock (_ghostLock)
        {
            if (!_layers.TryGetValue(layer, out Layer? l)) _layers[layer] = l = new Layer();
            l.Ghosts = ghosts.ToList();
            l.Lines = lines?.ToList() ?? new();
            l.Texts = texts?.ToList() ?? new();
            l.Dirty = true;
        }
    }

    public void ClearGhosts(string layer) => SetGhosts(layer, Array.Empty<Ghost>());

    private void SyncGhosts()
    {
        List<(Layer L, List<Ghost> G, List<GhostLine> Li, List<GhostText> T)> work = new();
        lock (_ghostLock)
        {
            foreach (Layer l in _layers.Values.Where(l => l.Dirty))
            {
                l.Dirty = false;
                work.Add((l, l.Ghosts, l.Lines, l.Texts));
            }
        }
        foreach (var (layer, ghosts, lines, texts) in work)
        {
            layer.Root?.QueueFree();
            layer.Root = new Node3D { Name = "Ghosts" };
            AddChild(layer.Root);
            foreach (Ghost g in ghosts)
            {
                var mat = Mat(g.Colour with { A = g.Ring ? 0.9f : g.Colour.A * 0.45f }, 0.5f, g.Colour, 1.2f);
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mat.NoDepthTest = g.Ring;
                Mesh mesh = g.Ring
                    ? new TorusMesh { InnerRadius = g.Radius - 0.08f, OuterRadius = g.Radius + 0.08f, Rings = 96, RingSegments = 6 }
                    : new CylinderMesh { TopRadius = g.Radius, BottomRadius = g.Radius, Height = 0.12f };
                Mesh(layer.Root, mesh, mat, World(g.X, g.Z, 0.08f));
            }
            foreach (GhostLine line in lines)
            {
                Vector3 a = World(line.From.X, line.From.Y, 0.1f), b = World(line.To.X, line.To.Y, 0.1f);
                float len = a.DistanceTo(b);
                if (len < 0.05f) continue;
                var mat = Mat(line.Colour, 0.5f, line.Colour, 1.2f);
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mat.NoDepthTest = true;
                var mi = Mesh(layer.Root, new BoxMesh { Size = new Vector3(line.Width, 0.04f, len) }, mat, (a + b) / 2);
                mi.LookAt(b, Vector3.Up);
            }
            foreach (GhostText t in texts)
            {
                var l = new Label3D
                {
                    Text = t.Text, Font = HudKit.Bold, FontSize = 48, PixelSize = t.Size, OutlineSize = 12, Modulate = t.Colour,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineModulate = new Color(0, 0, 0, 0.85f),
                    Position = World(t.X, t.Z, 0.6f),
                };
                layer.Root.AddChild(l);
            }
        }
    }

    // ------------------------------------------------------------------ world
    private void Environment() => TableLook.AddLighting(this);

    private static StandardMaterial3D Mat(Color c, float rough = 0.85f, Color? emit = null, float emitE = 1f) => TableLook.Mat(c, rough, emit, emitE);

    private static MeshInstance3D Mesh(Node parent, Mesh mesh, Material mat, Vector3 pos) => TableLook.Mesh(parent, mesh, mat, pos);

    private void Board() => TableLook.AddBoard(this, W, D);

    // ------------------------------------------------------------------ terrain
    private void SyncTerrain()
    {
        var present = Session.Table.Terrain.Objects.ToList();
        foreach (ITerrain t in present)
        {
            if (_terrainBuilt.ContainsKey(t)) continue;
            var nodes = new List<Node>();
            _terrainBuilt[t] = nodes;
            if (_rulesView) { BuildRulesPiece(t, nodes); continue; }
            Color c = TerrainColour(t.TerrainType);
            float h = t.HeightInches > 0.05f ? t.HeightInches : t.TerrainType.HasFlag(ETerrainType.Blocking) ? 3f : 0.25f;
            foreach (var poly in ZoneOutlines.Polygons(t.Shape))
                if (Prism(poly.Select(p => new Vector2(p.X - W / 2, D / 2 - p.Z)).ToArray(), h) is { } mesh)
                {
                    var mi = Mesh(_terrainRoot, mesh, Mat(c, 0.9f), Vector3.Zero);
                    mi.SetMeta("terrain", t.Name);
                    nodes.Add(mi);
                }
        }
        // Pieces leave the table too: a round's event terrain ends, and a Dangerous pick swaps a piece for its
        // Dangerous copy.
        if (_terrainBuilt.Count > present.Count || _terrainBuilt.Keys.Any(k => !present.Contains(k)))
            foreach (ITerrain gone in _terrainBuilt.Keys.Where(k => !present.Contains(k)).ToList())
            {
                foreach (Node n in _terrainBuilt[gone]) n.QueueFree();
                _terrainBuilt.Remove(gone);
            }
    }

    private void BuildRulesPiece(ITerrain t, List<Node> nodes)
    {
        Color rc = TableLook.RulesColour(t.TerrainType);
        float sx = 0, sz = 0; int n = 0;
        foreach (var poly in ZoneOutlines.Polygons(t.Shape))
        {
            if (Prism(poly.Select(p => new Vector2(p.X - W / 2, D / 2 - p.Z)).ToArray(), 0.12f) is { } mesh)
            {
                var mi = Mesh(_terrainRoot, mesh, TableLook.Mat(rc with { A = 0.6f }, 0.6f, rc, 0.5f), Vector3.Zero);
                mi.SetMeta("terrain", t.Name);
                nodes.Add(mi);
            }
            foreach (var p in poly) { sx += p.X; sz += p.Z; n++; }
        }
        if (n == 0) return;
        var label = new Label3D
        {
            Text = (string.IsNullOrWhiteSpace(t.Name) ? "" : t.Name + "\n") + TableLook.RulesShort(t.TerrainType),
            FontSize = 44, PixelSize = 0.018f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            Modulate = rc.Lightened(0.5f), Position = World(sx / n, sz / n, 1.2f),
        };
        _terrainRoot.AddChild(label);
        nodes.Add(label);
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
        // The top, drawn both ways round: the outline's winding depends on how the table's Z is flipped, and a
        // top facing down is culled - the pieces drew as hollow walls.
        for (int i = 0; i < tris.Length; i += 3)
        {
            foreach (int k in new[] { tris[i], tris[i + 2], tris[i + 1] })
                st.AddVertex(new Vector3(outline[k].X, height, outline[k].Y));
            foreach (int k in new[] { tris[i], tris[i + 1], tris[i + 2] })
                st.AddVertex(new Vector3(outline[k].X, height, outline[k].Y));
        }
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
                bool fresh = !_models.TryGetValue(m.ID, out ModelView? view);
                if (fresh)
                {
                    view = MakeModel(m, unit);
                    _models[m.ID] = view;
                    view!.Root.Position = World(p);
                }
                Vector3 target = World(p);
                view!.Root.Position = view.Root.Position.Lerp(target, Mathf.Min(1f, delta * 10f));
                // Which way it looks: the engine's facing (FacingRules - towards the enemy at deployment, the way it
                // moved, what it attacked). A model's front is its +Z (the Asset Lab's FRONT arrow); table z runs
                // the other way to world z. Turns smoothly rather than snapping.
                Float2 f = m.Facing;
                if (f.X * f.X + f.Y * f.Y > 0.01f)
                {
                    float want = Mathf.Atan2(f.X, -f.Y);
                    float yaw = fresh ? want : Mathf.LerpAngle(view.Root.Rotation.Y, want, Mathf.Min(1f, delta * 6f));
                    view.Root.Rotation = new Vector3(0, yaw, 0);
                }
                sum += target;
                placed++;
                top = Mathf.Max(top, view.Height + 1f); // the name floats a little over the tallest model
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
        var body = Mat(team.Darkened(0.45f).Lerp(new Color(0.55f, 0.55f, 0.55f), 0.25f), 0.55f);
        TableLook.AddBase(root, m.BaseRadiusInches, team);
        var rim = (StandardMaterial3D)((MeshInstance3D)root.GetChild(0)).MaterialOverride;
        float height;
        bool big = m.TotalWounds >= 6 || rad > 1.4f;
        if (AddUnitModel(root, m, unit, out float modelHeight)) height = modelHeight;
        else if (big)
        {
            Mesh(root, new BoxMesh { Size = new Vector3(rad * 1.3f, 1.8f, rad * 1.6f) }, body, new Vector3(0, 1.15f, 0));
            height = 2.05f;
        }
        else
        {
            float h = m.TotalWounds >= 3 ? 1.5f : 1.05f;
            Mesh(root, new CapsuleMesh { Radius = rad * 0.42f, Height = h }, body, new Vector3(0, 0.25f + h / 2, 0));
            Mesh(root, new SphereMesh { Radius = rad * 0.3f, Height = rad * 0.6f }, body, new Vector3(0, 0.3f + h, 0));
            height = 0.3f + h + rad * 0.3f;
        }
        var ringMat = Mat(HudKit.Lime, 0.5f, HudKit.Lime, 1.5f);
        ringMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        ringMat.NoDepthTest = true;
        var ring = Mesh(root, new TorusMesh { InnerRadius = rad + 0.18f, OuterRadius = rad + 0.42f, Rings = 48, RingSegments = 6 }, ringMat, new Vector3(0, 0.1f, 0));
        ring.Visible = false;
        return new ModelView { Root = root, Body = body, Rim = rim, Ring = ring, RingMat = ringMat, Model = m, Unit = unit, Height = height };
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
