using System.Text.Json;
using FDG;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.EngineInterface;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Config;
using FdgRaylib.Rendering;

namespace FdgRaylib.Lobby;

/// <summary>
/// Everything a lobby screen does that is not drawing, for the Godot lobby (port step 2): hosting, pointing
/// the lobby at a game and mode, starter armies for new seats, army picks, seat rules, team names, map
/// choice, launch blockers and LAUNCH itself.
///
/// <para>Ported from the Raylib <c>LobbyScreen</c>, which keeps its own copy until the Raylib build is
/// retired (Kai's port plan: Raylib screens are frozen). The view model (engine) does the real work; this
/// is the glue the screen used to hold. Call <see cref="Tick"/> from the screen's frame / timer: like the
/// Raylib lobby, starter armies arrive once the background armies-folder scan is in.</para>
/// </summary>
public sealed class LobbySession : IDisposable
{
    public ILobbyViewModel Vm { get; }

    /// <summary>The game the host launched, waiting for the front end to pick it up on its own thread.</summary>
    private IFDGGame? _launched;
    public IFDGGame? TakeLaunchedGame() => Interlocked.Exchange(ref _launched, null);

    public string? LastLaunchError { get; private set; }
    public bool LaunchStarted { get; private set; }

    // Slots this lobby has already handed a starter army (rolled or hand-picked), so a roll is never
    // overwritten on the next tick and a slot whose roll found nothing is asked once.
    private readonly HashSet<Guid> _served = new();
    private BotArmyPicker? _picker;
    private IReadOnlyList<ArmyCatalogEntry>? _armies;
    private EGameSystem? _armiesSystem, _servedForSystem;

    public LobbySession(ILobbyViewModel vm)
    {
        Vm = vm;
        _ = ArmyCatalogCache.Current;               // start the armies-folder scan now (background)
        vm.OnLaunched += game => Interlocked.Exchange(ref _launched, game);

        // A fresh host lobby opens on the settings the last hosted game used (#310); no-op for a client or
        // a resume.
        UserConfig.Current.HostSettings.ApplyTo(vm);
        if (vm.HasHostPrivileges && !vm.IsResumeMode)
        {
            var backgrounds = Enum.GetValues<ETableBackground>();
            vm.SetTableBackground(backgrounds[Random.Shared.Next(backgrounds.Length)]);
        }
    }

    /// <summary>Starts hosting on the saved name and the fixed port, pointed at <paramref name="system"/> and
    /// <paramref name="mode"/>. False with a sentence for the player when the port is taken.</summary>
    public static bool TryHost(EGameSystem system, EGameMode mode, out LobbySession? session, out string error)
    {
        ILobbyViewModel? created = null;
        var starter = new HostStarter { OnCreated = vm => created = vm };
        session = null;
        if (!starter.TryCreate(out error) || created == null) return false;
        session = new LobbySession(created);
        ApplyGameSystem(created, system);
        ApplyGameMode(created, mode);
        return true;
    }

    public void Dispose() => Vm.Dispose();

    // ------------------------------------------------------------------ game and mode

    /// <summary>Point a lobby at a game: its army folder, a sensible army size for its scale, a mode it has.</summary>
    public static void ApplyGameSystem(ILobbyViewModel viewModel, EGameSystem system)
    {
        EGameSystem current = viewModel.GameSystem;
        viewModel.SetGameSystem(system);
        viewModel.SetAllowedGameSystems(GameSystemInfo.ArmySource(system));
        viewModel.SetShootingMode(EShootingMode.DeclareFirst);
        bool wasSkirmish = GameSystemInfo.IsSkirmish(current), isSkirmish = GameSystemInfo.IsSkirmish(system);
        if (isSkirmish && !wasSkirmish) viewModel.SetArmyPoints(300);
        else if (!isSkirmish && wasSkirmish) viewModel.SetArmyPoints(2000);
        if (Array.IndexOf(GameSystemInfo.Modes(system), viewModel.GameMode) < 0)
            viewModel.SetGameMode(GameSystemInfo.Modes(system)[0]);
        else if (viewModel.GameMode == EGameMode.Horde)
            ApplyGameMode(viewModel, EGameMode.Horde);
    }

