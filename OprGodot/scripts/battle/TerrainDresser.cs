using System;
using System.Collections.Generic;
using System.Linq;
using FdgRaylib.Terrain;
using Godot;
using static FdgRaylib.Terrain.TerrainLooks;

namespace OprGodot.Battle;

/// <summary>
/// Builds a terrain piece's 3D look from its <see cref="TerrainLooks"/> recipe (Kai, 2026-11-14: free packs bundled with
/// the game, a look per game, area terrain scattered on a base, auto pick + Map Maker override). Shared by the battle
/// table and the Map Maker so both show the same thing.
///
/// <para>Outlines come in world units (table inches) on the XZ plane: <c>Vector2(x, z)</c> of the world, as
/// <see cref="BattleTable.Prism"/> takes them. Every look draws a base over the exact footprint first, so the edge the
/// rules use is always visible; the models stand on it. Randomness is seeded per piece
/// (<see cref="TerrainLooks.SeedFor"/>), so every machine draws the same trees in the same places.</para>
/// </summary>
public static class TerrainDresser
{
    private const float BaseHeight = 0.06f;

    // ------------------------------------------------------------------ models

    private sealed record Model(PackedScene Scene, Aabb Box);
    private static readonly Dictionary<string, Model?> Models = new();

    /// <summary>A bundled model ("nature/tree_oak") and its bounds, loaded once per run; null when it isn't there.</summary>
    private static Model? Load(string file)
    {
        if (Models.TryGetValue(file, out Model? m)) return m;
        PackedScene? scene = null;
        foreach (string ext in new[] { ".glb", ".gltf" })
        {
            string path = $"res://terrain/{file}{ext}";
            if (ResourceLoader.Exists(path)) { scene = GD.Load<PackedScene>(path); break; }
        }
        if (scene == null) { Models[file] = null; return null; }
        Node3D probe = scene.Instantiate<Node3D>();
        Aabb box = Bounds(probe, Transform3D.Identity);
        probe.Free();
        return Models[file] = box.Size.Y > 0 || box.Size.X > 0 ? new Model(scene, box) : null;
    }

    private static Aabb Bounds(Node node, Transform3D parent)
    {
        Transform3D t = node is Node3D n3 ? parent * n3.Transform : parent;
        Aabb? box = null;
        if (node is MeshInstance3D mi && mi.Mesh != null)
            box = t * mi.Mesh.GetAabb();
        foreach (Node c in node.GetChildren())
        {
            Aabb cb = Bounds(c, t);
            if (cb.Size == Vector3.Zero && cb.Position == Vector3.Zero) continue;
            box = box is { } b ? b.Merge(cb) : cb;
        }
        return box ?? new Aabb();
    }

    /// <summary>Puts <paramref name="file"/> on the table: its bottom-centre at <paramref name="at"/>, turned
    /// <paramref name="yaw"/> radians, scaled by <paramref name="scale"/> (x along its own width, z along its depth).</summary>
    private static Node3D? Place(Node parent, string file, Vector3 at, float yaw, Vector3 scale)
    {
        if (Load(file) is not { } m) return null;
        Node3D inst = m.Scene.Instantiate<Node3D>();
        if (file.StartsWith("nature/", StringComparison.Ordinal)) Naturalise(inst, file.Contains("_fall"));
        var holder = new Node3D { Position = at, Rotation = new Vector3(0, yaw, 0), Scale = scale };
        Vector3 c = m.Box.GetCenter();
        inst.Position = new Vector3(-c.X, -m.Box.Position.Y, -c.Z);
        holder.AddChild(inst);
        parent.AddChild(holder);
        return holder;
    }

    /// <summary>A model scaled evenly to stand <paramref name="height"/> tall - but never wider than
    /// <paramref name="maxWidth"/>, so a squat rock asked to be tall doesn't spread over the whole piece.</summary>
    private static Node3D? PlaceTall(Node parent, string file, Vector3 at, float yaw, float height, float maxWidth = float.MaxValue)
    {
        if (Load(file) is not { } m || m.Box.Size.Y <= 0.0001f) return null;
        float s = height / m.Box.Size.Y;
        float wide = Mathf.Max(m.Box.Size.X, m.Box.Size.Z) * s;
        if (wide > maxWidth && wide > 0) s *= maxWidth / wide;
        return Place(parent, file, at, yaw, new Vector3(s, s, s));
    }

