namespace FdgRaylib.Models3D;

/// <summary>
/// Splits a model into the areas a person would paint separately - cloak, face, staff, base - so it can be coloured
/// like a colouring book (Kai, 2026-11-15, option 3 of the painter mockups: "lets try it").
///
/// <para><b>How.</b> A printable model has no parts, only a surface of triangles, so the areas are found from its
/// shape:</para>
/// <list type="number">
/// <item>Neighbouring triangles that meet almost flat (under <c>creaseDegrees</c>) are the same patch of surface.
/// On a detailed sculpt that gives thousands of small patches - every fold of fur is its own.</item>
/// <item>Patches are then merged two at a time, cheapest first. Merging is cheap across a smooth border and between a
/// tiny patch and its neighbour, dear across a sharp crease between two big ones. Merging stops at nothing: the whole
/// order is recorded, so any number of areas can be read off afterwards (<see cref="AreasFor"/>) - "fewer areas" and
/// "more areas" cost nothing.</item>
/// <item>Separate pieces (a kit's figure and its base) are never merged into each other. Loose bits of one piece
/// that don't share an edge with the rest (a strap modelled as its own shell) are linked to whatever they touch.</item>
/// </list>
/// </summary>
public sealed class MeshAreas
{
    public const float DefaultCreaseDegrees = 30f;

    /// <summary>The usual number of areas offered: enough for cloth, armour, skin, hair, weapon, trim, base.</summary>
    public const int DefaultCount = 8;

    private readonly int[] _patchOfTriangle;
    private readonly float[] _triArea;
    private readonly List<(int Keep, int Gone)> _merges;
    private readonly int _patches;
    private readonly float _total;

    public int Triangles => _patchOfTriangle.Length;

    /// <summary>The fewest areas the model can be split into (one per separate piece, at least).</summary>
    public int MinAreas => _patches - _merges.Count;

    /// <summary>The most areas: every flat-enough patch on its own.</summary>
    public int MaxAreas => _patches;

    private MeshAreas(int[] patchOfTriangle, float[] triArea, List<(int, int)> merges, int patches)
    {
        _patchOfTriangle = patchOfTriangle;
        _triArea = triArea;
        _merges = merges;
        _patches = patches;
        _total = triArea.Sum();
    }

    /// <param name="positions">x y z per corner.</param>
    /// <param name="indices">three corners per triangle.</param>
    /// <param name="pieceOfTriangle">Which piece each triangle belongs to (figure 0, base 1...); null = all one piece.</param>
    public static MeshAreas Find(float[] positions, int[] indices, int[]? pieceOfTriangle = null, float creaseDegrees = DefaultCreaseDegrees)
    {
        int nT = indices.Length / 3, nV = positions.Length / 3;
        var normal = new (float X, float Y, float Z)[nT];
        var area = new float[nT];
        for (int t = 0; t < nT; t++)
        {
            var (ax, ay, az) = P(positions, indices[t * 3]);
            var (bx, by, bz) = P(positions, indices[t * 3 + 1]);
            var (cx, cy, cz) = P(positions, indices[t * 3 + 2]);
            float ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            area[t] = len / 2;
            normal[t] = len > 0 ? (nx / len, ny / len, nz / len) : (0, 0, 0);
        }
        int Piece(int t) => pieceOfTriangle?[t] ?? 0;

        // ---- triangles sharing an edge, with the angle between them
        var edgeOwner = new Dictionary<long, int>(nT * 2);
        var links = new List<(int A, int B, float Len, float Angle)>(nT * 3 / 2);
        for (int t = 0; t < nT; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = indices[t * 3 + k], b = indices[t * 3 + (k + 1) % 3];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!edgeOwner.TryGetValue(key, out int other)) { edgeOwner[key] = t; continue; }
                if (other < 0 || Piece(other) != Piece(t)) continue;
                edgeOwner[key] = -1; // a third triangle on the same edge (rare, badly-made files) joins nothing
                var (n1, n2) = (normal[t], normal[other]);
                float dot = Math.Clamp(n1.X * n2.X + n1.Y * n2.Y + n1.Z * n2.Z, -1f, 1f);
                links.Add((t, other, Dist(positions, a, b), MathF.Acos(dot) * 180f / MathF.PI));
            }

