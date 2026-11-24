using FdgRaylib.Config;
using Godot;

namespace OprGodot.Pads;

/// <summary>Every button on an Xbox / PlayStation pad, by position (Godot reads both the same way through SDL).</summary>
public enum PadButton
{
    A, B, X, Y, LB, RB, LT, RT, LStick, RStick, Back, Start, DUp, DDown, DLeft, DRight,
}

/// <summary>Where a binding works. Menus = moving a highlight between buttons; Pointer = the on-screen pointer
/// (the battle, the map editor, the Asset Lab - anything you click on rather than pick from); Battle = on top of
/// Pointer while a battle is on screen.</summary>
public enum PadContext { Menus, Pointer, Battle }

/// <summary>What a binding does.</summary>
public enum PadDo
{
    /// <summary>Godot's own menu handling (move highlight, press, back) - listed so the Controls tab can show it.</summary>
    Native,
    Click, RightClick, Key, TogglePointer, Info, PrevTab, NextTab, ScrollUp, ScrollDown,
    /// <summary>Battle: jump the pointer to the previous / next pickable unit, or select the next ready unit.</summary>
    PrevTarget, NextTarget, NextUnit,
}

/// <summary>One line of the controller layout: which button, where, and what it does in plain words.</summary>
public sealed record PadBinding(string Id, PadButton Default, PadContext Where, string Does, PadDo Act, Key Key = Key.None)
{
    /// <summary>The button it is on now - Kai's override from Settings when there is one, else the default.</summary>
    public PadButton Button => Pad.Override(Id) ?? Default;
}

/// <summary>
/// The controller layout (Kai, 2026-11-24: couch play with Xbox and PlayStation pads; "I'll work on adding exact button
/// mapping later"). Everything a button does is one line in <see cref="All"/> - so the Controls tab, the hint strip and
/// <see cref="PadDriver"/> all read the same list, and changing the mapping is changing a line (or a saved override in
/// <see cref="ControllerPreferences.Buttons"/>), nothing else.
///
/// <para>Battle bindings press the keyboard key the battle already listens for (<see cref="PadDo.Key"/>), so a pad
/// does exactly what the keyboard does and nothing has two code paths.</para>
/// </summary>
public static class Pad
{
    public static ControllerPreferences Prefs => UserConfig.Current.Controller;

