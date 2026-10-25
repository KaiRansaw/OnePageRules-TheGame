using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Where the pointer is ON THE TABLE, in the board's drawing coordinates. Every table tool (moving,
/// placing, measuring, targeting, hover) reads the pointer through here instead of ImGui's raw mouse
/// position, so the same tool works in both views:
/// <list type="bullet">
/// <item>2D: simply the mouse position - the board is drawn straight onto the screen.</item>
/// <item>3D: the renderer casts the mouse into the 3D scene, finds the point of the table it lands on,
/// and hands back where that point sits on the flat "virtual board" the tools draw onto (that board is
/// then painted onto the 3D table). A pointer that misses the table reports a spot far off the board.</item>
/// </list>
/// </summary>
internal static class TableInput
{
    /// <summary>Set by the renderer each frame while the 3D view is up; null in 2D.</summary>
    internal static Vector2? Override;

    /// <summary>3D only: the model whose upright card the pointer is over, if any. Lets the hover/click
    /// picker select a model by its card as well as by its base on the table.</summary>
    internal static ModelID? CardHit;

    /// <summary>The pointer position in board drawing coordinates.</summary>
    public static Vector2 MousePos => Override ?? ImGui.GetIO().MousePos;

    /// <summary>A spot no board ever reaches: what the pointer reads when it is off the 3D table.</summary>
    public static readonly Vector2 OffTable = new(-100000f, -100000f);
}