        // ---- patches: triangles joined across near-flat edges
        var uf = new UnionFind(nT);
        foreach (var l in links) if (l.Angle < creaseDegrees) uf.Union(l.A, l.B);
        var patchOf = new int[nT];
        var patchIds = new Dictionary<int, int>();
        for (int t = 0; t < nT; t++)
        {
            int r = uf.Find(t);
            if (!patchIds.TryGetValue(r, out int p)) patchIds[r] = p = patchIds.Count;
            patchOf[t] = p;
        }
        int nP = patchIds.Count;
        var patchArea = new double[nP];
        var patchPiece = new int[nP];
        for (int t = 0; t < nT; t++) { patchArea[patchOf[t]] += area[t]; patchPiece[patchOf[t]] = Piece(t); }

        // ---- borders between patches: length, and length x angle (for the average sharpness)
        var adj = new Dictionary<int, (double Len, double AngLen)>[nP];
        for (int p = 0; p < nP; p++) adj[p] = new();
        void AddBorder(int p, int q, double len, double angle)
        {
            if (p == q) return;
            adj[p][q] = adj[p].TryGetValue(q, out var e) ? (e.Len + len, e.AngLen + len * angle) : (len, len * angle);
            adj[q][p] = adj[q].TryGetValue(p, out e) ? (e.Len + len, e.AngLen + len * angle) : (len, len * angle);
        }
        foreach (var l in links) AddBorder(patchOf[l.A], patchOf[l.B], l.Len, l.Angle);
        LinkLooseShells(positions, indices, nV, nT, links, patchOf, Piece, AddBorder);
        LinkStrays(positions, indices, nT, patchOf, nP, patchPiece, adj, AddBorder);

        // ---- merge, cheapest first, recording the order
        double total = Math.Max(patchArea.Sum(), 1e-9);
        var version = new int[nP];
        var alive = Enumerable.Repeat(true, nP).ToArray();
        var queue = new PriorityQueue<(int P, int Q, int Vp, int Vq), double>();
        double Cost(int p, int q)
        {
            var e = adj[p][q];
            double sharp = e.AngLen / Math.Max(e.Len, 1e-9);
            double small = Math.Min(patchArea[p], patchArea[q]) / total;
            return (sharp + 3) * Math.Sqrt(small);
        }
        for (int p = 0; p < nP; p++)
            foreach (int q in adj[p].Keys)
                if (p < q) queue.Enqueue((p, q, 0, 0), Cost(p, q));