    /// <summary>Point a lobby at a mode. Horde also takes the army size its book prints.</summary>
    public static void ApplyGameMode(ILobbyViewModel viewModel, EGameMode mode)
    {
        viewModel.SetGameMode(mode);
        if (mode == EGameMode.Horde && FDG.StateMachine.HordeRules.BookArmyPoints(viewModel.GameSystem) is { } points)
            viewModel.SetArmyPoints(points);
    }

    /// <summary>"Age of Fantasy - Army vs Army - 2000 pts".</summary>
    public string HeaderTitle =>
        $"{GameSystemInfo.DisplayName(Vm.GameSystem)}  -  {ModeLabel(Vm.GameMode)}  -  {Vm.ArmyPoints} pts";

    public static string ModeLabel(EGameMode mode) => mode switch
    {
        EGameMode.Standard => "Army vs Army",
        EGameMode.Horde or EGameMode.StarQuestHorde => "Horde Mode",
        EGameMode.StarQuestMission => "Play a Mission",
        _ => mode.ToString(),
    };

    public bool CanEdit => Vm.HasHostPrivileges && !Vm.IsResumeMode;

    // ------------------------------------------------------------------ seats

    public const int SeatGridSize = 4;

    /// <summary>Seats 1 and 2 never switch off (a game needs two sides); only the last seat does, so the
    /// roster never slides a player into another square.</summary>
    public static bool SeatCanSwitchOff(int seat, int seatCount) => seat >= 2 && seat == seatCount - 1;

    /// <summary>Seats fill from the left: only the next empty square can be switched on.</summary>
    public static bool SeatCanSwitchOn(int seat, int seatCount) => seat == seatCount && seat < SeatGridSize;

    public EAiProfile BotProfile => GameSystemInfo.IsQuest(Vm.GameSystem) ? EAiProfile.StarQuest : EAiProfile.SoloCoOp;

    public void AddSeat() { if (CanEdit) Vm.AddAiPlayer(BotProfile); }

    public void ToggleBot(LobbyPlayerInfoSummary info)
    {
        if (!Vm.CheckCanSetPlayerType(info.PlayerID)) return;
        if (info.PlayerType == EPlayerType.AI) Vm.SetPlayerType(info.PlayerID, EPlayerType.Local);
        else Vm.SetPlayerType(info.PlayerID, EPlayerType.AI, BotProfile);
    }

    /// <summary>The seat's tag, read from where the viewer sits (a joined friend sees YOU / HOST).</summary>
    public string SeatOwnerLabel(LobbyPlayerInfoSummary info)
    {
        bool own = !Vm.HasHostPrivileges && Vm.CheckCanModifyPlayerIDInfo(info.PlayerID);
        if (Vm.HasHostPrivileges) return info.PlayerType == EPlayerType.AI ? "Bot" : info.PlayerType.ToString();
        if (own) return "You";
        return info.PlayerType switch
        {
            EPlayerType.Local => "Host",
            EPlayerType.Network => "Player",
            EPlayerType.AI => "Bot",
            _ => info.PlayerType.ToString(),
        };
    }

    /// <summary>Every seat's colour this frame: picks win, unpicked seats take the free defaults in order.</summary>
    public int[] EffectiveColours()
    {
        IReadOnlyList<LobbyPlayerInfoSummary> players = Vm.PlayerInfos;
        var chosen = new int?[players.Count];
        for (int i = 0; i < players.Count; i++) chosen[i] = players[i].ColorIndex >= 0 ? players[i].ColorIndex : null;
        return PlayerColorCodes.ResolveIndices(chosen);
    }

