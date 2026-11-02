using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Settings, from Kai's main-menu mock (screen "9"). Two sections, both deliberately incomplete and
/// honest about it.
///
/// <para>
/// <b>Sound is a shape, not a feature.</b> The game has no audio of any kind - no mixer, no clips, no
/// device. The switches are here because Kai asked for the section and wants music, SFX and voice lines
/// separable when there is something to separate, and building the shape now costs nothing. They say so
/// on screen rather than pretending to work: a volume slider that does nothing is worse than no slider,
/// because it reads as broken audio rather than absent audio.
/// </para>
///
/// <para>
/// <b>Keybinds are listed, not editable.</b> Kai, 2026-11-02: <i>"make the screen but we'll map
/// everything later. Just give me a list of our current keybinds and I'll tell you what to change."</i>
/// Remapping needs an input layer the game does not have - every key is checked where it is used, by
/// name - and that is a real slice rather than a screen, so this is the list and nothing more. It is
/// also the same list the in-game F1 panel shows, read from one place, so the two cannot disagree.
/// </para>
/// </summary>
public sealed class SettingsScreen : IAppScreen
{
    public Action? OnBack;

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.78f, 0.45f, 1f);

    private int _tab;

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##settings",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar);

        float margin = 24f;
        ImGui.SetCursorPos(new Vector2(margin, margin));
        UiText.Colored(Accent, "SETTINGS");
        ImGui.Separator();

        if (UiButton.NavigateSmall("Sound")) _tab = 0;
        ImGui.SameLine();
        if (UiButton.NavigateSmall("Controls")) _tab = 1;
        ImGui.SameLine();
        if (UiButton.NavigateSmall("Display")) _tab = 2;
        ImGui.Separator();

        float bodyH = screenH - ImGui.GetCursorPosY() - 80f;
        ImGui.BeginChild("##settingsbody", new Vector2(screenW - margin * 2f, bodyH));
        switch (_tab)
        {
            case 0: DrawSound(); break;
            case 1: DrawControls(); break;
            default: DrawDisplay(); break;
        }
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(margin, screenH - 64f));
        if (UiButton.Back("Back", new Vector2(180f, 44f)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            OnBack?.Invoke();

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawSound()
    {
        UiText.Colored(Warn, "There is no audio in the game yet, so none of these do anything.");
        UiText.Wrapped("They are here so the shape is settled: music, effects and voice lines are "
                       + "separate channels, each with its own level and its own mute. Nothing is wired "
                       + "to a mixer because there is no mixer.");
        ImGui.Spacing();

        ImGui.BeginDisabled();
        bool muted = false;
        UiButton.Checkbox("Mute everything", ref muted);
        ImGui.Spacing();
        foreach (string channel in new[] { "Music", "Sound effects", "Voice lines" })
        {
            int level = 80;
            ImGui.TextUnformatted(channel);
            ImGui.SameLine(200f);
            ImGui.SetNextItemWidth(320f);
            ImGui.SliderInt($"##vol{channel}", ref level, 0, 100);
        }
        ImGui.EndDisabled();
    }

    private static void DrawControls()
    {
        UiText.Colored(Warn, "Listed, not yet editable - remapping is its own job.");
        UiText.Wrapped("This is the same list the game shows on F1 while you are playing, read from one "
                       + "place so the two cannot disagree.");
        ImGui.Spacing();

        string? section = null;
        foreach ((string keys, string does, string group) in ControlsReference.All)
        {
            if (group != section)
            {
                section = group;
                ImGui.Spacing();
                UiText.Colored(Accent, group.ToUpperInvariant());
                ImGui.Separator();
            }
            ImGui.TextUnformatted(keys);
            ImGui.SameLine(260f);
            UiText.Colored(Dim, does);
        }
    }

    private static void DrawDisplay()
    {
        UiText.Wrapped("Fullscreen is F11 while the game is running. Everything else about the window "
                       + "follows the desktop.");
        ImGui.Spacing();
        UiText.Colored(Dim, "Nothing to set here yet.");
    }
}
