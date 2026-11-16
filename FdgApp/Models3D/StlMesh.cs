using System.Globalization;
using System.Text;

namespace FdgRaylib.Models3D;

/// <summary>
/// An STL file read into a plain indexed triangle mesh (Kai, 2026-11-15: models for 3D printing, often a kit of
/// several pieces). STL is what printable miniatures ship as: shape only - no colour, no texture, no parts - in
/// millimetres, with no agreed "up" (see <see cref="StandUp"/>).
///
/// <para>Reading welds the corners the triangles share (an STL repeats every corner for every triangle), and turns
/// the model so Y is up, as everything else in the game is, when it can tell the file was Z-up. Sizes are left in the file's millimetres - the Asset Lab
/// sets the height the model stands at.</para>
///
/// <para>Printable models are sculpted for a 3D printer, not a game: one piece alone can be 300,000 triangles. The
/// Godot side gives a detailed one lighter copies for distance (Godot's own simplifier keeps the surface intact; a
/// simple corner-merging copy tried first left it speckled and lumpy).</para>
/// </summary>
public sealed class StlMesh
{
    /// <summary>Corner positions, x y z per corner, Y up.</summary>
    public float[] Positions = Array.Empty<float>();
    /// <summary>Three corner numbers per triangle.</summary>
    public int[] Indices = Array.Empty<int>();

    public int Triangles => Indices.Length / 3;
    public int Corners => Positions.Length / 3;

    public static StlMesh Read(string file, bool standUp = true) => Read(File.ReadAllBytes(file), standUp);

    /// <summary>
    /// Reads a binary or text STL. Throws <see cref="InvalidDataException"/> when it is neither.
    /// <paramref name="standUp"/> false leaves it exactly as the file has it - for a kit, whose pieces must all be
    /// turned the same way (decided from the base: <see cref="KitUp"/>).
    /// </summary>
    public static StlMesh Read(byte[] data, bool standUp = true)
    {
        var welder = new Welder();
        if (IsBinary(data))
        {
            int n = BitConverter.ToInt32(data, 80);
            for (int t = 0; t < n; t++)
            {
                int o = 84 + 50 * t + 12; // skip the stored normal - normals are worked out from the shape
                for (int k = 0; k < 3; k++, o += 12)
                    welder.Add(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8));
            }
        }
        else
        {
            string text = Encoding.ASCII.GetString(data);
            if (!text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Not an STL file.");
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) throw new InvalidDataException("A corner in this STL file has fewer than three numbers.");
                welder.Add(F(p[1]), F(p[2]), F(p[3]));
            }
            if (welder.Count % 3 != 0) throw new InvalidDataException("This STL file ends part-way through a triangle.");
        }
        StlMesh mesh = welder.Build();
        mesh.DropFlatTriangles();
        if (standUp) mesh.StandUp();
        return mesh;
    }

    /// <summary>
    /// Binary STL: an 80-byte header, a triangle count, then 50 bytes per triangle. Many binary files still start
    /// their header with "solid", so the size is what tells them apart.
    /// </summary>
    private static bool IsBinary(byte[] data)
    {
        if (data.Length < 84) return false;
        long n = BitConverter.ToUInt32(data, 80);
        return 84 + 50 * n == data.Length;
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Which way the file said was up: 'Y' (as the game) or 'Z' (turned to Y when read).</summary>
    public char FileUp { get; private set; } = 'Y';

    /// <summary>
    /// STL files don't say which way is up, and makers differ: a file prepared for printing (Eivor) sits on the
    /// printer's bed - its lowest point at exactly Z = 0, Z up - while others (Archvillain's kits) are saved Y-up,
    /// figure and base sharing one place. So: a model whose lowest Z is exactly 0 (and lowest Y is not) is Z-up and
    /// is turned; anything else is taken as Y-up. The Asset Lab's "Up" box fixes a wrong guess.
    /// </summary>
    private void StandUp()
    {
        var b = Bounds();
        bool onBedZ = Math.Abs(b.Z0) < 1e-4f && Math.Abs(b.Y0) > 1e-3f;
        if (onBedZ) TurnZUp();
    }

    /// <summary>
    /// Which way a kit is up, from its base: a base is a flat disc, so its thinnest side is the up axis - 'Z' or 'Y'.
    /// (A figure alone can't tell: a staff or a cloak can make it deeper than it is tall.)
    /// </summary>
    public static char KitUp(StlMesh basePiece)
    {
        var b = basePiece.Bounds();
        float dx = b.X1 - b.X0, dy = b.Y1 - b.Y0, dz = b.Z1 - b.Z0;
        return dz < dy && dz < dx ? 'Z' : 'Y';
    }

    /// <summary>Turns a Z-up model so Y is up, as the game is.</summary>
    public void TurnZUp()
    {
        FileUp = 'Z';
        for (int i = 0; i < Positions.Length; i += 3)
        {
            float y = Positions[i + 1], z = Positions[i + 2];
            Positions[i + 1] = z; // a turn, not a mirror, so the triangles keep facing outwards
            Positions[i + 2] = y == 0f ? 0f : -y;
        }
    }

    /// <summary>The smallest box round the model: (min x, min y, min z, max x, max y, max z).</summary>
    public (float X0, float Y0, float Z0, float X1, float Y1, float Z1) Bounds()
    {
        if (Positions.Length == 0) return default;
        float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
        for (int i = 0; i < Positions.Length; i += 3)
        {
            x0 = Math.Min(x0, Positions[i]); x1 = Math.Max(x1, Positions[i]);
            y0 = Math.Min(y0, Positions[i + 1]); y1 = Math.Max(y1, Positions[i + 1]);
            z0 = Math.Min(z0, Positions[i + 2]); z1 = Math.Max(z1, Positions[i + 2]);
        }
        return (x0, y0, z0, x1, y1, z1);
    }

    /// <summary>Triangles with two corners the same - common in exported STLs - draw nothing and upset normals.</summary>
    private void DropFlatTriangles()
    {
        var idx = new List<int>(Indices.Length);
        for (int t = 0; t < Indices.Length; t += 3)
        {
            int a = Indices[t], b = Indices[t + 1], c = Indices[t + 2];
            if (a == b || b == c || a == c) continue;
            idx.Add(a); idx.Add(b); idx.Add(c);
        }
        if (idx.Count != Indices.Length) Indices = idx.ToArray();
    }

    /// <summary>Shares corners that sit at exactly the same place.</summary>
    private sealed class Welder
    {
        private readonly Dictionary<(int, int, int), int> _at = new();
        private readonly List<float> _pos = new();
        private readonly List<int> _idx = new();
        public int Count => _idx.Count;

        public void Add(float x, float y, float z)
        {
            float gx = x, gy = y, gz = z;
            var key = (BitConverter.SingleToInt32Bits(gx), BitConverter.SingleToInt32Bits(gy), BitConverter.SingleToInt32Bits(gz));
            if (!_at.TryGetValue(key, out int i))
            {
                _at[key] = i = _pos.Count / 3;
                _pos.Add(gx); _pos.Add(gy); _pos.Add(gz);
            }
            _idx.Add(i);
        }

        public StlMesh Build() => new() { Positions = _pos.ToArray(), Indices = _idx.ToArray() };
    }
}
