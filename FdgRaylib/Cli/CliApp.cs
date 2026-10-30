using FDG;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.TextInterface;
using FdgRaylib.Cli.Resolvers;
using FdgRaylib.Rendering;
using Raylib_cs;

namespace FdgRaylib.Cli;

public class CliApp
{
    private readonly bool _headless;
    private readonly int _slowDelayMs;

    // #193 --seed: seeds dice, decisive rolls, objective auto-placement, and each AI player's stream.
    private readonly int? _diceSeed;

    // #191 --ai-profile: which AI drives the computer slots (solo-rules baseline or the Tactician).
    private readonly EAiProfile _aiProfile;

    // Initialized by Prepare(); used by RunAsync().
    private LocalMessageBus? _messageBus;
    private GameDataStore? _gameDataStore;
    private FDGGame_AsLocal? _localGame;
    private FDGGame_AsLocal? _aiGame;

    public ITableState? TableState => _localGame?.TableState;
    public GameLog? Log { get; private set; }

    // Filled during CreatePlayerSlots(); read by the renderer at draw time.
    public Dictionary<PlayerID, Color> PlayerColors { get; } = new();

    // #413 --all-ai on the lobby path: slot 0 is a bot too, so a game plays itself end to end.
    private readonly bool _allAi;

    public CliApp(bool headless, int slowDelayMs = 0, int? diceSeed = null,
        EAiProfile aiProfile = EAiProfile.SoloRules, bool allAi = false)
    {
        _headless = headless;
        _slowDelayMs = slowDelayMs;
        _diceSeed = diceSeed;
        _aiProfile = aiProfile;
        _allAi = allAi;
    }

    /// <summary>
    /// #413 - what a scripted headless run is playing. Every field is optional; anything left null keeps
    /// the engine default, which is what every run did before this existed (standard Grimdark Future).
    /// <para>
    /// This is what makes Firefight, Skirmish, Horde and the Game Master reachable without a window.
    /// Before it, <c>GameSettings.GetDefault()</c> was the only thing a headless run could play, so most
    /// of the test plan could only ever be checked by hand - which is why five patches reached Kai
    /// untested at once.
    /// </para>
    /// </summary>
    /// <param name="GameSystem">GF, GF: Firefight, AoF, AoF: Skirmish.</param>
    /// <param name="GameMode">Standard, Horde, and the Star Quest modes.</param>
    /// <param name="PlayStyle">Standard or Game Master.</param>
    /// <param name="ArmyPoints">The lobby's points limit; a campaign army needs this lifted.</param>
    /// <param name="Army1">Slot 0's army file. Null prompts, as before.</param>
    /// <param name="Army2">Slot 1's army file. Null prompts, as before.</param>
    /// <param name="Party">
    /// Star Quest only: the 2-4 heroes slot 0 plays, as <c>.fdghero</c> paths, a folder of them, or
    /// "auto" for a generated party (see <see cref="PartyLoader"/>). On a Quest game system an empty list
    /// still means "generate one" - Star Quest's local side is never an army list, so there is no sane
    /// army-shaped default to fall back to.
    /// </param>
    public sealed record Setup(
        EGameSystem? GameSystem = null,
        EGameMode? GameMode = null,
        EPlayStyle? PlayStyle = null,
        int? ArmyPoints = null,
        string? Army1 = null,
        string? Army2 = null,
        IReadOnlyList<string>? Party = null);

    /// <summary>#413 - set before <see cref="RunAsync"/> to script what is played.</summary>
    public Setup? Scripted { get; set; }

    // Creates the local game instance (and thus TableState) without any user prompts.
    // Call this before starting the Raylib window.
    public void Prepare()
    {
        _messageBus    = new LocalMessageBus();
        _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
        _localGame     = new FDGGame_AsLocal(_gameDataStore, _messageBus);

        if (!_headless)
            Log = new GameLog();
    }