    public string TeamLabel(int team)
    {
        int count = Vm.PlayerInfos.Count;
        if (!GameSystemInfo.IsHordeLike(Vm.GameMode) || count == 0) return $"Team {team}";
        int defenders = (int)Vm.PlayerInfos[0].TeamNumber;
        if (team == defenders) return GameSystemInfo.IsQuestMode(Vm.GameMode) ? "Heroes" : "Defenders";
        if (GameSystemInfo.IsQuestMode(Vm.GameMode)) return "Enemies";
        return count > 2 ? $"Horde ({team})" : "Horde";
    }

    /// <summary>2v2: the grid's left column against its right column.</summary>
    public void TeamsTwoVsTwo()
    {
        var p = Vm.PlayerInfos;
        for (int seat = 0; seat < p.Count; seat++) Vm.SetPlayerTeam(p[seat].PlayerID, (ETeamOption)(seat % 2 + 1));
    }

    public void TeamsFreeForAll()
    {
        var p = Vm.PlayerInfos;
        for (int seat = 0; seat < p.Count; seat++)
            Vm.SetPlayerTeam(p[seat].PlayerID, (ETeamOption)Math.Min(seat + 1, TeamOptions.MaxTeamNumber));
    }

    // ------------------------------------------------------------------ armies

    public const string NoSeatArmyText = "No army chosen for this seat yet.";
    public const string LoadingSeatArmyText = "Loading army...";
    public const string UnreadableSeatArmyText = "Couldn't read this army - check both PCs run the same build.";
    public const string ResumeSeatArmyText = "Resuming a save: the armies are the ones in the save.";

    /// <summary>The list a seat is bringing, as far as this machine knows it, or why it can't be shown.</summary>
    public (ArmyListFile? Army, string Why) SeatArmy(PlayerID seat)
    {
        if (Vm.IsResumeMode) return (null, ResumeSeatArmyText);
        SeatArmyView view = Vm.GetSeatArmy(seat);
        return view.Status switch
        {
            ESeatArmyStatus.Ready => (view.Army, ""),
            ESeatArmyStatus.Loading => (null, LoadingSeatArmyText),
            ESeatArmyStatus.Unreadable => (null, UnreadableSeatArmyText),
            _ => (null, NoSeatArmyText),
        };
    }

    public static bool ArmyCatalogReady => ArmyCatalogCache.Current.IsLoaded;

    /// <summary>The armies in this game's folder, grouped by 1000-point bracket then A-Z. Empty until the scan is in.</summary>
    public IReadOnlyList<ArmyCatalogEntry> Armies()
    {
        if (!ArmyCatalogReady) return Array.Empty<ArmyCatalogEntry>();
        EGameSystem system = Vm.GameSystem;
        if (_armies == null || _armiesSystem != system)
        {
            _armies = ArmyPaths.EntriesFor(system, ArmyCatalogCache.Current.Entries)
                .OrderBy(e => e.Points <= 0 ? 0 : (e.Points - 1) / 1000)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (_armiesSystem != null && _armiesSystem != system && !Vm.IsResumeMode)
                foreach (LobbyPlayerInfoSummary info in Vm.PlayerInfos)
                    if (Vm.CheckCanModifyPlayerIDInfo(info.PlayerID)) _served.Remove(info.PlayerID.ID);
            _armiesSystem = system;
            _picker = null;
        }
        _picker ??= new BotArmyPicker(_armies);
        return _armies;
    }

    /// <summary>Rescan the armies folder if it changed (a list saved in Army Forge while the lobby was open).</summary>
    public void RefreshArmies()
    {
        if (!ArmyCatalogCache.RefreshIfChanged()) return;
        _armies = null;
        _picker = null;
    }

    /// <summary>Does this seat still need a starter army rolled onto it?</summary>
    public static bool NeedsStarterArmy(EPlayerType playerType, bool armyAssigned, bool canModify, bool alreadyServed)
    {
        if (!canModify || alreadyServed) return false;
        return playerType == EPlayerType.AI || !armyAssigned;
    }

