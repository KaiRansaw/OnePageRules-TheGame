using System.Numerics;

namespace FdgRaylib.Models3D;

/// <summary>
/// Finds the base a downloaded model was sculpted on, so the table can hide it and stand the model on the game's own
/// base instead (Kai, 2026-11-19: "the models we imported from ForceOrg ... have their own bases on the models. Some of
/// them have smaller bases than the game is showing" - option A: the game base is the rules' base, from Army Forge,
/// and the model's own base goes).
///
/// <para>
/// The ForceOrg download already drops a base that is its own plain, unskinned part (<c>ForceOrgAssembler</c>). This is
/// for the rest: a painted or textured base, a base merged into the model's mesh, a printable model with its base
/// sculpted on. A base is recognised by its shape, not its name: a flat, upward-facing surface low down that covers
/// most of the round-ish footprint the model stands on. Everything below that surface is the base.
/// </para>
///
/// <para>Plain maths on triangles (no engine, no drawing), so the front end only has to hand the triangles over.</para>
/// </summary>
public static class OwnBaseFinder
{
    /// <summary>One triangle, stood up the way the model will stand (Y up), in the file's own units.</summary>
    /// <param name="UpY">How much the face points up: the Y of its unit normal (1 = flat and facing up). NaN when the
    /// file has no normals - the face's tilt is then used without knowing which side is its front.</param>
    public readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C, float UpY);

    /// <summary>What was found: where to cut, above the model's lowest point, and how wide the base is.</summary>
    public readonly record struct Found(float CutInches, float WidthMm);

    /// <summary>Thin slices the up-facing area is counted in.</summary>
    private const float Slice = 0.01f;

    /// <summary>A bottom wider than this (inches) can be a base; a pair of feet or a single foot is not.</summary>
    private const float MinBaseInches = 0.55f;

    /// <summary>
    /// The model's own base, or null when it has none (or none this can be sure of). <paramref name="inchesPerUnit"/> is
    /// how many inches one file unit is on the table at the model's real size (1 for the ForceOrg / Tabletop Simulator
    /// catalogue).
    /// </summary>
    public static Found? Find(IReadOnlyList<Tri> tris, float inchesPerUnit)
    {
        if (tris.Count == 0 || inchesPerUnit <= 0) return null;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Tri t in tris)
            foreach (Vector3 p in Corners(t))
            {
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        float height = (maxY - minY) * inchesPerUnit;
        if (height < 0.25f) return null; // a flat thing (a tile, a token) is all "base"

        // The footprint it stands on: everything within a hair of the lowest point.
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (Tri t in tris)
            foreach (Vector3 p in Corners(t))
            {
                if ((p.Y - minY) * inchesPerUnit > 0.03f) continue;
                x0 = MathF.Min(x0, p.X); x1 = MathF.Max(x1, p.X);
                z0 = MathF.Min(z0, p.Z); z1 = MathF.Max(z1, p.Z);
            }
        if (x0 > x1) return null;
        float w = (x1 - x0) * inchesPerUnit, d = (z1 - z0) * inchesPerUnit;
        // Round or oval, and base-sized - not two feet far apart, not a long thin tank track.
        if (MathF.Min(w, d) < MinBaseInches || MathF.Min(w, d) / MathF.Max(w, d) < 0.45f) return null;

        // Up-facing area, slice by slice, in the bottom of the model (a base is a few mm thick; a tall rocky one ~10).
        float band = MathF.Min(0.45f, height * 0.4f);
        int n = (int)MathF.Ceiling(band / Slice) + 1;
        var area = new float[n];
        foreach (Tri t in tris)
        {
            Vector3 a = (t.A - new Vector3(0, minY, 0)) * inchesPerUnit;
            Vector3 b = (t.B - new Vector3(0, minY, 0)) * inchesPerUnit;
            Vector3 c = (t.C - new Vector3(0, minY, 0)) * inchesPerUnit;
            float cy = (a.Y + b.Y + c.Y) / 3f;
            if (cy <= 0.03f || cy > band) continue; // the underside sits at the very bottom
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float len = cross.Length();
            if (len < 1e-9f) continue;
            float up = float.IsNaN(t.UpY) ? MathF.Abs(cross.Y) / len : t.UpY;
            if (up < 0.85f) continue;
            area[Math.Min(n - 1, (int)(cy / Slice))] += MathF.Abs(cross.Y) / 2f; // area seen from above
        }

        // The base's top: the highest few slices that together cover a good part of the footprint.
        float footprint = MathF.PI / 4f * w * d, need = footprint * 0.3f;
        const int window = 5;
        for (int i = n - 1; i >= 0; i--)
        {
            float sum = 0;
            for (int j = Math.Max(0, i - window + 1); j <= i; j++) sum += area[j];
            if (sum < need) continue;
            int best = i;
            for (int j = Math.Max(0, i - window + 1); j <= i; j++) if (area[j] > area[best]) best = j;
            float cut = (best + 1) * Slice + 0.008f;
            if (height - cut < 0.2f) return null; // nothing would be left standing on it
            return new Found(cut, MathF.Max(w, d) * UnitModelSettings.MmPerInch);
        }
        return null;
    }

    private static IEnumerable<Vector3> Corners(Tri t)
    {
        yield return t.A;
        yield return t.B;
        yield return t.C;
    }
}
