using System.Text.Json;
using System.Text.Json.Serialization;
using FDG;
using FDG.ArmyBuilding;
using FDG.Network;
using FDG.Network.Connection.Lobby;

namespace FdgRaylib.Config;

/// <summary>
/// Per-user preferences that outlive a single run (#310): the player name every lobby leads with, and
/// the host dialog + lobby settings the last hosted game was set up with.
///
/// <para>Stored as human-editable JSON in the OS per-user config directory - <c>%APPDATA%\FDG\config.json</c>
/// on Windows, <c>~/.config/fdg/config.json</c> elsewhere - so it survives downloading a fresh build
/// into a new folder. Override the directory with the <c>FDG_CONFIG_DIR</c> environment variable
/// (tests and portable installs). The file is written with defaults the first time it is read if it
/// doesn't exist yet; every read and write is best-effort, and a missing/unreadable/corrupt file just
/// means "defaults", never an error the player sees.</para>
/// </summary>
public sealed class UserConfig
{
    /// <summary>What a fresh install calls you, until you type something else into a host/join dialog.</summary>
    public const string DefaultPlayerName = "Newbie";

    public const string DefaultServerName = "The Table";

    /// <summary>The name this player joins and hosts under. Written back whenever you host or join.</summary>
    public string PlayerName { get; set; } = DefaultPlayerName;

    /// <summary>Retired with the Host dialog (2026-11-07): the lobby is now named after the player. Kept so
    /// an older config file still reads.</summary>
    public string ServerName { get; set; } = DefaultServerName;

    /// <summary>Retired with the Host dialog (2026-11-07): every game hosts on the fixed
    /// <c>NetworkProtocol.DefaultPort</c>. Kept so an older config file still reads.</summary>
    public int Port { get; set; } = NetworkProtocol.DefaultPort;
    /// <summary>The last host address joined successfully, so Join opens on it (a friend's Tailscale name).</summary>
    public string LastHostAddress { get; set; } = "";

    /// <summary>Friends saved on the Join a Game popup (name + Tailscale address, starred ones first).</summary>
    public List<FriendEntry> Friends { get; set; } = new();

    /// <summary>Retired with the Host dialog (2026-11-07): nothing is ever listed publicly now (Tailscale
    /// only). Kept so an older config file still reads; nothing reads it.</summary>
    public bool ListPublicly { get; set; }

    /// <summary>Display: start in borderless-windowed fullscreen. F11 toggles it at runtime and writes the
    /// new choice back here, so it is remembered next launch. On by default on a fresh install.</summary>
    public bool StartFullscreen { get; set; } = true;

    /// <summary>#398: the Combat Calculator's per-side game-system choice (Grimdark Future / Age of
    /// Fantasy), remembered between runs because a player mostly works in one system. Slugs, not an
    /// enum, so the file stays readable and an unknown value degrades to the default rather than
    /// throwing. Side A and B are stored separately - a cross-system what-if is a legitimate question
    /// to ask a calculator.</summary>
    public string CalculatorSystemA { get; set; } = GameSystems.GrimdarkFuture;

    public string CalculatorSystemB { get; set; } = GameSystems.GrimdarkFuture;

    /// <summary>The Settings screen's Display / Gameplay / Camera / Sound choices (Kai, 2026-11-07: "Changes
    /// save as you make them"). Loaded into <c>ViewSettings</c> when the window opens.</summary>
    public ViewPreferences View { get; set; } = new();

    /// <summary>Godot build: Xbox / PlayStation controller support (Kai, 2026-11-24 - couch play). Settings -> Controls.</summary>
    public ControllerPreferences Controller { get; set; } = new();

    /// <summary>
    /// The main menu's "Turn off auto update detection" switch (Kai, 2026-11-14). On: the game never contacts GitHub -
    /// no check when it opens and the Update button is off - and the menu says it is disconnected. Off (the default):
    /// it checks once as the game opens, and only when Update is pressed after that.
    /// </summary>
    public bool AutoUpdateDetectionOff { get; set; }

    /// <summary>The lobby settings panel, as the last hosted (non-resume) game left it.</summary>
    public HostGameSettings HostSettings { get; set; } = HostGameSettings.FromDefaults();

    /// <summary>How to Play: lessons finished in each game (2026-11-24).</summary>
    public Tutorial.HowToPlayProgress HowToPlay { get; set; } = new();