    public static readonly IReadOnlyList<PadBinding> All = new List<PadBinding>
    {
        // Menus: Godot moves the highlight with the d-pad / left stick and presses with A, backs out with B.
        new("menu.move",   PadButton.DUp,    PadContext.Menus, "Move the highlight (d-pad or left stick)", PadDo.Native),
        new("menu.press",  PadButton.A,      PadContext.Menus, "Press the highlighted button", PadDo.Native),
        new("menu.back",   PadButton.B,      PadContext.Menus, "Back to the previous screen", PadDo.Native),
        new("menu.info",   PadButton.Y,      PadContext.Menus, "Hold: what the highlighted thing does (its hover text)", PadDo.Info),
        new("menu.tabl",   PadButton.LB,     PadContext.Menus, "Previous tab", PadDo.PrevTab),
        new("menu.tabr",   PadButton.RB,     PadContext.Menus, "Next tab", PadDo.NextTab),
        new("menu.scrollu",PadButton.LT,     PadContext.Menus, "Scroll up (or right stick)", PadDo.ScrollUp),
        new("menu.scrolld",PadButton.RT,     PadContext.Menus, "Scroll down (or right stick)", PadDo.ScrollDown),
        new("menu.pointer",PadButton.LStick, PadContext.Menus, "Switch to the pointer", PadDo.TogglePointer),

        // Pointer: the left stick moves a pointer that clicks like the mouse.
        new("ptr.click",   PadButton.A,      PadContext.Pointer, "Click (hold and move to drag)", PadDo.Click),
        new("ptr.right",   PadButton.B,      PadContext.Pointer, "Right click - step back / let go", PadDo.RightClick),
        new("ptr.esc",     PadButton.Start,  PadContext.Pointer, "Menu / cancel (Esc)", PadDo.Key, Key.Escape),
        new("ptr.menus",   PadButton.LStick, PadContext.Pointer, "Switch to the menu highlight", PadDo.TogglePointer),
        new("ptr.scrollu", PadButton.LT,     PadContext.Pointer, "Scroll up / zoom (right stick too)", PadDo.ScrollUp),
        new("ptr.scrolld", PadButton.RT,     PadContext.Pointer, "Scroll down / zoom (right stick too)", PadDo.ScrollDown),

        // Battle: on top of the pointer. Right stick turns/tilts the camera, triggers zoom, the pointer at the
        // screen edge pans it. The rest press the battle's own keys.
        new("bat.confirm", PadButton.X,      PadContext.Battle, "Confirm the move / shot (Space)", PadDo.Key, Key.Space),
        new("bat.next",    PadButton.Y,      PadContext.Battle, "Next ready unit - the pointer jumps to it", PadDo.NextUnit),
        new("bat.tprev",   PadButton.LB,     PadContext.Battle, "Previous target - jumps to the units you can pick right now (else enemies)", PadDo.PrevTarget),
        new("bat.tnext",   PadButton.RB,     PadContext.Battle, "Next target - shooting: enemies in range; activating: your ready units", PadDo.NextTarget),
        new("bat.zoomout", PadButton.LT,     PadContext.Battle, "Zoom out", PadDo.ScrollDown),
        new("bat.zoomin",  PadButton.RT,     PadContext.Battle, "Zoom in", PadDo.ScrollUp),
        new("bat.act1",    PadButton.DUp,    PadContext.Battle, "Action 1 - Hold (1)", PadDo.Key, Key.Key1),
        new("bat.act2",    PadButton.DRight, PadContext.Battle, "Action 2 - Advance (2)", PadDo.Key, Key.Key2),
        new("bat.act3",    PadButton.DLeft,  PadContext.Battle, "Action 3 - Rush (3)", PadDo.Key, Key.Key3),
        new("bat.act4",    PadButton.DDown,  PadContext.Battle, "Action 4 - Charge (4)", PadDo.Key, Key.Key4),
        new("bat.report",  PadButton.Back,   PadContext.Battle, "Battle report (R)", PadDo.Key, Key.R),
        new("bat.top",     PadButton.RStick, PadContext.Battle, "Top view (T)", PadDo.Key, Key.T),
    };

    /// <summary>The saved override for an action, if Kai has set one.</summary>
    public static PadButton? Override(string id) =>
        Prefs.Buttons.TryGetValue(id, out string? name) && Enum.TryParse(name, true, out PadButton b) ? b : null;

    /// <summary>Which binding a button press means here. Battle lines win over Pointer lines on the same button.</summary>
    public static PadBinding? Find(PadButton button, bool pointer, bool battle)
    {
        if (!pointer) return All.FirstOrDefault(b => b.Where == PadContext.Menus && b.Act != PadDo.Native && b.Button == button);
        if (battle && All.FirstOrDefault(b => b.Where == PadContext.Battle && b.Button == button) is { } bat) return bat;
        return All.FirstOrDefault(b => b.Where == PadContext.Pointer && b.Button == button);
    }

    /// <summary>Godot's joypad button index -> our button (triggers are axes, read by <see cref="PadDriver"/>).</summary>
    public static PadButton? FromJoy(JoyButton j) => j switch
    {
        JoyButton.A => PadButton.A, JoyButton.B => PadButton.B, JoyButton.X => PadButton.X, JoyButton.Y => PadButton.Y,
        JoyButton.LeftShoulder => PadButton.LB, JoyButton.RightShoulder => PadButton.RB,
        JoyButton.LeftStick => PadButton.LStick, JoyButton.RightStick => PadButton.RStick,
        JoyButton.Back => PadButton.Back, JoyButton.Start => PadButton.Start,
        JoyButton.DpadUp => PadButton.DUp, JoyButton.DpadDown => PadButton.DDown,
        JoyButton.DpadLeft => PadButton.DLeft, JoyButton.DpadRight => PadButton.DRight,
        _ => null,
    };

