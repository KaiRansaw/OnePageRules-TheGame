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

    /// <summary>What is moving, how far it may go, and the stage's own check of a reply.</summary>
    private sealed record Mover(DataBinding<UnitData> Unit, float MaxDistance, Func<List<ModelMoveEntry>, string?> Problem);

    private static Mover For(DefineMovementPathRequest r, ITableState table) =>
        new(r.UnitDataBinding, r.MaxDistanceInches, moves => Problem(r, table, moves));

    private static Mover For(ConsolidationMoveRequest r, ITableState table) =>
        new(r.UnitDataBinding, r.MaxDistanceInches, moves => Problem(r, table, moves));

    /// <summary>Every living, placed model of the moving unit.</summary>
    private static List<DataBinding<ModelData>> Movers(Mover mover) =>
        mover.Unit.GetValue().ModelBindings
            .Where(b => b.GetValue() is { } m && m.GetIsAlive()).ToList();

    /// <summary>No move at all: every model's path is empty (the unit stays where it is).</summary>
    public static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request) =>
        request.UnitDataBinding.GetValue().ModelBindings.Select(b => new ModelMoveEntry(b, new List<Position>())).ToList();

    /// <summary>Slides the unit so its middle heads for (<paramref name="x"/>, <paramref name="z"/>).</summary>
    public static Plan Towards(DefineMovementPathRequest request, ITableState table, float x, float z, float? cap = null) =>
        Towards(For(request, table), Array.Empty<(float, float)>(), x, z, cap);

    /// <summary>
    /// The same, going through turn points first (Kai, 2026-11-11: "a click should mark a point so a unit could go
    /// around corners"). <paramref name="via"/> are where the unit's middle passes, in order; every model follows the
    /// same bends, so the formation is kept and each model's path is measured along its bends.
    /// </summary>
    public static Plan Towards(DefineMovementPathRequest request, ITableState table, IReadOnlyList<(float X, float Z)> via,
        float x, float z, float? cap = null) => Towards(For(request, table), via, x, z, cap);

    /// <summary>
    /// A consolidation move (after a melee): the unit slides towards (x, z), up to the request's 3" (wipeout) or
    /// 1" (disengage), checked with the same <c>ValidateConsolidationPaths</c> the ConsolidateStage runs.
    /// </summary>
    public static Plan Consolidate(ConsolidationMoveRequest request, ITableState table, float x, float z) =>
        Towards(For(request, table), Array.Empty<(float, float)>(), x, z, null);

    /// <summary>A consolidation that leaves every model where it stands.</summary>
    public static List<ModelMoveEntry> StayInPlace(ConsolidationMoveRequest request) =>
        request.UnitDataBinding.GetValue().ModelBindings.Select(b => new ModelMoveEntry(b, new List<Position>())).ToList();

    private static Plan Towards(Mover mover, IReadOnlyList<(float X, float Z)> via, float x, float z, float? cap)
    {
        var movers = Movers(mover);
        if (movers.Count == 0) return Build(mover, Prefix.None, 0, 0);
        Prefix prefix = PrefixFor(movers, via);
        var (lx, lz) = prefix.Last;
        float cx = movers.Average(b => b.GetValue().Position.x), cz = movers.Average(b => b.GetValue().Position.z);
        return Furthest(mover, prefix, x - cx - lx, z - cz - lz, cap);
    }

    /// <summary>The turn points as offsets from where the unit stands, and the length of path they already use.</summary>
    private sealed record Prefix(IReadOnlyList<(float X, float Z)> Offsets, float Length)
    {
        public static readonly Prefix None = new(Array.Empty<(float, float)>(), 0);
        public (float X, float Z) Last => Offsets.Count > 0 ? Offsets[^1] : (0, 0);
    }

    private static Prefix PrefixFor(List<DataBinding<ModelData>> movers, IReadOnlyList<(float X, float Z)> via)
    {
        if (via.Count == 0 || movers.Count == 0) return Prefix.None;
        float cx = movers.Average(b => b.GetValue().Position.x), cz = movers.Average(b => b.GetValue().Position.z);
        var offsets = via.Select(p => (p.X - cx, p.Z - cz)).ToList();
        float length = 0, px = 0, pz = 0;
        foreach (var (ox, oz) in offsets) { length += Dist(px, pz, ox, oz); px = ox; pz = oz; }
        return new Prefix(offsets, length);
    }

    /// <summary>
    /// Slides the unit straight at <paramref name="target"/> until its closest model touches the closest
    /// enemy model (a charge, or moving up to an enemy).
    /// </summary>
    public static Plan IntoContact(DefineMovementPathRequest request, ITableState table, IUnit target, float? cap = null) =>
        IntoContact(request, table, Array.Empty<(float, float)>(), target, cap);

    /// <summary>Into contact after going through the turn points <paramref name="via"/> (a charge round a corner).</summary>
    public static Plan IntoContact(DefineMovementPathRequest request, ITableState table, IReadOnlyList<(float X, float Z)> via,
        IUnit target, float? cap = null)
    {
        Mover mover = For(request, table);
        var movers = Movers(mover);
        var enemies = target.Models.Where(m => m.GetIsAlive()).ToList();
        if (movers.Count == 0 || enemies.Count == 0) return Build(mover, Prefix.None, 0, 0);
        Prefix prefix = PrefixFor(movers, via);
        var (lx, lz) = prefix.Last;
        (ModelData a, IModel b, float d) best = (movers[0].GetValue(), enemies[0], float.MaxValue);
        foreach (var mb in movers)
        {
            ModelData m = mb.GetValue();
            foreach (IModel e in enemies)
            {
                float d = Dist(m.Position.x + lx, m.Position.z + lz, e.Position.x, e.Position.z);
                if (d < best.d) best = (m, e, d);
            }
        }
        float gap = best.d - best.a.BaseRadiusInches - best.b.BaseRadiusInches;
        if (best.d < 0.001f || gap <= 0.02f) return Build(mover, prefix, 0, 0);
        float travel = gap - 0.02f;   // a hair short of overlapping: base contact
        float ux = (best.b.Position.x - best.a.Position.x - lx) / best.d, uz = (best.b.Position.z - best.a.Position.z - lz) / best.d;
        return Furthest(mover, prefix, ux * travel, uz * travel, cap);
    }

    /// <summary>
    /// The full offset if the engine accepts it, else the largest fraction of it that it does.
    /// <paramref name="limit"/> caps the distance below the request's own maximum (the Advance or Rush the
    /// player picked).
    /// </summary>
    private static Plan Furthest(Mover mover, Prefix prefix, float dx, float dz, float? limit)
    {
        // Never ask for more than the furthest any model may go (the turn points already use some of it).
        float len = MathF.Sqrt(dx * dx + dz * dz);
        float cap = Math.Max(0f, Math.Min(limit ?? float.MaxValue, mover.MaxDistance) - 0.01f - prefix.Length);
        if (len > cap && len > 0.0001f) { dx *= cap / len; dz *= cap / len; }

        Plan full = Build(mover, prefix, dx, dz);
        if (full.Valid) return full;
        float lo = 0f, hi = 1f;
        Plan? good = null;
        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) / 2;
            Plan p = Build(mover, prefix, dx * mid, dz * mid);
            if (p.Valid) { lo = mid; good = p; } else hi = mid;
        }
        // Nothing short of standing still works: report the full attempt's problem so the player sees why.
        return good is { } g && g.Distance - prefix.Length > 0.05f ? g : full;
    }

    /// <summary>Every model goes through the turn points (as offsets from where it stands), then the last leg.</summary>
    private static Plan Build(Mover mover, Prefix prefix, float dx, float dz)
    {
        var moves = new List<ModelMoveEntry>();
        var ends = new List<(ModelID, float, float, float)>();
        var (lx, lz) = prefix.Last;
        bool leg = dx * dx + dz * dz > 1e-6f;
        foreach (var b in mover.Unit.GetValue().ModelBindings)
        {
            ModelData m = b.GetValue();
            if (!m.GetIsAlive()) { moves.Add(new ModelMoveEntry(b, new List<Position>())); continue; }
            var path = prefix.Offsets.Select(o => new Position(m.Position.x + o.X, m.Position.z + o.Z)).ToList();
            if (leg) path.Add(new Position(m.Position.x + lx + dx, m.Position.z + lz + dz));
            moves.Add(new ModelMoveEntry(b, path));
            var end = path.Count > 0 ? path[^1] : m.Position;
            ends.Add((m.ID, end.x, end.z, m.BaseRadiusInches));
        }
        string? problem = mover.Problem(moves);
        return new Plan(moves, prefix.Length + MathF.Sqrt(dx * dx + dz * dz), problem == null, problem, ends);
    }

    /// <summary>
    /// Models moved one at a time (Kai, 2026-11-13: single-model moves and box-select): every model in
    /// <paramref name="ends"/> goes straight from where it stands to its end; the rest stay. The distance is the longest
    /// single model's. Checked with the same validation as a whole-unit move.
    /// </summary>
    public static Plan Arranged(DefineMovementPathRequest request, ITableState table, IReadOnlyDictionary<ModelID, (float X, float Z)> ends)
    {
        Mover mover = For(request, table);
        var moves = new List<ModelMoveEntry>();
        var shown = new List<(ModelID, float, float, float)>();
        float longest = 0;
        foreach (var b in mover.Unit.GetValue().ModelBindings)
        {
            ModelData m = b.GetValue();
            if (!m.GetIsAlive()) { moves.Add(new ModelMoveEntry(b, new List<Position>())); continue; }
            var path = new List<Position>();
            float ex = m.Position.x, ez = m.Position.z;
            if (ends.TryGetValue(m.ID, out var e) && Dist(m.Position.x, m.Position.z, e.X, e.Z) > 0.01f)
            {
                path.Add(new Position(e.X, e.Z));
                (ex, ez) = e;
                longest = Math.Max(longest, Dist(m.Position.x, m.Position.z, e.X, e.Z));
            }
            moves.Add(new ModelMoveEntry(b, path));
            shown.Add((m.ID, ex, ez, m.BaseRadiusInches));
        }
        string? problem = mover.Problem(moves);
        return new Plan(moves, longest, problem == null, problem, shown);
    }

    /// <summary>
    /// The picked models slide together (their spacing kept) so their middle heads for (x, z); every other model keeps
    /// the place <paramref name="ends"/> gives it (or where it stands). Cut short, like a whole-unit move, to the
    /// furthest point the engine accepts and to <paramref name="cap"/> per model. Returns the new arrangement too.
    /// </summary>
    public static (Plan Plan, Dictionary<ModelID, (float X, float Z)> Ends) PickedTowards(DefineMovementPathRequest request, ITableState table,
        IReadOnlyDictionary<ModelID, (float X, float Z)> ends, IReadOnlyCollection<ModelID> picked, float x, float z, float? cap = null)
    {
        var living = request.UnitDataBinding.GetValue().ModelBindings.Select(b => b.GetValue()).Where(m => m.GetIsAlive()).ToList();
        (float X, float Z) At(ModelData m) => ends.TryGetValue(m.ID, out var e) ? e : (m.Position.x, m.Position.z);
        var moving = living.Where(m => picked.Contains(m.ID)).ToList();
        var baseEnds = living.ToDictionary(m => m.ID, At);
        if (moving.Count == 0) return (Arranged(request, table, baseEnds), baseEnds);
        float cx = moving.Average(m => At(m).X), cz = moving.Average(m => At(m).Z);
        float dx = x - cx, dz = z - cz;
        float limit = Math.Min(cap ?? float.MaxValue, request.MaxDistanceInches) - 0.01f;

        Dictionary<ModelID, (float X, float Z)> Shifted(float f)
        {
            var d = new Dictionary<ModelID, (float X, float Z)>(baseEnds);
            foreach (ModelData m in moving)
            {
                var (sx, sz) = At(m);
                float tx = sx + dx * f, tz = sz + dz * f;
                // Each model's own straight line from where it started may not pass the cap.
                float len = Dist(m.Position.x, m.Position.z, tx, tz);
                if (len > limit && len > 0.0001f)
                {
                    float k = limit / len;
                    tx = m.Position.x + (tx - m.Position.x) * k;
                    tz = m.Position.z + (tz - m.Position.z) * k;
                }
                d[m.ID] = (tx, tz);
            }
            return d;
        }

        var full = Shifted(1f);
        Plan fullPlan = Arranged(request, table, full);
        if (fullPlan.Valid) return (fullPlan, full);
        float lo = 0f, hi = 1f;
        (Plan, Dictionary<ModelID, (float X, float Z)>)? good = null;
        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) / 2;
            var d = Shifted(mid);
            Plan p = Arranged(request, table, d);
            if (p.Valid) { lo = mid; good = (p, d); } else hi = mid;
        }
        return good is { } g && lo > 0.02f ? g : (fullPlan, full);
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

    /// <summary>Null when the ConsolidateStage will accept the move; otherwise the reason, in plain words.</summary>
    public static string? Problem(ConsolidationMoveRequest request, ITableState table, List<ModelMoveEntry> moves)
    {
        IUnit me = request.UnitDataBinding.GetValue();
        var enemies = Footprints(table, me, request.TargetPlayerID, enemy: true);
        var friends = Footprints(table, me, request.TargetPlayerID, enemy: false);
        bool ok = MovementUtilities.ValidateConsolidationPaths(moves, request.MaxDistanceInches, enemies,
            request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain,
            table.Terrain.Objects.ToList(), out List<ReasonForInvalidMove> errors, friends);
        return ok ? null : string.Join(", ", errors.Select(e => e.ToString()).Distinct());
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
