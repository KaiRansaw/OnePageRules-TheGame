using FdgRaylib.Models3D;
using Godot;

namespace OprGodot.Lab;

/// <summary>
/// Puts one ForceOrg catalogue model together from its downloaded parts and saves it as a single .glb, with the
/// Asset Lab's settings file beside it.
///
/// <para><b>From Tabletop Simulator's space to Godot's.</b> TTS is Unity: left-handed, and its OBJ reader mirrors X.
/// So each part's mesh is used exactly as the file has it, and each part's place is mirrored once: position x
/// negated, the turns about Y and Z negated, the turn about X kept, applied in Unity's order (Z, then X, then Y).
/// Sizes are already right: the catalogue says 1 unit = 1 inch, which is the table's unit.</para>
///
/// <para><b>Bases.</b> Most models stand on a plain, unskinned base part. The game draws its own base, so that part
/// is left out, and its width tells the model's base size (snapped to the usual sizes).</para>
/// </summary>
public static class ForceOrgAssembler
{
    public sealed class Result
    {
        public Node3D? Root;
        public float? BaseMm;
        public int Triangles;
        public readonly List<string> Warnings = new();
        public string? Error;
    }

    public static Result Assemble(ForceOrgCatalog.Model model, IReadOnlyDictionary<string, string> files)
    {
        var r = new Result();
        var root = new Node3D { Name = UnitModelMap.SafeName(model.Name) };
        var nodes = new Node3D[model.Parts.Count];
        var textures = new Dictionary<string, Texture2D?>();
        for (int i = 0; i < model.Parts.Count; i++)
        {
            ForceOrgCatalog.Part p = model.Parts[i];
            var node = new Node3D { Name = $"Part{i}", Transform = PartTransform(p) };
            nodes[i] = node;

            if (!files.TryGetValue(p.Mesh, out string? meshFile)) { r.Warnings.Add($"part {i + 1}: mesh missing"); continue; }
            Color tint = p.Color.Length >= 3 ? new Color(p.Color[0], p.Color[1], p.Color[2]) : Colors.White;
            var mat = new StandardMaterial3D { Roughness = 0.7f, AlbedoColor = tint };
            if (!string.IsNullOrEmpty(p.Texture))
            {
                if (!textures.TryGetValue(p.Texture, out Texture2D? tex))
                    textures[p.Texture] = tex = files.TryGetValue(p.Texture, out string? tf) ? AssetLoader.TextureFromBytes(File.ReadAllBytes(tf)) : null;
                if (tex != null) mat.AlbedoTexture = tex;
                else r.Warnings.Add($"part {i + 1}: skin missing or unreadable - shown in its plain colour");
            }
            ArrayMesh? mesh;
            try { mesh = AssetLoader.ObjPartMesh(meshFile, mat); }
            catch (Exception e) { r.Warnings.Add($"part {i + 1}: mesh unreadable ({e.Message})"); continue; }
            if (mesh == null) { r.Warnings.Add($"part {i + 1}: mesh has no faces"); continue; }

            // The plain base the model stands on: a root part with no skin, flat and round-ish.
            Aabb box = mesh.GetAabb();
            bool isBase = p.Parent < 0 && string.IsNullOrEmpty(p.Texture) && box.Size.Y < 0.35f
                          && box.Size.X > 0.6f && Mathf.Abs(box.Size.X - box.Size.Z) < box.Size.X * 0.45f;
            if (isBase && r.BaseMm == null)
            {
                Vector3 s = PartScale(p);
                r.BaseMm = SnapBase(Mathf.Max(box.Size.X * Mathf.Abs(s.X), box.Size.Z * Mathf.Abs(s.Z)) * UnitModelSettings.MmPerInch);
                continue;
            }
            node.AddChild(new MeshInstance3D { Mesh = mesh, Name = "Mesh" });
            r.Triangles += mesh.GetFaces().Length / 3;
        }
        for (int i = 0; i < nodes.Length; i++)
        {
            int parent = model.Parts[i].Parent;
            if (parent >= 0 && parent < nodes.Length && parent != i) nodes[parent].AddChild(nodes[i]);
            else root.AddChild(nodes[i]);
        }
        if (r.Triangles == 0)
        {
            root.Free();
            r.Error = r.Warnings.Count > 0 ? string.Join("; ", r.Warnings) : "no mesh in any part";
            return r;
        }
        r.Root = root;
        return r;
    }

    /// <summary>Saves an assembled model as one .glb (meshes, skins and all) and writes its lab settings beside it.</summary>
    public static string? Save(Result r, string glbPath, string notes)
    {
        if (r.Root == null) return r.Error ?? "nothing to save";
        Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);
        var doc = new GltfDocument();
        var state = new GltfState();
        Error err = doc.AppendFromScene(r.Root, state);
        if (err == Godot.Error.Ok) err = doc.WriteToFilesystem(state, glbPath);
        if (err != Godot.Error.Ok) return $"could not write the .glb ({err})";
        new UnitModelSettings
        {
            HeightMm = 0, // the catalogue is already at table scale (1 unit = 1 inch)
            BaseMm = r.BaseMm ?? 25,
            UpAxis = EUpAxis.YUp,
            YawDegrees = ModelDownloadDefaults.Load().YawDegrees, // set from the facing review
            CentreOnBase = false, // parts are placed on the base already; a rider's overhang stays where it was put
            Notes = notes,
        }.Save(glbPath);
        return null;
    }

    private static Vector3 PartScale(ForceOrgCatalog.Part p) =>
        p.Scale.Length >= 3 ? new Vector3(p.Scale[0], p.Scale[1], p.Scale[2]) : Vector3.One;

    /// <summary>A part's place relative to its parent, mirrored from Unity's left-handed space (see the class note).</summary>
    public static Transform3D PartTransform(ForceOrgCatalog.Part p)
    {
        Vector3 pos = p.Pos.Length >= 3 ? new Vector3(-p.Pos[0], p.Pos[1], p.Pos[2]) : Vector3.Zero;
        float rx = p.Rot.Length >= 3 ? Mathf.DegToRad(p.Rot[0]) : 0, ry = p.Rot.Length >= 3 ? Mathf.DegToRad(p.Rot[1]) : 0,
            rz = p.Rot.Length >= 3 ? Mathf.DegToRad(p.Rot[2]) : 0;
        Basis b = new Basis(Vector3.Up, -ry) * new Basis(Vector3.Right, rx) * new Basis(new Vector3(0, 0, 1), -rz);
        return new Transform3D(b * Basis.FromScale(PartScale(p)), pos);
    }

    /// <summary>Nearest usual round base if within 3mm, else the measured size to the half millimetre.</summary>
    public static float SnapBase(float mm)
    {
        float best = UnitModelSettings.RoundBasesMm.OrderBy(b => Mathf.Abs(b - mm)).First();
        return Mathf.Abs(best - mm) <= 3f ? best : Mathf.Round(mm * 2) / 2;
    }
}
