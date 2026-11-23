using FdgRaylib.Models3D;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Lab;

namespace OprGodot.Battle;

/// <summary>
/// Kai, 2026-11-19: "if a wall is L shaped, JUST the L shaped building should be impassable, but it makes the ENTIRE
/// square of the terrain's footprint impassable". The parts of a Warhammer terrain model that actually stand up off
/// the ground, seen from above, as rectangles across the model's own box - the provider behind
/// <see cref="TerrainSolids"/>, so a map saves them as the piece's rules shape.
/// <para>
/// How: every triangle of the model (its scenic base already trimmed off, <see cref="TerrainBaseTrim"/>) is laid on a
/// grid seen from above, keeping only what rises over <see cref="ClearInches"/> above the ground the model is drawn on
/// (its Sink taken off) - a ruin's floor and the rubble on it stay open ground, its walls are solid. Gaps of one cell
/// (windows seen edge-on, a seam between wall pieces) are closed, and the cells are merged into rectangles.
/// </para>
/// </summary>
public static class TerrainSolidShape
{
    /// <summary>Cells along the model's longer side.</summary>
    private const int Cells = 64;
    /// <summary>What stands less than this far off the ground (inches) is walked over, not solid.</summary>
    private const float ClearInches = 0.5f;

    private static readonly Dictionary<string, (string Sig, IReadOnlyList<(float U0, float V0, float U1, float V1, float H)>? Parts)> Cache = new();

    /// <summary>Hooks the shape into map saving (App start-up).</summary>
    public static void Register()
    {
        TerrainSolids.Provider = For;
        TerrainSolids.PieceProvider = ForPiece;
    }

    /// <summary>Forget a model's shape (its file or base setting changed).</summary>
    public static void Forget(string file)
    {
        foreach (string key in Cache.Keys.Where(k => k.StartsWith(file + "|", StringComparison.Ordinal)).ToList()) Cache.Remove(key);
    }

    /// <summary>The solid parts of the model a look names, as fractions of its box; null when this computer can't tell.</summary>
    public static IReadOnlyList<(float U0, float V0, float U1, float V1, float H)>? For(string look)
    {
        if (WarhammerModels.Library?.ForLook(look) is not { Measured: true } entry) return null;
        string key = entry.File + "|" + entry.Id;
        string sig = $"{entry.Match.W:0.###}|{entry.Match.D:0.###}|{entry.Match.Sink:0.###}|{entry.Match.CutBase}";
        if (Cache.TryGetValue(key, out var have) && have.Sig == sig) return have.Parts;
        IReadOnlyList<(float, float, float, float, float)>? parts = null;
        try
        {
            if (WarhammerModels.Template(entry.File) is { } template)
                parts = Compute(template, WarhammerModels.UpOf(entry.File), entry.Match.W, entry.Match.D, entry.Match.Sink);
        }
        catch (Exception e) { GD.Print($"TerrainSolidShape: {entry.File}: {e.Message}"); }
        Cache[key] = (sig, parts);
        return parts;
    }

    /// <summary>The shape itself. <paramref name="widthInches"/> / <paramref name="depthInches"/>: the model's measured
    /// size, for its scale; <paramref name="sinkInches"/>: how far it is sunk into the ground.</summary>
    public static IReadOnlyList<(float U0, float V0, float U1, float V1, float H)>? Compute(Node3D model, EUpAxis up,
        float widthInches, float depthInches, float sinkInches)
    {
        Basis upBasis = ModelPlacement.UpBasis(up);
        var meshes = new List<(ArrayMesh Mesh, Transform3D ToUp)>();
        foreach (MeshInstance3D mi in AssetLoader.All<MeshInstance3D>(model))
            if (mi.Mesh is ArrayMesh am && mi.Visible) meshes.Add((am, new Transform3D(upBasis, Vector3.Zero) * TerrainBaseTrim.RelativeTo(model, mi)));
        if (meshes.Count == 0) return null;

        // The box the model is stretched by (WarhammerModels.Dress measures the same way).
        Aabb? boxOrNull = null;
        foreach (var (mesh, toUp) in meshes)
        {
            Aabb b = toUp * mesh.GetAabb();
            boxOrNull = boxOrNull is { } x ? x.Merge(b) : b;
        }
        if (boxOrNull is not { } box || box.Size.X < 1e-4f || box.Size.Z < 1e-4f) return null;

        float sx = widthInches / box.Size.X, sz = depthInches / box.Size.Z, sy = Mathf.Sqrt(sx * sz);
        float clearY = box.Position.Y + (sinkInches + ClearInches) / sy;
        float cell = Mathf.Max(box.Size.X, box.Size.Z) / Cells;
        int nx = Math.Max(1, Mathf.CeilToInt(box.Size.X / cell)), nz = Math.Max(1, Mathf.CeilToInt(box.Size.Z / cell));
        // Heights in inches over the ground the model stands on (its sink taken off).
        float groundY = box.Position.Y + sinkInches / sy;
        var grid = Raster(meshes, box, cell, clearY, nx, nz, y => (y - groundY) * sy);
        grid = Erode(Dilate(grid));
        return Rectangles(grid, nx, nz, cell, box);
    }