    /// <summary>Per frame: keeps two seats, re-rolls the bots after a game change, and hands every new seat
    /// this machine owns a starter army once the folder scan is in.</summary>
    public void Tick()
    {
        // A game change makes the bots' armies the wrong book: re-roll them (a human's pick turns red instead).
        EGameSystem now = Vm.GameSystem;
        if (_servedForSystem is { } was && was != now && !Vm.IsResumeMode)
            foreach (LobbyPlayerInfoSummary info in Vm.PlayerInfos)
                if (info.PlayerType == EPlayerType.AI && Vm.CheckCanModifyPlayerIDInfo(info.PlayerID))
                    _served.Remove(info.PlayerID.ID);
        _servedForSystem = now;

        if (CanEdit && Vm.PlayerInfos.Count < 2) Vm.AddAiPlayer(BotProfile, seatFiller: true);

        if (Vm.IsResumeMode || !ArmyCatalogReady) return;
        IReadOnlyList<LobbyPlayerInfoSummary> players = Vm.PlayerInfos;
        foreach (LobbyPlayerInfoSummary info in players)
        {
            // Quest: a human seat plays a party of heroes, picked by hand - no starter army.
            if (GameSystemInfo.IsQuest(Vm.GameSystem) && info.PlayerType != EPlayerType.AI) continue;
            if (NeedsStarterArmy(info.PlayerType, info.ArmyListSummary.IsAssigned,
                    Vm.CheckCanModifyPlayerIDInfo(info.PlayerID), _served.Contains(info.PlayerID.ID)))
                RollArmy(info.PlayerID);
        }
        var live = players.Select(p => p.PlayerID.ID).ToHashSet();
        foreach (Guid gone in _served.Where(id => !live.Contains(id)).ToList())
        {
            _served.Remove(gone);
            _picker?.Forget(gone);
        }
    }

    /// <summary>Rolls the next army for a seat: closest to the points limit first, never one another seat
    /// holds, never repeating until the seat has seen them all.</summary>
    public void RollArmy(PlayerID playerID)
    {
        _served.Add(playerID.ID);
        Armies();
        if (_picker == null) return;
        var inUseByOthers = Vm.PlayerInfos
            .Where(p => p.PlayerID != playerID && p.ArmyListSummary.IsAssigned)
            .Select(p => ArmyCatalogEntry.KeyFor(p.ArmyListSummary.ArmyName, p.ArmyListSummary.FactionName, p.ArmyListSummary.PointCost))
            .ToHashSet();
        if (_picker.PickNext(playerID.ID, Vm.ArmyPoints, Vm.AllowedGameSystems, inUseByOthers) is not { } pick) return;
        if (LoadArmyFile(pick.Path) is { } army) Vm.UpdateArmyListFile(playerID, army);
    }

    /// <summary>A hand-picked army for a seat. Never auto-replaced afterwards.</summary>
    public void PickArmy(PlayerID playerID, ArmyCatalogEntry entry)
    {
        if (LoadArmyFile(entry.Path) is not { } army) return;
        _served.Add(playerID.ID);
        Vm.UpdateArmyListFile(playerID, army);
    }

    /// <summary>The key of the army a seat holds, for marking it in a picker.</summary>
    public static string ArmyKey(ArmyListSummary s) =>
        s.IsAssigned ? ArmyCatalogEntry.KeyFor(s.ArmyName, s.FactionName, s.PointCost) : "";

    /// <summary>Reads a .fdgarmy (as a BuiltArmyFile, so a Forge-built army keeps its book and picks), or null.</summary>
    public static BuiltArmyFile? LoadArmyFile(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ map

    /// <summary>A map file, or null for Random (the game lays terrain out at the start).</summary>
    public void SetMap(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Vm.SetTerrainPlacementMode(ETerrainPlacementMode.AutoFromLayout);
            Vm.SetTerrainLayoutPath(null);
        }
        else
        {
            Vm.SetTerrainPlacementMode(ETerrainPlacementMode.LoadFromFile);
            Vm.SetTerrainLayoutPath(path);
        }
    }