    // ── Store ──────────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Enums as names, so a hand-edit reads "BoltAction" rather than "1" and survives an enum
        // gaining a value in the middle (ETerrainPlacementMode already appended one to keep the wire
        // values stable).
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly object FileLock = new();
    private static UserConfig? _current;

    /// <summary>
    /// The live config. Loaded from disk on first touch; if no file exists yet, one is written with the
    /// defaults so an install always has a config to edit.
    /// </summary>
    public static UserConfig Current
    {
        get
        {
            lock (FileLock)
            {
                if (_current == null)
                {
                    _current = LoadFrom(FilePath);
                    if (!File.Exists(FilePath))
                        WriteTo(FilePath, _current);
                }
                return _current;
            }
        }
    }

    /// <summary>Full path of the config file (existing or not).</summary>
    public static string FilePath => Path.Combine(Directory, "config.json");

    /// <summary>The per-user config directory, honouring the <c>FDG_CONFIG_DIR</c> override.</summary>
    public static string Directory
    {
        get
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FDG_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir.Trim();

            // %APPDATA% on Windows; $XDG_CONFIG_HOME (defaulting to ~/.config) on Linux and macOS.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = AppContext.BaseDirectory; // no home directory (some sandboxes) - keep it local
            return Path.Combine(appData, OperatingSystem.IsWindows() ? "FDG" : "fdg");
        }
    }

    /// <summary>Writes <see cref="Current"/> back to disk. Best-effort: a failure is logged, never thrown.</summary>
    public static void Save()
    {
        lock (FileLock)
        {
            WriteTo(FilePath, Current);
        }
    }

    /// <summary>Makes sure the install has a config file, creating one with defaults if it doesn't (#310).</summary>
    public static void EnsureExists() => _ = Current;

    /// <summary>Drops the cached instance so the next <see cref="Current"/> re-reads the file (tests).</summary>
    public static void ResetCacheForTests()
    {
        lock (FileLock) _current = null;
    }

    /// <summary>Reads a config file, falling back to defaults when it is missing or unreadable.</summary>
    public static UserConfig LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new UserConfig();
            string text = File.ReadAllText(path);
            UserConfig config = JsonSerializer.Deserialize<UserConfig>(text, Json) ?? new UserConfig();
            if (!text.Contains("\"" + nameof(HostGameSettings.VolumetricDefaulted) + "\"", StringComparison.OrdinalIgnoreCase))
            {
                config.HostSettings.VolumetricLineOfSight = true;
                config.HostSettings.VolumetricDefaulted = true;
            }
            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A hand-edit that doesn't parse shouldn't stop the game starting - fall back to defaults
            // and say so, rather than silently rewriting the file the player was editing.
            Console.WriteLine($"Could not read {path} ({ex.Message}); using default settings.");
            return new UserConfig();
        }
    }

    /// <summary>Writes a config file, creating the directory if needed. Best-effort.</summary>
    public static void WriteTo(string path, UserConfig config)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(config, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not write {path} ({ex.Message}); settings won't be remembered.");
        }
    }
}