    /// <summary>A top-down grid over <paramref name="box"/>: in each cell, how tall (inches, <paramref name="inches"/>
    /// of a model height) the highest thing standing there above <paramref name="clearY"/> is; 0 = open ground.</summary>
    private static float[,] Raster(List<(ArrayMesh Mesh, Transform3D ToUp)> meshes, Aabb box, float cell, float clearY, int nx, int nz,
        Func<float, float> inches)
    {
        var grid = new float[nx, nz];

        void Mark(float x, float z, float y)
        {
            int i = Math.Clamp((int)((x - box.Position.X) / cell), 0, nx - 1), j = Math.Clamp((int)((z - box.Position.Z) / cell), 0, nz - 1);
            grid[i, j] = Mathf.Max(grid[i, j], Mathf.Max(0.01f, inches(y)));
        }

        foreach (var (mesh, toUp) in meshes)
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if (mesh.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
                var arrays = mesh.SurfaceGetArrays(s);
                Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] idx = TerrainBaseTrim.Indices(arrays, v.Length);
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) w[i] = toUp * v[i];
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    Vector3 a = w[idx[t]], b = w[idx[t + 1]], c = w[idx[t + 2]];
                    if (a.Y <= clearY && b.Y <= clearY && c.Y <= clearY) continue;
                    MarkTriangle(a, b, c);
                }
            }

        // The part of a triangle above the clear height, seen from above: its high corners, the high stretches of its
        // edges (a wall seen edge-on is only its edges) and the cell middles inside it where it is high.
        void MarkTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            foreach (Vector3 p in new[] { a, b, c }) if (p.Y > clearY) Mark(p.X, p.Z, p.Y);
            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
            {
                float len = new Vector2(q.X - p.X, q.Z - p.Z).Length();
                int steps = Math.Max(1, Mathf.CeilToInt(len / (cell * 0.5f)));
                for (int k = 0; k <= steps; k++)
                {
                    Vector3 r = p.Lerp(q, k / (float)steps);
                    if (r.Y > clearY) Mark(r.X, r.Z, r.Y);
                }
            }
            float minX = Mathf.Min(a.X, Mathf.Min(b.X, c.X)), maxX = Mathf.Max(a.X, Mathf.Max(b.X, c.X));
            float minZ = Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)), maxZ = Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z));
            int i0 = Math.Clamp((int)((minX - box.Position.X) / cell), 0, nx - 1), i1 = Math.Clamp((int)((maxX - box.Position.X) / cell), 0, nx - 1);
            int j0 = Math.Clamp((int)((minZ - box.Position.Z) / cell), 0, nz - 1), j1 = Math.Clamp((int)((maxZ - box.Position.Z) / cell), 0, nz - 1);
            float det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (Mathf.Abs(det) < 1e-12f) return;
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    float x = box.Position.X + (i + 0.5f) * cell, z = box.Position.Z + (j + 0.5f) * cell;
                    float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
                    float l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
                    float l3 = 1 - l1 - l2;
                    if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                    float y = l1 * a.Y + l2 * b.Y + l3 * c.Y;
                    if (y > clearY) grid[i, j] = Mathf.Max(grid[i, j], Mathf.Max(0.01f, inches(y)));
                }
        }

        return grid;
    }

    private static readonly Dictionary<string, IReadOnlyList<(float, float, float, float, float)>?> PieceCache = new();

    /// <summary>
    /// The solid parts of a map piece set to one of the game's hand-built pieces, as table rectangles: the piece is
    /// dressed onto the footprint exactly as the table draws it, everything over <see cref="ClearInches"/> high is solid,
    /// and only what lies inside the footprint counts (the rules never reach past it).
    /// </summary>
    public static IReadOnlyList<(float X0, float Z0, float X1, float Z1, float H)>? ForPiece(MapPiece p)
    {
        if (TerrainPieces.ById(p.Look) is not { } piece) return null;
        string key = $"{p.Look}|{p.X:0.###}|{p.Z:0.###}|{p.W:0.###}|{p.D:0.###}|{p.Angle:0.###}|{p.Round}|{Newtonsoft.Json.JsonConvert.SerializeObject(piece)}";
        if (PieceCache.TryGetValue(key, out var have)) return have;
        IReadOnlyList<(float, float, float, float, float)>? result = null;
        var root = new Node3D();
        try
        {
            if (FdgRaylib.Rendering.ZoneOutlines.Polygons(p.ToZone()).FirstOrDefault() is { Length: >= 3 } outline)
            {
                // Table x / -z: the same handedness the table draws in (it only shifts by half the table).
                Vector2[] poly = outline.Select(q => new Vector2(q.X, -q.Z)).ToArray();
                if (PieceModels.Dress(root, poly, piece.Kind, p.Look, TerrainLooks.ETheme.Grimdark, 0))
                {
                    var meshes = new List<(ArrayMesh, Transform3D)>();
                    foreach (MeshInstance3D mi in AssetLoader.All<MeshInstance3D>(root))
                        if (mi.Mesh is ArrayMesh am && mi.Visible) meshes.Add((am, TerrainBaseTrim.RelativeTo(root, mi)));
                    float minX = poly.Min(v => v.X), maxX = poly.Max(v => v.X), minZ = poly.Min(v => v.Y), maxZ = poly.Max(v => v.Y);
                    var box = new Aabb(new Vector3(minX, 0, minZ), new Vector3(Mathf.Max(0.01f, maxX - minX), 1, Mathf.Max(0.01f, maxZ - minZ)));
                    float cell = Mathf.Max(box.Size.X, box.Size.Z) / Cells;
                    int nx = Math.Max(1, Mathf.CeilToInt(box.Size.X / cell)), nz = Math.Max(1, Mathf.CeilToInt(box.Size.Z / cell));
                    if (meshes.Count > 0)
                    {
                        var grid = Erode(Dilate(Raster(meshes, box, cell, ClearInches, nx, nz, y => y)));
                        for (int i = 0; i < nx; i++)
                            for (int j = 0; j < nz; j++)
                                if (grid[i, j] > 0f && !Geometry2D.IsPointInPolygon(new Vector2(minX + (i + 0.5f) * cell, minZ + (j + 0.5f) * cell), poly))
                                    grid[i, j] = 0f;
                        result = Rectangles(grid, nx, nz, cell, box)
                            .Select(r => (minX + r.U0 * box.Size.X, -(minZ + r.V1 * box.Size.Z), minX + r.U1 * box.Size.X, -(minZ + r.V0 * box.Size.Z), r.H))
                            .ToList();
                    }
                }
            }
        }
        catch (Exception e) { GD.Print($"TerrainSolidShape: piece {p.Look}: {e.Message}"); }
        finally { root.Free(); }
        PieceCache[key] = result;
        return result;
    }

    private static float[,] Dilate(float[,] g) => Step(g, grow: true);
    private static float[,] Erode(float[,] g) => Step(g, grow: false);

    // One cell of growing (the tallest neighbour) or shrinking (the shortest neighbour, the grid's edge counting as
    // standing so a wall along the model's edge keeps its full thickness): a one-cell gap closes at its sides' height.
    private static float[,] Step(float[,] g, bool grow)
    {
        int nx = g.GetLength(0), nz = g.GetLength(1);
        var r = new float[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float best = grow ? 0f : float.MaxValue;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int a = i + di, b = j + dj;
                        bool edge = a < 0 || b < 0 || a >= nx || b >= nz;
                        if (edge) continue;
                        best = grow ? Mathf.Max(best, g[a, b]) : Mathf.Min(best, g[a, b]);
                    }
                r[i, j] = best == float.MaxValue ? 0f : best;
            }
        return r;
    }

    /// <summary>Heights are merged in steps of half an inch; a piece that would split into more than
    /// <see cref="MaxParts"/> slivers (a sloped roof, a wood of many trees) is merged in coarser steps. Always rounding
    /// up, so a part never plays lower than the model.</summary>
    private static readonly float[] HeightSteps = { 0.5f, 1f, 1.5f, 2f, 3f };
    private const int MaxParts = 100;

    private static int Level(float h, float step) => h <= 0f ? 0 : Math.Max(1, Mathf.CeilToInt(h / step - 1e-3f));

    private static IReadOnlyList<(float U0, float V0, float U1, float V1, float H)> Rectangles(float[,] g, int nx, int nz, float cell, Aabb box)
    {
        IReadOnlyList<(float, float, float, float, float)> best = Array.Empty<(float, float, float, float, float)>();
        foreach (float step in HeightSteps)
        {
            best = Rectangles(g, nx, nz, cell, box, step);
            if (best.Count <= MaxParts) break;
        }
        return best;
    }

    // Row runs of equal height, stacked into rectangles while the run below is the same.
    private static IReadOnlyList<(float U0, float V0, float U1, float V1, float H)> Rectangles(float[,] g, int nx, int nz, float cell, Aabb box,
        float step)
    {
        var done = new List<(int X0, int X1, int Z0, int Z1, int L)>();
        var open = new Dictionary<(int, int, int), int>();
        for (int j = 0; j <= nz; j++)
        {
            var runs = new HashSet<(int, int, int)>();
            if (j < nz)
                for (int i = 0; i < nx;)
                {
                    int level = Level(g[i, j], step);
                    if (level == 0) { i++; continue; }
                    int start = i;
                    while (i < nx && Level(g[i, j], step) == level) i++;
                    runs.Add((start, i, level));
                }
            foreach (var run in open.Keys.ToList())
                if (!runs.Contains(run)) { done.Add((run.Item1, run.Item2, open[run], j, run.Item3)); open.Remove(run); }
            foreach (var run in runs) if (!open.ContainsKey(run)) open[run] = j;
        }
        return done.Select(r => (
            Mathf.Clamp(r.X0 * cell / box.Size.X, 0f, 1f), Mathf.Clamp(r.Z0 * cell / box.Size.Z, 0f, 1f),
            Mathf.Clamp(r.X1 * cell / box.Size.X, 0f, 1f), Mathf.Clamp(r.Z1 * cell / box.Size.Z, 0f, 1f),
            r.L * step)).ToList();
    }
}