    public async Task RunAsync()
    {
        if (_localGame == null) Prepare();

        Console.WriteLine("=== FDG Raylib ===");
        string modeDesc = _headless ? "headless CLI" : "CLI + Raylib";
        if (_slowDelayMs > 0) modeDesc += $" (slow mode: {_slowDelayMs}ms)";
        Console.WriteLine($"Mode: {modeDesc}");
        Console.WriteLine();

        var playerSlots = CreatePlayerSlots();

        ILogMessageUI logUI = Log != null
            ? new GuiLogMessageUI(Log)
            : new CliLogMessageUI();

        if (_allAi)
        {
            // #413 --all-ai: nobody is at the keyboard, so slot 0 is a bot like every other slot. The
            // human game owns NO player, and AssignInterfaces would throw building its chat endpoint, so
            // it is left unwired and the narrative arrives through slot 0's controller logSink - exactly
            // what the scenario path already does (see ScenarioLauncher.BuildResume).
            //
            // This is what makes Star Quest testable rather than merely launchable. Answering every
            // request with the EOF default means each hero takes the FIRST option it is offered, so the
            // party stands where it deployed: no objectives seized, no mission ever completed, and so no
            // reward, boss fight or exfiltration is ever reached. A bot at least walks the heroes
            // towards their goal and shoots.
            IStageResolverRegistry heroRegistry = AiProfileFactory.BuildRegistry(
                _aiProfile, _localGame!.TableState, playerSlots[0].PlayerID, _diceSeed,
                playerSlots[0].SlotID, null, false);
            playerSlots[0].AssignPlayerController(new ScenarioAiPlayerController(
                "Player 1 (AI)", playerSlots[0].PlayerID, _localGame, heroRegistry,
                logSink: Console.WriteLine));
        }
        else
        {
            // Player 1 — human (CLI)
            _localGame!.AddLocalPlayerID(playerSlots[0].PlayerID);

            IStageResolverRegistry resolverRegistry = BuildResolverRegistry();
            if (_slowDelayMs > 0)
                resolverRegistry = new SlowModeResolverRegistry(resolverRegistry, _slowDelayMs);

            _localGame!.AssignInterfaces(
                logMessageUI:          logUI,
                playerMessageUI:       new CliPlayerMessageUI(),
                stageResolverRegistry: resolverRegistry,
                presentationSink:      null, // headless: beats emitted but not rendered
                outstandingTaskDisplay: null);

            // Name the controllers explicitly: reading playerSlots[i].Name here returns the "[Empty]"
            // placeholder, since the controller that supplies the real name isn't assigned yet.
            var humanController = new LocalPlayerController("Player 1", playerSlots[0].PlayerID, _localGame);
            playerSlots[0].AssignPlayerController(humanController);
        }

        // Player 2 — computer (profile per --ai-profile; solo-rules by default)
        _aiGame = new FDGGame_AsLocal(_gameDataStore!, _messageBus!);
        var aiController = AiProfileFactory.CreateController(_aiProfile,
            "Player 2 (AI)", playerSlots[1].PlayerID, _aiGame, _diceSeed, playerSlots[1].SlotID);
        playerSlots[1].AssignPlayerController(aiController);

        var gameSettings = GameSettings.GetDefault();
        gameSettings.RandomnessType = ERandomnessType.Realistic;
        gameSettings.ObjectivePlacementMode = EObjectivePlacementMode.AutoPlaced;
        gameSettings.DiceSeed = _diceSeed;

        // #413: a scripted run overrides what it names and leaves the rest at the engine default.
        if (Scripted is { } setup)
        {
            if (setup.GameSystem is { } system)
            {
                gameSettings.GameSystem = system;
                // Match what the lobby does when a game type is picked: the army source follows the
                // system, or an army of the right system is refused at the launch gate.
                gameSettings.AllowedGameSystems = GameSystemInfo.ArmySource(system);
                if (GameSystemInfo.IsSkirmish(system)) gameSettings.ArmyPoints = 300;
            }
            if (setup.GameMode is { } mode) gameSettings.GameMode = mode;
            if (setup.PlayStyle is { } style) gameSettings.PlayStyle = style;
            if (setup.ArmyPoints is { } points && points > 0) gameSettings.ArmyPoints = points;

            Console.WriteLine($"Playing: {GameSystemInfo.DisplayName(gameSettings.GameSystem)} - "
                + $"{gameSettings.GameMode} - {gameSettings.PlayStyle} - {gameSettings.ArmyPoints} pts");
        }

        var gameEnded = new TaskCompletionSource();
        var server = new FDGServer(_gameDataStore!, _messageBus!, gameSettings, playerSlots);
        // #168: army loads just ran inside the server build - flush their buffered rule warnings
        // into this mode's log as the aggregated summary (headless keeps the stdout fallback).
        if (Log != null)
            RuleLoadWarnings.AttachLog(Log);
        server.OnGameCompleted += PrintGameResult;
        server.OnGameEnded += result =>
        {
            logUI.DisplayLogMessage($"Game ended: {result}", TextColor.White);
            gameEnded.TrySetResult();
        };

        Console.WriteLine("Game started.");
        await gameEnded.Task;
    }

