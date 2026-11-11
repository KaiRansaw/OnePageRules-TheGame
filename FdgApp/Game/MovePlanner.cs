using FDG;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FdgRaylib.Placement;

namespace FdgRaylib.Game;

/// <summary>
/// Plans a unit move for a front end, with no drawing: the whole unit slides by one shared offset (its
/// formation kept), towards a table point or into base contact with an enemy unit, cut short to the
/// furthest point the engine will accept. Every plan is checked with the SAME validation the authoritative
/// <c>DefinePathStage</c> runs on the reply (per-model budgets, terrain, enemies, friendly overlap, lenient
/// coherency, Fast-Play's measured leader and charge rule) - a reply that fails it ends the game on a fault,
/// so nothing that fails here is ever sent.
/// </summary>
public static class MovePlanner
{
    public sealed record Plan(List<ModelMoveEntry> Moves, float Distance, bool Valid, string? Problem,
        IReadOnlyList<(ModelID Id, float X, float Z, float Radius)> Ends);

    /// <summary>Every living, placed model of the moving unit.</summary>
    private static List<DataBinding<ModelData>> Movers(DefineMovementPathRequest request) =>
        request.UnitDataBinding.GetValue().ModelBindings
            .Where(b => b.GetValue() is { } m && m.GetIsAlive()).ToList();

