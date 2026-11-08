namespace FdgRaylib.Rendering;

/// <summary>
/// Global in-game view toggles, shared between the canvas overlays that read them and the in-game
/// menu's Options panel that sets them (#246). Static because there is one board on screen at a time
/// and these are display preferences, not per-game state — they persist across games like the grid
/// toggle always has.
/// </summary>
public static class ViewSettings
{
    /// <summary>Unit-name labels on the table (hotkey N; #329 moved it off L, which now opens
    /// the army list overlay).</summary>
    public static bool ShowLabels = true;

    /// <summary>Etched grid + felt vignette under the table (was RaylibRenderer.ShowGrid).</summary>
    public static bool ShowGrid = true;

    /// <summary>Dev toggle (hotkey T): reveal Invisible bookkeeping tokens in chips/tooltips.</summary>
    public static bool ShowAllTokens = false;

    /// <summary>
    /// #230/#247 (hotkey V): the master switch for the tactical overlay's reach picture — the opportunity
    /// field, with LoS and cover. One toggle covers every anchor it can take: the ghosts of a move or
    /// placement in progress, and the unit under the cursor (which wins while hovering, so only ever one
    /// field is on screen). Off means none of them draw. On by default.
    /// </summary>
    public static bool ShowReachOverlay = true;

    /// <summary>#331: firework bursts in the winning side's colours behind the game-over card.</summary>
    public static bool ShowVictoryFireworks = true;

    /// <summary>
    /// #344: multiplier on how long a dice-roll panel LINGERS on the #327 stack after it has settled.
    /// 1.0 is the tuned default; the slider spans <see cref="DiceLingerMin"/>..<see cref="DiceLingerMax"/>,
    /// so a player who reads fast can clear the caption zone in a third of the time and one who wants to
    /// study the arithmetic can double it.
    ///
    /// <para>The linger is the only part that scales. A panel's lifetime is <c>paced + linger</c>, and the
    /// PACED part is the engine's own wait on the beat (<c>PresentationBeat.NominalDuration</c>) - the
    /// window the dice tumble and settle in. Scaling that would retire the panel before the roll it is
    /// showing had finished, on a client whose engine is still waiting. So the knob moves the part that is
    /// purely "how long you get to re-read it", which is what the setting is actually for.</para>
    ///
    /// <para>Saved with the other Settings choices since 2026-11-07 (<see cref="Save"/>).</para>
    /// </summary>
    public static float DiceLingerScale = 1f;

    /// <summary>Dice tray: land the dice straight away instead of rumbling first.</summary>
    public static bool SkipDiceAnimation = false;

    /// <summary>How fast the game plays out (moves, shots, dice) - turn it down to follow the bot's turns.
    /// 1 = normal, 0.5 = half speed, 2 = double.</summary>
    public static float GameSpeed = 1f;

    /// <summary>Keyboard camera speed multiplier (WASD / Q-E / Shift+W-S / Shift+Q-E), set in Options.</summary>
    public static float CameraSpeed = 1f;

    /// <summary>Flips Shift+Q / Shift+E tilt.</summary>
    public static bool InvertTilt = false;

    /// <summary>Master volume, 0..1 (Settings > Sound). Applied to the audio device by the renderer.</summary>
    public static float MasterVolume = 1f;

    /// <summary>Copies the saved Settings choices in (once, when the window opens).</summary>
    public static void Load(Config.ViewPreferences p)
    {
        ShowLabels = p.ShowLabels;
        ShowGrid = p.ShowGrid;
        ShowAllTokens = p.ShowAllTokens;
        ShowVictoryFireworks = p.ShowVictoryFireworks;
        DiceLingerScale = Math.Clamp(p.DiceLingerScale, DiceLingerMin, DiceLingerMax);
        GameSpeed = Math.Clamp(p.GameSpeed, 0.25f, 2f);
        ShowReachOverlay = p.ShowReachOverlay;
        CameraSpeed = Math.Clamp(p.CameraSpeed, 0.25f, 3f);
        InvertTilt = p.InvertTilt;
        MasterVolume = Math.Clamp(p.MasterVolume, 0f, 1f);
    }

    /// <summary>Writes the current choices to the config file - called on every change, so there is no
    /// Save button (Kai, 2026-11-07). Hotkey toggles (N, T, B) call it too.</summary>
    public static void Save()
    {
        Config.ViewPreferences p = Config.UserConfig.Current.View;
        p.ShowLabels = ShowLabels;
        p.ShowGrid = ShowGrid;
        p.ShowAllTokens = ShowAllTokens;
        p.ShowVictoryFireworks = ShowVictoryFireworks;
        p.DiceLingerScale = DiceLingerScale;
        p.GameSpeed = GameSpeed;
        p.ShowReachOverlay = ShowReachOverlay;
        p.CameraSpeed = CameraSpeed;
        p.InvertTilt = InvertTilt;
        p.MasterVolume = MasterVolume;
        Config.UserConfig.Save();
    }

    /// <summary>Shortest the dice panels may linger: a third of the default (#344).</summary>
    public const float DiceLingerMin = 1f / 3f;

    /// <summary>Longest the dice panels may linger: twice the default (#344).</summary>
    public const float DiceLingerMax = 2f;
}
