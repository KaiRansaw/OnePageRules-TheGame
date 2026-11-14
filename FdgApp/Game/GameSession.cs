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
            // Quest: every hero keeps what it earned (XP, level, items, injuries), and a campaign mission is recorded
            // in its campaign - before the Game Over card reads it. Only heroes whose files are on this computer.
            if (GameSystemInfo.IsQuest(vm.GameSystem))
            {
                try { FdgRaylib.Quest.QuestHeroSync.WriteBack(game.TableState); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            session.EndedText = text;
        };
        session.Report = new BattleReport(game.TableState) { PlayerName = id => session.SeatOf(id)?.Name ?? "" };
        game.AssignInterfaces(session.Log, session.Log, registry, presentationSink: session.Report, outstandingTaskDisplay: null);
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

    public bool IsMine(PlayerID id) => Game.LocalPlayerIDs.Contains(id);

    /// <summary>The Quest campaign mission this game is, or null.</summary>
    public FdgRaylib.Quest.QuestCampaignLaunch? Campaign => Lobby.Campaign;

    /// <summary>The army campaign mission this game is, or null.</summary>
    public FdgRaylib.Quest.ArmyCampaignLaunch? ArmyCampaign => Lobby.ArmyCampaign;
}
