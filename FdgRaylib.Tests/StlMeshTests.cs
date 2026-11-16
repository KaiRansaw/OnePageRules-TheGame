using System.Text;
using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-15: printable miniatures come as STL files - read them, stand them up, make a lighter copy.</summary>
[TestFixture]
public class StlMeshTests
{
    // A 10mm cube on the floor: 12 triangles, 8 corners, Z up.
    private static readonly float[][] Cube =
    {
        new float[] { 0, 0, 0 }, new float[] { 10, 0, 0 }, new float[] { 10, 10, 0 }, new float[] { 0, 10, 0 },
        new float[] { 0, 0, 10 }, new float[] { 10, 0, 10 }, new float[] { 10, 10, 10 }, new float[] { 0, 10, 10 },
    };
    private static readonly int[] CubeFaces =
    {
        0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
    };

    private static byte[] Binary(float[][] corners, int[] faces, string header = "")
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        byte[] h = new byte[80];
        Encoding.ASCII.GetBytes(header).CopyTo(h, 0);
        w.Write(h);
        w.Write(faces.Length / 3);
        for (int t = 0; t < faces.Length; t += 3)
        {
            w.Write(0f); w.Write(0f); w.Write(0f);
            for (int k = 0; k < 3; k++) foreach (float f in corners[faces[t + k]]) w.Write(f);
            w.Write((ushort)0);
        }
        return ms.ToArray();
    }

    [Test]
    public void Binary_IsReadWelded_AndStoodYUp()
    {
        StlMesh m = StlMesh.Read(Binary(Cube, CubeFaces));
        Assert.That(m.Triangles, Is.EqualTo(12));
        Assert.That(m.Corners, Is.EqualTo(8), "shared corners are shared, not repeated per triangle");
        var b = m.Bounds();
        Assert.That(b.Y0, Is.EqualTo(0).Within(1e-5));
        Assert.That(b.Y1, Is.EqualTo(10).Within(1e-5), "the file's Z (height) becomes the game's Y (up)");
        Assert.That(b.Z0, Is.EqualTo(-10).Within(1e-5));
    }

    [Test]
    public void Binary_WhoseHeaderSaysSolid_IsStillBinary()
    {
        StlMesh m = StlMesh.Read(Binary(Cube, CubeFaces, "solid exported by some slicer"));
        Assert.That(m.Triangles, Is.EqualTo(12));
    }

    [Test]
    public void Text_IsRead()
    {
        var sb = new StringBuilder("solid cube\n");
        for (int t = 0; t < CubeFaces.Length; t += 3)
        {
            sb.Append("facet normal 0 0 0\n outer loop\n");
            for (int k = 0; k < 3; k++)
            {
                float[] c = Cube[CubeFaces[t + k]];
                sb.Append($"  vertex {c[0]} {c[1]} {c[2]}\n");
            }
            sb.Append(" endloop\nendfacet\n");
        }
        sb.Append("endsolid cube\n");
        StlMesh m = StlMesh.Read(Encoding.ASCII.GetBytes(sb.ToString()));
        Assert.That(m.Triangles, Is.EqualTo(12));
        Assert.That(m.Corners, Is.EqualTo(8));
    }

    [Test]
    public void Garbage_IsRefused() =>
        Assert.Throws<InvalidDataException>(() => StlMesh.Read(Encoding.ASCII.GetBytes("not a model at all")));
}