/// <summary>
/// The Settings screen's display and play preferences, as saved (#310 store). Defaults match the
/// game's built-in ones, so a fresh install looks the same as before this was remembered.
/// </summary>
public sealed class ViewPreferences
{
    public bool ShowLabels { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public bool ShowAllTokens { get; set; }
    public bool ShowVictoryFireworks { get; set; } = true;
    public float DiceLingerScale { get; set; } = 1f;
    public float GameSpeed { get; set; } = 1f;
    public bool ShowReachOverlay { get; set; } = true;
    public float CameraSpeed { get; set; } = 1f;
    public bool InvertTilt { get; set; }
    public float MasterVolume { get; set; } = 1f;
    /// <summary>Godot build: the small click and hover sounds on menu buttons (Kai, 2026-11-15).</summary>
    public bool MenuSounds { get; set; } = true;
    /// <summary>Godot build: how loud battle sounds are - shots, swings, hits, explosions (Kai, 2026-11-26). 0-1.</summary>
    public float EffectsVolume { get; set; } = 0.8f;
    /// <summary>Godot build (lane 4, 2026-11-27): menu music. 0-1.</summary>
    public float MusicVolume { get; set; } = 0.5f;
    /// <summary>Godot build: the battlefield's background - wind, distant rumbles, rain. 0-1.</summary>
    public float AmbienceVolume { get; set; } = 0.5f;
    /// <summary>Godot build: the game's own cues - dice, a round starting / ending, objectives seized or lost, units
    /// destroyed, morale failed, victory / defeat. 0-1.</summary>
    public float CueVolume { get; set; } = 0.7f;
    /// <summary>Godot build: how loud the menu click / hover sounds are (on/off is <see cref="MenuSounds"/>). 0-1.</summary>
    public float MenuSoundVolume { get; set; } = 1f;
    /// <summary>Godot build (2026-11-27): the announcer's voice lines - rounds, objectives, units lost, the result. 0-1.</summary>
    public float AnnouncerVolume { get; set; } = 0.9f;
    /// <summary>Godot build: the announcer speaks at all (it is silent anyway until voice files are in the audio folder).</summary>
    public bool Announcer { get; set; } = true;
    /// <summary>Godot build: music plays in battle when there are battle tracks in the audio folder (off: menus only).</summary>
    public bool BattleMusic { get; set; } = true;
    /// <summary>Godot build: muzzle flashes, projectiles, swings and impacts in battle (off: the plain tracers).</summary>
    public bool BattleEffects { get; set; } = true;
    /// <summary>Godot build: draw downloaded 3D models (UnitModels) on the battle table instead of the plain tokens.</summary>
    public bool ShowUnitModels { get; set; } = true;
    /// <summary>Godot build: the UI theme - "classic" (the drawn cut-corner panels) or an art pack's id ("hologram",
    /// "minimal"; OprGodot Hud.Skin.All). Kai is trying packs (2026-11-16); Settings -> Display -> UI theme.</summary>
    public string UiSkin { get; set; } = "minimal";
    /// <summary>Godot build: how big unit and army cards are (Kai, 2026-11-17), 50-150 %. 100 % = Army Forge cards about
    /// two-thirds of their old size, the army picture grid 4 across, and lobby army-list portraits bigger than before.</summary>
    public int CardSize { get; set; } = 100;
    /// <summary>The newest UI theme this copy has switched to by itself: a build that adds a theme puts it on once (Kai is
    /// trying them one after another); picking another in Settings sticks until the next new theme arrives.</summary>
    public string UiSkinOffered { get; set; } = "";

    /// <summary>The room around the table (Kai, 2026-11-14): a <c>TableRooms</c> id, "auto" (a room matching the game,
    /// once downloaded) or "" for none.</summary>
    public string Room { get; set; } = "tabletop";

    /// <summary>Set once a copy has switched itself to the battlefield surroundings (2026-11-18, Kai asked for them):
    /// a player who then picks a room keeps it.</summary>
    public bool BattlefieldOffered { get; set; }

    /// <summary>Set once a copy has switched itself to the tabletop look (Kai, 2026-11-25).</summary>
    public bool TabletopOffered { get; set; }

    /// <summary>Draw a map's weather (rain, snow, fog...) over the table (Kai, 2026-11-18). Cosmetic.</summary>
    public bool WeatherEffects { get; set; } = true;

    /// <summary>Which built-in terrain models the table uses (Kai, 2026-11-21: both sets in the game "so people can have
    /// options"): <c>stylised</c> (Quaternius / Majadroid) or <c>realistic</c> (Poly Haven). Kinds with no hand-built
    /// piece in that style fall back to the older scatter looks.</summary>
    public string TerrainStyle { get; set; } = "stylised";

    /// <summary>How bright the room behind the table is drawn (0.2 - 1.5).</summary>
    public float RoomBrightness { get; set; } = 0.7f;
}

/// <summary>
/// Controller settings for the Godot build (Kai, 2026-11-24: "I'm planning on playing on the couch" - Xbox and
/// PlayStation pads). The Raylib build ignores them.
/// </summary>
public sealed class ControllerPreferences
{
    /// <summary>Off: the game ignores every controller.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which button pictures the game shows: "auto" (from the controller's name), "xbox" or "playstation".</summary>
    public string Prompts { get; set; } = "auto";

    /// <summary>How fast the on-screen pointer moves with the left stick (0.25 - 3, 1 = normal).</summary>
    public float PointerSpeed { get; set; } = 1f;

    /// <summary>How fast the right stick turns and tilts the battle camera (0.25 - 3, 1 = normal).</summary>
    public float CameraSpeed { get; set; } = 1f;

