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

    private static readonly Dictionary<string, (string Sig, IReadOnlyList<(float U0, float V0, float U1, float V1)>? Parts)> Cache = new();

    /// <summary>Hooks the shape into map saving (App start-up).</summary>
    public static void Register() => TerrainSolids.Provider = For;

    /// <summary>Forget a model's shape (its file or base setting changed).</summary>
    public static void Forget(string file)
    {
        foreach (string key in Cache.Keys.Where(k => k.StartsWith(file + "|", StringComparison.Ordinal)).ToList()) Cache.Remove(key);
    }

    /// <summary>The solid parts of the model a look names, as fractions of its box; null when this computer can't tell.</summary>
    public static IReadOnlyList<(float U0, float V0, float U1, float V1)>? For(string look)
    {
        if (WarhammerModels.Library?.ForLook(look) is not { Measured: true } entry) return null;
        string key = entry.File + "|" + entry.Id;
        string sig = $"{entry.Match.W:0.###}|{entry.Match.D:0.###}|{entry.Match.Sink:0.###}|{entry.Match.CutBase}";
        if (Cache.TryGetValue(key, out var have) && have.Sig == sig) return have.Parts;
        IReadOnlyList<(float, float, float, float)>? parts = null;
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
    public static IReadOnlyList<(float U0, float V0, float U1, float V1)>? Compute(Node3D model, EUpAxis up,
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
        var grid = new bool[nx, nz];

        void Mark(float x, float z)
        {
            int i = Math.Clamp((int)((x - box.Position.X) / cell), 0, nx - 1), j = Math.Clamp((int)((z - box.Position.Z) / cell), 0, nz - 1);
            grid[i, j] = true;
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
            foreach (Vector3 p in new[] { a, b, c }) if (p.Y > clearY) Mark(p.X, p.Z);
            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
            {
                float len = new Vector2(q.X - p.X, q.Z - p.Z).Length();
                int steps = Math.Max(1, Mathf.CeilToInt(len / (cell * 0.5f)));
                for (int k = 0; k <= steps; k++)
                {
                    Vector3 r = p.Lerp(q, k / (float)steps);
                    if (r.Y > clearY) Mark(r.X, r.Z);
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
                    if (grid[i, j]) continue;
                    float x = box.Position.X + (i + 0.5f) * cell, z = box.Position.Z + (j + 0.5f) * cell;
                    float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
                    float l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
                    float l3 = 1 - l1 - l2;
                    if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                    if (l1 * a.Y + l2 * b.Y + l3 * c.Y > clearY) grid[i, j] = true;
                }
        }

        grid = Erode(Dilate(grid));
        return Rectangles(grid, nx, nz, cell, box);
    }

    private static bool[,] Dilate(bool[,] g) => Step(g, grow: true);
    private static bool[,] Erode(bool[,] g) => Step(g, grow: false);

    // One cell of growing (any neighbour set) or shrinking (every neighbour set, the grid's edge counting as set so a
    // wall along the model's edge keeps its full thickness).
    private static bool[,] Step(bool[,] g, bool grow)
    {
        int nx = g.GetLength(0), nz = g.GetLength(1);
        var r = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                bool any = false, all = true;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int a = i + di, b = j + dj;
                        bool on = a < 0 || b < 0 || a >= nx || b >= nz ? !grow : g[a, b];
                        any |= on; all &= on;
                    }
                r[i, j] = grow ? any : all;
            }
        return r;
    }

    // Row runs, stacked into rectangles while the run below is the same.
    private static IReadOnlyList<(float U0, float V0, float U1, float V1)> Rectangles(bool[,] g, int nx, int nz, float cell, Aabb box)
    {
        var done = new List<(int X0, int X1, int Z0, int Z1)>();
        var open = new Dictionary<(int, int), int>();
        for (int j = 0; j <= nz; j++)
        {
            var runs = new HashSet<(int, int)>();
            if (j < nz)
                for (int i = 0; i < nx;)
                {
                    if (!g[i, j]) { i++; continue; }
                    int start = i;
                    while (i < nx && g[i, j]) i++;
                    runs.Add((start, i));
                }
            foreach (var run in open.Keys.ToList())
                if (!runs.Contains(run)) { done.Add((run.Item1, run.Item2, open[run], j)); open.Remove(run); }
            foreach (var run in runs) if (!open.ContainsKey(run)) open[run] = j;
        }
        return done.Select(r => (
            Mathf.Clamp(r.X0 * cell / box.Size.X, 0f, 1f), Mathf.Clamp(r.Z0 * cell / box.Size.Z, 0f, 1f),
            Mathf.Clamp(r.X1 * cell / box.Size.X, 0f, 1f), Mathf.Clamp(r.Z1 * cell / box.Size.Z, 0f, 1f))).ToList();
    }
}
