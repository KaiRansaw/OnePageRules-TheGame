using System.Collections.Concurrent;
using System.Numerics;
using FDG;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.StageResolution.Requests;
using FdgRaylib.Audio;
using FdgRaylib.Config;
using FdgRaylib.Rendering.Presentation;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace FdgRaylib.Rendering;

public class RaylibRenderer
{
    // Populated once during Run() after fonts are loaded; null until then.
    public static ImGuiNET.ImFontPtr BodyFont;
    public static ImGuiNET.ImFontPtr LargeFont;
    // A large atlas for the big menu text. The menu scales DOWN from this (crisp) instead of stretching
    // the 18px body font up ~5x (which looked aliased at fullscreen). MenuFontPx is the baked size, 0 if
    // fonts failed to load -- callers fall back to the old body-font scaling in that case.
    public static ImGuiNET.ImFontPtr MenuFont;
    public static float MenuFontPx;

    private static float TableWIn      => GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
    private static float TableHIn      => GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private const int   MinMargin     = 20;
    // Anchor for the resolution-derived UI scale (see ComputeUiScale): the multiplier that was tuned
    // by hand on a 4K (2160p) desktop. Smaller displays scale down from here, larger ones cap here.
    private const float ReferenceUiScale  = 1.4f;
    private const float ReferenceHeightPx = 2160f;

    private static readonly Color Background  = new(30, 30, 30, 255);

    // The surface, grid tints and edge trim all come from the launched game's #265 table background
    // (green forest felt unless the lobby says otherwise) — see TableBackgrounds.
    private ETableBackground     _background = ETableBackground.Forest;
    private TableBackgroundStyle _backgroundStyle = TableBackgrounds.For(ETableBackground.Forest);

    // Table grid: minor lines every 6", major every 12" (matches the game's inch measurements — a
    // major square is one charge move across). Lines are etched darker than the felt for an engraved
    // look rather than painted on top. Everything here is confined to the table rectangle by
    // construction, so it never bleeds onto terrain/objectives/models drawn after.
    private const float GridMinorInches = 6f;
    private const float GridMajorInches = 12f;

    public MainMenuScreen    MainMenu     { get; } = new();
    public ArmyBuilderScreen ArmyBuilder  { get; } = new();
    public ArmyForgeScreen   ArmyForge    { get; } = new();
    public CombatCalculatorScreen CombatCalculator { get; } = new();
    public HostModal         HostModal    { get; } = new();
    public ClientModal       ClientModal  { get; } = new();
    public LobbyScreen       LobbyScreen  { get; } = new();
    public ArmyPickerScreen  ArmyPicker   { get; } = new();
    public LoadGameScreen    LoadGame     { get; } = new();
    public HeroBuilderScreen HeroBuilder  { get; } = new();
    public CampaignScreen Campaigns       { get; } = new();
    public StrikeTeamScreen StrikeTeams   { get; } = new();

    private IAppScreen _currentScreen;

    private ITableState? _tableState;
    private Func<PlayerID, Color>? _colorForPlayer;
    private GameLog? _log;
    private GuiPlayerMessageUI? _playerMessageUI;  // in-game chat sink + send hook (#077)
    private string _chatInput = "";

    // Right-column console: Log and Chat are independent TOGGLES (not exclusive tabs) -- with both on,
    // their lines are merged into one column in arrival order.
    private bool _showChat  = true;   // Chat source shown (button on the left)
    private bool _showLog   = true;   // Log source shown
    private bool _showDebug = false;  // Debug source shown (developer detail; off by default)
    private EChatMessageType _chatChannel = EChatMessageType.Global;
    private bool _chatUnread    = false;  // new chat arrived while Chat is toggled off / console collapsed
    private int  _lastChatCount = 0;      // for the unread check
    private GuiResolverOverlay? _resolverOverlay;
    private GuiOutstandingTaskDisplay? _taskDisplay;
    // #280 live decision previews: publisher streams the local resolver's ghosts/paths, remote
    // overlay draws every other player's. Both built at launch and carried on the resolver overlay.
    private Previews.PreviewPublisher? _previewPublisher;
    private Previews.RemotePreviewOverlay? _remotePreviews;
    private PresentationPlayer? _presentationPlayer;
    private PresentationPlayer? _reportHookedPlayer;   // the player the battle report listens to
    private AudioManager? _audio;
    private readonly TableTooltipOverlay _tooltipOverlay = new();
    private readonly TableHitTester      _hitTester      = new();
    private readonly MeasurementOverlay  _measurementOverlay = new();
    // Host-only Game Master panel (Play Style = Game Master); draws nothing in any other game.
    private readonly GameMasterPanel     _gameMasterPanel = new();
    private readonly TacticalOverlay.TacticalOverlayController _tacticalOverlay = new();
    private readonly EscapeMenuOverlay _escapeMenu = new();
    private readonly ArmyListOverlay _armyListOverlay = new();
    private bool _inGame = false;
    private bool _closeRequested = false;
    private bool _resolverOverlayFaulted = false;

    /// <summary>
    /// Runs once inside <see cref="Run"/>, right after the window + ImGui exist and before the first
    /// frame. The no-lobby <c>--scenario</c> launch (#167) hooks its game wiring here: TransitionToGame
    /// attaches the tactical overlay, which creates GL resources (#162) and therefore needs a live
    /// window - calling it before Run() segfaults inside raylib (null GL function pointers).
    /// </summary>
    public Action? OnWindowReady;

    // Starts the "open a .fdgsave and resume it" flow (the same one the main menu's Load Game runs).
    // Program.cs assigns the shared implementation; the in-game menu's Load option invokes it after
    // tearing the current game down and returning to the menu.
    public Action? OnLoadGameRequested;

    // Fired at the end of ExitGame() — i.e. on every path that tears a running game down (#271).
    public Action? OnGameExited;

    /// <summary>What the Take Back / Redo buttons show. Null (or no provider) = Take Back isn't offered
    /// in this game (networked play, a client, the --scenario launch).</summary>
    public sealed record TakeBackStatus(bool CanTakeBack, string TakeBackTip, bool CanRedo, string RedoTip);

    /// <summary>Program.cs supplies the current Take Back state; read once per frame while in game.</summary>
    public Func<TakeBackStatus?>? TakeBackStatusProvider;

    /// <summary>Take Back (false) or Redo (true) was pressed. Program.cs relaunches the game from the
    /// chosen save point. Invoked at the top of a frame, outside any drawing.</summary>
    public Action<bool>? OnTakeBackRequested;

    // Set by the button / hotkey mid-frame; acted on at the top of the next frame so the game is never torn
    // down in the middle of drawing it.
    private bool? _pendingTakeBack;

    /// <summary>Tears the running game down (same as quitting to the menu, minus the navigation).</summary>
    public void ExitCurrentGame() => ExitGame();

    // ── 3D view ──────────────────────────────────────────────────────────────────────────
    // The player's choice (button / F3). Stage 2: the whole game plays in 3D. Every table tool keeps doing
    // exactly what it does in 2D, but onto a flat "virtual board" (Board3DScale pixels per inch, origin at
    // the table's top-left) instead of the screen; that picture is then painted onto the 3D table (see
    // CaptureBoardOverlay), and the mouse reaches the tools through TableInput as the point of the 3D table
    // it is over.
    private bool _want3D;
    private Table3DView? _view3D;

    private bool Showing3D => _inGame && _want3D && _gameOverResult == null;

    /// <summary>Virtual-board resolution in 3D: pixels per table inch.</summary>
    private const float Board3DScale = 16f;

    private Layout VirtualBoardLayout => new(Board3DScale, 0, 0,
        (int)(TableWIn * Board3DScale), (int)(TableHIn * Board3DScale));

    // 3D: the last board point the pointer was over (kept while the pointer is off the 3D view).
    private Vector2? _last3DBoardPoint;

    private RenderTexture2D _boardOverlayRT;
    private bool _boardOverlayReady;


    // Take Back relaunch in progress: the old board stays up (dimmed, inputs off) until the relaunched
    // game's launch is picked up straight from the lobby screen - which is never shown - and
    // TransitionToGame swaps the new game in.
    private bool _takeBackInProgress;
    private double _takeBackStartedAt;
    private const double TakeBackTimeoutSeconds = 30.0;

    /// <summary>Program.cs: a Take Back relaunch is starting - keep the board on screen meanwhile.</summary>
    public void BeginTakeBack()
    {
        _takeBackInProgress = true;
        _takeBackStartedAt = Raylib.GetTime();
    }

    /// <summary>Program.cs: the relaunch could not start. Leave the stopped game for the main menu.</summary>
    public void AbortTakeBack()
    {
        _takeBackInProgress = false;
        ExitGame();
        NavigateTo(MainMenu);
    }
    // Set from the engine thread when the game ends (see ShowGameOver); read on the main thread to draw
    // the game-over overlay. Non-null = game finished, result string to display.
    private volatile string? _gameOverResult = null;

    // Optional second line under the result (#187: "Recovery save written to ..."). Same threading rules
    // as _gameOverResult.
    private volatile string? _gameOverNote = null;

    // #331: celebration behind the game-over card. Main-thread only (built and drawn in the render loop),
    // so unlike the two fields above it needs no volatile — _gameOverResult is the cross-thread signal.
    private readonly VictoryFireworks _fireworks = new VictoryFireworks();
    private bool _fireworksStarted;

    // Offscreen target for the Ambush enemy-exclusion blob: discs are painted opaque here (so overlaps
    // overwrite instead of stacking alpha), then composited once at a uniform light alpha. Lazily sized
    // to the window and recreated on resize; unloaded on shutdown.
    private RenderTexture2D _exclusionRT;
    private bool _exclusionRTReady;
    private int  _exclusionRTW, _exclusionRTH;
    // Opaque while painting the union; the final on-table alpha comes from the composite tint below.
    private static readonly Color ExclusionFill = new(235, 70, 70, 255);
    private static readonly Color ExclusionRim = new(255, 40, 40, 255);
    private const byte ExclusionCompositeAlpha = 105;

    // Subtle mottling over the felt: a small tileable Perlin-noise texture generated once, then tiled
    // across the table rect (repeat wrap) and composited additively at a low dark tint so it reads as
    // faint flecks (grass, sand grain, slab staining) rather than a flat surface. Lazily generated on
    // first draw; regenerated when the table background changes (each style has its own noise scale and
    // sample offset, so the patterns differ); unloaded on shutdown.
    private Texture2D _mottleTex;
    private bool      _mottleReady;
    private ETableBackground _mottleFor = ETableBackground.Forest;

    public RaylibRenderer()
    {
        _currentScreen = MainMenu;

        // In-game menu actions. Returning to the menu and quitting both tear the game down first; these
        // reuse the exact pair the game-over card uses (ExitGame + NavigateTo) so there's one teardown path.
        _escapeMenu.OnReturnToMainMenu = () => { ExitGame(); NavigateTo(MainMenu); };
        _escapeMenu.OnQuitToDesktop    = RequestClose;
        // Load ends the current game, returns to the menu, then runs the shared load-from-file flow.
        _escapeMenu.OnLoadGame = () => { ExitGame(); NavigateTo(MainMenu); OnLoadGameRequested?.Invoke(); };
    }

    public void NavigateTo(IAppScreen screen)
    {
        _currentScreen = screen;
        screen.OnShown();
    }

    // The owning unit rides along with the colour so DrawModels can ask whether the model is actually in
    // play. A model belonging to an off-table unit (Ambush reserve, embarked, an Aircraft that flew off)
    // sits at the world origin, which maps to the table's bottom-left corner - it must not be drawn there.
    private readonly ConcurrentDictionary<IModel, (Color Color, IUnit Unit)> _placedModels = new();

    /// <summary>The grey a downed hero's base is tinted (matches PresentationPlayer's own).</summary>
    private static readonly TextColor DownedHeroTint = new(150, 150, 170, 255);

    /// <summary>
    /// Star Quest: a hero that goes down is unconscious, not dead - it lies where it fell until the party
    /// revives it. The engine counts a unit with no living model as off the battlefield, so the draw loops
    /// have to ask this as well, or a knocked-out hero's token vanishes the moment it drops and there is
    /// nothing left to walk over to.
    /// </summary>
    private static bool IsDownedHeroOnTable(IUnit unit) =>
        unit is UnitData { IsQuestHero: true }
        && unit.Models.Any(m => m.Position.x != 0f || m.Position.z != 0f);

    /// <summary>Where a model is drawn when no animation is playing: a downed hero stays, greyed.</summary>
    private static ModelDrawState RestingDrawState(IUnit unit, IModel model)
    {
        if (model.GetIsAlive()) return new ModelDrawState(true, model.Position, 1f, null);
        if (unit is UnitData { IsQuestHero: true } && (model.Position.x != 0f || model.Position.z != 0f))
            return new ModelDrawState(true, model.Position, 0.45f, DownedHeroTint);
        return ModelDrawState.Hidden;
    }

    private bool _autoScroll = true;
    private int  _lastLogCount = 0;

    private record Layout(float Scale, int OriginX, int OriginY, int AreaW, int ScreenH);

    // In-game right column: a fixed-width strip on the right holding the resolver panel (top half) and the
    // log/chat console (bottom half). Everything left of it is the table viewport. Width is a fraction of
    // the window, floored so the panels stay usable on narrow windows and capped at half the width.
    private const float RightColumnFraction = 0.28f;
    private const int   RightColumnMinPx    = 340;
    private int RightColumnWidth(int screenW) =>
        _log == null ? 0 : Math.Clamp((int)(screenW * RightColumnFraction), RightColumnMinPx, screenW / 2);

    // Table view transform (#8): _zoom is a multiplier over the fit-to-viewport scale (1 = ~100% of the
    // viewport, up to MaxZoom); _pan is a pixel offset from the centered position. Both default to the
    // plain fit until the player zooms (Alt+wheel, toward the cursor) or pans (middle-drag).
    private const float MinZoom = 1f;   // fully zoomed out = table fills the viewport
    private const float MaxZoom = 3f;   // 300%
    // When zoomed in, allow panning this fraction of the table past each edge, so a strip of background
    // shows and it's clear you've hit the edge. Ignored at min zoom (the table stays locked centered).
    private const float PanEdgeMarginFraction = 0.05f;
    private float   _zoom = 1f;
    private Vector2 _pan  = Vector2.Zero;

    // Fit-to-viewport scale: the largest scale that shows the whole table inside the viewport (with a
    // margin). _zoom multiplies this; MinZoom==1 means "table fills the viewport".
    private static float FitScale(int viewportW, int screenH)
    {
        float fitX = (viewportW - MinMargin * 2f) / TableWIn;
        float fitY = (screenH   - MinMargin * 2f) / TableHIn;
        return Math.Max(1f, Math.Min(fitX, fitY));
    }

    // Smallest rectangle containing both — used to ghost the dice strip against every in-flight attack
    // at once (#327).
    private static Rectangle Union(Rectangle a, Rectangle b)
    {
        float x = Math.Min(a.X, b.X);
        float y = Math.Min(a.Y, b.Y);
        return new Rectangle(x, y,
            Math.Max(a.X + a.Width,  b.X + b.Width)  - x,
            Math.Max(a.Y + a.Height, b.Y + b.Height) - y);
    }

