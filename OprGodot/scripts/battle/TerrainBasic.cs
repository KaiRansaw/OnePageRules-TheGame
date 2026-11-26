using System;
using System.Collections.Generic;
using System.Linq;
using FdgRaylib.Terrain;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// Plain stand-in terrain (Kai, 2026-11-14: "very basic to get the game going", while the real pieces are designed in
/// the Terrain Lab). Simple shapes only - no model packs: one object per thing, sized to the piece's rules footprint
/// and height. Building = a house; Ruins = broken walls; Forest = a few trees on a base; Rubble = a few rocks; and so on.
/// A muted palette per game (Grimdark Future grey/rust, Age of Fantasy stone/wood). Seeded per piece, so every machine
/// draws the same thing.
/// </summary>
public static partial class TerrainDresser
{
    private sealed record Palette(Color Ground, Color Wall, Color Roof, Color Trim, Color Leaf, Color Trunk, Color Rock, Color Water, Color Lava);

    private static readonly Palette Grim = new(
        Ground: new Color("#4a4842"), Wall: new Color("#8a8a84"), Roof: new Color("#5a5f63"), Trim: new Color("#8b5a3c"),
        Leaf: new Color("#3f5a3a"), Trunk: new Color("#4a3a2c"), Rock: new Color("#6c6862"), Water: new Color("#2f5a63"), Lava: new Color("#b84a1c"));

    private static readonly Palette Fantasy = new(
        Ground: new Color("#5a5040"), Wall: new Color("#c9bca0"), Roof: new Color("#8c4a32"), Trim: new Color("#6a4a2e"),
        Leaf: new Color("#4f7a3a"), Trunk: new Color("#5a4028"), Rock: new Color("#7d786e"), Water: new Color("#3a6f8f"), Lava: new Color("#b84a1c"));

    /// <summary>The stand-in for a piece; always draws something (unknown kinds get rubble).</summary>
    public static void DressBasic(Node3D parent, IReadOnlyList<Vector2[]> polygons, float height, string kind,
        TerrainLooks.ETheme theme, int seed, string meta)
    {
        Palette pal = theme == TerrainLooks.ETheme.Grimdark ? Grim : Fantasy;
        var rng = new Random(seed);
        foreach (Vector2[] poly in polygons.Where(p => p.Length >= 3))
        {
            var root = new Node3D();
            parent.AddChild(root);
            root.SetMeta("terrain", meta);
            BasicOne(root, poly, Mathf.Max(0.2f, height), kind, theme, pal, rng);
        }
    }

    private static void BasicOne(Node3D root, Vector2[] poly, float h, string kind, TerrainLooks.ETheme theme, Palette pal, Random rng)
    {
        Frame f = Frame.Of(poly);
        switch (kind)
        {
            case "Building":
                Base(root, poly, pal.Ground);
                House(root, f, Mathf.Max(h, 2.5f), theme, pal);
                break;
            case "Ruins":
                Base(root, poly, pal.Ground);
                BrokenWalls(root, f, Mathf.Clamp(h, 1.5f, 3f), pal.Wall, rng);
                Rocks(root, poly, 0.12f, 0.25f, 0.6f, pal.Wall.Darkened(0.2f), rng);
                break;
            case "Forest":
                Base(root, poly, pal.Ground.Lerp(pal.Leaf, 0.35f));
                Trees(root, poly, Mathf.Clamp(h, 2.5f, 4f), theme, pal, rng);
                break;
            case "Rubble":
            case "Hazard":
                Base(root, poly, kind == "Hazard" ? pal.Ground.Lerp(pal.Lava, 0.25f) : pal.Ground);
                Rocks(root, poly, 0.3f, 0.25f, 0.7f, pal.Rock, rng);
                break;
            case "Mountain":
                Base(root, poly, pal.Ground);
                Rocks(root, poly, 0.12f, 1.8f, Mathf.Max(h, 3f), pal.Rock, rng, big: true);
                break;
            case "Hill":
                BasicMound(root, poly, Mathf.Clamp(h, 0.8f, 2.5f), pal.Ground.Lerp(pal.Leaf, 0.5f));
                break;
            case "Wall":
                Base(root, poly, pal.Ground);
                Box(root, f.Centre, f.U, f.Lu - 0.1f, Mathf.Min(f.Lv, 0.6f), Mathf.Clamp(h, 1f, 3f), pal.Wall, BaseHeight);
                break;
            case "Barricade":
                Base(root, poly, pal.Ground);
                Barricade(root, f, theme, pal, rng);
                break;
            case "Field":
                Base(root, poly, pal.Trunk.Lerp(pal.Ground, 0.5f));
                Rows(root, f, pal.Leaf.Lerp(new Color("#c8b25a"), theme == TerrainLooks.ETheme.Fantasy ? 0.6f : 0.2f));
                break;
            case "Swamp":
                Base(root, poly, pal.Water.Lerp(pal.Leaf, 0.5f).Darkened(0.3f));
                Reeds(root, poly, pal.Leaf, rng);
                break;
            case "LakeShallow" or "LakeDeep" or "RiverShallow" or "RiverDeep":
                Surface(root, poly, kind.EndsWith("Deep") ? pal.Water.Darkened(0.25f) : pal.Water, false);
                break;
            case "Lava":
                Surface(root, poly, pal.Lava, true);
                break;
            case "Wreck":
                Base(root, poly, pal.Ground);
                Wreck(root, f, pal, rng);
                break;
            case "Marker":
                // Tactical Phase forward position markers: a plain flat disc in olive (no rules of their own).
                Base(root, poly, new Color("#aabe5a"));
                break;
            default:
                Base(root, poly, pal.Ground);
                Rocks(root, poly, 0.2f, 0.25f, 0.6f, pal.Rock, rng);
                break;
        }
    }

