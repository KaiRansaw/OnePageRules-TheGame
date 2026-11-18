using FdgRaylib.Models3D;
using Godot;
using OprGodot.Lab;

namespace OprGodot.Battle;

/// <summary>
/// Kai, 2026-11-18: "it looks like the terrain is on a base" - cut them off. Some Warhammer terrain models (the woods
/// above all) were sculpted standing on a round scenic base, the way the plastic kit is, and it showed as a dark slab
/// under the trees. When a terrain model loads, this finds such a base and stops drawing it.
/// <para>
/// A base is told apart from ground-hugging pieces (a crater, a ruin floor) by its shape: a wide, flat, upward-facing
/// layer at the very bottom, with most of the model standing well above it. A crater is mostly low, so it is left
/// whole. Only the triangle list changes - every vertex stays in the mesh, so the model's bounding box, and with it the
/// footprint, the measured size and the scaling onto a map piece, are exactly what they were. The file on disk is not
/// touched.
/// </para>
/// </summary>
public static class TerrainBaseTrim
{
    /// <summary>The flat layer must lie in the lowest part of the model...</summary>
    private const float SearchBand = 0.2f;
    /// <summary>...cover this much of the model's ground box from above...</summary>
    private const float MinCover = 0.3f;
    /// <summary>...and this share of the model's surface must stand above it.</summary>
    private const float MinAbove = 0.3f;

