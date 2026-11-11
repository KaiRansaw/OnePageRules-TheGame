using FDG;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Game;

/// <summary>
/// Places a unit where the player points (Godot deployment, Kai 2026-11-11): the engine's own placement sweep
/// (<see cref="AiPlaceObjectsResolver{T}"/> - zone containment, distance from enemies, keep-out discs,
/// impassable terrain, no overlaps, cohesive block) aimed at the pointed spot instead of the bot's lanes. The
/// sweep returns the nearest legal block to the aim, so a click is always answered with a placement the stage
/// accepts. No drawing.
/// </summary>
public sealed class AimedPlacement : AiPlaceObjectsResolver<ModelData>
{
    private Position _aim;

    public AimedPlacement(ITableState table) : base(table) { }

    /// <summary>The block the unit would get with its middle aimed at (x, z), or null if there is nothing to place.</summary>
    public List<PlacedObjectEntry<ModelData>>? At(PlaceObjectsRequest<ModelData> request, float x, float z)
    {
        _aim = new Position(x, z);
        CancellableResult<List<PlacedObjectEntry<ModelData>>> result = Resolve(request).GetAwaiter().GetResult();
        return result is Selected<List<PlacedObjectEntry<ModelData>>> s && s.Value.Count > 0 ? s.Value : null;
    }

    protected override Position PreferredBlockCenter(PlaceObjectsRequest<ModelData> request, ZoneBounds bounds,
        int deployIndex, float maxRadius, float gridWidth, float gridHeight, float spacingX) => _aim;

    /// <summary>
    /// True when the request is a "move each model up to N inches from where it stands" reposition: the sweep
    /// knows nothing of each model's own circle, so the only placement offered for those is standing still.
    /// </summary>
    public static bool IsReposition(PlaceObjectsRequest<ModelData> request) => request.MaxDistanceFromStartInches > 0f;

    /// <summary>Every model where it already stands.</summary>
    public static List<PlacedObjectEntry<ModelData>> StayPut(PlaceObjectsRequest<ModelData> request) =>
        request.ModelsToPlace.Select(b => new PlacedObjectEntry<ModelData>(b, b.GetValue().Position)).ToList();
}