    // ---------------------------------------------------------------- which pad, which pictures

    public enum EStyle { Xbox, PlayStation }

    /// <summary>The button pictures to show: the Settings choice, or worked out from the connected pad's name.</summary>
    public static EStyle Style
    {
        get
        {
            if (Prefs.Prompts == "xbox") return EStyle.Xbox;
            if (Prefs.Prompts == "playstation") return EStyle.PlayStation;
            return IsPlayStation(PadDriver.LastDevice) ? EStyle.PlayStation : EStyle.Xbox;
        }
    }

    public static bool IsPlayStation(int device)
    {
        if (device < 0) return false;
        string n = Godot.Input.GetJoyName(device).ToLowerInvariant();
        return n.Contains("playstation") || n.Contains("dualsense") || n.Contains("dualshock") || n.Contains("ps4")
               || n.Contains("ps5") || n.Contains("ps3") || n.Contains("sony") || n.Contains("wireless controller");
    }

    /// <summary>The button's printed name on this style of pad ("A" / "Cross").</summary>
    public static string Name(PadButton b) => (Style, b) switch
    {
        (EStyle.PlayStation, PadButton.A) => "Cross", (EStyle.PlayStation, PadButton.B) => "Circle",
        (EStyle.PlayStation, PadButton.X) => "Square", (EStyle.PlayStation, PadButton.Y) => "Triangle",
        (EStyle.PlayStation, PadButton.LB) => "L1", (EStyle.PlayStation, PadButton.RB) => "R1",
        (EStyle.PlayStation, PadButton.LT) => "L2", (EStyle.PlayStation, PadButton.RT) => "R2",
        (EStyle.PlayStation, PadButton.LStick) => "L3", (EStyle.PlayStation, PadButton.RStick) => "R3",
        (EStyle.PlayStation, PadButton.Back) => "Create", (EStyle.PlayStation, PadButton.Start) => "Options",
        (_, PadButton.LStick) => "Left stick click", (_, PadButton.RStick) => "Right stick click",
        (_, PadButton.Back) => "View", (_, PadButton.Start) => "Menu",
        (_, PadButton.DUp) => "D-pad up", (_, PadButton.DDown) => "D-pad down",
        (_, PadButton.DLeft) => "D-pad left", (_, PadButton.DRight) => "D-pad right",
        _ => b.ToString(),
    };

    /// <summary>Makes sure Godot's menu actions answer a pad (d-pad and left stick move, A presses, B backs out).
    /// Godot's defaults already include most of these; anything missing is added, so a menu never ignores the pad.</summary>
    public static void InstallMenuActions()
    {
        void Button(string action, JoyButton b)
        {
            if (!InputMap.HasAction(action)) return;
            bool has = InputMap.ActionGetEvents(action).Any(e => e is InputEventJoypadButton jb && jb.ButtonIndex == b);
            if (!has) InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = b, Device = -1 });
        }
        void Axis(string action, JoyAxis a, float dir)
        {
            if (!InputMap.HasAction(action)) return;
            bool has = InputMap.ActionGetEvents(action).Any(e => e is InputEventJoypadMotion jm && jm.Axis == a && Math.Sign(jm.AxisValue) == Math.Sign(dir));
            if (!has) InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = a, AxisValue = dir, Device = -1 });
        }
        Button("ui_accept", JoyButton.A);
        Button("ui_cancel", JoyButton.B);
        Button("ui_up", JoyButton.DpadUp); Axis("ui_up", JoyAxis.LeftY, -1);
        Button("ui_down", JoyButton.DpadDown); Axis("ui_down", JoyAxis.LeftY, 1);
        Button("ui_left", JoyButton.DpadLeft); Axis("ui_left", JoyAxis.LeftX, -1);
        Button("ui_right", JoyButton.DpadRight); Axis("ui_right", JoyAxis.LeftX, 1);
        foreach (string a in new[] { "ui_up", "ui_down", "ui_left", "ui_right" })
            InputMap.ActionSetDeadzone(a, Math.Clamp(Prefs.Deadzone + 0.25f, 0.3f, 0.9f));
    }
}
