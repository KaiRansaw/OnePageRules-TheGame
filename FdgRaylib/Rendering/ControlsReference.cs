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

    /// <summary>
    /// Keys, what they do, which group they belong to, and a plain-words hover - in display order.
    ///
    /// <para>Corrected 2026-11-07 (Kai's In-Game list): Take Back / Redo are Ctrl+Z / Ctrl+X, not Z / X;
    /// the letter shortcuts on decision panels (W/C/S/A/X, Q E R T...) are switched off
    /// (<c>ResolverHotkeys.LettersDisabled</c>) so they are not listed; and the O key's mission window is
    /// gone (the mission lives in the Mission tab).</para>
    /// </summary>
    public static readonly (string Keys, string What, string Group, string Tip)[] All =
    {
        ("WASD / arrows",          "Move the camera",                          Camera,
            "Slides the view across the table. Hold the key to keep moving. Camera key speed in Settings changes how fast."),
        ("Alt + mouse wheel",      "Zoom (2D)",                                Camera,
            "In the 2D view this is the only way to zoom - the plain wheel is kept for turning the unit you are placing."),
        ("Mouse wheel",            "Zoom (3D) / turn the unit you are placing", Camera,
            "While you are placing or moving a unit the wheel turns it. Otherwise, in 3D, it zooms."),
        ("Middle-drag",            "Pan (2D) / change the angle (3D)",         Camera,
            "Hold the scroll wheel down and drag. In 2D it slides the table; in 3D it swings the camera round and up or down."),
        ("Right-drag",             "Move the board (3D)",                      Camera,
            "In the 3D view, hold the right mouse button and drag to slide the table."),
        ("Q / E",                  "Turn the camera (3D)",                     Camera,
            "Spins the 3D camera left or right around the table."),
        ("Shift + W / S",          "Zoom in / out",                            Camera,
            "Moves the camera closer or further away."),
        ("Shift + Q / E",          "Tilt up / down (3D)",                      Camera,
            "Tilts the 3D camera toward the horizon or toward a top-down view. Invert tilt in Settings swaps them."),
        ("V or F3",                "Switch 2D / 3D",                           Camera,
            "Flips between the flat top-down table and the 3D table."),

        ("Enter / Space",          "Done / confirm",                           Doing,
            "Presses the main button of whatever you are deciding (Confirm move, Done, auto-assign)."),
        ("Backspace",              "Back / undo a step",                       Doing,
            "Steps back out of what you are doing, one step at a time."),
        ("Wheel, R / Shift+R",     "Rotate while moving or placing",           Doing,
            "Turns the model or group you are moving or placing."),
        ("Ctrl + wheel",           "Cycle formation (group moves)",            Doing,
            "When moving a whole unit, changes the shape the models stand in."),
        ("G",                      "Group / single model moves",               Doing,
            "Switches between moving the whole unit at once and moving one model at a time."),
        ("Ctrl + drag / click",    "Pick some models to move together",        Doing,
            "Picks out a few models of the unit so they move as their own little group."),
        ("Ctrl + Z / Ctrl + X",    "Undo / redo - or Take Back / Redo an activation", Doing,
            "While placing or moving, undoes or redoes one step. Otherwise, in a game with only you and bots, " +
            "takes back your whole last activation (Ctrl+Z) or redoes it (Ctrl+X)."),
        ("Right-click a unit",     "Unit card",                                Doing,
            "Shows that unit's full details in the Active Unit panel."),
        ("Right-click bare table", "Deselect the unit",                        Doing,
            "Clears the unit you were looking at."),

        ("Alt + drag, M",          "Measure",                                  Looking,
            "Hold Alt and drag for a quick ruler, or press M for the measuring tool."),
        ("N",                      "Unit labels",                              Looking,
            "Shows or hides unit names over the table."),
        ("T",                      "Show all tokens",                          Looking,
            "Shows every marker on a unit, including the hidden bookkeeping ones the game normally keeps out of sight."),
        ("B",                      "Weapon reach overlay",                     Looking,
            "Shows or hides the shading of where a unit's weapons can reach."),
        ("L",                      "Army lists",                               Looking,
            "Opens every player's army list. Press L again to go back to the table."),

        ("Esc",                    "Menu",                                     App,
            "Opens the in-game menu (save, load, settings, leave). The game keeps running behind it."),
        ("Ctrl + S",               "Quick save into Saved Games",              App,
            "Saves the game straight away with the date in its name. Works from round 1, for the host."),
        ("F1",                     "This list",                                App,
            "Shows this list of keys during a game."),
        ("F11",                    "Fullscreen",                               App,
            "Switches between fullscreen and a window. The game remembers which you picked."),

        ("1-9, numpad",            "Pick a numbered row",                      Menus,
            "In a list of choices, the number key picks that row."),
        ("Up / Down, Tab",         "Move the highlight",                       Menus,
            "Moves the highlight up and down a list of choices."),
        ("Left / Right",           "Page through a long list",                 Menus,
            "Turns the page when a list of choices is too long for one screen."),
    };
}
