using FdgRaylib.Models3D;
using Godot;

namespace OprGodot.Lab;

/// <summary>
/// A printable model brought into the game once (Kai, 2026-11-15): its figure STL and, if wanted, its base STL,
/// made light enough for the table and saved as one .glb in the unit's folder - where Army Forge's looks find it, and from where it travels to other players like any model.
///
/// <para><b>Why convert at all.</b> A printable figure is 1-2 million triangles (a 50-90 MB file). Opening one took
/// most of a minute, every time. Converted, it is ~100,000 triangles and opens at once.</para>
///
/// <para><b>Kits line up already.</b> A maker's figure and base files share one position (checked on Archvillain's
/// Spacehorde, 2026-11-15) - so they are simply put together, and the base, a flat disc, tells which way is up.</para>
/// </summary>
public sealed class StlKit
{
    /// <summary>Triangles kept for the figure and for the base: plenty for a close-up, cheap on the table.</summary>
    public const int FigureTriangles = 90_000, BaseTriangles = 15_000;

    public required string FigureFile;
    public string? BaseFile;
    /// <summary>The light model, figure then base, Y up, in millimetres (STL wound: anticlockwise from outside).</summary>
    public required StlMesh Light;
    /// <summary>0 = figure, 1 = base, for each triangle of <see cref="Light"/>.</summary>
    public required int[] PieceOfTriangle;
    public int FullTriangles;
    /// <summary>The base piece's width, mm (0 with no base piece).</summary>
    public float BaseWidthMm;
    public float HeightMm;