    public bool HasMap => Vm.TerrainPlacementMode == ETerrainPlacementMode.LoadFromFile && !string.IsNullOrEmpty(Vm.TerrainLayoutPath);

    /// <summary>The table this game is set up for, ignoring any chosen map.</summary>
    public (float W, float H) TableWithoutMap() =>
        Vm.ChosenTableSize ?? TableSizing.AutoSize(Vm.GameSystem, Vm.GameMode, Vm.ArmyPoints);

    /// <summary>The table the game will actually play on: the map's own size when a map is chosen.</summary>
    public (float W, float H) TableSize() =>
        HasMap && Vm.MapTableSize is { } m ? m : TableWithoutMap();

    // ------------------------------------------------------------------ launch

    /// <summary>What stops LAUNCH, in plain words. Empty when it can go.</summary>
    public IReadOnlyList<string> Blockers() =>
        Vm.HasHostPrivileges && !Vm.IsResumeMode ? Vm.BlockingLaunchProblems() : Array.Empty<string>();

    /// <summary>Army problems that don't block but are worth a "launch anyway?".</summary>
    public IReadOnlyList<string> Warnings() => Vm.IsResumeMode ? Array.Empty<string>() : Vm.ValidateArmiesForLaunch();

    public bool Launch()
    {
        bool resume = Vm.IsResumeMode;
        string? fail;
        bool started = resume ? Vm.TryResumeGame(out fail) : Vm.TryLaunchGame(out fail);
        LastLaunchError = started ? null : (fail ?? "Launch failed.");
        if (started)
        {
            LaunchStarted = true;
            PersistHostSettings();
            // Rematch: what this game was set up with, while the lobby still has it. A resumed save keeps
            // the last fresh launch's plan cleared - it has no set-up of its own to repeat.
            LastLaunch = !resume && Vm is LobbyViewModel_Host host
                ? new RematchPlan(host.GameSystem, host.GameMode, host.GetSeats()) : null;
        }
        return started;
    }

    /// <summary>Remembers this lobby's settings for the next one the host opens (#310).</summary>
    public void PersistHostSettings()
    {
        if (!Vm.HasHostPrivileges || Vm.IsResumeMode) return;
        UserConfig.Current.HostSettings = HostGameSettings.CaptureFrom(Vm);
        UserConfig.Save();
    }

    // ------------------------------------------------------------------ rematch, save, load

    /// <summary>What a game was launched with, for the Game Over card's Rematch (Kai, 2026-11-07: "back to the
    /// same lobby, same armies").</summary>
    public sealed record RematchPlan(EGameSystem System, EGameMode Mode, IReadOnlyList<LobbyViewModel_Host.SeatSnapshot> Seats);

    /// <summary>The last game this computer launched fresh as host; null after a loaded game.</summary>
    public static RematchPlan? LastLaunch { get; private set; }

    /// <summary>Why Rematch can't be offered right now, or null when it can.</summary>
    public static string? RematchUnavailableReason(LobbySession? playing) =>
        playing?.Vm is not { HasHostPrivileges: true } ? "Only the host can start a rematch."
        : LastLaunch == null ? "A loaded game can't be rematched - start it again from the game screen."
        : null;

