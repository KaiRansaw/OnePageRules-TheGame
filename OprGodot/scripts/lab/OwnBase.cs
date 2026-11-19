using FdgRaylib.Models3D;
using Godot;
using Num = System.Numerics;

namespace OprGodot.Lab;

/// <summary>
/// Hides the base a unit model was sculpted on, so it stands on the game's own base - the Army Forge size the rules
/// measure from (Kai, 2026-11-19, option A). <see cref="OwnBaseFinder"/> finds the base from the model's shape; this
/// hands it the triangles and cuts a copy of the model: every triangle wholly inside the base goes, and anything
/// that dipped into it is flattened to the cut, so the model's box starts at its feet and
/// <see cref="ModelPlacement.Place"/> stands those feet on the game's base. The file on disk is not touched.
/// </summary>
public static class OwnBase
{
    /// <summary>The result: the model to use (the original when nothing was cut) and what was found.</summary>
    public readonly record struct Result(Node3D Model, OwnBaseFinder.Found? Found, float CutMm);

    /// <summary>
    /// A copy of <paramref name="model"/> with its own base cut off, as <paramref name="s"/> says
    /// (<see cref="UnitModelSettings.OwnBaseMm"/>). Returns the original node, unchanged, when there is nothing to cut.
    /// </summary>
    public static Result Strip(Node3D model, UnitModelSettings s)
    {
        if (s.OwnBaseMm == 0) return new Result(model, null, 0);
        var stand = new Transform3D(ModelPlacement.StandBasis(s), Vector3.Zero);
        List<(MeshInstance3D Mi, Transform3D ToUp)> meshes = Meshes(model, stand);
        if (meshes.Count == 0) return new Result(model, null, 0);

        var tris = new List<OwnBaseFinder.Tri>();
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var (mi, toUp) in meshes)
        {
            var mesh = (ArrayMesh)mi.Mesh;
            Basis nb = toUp.Basis.Inverse().Transposed();
            for (int si = 0; si < mesh.GetSurfaceCount(); si++)
            {
                if (mesh.SurfaceGetPrimitiveType(si) != Mesh.PrimitiveType.Triangles) continue;
                var arrays = mesh.SurfaceGetArrays(si);
                Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector3[] nrm = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? Array.Empty<Vector3>() : arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                int[] idx = Indices(arrays, v.Length);
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    w[i] = toUp * v[i];
                    minY = Mathf.Min(minY, w[i].Y);
                    maxY = Mathf.Max(maxY, w[i].Y);
                }
                bool hasN = nrm.Length == v.Length;
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                    float up = float.NaN;
                    if (hasN) up = (nb * (nrm[a] + nrm[b] + nrm[c])).Normalized().Y;
                    tris.Add(new OwnBaseFinder.Tri(N(w[a]), N(w[b]), N(w[c]), up));
                }
            }
        }
        if (tris.Count == 0) return new Result(model, null, 0);
        // Inches per file unit at the model's real size (its saved height, before the size slider).
        float h = Mathf.Max(maxY - minY, 1e-4f);
        float k = s.HeightMm > 0 ? s.HeightMm / UnitModelSettings.MmPerInch / h : 1f;

        OwnBaseFinder.Found? found = OwnBaseFinder.Find(tris, k);
        float cutInches = s.OwnBaseMm > 0 ? s.OwnBaseMm / UnitModelSettings.MmPerInch : found?.CutInches ?? 0;
        if (cutInches <= 0 || cutInches >= h * k * 0.9f) return new Result(model, found, 0);
        float cutY = minY + cutInches / k;

        var copy = (Node3D)model.Duplicate();
        foreach (var (mi, toUp) in Meshes(copy, stand)) Cut(mi, toUp, cutY);
        return new Result(copy, found, cutInches * UnitModelSettings.MmPerInch);
    }

    /// <summary>The mesh with every triangle below <paramref name="cutY"/> (standing-up space) dropped and the rest
    /// flattened onto it - so nothing, not even an unused vertex, is left below the cut.</summary>
    private static void Cut(MeshInstance3D mi, Transform3D toUp, float cutY)
    {
        var mesh = (ArrayMesh)mi.Mesh;
        Transform3D back = toUp.AffineInverse();
        var cut = new ArrayMesh();
        var overrides = new List<Material?>();
        for (int si = 0; si < mesh.GetSurfaceCount(); si++)
        {
            var arrays = mesh.SurfaceGetArrays(si);
            var prim = mesh.SurfaceGetPrimitiveType(si);
            if (prim == Mesh.PrimitiveType.Triangles)
            {
                Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] idx = Indices(arrays, v.Length);
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) w[i] = toUp * v[i];
                var keep = new List<int>(idx.Length);
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    if (w[idx[t]].Y <= cutY && w[idx[t + 1]].Y <= cutY && w[idx[t + 2]].Y <= cutY) continue;
                    keep.Add(idx[t]); keep.Add(idx[t + 1]); keep.Add(idx[t + 2]);
                }
                if (keep.Count == 0) continue; // the whole surface was base
                var flat = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                    flat[i] = w[i].Y < cutY ? back * new Vector3(w[i].X, cutY, w[i].Z) : v[i];
                arrays[(int)Mesh.ArrayType.Vertex] = flat;
                arrays[(int)Mesh.ArrayType.Index] = keep.ToArray();
            }
            cut.AddSurfaceFromArrays(prim, arrays);
            cut.SurfaceSetMaterial(cut.GetSurfaceCount() - 1, mesh.SurfaceGetMaterial(si));
            overrides.Add(mi.GetSurfaceOverrideMaterial(si));
        }
        mi.Mesh = cut;
        for (int i = 0; i < overrides.Count; i++)
            if (overrides[i] != null) mi.SetSurfaceOverrideMaterial(i, overrides[i]);
    }

    /// <summary>The model's plain (not skinned, not morphing) meshes, each with its transform into standing-up space.</summary>
    private static List<(MeshInstance3D, Transform3D)> Meshes(Node3D model, Transform3D stand)
    {
        var list = new List<(MeshInstance3D, Transform3D)>();
        foreach (MeshInstance3D mi in AssetLoader.All<MeshInstance3D>(model))
        {
            if (mi.Mesh is not ArrayMesh am || am.GetBlendShapeCount() > 0 || mi.Skin != null) continue;
            list.Add((mi, stand * model.Transform * RelativeTo(model, mi)));
        }
        return list;
    }

    private static Num.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

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

    private static Transform3D RelativeTo(Node3D root, Node3D node)
    {
        Transform3D t = Transform3D.Identity;
        for (Node? n = node; n != null && n != root; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }
}
