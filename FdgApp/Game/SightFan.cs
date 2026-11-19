using FDG;
using FDG.Stages;
using FDG.Utilities;

namespace FdgRaylib.Game;

/// <summary>
/// "What can this unit see" - the ground the selected unit could shoot a normal-sized model on, cell by cell, with one
/// band per weapon range. Uses the engine's own sight test (<see cref="LineOfSightUtilities"/>: classic or Volumetric,
/// whichever the game plays), so the picture and the dice can never disagree.
///
/// <para>The target is a 32mm model (<see cref="SightEnd.Point"/>) standing on the cell; distance is base edge to base
/// edge, the way ranges are measured. Every other unit blocks as it does for a real shot (the See-Through Allies house
/// rule is honoured), apart from a model standing on the cell itself - that would be the target.</para>
///
/// <para>Idea from Niemandsland (MIT, github.com/DutchMaxwell/Niemandsland), which draws a sight and range fan on F.</para>
/// </summary>
public static class SightFan
{
    /// <summary>One cell of the picture: its centre in table inches and the band. Band i = seen and within
    /// <see cref="Result.Ranges"/>[i] (the shortest range that reaches); -1 = within reach but out of sight.</summary>
    public readonly record struct Cell(float X, float Z, int Band);

    public sealed record Result(float CellInches, IReadOnlyList<Cell> Cells, IReadOnlyList<float> Ranges,
        IReadOnlyList<string> RangeNames, bool Volumetric)
    {
        public static readonly Result Empty = new(1f, Array.Empty<Cell>(), Array.Empty<float>(), Array.Empty<string>(), false);
    }

    public const float DefaultCellInches = 1f;

    /// <param name="onlyFrom">Leader-Based Shooting: only this model's sight and reach count.</param>
    public static Result Compute(ITableState table, IUnit unit, bool seeThroughFriendlyUnits,
        ModelID? onlyFrom = null, float cellInches = DefaultCellInches, CancellationToken cancel = default)
    {
        List<IModel> shooters = unit.Models.Where(m => m.GetIsAlive() && !(m.Position.x == 0f && m.Position.z == 0f)
                                                       && (onlyFrom is not { } only || m.ID.Equals(only)))
            .Where(m => m.Weapons.Any(w => w.RangeInches > 0)).ToList();
        if (shooters.Count == 0 || !unit.GetIsOnBattlefield()) return Result.Empty;

        var byRange = shooters.SelectMany(m => m.Weapons).Where(w => w.RangeInches > 0)
            .GroupBy(w => w.RangeInches).OrderBy(g => g.Key).ToList();
        float[] ranges = byRange.Select(g => g.Key).ToArray();
        string[] names = byRange.Select(g => string.Join(", ", g.Select(w => w.Name).Distinct())).ToArray();
        float maxRange = ranges[^1];
        float targetRadius = SightEnd.Point(default).RadiusInches;

        // Each shooter's own longest reach and its ranges, so a model only lights the bands its own weapons have.
        var own = shooters.Select(m => (Model: m, End: SightEnd.Of(m),
            Ranges: m.Weapons.Where(w => w.RangeInches > 0).Select(w => w.RangeInches).Distinct().OrderBy(r => r).ToArray())).ToList();

        var terrain = table.Terrain.Objects.ToList();
        var modelBlockers = LineOfSightUtilities.BuildModelBlockerSet(table, unit, seeThroughFriendlyUnits).Excluding(unit);

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var o in own)
        {
            float reach = o.Ranges[^1] + o.Model.BaseRadiusInches + targetRadius;
            minX = MathF.Min(minX, o.Model.Position.x - reach); maxX = MathF.Max(maxX, o.Model.Position.x + reach);
            minZ = MathF.Min(minZ, o.Model.Position.z - reach); maxZ = MathF.Max(maxZ, o.Model.Position.z + reach);
        }
        minX = MathF.Max(0f, minX); minZ = MathF.Max(0f, minZ);
        maxX = MathF.Min(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, maxX);
        maxZ = MathF.Min(GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, maxZ);

        var cells = new List<Cell>();
        var blockers = new List<ITerrain>(terrain.Count + modelBlockers.Count);
        for (float x = minX + cellInches / 2; x < maxX; x += cellInches)
        {
            cancel.ThrowIfCancellationRequested();
            for (float z = minZ + cellInches / 2; z < maxZ; z += cellInches)
            {
                var at = new Position(x, z);
                var flat = new Float2(x, z);
                // Nobody stands inside a solid piece.
                if (terrain.Any(t => (t.TerrainType.HasFlag(ETerrainType.Blocking) || t.TerrainType.HasFlag(ETerrainType.Impassible))
                                     && t.Shape.IsPointWithinZone(flat)))
                    continue;

                int band = int.MaxValue;
                bool inReach = false;
                bool blockersReady = false;
                foreach (var o in own)
                {
                    float d = Position.GetDistance2D(o.Model.Position, at) - o.Model.BaseRadiusInches - targetRadius;
                    int reachIndex = Array.FindIndex(o.Ranges, r => r >= d);
                    if (reachIndex < 0) continue;
                    inReach = true;
                    int ownBand = Array.IndexOf(ranges, o.Ranges[reachIndex]);
                    if (ownBand >= band) continue; // can't improve on a band already seen
                    if (!blockersReady)
                    {
                        blockers.Clear();
                        blockers.AddRange(terrain);
                        foreach (ITerrain b in modelBlockers)
                            if (!b.Shape.IsPointWithinZone(flat)) blockers.Add(b);
                        blockersReady = true;
                    }
                    if (LineOfSightUtilities.HasLineOfSight(o.End, SightEnd.Point(at), blockers)) band = ownBand;
                }
                if (!inReach) continue;
                cells.Add(new Cell(x, z, band == int.MaxValue ? -1 : band));
            }
        }
        return new Result(cellInches, cells, ranges, names, VolumetricSight.On);
    }
}
