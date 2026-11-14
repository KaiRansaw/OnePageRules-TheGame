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
        $"{GameSystemInfo.DisplayName(Vm.GameSystem)}  -  {ModeLabel(Vm.GameMode)}" + (GameSystemInfo.IsQuest(Vm.GameSystem) ? "" : $"  -  {Vm.ArmyPoints} pts");

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

    // ------------------------------------------------------------------ Quest parties

    private readonly Dictionary<Guid, List<string>> _parties = new();

    /// <summary>A Quest campaign mission this lobby was opened for (party fixed, settings from the campaign), or null.</summary>
    public FdgRaylib.Quest.QuestCampaignLaunch? Campaign { get; private set; }

    /// <summary>The hero files a Quest seat has picked (empty = none yet).</summary>
    public IReadOnlyList<string> PartyOf(PlayerID seat) =>
        _parties.TryGetValue(seat.ID, out List<string>? p) ? p : Array.Empty<string>();

    /// <summary>
    /// Gives a Quest seat its party: 2-4 saved heroes, played as one army (each hero a unit). Returns why not, or null.
    /// </summary>
    public string? SetParty(PlayerID seat, IEnumerable<string> heroFiles)
    {
        var files = heroFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count > 4) return "A party is 2 to 4 heroes.";
        _parties[seat.ID] = files;
        _served.Add(seat.ID);
        if (files.Count == 0) return null;
        if (FdgRaylib.Quest.QuestCampaignSession.PartyArmy(Vm.GameSystem, files) is not { } army) return "Couldn't read the heroes' files.";
        Vm.UpdateArmyListFile(seat, army);
        return null;
    }

    /// <summary>
    /// Sets the lobby up for a Quest campaign mission: the party on the host's seat, the chapter's enemy army on the
    /// first bot seat (one is added if there is none). Settings the campaign decides are read from the heroes' sheets
    /// by the engine; the lobby only shows them.
    /// </summary>
    public void ApplyCampaign(FdgRaylib.Quest.QuestCampaignLaunch launch)
    {
        Campaign = launch;
        if (!Vm.HasHostPrivileges || Vm.PlayerInfos.Count == 0) return;
        if (Vm.GameMode != EGameMode.StarQuestMission) ApplyGameMode(Vm, EGameMode.StarQuestMission);
        SetParty(Vm.PlayerInfos[0].PlayerID, launch.Party);
        if (!Vm.PlayerInfos.Any(p => p.PlayerType == EPlayerType.AI)) Vm.AddAiPlayer(BotProfile, seatFiller: true);
        if (launch.EnemyArmyFile is { } file && LoadArmyFile(file) is { } enemy
            && Vm.PlayerInfos.FirstOrDefault(p => p.PlayerType == EPlayerType.AI) is { } bot)
        {
            _served.Add(bot.PlayerID.ID);
            Vm.UpdateArmyListFile(bot.PlayerID, enemy);
        }
    }

    /// <summary>An army campaign mission this lobby was opened for (Mission Setup's START MISSION), or null.</summary>
    public FdgRaylib.Quest.ArmyCampaignLaunch? ArmyCampaign { get; private set; }

    /// <summary>
    /// Sets the lobby up for an army campaign mission (ported from the Raylib lobby's ApplyPendingCampaignArmy): your
    /// mission army on the host seat with the points limit lifted clear of it, the plan's map / deployment / mission
    /// type / challenge bonuses, and the campaign's own enemy on a fresh official-bot seat. Null, or what went wrong.
    /// </summary>
    public string? ApplyArmyCampaign(FdgRaylib.Quest.ArmyCampaignLaunch launch)
    {
        ArmyCampaign = launch;
        if (!Vm.HasHostPrivileges || Vm.PlayerInfos.Count == 0) return "Not hosting.";
        if (LoadArmyFile(launch.ArmyFile) is not { } army) return "Couldn't read your campaign army.";
        PlayerID host = Vm.PlayerInfos[0].PlayerID;
        if (army.TotalPoints > Vm.ArmyPoints) Vm.SetArmyPoints(army.TotalPoints);
        _served.Add(host.ID);
        Vm.UpdateArmyListFile(host, army);

        Vm.SetTerrainPlacementMode(launch.TerrainMode);
        Vm.SetTerrainLayoutPath(launch.TerrainMode == ETerrainPlacementMode.LoadFromFile ? launch.MapPath : null);
        Vm.SetPickDangerousTerrain(launch.TerrainMode == ETerrainPlacementMode.LoadFromFile && RolledOnGenericTable(launch.MapPath));
        Vm.SetDeploymentStyle(launch.Deployment);
        Vm.SetMissionCardGame(launch.MissionCards);
        // Always set (None included), so a campaign mission never inherits what the last ordinary lobby had ticked.
        Vm.SetChallengeBonuses(launch.ChallengeBonuses);

        // Every other seat goes: a campaign mission is you against the campaign's enemy.
        foreach (var extra in Vm.PlayerInfos.Skip(1).Where(p => p.PlayerType == EPlayerType.AI).ToList())
            if (Vm.CheckCanRemovePlayer(extra.PlayerID)) Vm.RemovePlayer(extra.PlayerID);
        if (launch.EnemyArmyFile is not { } file) return null;
        if (LoadArmyFile(file) is not { } enemy) return "Couldn't read the enemy's campaign army.";
        Vm.AddAiPlayer(EAiProfile.SoloCoOp);
        // The bot just added: the AI row nothing has been given yet (a seat that could not be removed was served below).
        if (Vm.PlayerInfos.LastOrDefault(p => p.PlayerID.ID != host.ID && p.PlayerType == EPlayerType.AI) is not { } bot)
            return "Couldn't seat the enemy.";
        _served.Add(bot.PlayerID.ID);
        if (enemy.TotalPoints > Vm.ArmyPoints) Vm.SetArmyPoints(enemy.TotalPoints);
        Vm.UpdateArmyListFile(bot.PlayerID, enemy);
        return null;
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
        // The Generic generator's last step comes with a map rolled on that table; anything else starts without it
        // (the tick box can still turn it on).
        Vm.SetPickDangerousTerrain(RolledOnGenericTable(path));
    }

    /// <summary>Whether a map file was last rolled on the Advanced Rulebooks' Generic terrain table.</summary>
    public static bool RolledOnGenericTable(string? path) =>
        !string.IsNullOrEmpty(path)
        && TerrainLayoutLoader.TryLoadFromFile(path, out _)?.GeneratedFrom == nameof(TerrainBook.ETheme.Generic);

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

    // ------------------------------------------------------------------ joining a friend's game

    /// <summary>
    /// Joins the lobby a friend is hosting at <paramref name="address"/> - their Tailscale name or IP (Kai,
    /// 2026-11-07: "Tailscale only, one box"). Ported from the Raylib Join popup: look the address up, connect on
    /// the game's port, then the join handshake (the host checks both copies of the game match and hands out a
    /// seat). <paramref name="status"/> reports each step in words. Returns the lobby, or null with the reason.
    /// </summary>
    public static async Task<(LobbySession? Session, string Error)> JoinAsync(string address, Action<string>? status = null)
    {
        string name = UserConfig.Current.PlayerName;
        if (string.IsNullOrWhiteSpace(name)) return (null, "Your name is empty - set it in Settings first.");
        if (string.IsNullOrWhiteSpace(address)) return (null, "Type your friend's Tailscale address first.");

        status?.Invoke("Looking up the address...");
        System.Net.IPAddress? ip = await ResolveAsync(address).ConfigureAwait(false);
        if (ip == null) return (null, "Could not find that address. Check the spelling and that Tailscale is running on both computers - or use the host's Tailscale number instead (it starts with 100.).");

        status?.Invoke("Connecting...");
        var client = new FDG.Network.Connection.FDGClient();
        bool connected;
        try { connected = await client.ConnectAsync(ip, HostStarter.Port).ConfigureAwait(false); }
        catch (Exception ex) { Console.WriteLine($"Join: connect failed: {ex.GetBaseException().Message}"); connected = false; }
        if (!connected) return (null, "Could not connect. Is your friend's game open on its lobby screen?");

        // The two-step handshake (#075): the client greets with its version + data fingerprint, the host either
        // hands out a PlayerID or says why not (a different build of the game, a game already running).
        status?.Invoke("Joining the lobby...");
        var vm = new LobbyViewModel_Client(name, client, password: "");
        Task<string?> join = vm.JoinResultTask;
        if (await Task.WhenAny(join, Task.Delay(TimeSpan.FromSeconds(JoinTimeoutSeconds))).ConfigureAwait(false) != join)
        {
            vm.Dispose();
            client.Disconnect();
            return (null, "Timed out waiting for the host to let you in.");
        }
        if (await join.ConfigureAwait(false) is { } reject)
        {
            vm.Dispose();
            client.Disconnect();
            return (null, reject);
        }
        // Remembered only once the host lets you in, so a typo doesn't replace the last good address.
        UserConfig.Current.LastHostAddress = address.Trim();
        UserConfig.Save();
        return (new LobbySession(vm), "");
    }

    public const int JoinTimeoutSeconds = 15;

    /// <summary>An IP passes straight through; a name is looked up. The host listens on IPv4 only.</summary>
    /// <summary>The address typed or pasted, looked up. Pasted extras are dropped first ("http://", ":7777", a
    /// trailing "/" - Kai's friend, 2026-11-12, got "could not find that address"), then an IPv4 answer is preferred,
    /// falling back to IPv6 (Tailscale gives every PC both).</summary>
    internal static async Task<System.Net.IPAddress?> ResolveAsync(string text)
    {
        string t = FdgRaylib.Config.FriendList.CleanAddress(text);
        if (t.Length == 0) return null;
        if (System.Net.IPAddress.TryParse(t, out var literal)) return literal;
        try
        {
            var all = await System.Net.Dns.GetHostAddressesAsync(t).ConfigureAwait(false);
            return all.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?? all.FirstOrDefault();
        }
        catch { return null; }
    }
}
