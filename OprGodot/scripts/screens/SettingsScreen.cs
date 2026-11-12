using FdgRaylib.Config;
using FdgRaylib.Network;
using Godot;

namespace OprGodot;

/// <summary>
/// Settings (Kai, 2026-11-07, "Settings Mockup"): one screen, from the main menu and the in-game Esc menu.
/// Tabs Player / Display / Gameplay / Camera / Sound / Controls, a tooltip on every row, and every change
/// saved as it is made - no Save button. The values are the same config file the Raylib build reads, so a
/// setting changed in one build is changed in both.
/// </summary>
public partial class SettingsScreen : Control
{
    private static UserConfig Config => UserConfig.Current;
    private static ViewPreferences View => UserConfig.Current.View;

    public override void _Ready()
    {
        GetNode<Button>("%Back").Pressed += App.I.Back;
        Ui.ScreenTitle(GetNode<Label>("Margin/Column/Header/Title"));
        // Settings rows are short: keep the panel to a readable width instead of the whole screen.
        GetNode<MarginContainer>("Margin").AddThemeConstantOverride("margin_right", 480);
        var tabs = GetNode<TabContainer>("%Tabs");
        AddTab(tabs, "Player", BuildPlayer);
        AddTab(tabs, "Display", BuildDisplay);
        AddTab(tabs, "Gameplay", BuildGameplay);
        AddTab(tabs, "Camera", BuildCamera);
        AddTab(tabs, "Sound", BuildSound);
        AddTab(tabs, "Controls", BuildControls);
    }