    /// <summary>
    /// Re-seats this fresh host lobby the way the last game was seated - same order, bots with the same AI, the
    /// same teams, colours and armies (ported from the Raylib lobby's ApplyPendingRematch). The settings already
    /// match: they are the saved host settings the last launch wrote. A friend who joined over the network has to
    /// join again, so a network seat is not re-created.
    /// </summary>
    public void ApplyRematch(RematchPlan plan)
    {
        if (!Vm.HasHostPrivileges || Vm.IsResumeMode || Vm.PlayerInfos.Count == 0 || plan.Seats.Count == 0) return;
        Vm.SetArmyPoints(UserConfig.Current.HostSettings.ArmyPoints);
        var teams = new List<(PlayerID Id, ETeamOption Team)>();
        for (int i = 0; i < plan.Seats.Count; i++)
        {
            LobbyViewModel_Host.SeatSnapshot seat = plan.Seats[i];
            PlayerID id;
            if (i == 0) id = Vm.PlayerInfos[0].PlayerID;   // the host's own seat is always row 0
            else
            {
                if (seat.PlayerType == EPlayerType.Network) continue;
                var before = Vm.PlayerInfos.Select(p => p.PlayerID.ID).ToHashSet();
                if (seat.PlayerType == EPlayerType.AI) Vm.AddAiPlayer(seat.AiProfile);
                else Vm.AddLocalPlayer();
                if (Vm.PlayerInfos.FirstOrDefault(p => !before.Contains(p.PlayerID.ID)) is not { } added) continue;
                id = added.PlayerID;
            }
            _served.Add(id.ID);
            if (seat.TeamNumber != ETeamOption.None) teams.Add((id, seat.TeamNumber));
            if (seat.ColorIndex >= 0) Vm.SetPlayerColor(id, seat.ColorIndex);
            if (seat.Army != null) Vm.UpdateArmyListFile(id, seat.Army);
        }
        foreach (var (id, team) in teams) Vm.SetPlayerTeam(id, team);
    }

    /// <summary>
    /// Writes the running game to the Saved Games folder as <paramref name="name"/> (made safe for a file name; a
    /// clash gets " (2)"). Returns the path, or null with a sentence in <paramref name="error"/>. Host only - a
    /// client has no authoritative copy of the game to save.
    /// </summary>
    public string? SaveGame(string name, out string error)
    {
        error = "";
        string? json = Vm.SaveGameToJson();
        if (json == null) { error = "Only the host can save a game."; return null; }
        try
        {
            string safe = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
            if (safe.Length == 0) safe = $"Saved Game {DateTime.Now:yyyy-MM-dd HH-mm}";
            string folder = FdgRaylib.SaveLoad.SavePaths.FolderPath;
            string path = Path.Combine(folder, safe + GameSaveFile.EXTENSION_WITH_PERIOD);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{safe} ({n}){GameSaveFile.EXTENSION_WITH_PERIOD}");
            File.WriteAllText(path, json);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "The save couldn't be written: " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Opens a host lobby around a saved game (ported from the Raylib Load Game flow): the lobby is in resume
    /// mode - the armies and settings are the save's - and LAUNCH carries on from where it was saved. False with
    /// a sentence for the player when the save can't be read, was made before round 1, or the port is taken.
    /// </summary>
    public static bool TryResume(string path, out LobbySession? session, out string error)
    {
        session = null;
        FDG.Data.GameDataStore store;
        try { store = GameSaveSerializer.Load(File.ReadAllText(path)); }
        catch (Exception ex)
        {
            Console.WriteLine($"Load game failed: {ex.GetBaseException().Message}");
            error = "That save couldn't be read.";
            return false;
        }
        if (GameProgressUtilities.TryGetProgress(store) == null)
        {
            error = "That game was saved before round 1 started, so it can't be resumed.";
            return false;
        }
        var host = new FDG.Network.Connection.FDGHost(port: HostStarter.Port);
        Task hostTask = host.StartAsync();
        if (hostTask.IsFaulted)
        {
            Console.WriteLine($"Host failed to start: {hostTask.Exception?.GetBaseException().Message}");
            error = $"The game could not start, because network port {HostStarter.Port} is already in use. Is another copy of the game open?";
            return false;
        }
        string name = string.IsNullOrWhiteSpace(UserConfig.Current.PlayerName) ? UserConfig.DefaultPlayerName : UserConfig.Current.PlayerName;
        session = new LobbySession(new LobbyViewModel_Host(name, "Loaded Game", "", host, store));
        LastLaunch = null;   // a loaded game has no fresh set-up to rematch
        error = "";
        return true;
    }
}
