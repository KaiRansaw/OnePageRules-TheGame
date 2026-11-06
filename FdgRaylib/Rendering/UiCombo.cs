using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// <c>ImGui.BeginCombo</c> whose open list uses the same text size as the box it drops from. A combo's
/// list is its own popup window, and a popup does not inherit the scale a panel set with
/// <c>SetWindowFontScale</c> - so in a scaled panel (lobby seat cards, the army picker) the list opened
/// in noticeably smaller text than the box (found 2026-11-06). Pair with <c>ImGui.EndCombo</c> as usual.
/// </summary>
internal static class UiCombo
{
    public static bool Begin(string label, string preview, ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        float want = ImGui.GetFontSize();
        if (!ImGui.BeginCombo(label, preview, flags)) return false;
        float have = ImGui.GetFontSize();
        if (have > 0f && MathF.Abs(want - have) > 0.5f) ImGui.SetWindowFontScale(want / have);
        return true;
    }
}
