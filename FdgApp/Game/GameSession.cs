using FDG;
using FDG.Ai;
using FDG.EngineInterface;
using FDG.Players;
using FDG.StageResolution;
using FdgRaylib.Lobby;
using FdgRaylib.Rendering;

namespace FdgRaylib.Game;

/// <summary>
/// A launched game as a front end needs it, with no drawing: the engine game, its table, the battle report,
/// the resolver registry (front end first, bot autopilot for the rest), each player's name, colour and team,
/// and how the game ended. Built once when the lobby's LAUNCH comes back with the engine game.
/// </summary>
public sealed class GameSession
{
    public IFDGGame Game { get; }
    public ITableState Table => Game.TableState;
    public GameLogBuffer Log { get; } = new();

    /// <summary>What happened, line by line with hover data, built from the engine's presentation beats.</summary>
    public BattleReport Report { get; private set; } = null!;
    public HybridResolverRegistry Registry { get; }
    public LobbySession Lobby { get; }

    public sealed record Seat(PlayerID Id, string Name, int ColourCode, int Team, bool Local, bool Bot, string Faction);
    public IReadOnlyList<Seat> Seats { get; }

    /// <summary>"Player 2 (AI) wins!" once the engine says the game is over; null while it runs.</summary>
    public string? EndedText { get; private set; }

    /// <summary>An army campaign mission's write-back summary (both armies' results), once the game is recorded.</summary>
    public string? CampaignNote { get; private set; }

    /// <summary>
    /// #187: a game a dropped connection ended is written to a recovery save on the host (no clicks - a wifi blip is
    /// common over the internet). Where it went, or why it couldn't be written; null for every other ending.
    /// </summary>
    public string? RecoveryNote { get; private set; }

    /// <summary>Records this game as a battle replay (host only; null on a joined friend's screen).</summary>
    public FdgRaylib.SaveLoad.BattleReplay.Recorder? Replay { get; private set; }

    /// <summary>The replay file written when the game ended, or null.</summary>
    public string? ReplayPath { get; private set; }

    public PlayerID? LocalPlayer => Game.LocalPlayerIDs.Count > 0 ? Game.LocalPlayerIDs[0] : null;

    private GameSession(IFDGGame game, LobbySession lobby, HybridResolverRegistry registry, IReadOnlyList<Seat> seats)
    {
        Game = game;
        Lobby = lobby;
        Registry = registry;
        Seats = seats;
    }