    /// <summary>
    /// #167 --scenario: resumes an already-populated store (a compiled scenario or a loaded save)
    /// headless, lobby-free: slot 0 drives via the CLI resolvers, every other saved slot is AI.
    /// --all-ai makes slot 0 AI too (observation runs); --log-decisions interleaves each planning
    /// AI's Choose Action narration into stdout as "[ai N] ..." lines (FdgLab's flag, here).
    /// </summary>
    public async Task RunScenarioAsync(GameDataStore loadedStore, bool allAi = false,
        bool logDecisions = false)
    {
        Console.WriteLine("=== FDG Raylib - scenario ===");

        var parts = ScenarioLauncher.BuildResume(loadedStore, _diceSeed, _aiProfile, allAi,
            logDecisions ? slot => line => Console.WriteLine($"[ai {slot}] {line}") : null,
            logSink: allAi ? Console.WriteLine : null);
        _messageBus = parts.Bus;
        _gameDataStore = parts.Store;
        _localGame = parts.HumanGame;

        ILogMessageUI logUI = new CliLogMessageUI();
        if (!allAi)
        {
            // A human on slot 0: the human game owns the log/chat endpoint and the CLI resolvers.
            // With --all-ai the human game owns NO player, and AssignInterfaces would throw building
            // the chat endpoint - the narrative arrives through slot 0's controller logSink instead.
            IStageResolverRegistry resolverRegistry = BuildResolverRegistry();
            if (_slowDelayMs > 0)
                resolverRegistry = new SlowModeResolverRegistry(resolverRegistry, _slowDelayMs);

            _localGame.AssignInterfaces(
                logMessageUI:          logUI,
                playerMessageUI:       new CliPlayerMessageUI(),
                stageResolverRegistry: resolverRegistry,
                presentationSink:      null,
                outstandingTaskDisplay: null);
        }

        var gameEnded = new TaskCompletionSource();
        var server = new FDGServer(parts.Store, parts.Bus, parts.Slots); // resume constructor
        server.OnGameCompleted += PrintGameResult;
        server.OnGameEnded += result =>
        {
            logUI.DisplayLogMessage($"Game ended: {result}", TextColor.White);
            gameEnded.TrySetResult();
        };

        Console.WriteLine("Scenario resumed.");
        await gameEnded.Task;
    }

    /// <summary>
    /// #192: the one machine-readable line a headless run emits. Automated verification (smoke tests,
    /// FdgLab benchmarks) keys off the "Game result:" prefix, so keep it single-line and stable.
    /// </summary>
    private static void PrintGameResult(GameResult result) =>
        Console.WriteLine($"Game result: {result.ToSummaryLine()}");

    private PlayerSlot[] CreatePlayerSlots()
    {
        var player1ID = new PlayerID(Guid.NewGuid());
        var player2ID = new PlayerID(Guid.NewGuid());

        PlayerColors[player1ID] = Color.Blue;
        PlayerColors[player2ID] = Color.Red;

        // #413: a scripted run names its armies, so nothing is read from stdin. A named file that
        // cannot be loaded is fatal rather than quietly falling back to the built-in test army - a
        // scripted run that silently plays the wrong army is worse than one that stops.
        //
        // Star Quest is the exception: slot 0 plays a PARTY of heroes, not an army list, and handing it
        // one ends the mission in round 1 with "every hero is down" because nothing on the table is a
        // quest hero. So on a Quest game the slot comes from PartyLoader instead.
        ArmyListFile army1 = WantsParty()
            ? PartyLoader.Build(Scripted!.Party ?? Array.Empty<string>())
            : LoadOrPrompt(Scripted?.Army1, "Player 1");
        Console.WriteLine();
        ArmyListFile army2 = LoadOrPrompt(Scripted?.Army2, "Player 2");
        Console.WriteLine();

        return new[]
        {
            new PlayerSlot(slotID: 0, teamNumber: 0, playerID: player1ID, armyListFile: army1, gameDataStore: _gameDataStore!),
            new PlayerSlot(slotID: 1, teamNumber: 1, playerID: player2ID, armyListFile: army2, gameDataStore: _gameDataStore!),
        };
    }

    /// <summary>
    /// #413 - whether slot 0 should be a Star Quest party rather than an army list. True when the run
    /// named a party, and true for a Quest game system or a Star Quest game mode even when it did not,
    /// since those cannot be played with an army list at all.
    /// </summary>
    public bool WantsParty()
    {
        if (Scripted is not { } setup) return false;
        if (setup.Party != null) return true;
        if (setup.GameSystem is { } system && GameSystemInfo.IsQuest(system)) return true;
        return setup.GameMode is { } mode && GameSystemInfo.IsQuestMode(mode);
    }

    /// <summary>
    /// #413 - a named army file, or the interactive prompt when none was named. In a scripted run
    /// nobody is at the keyboard, so an unnamed slot silently takes the built-in test army rather than
    /// stalling on a prompt. A named file that will not load is an error, never a silent fallback.
    /// </summary>
    private ArmyListFile LoadOrPrompt(string? path, string label)
    {
        if (path == null)
            return Scripted == null ? ArmyLoader.PromptForArmy(label) : ArmyLoader.TestArmy(label);
        return ArmyLoader.LoadPath(path)
            ?? throw new InvalidOperationException($"{label}: could not load the army file '{path}'.");
    }

    private IStageResolverRegistry BuildResolverRegistry()
        => ResolverRegistryFactory.Build(_localGame!.TableState);
}