    /// <summary>How far a stick must move before the game reacts (0.05 - 0.5), so a worn stick doesn't drift.</summary>
    public float Deadzone { get; set; } = 0.2f;

    /// <summary>Swap which way the right stick tilts the camera.</summary>
    public bool InvertCameraTilt { get; set; }

    /// <summary>Two players at one computer: swap which controller plays which seat (normally the first controller
    /// connected plays seat 1).</summary>
    public bool SwapPlayers { get; set; }

    /// <summary>Show the strip of button hints along the bottom of the screen while a controller is in use.</summary>
    public bool ShowHints { get; set; } = true;

    /// <summary>Button overrides: action id -> button name (OprGodot Pads.PadButton). Empty = the built-in layout.
    /// Kai is choosing the exact mapping later; an action missing here keeps its default.</summary>
    public Dictionary<string, string> Buttons { get; set; } = new();
}

/// <summary>
/// The lobby settings panel's host-owned values, as a saved preference (#310). Mirrors the editable
/// half of <see cref="GameSettings"/>.
///
/// <para>Two fields are deliberately absent. <see cref="GameSettings.TableBackground"/> is re-randomized
/// every time a lobby opens - remembering it would defeat the point - and <see cref="GameSettings.DiceSeed"/>
/// has no lobby control (it comes from a scenario file).</para>
/// </summary>
public sealed class HostGameSettings
{
    public int ArmyPoints { get; set; }
    // Absent from configs written before Horde Mode, which read back as Standard (enum 0).
    public EGameMode GameMode { get; set; }
    // Absent from configs written before game types, which read back as Grimdark Future (enum 0).
    public EGameSystem GameSystem { get; set; }
    public EPlayStyle PlayStyle { get; set; }
    public EAllowedGameSystems AllowedGameSystems { get; set; }
    public ETerrainPlacementMode TerrainPlacementMode { get; set; }
    public int TerrainPieceCount { get; set; }
    public int TerrainPointsTotal { get; set; }
    public int TerrainPointsPerTurn { get; set; }
    public string? TerrainLayoutPath { get; set; }
    public EObjectivePlacementMode ObjectivePlacementMode { get; set; }
    public ERandomnessType RandomnessType { get; set; }
    public ETurnStyle TurnStyle { get; set; }
    public EShootingMode ShootingMode { get; set; }
    public bool CoverProximityExceptions { get; set; }
    public bool SeeThroughFriendlyUnits { get; set; }
    public bool UnlimitedSplitFire { get; set; }

    // #456 Fast-Play rules, remembered like the other rules toggles.
    public bool FastPlayMovement { get; set; }
    public bool FastPlayMelee { get; set; }
    public bool FastPlayShooting { get; set; }
    public bool FastPlaySimplifiedTerrain { get; set; }
    /// <summary>Volumetric Line of Sight (Advanced Rulebooks), remembered like the other rules toggles. On by default
    /// since terrain heights are read from the models (Kai, 2026-11-22: "Yes, 3D sight").</summary>
    public bool VolumetricLineOfSight { get; set; } = true;
    /// <summary>Whether this file has had 3D sight turned on once as the new default (2026-11-22), so a file saved
    /// before then - with the tick off only because it used to be off by default - starts with it on. After that
    /// the player's own choice is kept.</summary>
    public bool VolumetricDefaulted { get; set; } = true;
    public bool ForceOrg { get; set; }
    /// <summary>The Army Forge's Force Org switch (Kai, 2026-11-15) - on unless turned off.</summary>
    public bool ForgeForceOrg { get; set; } = true;
    public int? NumberOfRounds { get; set; }
    public bool KingOfTheHill { get; set; }
    public bool PickDangerousTerrain { get; set; }
    public EExtraActions ExtraActions { get; set; }

    /// <summary>The optional Advanced Rulebook systems ticked last time.</summary>
    public EAdvancedRules AdvancedRules { get; set; }
    public EChallengeBonuses ChallengeBonuses { get; set; }
    public EDeploymentStyle DeploymentStyle { get; set; }
    public FDG.Missions.EMissionCardGame MissionCardGame { get; set; }
    public FDG.Missions.EExtraMission ExtraMission { get; set; }
    public bool SolidBuildings { get; set; }
    public FDG.Missions.ESideMissionPlay SideMissions { get; set; }
    public FDG.Missions.ESideMissionPick SideMissionPick { get; set; }