    /// <summary>No move at all: every model's path is empty (the unit stays where it is).</summary>
    public static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request) =>
        request.UnitDataBinding.GetValue().ModelBindings.Select(b => new ModelMoveEntry(b, new List<Position>())).ToList();

    /// <summary>Slides the unit so its middle heads for (<paramref name="x"/>, <paramref name="z"/>).</summary>
    public static Plan Towards(DefineMovementPathRequest request, ITableState table, float x, float z, float? cap = null)
    {
        var movers = Movers(request);
        if (movers.Count == 0) return Build(request, table, 0, 0);
        float cx = movers.Average(b => b.GetValue().Position.x), cz = movers.Average(b => b.GetValue().Position.z);
        return Furthest(request, table, x - cx, z - cz, cap);
    }

    /// <summary>
    /// Slides the unit straight at <paramref name="target"/> until its closest model touches the closest
    /// enemy model (a charge, or moving up to an enemy).
    /// </summary>
    public static Plan IntoContact(DefineMovementPathRequest request, ITableState table, IUnit target, float? cap = null)
    {
        var movers = Movers(request);
        var enemies = target.Models.Where(m => m.GetIsAlive()).ToList();
        if (movers.Count == 0 || enemies.Count == 0) return Build(request, table, 0, 0);
        (IModel a, IModel b, float d) best = (movers[0].GetValue(), enemies[0], float.MaxValue);
        foreach (var mb in movers)
        {
            ModelData m = mb.GetValue();
            foreach (IModel e in enemies)
            {
                float d = Dist(m.Position.x, m.Position.z, e.Position.x, e.Position.z);
                if (d < best.d) best = (m, e, d);
            }
        }
        float gap = best.d - best.a.BaseRadiusInches - best.b.BaseRadiusInches;
        if (best.d < 0.001f || gap <= 0.02f) return Build(request, table, 0, 0);
        float travel = gap - 0.02f;   // a hair short of overlapping: base contact
        float ux = (best.b.Position.x - best.a.Position.x) / best.d, uz = (best.b.Position.z - best.a.Position.z) / best.d;
        return Furthest(request, table, ux * travel, uz * travel, cap);
    }

    /// <summary>
    /// The full offset if the engine accepts it, else the largest fraction of it that it does.
    /// <paramref name="limit"/> caps the distance below the request's own maximum (the Advance or Rush the
    /// player picked).
    /// </summary>
    private static Plan Furthest(DefineMovementPathRequest request, ITableState table, float dx, float dz, float? limit)
    {
        // Never ask for more than the furthest any model may go.
        float len = MathF.Sqrt(dx * dx + dz * dz);
        float cap = Math.Max(0f, Math.Min(limit ?? float.MaxValue, request.MaxDistanceInches) - 0.01f);
        if (len > cap && len > 0.0001f) { dx *= cap / len; dz *= cap / len; }

        Plan full = Build(request, table, dx, dz);
        if (full.Valid) return full;
        float lo = 0f, hi = 1f;
        Plan? good = null;
        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) / 2;
            Plan p = Build(request, table, dx * mid, dz * mid);
            if (p.Valid) { lo = mid; good = p; } else hi = mid;
        }
        // Nothing short of standing still works: report the full attempt's problem so the player sees why.
        return good is { Distance: > 0.05f } ? good : full;
    }

    private static Plan Build(DefineMovementPathRequest request, ITableState table, float dx, float dz)
    {
        var moves = new List<ModelMoveEntry>();
        var ends = new List<(ModelID, float, float, float)>();
        foreach (var b in request.UnitDataBinding.GetValue().ModelBindings)
        {
            ModelData m = b.GetValue();
            if (!m.GetIsAlive()) { moves.Add(new ModelMoveEntry(b, new List<Position>())); continue; }
            bool moving = dx * dx + dz * dz > 1e-6f;
            var end = new Position(m.Position.x + dx, m.Position.z + dz);
            moves.Add(new ModelMoveEntry(b, moving ? new List<Position> { end } : new List<Position>()));
            ends.Add((m.ID, end.x, end.z, m.BaseRadiusInches));
        }
        string? problem = Problem(request, table, moves);
        return new Plan(moves, MathF.Sqrt(dx * dx + dz * dz), problem == null, problem, ends);
    }

    /// <summary>Null when the stage will accept the move; otherwise the reason, in plain words.</summary>
    public static string? Problem(DefineMovementPathRequest request, ITableState table, List<ModelMoveEntry> moves)
    {
        IUnit me = request.UnitDataBinding.GetValue();
        var enemies = Footprints(table, me, request.TargetPlayerID, enemy: true);
        var friends = Footprints(table, me, request.TargetPlayerID, enemy: false);
        bool ok = MovementUtilities.ValidatePaths(moves,
            entry => { var (_, rush, maxDist) = request.PlayerBudgetFor(entry.Model.GetValue().ID); return new ModelMoveBudget(rush, maxDist); },
            enemies, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain,
            table.Terrain.Objects.ToList(), out List<ReasonForInvalidMove> errors, friends, lenientCoherency: true,
            measured: request.LeaderOnlyMovement ? entry => request.IsMeasured(entry.Model.GetValue().ID) : null);
        if (!ok) return string.Join(", ", errors.Select(e => e.ToString()).Distinct());
        return MovementUtilities.FastPlayChargeProblem(request, moves, enemies);
    }

    /// <summary>Living, on-table models of enemy (or friendly, not the mover's own) units, as the stage builds them.</summary>
    private static List<EnemyModelFootprint> Footprints(ITableState table, IUnit mover, PlayerID me, bool enemy)
    {
        var list = new List<EnemyModelFootprint>();
        int key = 0;
        foreach (IUnit u in table.Units.Objects.ToList())
        {
            if (ReferenceEquals(u, mover) || u.ID.Equals(mover.ID)) continue;
            if (TeamAwareness.IsEnemyUnit(table, me, u) != enemy) continue;
            if (!u.GetIsOnBattlefield()) continue;
            bool uncontactable = enemy && FDG.Rules.Dispatch.AircraftRules.IsAircraft(u);
            bool any = false;
            foreach (IModel m in u.Models.Where(m => m.GetIsAlive()))
            {
                list.Add(new EnemyModelFootprint(m.Position, m.BaseRadiusInches, key, uncontactable, m.BaseShape, m.Facing));
                any = true;
            }
            if (any) key++;
        }
        return list;
    }

    private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
