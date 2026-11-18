using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Game;

/// <summary>
/// Hand-arranged deployment (Kai, 2026-11-17): the unit is dropped as a block, then the player drags single models
/// about freely. Nothing is checked while dragging - "only have the game check for unit coherency when I click done"
/// (and on 2026-11-02 for the Raylib build: "as long as all of the models in that unit are within coherency range
/// when you click done, that's all that matters"). This is that Done check: every model on its own (zone, other
/// bases, impassable terrain, enemy distance) and then the unit's coherency as the core rules word it - "within 1"
/// of at least one other model, and within 9" of all other models, forming an uninterrupted chain" (GF / AoF
/// Advanced Rulebook 3.6.0, Unit Coherency). Gaps are base to base, measured on the true base shapes (#150). No
/// drawing.
/// </summary>
public static class ManualPlacement
{
    /// <summary>What is wrong with one model, in plain words.</summary>
    public sealed record Fault(ModelID Id, string Reason);

    private const float Eps = 0.01f;

    /// <summary>Every model's faults, empty when the placement is legal.</summary>
    public static List<Fault> Check(PlaceObjectsRequest<ModelData> request, ITableState table,
        IReadOnlyList<PlacedObjectEntry<ModelData>> placed)
    {
        var faults = new List<Fault>();
        if (placed.Count == 0) return faults;
        var mine = new HashSet<ModelID>(placed.Select(p => p.Binding.GetValue().ID));
        var enemies = EnemyPositions(table, request.TargetPlayerID);
        var occupants = Occupants(table, mine);
        var terrain = table.Terrain.Objects.ToList();
        var shapes = placed.Select(p => (Shape: p.Binding.GetValue().BaseShape, Facing: FacingOf(p))).ToList();

        for (int i = 0; i < placed.Count; i++)
        {
            ModelData m = placed[i].Binding.GetValue();
            Position at = placed[i].Position;
            var (shape, facing) = shapes[i];
            string? why = null;
            if (request.MaxDistanceFromStartInches > 0f && !PlacementUtilities.IsWithinStartRadius(at, m.Position, request.MaxDistanceFromStartInches))
                why = $"more than {request.MaxDistanceFromStartInches:0.#}\" from where it stood";
            else if (!PlacementUtilities.IsBaseWithinZone(at, shape, facing, request.DeploymentZone))
                why = "outside the deployment zone";
            else if (PlacementUtilities.OverlapsImpassibleTerrain(at, shape, facing, terrain))
                why = "on impassable terrain";
            else if (PlacementDistanceRules.ViolatesEnemyDistance(request, at, enemies))
                why = request.MinDistanceFromEnemiesInches > 0f
                    ? $"within {request.MinDistanceFromEnemiesInches:0.#}\" of an enemy"
                    : "inside an enemy's keep-away area";
            else
            {
                for (int j = 0; j < placed.Count && why == null; j++)
                    if (j != i && BaseShapeGeometry.AreColliding(shape, at, facing, shapes[j].Shape, placed[j].Position, shapes[j].Facing))
                        why = "on top of another model of the unit";
                foreach (var (pos, s, f) in occupants)
                {
                    if (why != null) break;
                    if (BaseShapeGeometry.AreColliding(shape, at, facing, s, pos, f)) why = "on top of another unit's model";
                }
            }
            if (why != null) faults.Add(new Fault(m.ID, why));
        }

        if (request.MustTouchTableEdge && !placed.Any(p => PlacementUtilities.TouchesZoneEdge(p.Position,
                p.Binding.GetValue().BaseShape.CircumscribedRadiusInches, request.DeploymentZone.Bounds)))
            faults.Add(new Fault(placed[0].Binding.GetValue().ID, "the unit must touch the table edge"));

        // A reposition (#197) is judged against where the models already stood, like a move; the drag tool is only
        // offered for set-ups, so it is held to the plain rule.
        foreach (var (index, reason) in Coherency(placed.Select((p, i) => (p.Position, shapes[i].Shape, shapes[i].Facing)).ToList()))
        {
            ModelID id = placed[index].Binding.GetValue().ID;
            if (faults.All(f => !f.Id.Equals(id))) faults.Add(new Fault(id, reason));
        }
        return faults;
    }