    /// <summary>Attack &amp; Defend: the team that attacks (0 = team 1, -1 = the roll-off).</summary>
    public int AttackerTeam { get; set; }

    public bool FriendlyUnitsMayOverlap { get; set; }

    /// <summary>The engine's own defaults - one source of truth for what a fresh config holds.</summary>
    public static HostGameSettings FromDefaults()
    {
        HostGameSettings settings = From(GameSettings.GetDefault());
        settings.VolumetricLineOfSight = true; // 3D sight on for a new player (2026-11-22)
        return settings;
    }

    public static HostGameSettings From(GameSettings settings) => new()
    {
        ArmyPoints             = settings.ArmyPoints,
        GameMode               = settings.GameMode,
        GameSystem             = settings.GameSystem,
        PlayStyle              = settings.PlayStyle,
        AllowedGameSystems     = settings.AllowedGameSystems,
        TerrainPlacementMode   = settings.TerrainPlacementMode,
        TerrainPieceCount      = settings.TerrainPieceCount,
        TerrainPointsTotal     = settings.TerrainPointsTotal,
        TerrainPointsPerTurn   = settings.TerrainPointsPerTurn,
        TerrainLayoutPath      = settings.TerrainLayoutPath,
        ObjectivePlacementMode = settings.ObjectivePlacementMode,
        RandomnessType         = settings.RandomnessType,
        TurnStyle              = settings.TurnStyle,
        ShootingMode           = settings.ShootingMode,
        CoverProximityExceptions = settings.CoverProximityExceptionsEnabled,
        SeeThroughFriendlyUnits = settings.SeeThroughFriendlyUnits,
        UnlimitedSplitFire     = settings.UnlimitedSplitFire,
        FastPlayMovement       = settings.FastPlayMovement,
        FastPlayMelee          = settings.FastPlayMelee,
        FastPlayShooting       = settings.FastPlayShooting,
        FastPlaySimplifiedTerrain = settings.FastPlaySimplifiedTerrain,
        VolumetricLineOfSight = settings.VolumetricLineOfSight,
        ForceOrg = settings.ForceOrg,
        NumberOfRounds         = settings.NumberOfRounds,
        KingOfTheHill          = settings.KingOfTheHill,
        PickDangerousTerrain   = settings.PickDangerousTerrain,
        ExtraActions           = settings.ExtraActions,
        AdvancedRules          = settings.AdvancedRules,
        ChallengeBonuses       = settings.ChallengeBonuses,
        DeploymentStyle        = settings.DeploymentStyle,
        MissionCardGame        = settings.MissionCardGame,
        ExtraMission           = settings.ExtraMission,
        SolidBuildings         = settings.SolidBuildings,
        SideMissions           = settings.SideMissions,
        SideMissionPick        = settings.SideMissionPick,
        AttackerTeam           = settings.AttackerTeam,
        FriendlyUnitsMayOverlap = settings.FriendlyUnitsMayOverlap,
    };

    /// <summary>Snapshots what the lobby panel currently shows.</summary>
    public static HostGameSettings CaptureFrom(ILobbyViewModel viewModel) => new()
    {
        ArmyPoints             = viewModel.ArmyPoints,
        GameMode               = viewModel.GameMode,
        GameSystem             = viewModel.GameSystem,
        PlayStyle              = viewModel.PlayStyle,
        AllowedGameSystems     = viewModel.AllowedGameSystems,
        TerrainPlacementMode   = viewModel.TerrainPlacementMode,
        TerrainPieceCount      = viewModel.TerrainCount,
        TerrainPointsTotal     = viewModel.TerrainPointsTotal,
        TerrainPointsPerTurn   = viewModel.TerrainPointsPerTurn,
        TerrainLayoutPath      = viewModel.TerrainLayoutPath,
        ObjectivePlacementMode = viewModel.ObjectivePlacementMode,
        RandomnessType         = viewModel.RandomnessType,
        TurnStyle              = viewModel.TurnStyle,
        ShootingMode           = viewModel.ShootingMode,
        CoverProximityExceptions = viewModel.CoverProximityExceptions,
        SeeThroughFriendlyUnits = viewModel.SeeThroughFriendlyUnits,
        UnlimitedSplitFire     = viewModel.UnlimitedSplitFire,
        FastPlayMovement       = viewModel.FastPlayMovement,
        FastPlayMelee          = viewModel.FastPlayMelee,
        FastPlayShooting       = viewModel.FastPlayShooting,
        FastPlaySimplifiedTerrain = viewModel.FastPlaySimplifiedTerrain,
        VolumetricLineOfSight = viewModel.VolumetricLineOfSight,
        ForceOrg = viewModel.ForceOrg,
        NumberOfRounds         = viewModel.NumberOfRounds,
        KingOfTheHill          = viewModel.KingOfTheHill,
        PickDangerousTerrain   = viewModel.PickDangerousTerrain,
        ExtraActions           = viewModel.ExtraActions,
        AdvancedRules          = viewModel.AdvancedRules,
        ChallengeBonuses       = viewModel.ChallengeBonuses,
        DeploymentStyle        = viewModel.DeploymentStyle,
        MissionCardGame        = viewModel.MissionCardGame,
        ExtraMission           = viewModel.ExtraMission,
        SolidBuildings         = viewModel.SolidBuildings,
        SideMissions           = viewModel.SideMissions,
        SideMissionPick        = viewModel.SideMissionPick,
        AttackerTeam           = viewModel.AttackerTeam,
        FriendlyUnitsMayOverlap = viewModel.FriendlyUnitsMayOverlap,
    };

