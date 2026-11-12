using System.Globalization;
using FdgRaylib.Models3D;
using Godot;

namespace OprGodot.Lab;

/// <summary>What came out of a file: the node to show, and the numbers worth knowing about it.</summary>
public sealed class LoadedAsset
{
    public required string File;
    /// <summary>The model as loaded, unscaled and unturned (null for a picture).</summary>
    public Node3D? Model;
    /// <summary>The picture, for a picture file (or the first texture a model uses, for the HUD preview).</summary>
    public Texture2D? Picture;
    public bool IsPicture => Model == null && Picture != null;
    public int Triangles;
    public int Meshes;
    public int Materials;
    public readonly List<string> Textures = new();
    public readonly List<string> Animations = new();
    public readonly List<string> Warnings = new();
    public string? Error;
}

/// <summary>
/// Opens 3D models and pictures at run time, from anywhere on disk - no import step, no Godot editor needed - so a
/// file dropped into UnitModels can be looked at straight away.
/// <list type="bullet">
/// <item><b>.glb / .gltf</b> through Godot's own glTF loader (materials, textures, animations).</item>
/// <item><b>.obj</b> (what the Tabletop Simulator mod's models are) through a reader here: groups, materials from
/// the .mtl, and the skin from map_Kd - or, when there is no .mtl, a picture with the same name beside it.</item>
/// <item><b>.png / .jpg / .webp</b> as a picture.</item>
/// </list>
/// </summary>
public static class AssetLoader
{
    /// <summary>A model over this many triangles is flagged: a 100-model army of them would slow the table down.</summary>
    public const int HeavyTriangles = 60_000;

