using System.Numerics;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Where the unit taking its turn sits on the screen this frame, so decision pop-ups can open right next to
/// it instead of in the side panel. Set by the renderer every frame (2D and 3D) before the resolvers draw.
/// </summary>
public static class PopupAnchor
{
    /// <summary>The unit taking its turn, or null between activations.</summary>
    public static IUnit? Unit;

    /// <summary>The game's progress (round, who has activated), for click-to-switch checks.</summary>
    public static IGameProgress? Progress;

    /// <summary>Screen-space box around the acting unit's models, or null when it has none on the table.</summary>
    public static Rectangle? UnitScreenRect;

    /// <summary>The live table (units, terrain), for range checks like "is anything in charge range".</summary>
    public static ITableState? TableState;

    /// <summary>A unit a tool is working on that isn't the acting unit (the unit being deployed), so the
    /// unit info window can show it. Cleared by the renderer each frame after it is read.</summary>
    public static IUnit? FocusUnit;

    /// <summary>Top-left for a thin tool strip centred just under the top bar, clear of the table's edges
    /// and deployment zones (used while moving or placing units, and for the unit cards).</summary>
    public static Vector2 PlaceTopStrip(Vector2 size) => new(MathF.Max(8f, (AreaW - size.X) / 2f), 72f);

    /// <summary>Top-left for a panel of <paramref name="size"/> centred along the bottom of the board, out of
    /// the way of the table (used while moving or placing units).</summary>
    public static Vector2 PlaceBottomCenter(Vector2 size) =>
        new(MathF.Max(8f, (AreaW - size.X) / 2f), MathF.Max(70f, ScreenH - size.Y - 12f));

    /// <summary>The unit's picture for cards (its portrait, else its full picture), or null if it has none.</summary>
    public static Func<IUnit, Texture2D?>? CardImageFor;

    /// <summary>The unit under the mouse this frame (from the table hit test), or null.</summary>
    public static IUnit? HoveredUnit;

    /// <summary>True on the frame a right-click is released over empty table: not dragged (right-drag turns the
    /// 3D camera), not on a unit (that opens its card), not over a window.</summary>
    public static bool RightClickOnEmptyTable()
    {
        var io = ImGuiNET.ImGui.GetIO();
        return ImGuiNET.ImGui.IsMouseReleased(ImGuiNET.ImGuiMouseButton.Right)
            && io.MouseDragMaxDistanceSqr[1] < 16f
            && !io.WantCaptureMouse
            && HoveredUnit == null;
    }

    /// <summary>Width of the board area (the screen minus the right column) and the screen height.</summary>
    public static float AreaW = 1280f;
    public static float ScreenH = 720f;

    /// <summary>
    /// Top-left corner for a pop-up of <paramref name="size"/>: beside the unit (to its right, or its left
    /// when there is no room), vertically centred on it, kept inside the board area and below the top bar.
    /// With no unit on the table it sits top-centre of the board.
    /// </summary>
    public static Vector2 Place(Vector2 size)
    {
        const float gap = 28f, edge = 8f, topBar = 70f;
        if (UnitScreenRect is not Rectangle r)
            return new Vector2(MathF.Max(edge, (AreaW - size.X) / 2f), topBar);

        float x = r.X + r.Width + gap;
        if (x + size.X > AreaW - edge) x = r.X - gap - size.X;
        x = Math.Clamp(x, edge, MathF.Max(edge, AreaW - size.X - edge));
        float y = r.Y + r.Height / 2f - size.Y / 2f;
        y = Math.Clamp(y, topBar, MathF.Max(topBar, ScreenH - size.Y - edge));
        return new Vector2(x, y);
    }
}