    /// <summary>
    /// Pushes these settings into a fresh host lobby. Host-only and never on a resume - a saved game
    /// launches from its own settings (see <see cref="GameSettings.WithResumeOverridesFrom"/>).
    /// </summary>
    public void ApplyTo(ILobbyViewModel viewModel)
    {
        if (!viewModel.HasHostPrivileges || viewModel.IsResumeMode) return;

        viewModel.SetGameSystem(GameSystemInfo.IsAvailable(GameSystem) ? GameSystem : EGameSystem.GrimdarkFuture);
        viewModel.SetGameMode(GameMode);
        viewModel.SetPlayStyle(PlayStyle);
        viewModel.SetArmyPoints(ArmyPoints);
        // The army source follows the Game Type (no longer a lobby choice).
        viewModel.SetAllowedGameSystems(GameSystemInfo.ArmySource(viewModel.GameSystem));
        viewModel.SetTerrainPlacementMode(TerrainPlacementMode);
        viewModel.SetTerrainCount(TerrainPieceCount);
        viewModel.SetTerrainPointsTotal(TerrainPointsTotal);
        viewModel.SetTerrainPointsPerTurn(TerrainPointsPerTurn);
        viewModel.SetTerrainLayoutPath(TerrainLayoutPath);
        viewModel.SetObjectivePlacementMode(ObjectivePlacementMode);
        viewModel.SetRandomnessType(RandomnessType);
        viewModel.SetTurnStyle(TurnStyle);
        // Declare First is the official rule (targets declared before rolling); no longer a lobby choice.
        viewModel.SetShootingMode(EShootingMode.DeclareFirst);
        viewModel.SetCoverProximityExceptions(CoverProximityExceptions);
        viewModel.SetSeeThroughFriendlyUnits(SeeThroughFriendlyUnits);
        viewModel.SetUnlimitedSplitFire(UnlimitedSplitFire);
        viewModel.SetFastPlay(FastPlayMovement, FastPlayMelee, FastPlayShooting);
        viewModel.SetFastPlaySimplifiedTerrain(FastPlaySimplifiedTerrain);
        viewModel.SetVolumetricLineOfSight(VolumetricLineOfSight);
        viewModel.SetForceOrg(ForceOrg);
        viewModel.SetNumberOfRounds(NumberOfRounds);
        viewModel.SetKingOfTheHill(KingOfTheHill);
        viewModel.SetPickDangerousTerrain(PickDangerousTerrain);
        viewModel.SetExtraActions(ExtraActions);
        viewModel.SetAdvancedRules(AdvancedRules);
        viewModel.SetChallengeBonuses(ChallengeBonuses);
        viewModel.SetDeploymentStyle(DeploymentStyle);
        viewModel.SetMissionCardGame(MissionCardGame);
        viewModel.SetExtraMission(ExtraMission);
        viewModel.SetSolidBuildings(SolidBuildings);
        viewModel.SetSideMissions(SideMissions);
        viewModel.SetSideMissionPick(SideMissionPick);
        viewModel.SetAttackerTeam(AttackerTeam);
        viewModel.SetFriendlyUnitsMayOverlap(FriendlyUnitsMayOverlap);
    }
}
