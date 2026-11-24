using FdgRaylib.Config;
using FdgRaylib.Terrain;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// The built-in terrain models (Kai, 2026-11-21: both sets in the game) and the hand-built pieces made from them
/// (<see cref="TerrainPieces"/>): the model catalogue the Terrain Workshop's palette lists, and the code that stands a
/// piece's models on a footprint - the same in the Workshop, the Map Maker and the battle, so what Kai builds is what
/// the table shows.
/// </summary>
public static class PieceModels
{
    public const string Root = "res://terrain/";

    /// <summary>A model the Workshop offers. <see cref="Path"/> is relative to <see cref="Root"/>.</summary>
    public sealed record Model(string Path, string Name, string Group, string Style);

    /// <summary>The groups each style's palette shows, with where their files are.</summary>
    private static readonly (string Style, string Group, string Folder, string Ext)[] Sources =
    {
        (TerrainPieces.Stylised, "Nature", "stylised/nature/models", ".gltf"),
        (TerrainPieces.Stylised, "Village", "stylised/village/models", ".gltf"),
        (TerrainPieces.Stylised, "Fantasy props", "stylised/props/models", ".gltf"),
        (TerrainPieces.Stylised, "Sci-fi kit", "stylised/scifi/models", ".gltf"),
        (TerrainPieces.Stylised, "City ruins", "stylised/city", ".fbx"),
        (TerrainPieces.Realistic, "Realistic", "realistic/models", ".gltf"),
    };

    private static List<Model>? _catalogue;

    /// <summary>Every model in the game's terrain folders.</summary>
    public static IReadOnlyList<Model> Catalogue => _catalogue ??= Scan();

    public static IEnumerable<string> GroupsOf(string style) =>
        style == TerrainPieces.Realistic ? RealisticGroups : Sources.Where(s => s.Style == style).Select(s => s.Group);

    // The realistic models come as one folder; the palette sorts them by what they are.
    private static readonly string[] RealisticGroups = { "Plants and trees", "Rocks", "Buildings and walls", "Props" };
    private static string RealisticGroup(string name)
    {
        string n = name.ToLowerInvariant();
        if (new[] { "tree", "trunk", "sapling", "fern", "plant", "branch", "bush", "shrub", "grass", "log", "stump", "root", "flower" }.Any(n.Contains)) return RealisticGroups[0];
        if (new[] { "rock", "boulder", "stone", "cliff", "mountain" }.Any(n.Contains)) return RealisticGroups[1];
        if (new[] { "facade", "fort", "wall", "gate", "door", "fence", "barrier", "pier", "house", "ruin", "building", "tower", "pipes" }.Any(n.Contains)) return RealisticGroups[2];
        return RealisticGroups[3];
    }