    /// <summary>The kit's base files beside a figure: STL files with "base" in their name, in its folder or below.</summary>
    public static List<string> BasesBeside(string figure)
    {
        string? dir = Path.GetDirectoryName(figure);
        if (dir == null || !Directory.Exists(dir)) return new();
        return Directory.EnumerateFiles(dir, "*.stl", SearchOption.AllDirectories)
            .Where(f => Path.GetFileNameWithoutExtension(f).Contains("base", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f, figure, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The figures in a kit folder: STL files without "base" in their name.</summary>
    public static bool IsBaseFile(string file) => Path.GetFileNameWithoutExtension(file).Contains("base", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads and lightens the kit. Slow (tens of seconds for a big sculpt) - call it off the main thread.</summary>
    public static StlKit Prepare(string figureFile, string? baseFile, Action<string>? progress = null)
    {
        progress?.Invoke("Reading the figure...");
        StlMesh figure = StlMesh.Read(figureFile, standUp: baseFile == null);
        StlMesh? basePiece = null;
        if (baseFile != null)
        {
            progress?.Invoke("Reading the base...");
            basePiece = StlMesh.Read(baseFile, standUp: false);
            if (StlMesh.KitUp(basePiece) == 'Z') { figure.TurnZUp(); basePiece.TurnZUp(); }
        }
        int full = figure.Triangles + (basePiece?.Triangles ?? 0);

        progress?.Invoke($"Making the figure lighter ({figure.Triangles:N0} triangles)...");
        StlMesh lightFigure = Lighten(figure, FigureTriangles);
        StlMesh? lightBase = null;
        if (basePiece != null)
        {
            progress?.Invoke("Making the base lighter...");
            lightBase = Lighten(basePiece, BaseTriangles);
        }

        // Figure first, then base.
        var pos = new List<float>(lightFigure.Positions);
        var idx = new List<int>(lightFigure.Indices);
        var piece = new List<int>(Enumerable.Repeat(0, lightFigure.Triangles));
        if (lightBase != null)
        {
            int offset = pos.Count / 3;
            pos.AddRange(lightBase.Positions);
            idx.AddRange(lightBase.Indices.Select(i => i + offset));
            piece.AddRange(Enumerable.Repeat(1, lightBase.Triangles));
        }
        var light = new StlMesh { Positions = pos.ToArray(), Indices = idx.ToArray() };

        var pieceArr = piece.ToArray();
        var b = light.Bounds();
        float baseWidth = 0;
        if (lightBase != null)
        {
            var bb = lightBase.Bounds();
            baseWidth = Math.Max(bb.X1 - bb.X0, bb.Z1 - bb.Z0);
        }
        return new StlKit
        {
            FigureFile = figureFile, BaseFile = baseFile, Light = light, PieceOfTriangle = pieceArr,
            FullTriangles = full, BaseWidthMm = baseWidth, HeightMm = b.Y1 - b.Y0,
        };
    }

    /// <summary>
    /// A copy under <paramref name="target"/> triangles, by Godot's own simplifier (the one it uses for distance
    /// copies, which keeps the surface whole). Already light enough: returned as it is.
    /// </summary>
    public static StlMesh Lighten(StlMesh m, int target)
    {
        if (m.Triangles <= target) return m;
        var verts = new Vector3[m.Corners];
        for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
        Vector3[] normals = SmoothNormals(m.Positions, m.Indices);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = m.Indices;
        var im = new ImporterMesh();
        im.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        im.GenerateLods(60f, 0f, new Godot.Collections.Array());

        int[]? pick = null;
        for (int l = 0; l < im.GetSurfaceLodCount(0); l++)
        {
            int[] lod = im.GetSurfaceLodIndices(0, l);
            pick = lod;
            if (lod.Length / 3 <= target) break;
        }
        if (pick == null || pick.Length == 0) return m;

        // Keep only the corners the lighter copy uses.
        var remap = new Dictionary<int, int>();
        var pos = new List<float>();
        var idx = new int[pick.Length];
        for (int i = 0; i < pick.Length; i++)
        {
            int v = pick[i];
            if (!remap.TryGetValue(v, out int nv))
            {
                remap[v] = nv = pos.Count / 3;
                pos.Add(m.Positions[v * 3]); pos.Add(m.Positions[v * 3 + 1]); pos.Add(m.Positions[v * 3 + 2]);
            }
            idx[i] = nv;
        }
        return new StlMesh { Positions = pos.ToArray(), Indices = idx };
    }

    /// <summary>Normals at each corner, from the triangles round it (bigger triangles count more). STL winding.</summary>
    public static Vector3[] SmoothNormals(float[] pos, int[] idx)
    {
        var n = new Vector3[pos.Length / 3];
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            var pa = new Vector3(pos[a * 3], pos[a * 3 + 1], pos[a * 3 + 2]);
            var pb = new Vector3(pos[b * 3], pos[b * 3 + 1], pos[b * 3 + 2]);
            var pc = new Vector3(pos[c * 3], pos[c * 3 + 1], pos[c * 3 + 2]);
            Vector3 f = (pb - pa).Cross(pc - pa); // length = twice the area: the weighting
            n[a] += f; n[b] += f; n[c] += f;
        }
        for (int i = 0; i < n.Length; i++) n[i] = n[i].LengthSquared() > 0 ? n[i].Normalized() : Vector3.Up;
        return n;
    }

    /// <summary>The model as one plain grey mesh, ready to show or save.</summary>
    public Node3D Build()
    {
        Vector3[] normals = SmoothNormals(Light.Positions, Light.Indices);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int v = 0; v < Light.Corners; v++)
        {
            st.SetNormal(normals[v]);
            st.AddVertex(new Vector3(Light.Positions[v * 3], Light.Positions[v * 3 + 1], Light.Positions[v * 3 + 2]));
        }
        // STL lists corners anticlockwise from outside; Godot's front faces are clockwise.
        for (int t = 0; t < Light.Indices.Length; t += 3)
        {
            st.AddIndex(Light.Indices[t]);
            st.AddIndex(Light.Indices[t + 2]);
            st.AddIndex(Light.Indices[t + 1]);
        }
        st.SetMaterial(new StandardMaterial3D { ResourceName = "Plain", AlbedoColor = AssetLoader.StlGrey, Roughness = 0.7f });
        var root = new Node3D { Name = UnitModelMap.SafeName(Path.GetFileNameWithoutExtension(FigureFile)) };
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), Name = "Model" });
        return root;
    }

    /// <summary>
    /// Saves the model as a .glb at <paramref name="glbPath"/>, with its settings beside it: the base size the base
    /// piece is (snapped to a usual size), the height the kit stands, already upright. Returns an error, or null.
    /// </summary>
    public string? Save(string glbPath)
    {
        Node3D root = Build();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);
            var doc = new GltfDocument();
            var state = new GltfState();
            Error err = doc.AppendFromScene(root, state);
            if (err == Error.Ok) err = doc.WriteToFilesystem(state, glbPath);
            if (err != Error.Ok) return $"Could not write the model ({err}).";

            float baseMm = BaseWidthMm > 0
                ? UnitModelSettings.RoundBasesMm.OrderBy(mm => Math.Abs(mm - BaseWidthMm)).First()
                : 25;
            new UnitModelSettings
            {
                BaseMm = baseMm,
                HeightMm = (float)Math.Round(HeightMm * 2, MidpointRounding.AwayFromZero) / 2,
                UpAxis = EUpAxis.YUp,
                CentreOnBase = true,
                Notes = $"Imported from {Path.GetFileName(FigureFile)}" + (BaseFile != null ? $" + {Path.GetFileName(BaseFile)}" : "")
                        + $" ({FullTriangles:N0} triangles -> {Light.Triangles:N0}).",
            }.Save(glbPath);
            return null;
        }
        catch (Exception e) { return "Could not save: " + e.Message; }
        finally { root.QueueFree(); }
    }
}