    public static LoadedAsset Load(string file)
    {
        var asset = new LoadedAsset { File = file };
        try
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (UnitModelPaths.IsImage(file))
            {
                asset.Picture = LoadTexture(file, asset);
                if (asset.Picture == null) asset.Error = "This picture could not be read.";
                else asset.Textures.Add($"{Path.GetFileName(file)} ({asset.Picture.GetWidth()} x {asset.Picture.GetHeight()})");
                return asset;
            }
            asset.Model = ext switch
            {
                ".glb" or ".gltf" => LoadGltf(file, asset),
                ".obj" => LoadObj(file, asset),
                _ => null,
            };
            if (asset.Model == null)
            {
                asset.Error ??= "This file type can't be opened here (use .glb, .gltf, .obj, .png or .jpg).";
                return asset;
            }
            Measure(asset.Model, asset);
            if (asset.Triangles == 0) asset.Warnings.Add("No triangles found - the file may be empty or use a feature this reader skips.");
            if (asset.Triangles > HeavyTriangles)
                asset.Warnings.Add($"Heavy: {asset.Triangles:N0} triangles. Fine for one hero; a big army of these will be slow - a lighter version (under {HeavyTriangles / 1000}k) is better for the table.");
        }
        catch (Exception e)
        {
            asset.Error = "Could not open this file: " + e.Message;
            asset.Model?.QueueFree();
            asset.Model = null;
        }
        return asset;
    }

    // ------------------------------------------------------------------ glTF

    private static Node3D? LoadGltf(string file, LoadedAsset asset)
    {
        var doc = new GltfDocument();
        var state = new GltfState();
        Error err = doc.AppendFromFile(file, state);
        if (err != Godot.Error.Ok)
        {
            asset.Error = $"Godot's glTF reader refused this file ({err}).";
            return null;
        }
        Node? scene = doc.GenerateScene(state);
        if (scene is not Node3D root)
        {
            scene?.QueueFree();
            asset.Error = "The file has no 3D scene in it.";
            return null;
        }
        foreach (Texture2D t in state.GetImages().OfType<Texture2D>())
            asset.Textures.Add($"{t.ResourceName} ({t.GetWidth()} x {t.GetHeight()})");
        asset.Picture = state.GetImages().OfType<Texture2D>().FirstOrDefault();
        foreach (AnimationPlayer p in All<AnimationPlayer>(root))
            asset.Animations.AddRange(p.GetAnimationList().Where(a => a != "RESET"));
        return root;
    }

    // ------------------------------------------------------------------ OBJ

    private sealed class ObjMaterial
    {
        public Color Diffuse = new(0.7f, 0.7f, 0.7f);
        public float Alpha = 1f;
        public string? DiffuseMap;
        public string? NormalMap;
    }

    private static Node3D? LoadObj(string file, LoadedAsset asset)
    {
        var pos = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var materials = new Dictionary<string, ObjMaterial>(StringComparer.OrdinalIgnoreCase);
        string dir = Path.GetDirectoryName(file) ?? ".";
        // Faces grouped by material, in the order the materials first appear.
        var groups = new List<(string Material, List<(int V, int T, int N)[]> Faces)>();
        List<(int V, int T, int N)[]> current = new();
        groups.Add(("", current));
        bool anyMtl = false;

        foreach (string raw in System.IO.File.ReadLines(file))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "v" when p.Length >= 4:
                    pos.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                    break;
                case "vt" when p.Length >= 3:
                    uvs.Add(new Vector2(F(p[1]), 1f - F(p[2]))); // OBJ counts V up from the bottom; Godot down from the top
                    break;
                case "vn" when p.Length >= 4:
                    normals.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                    break;
                case "f" when p.Length >= 4:
                    var face = new (int, int, int)[p.Length - 1];
                    for (int i = 1; i < p.Length; i++)
                    {
                        string[] k = p[i].Split('/');
                        face[i - 1] = (Index(k[0], pos.Count), k.Length > 1 && k[1].Length > 0 ? Index(k[1], uvs.Count) : -1,
                            k.Length > 2 && k[2].Length > 0 ? Index(k[2], normals.Count) : -1);
                    }
                    current.Add(face);
                    break;
                case "usemtl" when p.Length >= 2:
                    string name = line[6..].Trim();
                    var existing = groups.FirstOrDefault(g => g.Material.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing.Faces != null) current = existing.Faces;
                    else { current = new(); groups.Add((name, current)); }
                    break;
                case "mtllib" when p.Length >= 2:
                    string lib = Path.Combine(dir, line[6..].Trim());
                    if (System.IO.File.Exists(lib)) { ReadMtl(lib, materials); anyMtl = true; }
                    else asset.Warnings.Add($"The model names a material file it doesn't come with ({Path.GetFileName(lib)}).");
                    break;
            }
        }
        if (pos.Count == 0) { asset.Error = "No points in this OBJ file."; return null; }

        // No .mtl skin: the Tabletop Simulator models ship as Name.obj + Name.png (or .jpg).
        string? looseSkin = null;
        if (!materials.Values.Any(m => m.DiffuseMap != null))
        {
            string stem = Path.Combine(dir, Path.GetFileNameWithoutExtension(file));
            looseSkin = UnitModelPaths.ImageExtensions.Select(e => stem + e).FirstOrDefault(System.IO.File.Exists)
                ?? UnitModelPaths.ImageExtensions.SelectMany(e => new[] { stem + "_diffuse" + e, stem + "_d" + e, stem + "_albedo" + e }).FirstOrDefault(System.IO.File.Exists);
            if (looseSkin == null) asset.Warnings.Add(anyMtl
                ? "No skin picture named in the material file - shown in plain grey."
                : $"No skin picture found - put one beside it named {Path.GetFileNameWithoutExtension(file)}.png to see it painted.");
        }

        var mesh = new ArrayMesh();
        var textures = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (matName, faces) in groups)
        {
            if (faces.Count == 0) continue;
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            bool hasNormals = faces.All(f => f.All(c => c.N >= 0));
            foreach (var face in faces)
            {
                // A polygon as a fan; Godot's front faces wind the other way round from OBJ's.
                for (int i = 1; i + 1 < face.Length; i++)
                    foreach (var c in new[] { face[0], face[i + 1], face[i] })
                    {
                        if (c.T >= 0) st.SetUV(uvs[c.T]);
                        if (hasNormals) st.SetNormal(normals[c.N]);
                        st.AddVertex(pos[c.V]);
                    }
            }
            if (!hasNormals) st.GenerateNormals();
            materials.TryGetValue(matName, out ObjMaterial? om);
            var mat = new StandardMaterial3D { Roughness = 0.75f, AlbedoColor = om?.Diffuse ?? new Color(0.7f, 0.7f, 0.7f) };
            string? skin = om?.DiffuseMap != null ? Path.Combine(dir, om.DiffuseMap) : looseSkin;
            if (skin != null)
            {
                if (!textures.TryGetValue(skin, out Texture2D? tex)) textures[skin] = tex = LoadTexture(skin, asset);
                if (tex != null)
                {
                    mat.AlbedoTexture = tex;
                    mat.AlbedoColor = Colors.White with { A = om?.Alpha ?? 1f };
                }
            }
            if (om?.NormalMap is { } nm && LoadTexture(Path.Combine(dir, nm), asset) is { } ntex)
            {
                mat.NormalEnabled = true;
                mat.NormalTexture = ntex;
            }
            if ((om?.Alpha ?? 1f) < 1f) mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            st.SetMaterial(mat);
            st.Commit(mesh);
        }
        foreach (var (path, tex) in textures)
            if (tex != null) asset.Textures.Add($"{Path.GetFileName(path)} ({tex.GetWidth()} x {tex.GetHeight()})");
        asset.Picture = textures.Values.FirstOrDefault(t => t != null);
        var root = new Node3D { Name = Path.GetFileNameWithoutExtension(file) };
        root.AddChild(new MeshInstance3D { Mesh = mesh, Name = "Mesh" });
        return root;
    }

    private static void ReadMtl(string path, Dictionary<string, ObjMaterial> into)
    {
        ObjMaterial? m = null;
        foreach (string raw in System.IO.File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string rest = line[p[0].Length..].Trim();
            switch (p[0].ToLowerInvariant())
            {
                case "newmtl": into[rest] = m = new ObjMaterial(); break;
                case "kd" when m != null && p.Length >= 4: m.Diffuse = new Color(F(p[1]), F(p[2]), F(p[3])); break;
                case "d" when m != null && p.Length >= 2: m.Alpha = F(p[1]); break;
                case "tr" when m != null && p.Length >= 2: m.Alpha = 1f - F(p[1]); break;
                // Options (-bm 1, -s ...) come before the file name; the name is the last word(s) that exist as a file.
                case "map_kd" when m != null: m.DiffuseMap = MapFile(rest); break;
                case "map_bump" or "bump" or "norm" when m != null: m.NormalMap = MapFile(rest); break;
            }
        }
        static string MapFile(string rest)
        {
            string[] w = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            while (i < w.Length - 1 && w[i].StartsWith('-')) i += w[i] == "-bm" || w[i] == "-blendu" || w[i] == "-blendv" || w[i] == "-clamp" ? 2 : 4;
            return string.Join(' ', w.Skip(Math.Min(i, w.Length - 1)));
        }
    }

    private static int Index(string s, int count)
    {
        int i = int.Parse(s, CultureInfo.InvariantCulture);
        return i < 0 ? count + i : i - 1; // OBJ counts from 1; negative counts back from the end
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ pictures and numbers

    private static Texture2D? LoadTexture(string file, LoadedAsset asset)
    {
        if (!System.IO.File.Exists(file))
        {
            asset.Warnings.Add($"Skin picture missing: {Path.GetFileName(file)}.");
            return null;
        }
        var img = Image.LoadFromFile(file);
        if (img == null || img.IsEmpty()) { asset.Warnings.Add($"Could not read the picture {Path.GetFileName(file)}."); return null; }
        if (Math.Max(img.GetWidth(), img.GetHeight()) > 4096)
            asset.Warnings.Add($"{Path.GetFileName(file)} is {img.GetWidth()} x {img.GetHeight()} - larger than a table model needs (2048 is plenty).");
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static void Measure(Node3D root, LoadedAsset asset)
    {
        var mats = new HashSet<Material>();
        foreach (MeshInstance3D mi in All<MeshInstance3D>(root))
        {
            if (mi.Mesh == null) continue;
            asset.Meshes++;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = mi.Mesh.SurfaceGetArrays(s);
                var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                asset.Triangles += (idx.Length > 0 ? idx.Length : verts.Length) / 3;
                if ((mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s)) is { } m) mats.Add(m);
            }
        }
        asset.Materials = mats.Count;
    }

    public static IEnumerable<T> All<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (Node c in n.GetChildren())
            foreach (T x in All<T>(c)) yield return x;
    }

    /// <summary>The box round every mesh under <paramref name="root"/>, in <paramref name="space"/>'s own coordinates.</summary>
    public static Aabb? Bounds(Node3D root, Node3D space)
    {
        Aabb? box = null;
        Transform3D toSpace = space.GlobalTransform.AffineInverse();
        foreach (MeshInstance3D mi in All<MeshInstance3D>(root))
        {
            if (mi.Mesh == null || !mi.Visible) continue;
            Aabb b = (toSpace * mi.GlobalTransform) * mi.GetAabb();
            box = box is { } x ? x.Merge(b) : b;
        }
        return box;
    }
}