    private static List<Model> Scan()
    {
        var list = new List<Model>();
        foreach (var (style, group, folder, ext) in Sources)
        {
            using var dir = DirAccess.Open(Root + folder);
            if (dir == null) continue;
            var seen = new HashSet<string>();
            foreach (string raw in dir.GetFiles())
            {
                // An exported game lists "x.gltf.import" / ".remap" beside (or instead of) the file itself.
                string f = raw.EndsWith(".import") ? raw[..^7] : raw.EndsWith(".remap") ? raw[..^6] : raw;
                if (!f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || !seen.Add(f)) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(f).Replace('_', ' ');
                list.Add(new Model(folder + "/" + f, name, style == TerrainPieces.Realistic ? RealisticGroup(name) : group, style));
            }
        }
        return list.OrderBy(m => m.Style).ThenBy(m => m.Group).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly Dictionary<string, PackedScene?> Scenes = new();

    /// <summary>A fresh copy of a model, or null when the game doesn't have it.</summary>
    public static Node3D? Instance(string path)
    {
        if (!Scenes.TryGetValue(path, out PackedScene? scene))
        {
            scene = ResourceLoader.Exists(Root + path) ? ResourceLoader.Load<PackedScene>(Root + path) : null;
            Scenes[path] = scene;
        }
        return scene?.Instantiate<Node3D>();
    }

    private static readonly Dictionary<string, (float W, float D, float H, float Cx, float Cz)?> Sizes = new();

    /// <summary>
    /// A model's footprint and height in inches at the Workshop's scale, and where its middle sits from the file's
    /// origin (for single models placed on a map). Null when the game doesn't have it.
    /// </summary>
    public static (float W, float D, float H, float Cx, float Cz)? Measure(string path)
    {
        if (Sizes.TryGetValue(path, out var known)) return known;
        (float, float, float, float, float)? result = null;
        if (Instance(path) is { } model)
        {
            Aabb? box = null;
            void Walk(Node n, Transform3D t)
            {
                Transform3D here = n is Node3D n3 ? t * n3.Transform : t;
                if (n is MeshInstance3D mi && mi.Mesh != null)
                {
                    Aabb b = here * mi.Mesh.GetAabb();
                    box = box is { } a ? a.Merge(b) : b;
                }
                foreach (Node c in n.GetChildren()) Walk(c, here);
            }
            Walk(model, Transform3D.Identity);
            model.Free();
            if (box is { } bb)
            {
                float k = TerrainPieces.ModelScale;
                Vector3 centre = bb.GetCenter();
                result = (bb.Size.X * k, bb.Size.Z * k, bb.Size.Y * k, centre.X * k, centre.Z * k);
            }
        }
        Sizes[path] = result;
        return result;
    }

    /// <summary>
    /// Stands one item's model in a piece root, the piece built <paramref name="stretchX"/> / <paramref name="stretchZ"/>
    /// times its own footprint. Positions stretch; the model grows by the average of the two, so nothing is squashed.
    /// </summary>
    public static Node3D? Place(Node3D pieceRoot, TerrainPieces.Item item, float stretchX = 1, float stretchZ = 1)
    {
        if (Instance(item.Model) is not { } model) return null;
        float k = item.Scale * Mathf.Sqrt(stretchX * stretchZ);
        var holder = new Node3D
        {
            Position = new Vector3(item.X * stretchX, item.Y, item.Z * stretchZ),
            RotationDegrees = new Vector3(0, item.Turn, 0),
            Scale = Vector3.One * k,
        };
        holder.AddChild(model);
        pieceRoot.AddChild(holder);
        return holder;
    }

    /// <summary>
    /// Dresses a map piece with a hand-built piece: the one the map picked (look "piece:&lt;id&gt;"), else the best fit
    /// for its kind and size in the player's terrain style (Settings). False when there is none - draw it the old way.
    /// </summary>
    public static bool Dress(Node3D parent, Vector2[] footprint, string kind, string? look, TerrainLooks.ETheme theme, int seed)
    {
        if (footprint.Length < 3) return false;
        var f = TerrainDresser.Frame.Of(footprint);
        bool round = footprint.Length > 8;
        string style = UserConfig.Current.View.TerrainStyle;
        TerrainPieces.Piece? piece = TerrainPieces.ById(look);
        if (piece == null && look != null && look.StartsWith(TerrainPieces.LookPrefix)) return false; // a piece this game doesn't have
        piece ??= TerrainPieces.Pick(kind, style, theme, f.Lu, f.Lv, round, seed)
                  ?? TerrainPieces.Pick(kind, style, theme, f.Lv, f.Lu, round, seed);
        if (piece == null) return false;

        // The piece's long side lies along the footprint's long side.
        bool pieceLongIsX = piece.W >= piece.D;
        float sx = (pieceLongIsX ? f.Lu : f.Lv) / Mathf.Max(0.1f, piece.W);
        float sz = (pieceLongIsX ? f.Lv : f.Lu) / Mathf.Max(0.1f, piece.D);
        var root = new Node3D
        {
            Position = new Vector3(f.Centre.X, 0, f.Centre.Y),
            Rotation = new Vector3(0, TerrainDresser.YawFor(f.U) + (pieceLongIsX ? 0 : Mathf.Pi / 2), 0),
        };
        parent.AddChild(root);
        int placed = 0;
        foreach (TerrainPieces.Item item in piece.Items)
            if (Place(root, item, sx, sz) != null) placed++;
        return placed > 0;
    }
}