    // ------------------------------------------------------------------ shapes

    private static StandardMaterial3D Flat(Color c) => TableLook.Mat(c, 0.9f);

    /// <summary>A box <paramref name="len"/> along <paramref name="u"/>, <paramref name="depth"/> across, standing on <paramref name="y"/>.</summary>
    private static MeshInstance3D Box(Node3D root, Vector2 at, Vector2 u, float len, float depth, float tall, Color c, float y)
    {
        var mi = TableLook.Mesh(root, new BoxMesh { Size = new Vector3(len, tall, depth) }, Flat(c), new Vector3(at.X, y + tall / 2, at.Y));
        mi.Rotation = new Vector3(0, YawFor(u), 0);
        return mi;
    }

    /// <summary>A house filling the footprint: walls to 60% of the height, then the roof (gabled in fantasy, flat with a
    /// parapet in sci-fi).</summary>
    private static void House(Node3D root, Frame f, float h, TerrainLooks.ETheme theme, Palette pal)
    {
        float len = Mathf.Max(1f, f.Lu - 0.3f), dep = Mathf.Max(1f, f.Lv - 0.3f);
        float yaw = YawFor(f.U);
        var at = new Vector3(f.Centre.X, BaseHeight, f.Centre.Y);
        if (theme == TerrainLooks.ETheme.Fantasy)
        {
            float wallH = h * 0.6f;
            var walls = TableLook.Mesh(root, new BoxMesh { Size = new Vector3(len, wallH, dep) }, Flat(pal.Wall), at + new Vector3(0, wallH / 2, 0));
            walls.Rotation = new Vector3(0, yaw, 0);
            // Gable roof: a prism along the long side, overhanging a little.
            var roof = TableLook.Mesh(root, new PrismMesh { Size = new Vector3(dep + 0.4f, h - wallH, len + 0.3f) }, Flat(pal.Roof), at + new Vector3(0, wallH + (h - wallH) / 2, 0));
            roof.Rotation = new Vector3(0, yaw + Mathf.Pi / 2, 0);
            // A door on one long side.
            var door = TableLook.Mesh(root, new BoxMesh { Size = new Vector3(0.5f, Mathf.Min(0.9f, wallH * 0.7f), 0.06f) }, Flat(pal.Trim),
                at + new Vector3(0, Mathf.Min(0.9f, wallH * 0.7f) / 2, 0));
            door.Rotation = new Vector3(0, yaw, 0);
            door.Position += new Vector3(f.V.X, 0, f.V.Y) * (dep / 2 + 0.02f);
        }
        else
        {
            var body = TableLook.Mesh(root, new BoxMesh { Size = new Vector3(len, h, dep) }, Flat(pal.Wall), at + new Vector3(0, h / 2, 0));
            body.Rotation = new Vector3(0, yaw, 0);
            var roof = TableLook.Mesh(root, new BoxMesh { Size = new Vector3(len - 0.2f, 0.15f, dep - 0.2f) }, Flat(pal.Roof), at + new Vector3(0, h + 0.07f, 0));
            roof.Rotation = new Vector3(0, yaw, 0);
            var band = TableLook.Mesh(root, new BoxMesh { Size = new Vector3(len + 0.04f, 0.25f, dep + 0.04f) }, Flat(pal.Trim), at + new Vector3(0, h * 0.35f, 0));
            band.Rotation = new Vector3(0, yaw, 0);
        }
    }