    // Kenney's Nature Kit is coloured teal and salmon; next to the other packs on a battle table that reads as candy.
    // Its leaves are turned green and its trunks and rocks brown-grey, once per material (autumn leaves stay orange).
    private static readonly Dictionary<Material, Material> Recoloured = new();

    private static void Naturalise(Node node, bool keepOrange)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.GetActiveMaterial(i) is StandardMaterial3D mat)
                {
                    if (!Recoloured.TryGetValue(mat, out Material? fixedMat))
                    {
                        Color c = mat.AlbedoColor;
                        Color n = c;
                        if (c.S > 0.25f && c.H > 0.38f && c.H < 0.56f) n = Color.FromHsv(0.25f + (c.H - 0.45f) * 0.3f, c.S * 0.7f, c.V * 0.62f);
                        else if (!keepOrange && c.S > 0.3f && (c.H < 0.11f || c.H > 0.95f)) n = Color.FromHsv(0.07f, c.S * 0.4f, c.V * 0.6f);
                        var dup = (StandardMaterial3D)mat.Duplicate();
                        dup.AlbedoColor = n;
                        Recoloured[mat] = fixedMat = dup;
                    }
                    mi.SetSurfaceOverrideMaterial(i, fixedMat);
                }
        foreach (Node c in node.GetChildren()) Naturalise(c, keepOrange);
    }

    // ------------------------------------------------------------------ the piece

    /// <summary>
    /// The piece's look under <paramref name="parent"/>, or false when the theme has no look for it (the caller draws
    /// its block). <paramref name="polygons"/> are the footprint's parts in world XZ; <paramref name="height"/> is the
    /// rules height in inches.
    /// </summary>
    public static bool Dress(Node3D parent, IReadOnlyList<Vector2[]> polygons, float height, string kind, string? lookId,
        ETheme theme, int seed, string meta)
    {
        Look? look = Pick(kind, lookId, theme, seed);
        if (look == null || polygons.Count == 0) return false;
        var rng = new Random(seed);
        Color ground = Color.FromHtml(look.Ground);
        foreach (Vector2[] poly in polygons.Where(p => p.Length >= 3))
        {
            var piece = new Node3D();
            parent.AddChild(piece);
            piece.SetMeta("terrain", meta);
            DressOne(piece, poly, height, look, ground, rng);
        }
        return true;
    }

    private static void DressOne(Node3D root, Vector2[] poly, float height, Look look, Color ground, Random rng)
    {
        switch (look.Layout)
        {
            case ELayout.Flat:
                Surface(root, poly, ground, look.Kinds.Contains("Lava"));
                Ring(root, poly, look.Props, look.Density, 0, rng);
                return;
            case ELayout.Mound:
                Mound(root, poly, Mathf.Clamp(height, 0.8f, 3f), ground, look, rng);
                return;
        }
        Base(root, poly, ground);
        switch (look.Layout)
        {
            case ELayout.Scatter:
                Scatter(root, poly, look.Props, look.Density, rng, 0.25f);
                if (look.Extras != null) Scatter(root, poly, look.Extras, look.ExtraDensity, rng, 0.15f);
                break;
            case ELayout.Rows:
                Rows(root, poly, look.Props, look.Density, rng);
                break;
            case ELayout.Single:
                Single(root, poly, height, look.Props, rng);
                break;
            case ELayout.Line:
            {
                Frame f = Frame.Of(poly);
                Line(root, f, f.Centre, f.U, f.Lu, f.Lv, look.Props, rng);
                break;
            }
            case ELayout.Corner:
                Corner(root, poly, look, rng);
                break;
            case ELayout.Stack:
                Stack(root, poly, height, look.Props);
                break;
            case ELayout.Cottage:
                Cottage(root, poly, height, look.Props, rng);
                break;
        }
    }

    // ------------------------------------------------------------------ layouts

    private static void Base(Node3D root, Vector2[] poly, Color c)
    {
        if (BattleTable.Prism(poly, BaseHeight) is { } mesh)
            TableLook.Mesh(root, mesh, TableLook.Mat(c, 0.95f), Vector3.Zero);
    }

    private static void Surface(Node3D root, Vector2[] poly, Color c, bool lava)
    {
        if (BattleTable.Prism(poly, BaseHeight) is not { } mesh) return;
        var mat = lava
            ? TableLook.Mat(c, 0.6f, c, 0.45f)
            : new StandardMaterial3D { AlbedoColor = c with { A = 0.92f }, Roughness = 0.08f, Metallic = 0.3f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        TableLook.Mesh(root, mesh, mat, Vector3.Zero);
    }

    /// <summary>Props scattered over the footprint, kept <paramref name="margin"/> inches in from its edge, no two
    /// closer than their spacing, so trees don't grow out of each other.</summary>
    private static void Scatter(Node3D root, Vector2[] poly, Prop[] props, float density, Random rng, float margin)
    {
        if (props.Length == 0 || density <= 0) return;
        float area = Mathf.Abs(Area(poly));
        int count = Math.Clamp((int)MathF.Round(area * density), 1, 60);
        float spacing = Mathf.Max(0.25f, 0.65f / Mathf.Sqrt(density));
        foreach (Vector2 p in Points(poly, count, spacing, margin, rng))
        {
            var prop = props[rng.Next(props.Length)];
            float room = Mathf.Min(spacing * 1.5f, EdgeDistance(poly, p) * 2f);
            PlaceTall(root, prop.File, new Vector3(p.X, BaseHeight, p.Y), (float)rng.NextDouble() * Mathf.Tau, Lerp(prop, rng), room);
        }
    }

    private static void Rows(Node3D root, Vector2[] poly, Prop[] props, float density, Random rng)
    {
        Frame f = Frame.Of(poly);
        float step = Mathf.Clamp(1f / Mathf.Sqrt(Mathf.Max(density, 0.1f)), 0.6f, 2f);
        for (float a = -f.Lu / 2 + step / 2; a < f.Lu / 2; a += step)
            for (float b = -f.Lv / 2 + step / 2; b < f.Lv / 2; b += step)
            {
                Vector2 p = f.Centre + f.U * a + f.V * b + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * step * 0.15f;
                if (!Inside(poly, p, 0.2f)) continue;
                var prop = props[rng.Next(props.Length)];
                PlaceTall(root, prop.File, new Vector3(p.X, BaseHeight, p.Y), (float)rng.NextDouble() * Mathf.Tau, Lerp(prop, rng), step * 1.1f);
            }
    }

    /// <summary>Props round the edge of the footprint (rocks round a pond, stones by the lava).</summary>
    private static void Ring(Node3D root, Vector2[] poly, Prop[] props, float density, float y, Random rng)
    {
        if (props.Length == 0) return;
        float perimeter = Enumerable.Range(0, poly.Length).Sum(i => poly[i].DistanceTo(poly[(i + 1) % poly.Length]));
        int count = Math.Clamp((int)(perimeter * density), 2, 40);
        Vector2 c = Centroid(poly);
        for (int i = 0; i < count; i++)
        {
            Vector2 edge = OnEdge(poly, (float)rng.NextDouble());
            float k = 0.85f + (float)rng.NextDouble() * 0.12f;
            Vector2 p = c + (edge - c) * k;
            var prop = props[rng.Next(props.Length)];
            PlaceTall(root, prop.File, new Vector3(p.X, y + BaseHeight, p.Y), (float)rng.NextDouble() * Mathf.Tau, Lerp(prop, rng), Mathf.Max(0.3f, EdgeDistance(poly, p) * 2f));
        }
    }

    /// <summary>A hill: rings drawn in toward the middle, rising to <paramref name="top"/>; props round its lower slope.</summary>
    private static void Mound(Node3D root, Vector2[] poly, float top, Color c, Look look, Random rng)
    {
        Vector2 centre = Centroid(poly);
        float[] ks = { 1f, 0.85f, 0.65f, 0.45f, 0.25f, 0f };
        float H(float k) => top * (1 - k * k) * (1 - 0.15f * k);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 At(int i, int r) => new Vector3(centre.X + (poly[i].X - centre.X) * ks[r], H(ks[r]) + BaseHeight, centre.Y + (poly[i].Y - centre.Y) * ks[r]);
        void Tri(Vector3 a, Vector3 b, Vector3 cc)
        {
            // Wound so the face points up, whichever way round the outline runs.
            if ((b - a).Cross(cc - a).Y < 0) (b, cc) = (cc, b);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(cc);
        }
        for (int r = 0; r < ks.Length - 1; r++)
            for (int i = 0; i < poly.Length; i++)
            {
                int j = (i + 1) % poly.Length;
                Vector3 a = At(i, r), b = At(j, r), cc = At(j, r + 1), d = At(i, r + 1);
                Tri(a, b, cc);
                if (r < ks.Length - 2) Tri(a, cc, d);
            }
        st.GenerateNormals();
        var mat = TableLook.Mat(c, 0.95f);
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        TableLook.Mesh(root, st.Commit(), mat, Vector3.Zero);
        Base(root, poly, c.Darkened(0.15f));
        int count = Math.Clamp((int)(Mathf.Abs(Area(poly)) * look.Density), 2, 30);
        for (int n = 0; n < count; n++)
        {
            Vector2 edge = OnEdge(poly, (float)rng.NextDouble());
            float k = 0.55f + (float)rng.NextDouble() * 0.4f;
            Vector2 p = centre + (edge - centre) * k;
            var prop = look.Props[rng.Next(look.Props.Length)];
            PlaceTall(root, prop.File, new Vector3(p.X, H(k) + BaseHeight - 0.05f, p.Y), (float)rng.NextDouble() * Mathf.Tau, Lerp(prop, rng), 1.2f);
        }
    }

    /// <summary>One model fitted to the footprint, its long side along the footprint's: of the look's models, one of
    /// the three whose shape is nearest the footprint's.</summary>
    private static void Single(Node3D root, Vector2[] poly, float height, Prop[] props, Random rng)
    {
        Frame f = Frame.Of(poly);
        float want = f.Lu / Mathf.Max(f.Lv, 0.1f);
        var ranked = props.Select(p => (p, m: Load(p.File))).Where(x => x.m != null)
            .Select(x => (x.p, x.m!, aspect: Aspect(x.m!.Box)))
            .OrderBy(x => Mathf.Abs(Mathf.Log(x.aspect / want))).Take(3).ToList();
        if (ranked.Count == 0) return;
        var (prop, m, _) = ranked[rng.Next(ranked.Count)];
        bool longIsX = m.Box.Size.X >= m.Box.Size.Z;
        float longM = Mathf.Max(m.Box.Size.X, m.Box.Size.Z), shortM = Mathf.Max(0.001f, Mathf.Min(m.Box.Size.X, m.Box.Size.Z));
        float s = Mathf.Min(f.Lu / longM, f.Lv / shortM) * 0.94f;
        float tall = m.Box.Size.Y * s, cap = Mathf.Max(prop.MaxHeight, height * 1.5f);
        float sy = tall > cap ? s * cap / tall : s;
        float yaw = YawFor(f.U) + (longIsX ? 0 : Mathf.Pi / 2);
        Place(root, prop.File, new Vector3(f.Centre.X, BaseHeight, f.Centre.Y), yaw, new Vector3(s, sy, s));
    }

    /// <summary>Segments of a wall model along <paramref name="u"/>, <paramref name="length"/> long, stretched to fill
    /// it exactly; each segment no deeper than <paramref name="depth"/>.</summary>
    private static void Line(Node3D root, Frame f, Vector2 centre, Vector2 u, float length, float depth, Prop[] props, Random rng)
    {
        var usable = props.Where(p => Load(p.File) != null).ToList();
        if (usable.Count == 0 || length < 0.2f) return;
        var first = usable[rng.Next(usable.Count)];
        Model m0 = Load(first.File)!;
        float targetH = Lerp(first, rng);
        float s0 = targetH / Mathf.Max(0.001f, m0.Box.Size.Y);
        float segLen = Mathf.Max(m0.Box.Size.X, m0.Box.Size.Z) * s0;
        int n = Math.Max(1, (int)MathF.Round(length / Mathf.Max(0.3f, segLen)));
        float each = length / n;
        for (int i = 0; i < n; i++)
        {
            var prop = usable[rng.Next(usable.Count)];
            Model m = Load(prop.File)!;
            bool longIsX = m.Box.Size.X >= m.Box.Size.Z;
            float longM = Mathf.Max(0.001f, Mathf.Max(m.Box.Size.X, m.Box.Size.Z)), shortM = Mathf.Max(0.001f, Mathf.Min(m.Box.Size.X, m.Box.Size.Z));
            float sUp = targetH / Mathf.Max(0.001f, m.Box.Size.Y);
            float sAlong = each / longM, sAcross = Mathf.Min(sUp, depth / shortM);
            Vector2 p = centre + u * (-length / 2 + each * (i + 0.5f));
            float yaw = YawFor(u) + (longIsX ? 0 : Mathf.Pi / 2);
            Vector3 scale = longIsX ? new Vector3(sAlong, sUp, sAcross) : new Vector3(sAcross, sUp, sAlong);
            Place(root, prop.File, new Vector3(p.X, BaseHeight, p.Y), yaw, scale);
        }
    }

    /// <summary>Ruins: broken walls along two sides that meet at a corner, rubble scattered inside.</summary>
    private static void Corner(Node3D root, Vector2[] poly, Look look, Random rng)
    {
        Frame f = Frame.Of(poly);
        float su = rng.Next(2) == 0 ? -1 : 1, sv = rng.Next(2) == 0 ? -1 : 1;
        float thick = Mathf.Min(0.6f, f.Lv * 0.2f);
        Vector2 cornerEdgeA = f.Centre + f.V * sv * (f.Lv / 2 - thick / 2);
        float lenA = f.Lu * (0.55f + (float)rng.NextDouble() * 0.3f);
        Line(root, f, cornerEdgeA + f.U * su * (f.Lu - lenA) / 2, f.U, lenA, thick, look.Props, rng);
        Vector2 cornerEdgeB = f.Centre + f.U * su * (f.Lu / 2 - thick / 2);
        float lenB = f.Lv * (0.45f + (float)rng.NextDouble() * 0.35f);
        Line(root, f, cornerEdgeB + f.V * sv * (f.Lv - lenB) / 2, f.V, lenB, thick, look.Props, rng);
        if (look.Extras != null) Scatter(root, poly, look.Extras, look.ExtraDensity, rng, 0.2f);
    }

    /// <summary>A tower: base, middles up to the height, roof - fitted to the footprint's short side.</summary>
    private static void Stack(Node3D root, Vector2[] poly, float height, Prop[] parts)
    {
        if (parts.Length < 3 || Load(parts[0].File) is not { } b) return;
        Frame f = Frame.Of(poly);
        float side = Mathf.Min(f.Lu, f.Lv) * 0.9f;
        float s = side / Mathf.Max(0.001f, Mathf.Max(b.Box.Size.X, b.Box.Size.Z));
        float y = BaseHeight, target = Mathf.Max(height, 3f);
        float yaw = YawFor(f.U);
        Place(root, parts[0].File, new Vector3(f.Centre.X, y, f.Centre.Y), yaw, Vector3.One * s);
        y += b.Box.Size.Y * s;
        if (Load(parts[1].File) is { } mid)
            for (int i = 0; i < 4 && y + mid.Box.Size.Y * s < target; i++)
            {
                Place(root, parts[1].File, new Vector3(f.Centre.X, y, f.Centre.Y), yaw, Vector3.One * s);
                y += mid.Box.Size.Y * s;
            }
        Place(root, parts[2].File, new Vector3(f.Centre.X, y, f.Centre.Y), yaw, Vector3.One * s);
    }

    /// <summary>
    /// A cottage from Quaternius' Medieval Village walls (2 m modules, 3.12 m tall) under the round-tile roof whose size
    /// is nearest the footprint's shape, scaled to fill it. Props[0..2] are the plain / window / door walls,
    /// Props[3] the corner post.
    /// </summary>
    private static void Cottage(Node3D root, Vector2[] poly, float height, Prop[] parts, Random rng)
    {
        Frame f = Frame.Of(poly);
        (int A, int B)[] roofs = { (4, 4), (4, 6), (4, 8), (6, 6), (6, 8), (6, 10), (6, 12), (6, 14), (8, 8), (8, 10), (8, 12), (8, 14) };
        float want = f.Lu / Mathf.Max(f.Lv, 0.1f);
        var (A, B) = roofs.OrderBy(r => Mathf.Abs(Mathf.Log((float)r.B / r.A / want))).ThenBy(r => r.A).First();
        var house = new Node3D();
        const float WallH = 3.12f;
        int doorAt = rng.Next(B / 2);
        void Side(float x0, float z0, Vector2 dir, int modules, float yaw, bool door)
        {
            for (int i = 0; i < modules; i++)
            {
                int pick = door && i == doorAt ? 2 : rng.Next(3) == 0 ? 1 : 0;
                var at = new Vector3(x0 + dir.X * (2 * i + 1), 0, z0 + dir.Y * (2 * i + 1));
                Place(house, parts[pick].File, at, yaw, Vector3.One);
            }
        }
        // Long sides run along local z (B metres), short along x (A metres); each wall's outside faces away from the middle.
        Side(-A / 2f, -B / 2f, new Vector2(0, 1), B / 2, -Mathf.Pi / 2, true);
        Side(A / 2f, -B / 2f, new Vector2(0, 1), B / 2, Mathf.Pi / 2, false);
        Side(-A / 2f, -B / 2f, new Vector2(1, 0), A / 2, Mathf.Pi, false);
        Side(-A / 2f, B / 2f, new Vector2(1, 0), A / 2, 0, false);
        foreach (var (cx, cz) in new[] { (-A / 2f, -B / 2f), (A / 2f, -B / 2f), (A / 2f, B / 2f), (-A / 2f, B / 2f) })
            Place(house, parts[3].File, new Vector3(cx, 0, cz), 0, Vector3.One);
        if (Load($"village/Roof_RoundTiles_{A}x{B}") is { } roof)
        {
            Node3D inst = roof.Scene.Instantiate<Node3D>();
            inst.Position = new Vector3(0, WallH, 0);
            house.AddChild(inst);
        }
        // Fit the walls (A x B metres) to the footprint, long side along its long side; keep the whole house no taller
        // than half again the rules height.
        float s = Mathf.Min(f.Lu / B, f.Lv / A) * 0.92f;
        float tall = (WallH + (roofs.Contains((A, B)) ? A * 0.6f : 2f)) * s;
        float cap = Mathf.Max(height * 1.5f, 3f);
        float sy = tall > cap ? s * cap / tall : s;
        house.Scale = new Vector3(s, sy, s);
        house.Rotation = new Vector3(0, YawFor(f.U) + Mathf.Pi / 2, 0);
        house.Position = new Vector3(f.Centre.X, BaseHeight, f.Centre.Y);
        root.AddChild(house);
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>A footprint's own frame: its middle, its long side's direction <see cref="U"/> (and across,
    /// <see cref="V"/>) and its length along each.</summary>
    private readonly record struct Frame(Vector2 Centre, Vector2 U, Vector2 V, float Lu, float Lv)
    {
        public static Frame Of(Vector2[] poly)
        {
            // Try each edge's direction and keep the one whose box is smallest (a rectangle's own sides).
            Frame best = default;
            float bestArea = float.MaxValue;
            int step = Math.Max(1, poly.Length / 16);
            for (int i = 0; i < poly.Length; i += step)
            {
                Vector2 e = poly[(i + 1) % poly.Length] - poly[i];
                if (e.LengthSquared() < 1e-6f) continue;
                Vector2 u = e.Normalized(), v = new(-u.Y, u.X);
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (Vector2 p in poly)
                {
                    float a = p.Dot(u), b = p.Dot(v);
                    minU = Mathf.Min(minU, a); maxU = Mathf.Max(maxU, a); minV = Mathf.Min(minV, b); maxV = Mathf.Max(maxV, b);
                }
                float area = (maxU - minU) * (maxV - minV);
                if (area >= bestArea - 1e-4f) continue;
                bestArea = area;
                Vector2 c = u * (minU + maxU) / 2 + v * (minV + maxV) / 2;
                best = maxU - minU >= maxV - minV
                    ? new Frame(c, u, v, maxU - minU, maxV - minV)
                    : new Frame(c, v, -u, maxV - minV, maxU - minU);
            }
            return best;
        }
    }

    /// <summary>The Y rotation that turns a model's own +X onto <paramref name="dir"/> (world XZ).</summary>
    private static float YawFor(Vector2 dir) => Mathf.Atan2(-dir.Y, dir.X);

    private static float Aspect(Aabb box) =>
        Mathf.Max(box.Size.X, box.Size.Z) / Mathf.Max(0.001f, Mathf.Min(box.Size.X, box.Size.Z));

    private static float Lerp(Prop p, Random rng) => p.MinHeight + (p.MaxHeight - p.MinHeight) * (float)rng.NextDouble();

    private static float Area(Vector2[] poly)
    {
        float a = 0;
        for (int i = 0; i < poly.Length; i++) a += poly[i].Cross(poly[(i + 1) % poly.Length]);
        return a / 2;
    }

    private static Vector2 Centroid(Vector2[] poly)
    {
        Vector2 s = Vector2.Zero;
        foreach (Vector2 p in poly) s += p;
        return s / poly.Length;
    }

    /// <summary>A point a fraction <paramref name="t"/> of the way round the outline.</summary>
    private static Vector2 OnEdge(Vector2[] poly, float t)
    {
        float total = Enumerable.Range(0, poly.Length).Sum(i => poly[i].DistanceTo(poly[(i + 1) % poly.Length]));
        float want = t * total;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            float len = a.DistanceTo(b);
            if (want <= len) return a.Lerp(b, len < 1e-6f ? 0 : want / len);
            want -= len;
        }
        return poly[0];
    }

    /// <summary>How far <paramref name="p"/> is from the footprint's nearest edge.</summary>
    private static float EdgeDistance(Vector2[] poly, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < poly.Length; i++)
            best = Mathf.Min(best, Geometry2D.GetClosestPointToSegment(p, poly[i], poly[(i + 1) % poly.Length]).DistanceTo(p));
        return best;
    }

    private static bool Inside(Vector2[] poly, Vector2 p, float margin)
    {
        if (!Geometry2D.IsPointInPolygon(p, poly)) return false;
        for (int i = 0; i < poly.Length; i++)
            if (Geometry2D.GetClosestPointToSegment(p, poly[i], poly[(i + 1) % poly.Length]).DistanceTo(p) < margin) return false;
        return true;
    }

    private static IEnumerable<Vector2> Points(Vector2[] poly, int count, float spacing, float margin, Random rng)
    {
        float minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
        var placed = new List<Vector2>();
        for (int tries = 0; tries < count * 30 && placed.Count < count; tries++)
        {
            var p = new Vector2(minX + (float)rng.NextDouble() * (maxX - minX), minY + (float)rng.NextDouble() * (maxY - minY));
            if (!Inside(poly, p, margin)) continue;
            if (placed.Any(q => q.DistanceTo(p) < spacing)) continue;
            placed.Add(p);
        }
        return placed;
    }
}