        var merges = new List<(int, int)>();
        while (queue.TryDequeue(out var item, out _))
        {
            var (p, q, vp, vq) = item;
            if (!alive[p] || !alive[q] || version[p] != vp || version[q] != vq) continue;
            int keep = adj[p].Count >= adj[q].Count ? p : q, gone = keep == p ? q : p;
            foreach (var (r, e) in adj[gone])
            {
                if (r == keep) continue;
                adj[r].Remove(gone);
                adj[keep][r] = adj[keep].TryGetValue(r, out var k) ? (k.Len + e.Len, k.AngLen + e.AngLen) : e;
                adj[r][keep] = adj[keep][r];
            }
            adj[keep].Remove(gone);
            adj[gone] = new();
            alive[gone] = false;
            patchArea[keep] += patchArea[gone];
            version[keep]++;
            merges.Add((keep, gone));
            foreach (int r in adj[keep].Keys)
                queue.Enqueue(keep < r ? (keep, r, version[keep], version[r]) : (r, keep, version[r], version[keep]), Cost(keep, r));
        }
        return new MeshAreas(patchOf, area, merges, nP);
    }

    /// <summary>
    /// The model in <paramref name="count"/> areas (or as near as it can get): an area number for every triangle,
    /// biggest area first (0), and each area's share of the surface.
    /// </summary>
    public int[] AreasFor(int count, out float[] shares)
    {
        int steps = Math.Clamp(_patches - count, 0, _merges.Count);
        var uf = new UnionFind(_patches);
        for (int i = 0; i < steps; i++) uf.UnionInto(_merges[i].Keep, _merges[i].Gone);

        var sum = new Dictionary<int, double>();
        for (int t = 0; t < _patchOfTriangle.Length; t++)
        {
            int r = uf.Find(_patchOfTriangle[t]);
            sum[r] = sum.GetValueOrDefault(r) + _triArea[t];
        }
        var order = sum.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        var rank = new Dictionary<int, int>();
        for (int i = 0; i < order.Count; i++) rank[order[i]] = i;
        double total = Math.Max(_total, 1e-9);
        shares = order.Select(r => (float)(sum[r] / total)).ToArray();
        var result = new int[_patchOfTriangle.Length];
        for (int t = 0; t < result.Length; t++) result[t] = rank[uf.Find(_patchOfTriangle[t])];
        return result;
    }

    /// <summary>
    /// Shells of one piece that share no edge with the rest of it (a belt, a strap, a beard modelled separately)
    /// are tied to the nearest other part of the same piece they touch, so they can merge into it.
    /// </summary>
    private static void LinkLooseShells(float[] pos, int[] idx, int nV, int nT, List<(int A, int B, float Len, float Angle)> links,
        int[] patchOf, Func<int, int> piece, Action<int, int, double, double> addBorder)
    {
        var shells = new UnionFind(nT);
        foreach (var l in links) shells.Union(l.A, l.B);
        var shellSize = new Dictionary<int, int>();
        for (int t = 0; t < nT; t++) { int s = shells.Find(t); shellSize[s] = shellSize.GetValueOrDefault(s) + 1; }
        if (shellSize.Count <= 1) return;

        var triOfCorner = new int[nV];
        Array.Fill(triOfCorner, -1);
        for (int t = 0; t < nT; t++) for (int k = 0; k < 3; k++) if (triOfCorner[idx[t * 3 + k]] < 0) triOfCorner[idx[t * 3 + k]] = t;

        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        for (int v = 0; v < nV; v++)
        {
            x0 = Math.Min(x0, pos[v * 3]); x1 = Math.Max(x1, pos[v * 3]);
            y0 = Math.Min(y0, pos[v * 3 + 1]); y1 = Math.Max(y1, pos[v * 3 + 1]);
            z0 = Math.Min(z0, pos[v * 3 + 2]); z1 = Math.Max(z1, pos[v * 3 + 2]);
        }
        float cell = Math.Max(Math.Max(x1 - x0, y1 - y0), z1 - z0) / 150f;
        if (cell <= 0) return;
        (int, int, int) Cell(int v) => ((int)((pos[v * 3] - x0) / cell), (int)((pos[v * 3 + 1] - y0) / cell), (int)((pos[v * 3 + 2] - z0) / cell));
        var grid = new Dictionary<(int, int, int), List<int>>();
        for (int v = 0; v < nV; v++)
        {
            if (triOfCorner[v] < 0) continue;
            var c = Cell(v);
            if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
            if (list.Count < 24) list.Add(v); // a few per cell is plenty to find a neighbour
        }

        int biggest = shellSize.MaxBy(kv => kv.Value).Key;
        var linked = new HashSet<(int, int)>();
        for (int v = 0; v < nV; v++)
        {
            int t = triOfCorner[v];
            if (t < 0) continue;
            int shell = shells.Find(t);
            if (shell == biggest) continue;
            var (cx, cy, cz) = Cell(v);
            int best = -1;
            float bestD = cell * cell * 4;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var near))
                            foreach (int w in near)
                            {
                                int tw = triOfCorner[w];
                                if (shells.Find(tw) == shell || piece(tw) != piece(t)) continue;
                                float d = Dist2(pos, v, w);
                                if (d < bestD) { bestD = d; best = tw; }
                            }
            if (best < 0) continue;
            int p = patchOf[t], q = patchOf[best];
            if (linked.Add(p < q ? (p, q) : (q, p))) addBorder(p, q, cell, 60);
        }
    }

    /// <summary>
    /// Whatever is still cut off from the rest of its piece after <see cref="LinkLooseShells"/> (a bit floating a
    /// little way off - a spark, a strand) is tied to the nearest patch of the main part, so it can always merge.
    /// Without this each stray bit is an area of its own that can never go, and they crowd out the real areas.
    /// </summary>
    private static void LinkStrays(float[] pos, int[] idx, int nT, int[] patchOf, int nP, int[] patchPiece,
        Dictionary<int, (double Len, double AngLen)>[] adj, Action<int, int, double, double> addBorder)
    {
        var centre = new (double X, double Y, double Z, double W)[nP];
        for (int t = 0; t < nT; t++)
        {
            int p = patchOf[t];
            for (int k = 0; k < 3; k++)
            {
                int v = idx[t * 3 + k];
                var c = centre[p];
                centre[p] = (c.X + pos[v * 3], c.Y + pos[v * 3 + 1], c.Z + pos[v * 3 + 2], c.W + 1);
            }
        }
        for (int p = 0; p < nP; p++) { var c = centre[p]; centre[p] = (c.X / c.W, c.Y / c.W, c.Z / c.W, c.W); }
        double D(int a, int b) { var (x, y, z, _) = centre[a]; var (u, v, w, _) = centre[b]; return (x - u) * (x - u) + (y - v) * (y - v) + (z - w) * (z - w); }

        var comp = new UnionFind(nP);
        for (int p = 0; p < nP; p++) foreach (int q in adj[p].Keys) comp.Union(p, q);
        foreach (int piece in patchPiece.Distinct())
        {
            var groups = Enumerable.Range(0, nP).Where(p => patchPiece[p] == piece).GroupBy(p => comp.Find(p))
                .Select(g => g.ToList()).OrderByDescending(g => g.Sum(p => centre[p].W)).ToList();
            if (groups.Count <= 1) continue;
            var main = new List<int>(groups[0]);
            foreach (var stray in groups.Skip(1))
            {
                // The main part's patch nearest the stray bit, then the stray's patch nearest that.
                var (sx, sy, sz) = (stray.Average(p => centre[p].X), stray.Average(p => centre[p].Y), stray.Average(p => centre[p].Z));
                int q = main.MinBy(m => { var (x, y, z, _) = centre[m]; return (x - sx) * (x - sx) + (y - sy) * (y - sy) + (z - sz) * (z - sz); });
                int p = stray.MinBy(s => D(s, q));
                addBorder(p, q, 1e-3, 75);
                main.AddRange(stray);
            }
        }
    }

    private static (float, float, float) P(float[] pos, int v) => (pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]);
    private static float Dist(float[] pos, int a, int b) => MathF.Sqrt(Dist2(pos, a, b));
    private static float Dist2(float[] pos, int a, int b)
    {
        float dx = pos[a * 3] - pos[b * 3], dy = pos[a * 3 + 1] - pos[b * 3 + 1], dz = pos[a * 3 + 2] - pos[b * 3 + 2];
        return dx * dx + dy * dy + dz * dz;
    }

    private sealed class UnionFind
    {
        private readonly int[] _parent;
        public UnionFind(int n) { _parent = new int[n]; for (int i = 0; i < n; i++) _parent[i] = i; }
        public int Find(int x)
        {
            while (_parent[x] != x) { _parent[x] = _parent[_parent[x]]; x = _parent[x]; }
            return x;
        }
        public void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) _parent[b] = a; }
        public void UnionInto(int keep, int gone) { keep = Find(keep); gone = Find(gone); if (keep != gone) _parent[gone] = keep; }
    }
}
