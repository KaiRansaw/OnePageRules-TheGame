namespace FdgRaylib.Rendering;

/// <summary>
/// Every key the game binds, in one list, grouped the way a player would look for them.
///
/// <para>
/// There are two places that show the controls - the in-game <b>F1</b> panel and the <b>Settings</b>
/// screen - and before this each would have had its own copy. A keybind list that disagrees with the
/// keys is worse than none, because a player trusts it and then blames themselves; and a second copy
/// goes stale the first time a key moves. So both read this.
/// </para>
///
/// <para>
/// It is a LIST, not a binding table: the keys themselves are still checked where they are used, by
/// name. Remapping needs an input layer the game does not have, and inventing a half of one here - a
/// table nothing reads - would be the kind of thing that looks finished and is not. When remapping is
/// built, this is where it starts.
/// </para>
///
/// <para>Game text, so ASCII only (CLAUDE.md).</para>
/// </summary>
public static class ControlsReference
{
    public const string Camera = "Camera";
    public const string Doing = "Doing things";
    public const string Looking = "Looking at things";
    public const string App = "App";
    public const string Menus = "Menu hotkeys";

    /// <summary>Keys, what they do, and which group they belong to - in display order.</summary>
    public static readonly (string Keys, string What, string Group)[] All =
    {
        ("WASD / arrows",          "Move the camera",                          Camera),
        ("Mouse wheel",            "Zoom",                                     Camera),
        ("Middle-drag",            "Pan (2D) / change the angle (3D)",         Camera),
        ("Right-drag",             "Move the board (3D)",                      Camera),
        ("Q / E",                  "Turn the camera (3D)",                     Camera),
        ("Shift + W / S",          "Zoom in / out",                            Camera),
        ("Shift + Q / E",          "Tilt up / down (3D)",                      Camera),
        ("V or F3",                "Switch 2D / 3D",                           Camera),

        ("Enter / Space",          "Done / confirm",                           Doing),
        ("Backspace",              "Back / undo a step",                       Doing),
        ("Wheel, R / Shift+R",     "Rotate while moving or placing",           Doing),
        ("Ctrl + wheel",           "Cycle formation (group moves)",            Doing),
        ("G",                      "Group / single model moves",               Doing),
        ("Ctrl + Z / Ctrl + X",    "Undo / redo while placing or moving",      Doing),
        ("Z / X",                  "Take Back / Redo a whole activation",      Doing),
        ("Right-click a unit",     "Unit card",                                Doing),
        ("Right-click bare table", "Deselect the unit",                        Doing),

        ("Alt + drag, M",          "Measure",                                  Looking),
        ("N",                      "Unit labels",                              Looking),
        ("T",                      "Show all tokens",                          Looking),
        ("B",                      "Weapon reach overlay",                     Looking),
        ("L",                      "Army lists",                               Looking),
        ("O",                      "Mission window (Quest only)",              Looking),

        ("Esc",                    "Menu",                                     App),
        ("Ctrl + S",               "Quick save into Saved Games",              App),
        ("F1",                     "This list",                                App),
        ("F11",                    "Fullscreen",                               App),

        ("W / C / S / A / X",      "Move / Charge / Shoot / Cast / Pass",      Menus),
        ("Q E R T D F G Z V B",    "Other actions, in the order they appear",  Menus),
        ("1-9, numpad",            "Pick a numbered row",                      Menus),
        ("Up / Down, Tab",         "Move the highlight",                       Menus),
        ("Left / Right",           "Page through a long list",                 Menus),
    };
}