    /// <summary>
    /// The core rules' coherency for one unit's footprints: the index of each model that breaks it, and why.
    /// Models outside the biggest 1" chain are the ones flagged (as the engine's movement check does).
    /// </summary>
    public static List<(int Index, string Reason)> Coherency(IReadOnlyList<(Position At, IBaseShape Shape, Float2 Facing)> models)
    {
        var bad = new List<(int, string)>();
        int n = models.Count;
        if (n <= 1) return bad;
        float near = GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES + Eps;
        float far = GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES + Eps;
        var gap = new float[n, n];
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                gap[i, j] = gap[j, i] = BaseShapeGeometry.SurfaceGap2D(models[i].Shape, models[i].At, models[i].Facing,
                    models[j].Shape, models[j].At, models[j].Facing);
        // The chain: flood fill over 1" links, keep the biggest clump (ties: the one holding the lowest index).
        var group = Enumerable.Repeat(-1, n).ToArray();
        int best = -1, bestSize = 0, groups = 0;
        for (int s = 0; s < n; s++)
        {
            if (group[s] >= 0) continue;
            int id = groups++, size = 0;
            var stack = new Stack<int>();
            group[s] = id; stack.Push(s);
            while (stack.Count > 0)
            {
                int k = stack.Pop(); size++;
                for (int o = 0; o < n; o++)
                    if (group[o] < 0 && gap[k, o] <= near) { group[o] = id; stack.Push(o); }
            }
            if (size > bestSize) { bestSize = size; best = id; }
        }
        for (int i = 0; i < n; i++)
        {
            if (group[i] != best) { bad.Add((i, "more than 1\" from the rest of the unit")); continue; }
            float farthest = 0;
            for (int j = 0; j < n; j++) if (j != i) farthest = Math.Max(farthest, gap[i, j]);
            if (farthest > far) bad.Add((i, "more than 9\" from another model of the unit"));
        }
        return bad;
    }

    /// <summary>One line for the player: the first problem, and how many models have one.</summary>
    public static string Describe(IReadOnlyList<Fault> faults) => faults.Count switch
    {
        0 => "",
        1 => $"A model is {faults[0].Reason}.",
        _ => $"{faults.Count} models are out of place - one is {faults[0].Reason}.",
    };

    private static Float2 FacingOf(PlacedObjectEntry<ModelData> p) => p.Facing ?? p.Binding.GetValue().Facing;

    private static List<Position> EnemyPositions(ITableState table, PlayerID self)
    {
        var list = new List<Position>();
        foreach (IUnit unit in table.Units.Objects)
        {
            if (ITeamExtensions.AreAllied(table.Teams.Objects, self, unit.PlayerID) || !unit.GetIsOnBattlefield()) continue;
            foreach (IModel m in unit.Models)
                if (m.GetIsAlive() && (m.Position.x != 0f || m.Position.z != 0f)) list.Add(m.Position);
        }
        return list;
    }

    private static List<(Position Pos, IBaseShape Shape, Float2 Facing)> Occupants(ITableState table, HashSet<ModelID> mine)
    {
        var list = new List<(Position, IBaseShape, Float2)>();
        foreach (IUnit unit in table.Units.Objects)
        {
            if (!unit.GetIsOnBattlefield()) continue;
            foreach (IModel m in unit.Models)
            {
                if (!m.GetIsAlive() || mine.Contains(m.ID) || m.Position.x == 0f && m.Position.z == 0f) continue;
                list.Add((m.Position, m.BaseShape, m.Facing));
            }
        }
        return list;
    }
}
