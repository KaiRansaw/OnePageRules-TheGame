using System.Numerics;
using FdgRaylib.Audio;
using FdgRaylib.Config;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The one Settings screen (Kai, 2026-11-07, "Settings Mockup"), opened from the main menu and from the
/// in-game Esc menu. Tabs: Player / Display / Gameplay / Camera / Sound / Controls. The in-game Options
/// page's real settings live here now; every row has a hover in plain words; every change saves the
/// moment it is made (no Save button).
///
/// <para><see cref="Draw"/> is the full-screen version for the main menu; <see cref="DrawBody"/> is the
/// same content, which the Esc menu draws inside its own window so the game keeps running behind it.</para>
///
/// <para>Rental layout: plain tabs and rows.</para>
/// </summary>
public sealed class SettingsScreen : IAppScreen
{
    public Action? OnBack;

    /// <summary>The app's audio device, for the master volume (null or disabled = no sound device).</summary>
    public AudioManager? Audio { get; set; }

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);

    private string _name = UserConfig.Current.PlayerName;

    public void OnShown() => _name = UserConfig.Current.PlayerName;

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
        ImGui.PushFont(RaylibRenderer.LargeFont);
        UiText.Colored(Accent, "SETTINGS");
        ImGui.PopFont();
        ImGui.Separator();

        float bodyH = screenH - ImGui.GetCursorPosY() - 80f;
        ImGui.SetCursorPosX(margin);
        ImGui.BeginChild("##settingsbody", new Vector2(screenW - margin * 2f, bodyH));
        DrawBody();
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(margin, screenH - 64f));
        if (UiButton.Back("Back", new Vector2(180f, 44f))
            || (ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput))
        {
            CommitName();
            OnBack?.Invoke();
        }

        ImGui.End();
        ImGui.PopStyleColor();
    }

    /// <summary>The tabs and their rows, drawn into the current window.</summary>
    public void DrawBody()
    {
        if (!ImGui.BeginTabBar("##settingstabs")) return;
        Tab("Player", DrawPlayer);
        Tab("Display", DrawDisplay);
        Tab("Gameplay", DrawGameplay);
        Tab("Camera", DrawCamera);
        Tab("Sound", DrawSound);
        Tab("Controls", DrawControls);
        ImGui.EndTabBar();

        static void Tab(string name, Action body)
        {
            if (!ImGui.BeginTabItem(name)) return;
            ImGui.Spacing();
            body();
            ImGui.EndTabItem();
        }
    }

    /// <summary>Writes a typed name that has not been saved yet (the box saves when you leave it).</summary>
    public void CommitName()
    {
        string trimmed = _name.Trim();
        if (trimmed.Length == 0) { _name = UserConfig.Current.PlayerName; return; }
        if (trimmed == UserConfig.Current.PlayerName) return;
        UserConfig.Current.PlayerName = trimmed;
        UserConfig.Save();
    }

    // ── Player ─────────────────────────────────────────────────────────────────────────────────────

    private void DrawPlayer()
    {
        Label("Your name", "The name other players see in the lobby and the game. Saved as soon as you click away.");
        ImGui.SetNextItemWidth(360f);
        ImGui.InputText("##playername", ref _name, 32);
        if (ImGui.IsItemHovered()) UiText.TooltipWrapped("The name other players see in the lobby and the game.");
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitName();

        ImGui.Spacing();
        Label("Your Tailscale address", "What a friend types into Join a Game to reach your game. Friends only " +
            "ever connect over Tailscale, so both of you need it running.");
        string? address = FdgRaylib.Network.TailscaleInfo.JoinAddress();
        if (address != null)
        {
            ImGui.TextUnformatted(address);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy##tailscale")) ImGui.SetClipboardText(address);
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Copies the address so you can paste it to a friend.");
            UiText.Colored(Dim, "Give this to a friend: they type it into Join a Game on the main menu.");
        }
        else
        {
            UiText.Colored(Dim, "Tailscale isn't running on this computer - start it to play with friends.");
        }
    }

    // ── Display ────────────────────────────────────────────────────────────────────────────────────

    private static void DrawDisplay()
    {
        bool fullscreen = UserConfig.Current.StartFullscreen;
        if (Check("Fullscreen", ref fullscreen,
                "Fill the whole screen, or play in a window. F11 does the same at any time. Remembered next time."))
        {
            // Same as F11 (RaylibRenderer.Run): flip the window and remember it.
            Raylib.ToggleBorderlessWindowed();
            UserConfig.Current.StartFullscreen = fullscreen;
            UserConfig.Save();
        }
        Check("Unit labels", ref ViewSettings.ShowLabels,
            "Show each unit's name over it on the table. N switches it during a game.");
        Check("Table grid", ref ViewSettings.ShowGrid,
            "Draw faint lines every 6\" across the table, to judge distances at a glance.");
        Check("Show all tokens", ref ViewSettings.ShowAllTokens,
            "Show every marker on a unit, including the hidden bookkeeping ones the game normally keeps out of " +
            "sight. Mostly for checking the game. T switches it during a game.");
        Check("Victory fireworks", ref ViewSettings.ShowVictoryFireworks,
            "Fireworks in the winner's colours behind the Game Over card.");
    }

    // ── Gameplay ───────────────────────────────────────────────────────────────────────────────────

    private static void DrawGameplay()
    {
        Slider("Game speed", ref ViewSettings.GameSpeed, 0.25f, 2f, "%.2fx",
            "How fast moves, shots and dice play out. Turn it down to follow the bots' turns more easily. " +
            "1.00x is normal.");
        Slider("Dice popup time", ref ViewSettings.DiceLingerScale, ViewSettings.DiceLingerMin,
            ViewSettings.DiceLingerMax, "%.2fx",
            "How long a dice roll stays on screen after it lands. 1.00x is normal. Hovering the dice always " +
            "holds them.");
        Check("Weapon reach overlay", ref ViewSettings.ShowReachOverlay,
            "Shade where a unit's weapons can reach - the unit you are moving or placing, or any unit you hover. " +
            "B switches it during a game.");
    }

    // ── Camera ─────────────────────────────────────────────────────────────────────────────────────

    private static void DrawCamera()
    {
        Slider("Camera key speed", ref ViewSettings.CameraSpeed, 0.25f, 3f, "%.2fx",
            "How fast the keyboard moves, turns, zooms and tilts the camera (WASD, Q / E, Shift + W / S, " +
            "Shift + Q / E). 1.00x is normal.");
        Check("Invert tilt", ref ViewSettings.InvertTilt,
            "Swap which way Shift + Q and Shift + E tilt the 3D camera.");
    }

    // ── Sound ──────────────────────────────────────────────────────────────────────────────────────

    private void DrawSound()
    {
        bool device = Audio is { Enabled: true };
        ImGui.BeginDisabled(!device);
        float vol = ViewSettings.MasterVolume;
        Label("Master volume", "How loud every sound in the game is.");
        ImGui.SetNextItemWidth(360f);
        if (ImGui.SliderFloat("##mastervolume", ref vol, 0f, 1f, "%.2f"))
        {
            ViewSettings.MasterVolume = vol;
            Audio?.SetMasterVolume(vol);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.TooltipWrapped("How loud every sound in the game is.");
        if (ImGui.IsItemDeactivatedAfterEdit()) ViewSettings.Save();
        ImGui.EndDisabled();
        if (!device) UiText.Colored(Dim, "No sound device was found on this computer.");

        ImGui.Spacing();
        foreach (string channel in new[] { "Music", "Effects", "Voices" })
        {
            ImGui.BeginDisabled();
            Label(channel, "Not in the game yet.");
            int level = 80;
            ImGui.SetNextItemWidth(360f);
            ImGui.SliderInt($"##vol{channel}", ref level, 0, 100);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiText.TooltipWrapped($"{channel} volume. Not in the game yet - the game has no {channel.ToLowerInvariant()} so far.");
            ImGui.SameLine();
            UiText.Colored(Dim, "Not in the game yet");
        }
    }

    // ── Controls ───────────────────────────────────────────────────────────────────────────────────

    private static void DrawControls()
    {
        UiText.Colored(Dim, "Keys can't be changed yet. Press F1 during a game to see this list there.");
        ImGui.Spacing();

        string? section = null;
        foreach ((string keys, string does, string group, string tip) in ControlsReference.All)
        {
            if (group != section)
            {
                section = group;
                ImGui.Spacing();
                UiText.Colored(Accent, group.ToUpperInvariant());
                ImGui.Separator();
            }
            ImGui.TextUnformatted(keys);
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped(tip);
            ImGui.SameLine(260f);
            UiText.Colored(Dim, does, wrap: false);
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped(tip);
        }
    }

    // ── Rows ───────────────────────────────────────────────────────────────────────────────────────

    private static void Label(string text, string tip)
    {
        ImGui.TextUnformatted(text);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.TooltipWrapped(tip);
    }

    /// <summary>A checkbox with a hover; saves the moment it flips.</summary>
    private static bool Check(string label, ref bool value, string tip)
    {
        bool changed = UiButton.Checkbox(label, ref value);
        if (ImGui.IsItemHovered()) UiText.TooltipWrapped(tip);
        if (changed) ViewSettings.Save();
        ImGui.Spacing();
        return changed;
    }

    /// <summary>A labelled slider with a hover; saves when you let go of it.</summary>
    private static void Slider(string label, ref float value, float min, float max, string format, string tip)
    {
        Label(label, tip);
        ImGui.SetNextItemWidth(360f);
        ImGui.SliderFloat($"##{label}", ref value, min, max, format);
        if (ImGui.IsItemHovered()) UiText.TooltipWrapped(tip);
        if (ImGui.IsItemDeactivatedAfterEdit()) ViewSettings.Save();
        ImGui.Spacing();
    }
}
