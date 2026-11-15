using FdgRaylib.Models3D;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Lab;

namespace OprGodot.Battle;

/// <summary>
/// Puts a downloaded Warhammer terrain model on a terrain piece's footprint (Kai, 2026-11-14: "the model is the
/// piece"). A map piece names its model in its Look (<c>wh:&lt;id&gt;</c>); this computer either has that model, and it
/// is drawn stretched to the footprint and turned with it, or it doesn't, and the caller draws the plain stand-in.
/// Used by the Map Maker and the battle table, so a piece looks the same in both.
/// </summary>
public static class WarhammerModels
{
    private static WarhammerTerrainLibrary? _library;
    private static DateTime _libraryStamp;
    private static readonly Dictionary<string, Node3D?> Templates = new();

    /// <summary>The matched pieces on this computer, re-read when the match file changes (the match screen saves it).</summary>
    public static WarhammerTerrainLibrary? Library
    {
        get
        {
            string matches = Path.Combine(WarhammerTerrain.ModelsFolder, WarhammerTerrainLibrary.FileName);
            string list = WarhammerTerrain.DefaultPath;
            DateTime stamp = new[] { matches, list }.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
            if (_library == null || stamp != _libraryStamp)
            {
                _library = WarhammerTerrainLibrary.Load();
                _libraryStamp = stamp;
            }
            return _library;
        }
    }

    /// <summary>Forget the loaded list (the match screen calls this after saving).</summary>
    public static void Reload() { _library = null; Ups.Clear(); }

    private static readonly Dictionary<string, EUpAxis> Ups = new();

    /// <summary>
    /// Which way up a piece's file stands (Kai, 2026-11-14: "some of the map assets are spawned in upside down"), from its
    /// model settings - set on the Match terrain screen (or the Asset Lab's Up box), the same for every screen.
    /// </summary>
    public static EUpAxis UpOf(string file)
    {
        if (!Ups.TryGetValue(file, out EUpAxis up)) Ups[file] = up = UnitModelSettings.Load(file)?.UpAxis ?? EUpAxis.YUp;
        return up;
    }

    /// <summary>Saves which way up a piece stands, for every screen that draws it.</summary>
    public static void SetUp(string file, EUpAxis up)
    {
        var s = UnitModelSettings.Load(file) ?? new UnitModelSettings { BaseMm = 0, HeightMm = 0 };
        s.UpAxis = up;
        s.Save(file);
        Ups[file] = up;
    }

    /// <summary>
    /// Draws the model a piece's Look names onto <paramref name="footprint"/> (the piece's outline in table space, as
    /// the stand-ins take it: x across, y = the table's z). False - nothing added - when this computer doesn't have it.
    /// </summary>
    public static bool Dress(Node3D parent, string? look, Vector2[] footprint)
    {
        if (WarhammerTerrainLibrary.IdOf(look) is null || footprint.Length < 3) return false;
        if (Library?.ForLook(look) is not { } entry) return false;
        if (Template(entry.File) is not { } template) return false;

        var holder = new Node3D { Name = "Warhammer_" + entry.Id };
        parent.AddChild(holder);
        var pivot = new Node3D();
        holder.AddChild(pivot);
        var body = (Node3D)template.Duplicate();
        body.Transform = new Transform3D(ModelPlacement.UpBasis(UpOf(entry.File)), Vector3.Zero);
        pivot.AddChild(body);
        Aabb? measured = AssetLoader.Bounds(body, holder);
        if (measured is not { } box || box.Size.X < 1e-3f || box.Size.Z < 1e-3f)
        {
            holder.QueueFree();
            return false;
        }

        // A rectangle's first edge is its width (MapPiece.Corners); anything else is fitted to its bounds, unturned.
        Vector2 centre = footprint.Aggregate(Vector2.Zero, (a, p) => a + p) / footprint.Length;
        Vector2 across, deep;
        if (footprint.Length == 4)
        {
            across = footprint[1] - footprint[0];
            deep = footprint[2] - footprint[1];
        }
        else
        {
            float minX = footprint.Min(p => p.X), maxX = footprint.Max(p => p.X), minY = footprint.Min(p => p.Y), maxY = footprint.Max(p => p.Y);
            across = new Vector2(maxX - minX, 0);
            deep = new Vector2(0, maxY - minY);
        }
        float sx = across.Length() / box.Size.X, sz = deep.Length() / box.Size.Z, sy = Mathf.Sqrt(sx * sz);
        pivot.Scale = new Vector3(sx, sy, sz);
        Vector3 c = box.GetCenter();
        pivot.Position = new Vector3(-c.X * sx, -box.Position.Y * sy, -c.Z * sz);
        holder.Position = new Vector3(centre.X, 0, centre.Y);
        // Turn the model's +X onto the footprint's first edge (its width), the same way the outlines are drawn - checked
        // against them in the Map Maker's top view (2026-11-14).
        holder.Rotation = new Vector3(0, Mathf.Atan2(across.Y, across.X), 0);
        return true;
    }

    /// <summary>The model's own box, as it is drawn on a footprint (unturned, unscaled) - its size in table inches.
    /// <paramref name="inTree"/> is any node in the scene tree to measure under.</summary>
    public static Aabb? Measure(Node inTree, string file)
    {
        if (Template(file) is not { } template) return null;
        var holder = new Node3D();
        inTree.AddChild(holder);
        var body = (Node3D)template.Duplicate();
        body.Transform = new Transform3D(ModelPlacement.UpBasis(UpOf(file)), Vector3.Zero);
        holder.AddChild(body);
        Aabb? box = AssetLoader.Bounds(body, holder);
        holder.QueueFree();
        return box;
    }

    /// <summary>A model loaded once and copied for every piece that shows it (some are 17 MB).</summary>
    private static Node3D? Template(string file)
    {
        if (Templates.TryGetValue(file, out Node3D? have)) return have;
        Node3D? model = File.Exists(file) ? AssetLoader.Load(file).Model : null;
        Templates[file] = model;
        return model;
    }
}
