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
    public static Func<string, IReadOnlyList<(float U0, float V0, float U1, float V1)>?>? Provider { get; set; }

    /// <summary>Which pieces play on their model's solid parts: those whose rules stop models or sight.</summary>
    public static bool Applies(MapPiece p) =>
        p.Raw == null && !p.Round && WarhammerTerrainLibrary.IdOf(p.Look) != null
        && (p.Rules.HasFlag(ETerrainType.Impassible) || p.Rules.HasFlag(ETerrainType.Blocking));

    /// <summary>The rules shape of a piece drawn as a model, or null to play the whole footprint.</summary>
    public static IZone? For(MapPiece p)
    {
        if (!Applies(p) || Provider?.Invoke(p.Look) is not { Count: > 0 } parts) return null;
        return FromParts(p, parts);
    }

    /// <summary>
    /// The parts placed on the piece's footprint. A model is drawn with its +X along the footprint's width and its +Z
    /// towards the footprint's -depth side (WarhammerModels.Dress, table z running the other way to Godot's), so a
    /// part's v runs from the footprint's far (+z) edge to its near one.
    /// </summary>
    public static IZone FromParts(MapPiece p, IReadOnlyList<(float U0, float V0, float U1, float V1)> parts)
    {
        var zones = new List<IZone>();
        foreach ((float u0, float v0, float u1, float v1) in parts)
        {
            float left = p.X - p.W / 2f + u0 * p.W, right = p.X - p.W / 2f + u1 * p.W;
            float bottom = p.Z + (0.5f - v1) * p.D, top = p.Z + (0.5f - v0) * p.D;
            if (right - left < 0.01f || top - bottom < 0.01f) continue;
            var rect = new RectangularZone(left, right, bottom, top);
            zones.Add(MathF.Abs(p.Angle % 360f) < 0.01f ? rect : new RotatedZoneWrapper(rect, p.Angle, new Float2(p.X, p.Z)));
        }
        return zones.Count switch
        {
            0 => p.ToZone(),
            1 => zones[0],
            _ => new CompositeZone(zones),
        };
    }

    /// <summary>
    /// Brings a saved map's solid parts up to date with this computer's models and writes the file when they changed.
    /// Nothing happens without a <see cref="Provider"/>. Returns whether the file was rewritten.
    /// </summary>
    public static bool RefreshFile(string path)
    {
        if (Provider == null || !File.Exists(path)) return false;
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