    /// <summary>Two broken walls meeting at a corner: segments of uneven height, gaps left in them.</summary>
    private static void BrokenWalls(Node3D root, Frame f, float h, Color c, Random rng)
    {
        float t = 0.3f;
        float su = rng.Next(2) == 0 ? -1 : 1, sv = rng.Next(2) == 0 ? -1 : 1;
        void Run(Vector2 start, Vector2 dir, float length)
        {
            float pos = 0;
            while (pos < length - 0.3f)
            {
                float seg = Mathf.Min(length - pos, 0.6f + (float)rng.NextDouble() * 0.9f);
                if (rng.NextDouble() > 0.18) // a gap now and then
                {
                    float tall = h * (0.35f + (float)rng.NextDouble() * 0.65f);
                    Box(root, start + dir * (pos + seg / 2), dir, seg, t, tall, c.Darkened((float)rng.NextDouble() * 0.12f), BaseHeight);
                }
                pos += seg;
            }
        }
        Vector2 corner = f.Centre - f.U * su * (f.Lu / 2 - 0.25f) - f.V * sv * (f.Lv / 2 - 0.25f);
        Run(corner, f.U * su, f.Lu * 0.85f);
        Run(corner, f.V * sv, f.Lv * 0.75f);
    }

    /// <summary>Simple trees (a trunk and a cone, or a ball in fantasy) about two inches apart.</summary>
    private static void Trees(Node3D root, Vector2[] poly, float h, TerrainLooks.ETheme theme, Palette pal, Random rng)
    {
        int count = Math.Clamp((int)(Mathf.Abs(Area(poly)) / 4.5f), 2, 14);
        foreach (Vector2 p in Points(poly, count, 1.6f, 0.6f, rng))
        {
            float tall = h * (0.75f + (float)rng.NextDouble() * 0.35f);
            float trunkH = tall * 0.35f;
            Color leaf = pal.Leaf.Darkened((float)rng.NextDouble() * 0.15f);
            TableLook.Mesh(root, new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.13f, Height = trunkH }, Flat(pal.Trunk), new Vector3(p.X, BaseHeight + trunkH / 2, p.Y));
            if (theme == TerrainLooks.ETheme.Fantasy && rng.Next(3) > 0)
            {
                float r = (tall - trunkH) * 0.5f;
                TableLook.Mesh(root, new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 10, Rings = 6 }, Flat(leaf), new Vector3(p.X, BaseHeight + trunkH + r * 0.85f, p.Y));
            }
            else
                TableLook.Mesh(root, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.55f, Height = tall - trunkH * 0.6f, RadialSegments = 8 }, Flat(leaf),
                    new Vector3(p.X, BaseHeight + trunkH * 0.4f + (tall - trunkH * 0.6f) / 2, p.Y));
        }
    }

    /// <summary>Low-poly rocks, spaced out, never past the footprint's edge.</summary>
    private static void Rocks(Node3D root, Vector2[] poly, float density, float minSize, float maxSize, Color c, Random rng, bool big = false)
    {
        float area = Mathf.Abs(Area(poly));
        int count = Math.Clamp((int)(area * density), big ? 2 : 3, big ? 8 : 18);
        float spacing = big ? maxSize * 0.6f : 0.6f;
        foreach (Vector2 p in Points(poly, count, spacing, big ? 0.6f : 0.2f, rng))
        {
            float size = minSize + (float)rng.NextDouble() * (maxSize - minSize);
            float room = EdgeDistance(poly, p) * 2f;
            float wide = Mathf.Min(size * (big ? 0.8f : 1.2f), room);
            var mi = TableLook.Mesh(root, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 6, Rings = 3 }, Flat(c.Darkened((float)rng.NextDouble() * 0.15f)),
                new Vector3(p.X, BaseHeight + size * 0.4f, p.Y));
            mi.Scale = new Vector3(wide, size, wide * (0.7f + (float)rng.NextDouble() * 0.4f));
            mi.Rotation = new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0);
        }
    }

    private static void BasicMound(Node3D root, Vector2[] poly, float top, Color c)
    {
        Vector2 centre = Centroid(poly);
        float[] ks = { 1f, 0.85f, 0.65f, 0.45f, 0.25f, 0f };
        float H(float k) => top * (1 - k * k);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 At(int i, int r) => new(centre.X + (poly[i].X - centre.X) * ks[r], H(ks[r]) + BaseHeight, centre.Y + (poly[i].Y - centre.Y) * ks[r]);
        void Tri(Vector3 a, Vector3 b, Vector3 cc)
        {
            if ((b - a).Cross(cc - a).Y < 0) (b, cc) = (cc, b);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(cc);
        }
        for (int r = 0; r < ks.Length - 1; r++)
            for (int i = 0; i < poly.Length; i++)
            {
                int j = (i + 1) % poly.Length;
                Tri(At(i, r), At(j, r), At(j, r + 1));
                if (r < ks.Length - 2) Tri(At(i, r), At(j, r + 1), At(i, r + 1));
            }
        st.GenerateNormals();
        var mat = Flat(c);
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        TableLook.Mesh(root, st.Commit(), mat, Vector3.Zero);
        Base(root, poly, c.Darkened(0.15f));
    }

    /// <summary>A barricade: sandbags (sci-fi) or a wooden fence (fantasy) along the long side.</summary>
    private static void Barricade(Node3D root, Frame f, TerrainLooks.ETheme theme, Palette pal, Random rng)
    {
        float len = Mathf.Max(0.5f, f.Lu - 0.2f);
        if (theme == TerrainLooks.ETheme.Grimdark)
        {
            int n = Math.Max(2, (int)(len / 0.55f));
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < n - row; i++)
                {
                    float x = -len / 2 + (i + 0.5f + row * 0.5f) * (len / n);
                    Vector2 p = f.Centre + f.U * x;
                    var bag = TableLook.Mesh(root, new CapsuleMesh { Radius = 0.14f, Height = len / n + 0.02f }, Flat(new Color("#8a7d5a").Darkened((float)rng.NextDouble() * 0.12f)),
                        new Vector3(p.X, BaseHeight + 0.13f + row * 0.24f, p.Y));
                    bag.Rotation = new Vector3(0, YawFor(f.U), Mathf.Pi / 2);
                }
        }
        else
        {
            int posts = Math.Max(2, (int)(len / 1f) + 1);
            for (int i = 0; i < posts; i++)
            {
                Vector2 p = f.Centre + f.U * (-len / 2 + i * len / (posts - 1));
                TableLook.Mesh(root, new BoxMesh { Size = new Vector3(0.12f, 0.8f, 0.12f) }, Flat(pal.Trim), new Vector3(p.X, BaseHeight + 0.4f, p.Y));
            }
            foreach (float y in new[] { 0.3f, 0.62f })
                Box(root, f.Centre, f.U, len, 0.06f, 0.1f, pal.Trim.Lightened(0.1f), BaseHeight + y);
        }
    }

    private static void Rows(Node3D root, Frame f, Color crop)
    {
        float step = 0.8f;
        for (float b = -f.Lv / 2 + step / 2; b < f.Lv / 2 - 0.2f; b += step)
            Box(root, f.Centre + f.V * b, f.U, Mathf.Max(0.5f, f.Lu - 0.5f), 0.3f, 0.35f, crop, BaseHeight);
    }

    private static void Reeds(Node3D root, Vector2[] poly, Color c, Random rng)
    {
        int clumps = Math.Clamp((int)(Mathf.Abs(Area(poly)) / 5f), 2, 10);
        foreach (Vector2 p in Points(poly, clumps, 1.2f, 0.4f, rng))
            for (int i = 0; i < 5; i++)
            {
                var off = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.4f;
                float tall = 0.5f + (float)rng.NextDouble() * 0.5f;
                TableLook.Mesh(root, new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.035f, Height = tall, RadialSegments = 5 }, Flat(c),
                    new Vector3(p.X + off.X, BaseHeight + tall / 2, p.Y + off.Y));
            }
    }

    private static void Wreck(Node3D root, Frame f, Palette pal, Random rng)
    {
        float len = Mathf.Max(1f, f.Lu * 0.8f), dep = Mathf.Max(0.8f, f.Lv * 0.6f);
        var hull = Box(root, f.Centre, f.U, len, dep, Mathf.Min(1.4f, dep), pal.Roof.Darkened(0.2f), BaseHeight);
        hull.Rotation += new Vector3(0.12f, 0, 0.18f);
        Rocks(root, new[] { f.Centre - f.U * f.Lu / 2 - f.V * f.Lv / 2, f.Centre + f.U * f.Lu / 2 - f.V * f.Lv / 2, f.Centre + f.U * f.Lu / 2 + f.V * f.Lv / 2, f.Centre - f.U * f.Lu / 2 + f.V * f.Lv / 2 },
            0.15f, 0.15f, 0.4f, pal.Trim, rng);
    }
}