    private static void AddTab(TabContainer tabs, string name, Action<VBoxContainer> build)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(rows);
        tabs.AddChild(scroll);
        build(rows);
    }

    // ── Player ───────────────────────────────────────────────────────────────────────────────────

    private static void BuildPlayer(VBoxContainer rows)
    {
        var name = new LineEdit { Text = Config.PlayerName, CustomMinimumSize = new Vector2(360, 0) };
        name.TextChanged += text =>
        {
            Config.PlayerName = text.Trim().Length > 0 ? text.Trim() : UserConfig.DefaultPlayerName;
            UserConfig.Save();
        };
        rows.AddChild(Row("Your name", "What other players see on your seat in the lobby and in the battle report.", name));

        string? address = TailscaleInfo.JoinAddress();
        var addressBox = new HBoxContainer();
        addressBox.AddThemeConstantOverride("separation", 10);
        addressBox.AddChild(Ui.Label(address ?? "Tailscale isn't running on this computer - start it to play with friends.",
            address == null ? "DimLabel" : null));
        if (address != null)
            addressBox.AddChild(Ui.Button("Copy", () => DisplayServer.ClipboardSet(address), "Copy it to send to a friend."));
        rows.AddChild(Row("Your Tailscale address",
            "Friends type this into Join a Game to join a game you host. They need Tailscale and access to this computer.",
            addressBox));
    }

    // ── Display ──────────────────────────────────────────────────────────────────────────────────

    private static void BuildDisplay(VBoxContainer rows)
    {
        rows.AddChild(Check("Fullscreen", "Fill the whole screen, or play in a window. Remembered next time.",
            Config.StartFullscreen, on =>
            {
                Config.StartFullscreen = on;
                DisplayServer.WindowSetMode(on ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
            }));
        rows.AddChild(Check("3D unit models", "Show the models you downloaded in the Asset Lab on the battle table. Off: everyone is the plain token (quicker on a slow computer). Takes effect from the next game.",
            View.ShowUnitModels, on => View.ShowUnitModels = on));
        rows.AddChild(Check("Unit labels", "Show each unit's name over it on the table.",
            View.ShowLabels, on => View.ShowLabels = on));
        rows.AddChild(Check("Table grid", "Draw faint lines every 6\" across the table, to judge distances at a glance.",
            View.ShowGrid, on => View.ShowGrid = on));
        rows.AddChild(Check("Show all tokens",
            "Show every marker on a unit, including the hidden bookkeeping ones the game normally keeps out of sight. Mostly for checking the game.",
            View.ShowAllTokens, on => View.ShowAllTokens = on));
        rows.AddChild(Check("Victory fireworks", "Fireworks in the winner's colours behind the Game Over card.",
            View.ShowVictoryFireworks, on => View.ShowVictoryFireworks = on));
    }

    // ── Gameplay ─────────────────────────────────────────────────────────────────────────────────

    private static void BuildGameplay(VBoxContainer rows)
    {
        rows.AddChild(Slider("Game speed",
            "How fast moves, shots and dice play out. Turn it down to follow the bots' turns more easily. 1.00x is normal.",
            View.GameSpeed, 0.25, 2, v => View.GameSpeed = v, "x"));
        rows.AddChild(Slider("Dice popup time",
            "How long a dice roll stays on screen after it lands. 1.00x is normal. Hovering the dice always holds them.",
            View.DiceLingerScale, 1.0 / 3.0, 2, v => View.DiceLingerScale = v, "x"));
        rows.AddChild(Check("Weapon reach overlay",
            "Shade where a unit's weapons can reach - the unit you are moving or placing, or any unit you hover.",
            View.ShowReachOverlay, on => View.ShowReachOverlay = on));
    }

    // ── Camera ───────────────────────────────────────────────────────────────────────────────────

    private static void BuildCamera(VBoxContainer rows)
    {
        rows.AddChild(Slider("Camera key speed",
            "How fast the keyboard moves, turns, zooms and tilts the camera. 1.00x is normal.",
            View.CameraSpeed, 0.25, 3, v => View.CameraSpeed = v, "x"));
        rows.AddChild(Check("Invert tilt", "Swap which way the camera tilts.", View.InvertTilt, on => View.InvertTilt = on));
    }

    // ── Sound ────────────────────────────────────────────────────────────────────────────────────

    private static void BuildSound(VBoxContainer rows)
    {
        rows.AddChild(Slider("Master volume", "How loud every sound in the game is.", View.MasterVolume, 0, 1,
            v =>
            {
                View.MasterVolume = v;
                AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Math.Max(v, 0.0001f)));
            }, ""));
        foreach (string channel in new[] { "Music", "Effects", "Voices" })
        {
            var slider = new HSlider { MinValue = 0, MaxValue = 100, Value = 80, Editable = false, CustomMinimumSize = new Vector2(360, 0) };
            rows.AddChild(Row(channel, $"{channel} volume. Not in the game yet - the game has no {channel.ToLowerInvariant()} so far.", slider));
        }
    }

    // ── Controls ─────────────────────────────────────────────────────────────────────────────────

    private static void BuildControls(VBoxContainer rows) =>
        rows.AddChild(Ui.Label("The key list arrives with the table (port step 3): the camera, measuring and the table hotkeys " +
                               "are being rebuilt for Godot, and this tab will list exactly the keys that build uses.", "DimLabel", wrap: true));

    // ── Row builders ─────────────────────────────────────────────────────────────────────────────

    private static Control Row(string label, string tooltip, Control control)
    {
        var row = new HBoxContainer { TooltipText = tooltip };
        row.AddThemeConstantOverride("separation", 20);
        Label name = Ui.Label(label);
        name.CustomMinimumSize = new Vector2(280, 0);
        name.TooltipText = tooltip;
        name.MouseFilter = MouseFilterEnum.Stop;
        row.AddChild(name);
        control.TooltipText = tooltip;
        row.AddChild(control);
        return row;
    }

    private static Control Check(string label, string tooltip, bool value, Action<bool> set)
    {
        var box = new CheckBox { ButtonPressed = value, Text = value ? "On" : "Off" };
        box.Toggled += on =>
        {
            box.Text = on ? "On" : "Off";
            set(on);
            UserConfig.Save();
        };
        return Row(label, tooltip, box);
    }

    private static Control Slider(string label, string tooltip, float value, double min, double max, Action<float> set, string unit)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(360, 0) };
        var readout = Ui.Label($"{value:0.00}{unit}");
        slider.ValueChanged += v =>
        {
            readout.Text = $"{v:0.00}{unit}";
            set((float)v);
            UserConfig.Save();
        };
        line.AddChild(slider);
        line.AddChild(readout);
        slider.TooltipText = tooltip;
        return Row(label, tooltip, line);
    }
}
