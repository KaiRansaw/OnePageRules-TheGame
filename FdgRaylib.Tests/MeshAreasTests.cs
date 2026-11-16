using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-15: printable models are coloured by areas the game finds from the shape.</summary>
[TestFixture]
public class MeshAreasTests
{
    // A box, as corners and triangles, offset by (ox, oy, oz).
    private static void Box(List<float> pos, List<int> idx, float ox, float oy, float oz, float size)
    {
        int b = pos.Count / 3;
        float[][] c =
        {
            new[] { 0f, 0, 0 }, new[] { 1f, 0, 0 }, new[] { 1f, 1, 0 }, new[] { 0f, 1, 0 },
            new[] { 0f, 0, 1 }, new[] { 1f, 0, 1 }, new[] { 1f, 1, 1 }, new[] { 0f, 1, 1 },
        };
        foreach (var p in c) { pos.Add(ox + p[0] * size); pos.Add(oy + p[1] * size); pos.Add(oz + p[2] * size); }
        int[] f = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
        foreach (int i in f) idx.Add(b + i);
    }

    [Test]
    public void ABox_IsSixFlatAreas_AndCanBeOne()
    {
        var pos = new List<float>(); var idx = new List<int>();
        Box(pos, idx, 0, 0, 0, 10);
        MeshAreas m = MeshAreas.Find(pos.ToArray(), idx.ToArray());
        Assert.That(m.MaxAreas, Is.EqualTo(6), "each face of a box is flat; the edges between them are 90 degrees");
        m.AreasFor(6, out float[] six);
        Assert.That(six.Length, Is.EqualTo(6));
        int[] one = m.AreasFor(1, out float[] all);
        Assert.That(all.Length, Is.EqualTo(1));
        Assert.That(one.Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AKitsBase_NeverMergesIntoTheFigure()
    {
        var pos = new List<float>(); var idx = new List<int>();
        Box(pos, idx, 0, 0, 0, 10);   // the "base"
        Box(pos, idx, 2, 10, 2, 6);   // the "figure", standing on it
        var piece = Enumerable.Range(0, idx.Count / 3).Select(t => t < 12 ? 1 : 0).ToArray();
        MeshAreas m = MeshAreas.Find(pos.ToArray(), idx.ToArray(), piece);
        Assert.That(m.MinAreas, Is.EqualTo(2));
        int[] areas = m.AreasFor(1, out _);
        Assert.That(areas.Take(12).Distinct().Count(), Is.EqualTo(1));
        Assert.That(areas.Skip(12).Distinct().Count(), Is.EqualTo(1));
        Assert.That(areas[0], Is.Not.EqualTo(areas[12]));
        Assert.That(areas[0], Is.EqualTo(0), "the bigger piece (the base here) is area 0 - biggest first");
    }

    [Test]
    public void ALooseShellTouchingTheModel_CanMergeIntoIt()
    {
        // A strap modelled as its own box, touching the figure - same piece, no shared edge. (Touching at a corner:
        // shells are linked where their points meet, which on a real sculpt's dense surface is anywhere they touch.)
        var pos = new List<float>(); var idx = new List<int>();
        Box(pos, idx, 0, 0, 0, 10);
        Box(pos, idx, 10, 0, 0, 2);
        MeshAreas m = MeshAreas.Find(pos.ToArray(), idx.ToArray());
        Assert.That(m.MinAreas, Is.EqualTo(1), "the strap touches the figure, so it can join it");
    }

    [Test]
    public void ATinyBump_MergesIntoItsNeighbour_BeforeBigFacesDo()
    {
        // A big box and a small box on its top corner. The bump is tiny, so it joins its neighbour before any two
        // of the big box's faces join: at six areas the big box still has all six faces and the bump is gone.
        var pos = new List<float>(); var idx = new List<int>();
        Box(pos, idx, 0, 0, 0, 10);
        Box(pos, idx, 9, 10, 9, 1);
        MeshAreas m = MeshAreas.Find(pos.ToArray(), idx.ToArray());
        int[] areas = m.AreasFor(6, out float[] shares);
        Assert.That(shares.Length, Is.EqualTo(6));
        int bump = areas[12];
        Assert.That(areas.Take(12).Contains(bump), "the bump joined one of the big box's faces");
    }
}