    public void TransitionToGame(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        GameLog? log, GuiResolverOverlay? resolverOverlay = null,
        GuiOutstandingTaskDisplay? taskDisplay = null,
        PresentationPlayer? presentationPlayer = null,
        Func<string?>? saveGameToJson = null,
        GuiPlayerMessageUI? playerMessageUI = null)
    {
        // Take Back: the old (stopped) game is still on screen. Clear it out without the full exit
        // (Program.cs has already torn its lobby down), and keep the view where the player had it.
        (float Zoom, Vector2 Pan, bool Chat, bool Log)? keptView = null;
        DiceTray.Reset();
        if (_inGame)
        {
            keptView = (_zoom, _pan, _showChat, _showLog);
            ExitGame(notifyExited: false);
        }
        _takeBackInProgress = false;

        _tableState         = tableState;
        _colorForPlayer     = colorForPlayer;
        // Star Quest: downed heroes stay drawn (greyed) so the party can find and revive them.
        PresentationPlayer.IsUnconsciousHero = model => tableState.Units.Objects
            .Any(u => u is UnitData { IsQuestHero: true } && u.Models.Contains(model));
        _log                = log;
        // The battle report says who shot at whom (blue = yours, red = the opponent's).
        GuiChooseRangedAttackResolver.ShotChosen = null;
        GuiAssignWoundsResolver.ClearActive();
        var localIds = taskDisplay?.LocalPlayerIDs.ToList() ?? new List<PlayerID>();
        DiceTray.IsMine = u => localIds.Contains(u.PlayerID);
        if (presentationPlayer != null && !ReferenceEquals(_reportHookedPlayer, presentationPlayer))
        {
            presentationPlayer.BeatStarted += DiceTray.OnBeat;
            _reportHookedPlayer = presentationPlayer;
        }
        _resolverOverlay    = resolverOverlay;
        _taskDisplay        = taskDisplay;
        _presentationPlayer = presentationPlayer;
        _playerMessageUI    = playerMessageUI;
        // #265: the launched game's table surface rides the overlay (stamped in BuildGui). The mottle
        // texture is regenerated lazily on the next draw when this differs from what it was baked for.
        _background         = resolverOverlay?.TableBackground ?? ETableBackground.Forest;
        _backgroundStyle    = TableBackgrounds.For(_background);
        _tooltipOverlay.Attach(tableState, colorForPlayer, presentationPlayer);
        // #329: local player IDs ride the task display (the one launch-path object that carries
        // them), so the army list can put "your" tab first.
        _armyListOverlay.Attach(tableState, colorForPlayer,
            taskDisplay?.LocalPlayerIDs ?? [], resolverOverlay);
        _escapeMenu.AttachSave(saveGameToJson);
        // A save made before round 1 can't be resumed, so saving opens once the first round starts.
        _escapeMenu.CanSaveNow = () => _tableState?.Progress.RoundCount != null;
        // #226: the bug reporter captures this launch's log + chat and (on the host) the save hook,
        // so a report carries everything the session knows. The announce hook rides the chat relay
        // so the other players - whose chat and army lists a report carries - are told it happened.
        _escapeMenu.AttachBugReport(new BugReport.BugReporter(log, playerMessageUI?.ChatLog, saveGameToJson,
            announce: playerMessageUI == null
                ? null
                : text => playerMessageUI.Submit(text, EChatMessageType.Global)));
        _measurementOverlay.Attach(tableState);
        // [overlay] messages are developer detail (rebuild-budget warnings) -> the Debug log category.
        _tacticalOverlay.Attach(tableState, msg => _log?.Add(msg, new TextColor(255, 180, 90, 255), isDebug: true),
            pid => { Color c = colorForPlayer(pid); return (c.R, c.G, c.B); },
            // #201/#384: the launched game's cover + LoS settings ride the resolver overlay
            // (stamped in BuildGui).
            coverProximityExceptions: resolverOverlay?.CoverProximityExceptions ?? true,
            seeThroughFriendlyUnits: resolverOverlay?.SeeThroughFriendlyUnits ?? false);
        _tacticalOverlay.AttachMovementResolver(resolverOverlay?.MovementResolver);
        // #230: lets the field anchor on a placement's ghosts when no move job is running.
        _tacticalOverlay.AttachResolverOverlay(resolverOverlay);
        // The menu's Options panel drives the tactical toggles (Threat, field anchor) and master volume.
        _escapeMenu.AttachOptions(_tacticalOverlay, _audio);
        // #280: preview sharing rides the overlay (built at launch by GameGuiWiring.AttachPreviews).
        _previewPublisher = resolverOverlay?.PreviewPublisher;
        _remotePreviews   = resolverOverlay?.RemotePreviews;
        _remotePreviews?.AttachColors(colorForPlayer);

        // Play a sound cue the moment each beat becomes active, in lockstep with its visual. Audio is
        // GUI-only and may be unavailable (then AudioManager no-ops), so this is best-effort.
        if (_presentationPlayer != null && _audio != null)
        {
            _presentationPlayer.BeatStarted += beat =>
            {
                string? cue = PresentationSoundCues.CueFor(beat);
                if (cue != null) _audio.Play(cue);
            };
            // #238: attacks sound once per VOLLEY (in step with each visible burst of shots/swings),
            // not once at beat start. #239: each cue is voiced by the weapon's effect set, and a
            // volley that lands something also sounds its impact when the shots arrive.
            _presentationPlayer.AttackVolleyStarted += attack =>
                _audio.Play(PresentationSoundCues.VolleyCue(attack));
            _presentationPlayer.AttackVolleyImpact += attack =>
                _audio.Play(PresentationSoundCues.ImpactCue(attack));
            // #294: movement patters once per FOOTFALL for as long as the models are gliding, pitched
            // down by the unit's weight and alternating foot to foot.
            _presentationPlayer.UnitStepped += (moved, step) =>
            {
                var (cue, pitch, volume) = PresentationSoundCues.StepVoice(moved, step);
                _audio.Play(cue, pitch, volume);
            };
        }

        tableState.Models.OnObjectCreated += SubscribeToModel;
        foreach (var model in tableState.Models.Objects)
            SubscribeToModel(model);

        tableState.Terrain.OnObjectCreated += AddTerrain;
        tableState.Terrain.OnObjectRemoved += RemoveTerrain;
        foreach (var terrain in tableState.Terrain.Objects)
            AddTerrain(terrain);

        tableState.Objectives.OnObjectCreated += AddObjective;
        tableState.Objectives.OnObjectRemoved += RemoveObjective;
        foreach (var objective in tableState.Objectives.Objects)
            AddObjective(objective);

        if (keptView is { } view)
        {
            _zoom     = view.Zoom;
            _pan      = view.Pan;
            _showChat = view.Chat;
            _showLog  = view.Log;
        }

        _inGame = true;
    }

    private readonly List<ITerrain>   _terrain      = new();
    private readonly object           _terrainLock  = new();
    private readonly List<IObjective> _objectives   = new();
    private readonly object           _objectivesLock = new();

    private void AddTerrain(ITerrain terrain)
    {
        lock (_terrainLock) _terrain.Add(terrain);
    }

    private void RemoveTerrain(ITerrain terrain)
    {
        lock (_terrainLock) _terrain.Remove(terrain);
    }

    private void AddObjective(IObjective objective)
    {
        lock (_objectivesLock) _objectives.Add(objective);
    }

    private void RemoveObjective(IObjective objective)
    {
        lock (_objectivesLock) _objectives.Remove(objective);
    }

    public void RequestClose() => _closeRequested = true;

    /// <summary>
    /// Records that the game has finished so the next frame can draw the game-over overlay. Called from
    /// the engine thread (via the lobby's <c>OnGameEnded</c>), so it only stores the result — the actual
    /// teardown + navigation happens on the main thread when the player clicks "Return to Main Menu".
    /// </summary>
    public void ShowGameOver(string result)
    {
        _gameOverResult = result;
        // Star Quest: the heroes keep what they earned (XP, levels) - write it back to their hero files.
        if (_tableState != null) FdgRaylib.Quest.QuestHeroSync.WriteBack(_tableState);
    }

    /// <summary>
    /// #187: a secondary line for the game-over card, used to tell the host where the recovery save for a
    /// disconnected game was written. Set from the engine thread before <see cref="ShowGameOver"/>; the
    /// card reads it on the main thread. Cleared by <see cref="ExitGame"/> with the result.
    /// </summary>
    public void SetGameOverNote(string? note) => _gameOverNote = note;

    /// <summary>GF: Firefight Campaign: writes each strike team's result to its file (host, end of game). Returns a
    /// line for the game-over card, or null when no strike team played.</summary>
    public string? RecordStrikeTeams(GameResult result) => FdgRaylib.Quest.StrikeTeamFiles.WriteBack(_tableState, result);

    /// <summary>
    /// Tears down all in-game state so the renderer can return to the screen stack and a later launch
    /// starts clean. Unsubscribes the table-state event handlers wired in <see cref="TransitionToGame"/>
    /// and drops every per-game reference. Runs on the main thread.
    /// </summary>
    private void ExitGame(bool notifyExited = true)
    {
        _view3D?.Unload();
        _view3D = null;
        if (_tableState != null)
        {
            _tableState.Models.OnObjectCreated      -= SubscribeToModel;
            _tableState.Terrain.OnObjectCreated     -= AddTerrain;
            _tableState.Terrain.OnObjectRemoved     -= RemoveTerrain;
            _tableState.Objectives.OnObjectCreated  -= AddObjective;
            _tableState.Objectives.OnObjectRemoved  -= RemoveObjective;
        }

        _measurementOverlay.Reset();
        _gameMasterPanel.Reset();
        _tacticalOverlay.Detach();
        _escapeMenu.Close();
        _escapeMenu.AttachBugReport(null); // per-game refs (log, save hook) must not outlive the game
        _armyListOverlay.Close();
        _placedModels.Clear();
        lock (_terrainLock)    _terrain.Clear();
        lock (_objectivesLock) _objectives.Clear();

        _tableState            = null;
        _colorForPlayer        = null;
        _log                   = null;
        _playerMessageUI       = null;
        _chatInput             = "";
        _zoom                  = 1f;
        _pan                   = Vector2.Zero;
        _showChat              = true;
        _showLog               = true;
        _showDebug             = false;
        _chatChannel           = EChatMessageType.Global;
        _chatUnread            = false;
        _lastChatCount         = 0;
        _resolverOverlay       = null;
        _taskDisplay           = null;
        _previewPublisher      = null;
        _remotePreviews        = null;
        _presentationPlayer    = null;
        _resolverOverlayFaulted = false;
        _lastLogCount          = 0;
        _gameOverResult        = null;
        _gameOverNote          = null;
        _inGame                = false;

        // Every game teardown funnels through here (game-over card, escape-menu quit-to-menu,
        // escape-menu load). Program.cs uses this to stop the public-listing heartbeat (#271).
        // Take Back's in-place swap skips it: its new lobby is already the active one.
        if (notifyExited) OnGameExited?.Invoke();
    }

