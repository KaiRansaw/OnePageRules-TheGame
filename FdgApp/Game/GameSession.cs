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
    public HybridResolverRegistry Registry { get; }
    public LobbySession Lobby { get; }

    public sealed record Seat(PlayerID Id, string Name, int ColourCode, int Team, bool Local, bool Bot, string Faction);
    public IReadOnlyList<Seat> Seats { get; }

    /// <summary>"Player 2 (AI) wins!" once the engine says the game is over; null while it runs.</summary>
    public string? EndedText { get; private set; }

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
            lobby.SeatArmy(p.PlayerID).Army?.Faction ?? "")).ToList();

        PlayerID autopilotFor = game.LocalPlayerIDs.Count > 0 ? game.LocalPlayerIDs[0] : infos[0].PlayerID;
        EAiProfile profile = GameSystemInfo.IsQuest(vm.GameSystem) ? EAiProfile.StarQuest : EAiProfile.SoloCoOp;
        IStageResolverRegistry autopilot = AiProfileFactory.BuildRegistry(profile, game.TableState, autopilotFor,
            seeThroughFriendlyUnits: vm.SeeThroughFriendlyUnits);
        var registry = new HybridResolverRegistry(autopilot);
        registerFrontEnd(registry);

        var session = new GameSession(game, lobby, registry, seats);
        vm.OnGameEnded += text => session.EndedText = text;
        game.AssignInterfaces(session.Log, session.Log, registry, presentationSink: null, outstandingTaskDisplay: null);
        return session;
    }

    public Seat? SeatOf(PlayerID id) => Seats.FirstOrDefault(s => s.Id == id);

    public bool IsMine(PlayerID id) => Game.LocalPlayerIDs.Contains(id);
}
