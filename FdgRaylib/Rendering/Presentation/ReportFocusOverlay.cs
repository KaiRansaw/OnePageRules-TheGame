using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Shows on the table what the hovered battle-report card is about (Kai, 2026-11-05: "draw a highlighted
/// line between the units that shot and were shot. We need to be able to quickly see which units are
/// involved in the battle reports. also, highlight the units that the battle reports are referencing").
/// <list type="bullet">
///   <item>Every unit the card names gets a ring round each of its living models.</item>
///   <item>An attack card also gets a line from the attacker to the target with an arrowhead at the target.</item>
/// </list>
/// Drawn on the background draw list in board pixels, so it sits on the table (above the bases, under the
/// windows) and the 3D view lifts it onto the board with everything else drawn there.
/// <para>
/// Black under white/gold, the same reason as the leader mark: any single colour is somebody's base colour,
/// a dark edge under a light stroke reads on all of them.
/// </para>
/// <para>
/// Units are found where they stand NOW. A unit that has since died (or never had a body on the table) falls
/// back to where the card says it was at the time, so the line still points somewhere true.
/// </para>
/// </summary>
public static class ReportFocusOverlay
{
    private static readonly uint Edge = ImGui.ColorConvertFloat4ToU32(new Vector4(0.03f, 0.03f, 0.03f, 0.95f));
    private static readonly uint Ring = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
    private static readonly uint LineCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.86f, 0.30f, 1f));

    public static void Draw(ITableState? tableState, float scale, int originX, int originY, float tableH)
    {
        if (tableState == null || DiceTray.Hovered is not { } focus) return;
        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        Vector2 Px(Position p) => new(originX + p.x * scale, originY + (tableH - p.z) * scale);

        var units = tableState.Units.Objects.ToList();
        IUnit? Find(UnitID id) => units.FirstOrDefault(u => u.ID.Equals(id));

        foreach (UnitID id in focus.Units)
        {
            if (Find(id) is not { } unit || !unit.GetIsOnBattlefield()) continue;
            foreach (IModel m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                Vector2 c = Px(m.Position);
                float r = MathF.Max(4f, m.BaseRadiusInches * scale) + 4f;
                dl.AddCircle(c, r, Edge, 32, 5f);
                dl.AddCircle(c, r, Ring, 32, 2.5f);
            }
        }

        Position? from = Where(focus.Attacker, focus.FromAt);
        Position? to = Where(focus.Target, focus.ToAt);
        if (from is { } a && to is { } b) DrawArrow(dl, Px(a), Px(b));

        Position? Where(UnitID? id, Position? fallback)
        {
            if (id is { } uid && Find(uid) is { } u && u.GetIsOnBattlefield()
                && ReportFocusGeometry.Centroid(u.Models.Where(m => m.GetIsAlive()).Select(m => m.Position)) is { } c)
                return c;
            return fallback;
        }
    }

    private static void DrawArrow(ImDrawListPtr dl, Vector2 a, Vector2 b)
    {
        if (ReportFocusGeometry.ArrowHead(a, b, 16f) is not { } head) return;
        dl.AddLine(a, head.Base, Edge, 7f);
        dl.AddTriangleFilled(head.Tip + head.Dir * 2f, head.Left - head.Dir, head.Right - head.Dir, Edge);
        dl.AddLine(a, head.Base, LineCol, 3.5f);
        dl.AddTriangleFilled(head.Tip, head.Left, head.Right, LineCol);
    }
}

/// <summary>Pure geometry for <see cref="ReportFocusOverlay"/> - unit-tested.</summary>
public static class ReportFocusGeometry
{
    public static Position? Centroid(IEnumerable<Position> points)
    {
        float x = 0f, z = 0f;
        int n = 0;
        foreach (Position p in points) { x += p.x; z += p.z; n++; }
        return n == 0 ? null : new Position(x / n, z / n);
    }

    /// <summary>
    /// The arrowhead for a line a -> b: its tip at b, base <paramref name="size"/> back along the line, and
    /// the two back corners. Null when the two points are too close to give the arrow a direction (two
    /// units in base contact, at this zoom) - then only the rings show, which is enough.
    /// </summary>
    public static (Vector2 Tip, Vector2 Base, Vector2 Left, Vector2 Right, Vector2 Dir)? ArrowHead(
        Vector2 a, Vector2 b, float size)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < size * 1.5f) return null;
        Vector2 dir = d / len;
        Vector2 normal = new(-dir.Y, dir.X);
        Vector2 back = b - dir * size;
        return (b, back, back + normal * size * 0.55f, back - normal * size * 0.55f, dir);
    }
}
