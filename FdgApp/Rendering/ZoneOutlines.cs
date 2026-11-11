using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// Any engine zone (deployment zone, terrain piece) as plain filled polygons in table inches (x across, z
/// down the table), so a front end can draw it without knowing the zone classes. Same cases the Raylib
/// MapThumbnail draws; the Godot lobby preview and map pictures use this.
/// </summary>
public static class ZoneOutlines
{
    public static List<(float X, float Z)[]> Polygons(IZone shape)
    {
        var result = new List<(float X, float Z)[]>();
        Add(shape, result);
        return result;
    }

    private static void Add(IZone shape, List<(float X, float Z)[]> into)
    {
        switch (shape)
        {
            // Before RectangularZone: both derive from it, and that case would add their bounding box.
            case MultiRectZone multi:
                foreach (RectangularZone part in multi.Parts) Add(part, into);
                break;
            case PolygonZone poly:
                foreach (var part in poly.Vertices) into.Add(part.Select(v => (v.X, v.Y)).ToArray());
                break;
            case RectangularZone r:
                into.Add(new[] { (r.Left, r.Top), (r.Right, r.Top), (r.Right, r.Bottom), (r.Left, r.Bottom) });
                break;
            case CircularZone c:
                const int n = 32;
                into.Add(Enumerable.Range(0, n).Select(i =>
                {
                    float a = i * MathF.Tau / n;
                    return (c.Center.X + MathF.Cos(a) * c.Radius, c.Center.Y + MathF.Sin(a) * c.Radius);
                }).ToArray());
                break;
            case CompositeZone composite:
                foreach (IZone part in composite.Parts) Add(part, into);
                break;
            case RotatedZoneWrapper rot when rot.Inner is RectangularZone rr:
            {
                float a = rot.AngleDegrees * MathF.PI / 180f, cos = MathF.Cos(a), sin = MathF.Sin(a);
                (float, float) Corner(float x, float z)
                {
                    float dx = x - rot.Pivot.X, dz = z - rot.Pivot.Y;
                    return (rot.Pivot.X + dx * cos - dz * sin, rot.Pivot.Y + dx * sin + dz * cos);
                }
                into.Add(new[] { Corner(rr.Left, rr.Bottom), Corner(rr.Right, rr.Bottom), Corner(rr.Right, rr.Top), Corner(rr.Left, rr.Top) });
                break;
            }
            case RotatedZoneWrapper other:
                foreach (IZone primitive in ((IZone)other).Primitives()) Add(primitive, into);
                break;
        }
    }
}