    /// <summary>Trims the base off <paramref name="model"/> in place. True when it found one.</summary>
    public static bool Apply(Node3D model, EUpAxis up, bool? force = null)
    {
        if (force == false) return false;
        Basis upBasis = ModelPlacement.UpBasis(up);
        var meshes = new List<(MeshInstance3D Mi, Transform3D ToUp)>();
        foreach (MeshInstance3D mi in AssetLoader.All<MeshInstance3D>(model))
            if (mi.Mesh is ArrayMesh) meshes.Add((mi, new Transform3D(upBasis, Vector3.Zero) * RelativeTo(model, mi)));
        if (meshes.Count == 0) { Debug("no ArrayMesh in " + string.Join(",", AssetLoader.All<MeshInstance3D>(model).Select(m => m.Mesh?.GetType().Name))); return false; }

        // Every triangle in "standing up" space.
        var tris = new List<(int Mesh, int Surface, Vector3 A, Vector3 B, Vector3 C)>();
        int vertexCount = 0;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        var allY = new List<float>();
        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = (ArrayMesh)meshes[m].Mi.Mesh;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if (mesh.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
                var arrays = mesh.SurfaceGetArrays(s);
                Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] idx = Indices(arrays, v.Length);
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    w[i] = meshes[m].ToUp * v[i];
                    minY = Math.Min(minY, w[i].Y); maxY = Math.Max(maxY, w[i].Y);
                    minX = Math.Min(minX, w[i].X); maxX = Math.Max(maxX, w[i].X);
                    minZ = Math.Min(minZ, w[i].Z); maxZ = Math.Max(maxZ, w[i].Z);
                    allY.Add(w[i].Y);
                }
                vertexCount += v.Length;
                for (int t = 0; t + 2 < idx.Length; t += 3) tris.Add((m, s, w[idx[t]], w[idx[t + 1]], w[idx[t + 2]]));
            }
        }
        float height = maxY - minY;
        float groundArea = (maxX - minX) * (maxZ - minZ);
        if (height < 1e-4f || groundArea < 1e-6f || vertexCount == 0) return false;

        // Where does the upward-facing area pile up in the bottom of the model? That height is the base's top.
        const int Bins = 40;
        var areaAt = new float[Bins];
        float band = height * SearchBand;
        foreach (var (_, _, a, b, c) in tris)
        {
            Vector3 n = (b - a).Cross(c - a);
            float area2 = n.Length();
            if (area2 < 1e-9f || Math.Abs(n.Y) / area2 < 0.85f) continue; // flat faces (either winding - Godot's front face is clockwise)
            float y = (a.Y + b.Y + c.Y) / 3f - minY;
            if (y > band) continue;
            areaAt[Math.Min(Bins - 1, (int)(y / band * Bins))] += area2 / 2f;
        }
        // A base is a slab: a flat underside at the very bottom AND a flat top a little above it, both wide. (A crater
        // or a ruin floor is one open surface - no underside - so it is left whole.) Its top is the highest strong layer.
        float peak = areaAt.Max();
        int best = Array.FindLastIndex(areaAt, x => x >= 0.5f * peak);
        float cover = MinCover * groundArea;
        bool slab = best > 0 && areaAt[0] >= cover && areaAt[best] >= cover;
        float top = minY + (best + 1) * band / Bins + height * 0.01f;
        float above = 0, total = 0;
        foreach (var (_, _, a, b, c) in tris)
        {
            float area = (b - a).Cross(c - a).Length() / 2f;
            total += area;
            if (Math.Max(a.Y, Math.Max(b.Y, c.Y)) > top) above += area;
        }
        if (force != true)
        {
            if (!slab) { Debug($"no slab (bottom {areaAt[0]:0.##}, top {(best < 0 ? 0 : areaAt[best]):0.##}, need {cover:0.##})"); return false; }
            if (above < MinAbove * total) { Debug($"only {above / total:P0} of the surface above - kept"); return false; }
        }
        else if (best < 0) return false;

        // Drop every triangle lying wholly in the base layer.
        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = (ArrayMesh)meshes[m].Mi.Mesh;
            var trimmed = new ArrayMesh();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.SurfaceGetArrays(s);
                if (mesh.SurfaceGetPrimitiveType(s) == Mesh.PrimitiveType.Triangles)
                {
                    Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    int[] idx = Indices(arrays, v.Length);
                    var keep = new List<int>(idx.Length);
                    for (int t = 0; t + 2 < idx.Length; t += 3)
                    {
                        Vector3 a = meshes[m].ToUp * v[idx[t]], b = meshes[m].ToUp * v[idx[t + 1]], c = meshes[m].ToUp * v[idx[t + 2]];
                        if (a.Y <= top && b.Y <= top && c.Y <= top) continue;
                        keep.Add(idx[t]); keep.Add(idx[t + 1]); keep.Add(idx[t + 2]);
                    }
                    if (keep.Count == 0) keep.AddRange(new[] { 0, 0, 0 }); // a degenerate triangle keeps the surface (and its box)
                    arrays[(int)Mesh.ArrayType.Index] = keep.ToArray();
                }
                trimmed.AddSurfaceFromArrays(mesh.SurfaceGetPrimitiveType(s), arrays);
                trimmed.SurfaceSetMaterial(s, mesh.SurfaceGetMaterial(s));
            }
            meshes[m].Mi.Mesh = trimmed;
        }
        Debug($"trimmed below {top - minY:0.###} of {height:0.##} tall, {tris.Count} triangles");
        return true;
    }

    /// <summary>Set by ScriptedRun's trimshot to see why a model was or wasn't trimmed.</summary>
    public static bool Verbose;
    private static void Debug(string why) { if (Verbose) GD.Print("TerrainBaseTrim: " + why); }

    private static int[] Indices(Godot.Collections.Array arrays, int vertexCount)
    {
        Variant ix = arrays[(int)Mesh.ArrayType.Index];
        if (ix.VariantType != Variant.Type.Nil)
        {
            int[] idx = ix.AsInt32Array();
            if (idx.Length > 0) return idx;
        }
        return Enumerable.Range(0, vertexCount).ToArray();
    }

    /// <summary>The node's transform relative to <paramref name="root"/>, without needing either in the scene tree.</summary>
    private static Transform3D RelativeTo(Node3D root, Node3D node)
    {
        Transform3D t = Transform3D.Identity;
        for (Node? n = node; n != null && n != root; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }
}
