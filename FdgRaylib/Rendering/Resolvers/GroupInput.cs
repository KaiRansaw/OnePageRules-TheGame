using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Shared wheel / R input for the placement and movement overlays (#277), replacing three
/// copy-pasted blocks. Plain Wheel and R / Shift+R rotate (15 deg per notch, unchanged semantics);
/// Ctrl+Wheel is the formation cycle.
///
/// <para>
/// The three wheel gestures do not overlap, which is the whole arrangement: plain Wheel turns the
/// unit, Ctrl+Wheel changes its shape, and ALT+Wheel zooms the camera and does nothing else (Kai,
/// 2026-11-02: "make it so that the ONLY way to zoom in 2d is to hold Alt+scroll"). Alt is explicitly
/// excluded below so holding it to zoom does not also spin the unit you happen to be placing - which
/// is the exact collision that made the plain-wheel zoom unusable.
/// </para>
/// </summary>
internal static class GroupInput
{
    public static readonly float RotationStep = MathF.PI / 12f; // 15 deg per wheel notch / key press

    /// <summary>
    /// Rotation delta (radians) and formation-cycle delta for this frame. The wheel and the key are
    /// gated SEPARATELY — the wheel by mouse capture, R by keyboard capture — because tying both to
    /// one combined flag let a latched keyboard capture (ImGui nav focus after any panel-button
    /// click) silently kill the wheel too, which read as "rotation stopped working". Both are muted
    /// while typing or while the Esc menu is open, same as <see cref="ResolverHotkeys"/>.
    /// </summary>
    public static (float rotationDelta, int formationDelta) Read(bool wantMouse, bool wantKeyboard)
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput || EscapeRouter.MenuOpen) return (0f, 0);
        bool shift = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        // Legacy io.KeyCtrl isn't populated by every backend, so also check the keys directly.
        bool ctrl = io.KeyCtrl || ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
        bool alt = io.KeyAlt || ImGui.IsKeyDown(ImGuiKey.LeftAlt) || ImGui.IsKeyDown(ImGuiKey.RightAlt);

        float rotation = 0f;
        int cycle = 0;
        // Alt+Wheel belongs to the camera. Taking it here as well would turn the unit every time the
        // player zoomed in to look at where they were putting it.
        if (wantMouse && !alt && io.MouseWheel != 0f)
        {
            if (ctrl) cycle = io.MouseWheel > 0f ? 1 : -1;
            else rotation += io.MouseWheel > 0f ? RotationStep : -RotationStep;
        }
        if (wantKeyboard && ImGui.IsKeyPressed(ImGuiKey.R))
            rotation += shift ? RotationStep : -RotationStep;
        return (rotation, cycle);
    }
}
