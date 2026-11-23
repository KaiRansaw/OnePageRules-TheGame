using FDG;

namespace FdgRaylib.Terrain;

/// <summary>
/// Kai, 2026-11-19: "models that use Impassable terrain make the ENTIRE footprint impassable ... if a wall is L shaped,
/// JUST the L shaped building should be impassable". A Warhammer terrain model's solid parts, as rectangles across its
/// footprint, become the piece's rules shape (<see cref="FDG.SaveLoad.TerrainPieceEntry.Solid"/>) when a map is saved.
///
/// <para>Only the front end can look inside a model, so it sets <see cref="Provider"/>: for a look, the solid parts
/// as (u0, v0, u1, v1) fractions of the model's own box - u along the footprint's width, v along its depth, the way
/// <c>WarhammerModels.Dress</c> stretches the model onto the footprint. No provider (the Raylib build, the command line)
/// or no answer = the whole footprint plays, as before.</para>
/// </summary>
public static class TerrainSolids
{
    /// <summary>The solid parts of the model a look names, or null when unknown.</summary>
    public static Func<string, IReadOnlyList<(float U0, float V0, float U1, float V1, float H)>?>? Provider { get; set; }

    /// <summary>
    /// For a map piece the map maker set to one of the game's own hand-built pieces (look "piece:&lt;id&gt;", the Terrain
    /// Workshop): its solid parts as table rectangles (x0, z0, x1, z1, inches, table axes), or null when unknown. Only an
    /// explicitly picked piece counts - an automatic pick depends on each player's terrain style and pieces, and the rules
    /// shape must be the same for everyone.
    /// </summary>
    public static Func<MapPiece, IReadOnlyList<(float X0, float Z0, float X1, float Z1, float H)>?>? PieceProvider { get; set; }

    // Climbable too (2026-11-22): a wall is climbed where the model actually stands, at the model's own heights.
    private static bool Stops(MapPiece p) => p.Rules.HasFlag(ETerrainType.Impassible) || p.Rules.HasFlag(ETerrainType.Blocking)
        || p.Rules.HasFlag(ETerrainType.Climbable);

    /// <summary>A piece set to one of the game's hand-built pieces by name.</summary>
    public static bool IsPiece(MapPiece p) => p.Look.StartsWith(TerrainPieces.LookPrefix, StringComparison.Ordinal);

    /// <summary>Which pieces play on their model's solid parts: those whose rules stop models or sight.</summary>
    public static bool Applies(MapPiece p) =>
        p.Raw == null && Stops(p)
        && ((!p.Round && WarhammerTerrainLibrary.IdOf(p.Look) != null) || IsPiece(p));

    /// <summary>The rules shape of a piece drawn as a model, or null to play the whole footprint.</summary>
    public static IZone? For(MapPiece p)
    {
        if (!Applies(p)) return null;
        if (IsPiece(p))
        {
            if (PieceProvider?.Invoke(p) is not { Count: > 0 } rects) return null;
            var zones = rects.Where(r => r.X1 - r.X0 > 0.01f && r.Z1 - r.Z0 > 0.01f)
                .Select(r => Rect(r.X0, r.X1, r.Z0, r.Z1, r.H)).ToList();
            return zones.Count switch { 0 => null, 1 => zones[0], _ => new CompositeZone(zones) };
        }
        if (Provider?.Invoke(p.Look) is not { Count: > 0 } parts) return null;
        return FromParts(p, parts);
    }

    /// <summary>
    /// The parts placed on the piece's footprint. A model is drawn with its +X along the footprint's width and its +Z
    /// towards the footprint's -depth side (WarhammerModels.Dress, table z running the other way to Godot's), so a
    /// part's v runs from the footprint's far (+z) edge to its near one.
    /// </summary>
    public static IZone FromParts(MapPiece p, IReadOnlyList<(float U0, float V0, float U1, float V1, float H)> parts)
    {
        var zones = new List<IZone>();
        foreach ((float u0, float v0, float u1, float v1, float h) in parts)
        {
            float left = p.X - p.W / 2f + u0 * p.W, right = p.X - p.W / 2f + u1 * p.W;
            float bottom = p.Z + (0.5f - v1) * p.D, top = p.Z + (0.5f - v0) * p.D;
            if (right - left < 0.01f || top - bottom < 0.01f) continue;
            RectangularZone rect = Rect(left, right, bottom, top, h);
            zones.Add(MathF.Abs(p.Angle % 360f) < 0.01f ? rect : new RotatedZoneWrapper(rect, p.Angle, new Float2(p.X, p.Z)));
        }
        return zones.Count switch
        {
            0 => p.ToZone(),
            1 => zones[0],
            _ => new CompositeZone(zones),
        };
    }

    /// <summary>A part's rectangle, with how tall the model stands there when that is known (h &gt; 0; heights read
    /// from the models, Kai 2026-11-22), so 3D line of sight sees over its low parts.</summary>
    private static RectangularZone Rect(float left, float right, float bottom, float top, float h) =>
        h > 0f ? new RaisedRectZone(left, right, bottom, top, MathF.Round(h * 4f) / 4f) : new RectangularZone(left, right, bottom, top);

    /// <summary>
    /// Brings a saved map's solid parts up to date with this computer's models and writes the file when they changed.
    /// Nothing happens without a <see cref="Provider"/>. Returns whether the file was rewritten.
    /// </summary>
    public static bool RefreshFile(string path)
    {
        if ((Provider == null && PieceProvider == null) || !File.Exists(path)) return false;
        try
        {
            MapDraft? draft = MapDraft.Load(path, out _);
            if (draft == null || !draft.Pieces.Any(Applies)) return false;
            FDG.SaveLoad.TerrainLayoutFile now = draft.ToLayout();
            string fresh = FdgRaylib.Cli.TerrainLoader.SerializeToJson(now);
            string? old = FDG.SaveLoad.TerrainLayoutLoader.TryLoadFromFile(path, out _) is { } f ? FdgRaylib.Cli.TerrainLoader.SerializeToJson(f) : null;
            if (old == fresh) return false;
            File.WriteAllText(path, fresh);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