    /// <summary>
    /// Wires a just-launched game: <paramref name="registerFrontEnd"/> registers the resolvers the front end
    /// has; everything else is answered by the official bot for the local player.
    /// </summary>
    public static GameSession Start(IFDGGame game, LobbySession lobby, Action<HybridResolverRegistry> registerFrontEnd)
    {
        var vm = lobby.Vm;
        var infos = vm.PlayerInfos;
        int[] colours = lobby.EffectiveColours();
        var seats = infos.Select((p, i) => new Seat(p.PlayerID, p.PlayerName, colours[i], (int)p.TeamNumber,
            game.LocalPlayerIDs.Contains(p.PlayerID), p.PlayerType == EPlayerType.AI,
            FactionOf(game, lobby, p.PlayerID))).ToList();

        PlayerID autopilotFor = game.LocalPlayerIDs.Count > 0 ? game.LocalPlayerIDs[0] : infos[0].PlayerID;
        EAiProfile profile = GameSystemInfo.IsQuest(vm.GameSystem) ? EAiProfile.StarQuest : EAiProfile.SoloCoOp;
        IStageResolverRegistry autopilot = AiProfileFactory.BuildRegistry(profile, game.TableState, autopilotFor,
            seeThroughFriendlyUnits: vm.SeeThroughFriendlyUnits);
        // Hot-seat: every other local player gets a bot of their own for the decisions the screen doesn't take yet.
        var perPlayer = new Dictionary<PlayerID, IStageResolverRegistry> { [autopilotFor] = autopilot };
        foreach (PlayerID other in game.LocalPlayerIDs.Where(id => !id.Equals(autopilotFor)))
            perPlayer[other] = AiProfileFactory.BuildRegistry(profile, game.TableState, other, seeThroughFriendlyUnits: vm.SeeThroughFriendlyUnits);
        var registry = new HybridResolverRegistry(autopilot, perPlayer);
        registerFrontEnd(registry);

        var session = new GameSession(game, lobby, registry, seats);
        // Army campaigns: every campaign army that played is recorded (casualty rolls, XP, treasury, journal) the moment
        // the host's server reports the result - before OnGameEnded, so the end card can read the files.
        if (vm.HasHostPrivileges)
            vm.OnGameCompleted += result =>
            {
                try { session.CampaignNote = FdgRaylib.Quest.StrikeTeamFiles.WriteBack(game.TableState, result); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                // Engine thread, before anything reacts to the end: the cleanest snapshot of the game there is.
                if (result.Outcome == EGameOutcome.Disconnect)
                    session.RecoveryNote = RecoveryNoteFor(FdgRaylib.SaveLoad.RecoverySave.TryWrite(vm.SaveGameToJson(), DateTime.UtcNow));
            };
        vm.OnGameEnded += text =>
        {
            // Battle replays (homebrew, Kai 2026-11-27): written the moment the game ends (or is paused).
            if (session.Replay is { } rec)
            {
                try { session.ReplayPath = rec.Finish(GameSystemInfo.DisplayName(vm.GameSystem), session.Seats.Select(s => s.Name), text); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            // Quest: every hero keeps what it earned (XP, level, items, injuries), and a campaign mission is recorded
            // in its campaign - before the Game Over card reads it. Only heroes whose files are on this computer.
            if (GameSystemInfo.IsQuest(vm.GameSystem))
            {
                try { FdgRaylib.Quest.QuestHeroSync.WriteBack(game.TableState); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            session.EndedText = text;
        };
        // Pause & resume (homebrew, Kai 2026-11-27): a friend asked to pause - the host saves and stops the game.
        if (vm is FDG.Network.Connection.Lobby.LobbyViewModel_Host pausingHost)
            pausingHost.OnPauseRequested += by => session.PauseForEveryone(by);
        session.Report = new BattleReport(game.TableState) { PlayerName = id => session.SeatOf(id)?.Name ?? "" };
        // Only the host has the game's save points to record.
        if (vm.HasHostPrivileges)
            session.Replay = new FdgRaylib.SaveLoad.BattleReplay.Recorder(session.Report, id => session.SeatOf(id)?.Name ?? "");
        game.AssignInterfaces(session.Log, session.Log, registry, presentationSink: session.Report, outstandingTaskDisplay: null);
        return session;
    }

    /// <summary>A battle replay being watched, or null for a real game.</summary>
    public ReplayPlayer? Watching { get; private set; }

    /// <summary>
    /// Battle replays (homebrew, Kai 2026-11-27): a session for watching a replay. The table is the replay's
    /// switching store; the lobby (which the screen reads the game's settings from) is a host lobby around the
    /// first frame with no network; nobody plays, so the screen is in its watch-only mode; no engine runs.
    /// </summary>
    public static GameSession ForReplay(ReplayPlayer replay)
    {
        string name = string.IsNullOrWhiteSpace(Config.UserConfig.Current.PlayerName) ? Config.UserConfig.DefaultPlayerName : Config.UserConfig.Current.PlayerName;
        var vm = new FDG.Network.Connection.Lobby.LobbyViewModel_Host(name, "Replay", "", new FdgRaylib.Network.NoNetworkHost(), replay.FirstFrame);
        var lobby = new LobbySession(vm);
        var game = new FDG.GameModel.FDGGame_AsLocal(replay.Store, new FDG.Simulation.SimulationMessageBus());

        int[] colours = lobby.EffectiveColours();
        var infos = vm.PlayerInfos;
        var seats = infos.Select((p, i) => new Seat(p.PlayerID, replay.Manifest.Players.ElementAtOrDefault(i) ?? p.PlayerName,
            i < colours.Length ? colours[i] : i, (int)p.TeamNumber, Local: false, Bot: true,
            game.TableState.Armies.Objects.OfType<FDG.ArmyData>().FirstOrDefault(a => a.PlayerID.Equals(p.PlayerID))?.Faction ?? "")).ToList();

        IStageResolverRegistry autopilot = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, game.TableState, infos[0].PlayerID);
        var registry = new HybridResolverRegistry(autopilot, new Dictionary<PlayerID, IStageResolverRegistry>());
        var session = new GameSession(game, lobby, registry, seats) { Watching = replay };
        session.Report = new BattleReport(game.TableState) { PlayerName = id => session.SeatOf(id)?.Name ?? "" };
        replay.Report = session.Report;
        game.AssignInterfaces(session.Log, session.Log, registry, presentationSink: session.Report, outstandingTaskDisplay: null);
        replay.Show(0);
        return session;
    }

    /// <summary>The end card's line for a recovery save: where it is and how to resume it, or that it failed.</summary>
    public static string RecoveryNoteFor(string? path) => path != null
        ? $"A player's connection dropped, so the game was saved: {Path.GetFileName(path)}. Load Game on the main menu picks it up where it stopped."
        : "A player's connection dropped, and the recovery save could not be written.";

    /// <summary>A seat's faction: the lobby's army, or (a resumed save, whose armies are in the game) the game's own.</summary>
    private static string FactionOf(IFDGGame game, LobbySession lobby, PlayerID id)
    {
        if (lobby.SeatArmy(id).Army?.Faction is { Length: > 0 } f) return f;
        return game.TableState.Armies.Objects.OfType<FDG.ArmyData>().FirstOrDefault(a => a.PlayerID.Equals(id))?.Faction ?? "";
    }

    public Seat? SeatOf(PlayerID id) => Seats.FirstOrDefault(s => s.Id == id);

    // -- Pause & resume (homebrew, Kai 2026-11-27) ------------------------------------------------------------

    /// <summary>A friend plays in this game over the network (it can be paused for everyone).</summary>
    public bool WithFriends => Lobby.Vm.PlayerInfos.Any(p => p.PlayerType == EPlayerType.Network)
                               || Lobby.Vm is FDG.Network.Connection.Lobby.LobbyViewModel_Client;

    /// <summary>Why the game can't be paused yet, or null.</summary>
    public string? PauseProblem => !WithFriends ? "Pausing is for games with friends - save the game instead."
        : Table.Progress.RoundCount == null ? "Pausing opens once round 1 starts (a game saved during set-up can't be resumed)."
        : EndedText != null ? "The game is over." : null;

    /// <summary>The save name a paused game gets: who played and the round, so everyone finds it in Load Game.</summary>
    public string PausedSaveName()
    {
        string who = string.Join(" vs ", Seats.Select(s => s.Name));
        string round = Table.Progress.RoundCount is int r ? $"Round {r}" : "Set-up";
        return $"Paused - {who} - {round} - {DateTime.Now:yyyy-MM-dd HH-mm}";
    }

    /// <summary>
    /// Pauses the game for everyone. The host saves it (Saved Games, named by <see cref="PausedSaveName"/>) and ends
    /// it on every screen with a line saying who paused and where it was saved; a joined friend asks the host to.
    /// Load Game resumes it later: the host opens the save, friends join, and each goes back into their own seat.
    /// </summary>
    public void PauseForEveryone(string by)
    {
        if (PauseProblem != null) return;
        switch (Lobby.Vm)
        {
            case FDG.Network.Connection.Lobby.LobbyViewModel_Client client:
                client.RequestPause();
                break;
            case FDG.Network.Connection.Lobby.LobbyViewModel_Host host:
                string? path = Lobby.SaveGame(PausedSaveName(), out _);
                host.PauseForEveryone(FDG.Network.Connection.Lobby.LobbyViewModel_Host.PausedText(by,
                    path != null ? Path.GetFileNameWithoutExtension(path) : null));
                break;
        }
    }

    public bool IsMine(PlayerID id) => Game.LocalPlayerIDs.Contains(id);

    /// <summary>
    /// Two or more players at this computer, on opposite sides (hot-seat). Every army is then "mine" to the screen, so
    /// it must not call one of them yours and the other the enemy's - it names both sides (Kai, 2026-11-19).
    /// </summary>
    public bool HotSeat => Game.LocalPlayerIDs.Count > 1 && Game.LocalPlayerIDs.Skip(1).Any(id => IsEnemy(Game.LocalPlayerIDs[0], id));

    /// <summary>
    /// Whether <paramref name="other"/> is on the other side from <paramref name="me"/> - by team, the way the engine
    /// judges it. "Not one of this computer's players" (<see cref="IsMine"/>) is NOT the same thing: with two people
    /// on one computer (hot-seat) both armies are this computer's, and an ally played by a bot or a friend is not.
    /// Anything that asks "can this unit shoot / charge that one" asks this.
    /// </summary>
    public bool IsEnemy(PlayerID me, PlayerID other) => FdgRaylib.Placement.TeamAwareness.IsEnemy(Table, me, other);

    /// <summary>The Quest campaign mission this game is, or null.</summary>
    public FdgRaylib.Quest.QuestCampaignLaunch? Campaign => Lobby.Campaign;

    /// <summary>The army campaign mission this game is, or null.</summary>
    public FdgRaylib.Quest.ArmyCampaignLaunch? ArmyCampaign => Lobby.ArmyCampaign;
}