    /// <summary>
    /// Take Back / Redo, just left of the Measure button, plus their keys (Ctrl+Z / Ctrl+X) whenever the
    /// active tool isn't using those keys for its own undo (deployment, movement planning).
    /// </summary>
    /// <summary>Dims the board and swallows input while a Take Back relaunch is on its way.</summary>
    private void DrawTakeBackCurtain(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH));
        ImGui.SetNextWindowBgAlpha(0.35f);
        ImGui.SetNextWindowFocus();
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav;
        if (ImGui.Begin("##takeBackCurtain", flags))
        {
            const string text = "Taking back...";
            Vector2 size = ImGui.CalcTextSize(text);
            ImGui.SetCursorPos(new Vector2((screenW - size.X) * 0.5f, (screenH - size.Y) * 0.5f));
            ImGui.TextUnformatted(text);
        }
        ImGui.End();
    }

    /// <returns>The buttons' left edge, so the 3D toggle can sit beside them; null when not drawn.</returns>
    private float? DrawTakeBackButtons(float? rightEdge)
    {
        if (_takeBackInProgress) return null;
        TakeBackStatus? status = TakeBackStatusProvider?.Invoke();
        if (status == null || rightEdge == null) return null;

        const string takeBackLabel = "Take Back";
        const string redoLabel = "Redo";
        var style = ImGui.GetStyle();
        float width = ImGui.CalcTextSize(takeBackLabel).X + ImGui.CalcTextSize(redoLabel).X
            + style.FramePadding.X * 4f + style.ItemSpacing.X;
        float x = MathF.Max(4f, rightEdge.Value - 10f - width);
        float y = StatusHudOverlay.StripTop + (StatusHudOverlay.StripHeight - ImGui.GetFrameHeight()) * 0.5f;

        ImGui.SetNextWindowPos(new Vector2(x, MathF.Max(2f, y)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##takeBack", flags))
        {
            ImGui.BeginDisabled(!status.CanTakeBack);
            if (ImGui.Button(takeBackLabel)) _pendingTakeBack = false;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(status.TakeBackTip);

            ImGui.SameLine();
            ImGui.BeginDisabled(!status.CanRedo);
            if (ImGui.Button(redoLabel)) _pendingTakeBack = true;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(status.RedoTip);
        }
        ImGui.End();
        ImGui.PopStyleVar(2);

        var io = ImGui.GetIO();
        if (io.KeyCtrl && !io.WantCaptureKeyboard && !(_resolverOverlay?.ActiveToolUsesUndoKeys ?? false))
        {
            if (status.CanTakeBack && ImGui.IsKeyPressed(ImGuiKey.Z, false)) _pendingTakeBack = false;
            else if (status.CanRedo && ImGui.IsKeyPressed(ImGuiKey.X, false)) _pendingTakeBack = true;
        }
        return x;
    }

    /// <summary>The 2D / 3D switch, left of the Take Back buttons (or the Measure button).</summary>
    private void Draw3DToggle(float? rightEdge, bool showing3D)
    {
        if (rightEdge == null) return;
        string label = !_want3D ? "3D View" : showing3D ? "2D View" : "3D View (game over)";
        var style = ImGui.GetStyle();
        float width = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2f;
        float x = MathF.Max(4f, rightEdge.Value - 10f - width);
        float y = StatusHudOverlay.StripTop + (StatusHudOverlay.StripHeight - ImGui.GetFrameHeight()) * 0.5f;

        ImGui.SetNextWindowPos(new Vector2(x, MathF.Max(2f, y)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##view3dToggle", flags))
        {
            bool on = _want3D;
            if (on)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.ButtonCommit);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.ButtonCommitHover);
            }
            if (ImGui.Button(label)) _want3D = !_want3D;
            if (on) ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(!_want3D
                    ? "Switch to the 3D view (F3 or V)."
                    : showing3D
                        ? "Back to the 2D view (F3 or V)."
                        : "The game-over screen shows the 2D board. Click to turn 3D off (F3 or V).");
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    /// <summary>
    /// 3D view: paints this frame's table-tool drawing onto the virtual board texture the 3D table shows.
    /// The tools drew it on ImGui's background draw list in virtual-board coordinates; this renders that
    /// list into the texture (with rlImGui's own triangle routine) and then empties it, so none of it also
    /// lands on the screen. The tactical overlay's Raylib-drawn field goes underneath it. The 3D table shows
    /// the result from the next frame on, a lag no one can see.
    /// </summary>
    private void CaptureBoardOverlay()
    {
        if (_view3D == null) return;
        int w = (int)(TableWIn * Board3DScale), h = (int)(TableHIn * Board3DScale);
        if (!_boardOverlayReady || _boardOverlayRT.Texture.Width != w || _boardOverlayRT.Texture.Height != h)
        {
            if (_boardOverlayReady) Raylib.UnloadRenderTexture(_boardOverlayRT);
            _boardOverlayRT = Raylib.LoadRenderTexture(w, h);
            Raylib.SetTextureFilter(_boardOverlayRT.Texture, TextureFilter.Bilinear);
            _boardOverlayReady = true;
        }

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        dl.PopClipRect();

        // The Ambush no-go area paints into its own texture first (texture modes can't nest), and the reach
        // field rebuilds into its own textures here too, for the same reason.
        Layout board = VirtualBoardLayout;
        _tacticalOverlay.PrepareField();
        bool exclusion = RenderAmbushExclusion(board, w, h, out var exclusionWaivers);

        Raylib.BeginTextureMode(_boardOverlayRT);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        // Premultiplied output: color blends as usual, alpha accumulates (1 - a) the normal way, so the
        // picture composites onto the table exactly as it would have onto the screen.
        const int SrcAlpha = 0x0302, OneMinusSrcAlpha = 0x0303, One = 1, FuncAdd = 0x8006;
        Rlgl.SetBlendFactorsSeparate(SrcAlpha, OneMinusSrcAlpha, One, OneMinusSrcAlpha, FuncAdd, FuncAdd);
        Raylib.BeginBlendMode(BlendMode.CustomSeparate);

        // The tactical overlay's reach field and contours are drawn with Raylib, under the tools.
        _tacticalOverlay.DrawField();
        _tacticalOverlay.DrawContours();
        if (exclusion) CompositeAmbushExclusion(board, exclusionWaivers);

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        ImPtrVector<ImDrawCmdPtr> cmds = dl.CmdBuffer;
        for (int i = 0; i < cmds.Size; i++)
        {
            ImDrawCmdPtr cmd = cmds[i];
            if (cmd.UserCallback != IntPtr.Zero || cmd.ElemCount == 0) continue;
            DrawImGuiTriangles(dl, cmd);
            cmd.ElemCount = 0;   // drawn on the table - don't also draw it on the screen
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.SetTexture(0);
        Rlgl.EnableBackfaceCulling();

        Raylib.EndBlendMode();
        Raylib.EndTextureMode();
        _view3D.SetOverlay(_boardOverlayRT.Texture);
    }

    /// <summary>
    /// Draws one ImGui draw command's triangles with Rlgl, into whatever target is bound (the 3D table's board
    /// texture). Our own copy of what rlImGui does at the end of the frame - it used to borrow rlImGui's
    /// private routine through reflection, which isn't found in every build of rlImGui; when it went missing
    /// the tools' drawing stayed on the screen, huge, instead of moving onto the table.
    /// </summary>
    private static unsafe void DrawImGuiTriangles(ImDrawListPtr dl, ImDrawCmdPtr cmd)
    {
        var verts = (ImDrawVert*)dl.VtxBuffer.Data;
        var indices = (ushort*)dl.IdxBuffer.Data;
        uint texture = (uint)cmd.TextureId.ToInt64();
        // Begin first: starting a new draw resets its texture to the default one.
        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.SetTexture(texture);
        for (uint n = 0; n + 2 < cmd.ElemCount; n += 3)
        {
            // Keep each triangle inside one batch (Rlgl flushes when full, and must not split a triangle).
            if (Rlgl.CheckRenderBatchLimit(3))
            {
                Rlgl.Begin(DrawMode.Triangles);
                Rlgl.SetTexture(texture);
            }
            for (uint k = 0; k < 3; k++)
            {
                ImDrawVert v = verts[indices[cmd.IdxOffset + n + k] + cmd.VtxOffset];
                Rlgl.Color4ub((byte)(v.col & 0xFF), (byte)((v.col >> 8) & 0xFF), (byte)((v.col >> 16) & 0xFF),
                    (byte)((v.col >> 24) & 0xFF));
                Rlgl.TexCoord2f(v.uv.X, v.uv.Y);
                Rlgl.Vertex2f(v.pos.X, v.pos.Y);
            }
        }
        Rlgl.End();
    }

    /// <summary>Gathers this frame's table state and hands it to the 3D view.</summary>
    private void Draw3DBoard(Rectangle boardRect)
    {
        if (_view3D == null || _tableState == null) return;

        var liveUnitByModel = new Dictionary<ModelID, IUnit>();
        foreach (IUnit u in _tableState.Units.Objects)
            foreach (IModel m in u.Models)
                liveUnitByModel[m.ID] = u;
        UnitID? active = _tableState.Progress.ActivatingUnit?.ID;

        Dictionary<PlayerID, string> factionByPlayer = FactionByPlayer();

        var models = new List<Table3DView.ModelDraw>();
        foreach (var (model, (color, cachedUnit)) in _placedModels)
        {
            IUnit unit = liveUnitByModel.TryGetValue(model.ID, out IUnit? live) ? live : cachedUnit;
            if (!unit.GetIsOnBattlefield() && !IsDownedHeroOnTable(unit)) continue;
            ModelDrawState draw = _presentationPlayer?.GetModelDrawState(model) ?? RestingDrawState(unit, model);
            if (!draw.Visible) continue;

            bool joinedHero = HeroMarkerRenderer.IsHeroModel(unit, model);
            bool heroUnit = joinedHero || unit.RuleDefinitions.Any(r => r.Definition.Name == "Hero");
            string label = joinedHero ? ((unit as UnitData)?.HeroAttachment?.Name ?? unit.Name) : unit.Name;
            Color baseColor = draw.Tint is { } tint ? new Color(tint.R, tint.G, tint.B, (byte)255) : color;
            models.Add(new Table3DView.ModelDraw(draw.Position, model.BaseRadiusInches, baseColor, draw.Alpha,
                unit.ID, label, heroUnit, active.HasValue && unit.ID == active.Value, model.ID,
                factionByPlayer.TryGetValue(unit.PlayerID, out string? faction) ? faction : ""));
        }

        ITerrain[] terrainSnapshot;
        lock (_terrainLock) terrainSnapshot = _terrain.ToArray();
        var terrain = terrainSnapshot
            .Select(t => new Table3DView.TerrainDraw(t.Shape, t.TerrainType, t.HeightInches)).ToList();

        IObjective[] objectiveSnapshot;
        lock (_objectivesLock) objectiveSnapshot = _objectives.ToArray();
        var objectives = new List<Table3DView.ObjectiveDraw>();
        for (int i = 0; i < objectiveSnapshot.Length; i++)
        {
            IObjective obj = objectiveSnapshot[i];
            Color c = obj.OwnerID.HasValue ? _colorForPlayer!(obj.OwnerID.Value) : ObjectiveNeutralColor;
            objectives.Add(new Table3DView.ObjectiveDraw(obj.Position, (obj.HordeNumber ?? i + 1).ToString(), c,
                obj.IsHordePriority));
        }
        foreach (QuestMarkerDraw m in QuestMarkerDraws())
            objectives.Add(new Table3DView.ObjectiveDraw(m.Position, m.Label, m.Color, false, IsToken: true));
        foreach (FDG.QuestToken token in QuestTokens())
            objectives.Add(new Table3DView.ObjectiveDraw(new Position(token.X, token.Z), token.IsTreasure ? "$" : "?",
                token.IsTreasure ? QuestTreasureColor : QuestSearchColor, false, IsToken: true));

        var attacks = new List<Table3DView.AttackDraw>();
        if (_presentationPlayer != null)
            foreach ((AttackBeat beat, float progress) in _presentationPlayer.GetActiveAttacks())
                attacks.Add(new Table3DView.AttackDraw(beat.From, beat.To, beat.IsMelee,
                    progress * Math.Max(1, beat.VolleyCount)));

        _view3D.Draw(boardRect, _backgroundStyle.Surface, _backgroundStyle.Border, Background,
            terrain, objectives, models, attacks);
    }

    private void SubscribeToModel(IModel model)
    {
        model.OnPositionChanged += (_, _) => OnModelPlaced(model);

        // A model restored from a save already has its position set, so no OnPositionChanged will
        // fire to register it for drawing — seed it now. (0,0,0) means unplaced, so skip those.
        if (model.Position.x != 0f || model.Position.z != 0f)
            OnModelPlaced(model);
    }

    private void OnModelPlaced(IModel model)
    {
        // A per-model OnPositionChanged subscription (wired via a lambda we can't unsubscribe) may fire
        // after ExitGame has dropped the table state. Nothing to draw once the game is gone.
        if (_tableState == null || _colorForPlayer == null) return;

        // #309: match by ModelID, not instance. On a networked client every replicated update to a
        // ModelData slot deserializes a NEW instance, so the instance this subscription captured at
        // creation may no longer be the one the unit's bindings return - a reference Contains would
        // then never find the unit and the model would never register for drawing.
        var unit = _tableState.Units.Objects.FirstOrDefault(u => u.Models.Any(m => m.ID == model.ID));
        if (unit != null)
            _placedModels[model] = (_colorForPlayer(unit.PlayerID), unit);
    }

    /// <summary>
    /// UI scale derived from the display height so the interface isn't oversized on a 1080p laptop yet
    /// stays exactly as tuned on a 4K desktop. Anchored at <see cref="ReferenceUiScale"/> for a 2160p
    /// display, scaled proportionally below that, and clamped: floored at 1.0 so small screens stay
    /// readable, capped at the reference so &gt;4K doesn't balloon.
    ///
    /// TODO: only verified on a 1080p laptop and a 4K desktop. Test on more monitors (1440p, ultrawide,
    /// and displays with fractional OS scaling) and tune the anchor/floor if the UI feels off. Also note
    /// this is computed once at startup from the monitor — it doesn't re-derive on window resize / monitor
    /// move (fonts are baked at load).
    /// </summary>
    internal static float ComputeUiScale(int monitorHeightPx)
    {
        if (monitorHeightPx <= 0) return ReferenceUiScale; // unknown display — keep the tuned default
        float scaled = monitorHeightPx / ReferenceHeightPx * ReferenceUiScale;
        return Math.Clamp(scaled, 1.0f, ReferenceUiScale);
    }

    public void Run()
    {
        // Window mode is a remembered per-user setting (#310): borderless-windowed fullscreen by
        // default, toggled with F11 (main loop below). Borderless covers the whole monitor with no
        // video-mode change (safest across Linux WMs and multi-monitor) and clean alt-tab. The window
        // stays resizable so a toggle drops it back to a windowed 1280x720 and back.
        // ToggleBorderlessWindowed must be called after InitWindow; it sizes the window to the monitor.
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(1280, 720, "Future of Dark Grimness");
        // 60, not 30. Raylib samples the mouse once per frame and rlImGui hands ImGui that sample, so
        // the frame interval is also the input interval: at 30 a click shorter than 33ms could be pressed
        // and released between two samples and vanish entirely, which read as "I have to click twice"
        // (#398 owner report - clicking an army in the Combat Calculator seemed to need a double-click,
        // and the second click landed on whatever the first one had revealed). Nothing here is expensive
        // to draw; the screens are ImGui panels over a static canvas.
        Raylib.SetTargetFPS(60);
        if (UserConfig.Current.StartFullscreen)
            Raylib.ToggleBorderlessWindowed();

        // Escape is not a quit key. It reads as "cancel the action I'm in the middle of" - several
        // resolvers already bind it that way - and raylib's default exit key made it tear the window
        // down instead. Quit via the main menu or the window close button.
        Raylib.SetExitKey(KeyboardKey.Null);

        int monitor   = Raylib.GetCurrentMonitor();
        int monitorH  = Raylib.GetMonitorHeight(monitor);

        float uiScale = ComputeUiScale(monitorH);

        rlImGui.Setup(true);
        // Let one draw list hold more than 65,536 points. Without this, a busy table layer (big weapon-range
        // circles on long-ranged units) overflowed ImGui's 16-bit point numbers, and the 3D table showed
        // shapes built from the wrong points: the moving unit's ghost jumped away from the mouse and
        // flickered. Our 3D capture (DrawImGuiTriangles) honours each command's VtxOffset.
        ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        // Apply the app-wide "Dark Grimness" theme BEFORE scaling, so its rounding/border sizes scale
        // with the display too (colors are unaffected by ScaleAllSizes).
        ImGuiTheme.Apply();
        // Enlarge every widget's padding/spacing/frame sizes (fonts are scaled at load below).
        ImGui.GetStyle().ScaleAllSizes(uiScale);

        // App-wide audio device + presentation cue bank (placeholder until real assets land in
        // Assets/Sounds/). No-ops gracefully if no audio device is available.
        _audio = new AudioManager();
        PresentationSoundCues.LoadInto(_audio);
        // UI chrome cues (button clicks, toggles, canvas commits) share the same device; the static
        // UiSound facade lets any screen/resolver voice one without being handed the AudioManager.
        UiSound.Attach(_audio);

        // Replace the default 13px bitmap font with DejaVuSans TTF.
        // Must clear the atlas first — Setup already added the pixel font at index 0;
        // adding without clearing would leave it as the default and push ours to index 1.
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf");
        if (File.Exists(fontPath))
        {
            var fonts = ImGui.GetIO().Fonts;
            fonts.Clear();
            BodyFont  = fonts.AddFontFromFileTTF(fontPath, 18f * uiScale);
            LargeFont = fonts.AddFontFromFileTTF(fontPath, 32f * uiScale);
            // Baked near the largest on-screen menu text (title ~7% of display height) so the menu never
            // upscales the atlas. Clamped so it stays a sane texture size on tiny and 4K displays alike.
            MenuFontPx = Math.Clamp(monitorH * 0.075f, 48f, 220f);
            MenuFont   = fonts.AddFontFromFileTTF(fontPath, MenuFontPx);
            rlImGui.ReloadFonts();
        }

        OnWindowReady?.Invoke();

        while (!Raylib.WindowShouldClose() && !_closeRequested)
        {
            // F11 toggles borderless-windowed fullscreen <-> windowed (so the fullscreen default is
            // never a trap - Escape does not quit here, see SetExitKey above) and remembers the choice:
            // the flip keeps StartFullscreen in step with the actual window state, since startup applied
            // whatever it held. Save is ~0.1ms and only fires on the keypress (#310).
            if (Raylib.IsKeyPressed(KeyboardKey.F11))
            {
                Raylib.ToggleBorderlessWindowed();
                UserConfig.Current.StartFullscreen = !UserConfig.Current.StartFullscreen;
                UserConfig.Save();
            }

            if (_pendingTakeBack is bool redoRequested)
            {
                _pendingTakeBack = null;
                if (_inGame && !_takeBackInProgress) OnTakeBackRequested?.Invoke(redoRequested);
            }

            // Take Back: pick up the relaunched game as soon as its lobby hands it over (that lobby is
            // never drawn). A relaunch that never arrives gives up rather than freezing on the curtain.
            if (_takeBackInProgress)
            {
                LobbyScreen.ConsumePendingLaunch();
                if (_takeBackInProgress && Raylib.GetTime() - _takeBackStartedAt > TakeBackTimeoutSeconds)
                {
                    Console.WriteLine("Take Back: the relaunched game did not start in time.");
                    AbortTakeBack();
                }
            }

            int screenW = Raylib.GetScreenWidth();
            int screenH = Raylib.GetScreenHeight();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            if (_inGame)
            {
                // Options > Game speed: the engine waits longer/shorter per beat, the animations play to match.
                FDG.Presentation.RealtimePresentationClock.PaceScale = 1f / Math.Clamp(ViewSettings.GameSpeed, 0.25f, 4f);
                _presentationPlayer?.Update(Raylib.GetFrameTime() * Math.Clamp(ViewSettings.GameSpeed, 0.25f, 4f));

                bool show3D = Showing3D;
                if (!show3D) HandleTableViewInput(screenW, screenH);
                var layout = ComputeLayout(screenW, screenH);
                var boardRect = new Rectangle(0, 0, layout.AreaW, screenH);
                // Where the table tools draw and measure: the screen in 2D, the virtual board in 3D.
                var tl = show3D ? VirtualBoardLayout : layout;
                TableInput.Override = null;
                TableInput.CardHit = null;
                if (show3D)
                {
                    _view3D ??= new Table3DView(TableWIn, TableHIn);
                    var io3 = ImGui.GetIO();
                    bool toolActive = (_resolverOverlay?.ActiveToolNeedsTable ?? false) || _gameMasterPanel.UsesTable;
                    _view3D.HandleInput(boardRect, mouseFree: !io3.WantCaptureMouse,
                        keysFree: !io3.WantCaptureKeyboard && !_escapeMenu.IsOpen && !_armyListOverlay.IsOpen,
                        rightDragOrbits: !(_resolverOverlay?.ActiveToolUsesRightClick ?? false) && !_gameMasterPanel.UsesTable,
                        plainWheelZooms: !toolActive,
                        letterKeysMove: true);   // letters always drive the camera (panel letter shortcuts are off)
                    var (tablePoint, card) = _view3D.Pick(boardRect, Raylib.GetMousePosition());
                    // A pointer that leaves the 3D view (over the side panel, say, to press Done) keeps its
                    // last table point. Sending it far off the board instead made the move ghosts and sight
                    // lines swing away toward that faraway point the moment the mouse left the table.
                    if (tablePoint is Float2 tp)
                        _last3DBoardPoint = new Vector2(tp.X * Board3DScale, (TableHIn - tp.Y) * Board3DScale);
                    TableInput.Override = _last3DBoardPoint ?? TableInput.OffTable;
                    _view3D.Cursor = tablePoint;
                    TableInput.CardHit = card;
                    _tacticalOverlay.UpdateLayout(tl.Scale, tl.OriginX, tl.OriginY, TableHIn);
                    Draw3DBoard(boardRect);
                }
                else
                {
                // Push the layout to the tactical overlay once per frame so both its canvas-pass draws
                // (below) and its ImGui-pass instruments read the same world<->screen mapping.
                _tacticalOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                DrawTable(layout);
                if (ViewSettings.ShowGrid)
                    DrawTableGrid(layout);   // etched grid + felt vignette, under terrain/objectives/models
                _tacticalOverlay.DrawField();    // opportunity field: under terrain (spec draw order)
                DrawTerrain(layout);
                _tacticalOverlay.DrawContours(); // threat + secondary contours: above terrain, under objectives
                DrawObjectives(layout);
                DrawQuestTokens(layout);
                DrawAmbushExclusion(layout, screenW, screenH);
                DrawActiveUnitSpotlight(layout);
                DrawModels(layout);
                DrawUnitQuestMarkers(layout);   // poison / silence chips ride on top of their token
                DrawDeathBursts(layout);

                // #327: usually one attack, occasionally two overlapping (the next weapon fires before
                // the previous tracers land, which held dice made possible).
                if (_presentationPlayer != null)
                {
                    foreach ((AttackBeat attackBeat, float attackProgress) in _presentationPlayer.GetActiveAttacks())
                        AttackOverlay.Draw(attackBeat, attackProgress, layout.Scale, layout.OriginX,
                            layout.OriginY, TableHIn);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveSave(out var saveBeat, out var saveProgress))
                {
                    SaveOverlay.Draw(saveBeat, saveProgress, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                }

                // #274: cast success/failure, the per-target landing, and the assist streams that
                // swayed the roll — all world-space, over the models they belong to.
                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveSpell(out var spellBeat, out var spellProgress))
                {
                    SpellOverlay.Draw(spellBeat, spellProgress, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                }

                // #399: the dust a unit throws up arriving from reserve. Drawn after the spell layer and
                // over the models - they are already placed by the time the beat plays, so the cloud
                // settling is what reveals them.
                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveArrival(out var arrivalBeat, out var arrivalProgress))
                {
                    DustOverlay.Draw(arrivalBeat, arrivalProgress, layout.Scale, layout.OriginX,
                        layout.OriginY, TableHIn);
                }
                } // end 2D board

                // #327: handed to the tooltip overlay below. Its unit labels are ImGui draw-list text and
                // therefore land ON TOP of these Raylib-drawn panels, so they have to know where the stack
                // is in order to yield to it.
                Rectangle? rollStackBounds = null;
                if (_presentationPlayer != null)
                {
                    // #245: the caption strip ghosts itself while an attack animation reaches into it —
                    // over the union of them when more than one is in flight (#327).
                    Rectangle? diceAvoid = null;
                    foreach ((AttackBeat avoidAttack, float _) in _presentationPlayer.GetActiveAttacks())
                    {
                        Rectangle bounds = AttackOverlay.ScreenBounds(avoidAttack,
                            layout.Scale, layout.OriginX, layout.OriginY, TableHIn, 48f);
                        diceAvoid = diceAvoid.HasValue ? Union(diceAvoid.Value, bounds) : bounds;
                    }

                    // #327: the whole stack - dice rolls AND roll-offs - oldest anchored at the bottom.
                    // Hovering it freezes every panel's timer; the bounds it reports are what the pointer
                    // is tested against next frame (a frame of lag is invisible and avoids measuring the
                    // stack twice).
                    // The dice now live in the dice tray (top of the right column, drawn in the ImGui pass);
                    // nothing is drawn over the board any more. Hovering the tray freezes the rolls.
                    _ = diceAvoid;
                    int trayW = RightColumnWidth(screenW);
                    rollStackBounds = new Rectangle(screenW - trayW, 0, trayW, (int)(screenH * DecisionPanelHeightFraction));
                    // #386: GetMousePosition only updates from in-window motion, so once the pointer
                    // leaves the window (second monitor, OBS) it reports the last in-window position
                    // forever — a stale point the growing stack can reach and "hover" with nobody there.
                    // IsCursorOnScreen gates the freeze to a pointer actually over the window.
                    _presentationPlayer.SetRollStackHovered(rollStackBounds is { Width: > 0 }
                        && Raylib.IsCursorOnScreen()
                        && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rollStackBounds.Value));
                }

                // #275/#327: every banner tier rides one concurrent display track and stacks within its
                // band, so they draw every frame regardless of what holds the active slot.
                if (_presentationPlayer != null)
                {
                    BannerOverlay.Draw(_presentationPlayer.GetBanners(), layout.AreaW, screenH);
                }

                UpdateMissionInfo();

                // #331: victory fireworks. Last of the Raylib-drawn pass, so they sit over the board and
                // banners but under every ImGui window - the game-over card floats on top of them, which is
                // the intended look and costs no layering work.
                UpdateVictoryFireworks(layout.AreaW, screenH);
                _fireworks.Draw();

                // Right-column regions: resolver panel (top half) + log/chat console (bottom half).
                int rightW    = RightColumnWidth(screenW);
                int rightX    = screenW - rightW;
                // Right column, top to bottom: decision panel, the unit info window, Chat / Log.
                // The info window shows the unit taking its turn, otherwise the last unit clicked on the table.
                if (_selectedInfoUnit != null && _selectedInfoUnit is not UnitData { IsQuestHero: true } && !_selectedInfoUnit.Models.Any(m => m.GetIsAlive())) _selectedInfoUnit = null;
                // A unit a tool is working on (the one being deployed) shows here too.
                IUnit? focusUnit = PopupAnchor.FocusUnit;
                PopupAnchor.FocusUnit = null;
                // Whoever starts activating takes the card; from then on, clicking a unit swaps to it.
                // (It used to be the other way round - the activating unit always won, so clicking another
                // unit mid-activation left the card showing someone else.)
                IUnit? activating = _tableState?.Progress.ActivatingUnit;
                if (!Equals(activating?.ID, _lastActingUnit))
                {
                    _lastActingUnit = activating?.ID;
                    _selectedInfoUnit = null;
                }
                IUnit? infoUnit = _gameOverResult == null
                    ? (focusUnit ?? _selectedInfoUnit ?? activating)
                    : null;
                // Right column, top to bottom: Active Unit (always open), Actions (every decision the game
                // asks for), then the tabbed Battle Report / Mission / Game Master window.
                int infoH = _gameOverResult != null ? 0 : (int)(screenH * UnitInfoHeightFraction);
                int actionsH = (int)(screenH * ActionsHeightFraction);
                int tabsY = infoH + actionsH;
                ResolverPanelLayout.Set(rightX, infoH, rightW, actionsH);

                rlImGui.Begin();
                // 3D: the tools draw on the virtual board, which reaches well past the screen - widen the
                // table layer's clip so nothing they draw is trimmed away before it is captured.
                if (show3D)
                    ImGui.GetBackgroundDrawList().PushClipRect(new Vector2(-1e5f, -1e5f), new Vector2(1e5f, 1e5f), false);
                // Reset the Escape arbiter before any consumer runs this frame. Passing the menu's open
                // state mutes every other Escape consumer while the menu owns the key. The army list
                // modal (#329) counts as "menu open" for the same reason: while it covers the board,
                // resolver Esc-cancels and canvas hotkeys must not fire underneath it — the overlay
                // handles its own Escape/L directly, like the Esc menu does.
                EscapeRouter.BeginFrame(_escapeMenu.IsOpen || _armyListOverlay.IsOpen);
                // Runs before the hit tester / resolvers so its Alt-measure WantCaptureMouse override
                // lands before they read that flag (see MeasurementOverlay).
                // F3 toggles the 3D view (when no text box has the keyboard).
                {
                    var kio = ImGui.GetIO();
                    bool keysOk = !kio.WantCaptureKeyboard && !_escapeMenu.IsOpen;
                    // F3 or V: switch 2D / 3D.
                    if (keysOk && !kio.KeyCtrl && (ImGui.IsKeyPressed(ImGuiKey.F3, false) || ImGui.IsKeyPressed(ImGuiKey.V, false)))
                        _want3D = !_want3D;
                    // Ctrl+S: quick save into Saved Games (host only - a client has nothing to save).
                    if (keysOk && kio.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S, false))
                    {
                        string? saved = _escapeMenu.QuickSave();
                        // One reason, said the same way in the log and in the battle report. Before the
                        // first round the engine has nothing to resume from, which is the usual case and
                        // deserves the explanation rather than a bare "can't save right now".
                        bool beforeRound1 = (_tableState?.Progress.RoundCount ?? 0) < 1;
                        string why = saved != null ? $"Game saved: {Path.GetFileNameWithoutExtension(saved)}"
                            : beforeRound1 ? "You can save once round 1 has started."
                            : "Quick save isn't available here.";
                        _log?.Add(why, saved != null ? new TextColor(120, 220, 120, 255) : new TextColor(255, 160, 120, 255));
                        DiceTray.AddNote(why,
                            saved != null ? new Vector4(0.47f, 0.86f, 0.47f, 1f) : new Vector4(1f, 0.63f, 0.47f, 1f));
                    }
                    // O: show or hide the Star Quest mission window. (M is the measuring tool.)
                    if (keysOk && !kio.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.O, false))
                        QuestMissionPanel.Open = !QuestMissionPanel.Open;
                    // F1: the controls list.
                    if (!kio.WantCaptureKeyboard && ImGui.IsKeyPressed(ImGuiKey.F1, false))
                        _showControlsHelp = !_showControlsHelp;
                }
                _measurementOverlay.UpdateLayout(tl.Scale, tl.OriginX, tl.OriginY, TableHIn);
                _measurementOverlay.Draw(screenW, screenH);
                // The 2D/3D, Measure and Take Back buttons live in the bottom-left bar with Menu and Army
                // Lists (DrawMenuButton). The 3D camera controls stay on the board.
                if (!_escapeMenu.IsOpen && !_armyListOverlay.IsOpen && _gameOverResult == null)
                {
                    HandleTakeBackKeys();
                    if (show3D) _view3D?.DrawControls(boardRect);
                }
                _showing3D = show3D;
                // Game Master tools: also claim their table clicks before the hit tester reads them.
                _gameMasterPanel.UpdateLayout(tl.Scale, tl.OriginX, tl.OriginY, TableHIn);
                // The tools' window is the Game Master tab now; this only runs their table clicks.
                _gameMasterPanel.Draw(hidden: _escapeMenu.IsOpen || _armyListOverlay.IsOpen || _gameOverResult != null,
                    tableTools: true, window: false);
                _hitTester.Update(_tableState!, tl.Scale, tl.OriginX, tl.OriginY, TableHIn);
                // Overlay input (F toggle, hover timing, pins) runs after the hit tester so hover state
                // is fresh; heavy rebuilds happen in the next frame's DrawField.
                _tacticalOverlay.UpdateInput(Raylib.GetFrameTime(), _hitTester);
                // Clicking a unit selects it; clicking bare table lets it go again. The hit tester reports
                // no click at all while ImGui owns the mouse, so pressing a button in the activation
                // pop-up is not a click on the table and never drops the selection.
                if (_hitTester.Clicked)
                    _selectedInfoUnit = _hitTester.HoveredUnit;
                // Right column, top to bottom: battle report (fills), wound window (only while wounds are
                // waiting), unit info (pinned to the bottom). The chat/log console is gone.
                // Wounds to assign are a decision too: they take the Actions window.
                GuiAssignWoundsResolver.Dock = new Vector4(rightX, infoH, rightW, actionsH);
                if (infoH > 0) DrawUnitInfoPanel(infoUnit, rightX, 0, rightW, infoH);
                _tooltipOverlay.ShowUnitCard = u => _selectedInfoUnit = u;
                _tooltipOverlay.UpdateLayout(tl.Scale, tl.OriginX, tl.OriginY, TableHIn);
                _tooltipOverlay.Draw(screenW, screenH, _hitTester, _resolverOverlay?.ActiveInteractionHandler,
                    rollStackBounds, rightClickReserved: _resolverOverlay?.ActiveToolUsesRightClick ?? false);
                _resolverOverlay?.UpdateLayout(tl.Scale, tl.OriginX, tl.OriginY, TableHIn);
                // #280: other players' live previews (ghosts / planned paths) - same background
                // draw list layer as the local resolver's own ghosts, drawn regardless of what the
                // local player is doing (they're someone else's state).
                _remotePreviews?.UpdateLayout(tl.Scale, tl.OriginX, tl.OriginY, TableHIn);
                _remotePreviews?.Draw();
                // Hold interactive prompts until the animation queue drains, so the player always
                // sees movement / shots land before being asked to react.
                bool animating = _presentationPlayer?.IsAnimating ?? false;
                bool resolverShown = false;
                UpdatePopupAnchor(layout, show3D, boardRect, screenH);
                // Bottom of the right column: Battle Report / Mission / Game Master tabs.
                DrawBottomTabs(rightX, tabsY, rightW, Math.Max(80, screenH - tabsY));
                if (!_resolverOverlayFaulted && !animating && !_takeBackInProgress)
                {
                    try
                    {
                        _resolverOverlay?.Draw(screenW, screenH);
                        resolverShown = _resolverOverlay?.HasAnyPending ?? false;
                    }
                    catch (Exception ex)
                    {
                        _resolverOverlayFaulted = true;
                        var errColor = new TextColor(255, 120, 120, 255);
                        _log?.Add($"[RESOLVER ERROR] {ex.GetType().Name}: {ex.Message}", errColor);
                        DiceTray.AddNote($"Error: {ex.GetType().Name}: {ex.Message}", new Vector4(1f, 0.47f, 0.47f, 1f));
                        _log?.Add(ex.StackTrace ?? "(no stack trace)", errColor);
                    }
                }
                // Nothing to decide right now: the Actions window says what's going on instead of vanishing.
                if (!resolverShown && !GuiAssignWoundsResolver.AnyPending && _gameOverResult == null)
                    DrawIdleResolverPanel();
                // Instruments draw AFTER the resolver so the movement resolver has published this frame's
                // ghost snapshot -- pips/counts/distance then track the ghost with no one-frame lag. On the
                // background draw list (above tokens, under windows), same layer as the ghosts they annotate.
                _tacticalOverlay.DrawInstruments(screenW, screenH);
                // #280: publish the local resolver's preview - after its Draw for the same
                // freshness reason as the instruments above (it reads the frame's ghost snapshot).
                _previewPublisher?.Tick(Raylib.GetTime());
                // Fill the resolver panel with an idle placeholder whenever nothing is prompting, so the
                // top-right region always reads as an intentional panel rather than empty space.
                // (The top of the right column is the dice tray now - drawn before the resolvers above.)
                DrawGameOverOverlay(screenW, screenH);

                // Standalone "Menu" button in the bottom-left corner (where the retired toolbar sat),
                // the discoverable way into the Esc menu. Drawn before the menu so its dim covers it.
                DrawMenuButton(screenH);

                // #329: the army list overlay, sized to the table area so the right column stays
                // visible beside it. Drawn before the Esc menu so the menu's dim sits above it; its
                // own open/close keys (L, Esc) are handled inside Draw. Suppressed once the
                // game-over card is up so that card stays reachable.
                _armyListOverlay.Draw(layout.AreaW, screenH, _escapeMenu.IsOpen, _gameOverResult != null);

                // In-game menu (Esc). Opens only when no context claimed Escape this frame, and never
                // over the game-over card (that has its own Return to Main Menu button). Drawn last so
                // its dim + window sit above every other in-game overlay.
                bool justOpenedMenu = false;
                if (_gameOverResult == null && EscapeRouter.ShouldOpenMenu())
                {
                    _escapeMenu.Open();
                    justOpenedMenu = true;
                }
                _escapeMenu.Draw(screenW, screenH, justOpenedMenu);
                if (_showControlsHelp) DrawControlsHelp(screenW, screenH);

                if (_takeBackInProgress) DrawTakeBackCurtain(screenW, screenH);

                // 3D: lift everything the tools drew this frame off the screen and onto the 3D table.
                if (show3D) CaptureBoardOverlay();
                else _view3D?.SetOverlay(null);

                rlImGui.End();
            }
            else
            {
                rlImGui.Begin();
                _currentScreen.Draw(screenW, screenH);
                // A networked client can lose the host while still in the lobby (before any game exists);
                // its lobby view-model raises the same game-ended signal, so draw the overlay here too so
                // that pre-launch case isn't silently stuck (QF8). No-op when nothing has ended.
                DrawGameOverOverlay(screenW, screenH);
                rlImGui.End();
            }

            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        if (_boardOverlayReady) { Raylib.UnloadRenderTexture(_boardOverlayRT); _boardOverlayReady = false; }
        if (_exclusionRTReady) Raylib.UnloadRenderTexture(_exclusionRT);
        if (_mottleReady) Raylib.UnloadTexture(_mottleTex);
        _audio?.Dispose();
        Raylib.CloseWindow();
    }

    private Layout ComputeLayout(int screenW, int screenH)
    {
        // The right column reserves width on the right; the table viewport is everything to its left.
        int rightW    = RightColumnWidth(screenW);
        int viewportW = Math.Max(1, screenW - rightW);

        // Fit-to-viewport scale (the #8 zoom-out clamp: the table fills ~100% of the viewport at _zoom==1).
        float scale = FitScale(viewportW, screenH) * _zoom;

        int tablePixW = (int)(TableWIn * scale);
        int tablePixH = (int)(TableHIn * scale);
        int originX   = (int)((viewportW - tablePixW) / 2f + _pan.X);
        int originY   = (int)((screenH   - tablePixH) / 2f + _pan.Y);

        // AreaW is the viewport width so centered overlays (status HUD, dice, banners) sit over the table,
        // not under the right column.
        return new Layout(scale, originX, originY, viewportW, screenH);
    }

    // Alt+wheel to zoom toward the cursor (clamped MinZoom..MaxZoom), middle-drag to pan. Runs at the top
    // of the frame, before ComputeLayout, so this frame renders with the updated transform. Zoom is gated
    // on the mouse being over the table viewport only (NOT WantCaptureMouse -- the Alt-held measurement
    // overlay raises that flag over the table, which would otherwise veto every zoom); pan additionally
    // respects WantCaptureMouse so dragging over the toolbar/panels doesn't scroll the board.
    // #277: Alt is the camera/measure modifier (Alt+wheel zoom, Alt+drag measure) so Ctrl+Wheel belongs
    // entirely to the group-formation cycle -- zoom keeps working while a group ghost is live.
    private void HandleTableViewInput(int screenW, int screenH)
    {
        int rightW    = RightColumnWidth(screenW);
        int viewportW = Math.Max(1, screenW - rightW);
        float fit     = FitScale(viewportW, screenH);

        float mx = Raylib.GetMouseX(), my = Raylib.GetMouseY();
        bool overViewport = mx < viewportW && my < screenH;

        // Alt + wheel: zoom, keeping the world point under the cursor pinned.
        bool alt    = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        float wheel = Raylib.GetMouseWheelMove();
        if (overViewport && alt && wheel != 0f)
        {
            float scaleOld   = fit * _zoom;
            float originXOld = (viewportW - TableWIn * scaleOld) / 2f + _pan.X;
            float originYOld = (screenH   - TableHIn * scaleOld) / 2f + _pan.Y;
            float worldXin   = (mx - originXOld) / scaleOld;   // inches from table origin under the cursor
            float worldYin   = (my - originYOld) / scaleOld;

            float newZoom  = Math.Clamp(_zoom * MathF.Pow(1.1f, wheel), MinZoom, MaxZoom);
            float scaleNew = fit * newZoom;
            _pan.X = mx - (viewportW - TableWIn * scaleNew) / 2f - worldXin * scaleNew;
            _pan.Y = my - (screenH   - TableHIn * scaleNew) / 2f - worldYin * scaleNew;
            _zoom  = newZoom;
        }

        // Middle-drag: pan (skip when an ImGui window owns the mouse).
        bool overUi = ImGui.GetIO().WantCaptureMouse;
        if (overViewport && !overUi && Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            Vector2 d = Raylib.GetMouseDelta();
            _pan.X += d.X;
            _pan.Y += d.Y;
        }

        // Keyboard (2D): WASD pans, Shift+W / S zooms around the middle of the view. Skipped while typing,
        // in a menu, or with Ctrl held (Ctrl+S and friends).
        var kio2 = ImGui.GetIO();
        bool ctrlDown = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (!kio2.WantCaptureKeyboard && !_escapeMenu.IsOpen && !_armyListOverlay.IsOpen && !ctrlDown)
        {
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);
            bool shiftDown = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            float panPx = 700f * dt * ViewSettings.CameraSpeed;
            if (!shiftDown)
            {
                if (Raylib.IsKeyDown(KeyboardKey.W)) _pan.Y += panPx;
                if (Raylib.IsKeyDown(KeyboardKey.S)) _pan.Y -= panPx;
                if (Raylib.IsKeyDown(KeyboardKey.A)) _pan.X += panPx;
                if (Raylib.IsKeyDown(KeyboardKey.D)) _pan.X -= panPx;
            }
            else
            {
                float zoomStep = 0f;
                if (Raylib.IsKeyDown(KeyboardKey.W)) zoomStep += 1f;
                if (Raylib.IsKeyDown(KeyboardKey.S)) zoomStep -= 1f;
                if (zoomStep != 0f)
                {
                    float cx = viewportW / 2f, cy = screenH / 2f;
                    float scaleOld   = fit * _zoom;
                    float originXOld = (viewportW - TableWIn * scaleOld) / 2f + _pan.X;
                    float originYOld = (screenH   - TableHIn * scaleOld) / 2f + _pan.Y;
                    float worldXin   = (cx - originXOld) / scaleOld;
                    float worldYin   = (cy - originYOld) / scaleOld;
                    float newZoom  = Math.Clamp(_zoom * MathF.Pow(2.2f, zoomStep * dt * ViewSettings.CameraSpeed), MinZoom, MaxZoom);
                    float scaleNew = fit * newZoom;
                    _pan.X = cx - (viewportW - TableWIn * scaleNew) / 2f - worldXin * scaleNew;
                    _pan.Y = cy - (screenH   - TableHIn * scaleNew) / 2f - worldYin * scaleNew;
                    _zoom  = newZoom;
                }
            }
        }

        // Bound the pan by the overflow of the scaled table past the viewport, plus (when zoomed in) a 5%
        // margin so a strip of background shows past the edge and it reads as the edge. At min zoom there's
        // no overflow and no margin, so the table stays locked centered.
        float scale = fit * _zoom;
        float overflowX = MathF.Max(0f, (TableWIn * scale - viewportW) / 2f);
        float overflowY = MathF.Max(0f, (TableHIn * scale - screenH)   / 2f);
        float marginX = _zoom > MinZoom ? PanEdgeMarginFraction * TableWIn * scale : 0f;
        float marginY = _zoom > MinZoom ? PanEdgeMarginFraction * TableHIn * scale : 0f;
        _pan.X = Math.Clamp(_pan.X, -(overflowX + marginX), overflowX + marginX);
        _pan.Y = Math.Clamp(_pan.Y, -(overflowY + marginY), overflowY + marginY);
    }

    // ── Decision pop-ups: where the acting unit is on screen ──────────────────────────────
    private void UpdatePopupAnchor(Layout layout, bool show3D, Rectangle boardRect, int screenH)
    {
        PopupAnchor.AreaW = layout.AreaW;
        PopupAnchor.ScreenH = screenH;
        PopupAnchor.Progress = _tableState?.Progress;
        PopupAnchor.HoveredUnit = _hitTester.HoveredUnit;
        PopupAnchor.TableState = _tableState;
        PopupAnchor.CardImageFor ??= CardImage;
        IUnit? unit = _tableState?.Progress.ActivatingUnit;
        PopupAnchor.Unit = unit;
        PopupAnchor.UnitScreenRect = null;
        if (unit == null) return;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (IModel m in unit.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            Vector2? s;
            float r;
            if (show3D)
            {
                s = _view3D?.WorldToScreen(boardRect, p.x, p.z);
                r = 18f;
            }
            else
            {
                s = new Vector2(layout.OriginX + p.x * layout.Scale, layout.OriginY + (TableHIn - p.z) * layout.Scale);
                r = m.BaseRadiusInches * layout.Scale;
            }
            if (s is not Vector2 sp) continue;
            minX = MathF.Min(minX, sp.X - r); maxX = MathF.Max(maxX, sp.X + r);
            minY = MathF.Min(minY, sp.Y - r); maxY = MathF.Max(maxY, sp.Y + r);
        }
        if (minX <= maxX)
            PopupAnchor.UnitScreenRect = new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    // ── F1: controls list ──────────────────────────────────────────────────────────────
    private bool _showControlsHelp;

    private static readonly (string Keys, string What)[] ControlsHelp =
    {
        ("WASD / arrows", "Move the camera"),
        ("Q / E", "Turn the camera (3D)"),
        ("Shift + W / S", "Zoom in / out"),
        ("Shift + Q / E", "Tilt up / down (3D)"),
        ("Mouse wheel, Alt + wheel", "Zoom"),
        ("Middle-drag / right-drag", "Pan (2D) / orbit (3D)"),
        ("V or F3", "Switch 2D / 3D"),
        ("Ctrl + S", "Quick save into Saved Games"),
        ("Ctrl + Z / Ctrl + X", "Undo / redo while placing or moving"),
        ("Z / X", "Take Back / Redo an activation"),
        ("Enter / Space", "Done / confirm"),
        ("Backspace", "Back / undo a step"),
        ("Wheel, R / Shift+R", "Rotate while moving or placing"),
        ("Ctrl + wheel", "Cycle formation (group moves)"),
        ("G", "Group / single model moves"),
        ("Alt + drag, M", "Measure"),
        ("N", "Unit labels"),
        ("O", "Mission window (objectives, weather, marker key)"),
        ("T", "Show all tokens"),
        ("B", "Weapon reach overlay"),
        ("L", "Army lists"),
        ("Right-click a unit", "Unit card"),
        ("Esc", "Menu"),
        ("F1", "This list"),
    };

    private void DrawControlsHelp(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowBgAlpha(0.94f);
        bool open = true;
        if (ImGui.Begin("Controls (F1)", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse |
                                                   ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
        {
            if (ImGui.BeginTable("##controls", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
            {
                foreach (var (keys, what) in ControlsHelp)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(keys);
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(what);
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
        if (!open) _showControlsHelp = false;
    }

    // Border thickness (px) framing the table rect.
    private const float TableBorderThickness = 3f;

    /// <summary>
    /// Each player's faction (the army's list name when the file has no faction), for the per-faction
    /// picture folders - and makes UnitImages/&lt;faction&gt; for each one, in either view.
    /// </summary>
    private Dictionary<PlayerID, string> FactionByPlayer()
    {
        var factions = new Dictionary<PlayerID, string>();
        if (_tableState == null) return factions;
        foreach (ArmyData army in _tableState.Armies.Objects.OfType<ArmyData>())
        {
            string name = !string.IsNullOrWhiteSpace(army.Faction) ? army.Faction : army.ArmyName ?? "";
            if (factions.TryAdd(army.PlayerID, name)) UnitImageLibrary.EnsureFactionFolder(name);
        }
        return factions;
    }

    private void DrawTable(Layout l)
    {
        FactionByPlayer();
        int tw = (int)(TableWIn * l.Scale);
        int th = (int)(TableHIn * l.Scale);
        Raylib.DrawRectangle(l.OriginX, l.OriginY, tw, th, _backgroundStyle.Surface);
        DrawMottleTexture(l, tw, th);
        // A thin, defined frame around the felt (replaces the old edge vignette / "satin" sheen).
        Raylib.DrawRectangleLinesEx(new Rectangle(l.OriginX, l.OriginY, tw, th),
            TableBorderThickness, _backgroundStyle.Border);
    }

    // Tiles the mottle-noise texture across the table at a constant on-screen density (source rect larger
    // than the texture, repeat wrap) and blends it additively so only the lighter flecks show through.
    private void DrawMottleTexture(Layout l, int tw, int th)
    {
        EnsureMottleTexture();
        var src = new Rectangle(0, 0, tw, th);
        var dst = new Rectangle(l.OriginX, l.OriginY, tw, th);
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawTexturePro(_mottleTex, src, dst, Vector2.Zero, 0f, _backgroundStyle.MottleTint);
        Raylib.EndBlendMode();
    }

    private void EnsureMottleTexture()
    {
        if (_mottleReady && _mottleFor == _background) return;
        if (_mottleReady) Raylib.UnloadTexture(_mottleTex);
        // 128px Perlin patch; the style's scale sets the grain (fine and organic for grass, coarse and
        // blotchy for concrete), its offset moves the sample window so no two surfaces share a pattern.
        // Bilinear + repeat so it tiles seamlessly and stays smooth when stretched.
        Image img = Raylib.GenImagePerlinNoise(128, 128,
            _backgroundStyle.MottleOffset, _backgroundStyle.MottleOffset, _backgroundStyle.MottleScale);
        _mottleTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_mottleTex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_mottleTex, TextureWrap.Repeat);
        _mottleReady = true;
        _mottleFor   = _background;
    }

    // Etched inch grid + a soft felt vignette, drawn only within the table rect (so it stays under
    // terrain/objectives/models, which draw afterward). Interior lines only — the border is the edge.
    private void DrawTableGrid(Layout l)
    {
        Color minor = _backgroundStyle.GridMinor;
        Color major = _backgroundStyle.GridMajor;

        int tw = (int)(TableWIn * l.Scale);
        int th = (int)(TableHIn * l.Scale);
        int x0 = l.OriginX, y0 = l.OriginY;
        int x1 = x0 + tw,   y1 = y0 + th;

        for (float xi = GridMinorInches; xi < TableWIn; xi += GridMinorInches)
        {
            int px = x0 + (int)(xi * l.Scale);
            Raylib.DrawLine(px, y0, px, y1, IsMajorGridLine(xi) ? major : minor);
        }
        for (float zi = GridMinorInches; zi < TableHIn; zi += GridMinorInches)
        {
            int py = y0 + (int)(zi * l.Scale);
            Raylib.DrawLine(x0, py, x1, py, IsMajorGridLine(zi) ? major : minor);
        }
    }

    private static bool IsMajorGridLine(float inches)
    {
        float q = inches / GridMajorInches;
        return Math.Abs(q - MathF.Round(q)) < 0.01f;
    }

    private void DrawTerrain(Layout l)
    {
        ITerrain[] snapshot;
        lock (_terrainLock) snapshot = _terrain.ToArray();

        foreach (var terrain in snapshot)
        {
            (Color fill, Color outline) = TerrainColors.For(terrain.TerrainType);
            ZoneRenderer.DrawFilled(terrain.Shape, l.Scale, l.OriginX, l.OriginY, TableHIn, fill, outline);
            // Legibility patterns layered on top (hatch=difficult, chevrons=cover, crimson Xs=dangerous).
            TerrainPatternRenderer.Draw(terrain.Shape, terrain.TerrainType, l.Scale, l.OriginX, l.OriginY, TableHIn);
        }
    }

    private static readonly Color ObjectiveNeutralColor = new(160, 160, 160, 255);
    private const float ObjectiveMarkerRadiusInches = 0.5f;
    private const float ObjectiveSeizureRadiusInches = 3f;

    private static readonly Color QuestSearchColor = new(150, 90, 220, 255);
    private static readonly Color QuestTreasureColor = new(225, 175, 40, 255);

    /// <summary>Star Quest: the search tokens and treasure markers on the table (none in other games).</summary>
    private IReadOnlyList<FDG.QuestToken> QuestTokens() =>
        (IReadOnlyList<FDG.QuestToken>?)_tableState?.Progress.Quest?.Tokens ?? Array.Empty<FDG.QuestToken>();

    private static readonly Color QuestObjectiveColor = new(60, 200, 210, 255);
    private static readonly Color QuestDeliveryColor = new(230, 110, 60, 255);

    /// <summary>Star Quest mission markers: objectives ("!", or "!2/6" while held), delivery ("D") and
    /// extraction ("X") markers. A carried objective is drawn on its carrier.</summary>
    /// <summary>The marker kinds that really cover an area, and so earn the faint 3" ring.</summary>
    private static readonly HashSet<string> AreaMarkerKinds =
        new() { "grease", "smoke", "fire", "spikes", "spores", "biotrap", "noise", "phantasm" };

    /// <summary>
    /// A marker to draw. <paramref name="OnUnit"/> markers (poison, silence) ride a model rather than
    /// sitting on the table, so they are drawn after the models instead of underneath them, and they get
    /// no area ring - a 3" circle around a poisoned unit says "this area is poisoned", which it is not.
    /// </summary>
    private readonly record struct QuestMarkerDraw(Position Position, string Label, Color Color, bool Area, bool OnUnit);

    private List<QuestMarkerDraw> QuestMarkerDraws()
    {
        var list = new List<QuestMarkerDraw>();
        if (_tableState?.Progress.Quest is not { } quest) return list;
        foreach (FDG.QuestMarker m in quest.Markers)
        {
            Position p = FDG.Stages.QuestMission.Where(_tableState, m);
            string label = m.Kind switch
            {
                "delivery" => "D",
                "extraction" => "X",
                "noise" => "N", "grease" => "G", "smoke" => "S", "fire" => "F", "phantasm" => "P",
                "silence" => "Si", "poison" => "Po",
                _ => m.Action == "seize" && m.Activated ? $"{m.Tokens}/{m.TokensNeeded}" : "!",
            };
            bool onUnit = false;
            if (m.Kind is "silence" or "poison" && _tableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == m.OnUnit) is { } on
                && on.Models.FirstOrDefault(x => x.GetIsAlive()) is { } carrier)
            {
                // On the token's upper-right shoulder, just clear of its base, whatever size that base is.
                float off = carrier.BaseRadiusInches + 0.5f;
                p = new Position(carrier.Position.x + off * 0.7f, carrier.Position.z + off * 0.7f);
                onUnit = true;
            }
            Color color = m.Kind switch
            {
                "objective" => QuestObjectiveColor,
                "delivery" or "extraction" => QuestDeliveryColor,
                "fire" => new Color(240, 90, 40, 255),
                "smoke" => new Color(170, 170, 170, 255),
                "grease" => new Color(120, 100, 60, 255),
                "poison" => new Color(110, 200, 70, 255),
                _ => new Color(200, 120, 230, 255),
            };
            list.Add(new QuestMarkerDraw(p, label, color, AreaMarkerKinds.Contains(m.Kind), onUnit));
        }
        return list;
    }

    /// <summary>Star Quest search tokens ("?") and treasure markers ("$", with what they hold).</summary>
    private void DrawQuestTokens(Layout l)
    {
        foreach (QuestMarkerDraw m in QuestMarkerDraws())
            if (!m.OnUnit) DrawQuestMarker(l, m);
        foreach (FDG.QuestToken token in QuestTokens())
        {
            int cx = l.OriginX + (int)(token.X * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - token.Z) * l.Scale);
            float r = 0.45f * l.Scale;
            Color fill = token.IsTreasure ? QuestTreasureColor : QuestSearchColor;
            Raylib.DrawCircle(cx, cy, r, fill);
            Raylib.DrawCircleLines(cx, cy, r, Color.Black);
            string mark = token.IsTreasure ? "$" : "?";
            int fs = Math.Max(8, (int)(r * 1.5f));
            Raylib.DrawText(mark, cx - Raylib.MeasureText(mark, fs) / 2, cy - fs / 2, fs, Color.White);
            if (token.IsTreasure && token.Contents.Count > 0)
            {
                string what = string.Join(" + ", token.Contents);
                int small = Math.Max(9, (int)(r * 0.9f));
                int tw = Raylib.MeasureText(what, small);
                Raylib.DrawRectangle(cx - tw / 2 - 3, (int)(cy + r + 2), tw + 6, small + 4, new Color(0, 0, 0, 170));
                Raylib.DrawText(what, cx - tw / 2, (int)(cy + r + 4), small, QuestTreasureColor);
            }
        }
    }

    /// <summary>Markers that ride a model (poison, silence). Drawn after the models, so they sit on top.</summary>
    private void DrawUnitQuestMarkers(Layout l)
    {
        foreach (QuestMarkerDraw m in QuestMarkerDraws())
            if (m.OnUnit) DrawQuestMarker(l, m);
    }

    private static void DrawQuestMarker(Layout l, QuestMarkerDraw m)
    {
        int mx = l.OriginX + (int)(m.Position.x * l.Scale);
        int my = l.OriginY + (int)((TableHIn - m.Position.z) * l.Scale);
        float mr = (m.OnUnit ? 0.45f : 0.6f) * l.Scale;
        if (m.Area) Raylib.DrawCircleLines(mx, my, 3f * l.Scale, new Color(m.Color.R, m.Color.G, m.Color.B, (byte)120));
        Raylib.DrawCircle(mx, my, mr, m.Color);
        Raylib.DrawCircleLines(mx, my, mr, Color.Black);
        int mfs = Math.Max(8, (int)(mr * (m.Label.Length > 1 ? 0.9f : 1.4f)));
        Raylib.DrawText(m.Label, mx - Raylib.MeasureText(m.Label, mfs) / 2, my - mfs / 2, mfs, Color.White);
    }

    private void DrawObjectives(Layout l)
    {
        IObjective[] snapshot;
        lock (_objectivesLock) snapshot = _objectives.ToArray();

        for (int i = 0; i < snapshot.Length; i++)
        {
            var obj = snapshot[i];
            int cx = l.OriginX + (int)(obj.Position.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - obj.Position.z) * l.Scale);

            Color baseColor = obj.OwnerID.HasValue
                ? _colorForPlayer!(obj.OwnerID.Value)
                : ObjectiveNeutralColor;

            // Translucent 3" seizure zone, clipped to the felt: a marker placed near an edge has part
            // of its radius hanging off the table, and that part is not table, so it isn't drawn.
            float seizurePx = ObjectiveSeizureRadiusInches * l.Scale;
            TableClip.BeginScissor(l.Scale, l.OriginX, l.OriginY, TableHIn);
            Raylib.DrawCircle(cx, cy, seizurePx, new Color(baseColor.R, baseColor.G, baseColor.B, (byte)45));
            Raylib.DrawCircleLines(cx, cy, seizurePx, new Color(baseColor.R, baseColor.G, baseColor.B, (byte)180));
            Raylib.EndScissorMode();

            // Solid inner marker.
            float markerPx = ObjectiveMarkerRadiusInches * l.Scale;
            Raylib.DrawCircle(cx, cy, markerPx, baseColor);
            Raylib.DrawCircleLines(cx, cy, markerPx, Color.Black);

            // Number centered inside the marker. Horde Mode markers keep their fixed 1-5 number, so the
            // label still matches the banners after another objective is destroyed.
            string label = (obj.HordeNumber ?? i + 1).ToString();
            int fontSize = Math.Max(8, (int)(markerPx * 1.5f));
            int textW    = Raylib.MeasureText(label, fontSize);
            Raylib.DrawText(label, cx - textW / 2, cy - fontSize / 2, fontSize, Color.White);
        }
    }

    // Draws the Ambush no-go region: a single blended blob covering everywhere an enemy-distance rule
    // forbids. Each keep-out disc (the flat over-9" rule per enemy model, plus Repel Ambushers' larger
    // per-model discs — #197 P22) is painted OPAQUE into an offscreen texture so overlaps overwrite
    // rather than stack; waiver discs (Ambush Beacon) are then ERASED back out — inside one, every
    // enemy-distance restriction is void, so the hole IS the semantics — and the whole union is
    // composited once at a uniform light alpha. Waiver bubbles get a green outline so the player sees
    // why the hole exists.
    private void DrawAmbushExclusion(Layout l, int screenW, int screenH)
    {
        if (RenderAmbushExclusion(l, screenW, screenH, out var waivers))
            CompositeAmbushExclusion(l, waivers);
    }

    /// <summary>
    /// Ambush (and other "over X\" from enemies") placement: paints the no-go area - the union of every
    /// enemy model's keep-out disc - into its own texture, with a solid red rim along the outside edge so the
    /// border reads clearly, and Ambush Beacon bubbles cut back out. False when no such placement is up.
    /// Must run outside any other texture mode (it uses its own).
    /// </summary>
    private bool RenderAmbushExclusion(Layout l, int w, int h, out IReadOnlyList<PlacementDisc> waivers)
    {
        waivers = Array.Empty<PlacementDisc>();
        IEnemyExclusionProvider? provider = _resolverOverlay?.ActiveEnemyExclusion;
        if (provider == null) return false;
        if (!provider.TryGetEnemyExclusion(out IReadOnlyList<PlacementDisc> keepOut, out waivers)
            || keepOut.Count == 0)
        {
            return false;
        }

        EnsureExclusionTexture(w, h);

        Vector2 ToPx(Position c) => new(
            l.OriginX + c.x * l.Scale,
            l.OriginY + (TableHIn - c.z) * l.Scale);

        Raylib.BeginTextureMode(_exclusionRT);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        // Rim first (the full discs in the rim colour), then the fill over them a few pixels smaller: where
        // discs overlap the fill covers the inner rims, so only the outside edge of the whole area keeps one.
        float rim = MathF.Max(2f, 0.12f * l.Scale);
        foreach (PlacementDisc disc in keepOut)
            Raylib.DrawCircleV(ToPx(disc.Center), disc.RadiusInches * l.Scale, ExclusionRim);
        foreach (PlacementDisc disc in keepOut)
            Raylib.DrawCircleV(ToPx(disc.Center), MathF.Max(0f, disc.RadiusInches * l.Scale - rim), ExclusionFill);

        // Erase the waiver bubbles: custom blend (src x 0 + dst x (1 - srcAlpha)) turns an opaque draw
        // into a pure alpha-cut, so the beacon's bubble reads as legal ground, not a lighter wash.
        Rlgl.SetBlendFactors(0, 0x0303, 0x8006); // GL_ZERO, GL_ONE_MINUS_SRC_ALPHA, GL_FUNC_ADD
        Raylib.BeginBlendMode(BlendMode.Custom);
        foreach (PlacementDisc waiver in waivers)
        {
            Raylib.DrawCircleV(ToPx(waiver.Center), waiver.RadiusInches * l.Scale, Color.White);
        }
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();
        return true;
    }

    /// <summary>Lays the texture <see cref="RenderAmbushExclusion"/> painted onto the current target (the
    /// screen in 2D, the 3D table's board picture in 3D), plus the beacons' green outlines.</summary>
    private void CompositeAmbushExclusion(Layout l, IReadOnlyList<PlacementDisc> waivers)
    {
        Vector2 ToPx(Position c) => new(
            l.OriginX + c.x * l.Scale,
            l.OriginY + (TableHIn - c.z) * l.Scale);

        // Composite the opaque union once. Render-texture contents are y-flipped, hence the negative
        // source height. The tint must be WHITE (it multiplies the texture's colour, so a coloured tint
        // would shift the hue) — only its alpha scales, making the whole blob uniformly translucent.
        var src = new Rectangle(0, 0, _exclusionRT.Texture.Width, -_exclusionRT.Texture.Height);
        Raylib.DrawTextureRec(_exclusionRT.Texture, src, Vector2.Zero,
            new Color((byte)255, (byte)255, (byte)255, ExclusionCompositeAlpha));

        // The waiver outline, on the canvas itself (not the RT) so it stays crisp at full alpha.
        foreach (PlacementDisc waiver in waivers)
        {
            Raylib.DrawCircleLinesV(ToPx(waiver.Center), waiver.RadiusInches * l.Scale, WaiverOutline);
        }
    }

    // Ambush Beacon waiver outline — green, matching "this ground is legal".
    private static readonly Color WaiverOutline = new(90, 220, 120, 200);

    private void EnsureExclusionTexture(int w, int h)
    {
        if (_exclusionRTReady && _exclusionRTW == w && _exclusionRTH == h) return;
        if (_exclusionRTReady) Raylib.UnloadRenderTexture(_exclusionRT);
        _exclusionRT = Raylib.LoadRenderTexture(w, h);
        _exclusionRTW = w;
        _exclusionRTH = h;
        _exclusionRTReady = true;
    }

    // #6 -- a soft pulsing halo under each model of the unit currently taking its activation, so whose
    // turn it is reads at a glance. The activating unit comes from ITableState.Progress (live, replicated
    // state), and the ring rides the presentation animation position so it stays under a gliding model.
    private static readonly (byte r, byte g, byte b) SpotlightRGB = (255, 205, 110); // warm highlight
    private void DrawActiveUnitSpotlight(Layout l)
    {
        if (_tableState == null) return;
        IUnit? active = _tableState.Progress.ActivatingUnit;
        if (active == null) return;

        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3.2f); // 0..1
        var (r, g, b) = SpotlightRGB;
        var fill  = new Color(r, g, b, (byte)(28 + 22 * pulse));
        var ring  = new Color(r, g, b, (byte)(130 + 90 * pulse));
        var halo  = new Color(r, g, b, (byte)(40 + 30 * pulse));

        foreach (IModel model in active.Models)
        {
            // Same position source as DrawModels so the halo tracks gliding/hurt/dying state.
            ModelDrawState draw = _presentationPlayer?.GetModelDrawState(model)
                ?? (model.GetIsAlive()
                    ? new ModelDrawState(true, model.Position, 1f, null)
                    : ModelDrawState.Hidden);
            if (!draw.Visible) continue;

            int cx = l.OriginX + (int)(draw.Position.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - draw.Position.z) * l.Scale);
            // #341: and the same facing source, so a halo can't stay square to the table while the base it
            // rings turns underneath it.
            Float2 facing = draw.Facing ?? model.Facing;

            // #250: follow the model's true base shape, like DrawModels does — a rectangle-based model
            // used to get a circular halo that contradicted the base drawn under it. Inflations are in
            // inches so they track zoom; the pulse ring's extra pixels convert back through the scale.
            const float HaloInflateIn = 0.18f; // just outside the base
            float pulseInflateIn = HaloInflateIn + (3f + 6f * pulse) / l.Scale;

            ModelBaseRenderer.DrawFilledRaylib(model.BaseShape, cx, cy, l.Scale, fill, ring, facing, HaloInflateIn);
            ModelBaseRenderer.DrawOutlineRaylib(model.BaseShape, cx, cy, l.Scale, halo,
                thickness: 1f, inflateInches: pulseInflateIn, facing: facing);
        }
    }

    // 2D token portraits: UnitImages/<faction>/Portraits/<unit>.png (or UnitImages/Portraits for every faction).
    private readonly UnitImageLibrary _portraits = new(UnitImageLibrary.PortraitFolderName);
    private readonly UnitImageLibrary _cardImages = new();
    private readonly UnitImageLibrary _deployImages = new(UnitImageLibrary.DeploymentFolderName);

    /// <summary>A unit's picture for the deploy cards: UnitImages/&lt;faction&gt;/Deployment, else its portrait,
    /// else its full standee picture.</summary>
    private Texture2D? CardImage(IUnit unit)
    {
        string faction = FactionByPlayer().TryGetValue(unit.PlayerID, out string? f) ? f : "";
        return _deployImages.Get(unit.Name, faction) ?? _portraits.Get(unit.Name, faction) ?? _cardImages.Get(unit.Name, faction);
    }

    /// <summary>
    /// A unit portrait inside a 2D token: cropped to a circle (centred side to side, from the top for tall
    /// pictures, where the face usually is), leaving a ring of the owner's colour around it.
    /// </summary>
    private static void DrawPortraitDisc(Texture2D tex, int cx, int cy, float radius, byte alpha)
    {
        if (radius < 2f) return;
        float w = tex.Width, h = tex.Height;
        float side = MathF.Min(w, h);
        float u0 = (w - side) / 2f / w, v0 = h > w ? 0f : (h - side) / 2f / h;
        float du = side / w, dv = side / h;
        int segments = Math.Clamp((int)(radius * 1.5f), 16, 64);

        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.SetTexture(tex.Id);
        Rlgl.Color4ub(255, 255, 255, alpha);
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.Tau * i / segments, a1 = MathF.Tau * (i + 1) / segments;
            (float X, float Y) p0 = (MathF.Cos(a0), MathF.Sin(a0)), p1 = (MathF.Cos(a1), MathF.Sin(a1));
            if (Rlgl.CheckRenderBatchLimit(3)) { Rlgl.Begin(DrawMode.Triangles); Rlgl.SetTexture(tex.Id); Rlgl.Color4ub(255, 255, 255, alpha); }
            Rlgl.TexCoord2f(u0 + du * 0.5f, v0 + dv * 0.5f);
            Rlgl.Vertex2f(cx, cy);
            Rlgl.TexCoord2f(u0 + du * (0.5f + 0.5f * p1.X), v0 + dv * (0.5f + 0.5f * p1.Y));
            Rlgl.Vertex2f(cx + radius * p1.X, cy + radius * p1.Y);
            Rlgl.TexCoord2f(u0 + du * (0.5f + 0.5f * p0.X), v0 + dv * (0.5f + 0.5f * p0.Y));
            Rlgl.Vertex2f(cx + radius * p0.X, cy + radius * p0.Y);
        }
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    private void DrawModels(Layout l)
    {
        Dictionary<PlayerID, string> factionByPlayer = FactionByPlayer();
        // #309: resolve each model's owning unit from LIVE table state, not the instance captured at
        // registration. A networked client's store REPLACES UnitData instances on every replicated
        // update (token changes ride a whole-object rebroadcast), so a captured instance keeps stale
        // token state forever - an ambush arrival registered between its position sync and its
        // reserve-clear sync read as still InReserve and its bases never drew, while the (live-read)
        // label overlay did. The captured tuple stays as color source (PlayerID is immutable) and as
        // fallback for the one-frame window where a unit isn't enumerable.
        var liveUnitByModel = new Dictionary<ModelID, IUnit>();
        if (_tableState != null)
        {
            foreach (IUnit u in _tableState.Units.Objects)
                foreach (IModel m in u.Models)
                    liveUnitByModel[m.ID] = u;
        }

        foreach (var (model, (color, cachedUnit)) in _placedModels)
        {
            IUnit unit = liveUnitByModel.TryGetValue(model.ID, out IUnit? live) ? live : cachedUnit;

            // Not in play: held in Ambush reserve, riding a transport, or an Aircraft that flew off the
            // edge. Its models are parked at the origin, which is the table's bottom-left corner.
            if (!unit.GetIsOnBattlefield() && !IsDownedHeroOnTable(unit)) continue;

            // The presentation player decides position/visibility/effects: gliding mid-move,
            // tinted while dying (red, fading) or hurt (orange), hidden once dead, else authoritative.
            ModelDrawState draw = _presentationPlayer?.GetModelDrawState(model) ?? RestingDrawState(unit, model);

            if (!draw.Visible) continue;

            int cx = l.OriginX + (int)(draw.Position.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - draw.Position.z) * l.Scale);
            // #341: the presentation player owns the facing mid-animation for the same reason it owns the
            // position - the authoritative one is already at the END of the move while the glide is playing,
            // so drawing model.Facing here would snap the turn on before the model set off.
            Float2 facing = draw.Facing ?? model.Facing;

            Color baseColor = draw.Tint is { } tint ? new Color(tint.R, tint.G, tint.B, (byte)255) : color;
            byte a = (byte)Math.Clamp(draw.Alpha * 255f, 0f, 255f);
            Color fill    = new(baseColor.R, baseColor.G, baseColor.B, a);
            Color outline = new((byte)0, (byte)0, (byte)0, a);

            ModelBaseRenderer.DrawFilledRaylib(model.BaseShape, cx, cy, l.Scale, fill, outline, facing);

            // The unit's portrait (UnitImages/<faction>/Portraits) inside the base, ringed by the owner's colour.
            bool joinedHeroModel = HeroMarkerRenderer.IsHeroModel(unit, model);
            string portraitName = joinedHeroModel ? ((unit as UnitData)?.HeroAttachment?.Name ?? unit.Name) : unit.Name;
            if (_portraits.Get(portraitName, factionByPlayer.TryGetValue(unit.PlayerID, out string? fac) ? fac : "")
                is Texture2D portrait)
            {
                float r = model.BaseRadiusInches * l.Scale;
                DrawPortraitDisc(portrait, cx, cy, r - MathF.Max(2f, r * 0.16f), a);
            }

            ModelBaseRenderer.DrawHeadingRaylib(model.BaseShape, cx, cy, l.Scale, facing,
                new Color((byte)255, (byte)255, (byte)255, a));

            // Joined-Hero marker (#227): a white, dark-outlined star centred on the hero model's base. Drawn
            // last so it sits atop the fill; overlays the base rather than ringing it, so it never clashes
            // with the selection / hover halos or range rings.
            if (HeroMarkerRenderer.IsHeroModel(unit, model))
                HeroMarkerRenderer.DrawStarRaylib(cx, cy, model.BaseRadiusInches * l.Scale, a);
        }
    }

    // Top-center status strip: current round (from ITableState.Progress) + a live objective scoreboard
    // (one player-colored pip + controlled count per player). Both read as live state each frame -- the
    // round comes from the replicated GameProgressData, the counts from the objectives' owners.
    // Where this frame's status strip starts (null when it shows no round or score), so the measuring
    // tool's button can dock to its left. Written in the Raylib pass, read in the ImGui pass of the same frame.
    private int? _statusStripLeft;

    private void UpdateMissionInfo()
    {
        _missionScores.Clear(); _missionLines.Clear(); _missionActivations.Clear(); _missionWaiting.Clear();
        _missionTurn = null; _missionRound = null;
        if (_tableState == null || _colorForPlayer == null) return;

        // The aggregate progress read model does the work (round, per-player objective counts); the
        // renderer just maps each player to its table color. RoundCount is null before the main phase.
        IGameProgress progress = _tableState.Progress;

        var scores = new List<(Color color, int count)>();
        foreach (PlayerObjectiveScore s in progress.Scores)
            scores.Add((_colorForPlayer(s.PlayerID), s.ObjectiveCount));

        // #322: whose decision the game is waiting on (non-local players only - the resolver panel
        // already covers the local player's own pending tasks).
        var waiting = new List<(Color color, string playerName, string taskName)>();
        if (_taskDisplay != null)
            foreach (var task in _taskDisplay.GetWaitingOnOthers())
                waiting.Add((_colorForPlayer(task.PlayerInfo.PlayerID), task.PlayerInfo.Name, task.TaskName));

        // Horde Mode: objective counts mean nothing there, so the strip shows the wave and the defenders'
        // points instead of the per-player pips.
        List<(string text, Color color)>? segments = null;
        if (progress.GameMode == EGameMode.Horde && progress.RoundCount is int hordeRound)
        {
            int wave = FDG.StateMachine.HordeRules.WaveOfRound(hordeRound);
            int waves = FDG.StateMachine.HordeRules.WaveOfRound(progress.TotalRounds);
            segments = new List<(string text, Color color)>
            {
                ($"WAVE {wave} / {waves}", new Color(255, 140, 90, 255)),
                ($"{progress.HordePointsAvailable ?? 0} PTS", new Color(255, 215, 0, 255)),
            };
        }
        else if (progress.GameMode == EGameMode.StarQuestHorde && progress.Quest is { } quest)
        {
            segments = new List<(string text, Color color)>
            {
                ($"WAVE {quest.Wave} / {FDG.StateMachine.StarQuestHordeRules.FinalWave}", new Color(255, 140, 90, 255)),
                ($"ALERTNESS {quest.Alertness} / {FDG.StateMachine.StarQuestHordeRules.AlertnessToEndWave}", new Color(255, 190, 90, 255)),
                ($"{quest.PartyXp} XP", new Color(255, 215, 0, 255)),
            };
            AddQuestConditions(segments, quest);
        }
        else if (progress.GameMode == EGameMode.StarQuestMission && progress.Quest is { } mission && mission.Difficulty > 0)
        {
            string Objective(bool sec)
            {
                int n = sec ? mission.Secondary : mission.Primary;
                string name = sec ? FDG.StateMachine.StarQuestMissionRules.SecondaryNames[n] : FDG.StateMachine.StarQuestMissionRules.PrimaryNames[n];
                bool done = sec ? mission.SecondaryDone : mission.PrimaryDone;
                var held = mission.Markers.FirstOrDefault(m => m.ForSecondary == sec && m.Action == "seize");
                string extra = done ? " DONE" : held != null && held.Activated ? $" {held.Tokens}/{held.TokensNeeded}" : "";
                return (sec ? "SECONDARY " : "PRIMARY ") + name.ToUpperInvariant() + extra;
            }
            segments = new List<(string text, Color color)>
            {
                (FDG.StateMachine.StarQuestMissionRules.DifficultyNames[mission.Difficulty].ToUpperInvariant(), new Color(200, 170, 255, 255)),
                (Objective(false), mission.PrimaryDone ? new Color(120, 230, 140, 255) : new Color(255, 215, 0, 255)),
            };
            if (mission.Secondary > 0)
                segments.Add((Objective(true), mission.SecondaryDone ? new Color(120, 230, 140, 255) : new Color(255, 215, 0, 255)));
            segments.Add(($"ALERTNESS {mission.Alertness} / 6", new Color(255, 190, 90, 255)));
            if (mission.PrimaryDone)
                segments.Add(("EXIT: ALL HEROES TO ONE CORNER", new Color(130, 200, 255, 255)));
            AddQuestConditions(segments, mission);
        }

        // Second line: units each player has left to activate this round, and whose unit is acting.
        List<(string text, Color color)>? subline = null;
        if (progress.RoundCount != null)
        {
            subline = new List<(string text, Color color)>();
            var leftByPlayer = progress.UnactivatedUnits.GroupBy(u => u.PlayerID).ToDictionary(g => g.Key, g => g.Count());
            var dim = new Color(190, 196, 205, 235);
            bool first = true;
            foreach (IPlayerSlotInfo slot in _tableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID))
            {
                if (!first) subline.Add(("    ", dim));
                first = false;
                subline.Add((slot.Name, _colorForPlayer(slot.PlayerID)));
                int left = leftByPlayer.TryGetValue(slot.PlayerID, out int n) ? n : 0;
                subline.Add(($": {left} left", dim));
            }
            if (progress.ActivatingUnit is IUnit acting)
            {
                string owner = _tableState.Players.Objects.FirstOrDefault(p => p.PlayerID == acting.PlayerID)?.Name ?? "";
                subline.Add(("    -    ", dim));
                subline.Add(($"{owner}'s turn", _colorForPlayer(acting.PlayerID)));
            }
        }

        _missionRound = progress.RoundCount;
        _missionTotalRounds = progress.TotalRounds;
        foreach (PlayerObjectiveScore sc in progress.Scores)
        {
            string name = _tableState.Players.Objects.FirstOrDefault(pl => pl.PlayerID == sc.PlayerID)?.Name ?? "";
            _missionScores.Add((_colorForPlayer(sc.PlayerID), name, sc.ObjectiveCount));
        }
        if (segments != null) _missionLines.AddRange(segments);
        _missionWaiting.AddRange(waiting);
        if (progress.RoundCount != null)
        {
            var leftByPlayer = progress.UnactivatedUnits.GroupBy(u => u.PlayerID).ToDictionary(g => g.Key, g => g.Count());
            foreach (IPlayerSlotInfo slot in _tableState.Players.Objects.Where(pl => pl.IsFilled).OrderBy(pl => pl.SlotID))
                _missionActivations.Add((_colorForPlayer(slot.PlayerID), slot.Name,
                    leftByPlayer.TryGetValue(slot.PlayerID, out int n) ? n : 0));
            if (progress.ActivatingUnit is IUnit actingUnit)
            {
                string owner = _tableState.Players.Objects.FirstOrDefault(pl => pl.PlayerID == actingUnit.PlayerID)?.Name ?? "";
                _missionTurn = $"{owner}'s turn";
                _missionTurnColor = _colorForPlayer(actingUnit.PlayerID);
            }
        }
    }

    // #331: start the fireworks the frame the game-over card first appears, then keep them running while
    // it is up. The winning side is derived here rather than read off the result, because the structured
    // GameResult is host-side only and never crosses the wire - TeamScoreTally over the REPLICATED
    // objectives and slots is the same computation VictoryCalculationStage used, so a client celebrates
    // the same winner the host does. A tie hands back every tied team, and a game nobody won hands back
    // nothing, which stands the effect down.
    private void UpdateVictoryFireworks(int areaW, int screenH)
    {
        bool wanted = _gameOverResult != null && ViewSettings.ShowVictoryFireworks
            && _tableState != null && _colorForPlayer != null;

        if (!wanted)
        {
            // Covers the toggle going off mid-celebration and the pre-launch disconnect card, which shows
            // with no table state behind it.
            if (_fireworks.IsActive) _fireworks.Stop();
            _fireworksStarted = false;
            return;
        }

        if (!_fireworksStarted)
        {
            _fireworksStarted = true;
            _fireworks.Restart(VictoryFireworks.ColorsForWinners(
                TeamScoreTally.TopTeams(TeamScoreTally.Build(_tableState!)), _colorForPlayer!));
        }

        _fireworks.Update(Raylib.GetFrameTime(), areaW, screenH);
    }

    // A short-lived dust puff where a model died: an expanding, fading gray cloud plus a few debris
    // specks flung outward, riding the death animation's progress. Drawn over the (red, fading) model.
    private static readonly (byte r, byte g, byte b) DustRGB = (150, 140, 128);
    private void DrawDeathBursts(Layout l)
    {
        if (_presentationPlayer == null) return;
        foreach (var (pos, progress) in _presentationPlayer.GetActiveDeathBursts())
        {
            int cx = l.OriginX + (int)(pos.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - pos.z) * l.Scale);
            var (r, g, b) = DustRGB;

            // Central cloud: grows from the base size outward, fades as it expands.
            float radius = (0.18f + 0.55f * progress) * l.Scale;
            byte cloudA  = (byte)Math.Clamp((1f - progress) * 130f, 0f, 255f);
            Raylib.DrawCircleV(new Vector2(cx, cy), radius, new Color(r, g, b, cloudA));

            // Debris specks flung outward on fixed spokes.
            byte specA = (byte)Math.Clamp((1f - progress) * 200f, 0f, 255f);
            var spec = new Color(r, g, b, specA);
            float reach = (0.15f + 0.85f * progress) * l.Scale;
            for (int k = 0; k < 6; k++)
            {
                float ang = k * (MathF.PI * 2f / 6f) + 0.3f;
                float px = cx + MathF.Cos(ang) * reach;
                float py = cy + MathF.Sin(ang) * reach;
                Raylib.DrawCircleV(new Vector2(px, py), MathF.Max(1.2f, 0.05f * l.Scale), spec);
            }
        }
    }

    // Small always-visible "Menu" button pinned to the bottom-left corner (pivot 0,1), the same spot the
    // old view toolbar occupied. Opens the in-game menu; Esc does the same. Auto-sized, faintly backed.
    private void DrawMenuButton(int screenH)
    {
        ImGui.SetNextWindowPos(new Vector2(8, screenH - 8), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowBgAlpha(0.70f);
        ImGui.Begin("##menubtn",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        if (ImGui.Button("Menu (Esc)"))
            _escapeMenu.Open();
        // #329: the discoverable way into the army list, beside the menu (the hotkey is L).
        ImGui.SameLine();
        if (ImGui.Button("Army Lists (L)"))
            _armyListOverlay.Toggle();
        if (_gameOverResult == null && !_armyListOverlay.IsOpen)
        {
            // 2D / 3D switch.
            ImGui.SameLine();
            string viewLabel = !_want3D ? "3D View (V)" : _showing3D ? "2D View (V)" : "3D View (game over)";
            bool on3D = _want3D;
            if (on3D)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.ButtonCommit);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.ButtonCommitHover);
            }
            if (ImGui.Button(viewLabel)) _want3D = !_want3D;
            if (on3D) ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(_want3D ? "Back to the 2D view (F3 or V)." : "Switch to the 3D view (F3 or V).");

            ImGui.SameLine();
            _measurementOverlay.DrawInlineToggle();

            // Take Back / Redo, when the game offers them.
            if (!_takeBackInProgress && TakeBackStatusProvider?.Invoke() is { } status)
            {
                ImGui.SameLine();
                ImGui.BeginDisabled(!status.CanTakeBack);
                if (ImGui.Button("Take Back")) _pendingTakeBack = false;
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(status.TakeBackTip);
                ImGui.SameLine();
                ImGui.BeginDisabled(!status.CanRedo);
                if (ImGui.Button("Redo")) _pendingTakeBack = true;
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(status.RedoTip);
            }
        }
        ImGui.End();
    }

    private bool _showing3D;

    /// <summary>Ctrl+Z / Ctrl+X take back / redo a whole activation (the buttons are in the bottom-left bar).</summary>
    private void HandleTakeBackKeys()
    {
        if (_takeBackInProgress || TakeBackStatusProvider?.Invoke() is not { } status) return;
        var io = ImGui.GetIO();
        if (io.KeyCtrl && !io.WantCaptureKeyboard && !(_resolverOverlay?.ActiveToolUsesUndoKeys ?? false))
        {
            if (status.CanTakeBack && ImGui.IsKeyPressed(ImGuiKey.Z, false)) _pendingTakeBack = false;
            else if (status.CanRedo && ImGui.IsKeyPressed(ImGuiKey.X, false)) _pendingTakeBack = true;
        }
    }

    // ── Bottom tabs: Battle Report / Mission / Game Master ──────────────────────────────────────────────
    private static Vector4 ToVec4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    private void DrawBottomTabs(int x, int y, int w, int h)
    {
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.07f, 0.08f, 0.97f));
        ImGui.Begin("##bottomtabs", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        if (ImGui.BeginTabBar("##bottomtabbar"))
        {
            if (ImGui.BeginTabItem("Battle Report"))
            {
                ImGui.BeginChild("##reportbody");
                DiceTray.DrawBody();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Mission"))
            {
                ImGui.BeginChild("##missionbody");
                DrawMissionTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (GameMasterPanel.Available && ImGui.BeginTabItem("Game Master"))
            {
                ImGui.BeginChild("##gmbody");
                _gameMasterPanel.DrawBody();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    // What used to be the top status strip, gathered each frame for the Mission tab.
    private int? _missionRound;
    private int _missionTotalRounds;
    private readonly List<(Color color, string name, int count)> _missionScores = new();
    private readonly List<(string text, Color color)> _missionLines = new();
    private readonly List<(Color color, string name, int left)> _missionActivations = new();
    private readonly List<(Color color, string player, string task)> _missionWaiting = new();
    private string? _missionTurn;
    private Color _missionTurnColor;

    private void DrawMissionTab()
    {
        ImGui.PushTextWrapPos(0f);
        if (_missionRound is int round)
            UiText.Colored(new Vector4(0.55f, 0.75f, 1f, 1f), $"ROUND {round} / {_missionTotalRounds}");
        else
            ImGui.TextDisabled("The game hasn't started its first round yet.");

        if (_missionLines.Count > 0)
        {
            ImGui.Spacing();
            foreach (var (text, color) in _missionLines) UiText.Colored(ToVec4(color), text);
        }
        else if (_missionScores.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Objectives held:");
            foreach (var (color, name, count) in _missionScores)
            {
                ImGui.TextColored(ToVec4(color), "  " + name);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{count}");
            }
        }

        if (_missionActivations.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Activations left this round:");
            foreach (var (color, name, left) in _missionActivations)
            {
                ImGui.TextColored(ToVec4(color), "  " + name);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{left}");
            }
            if (_missionTurn != null) ImGui.TextColored(ToVec4(_missionTurnColor), _missionTurn);
        }
        foreach (var (color, player, task) in _missionWaiting)
        {
            ImGui.TextColored(ToVec4(color), player);
            ImGui.SameLine();
            ImGui.TextDisabled($"is deciding: {task}");
        }
        ImGui.PopTextWrapPos();

        if (_tableState?.Progress.Quest is { } quest)
        {
            ImGui.Spacing();
            ImGui.Separator();
            QuestMissionPanel.DrawBody(quest);
        }
    }

    // Centered "Game Over" card shown once the game ends. The board stays visible behind it; the player
    // must click through to leave (no auto-return), at which point we tear down and return to the menu.
    // #235: draggable (position forced only when it appears) so the final board can be inspected.
    private void DrawGameOverOverlay(int screenW, int screenH)
    {
        string? result = _gameOverResult;
        if (result == null) return;

        // A recovery note (#187) is a wrapped absolute path, so the card grows to hold it.
        string? note = _gameOverNote;
        var size = new Vector2(Math.Min(460f, screenW * 0.8f), note == null ? 200f : 290f);
        ImGui.SetNextWindowPos(new Vector2((screenW - size.X) / 2f, (screenH - size.Y) / 2f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin("Game Over##overlay",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);

        ImGui.PushFont(LargeFont);
        CenteredText(size.X, "Game Over");
        ImGui.PopFont();

        ImGui.Spacing();
        // Center the result line when it fits; long results fall back to left-aligned wrapping.
        if (ImGui.CalcTextSize(result).X <= ImGui.GetContentRegionAvail().X)
            CenteredText(size.X, result);
        else
            ImGui.TextWrapped(result);
        ImGui.Spacing();
        ImGui.Spacing();

        if (note != null)
        {
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled(note);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        float btnH = 44f;
        // Pin the button to the bottom of the card.
        ImGui.SetCursorPosY(size.Y - btnH - ImGui.GetStyle().WindowPadding.Y);
        if (ImGui.Button("Return to Main Menu", new Vector2(ImGui.GetContentRegionAvail().X, btnH)))
        {
            ExitGame();
            NavigateTo(MainMenu);
        }

        ImGui.End();
    }

    // One line of text horizontally centered in a window of the given width (uses the current font).
    private static void CenteredText(float windowW, string text)
    {
        ImGui.SetCursorPosX(MathF.Max(0f, (windowW - ImGui.CalcTextSize(text).X) / 2f));
        ImGui.TextUnformatted(text);
    }

    // Log/chat console: fills the bottom half of the in-game right column. Log and Chat are independent
    // TOGGLES (Chat on the left); with both on, their lines merge into one column in arrival order (by the
    // shared LogEntry.Sequence). The engine GameLog is the Log source; the sender-coloured chat store is
    // the Chat source, which also shows the Global/Team channel toggle + input. Chat flags unread when a
    // message arrives while Chat is toggled off.
    // ── Unit info window (right column, between the decision panel and Chat / Log) ─────────────
    private const float DecisionPanelHeightFraction = 0.42f;
    private const float UnitInfoHeightFraction = 0.30f;
    private const float ActionsHeightFraction = 0.30f;
    private IUnit? _selectedInfoUnit;

    /// <summary>The unit that was activating last frame, so a new activation can take over the card.</summary>
    private UnitID? _lastActingUnit;

    /// <summary>The info window: the acting (or last clicked) unit's status, stats, weapons, special rules
    /// with what they do, and tokens. The header button folds it to one line. Stays on screen with nothing
    /// selected so the right column keeps its shape.</summary>
    private void DrawUnitInfoPanel(IUnit? unit, int x, int y, int w, int h)
    {
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Always);
        ImGui.Begin("##unitinfo",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus);

        bool acting = unit != null && _tableState?.Progress.ActivatingUnit?.ID.Equals(unit.ID) == true;
        ResolverPanelLayout.PanelHeading(unit == null || acting ? "ACTIVE UNIT" : "SELECTED UNIT");
        if (unit == null)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled("Click a unit on the table (or right-click any unit) to see its card here.");
            ImGui.PopTextWrapPos();
            ImGui.End();
            return;
        }
        {
            // Status first: activated / Shaken / in reserve.
            if (_tableState != null && UnitActivation.HasActivated(_tableState.Progress, unit))
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.40f, 1f), "Activated this round");
            if (unit.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken))
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Shaken");
            if (FDG.Stages.FirefightRules.MarkerSummary(unit) is { } woundMarkers)
                UiText.Colored(new Vector4(1f, 0.65f, 0.3f, 1f), woundMarkers);
            if (UnitActivation.IsInReserve(unit))
                ImGui.TextDisabled("In reserve (off the table)");

            float wrap = Math.Max(120f, ImGui.GetContentRegionAvail().X - 20f);
            if (unit is UnitData { IsQuestHero: true } questHero)
                DrawQuestHeroStats(questHero, _tableState?.Progress.Quest);
            UnitStatBlockRenderer.Draw(unit, includeRuleDescriptions: true, descriptionWrapWidth: wrap,
                quest: _tableState?.Progress.Quest);

            var tokens = TableTooltipOverlay.VisibleTokenNames(unit);
            if (tokens.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted("Tokens:");
                foreach (string t in tokens) ImGui.TextUnformatted($"- {t}");
            }
        }
        ImGui.End();
    }

    /// <summary>Star Quest: the hero's power, Str/Dex/Wil, level and skills, above its stat block.</summary>
    /// <summary>Star Quest optional challenges in the status strip: time of day, weather, the random event.</summary>
    private static void AddQuestConditions(List<(string text, Color color)> segments, FDG.QuestProgress quest)
    {
        if (quest.TimeOfDay.Length > 0) segments.Add((quest.TimeOfDay.ToUpperInvariant(), new Color(170, 180, 255, 255)));
        if (quest.Weather.Length > 0) segments.Add((quest.Weather.ToUpperInvariant() + " WEATHER", new Color(150, 210, 230, 255)));
        if (quest.Event.Length > 0) segments.Add(("EVENT " + quest.Event.ToUpperInvariant(), new Color(220, 160, 255, 255)));
        if (quest.BossFight == 1) segments.Add(("BOSS FIGHT", new Color(255, 100, 80, 255)));
    }

    private static void DrawQuestHeroStats(UnitData hero, FDG.QuestProgress? quest)
    {
        FDG.Quest.QuestHero? sheet = FDG.Quest.QuestHero.FromJson(hero.QuestHeroJson);
        if (sheet == null) return;
        if (!hero.GetIsAlive())
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1f), "UNCONSCIOUS - a hero within 3 inches can revive it (Skill, 2 power).");
        ImGui.TextColored(new Vector4(1f, 0.86f, 0.5f, 1f), $"Level {sheet.Level} {sheet.Class}   XP {sheet.Xp}");
        ImGui.TextColored(new Vector4(0.45f, 0.75f, 1f, 1f), $"Power {hero.QuestPower}/{sheet.Power}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"   Str {sheet.Strength}+   Dex {sheet.Dexterity}+   Wil {sheet.Willpower}+"
            + (hero.QuestStatBonus != 0 ? $"   (next stat roll +{hero.QuestStatBonus})" : ""));
        var skills = new List<string>();
        if (sheet.ClassFeat.Length > 0) skills.Add(sheet.ClassFeat + (hero.QuestFeatsUsed.Contains(sheet.ClassFeat) ? " (used)" : ""));
        skills.AddRange(sheet.Skills);
        ImGui.PushTextWrapPos(0f);
        if (skills.Count > 0)
            ImGui.TextDisabled("Skills: " + string.Join(", ", skills));
        string pack = sheet.Items.Count == 0 ? "nothing" : string.Join(", ", sheet.Items.GroupBy(i => i)
            .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
        ImGui.TextColored(new Vector4(1f, 0.82f, 0.3f, 1f), $"{sheet.Credits}c");
        ImGui.SameLine();
        ImGui.TextUnformatted("  Items: " + pack);
        if (sheet.Conditions.Count > 0)
            UiText.Colored(new Vector4(1f, 0.5f, 0.4f, 1f), "Conditions: " + string.Join(", ", sheet.Conditions));
        // Skills in play this round (Reckless, Dread Attack ready, +6" next spell...).
        List<string> active = FDG.Stages.QuestFlags.Describe(quest, hero);
        if (active.Count > 0)
            UiText.Colored(new Vector4(0.6f, 0.9f, 0.6f, 1f), "This round: " + string.Join(", ", active));
        ImGui.PopTextWrapPos();
        ImGui.Separator();
    }

    private void DrawConsole(int x, int y, int w, int h)
    {
        if (_log == null) return;

        // Unread bookkeeping (every frame, regardless of what's shown).
        int chatCount = _playerMessageUI?.ChatLog.Count ?? 0;
        if (chatCount > _lastChatCount && !_showChat)
            _chatUnread = true;
        _lastChatCount = chatCount;

        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Always);
        ImGui.Begin("##console",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus);

        // Source toggles: Chat (left), then Log, then Debug (developer detail, off by default).
        DrawConsoleToggle((_chatUnread ? "Chat *" : "Chat") + "##chattoggle", ref _showChat, isChat: true);
        ImGui.SameLine();
        DrawConsoleToggle("Log##logtoggle", ref _showLog, isChat: false);
        ImGui.SameLine();
        bool debugWasOn = _showDebug;
        DrawConsoleToggle("Debug##debugtoggle", ref _showDebug, isChat: false);
        if (_showDebug != debugWasOn)
        {
            // #163 — the Debug view doubles as the rule-trace switch: evaluations run host-side, so on
            // the host this starts/stops trace generation (a client's toggle only controls its local
            // view of relayed lines). Assign only on change so a --trace-rules launch isn't clobbered
            // while the view happens to be closed.
            FDG.Rules.Dispatch.RuleTrace.Enabled = _showDebug;
        }

        ImGui.Separator();
        DrawConsoleContent();

        ImGui.End();
    }

    // Behind the wound window: keeps its slot filled while the last dice animations finish playing.
    private static void DrawWoundWaitPanel(int x, int y, int w, int h)
    {
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Always);
        ImGui.Begin("##woundwait", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoSavedSettings);
        ImGui.TextDisabled("Wounds incoming...");
        ImGui.End();
    }

    // Idle placeholder shown in the resolver panel (top-right) when no decision is pending, so the region
    // always reads as an intentional panel.
    private void DrawIdleResolverPanel()
    {
        if (_log == null) return;
        if (ResolverPanelLayout.BeginDocked("##idleresolver"))
        {
            ImGui.PushTextWrapPos(0f);
            if (_missionWaiting.Count > 0)
                foreach (var (color, player, task) in _missionWaiting)
                {
                    ImGui.TextColored(ToVec4(color), player);
                    ImGui.SameLine();
                    ImGui.TextDisabled($"is deciding: {task}");
                }
            else if (_tableState?.Progress.ActivatingUnit is IUnit acting)
                ImGui.TextDisabled($"{acting.Name} is taking its turn...");
            else
                ImGui.TextDisabled("Nothing to decide right now. Your options appear here on your turn.");
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
    }

    // A source toggle, highlighted when on. Turning Chat on clears its unread flag.
    private void DrawConsoleToggle(string labelWithId, ref bool on, bool isChat)
    {
        // Capture the state BEFORE the button: clicking flips `on`, and the pop must match the push
        // regardless of that flip (else the style stack is left unbalanced -> ImGui asserts/crashes).
        bool wasOn = on;
        if (wasOn) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        if (ImGui.Button(labelWithId))
        {
            on = !on;
            if (isChat && on) _chatUnread = false;
        }
        if (wasOn) ImGui.PopStyleColor();
    }

    // Merged scrollback of the enabled sources, plus the chat input row when Chat is on.
    private void DrawConsoleContent()
    {
        if (_showChat) _chatUnread = false; // chat is visible

        float inputH = _showChat ? ImGui.GetFrameHeightWithSpacing() : 0f;
        // No HorizontalScrollbar: with it set, ImGui widens the child's work rect to the content size, which
        // is what RenderConsoleLine's TextWrapped wraps against - so nothing ever wrapped and every long line
        // grew the scrollbar instead. Without the flag the wrap width is the visible column and long lines
        // spill onto the next row, which is what a log/chat column should do.
        ImGui.BeginChild("##consolescroll", new Vector2(0, -inputH), ImGuiChildFlags.None,
            ImGuiWindowFlags.None);

        // The engine log stream carries both normal and Debug-tagged lines; keep whichever categories are
        // toggled on (Debug is a developer view, off by default).
        List<LogEntry>? logMsgs = null;
        if (_showLog || _showDebug)
        {
            logMsgs = _log!.Snapshot();
            logMsgs.RemoveAll(e => e.IsDebug ? !_showDebug : !_showLog);
        }
        List<LogEntry>? chatMsgs = (_showChat && _playerMessageUI != null) ? _playerMessageUI.ChatLog.Snapshot() : null;
        int ln = logMsgs?.Count ?? 0, cn = chatMsgs?.Count ?? 0;

        if (ln == 0 && cn == 0)
        {
            ImGui.TextDisabled(!_showLog && !_showChat && !_showDebug
                ? "Log, Debug and Chat hidden -- toggle one on above."
                : "No messages yet.");
        }
        else
        {
            // Merge two arrival-ordered lists by Sequence (two-pointer -- both already sorted).
            int li = 0, ci = 0;
            while (li < ln || ci < cn)
            {
                bool takeLog = ci >= cn || (li < ln && logMsgs![li].Sequence <= chatMsgs![ci].Sequence);
                RenderConsoleLine(takeLog ? logMsgs![li++] : chatMsgs![ci++]);
            }
        }

        int total = ln + cn;
        bool hasNew = total > _lastLogCount;
        _lastLogCount = total;
        if (hasNew && _autoScroll) ImGui.SetScrollHereY(1.0f);
        _autoScroll = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;

        ImGui.EndChild();

        if (_showChat) DrawChatInputRow();
    }

    private static void RenderConsoleLine(LogEntry entry)
    {
        var c = entry.Color;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
        ImGui.TextWrapped(entry.Message);
        ImGui.PopStyleColor();
    }

    private void DrawChatInputRow()
    {
        bool team = _chatChannel == EChatMessageType.Team;
        if (team) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        if (ImGui.Button((team ? "Team" : "Global") + "##chatchannel", new Vector2(74f, 0f)))
            _chatChannel = team ? EChatMessageType.Global : EChatMessageType.Team;
        if (team) ImGui.PopStyleColor();
        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1f);
        if (_playerMessageUI != null &&
            ImGui.InputTextWithHint("##chatinput", "Chat... (Enter to send)", ref _chatInput, 512,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _playerMessageUI.Submit(_chatInput, _chatChannel);
            _chatInput = "";
        }
    }
}
