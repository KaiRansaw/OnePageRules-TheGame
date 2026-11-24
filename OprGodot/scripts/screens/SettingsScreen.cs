using FdgRaylib.Terrain;
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
        // Card size (Kai, 2026-11-17): Army Forge cards, the army picture grid, lobby army-list portraits.
        var cards = new HSlider { Name = "CardSize", MinValue = 50, MaxValue = 150, Step = 5, Value = View.CardSize, CustomMinimumSize = new Vector2(260, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var cardsLabel = Hud.HudKit.Text($"{View.CardSize} %", 16, Hud.HudKit.Fg, Hud.HudKit.Semi);
        cards.ValueChanged += v => { View.CardSize = (int)v; cardsLabel.Text = $"{(int)v} %"; };
        cards.DragEnded += _ => FdgRaylib.Config.UserConfig.Save();
        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", 12);
        cardRow.AddChild(cards);
        cardRow.AddChild(cardsLabel);
        rows.AddChild(Row("Card size", "How big unit cards and army tiles are: the Army Forge cards, the army picture grid (4 across at 100 %) and the lobby's army-list portraits. Smaller = more on screen. Applies next time those screens open.", cardRow));
        // UI themes from art packs (Kai, 2026-11-16) - Classic is the drawn look; each pack is one entry in Hud.Skin.All.
        var themes = new OptionButton { Name = "UiTheme" };
        var ids = new List<string> { "classic" };
        themes.AddItem("Classic (drawn panels)");
        foreach (var d in Hud.Skin.All) { ids.Add(d.Id); themes.AddItem(d.Label); }
        themes.Selected = Math.Max(0, ids.IndexOf(View.UiSkin));
        themes.ItemSelected += i => Hud.Skin.Set(ids[(int)i]);
        rows.AddChild(Row("UI theme", "The art on every panel and button. Each pack has a Grimdark colouring and an Age of Fantasy one. Classic = the plain cut-corner panels. Changes straight away.", themes));
        // Kai, 2026-11-21: both built-in terrain sets in the game, the player picks.
        var tstyle = new OptionButton { Name = "TerrainStyle" };
        tstyle.AddItem("Stylised (painted look)");
        tstyle.AddItem("Realistic (photo-scanned)");
        tstyle.Selected = View.TerrainStyle == FdgRaylib.Terrain.TerrainPieces.Realistic ? 1 : 0;
        tstyle.ItemSelected += i => { View.TerrainStyle = i == 1 ? FdgRaylib.Terrain.TerrainPieces.Realistic : FdgRaylib.Terrain.TerrainPieces.Stylised; FdgRaylib.Config.UserConfig.Save(); };
        rows.AddChild(Row("Terrain style", "Which set of built-in terrain models the table uses. A terrain kind with no hand-built piece in that style yet keeps the older look. Your Warhammer terrain is not affected. Takes effect from the next game.", tstyle));
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
        rows.AddChild(Check("Weather effects", "Draw a map's weather - rain, snow, fog, a thunderstorm... - over the table. Only for looks; turn it off on a slow computer.",
            View.WeatherEffects, on => View.WeatherEffects = on));
        RoomRows(rows);
    }

    // Kai, 2026-11-14: a room around the table - 360-degree photos from Poly Haven, downloaded on this computer.
    private static void RoomRows(VBoxContainer rows)
    {
        var pick = new OptionButton { Name = "Room", CustomMinimumSize = new Vector2(360, 0) };
        var ids = new List<string>();
        void Fill()
        {
            pick.Clear();
            ids.Clear();
            void Add(string id, string text, bool disabled = false)
            {
                pick.AddItem(text);
                pick.SetItemDisabled(pick.ItemCount - 1, disabled);
                ids.Add(id);
            }
            Add(TableRooms.Battlefield, "Battlefield - the land goes on past the table");
            Add(TableRooms.Auto, "Auto - grimdark for GF games, fantasy for AoF");
            Add(TableRooms.None, "None - the plain dark background");
            foreach (var r in TableRooms.All)
            {
                bool have = TableRooms.Have(r);
                Add(r.Id, $"{r.Kind}: {r.Name}" + (have ? "" : "  (not downloaded)"), !have);
            }
            pick.Selected = Math.Max(0, ids.IndexOf(View.Room));
        }
        Fill();
        pick.ItemSelected += i => { View.Room = ids[(int)i]; UserConfig.Save(); };
        rows.AddChild(Row("Around the table",
            "Battlefield: no table edge - the ground runs on to the map's horizon (its sky, hills and haze). Or a 360-degree photo of a room around the table. Auto picks a downloaded room that suits the game. Takes effect when you leave Settings.", pick));
        rows.AddChild(Slider("Room brightness", "How bright the room behind the table is. The table itself is lit the same either way.",
            View.RoomBrightness, 0.2, 1.5, v => View.RoomBrightness = v, "x"));

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        var status = Ui.Label("");
        int have = TableRooms.All.Count(r => TableRooms.Have(r));
        status.Text = $"{have} of {TableRooms.All.Count} rooms on this computer.";
        var dl = new Button { Name = "DownloadRooms", Text = "Download rooms", CustomMinimumSize = new Vector2(220, 36), FocusMode = FocusModeEnum.None,
            TooltipText = "Download the rooms not on this computer yet (about 20 MB each) from Poly Haven." };
        dl.Pressed += () =>
        {
            dl.Disabled = true;
            var todo = TableRooms.All.Where(r => !TableRooms.Have(r)).ToList();
            _ = Task.Run(async () =>
            {
                var (saved, failed, error) = await TableRooms.DownloadAsync(todo, msg => Callable.From(() => { if (GodotObject.IsInstanceValid(status)) status.Text = msg; }).CallDeferred(), CancellationToken.None);
                Callable.From(() =>
                {
                    if (!GodotObject.IsInstanceValid(status)) return;
                    int now = TableRooms.All.Count(r => TableRooms.Have(r));
                    status.Text = $"{now} of {TableRooms.All.Count} rooms on this computer." + (failed > 0 ? $" {failed} failed: {error}" : "");
                    dl.Disabled = false;
                    Fill();
                }).CallDeferred();
            });
        };
        line.AddChild(dl);
        line.AddChild(status);
        rows.AddChild(Row("Rooms", "Room photos are free (CC0) from Poly Haven, polyhaven.com.", line));
        rows.AddChild(Row("", "", Ui.Label("Room photos from Poly Haven (polyhaven.com), free to use (CC0).")));
    }

    // ── Gameplay ─────────────────────────────────────────────────────────────────────────────────

    private static void BuildGameplay(VBoxContainer rows)
    {
        // "Game speed" is not offered (Kai, 2026-11-19: hide it for now): the Godot battle plays at one speed and nothing
        // read the slider. The saved value (View.GameSpeed) stays for the day it does.
        // "Dice popup time" is not offered either (Kai, 2026-11-19: hide it for now) - nothing in the Godot battle read it.
        // The saved value (View.DiceLingerScale) stays.
        rows.AddChild(Check("Weapon reach overlay",
            "Shade where a unit's weapons can reach - the unit you are moving or placing, or any unit you hover.",
            View.ShowReachOverlay, on => View.ShowReachOverlay = on));
    }

    // ── Camera ───────────────────────────────────────────────────────────────────────────────────

    private static void BuildCamera(VBoxContainer rows)
    {
        rows.AddChild(Slider("Camera key speed",
            "How fast the keyboard moves and turns the camera. 1.00x is normal.",
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
        rows.AddChild(Check("Menu sounds", "A soft tick when the mouse or the keyboard cursor moves onto a button, and a click when you press one.",
            View.MenuSounds, on => View.MenuSounds = on));
        foreach (string channel in new[] { "Music", "Effects", "Voices" })
        {
            var slider = new HSlider { MinValue = 0, MaxValue = 100, Value = 80, Editable = false, CustomMinimumSize = new Vector2(360, 0) };
            rows.AddChild(Row(channel, $"{channel} volume. Not in the game yet - the game has no {channel.ToLowerInvariant()} so far.", slider));
        }
    }

    // ── Controls ─────────────────────────────────────────────────────────────────────────────────

    private static void BuildControls(VBoxContainer rows)
    {
        // Controller (Kai, 2026-11-24: couch play, Xbox and PlayStation pads). The exact mapping is Kai's to settle later;
        // the list below is read from Pads.Pad.All, so it always shows what the buttons really do.
        ControllerPreferences pad = Config.Controller;
        rows.AddChild(Section("Controller"));
        rows.AddChild(Check("Use a controller", "Play with an Xbox or PlayStation controller. Off: the game ignores every controller.",
            pad.Enabled, on => pad.Enabled = on));
        var glyphs = new List<Control>();
        var style = new OptionButton { Name = "PadPrompts" };
        style.AddItem("Auto - match the controller");
        style.AddItem("Xbox (A B X Y)");
        style.AddItem("PlayStation (Cross Circle Square Triangle)");
        style.Selected = pad.Prompts switch { "xbox" => 1, "playstation" => 2, _ => 0 };
        style.ItemSelected += i =>
        {
            pad.Prompts = i switch { 1 => "xbox", 2 => "playstation", _ => "auto" };
            UserConfig.Save();
            foreach (Control g in glyphs) g.QueueRedraw();
        };
        rows.AddChild(Row("Button pictures", "Which button pictures the hints and this list show. Auto works it out from the controller plugged in.", style));
        rows.AddChild(Slider("Pointer speed", "How fast the left stick moves the on-screen pointer (the battle, map editor and Asset Lab use the pointer). 1.00x is normal.",
            pad.PointerSpeed, 0.25, 3, v => pad.PointerSpeed = v, "x"));
        rows.AddChild(Slider("Camera stick speed", "How fast the right stick turns and tilts the battle camera, and how fast the pointer pans it at the screen edge. 1.00x is normal.",
            pad.CameraSpeed, 0.25, 3, v => pad.CameraSpeed = v, "x"));
        rows.AddChild(Slider("Stick deadzone", "How far a stick has to move before the game reacts. Raise it if the pointer or camera drifts by itself.",
            pad.Deadzone, 0.05, 0.5, v => pad.Deadzone = v, ""));
        rows.AddChild(Check("Invert camera tilt", "Swap which way pushing the right stick up and down tilts the battle camera.",
            pad.InvertCameraTilt, on => pad.InvertCameraTilt = on));
        rows.AddChild(Check("Swap players", "Two players at one computer, a controller each: the first controller plugged in plays seat 1 and the second plays seat 2. Tick this to swap them. Only the player whose turn it is can play; the other controller waits.",
            pad.SwapPlayers, on => pad.SwapPlayers = on));
        rows.AddChild(Check("Button hints", "Show a strip along the bottom of the screen with what the main buttons do, while you are using the controller.",
            pad.ShowHints, on => pad.ShowHints = on));
        var connected = Input.GetConnectedJoypads();
        rows.AddChild(Row("Connected", "The controllers this computer can see right now. Plug one in (or pair it over Bluetooth) and reopen this tab.",
            Ui.Label(connected.Count == 0 ? "No controller found." : string.Join(", ", connected.Select(d => Input.GetJoyName(d))),
                connected.Count == 0 ? "DimLabel" : null)));

        foreach (var (where, title, tip) in new[]
        {
            (Pads.PadContext.Menus, "In menus", "Menus: a highlight moves between buttons."),
            (Pads.PadContext.Pointer, "With the pointer", "The battle, map editor, Asset Lab and campaign map use an on-screen pointer that works like the mouse. The left stick click swaps between the pointer and the menu highlight on any screen."),
            (Pads.PadContext.Battle, "In battle (on top of the pointer)", "Extra buttons during a battle. The right stick turns and tilts the camera; push the pointer against a screen edge to pan."),
        })
        {
            rows.AddChild(Section(title, tip));
            foreach (Pads.PadBinding b in Pads.Pad.All.Where(x => x.Where == where))
            {
                var line = new HBoxContainer { TooltipText = b.Does };
                line.AddThemeConstantOverride("separation", 14);
                var g = new Pads.PadGlyphView { Button = b.Button, CustomMinimumSize = new Vector2(34, 30) };
                glyphs.Add(g);
                line.AddChild(g);
                var name = Ui.Label(Pads.Pad.Name(b.Button));
                name.CustomMinimumSize = new Vector2(220, 0);
                line.AddChild(name);
                line.AddChild(Ui.Label(b.Does));
                rows.AddChild(line);
            }
        }
        rows.AddChild(Ui.Label("Text boxes (names, search) still need the keyboard - or Steam's on-screen keyboard on a Steam Deck.", "DimLabel", wrap: true));
    }

    private static Control Section(string title, string tooltip = "")
    {
        Label l = Hud.HudKit.Text(title, 20, Hud.HudKit.Lime, Hud.HudKit.Semi);
        l.TooltipText = tooltip;
        l.MouseFilter = tooltip.Length > 0 ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        return l;
    }

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
