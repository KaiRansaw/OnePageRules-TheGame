using System.Numerics;
using FDG;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.EngineInterface;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.Config;
using FdgRaylib.Rendering.Presentation;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using FDG.Rules.Serialization;
using Raylib_cs;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

public class LobbyScreen : IAppScreen
{
    public Action? OnBack;
    public Action<ITableState, Func<PlayerID, Color>, GameLog?, GuiResolverOverlay, GuiOutstandingTaskDisplay, PresentationPlayer, Func<string?>?, GuiPlayerMessageUI>? OnGameLaunched;
    public Action<string>? OnGameEnded;

    // The structured end-of-game record, forwarded from the host's FDGServer (#192). Fires just BEFORE
    // OnGameEnded, while the game state is still whole - #187's recovery save is written from here.
    // Never fires on a client (only the host has an FDGServer).
    public Action<GameResult>? OnGameCompleted;

    private ILobbyViewModel? _viewModel;
    private string _chatInput = "";

    private IFDGGame? _pendingGame;

    private static readonly FileFilter ArmyFilter = new(
        $"Army List (*{ArmyListFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ArmyListFile.EXTENSION_WITH_PERIOD}" });

    private static readonly FileFilter TerrainFilter = new(
        $"Terrain Layout (*{TerrainLayoutFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{TerrainLayoutFile.EXTENSION_WITH_PERIOD}" });

    private string? _lastLaunchError;
    private IReadOnlyList<string> _launchProblems = Array.Empty<string>();

    // #372/#388 starter armies. The folder scan is process-wide and starts the first time a lobby opens
    // (SetViewModel touches it), because the armies folder doesn't change under us and re-reading 12 MB
    // of JSON per lobby would be waste. The ROTATION is per-lobby, so a fresh lobby re-rolls freely.
    private static readonly Lazy<ArmyCatalog> SharedArmyCatalog =
        new(() => { ArmyPaths.EnsureGameTypeFolders(); return new ArmyCatalog(); },
            LazyThreadSafetyMode.ExecutionAndPublication);

    private BotArmyPicker? _botArmyPicker;

    // Slots this lobby has already handed a starter army. Keyed by PlayerID rather than by "does the
    // slot have an army" so that a re-roll, or a hand-picked Load Army, is not immediately overwritten
    // on the next frame - and so a slot whose roll found nothing is asked once, not every frame.
    private readonly HashSet<Guid> _autoArmiedSlots = new();

    // Host connection info shown so the host knows what address to share (QF9). The port mirrors
    // CommandProtocol.TEMP_PORT (internal to the engine assembly, so it's restated here).
    private const int ListenPort = 6389;
    private string? _lanAddresses;                 // computed once, lazily
    private volatile string _publicAddress = "checking...";
    private bool _publicFetchStarted;

    // Light-blue accent used to make section/column headers read as headers. Shared with the
    // Host/Client dialogs via ImGuiTheme so every screen uses one accent.
    private static readonly Vector4 HeaderAccent = ImGuiTheme.HeaderAccent;

    /// <summary>
    /// Starts the game this lobby launched, if one is waiting. Normally done by <see cref="Draw"/>; Take
    /// Back calls it directly so its relaunch takes over the board without the lobby ever being shown.
    /// Main thread only.
    /// </summary>
    public void ConsumePendingLaunch()
    {
        IFDGGame? game = _pendingGame;
        if (game == null) return;
        _pendingGame = null;
        HandleLaunch(game);
    }

    public void SetViewModel(ILobbyViewModel viewModel)
    {
        _viewModel?.Dispose();
        _viewModel = viewModel;
        _chatInput = "";
        _pendingGame = null;

        // #372: start the armies scan as soon as a lobby exists (it runs on a background task) and give
        // this lobby its own rotation, so re-opening one starts the army cycle over.
        _botArmyPicker = null;
        _autoArmiedSlots.Clear();
        _resetSections = true;   // Connection closed, the rest open, every time the lobby opens
        _ = SharedArmyCatalog.Value;
        viewModel.OnLaunched += game => _pendingGame = game;
        // Fires on the engine thread; the renderer just records it and reacts on the main thread.
        viewModel.OnGameEnded += result => OnGameEnded?.Invoke(result);
        // Also the engine thread, and deliberately handled synchronously there: the recovery save must be
        // taken before anything reacts to the game ending (#187).
        viewModel.OnGameCompleted += result => OnGameCompleted?.Invoke(result);

        // #310: a fresh host lobby opens on the settings the last hosted game used (saved config), not
        // on the engine defaults. No-op for a client or a resume - ApplyTo gates on both, since a
        // resumed game launches from its own saved settings.
        UserConfig.Current.HostSettings.ApplyTo(viewModel);

        // Pick a random terrain type (cosmetic table surface) each time a fresh lobby opens, so every
        // new game leads with a different-looking board instead of always Forest. Host-only (the setting
        // is host-owned and broadcast to clients) and skipped on resume (saved games keep their surface).
        // Deliberately NOT remembered in the config (#310) - randomizing it is the whole point.
        if (viewModel.HasHostPrivileges && !viewModel.IsResumeMode)
        {
            var backgrounds = Enum.GetValues<ETableBackground>();
            viewModel.SetTableBackground(backgrounds[Random.Shared.Next(backgrounds.Length)]);
        }
    }

    public void Draw(int screenW, int screenH)
    {
        // Consume a pending launch on the main thread
        ConsumePendingLaunch();

        if (_viewModel == null) return;

        DrawScaled(screenW, screenH);

        // Map maker (Custom Map > Create / Edit): drawn over the whole lobby while open.
        if (_mapEditor.IsOpen) _mapEditor.Draw(screenW, screenH);
    }

    private readonly MapEditor _mapEditor = new();
    private string? _mapPendingDelete;

    // How much smaller the window is than the monitor the fonts were baked for, clamped to
    // [0.5, 1]. 1 when fullscreen/maximized; shrinks proportionally as the window does.
    private static float WindowToMonitorScale(int screenW, int screenH)
    {
        int monitor = Raylib.GetCurrentMonitor();
        float monW = Raylib.GetMonitorWidth(monitor);
        float monH = Raylib.GetMonitorHeight(monitor);
        if (monW <= 0f || monH <= 0f) return 1f;
        return Math.Clamp(MathF.Min(screenW / monW, screenH / monH), 0.5f, 1f);
    }

    private void DrawScaled(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        // Clean-dark lobby look: softer corners and a little more breathing room, scoped to this window.
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 7f));
        ImGui.Begin("Lobby",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar);

        // Fonts are baked for the monitor at startup and the panel scales below are tuned for a
        // fullscreen window; a smaller window scales the lobby down proportionally instead of
        // overflowing (truncated columns, oversized rows). This must ride the per-window
        // SetWindowFontScale calls: io.FontGlobalScale is latched at NewFrame, so setting it inside
        // Draw and restoring before returning never rendered (the old 1.2x was a silent no-op).
        float ws         = WindowToMonitorScale(screenW, screenH);
        float panelScale = 1.5f * ws;  // the "~50% bigger" panels, as tuned at fullscreen

        float margin       = 10f;
        float baseFontSize = ImGui.GetFontSize();       // baked font px, unaffected by ws
        float fontSize     = baseFontSize * ws;         // layout metric the lobby's rows derive from
        float framePadY    = ImGui.GetStyle().FramePadding.Y;
        float naturalBtnH  = fontSize + framePadY * 2;
        float headerH      = MathF.Max(40f, naturalBtnH + 12f);
        // The bottom action bar (LAUNCH, and why it is greyed) ~50% taller than a standard button, so
        // the one control that starts the game is the easiest thing on the screen to hit.
        float actionH      = MathF.Max(45f, (naturalBtnH + 6f) * 1.5f);

        // Three columns, the way the screen reads: what you SET on the left (mode, points, table,
        // rules), WHO is playing and with which army in the wide middle, and WHERE you will fight on
        // the right. Previously the settings were a right-hand rail and the map was a quarter-panel
        // sharing the bottom with chat; the map is a thing you look at while choosing armies, so it
        // gets a column of its own and the player rows get the height chat used to take.
        // Floors keep the side columns usable when the window is small; the middle absorbs the rest.
        float leftW        = MathF.Max(280f, screenW * 0.24f);
        float rightW       = MathF.Max(300f, screenW * 0.26f);
        // Clamped, not just subtracted: at a small enough window the two floored side columns can eat
        // the whole width, and a negative child size means "fill minus this" to ImGui, which would
        // silently invert the layout rather than overflow visibly.
        float midW         = MathF.Max(240f, screenW - leftW - rightW - margin * 4f);
        float headerW      = screenW - margin * 2f;
        float contentY     = margin + headerH + margin;
        float contentH     = MathF.Max(80f, screenH - contentY - margin - actionH - margin);

        // ── Header ────────────────────────────────────────────────────────────
        ImGui.SetCursorPos(new Vector2(margin, margin));
        // NoScrollbar: the Back button + window padding just overflow headerH, which would otherwise
        // draw a phantom scrollbar right next to Back.
        ImGui.BeginChild("##header", new Vector2(headerW, headerH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        float headerFontH  = headerH * 0.65f;
        float headerScale  = headerFontH / baseFontSize; // window-scale factor that renders at headerFontH px
        ImGui.SetWindowFontScale(headerScale);
        ImGui.SetCursorPosY((headerH - headerFontH) * 0.5f);
        // Draw() has already returned if there is no view model; the ! restates that for the null
        // analysis, which cannot see across the call.
        ImGui.TextUnformatted(_viewModel!.ServerName);
        // One-line summary of the game being set up, readable even with the settings collapsed.
        ImGui.SameLine(0f, 24f * ws);
        ImGui.SetWindowFontScale(headerScale * 0.7f);
        ImGui.SetCursorPosY((headerH - headerFontH * 0.7f) * 0.5f);
        UiText.Disabled(GameSummary());
        ImGui.SetWindowFontScale(ws);

        // CalcTextSize ignores SetWindowFontScale, so apply ws to the measured width by hand.
        float backW = (ImGui.CalcTextSize("Back").X + 36f) * ws;
        float backH = headerH - 8f;
        ImGui.SetCursorPos(new Vector2(headerW - backW - 4f, 4f));
        if (UiButton.Back("Back", new Vector2(backW, backH)))
        {
            PersistHostSettings();
            OnBack?.Invoke();
        }

        ImGui.EndChild();

        // ── Left column: settings ─────────────────────────────────────────────
        ImGui.SetCursorPos(new Vector2(margin, contentY));
        ImGui.BeginChild("##settings", new Vector2(leftW, contentH),
            ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        ImGui.SetWindowFontScale(panelScale);  // settings fields ~50% bigger than the rest of the lobby
        DrawSettings(leftW);
        ImGui.EndChild();
        _resetSections = false;

        // ── Middle column: players and their armies ───────────────────────────
        float midX = margin * 2f + leftW;
        ImGui.SetCursorPos(new Vector2(midX, contentY));
        ImGui.BeginChild("##players", new Vector2(midW, contentH), ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(panelScale);  // rows + font ~50% bigger than the rest of the lobby
        DrawPlayerList(midW, panelScale);
        ImGui.EndChild();

        // ── Right column: map preview (2D) ────────────────────────────────────
        float rightX = margin * 3f + leftW + midW;
        ImGui.SetCursorPos(new Vector2(rightX, contentY));
        ImGui.BeginChild("##mappreview", new Vector2(rightW, contentH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetWindowFontScale(panelScale * 0.8f);
        DrawMapPreview();
        ImGui.EndChild();

        // ── Action bar: why LAUNCH is greyed, then LAUNCH ─────────────────────
        IReadOnlyList<string> launchBlockers = _viewModel.HasHostPrivileges && !_viewModel.IsResumeMode
            ? _viewModel.BlockingLaunchProblems()
            : Array.Empty<string>();

        float actionY  = screenH - margin - actionH;
        float launchW  = MathF.Max(leftW, screenW * 0.22f);
        float blockerW = screenW - margin * 2f - launchW - margin;

        if (launchBlockers.Count > 0 && blockerW > 0f)
        {
            ImGui.SetCursorPos(new Vector2(margin, actionY));
            ImGui.BeginChild("##blockers", new Vector2(blockerW, actionH), ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.SetWindowFontScale(panelScale * 0.8f);
            ImGui.PushStyleColor(ImGuiCol.Text, OverPointsColor);
            string more = launchBlockers.Count > 1 ? $"  (+{launchBlockers.Count - 1} more - hover LAUNCH)" : "";
            UiText.Wrapped("Can't launch yet: " + launchBlockers[0] + more);
            ImGui.PopStyleColor();
            ImGui.EndChild();
        }

        ImGui.SetCursorPos(new Vector2(screenW - margin - launchW, actionY));
        // No border on the launch child — the button itself fills the panel and provides the visual edge.
        ImGui.BeginChild("##launch", new Vector2(launchW, actionH), ImGuiChildFlags.None);
        ImGui.SetWindowFontScale(panelScale);  // match the other panels
        DrawLaunch(launchW, actionH);
        ImGui.EndChild();

        ImGui.End();
        ImGui.PopStyleVar(5);
    }

    private void DrawPlayerList(float panelW, float panelScale)
    {
        AutoArmyNewSlots();

        IReadOnlyList<LobbyPlayerInfoSummary> players = _viewModel!.PlayerInfos;

        // Rows 50% taller: the extra height comes from cell padding (applied top+bottom each row).
        Vector2 cellPad = ImGui.GetStyle().CellPadding;
        float rowH = ImGui.GetTextLineHeight() + cellPad.Y * 2f;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(cellPad.X, cellPad.Y + rowH * 0.25f));

        // #221: effective colour per row this frame, resolved from the SYNCED picks (ColorIndex rides
        // LobbyPlayerInfoSummary through the lobby protocol, so every machine computes the same result) -
        // explicit picks win, unpicked rows fill with the free defaults in palette order.
        var chosen = new int?[players.Count];
        for (int i = 0; i < players.Count; i++)
            chosen[i] = players[i].ColorIndex >= 0 ? players[i].ColorIndex : null;
        int[] effectiveColor = PlayerColorOptions.ResolveIndices(chosen);

        if (ImGui.BeginTable("##ptable", 7,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name",    ImGuiTableColumnFlags.WidthStretch, 0.17f);
            ImGui.TableSetupColumn("Army",    ImGuiTableColumnFlags.WidthStretch, 0.32f);
            ImGui.TableSetupColumn("Faction", ImGuiTableColumnFlags.WidthStretch, 0.13f);
            ImGui.TableSetupColumn("Pts",     ImGuiTableColumnFlags.WidthStretch, 0.055f);
            ImGui.TableSetupColumn("Team",    ImGuiTableColumnFlags.WidthStretch, 0.12f);
            ImGui.TableSetupColumn("Color",   ImGuiTableColumnFlags.WidthStretch, 0.12f);
            // #372: every row now carries Load Army AND Random Army, so Actions takes the width back
            // from Army/Faction (both of which wrap gracefully; a clipped button does not).
            // #405: Remove makes it three buttons on most rows, hence the extra width. The weights are
            // normalized by SizingStretchProp, so raising this one narrows the rest proportionally.
            ImGui.TableSetupColumn("",        ImGuiTableColumnFlags.WidthStretch, 0.09f);
            ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
            ImGui.TableHeadersRow();
            ImGui.PopStyleColor();

            for (int i = 0; i < players.Count; i++)
            {
                LobbyPlayerInfoSummary info = players[i];
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                TextFitted(info.PlayerName, panelScale);
                if (info.PlayerType != EPlayerType.Local)
                {
                    // The old Type column, folded into a small tag after the name.
                    ImGui.SameLine();
                    ImGui.SetWindowFontScale(panelScale * 0.65f);
                    UiText.Disabled(PlayerTypeLabel(info.PlayerType).ToUpperInvariant());
                    ImGui.SetWindowFontScale(panelScale);
                }

                ImGui.TableNextColumn();
                DrawArmyPicker(i, info, panelScale);

                ImGui.TableNextColumn();
                DrawFactionCell(info.ArmyListSummary, _viewModel.AllowedGameSystems, panelScale);

                ImGui.TableNextColumn();
                DrawPointsCell(info.ArmyListSummary, GameSystemInfo.IsQuest(_viewModel.GameSystem) ? int.MaxValue : _viewModel.ArmyPoints);

                ImGui.TableNextColumn();
                DrawTeamCell(i, info, players.Count);

                ImGui.TableNextColumn();
                DrawColorCell(i, info, players, effectiveColor);

                ImGui.TableNextColumn();
                if (_viewModel.IsResumeMode)
                {
                    // Re-crew a saved slot. Host only; Local/bots today (networked client assignment TBD).
                    ImGui.BeginDisabled(!_viewModel.HasHostPrivileges);
                    if (UiButton.NavigateSmall($"Local##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.Local);
                    ImGui.SameLine();
                    if (UiButton.NavigateSmall($"Tactician##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.Tactician);
                    ImGui.SameLine();
                    if (UiButton.NavigateSmall($"Strategist##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.Strategist);
                    ImGui.SameLine();
                    if (UiButton.NavigateSmall($"DerpBot##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.SoloRules);
                    ImGui.EndDisabled();
                }
                else
                {
                    // Armies are picked from the Army column's dropdown (the Game Type's folder).
                    // #405: drop the slot entirely. Shown only where it is actually allowed rather than
                    // greyed out - unlike Random Army, an absent Remove needs no explaining, and a
                    // disabled one on the host's OWN row would read as a bug. The engine owns the policy
                    // (CheckCanRemovePlayer): not the host's own row, not a connected client, never in a
                    // resumed or launched lobby, and never on a client's screen.
                    //
                    // Removing mid-loop is safe: RemovePlayer publishes a NEW list, so `players` - the
                    // snapshot this frame is iterating - is unaffected, and the row simply vanishes next
                    // frame.
                    if (_viewModel.CheckCanRemovePlayer(info.PlayerID))
                    {
                        if (UiButton.NavigateSmall($"Remove##{i}"))
                            _viewModel.RemovePlayer(info.PlayerID);
                        if (ImGui.IsItemHovered())
                            UiText.Tooltip($"Remove {info.PlayerName} from the lobby.");
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar(); // CellPadding (taller rows)

        // Slots are fixed when resuming a saved game, so no add/remove there.
        if (_viewModel.HasHostPrivileges && !_viewModel.IsResumeMode)
        {
            ImGui.Spacing();
            if (UiButton.Navigate("Add Local Player"))
                _viewModel.AddLocalPlayer();
            // #405: what each of these four actually gets you. The bot rungs in particular were
            // indistinguishable from their names alone - "Tactician" and "Strategist" say nothing about
            // which is harder, or that one of them thinks for a while before it moves.
            if (ImGui.IsItemHovered())
                UiText.Tooltip(LocalPlayerTooltip);
            ImGui.SameLine();
            // #191 A6/B5: three bot flavors, one per ladder rung - the Strategist (searches ahead),
            // the Tactician (challenge AI, one ply), and the legacy solo-rules bot, rechristened
            // DerpBot (Chris's naming). Every rung stays selectable forever (plan G4).
            if (GameSystemInfo.IsQuest(_viewModel.GameSystem))
            {
                // Star Quest: one bot, playing the Star Quest AI rules.
                if (UiButton.Navigate("Add Star Quest AI"))
                    _viewModel.AddAiPlayer(EAiProfile.StarQuest);
                if (ImGui.IsItemHovered())
                    UiText.Tooltip("The enemy side, following the Star Quest AI flowchart:\nshoot or charge the closest hero it can see,\notherwise head for its AI Goal.");
                return;
            }
            if (UiButton.Navigate("Add Strategist Bot"))
                _viewModel.AddAiPlayer(EAiProfile.Strategist);
            if (ImGui.IsItemHovered())
                UiText.Tooltip(StrategistBotTooltip);
            ImGui.SameLine();
            if (UiButton.Navigate("Add Tactician Bot"))
                _viewModel.AddAiPlayer(EAiProfile.Tactician);
            if (ImGui.IsItemHovered())
                UiText.Tooltip(TacticianBotTooltip);
            ImGui.SameLine();
            if (UiButton.Navigate("Add DerpBot"))
                _viewModel.AddAiPlayer(EAiProfile.SoloRules);
            if (ImGui.IsItemHovered())
                UiText.Tooltip(DerpBotTooltip);
        }
    }

    // #372/#388 ---------------------------------------------------------------------------------------
    // Starter armies. Every new slot, bot or human, arrives with no army at all (#402 stopped the engine
    // from stamping bots with a "Test Army" stub), so the lobby fills one in from the armies folder as
    // soon as the scan is in. App-side on purpose: the armies FOLDER is an app concept (ArmyPaths), and
    // the engine has no business reading the user's disk. A slot the roll cannot fill stays visibly
    // empty and blocks the launch, rather than quietly playing a list nobody picked.

    private static bool ArmyCatalogReady => SharedArmyCatalog.Value.IsLoaded;

    // #405 (Chris's wording). UiText.Tooltip does not wrap, so these are hard-wrapped, same as
    // RandomArmyTooltip below. ASCII only - the ImGui atlas bakes no glyph past U+00FF.
    internal const string LocalPlayerTooltip =
        "Adds a human that's playing on the same computer as you. Trade seats\n" +
        "when the game says it's their turn.";

    internal const string StrategistBotTooltip =
        "Very smart bot. Can think many moves ahead and picks moves that give\n" +
        "it a long-term advantage. Takes a little time to make each move,\n" +
        "depending on your CPU speed.";

    internal const string TacticianBotTooltip =
        "Somewhat challenging bot. Considers the state of the board and runs\n" +
        "lots of simulations to decide how to move, but can't plan ahead.";

    internal const string DerpBotTooltip =
        "Based off the solo play rules from OnePageRules. Very dumb. Can\n" +
        "occasionally beat a human through sheer luck.";

    /// <summary>#405: the four add-button tooltips, so the ASCII guard can sweep them all.</summary>
    internal static IReadOnlyList<string> AddPlayerTooltips { get; } = new[]
    {
        LocalPlayerTooltip, StrategistBotTooltip, TacticianBotTooltip, DerpBotTooltip,
    };

    /// <summary>Why the Random Army button is or isn't available on a given row.</summary>
    private static string RandomArmyTooltip(bool canModify, LobbyPlayerInfoSummary info)
    {
        if (!canModify)
            return $"{info.PlayerName} picks their own army - only they can change it.";
        if (!ArmyCatalogReady)
            return "Reading the armies folder...";
        return "Roll another army from the armies folder, closest to the points limit\n" +
               "first. Only armies this lobby's Army Source setting accepts. Skips armies\n" +
               "other players are using, never repeats one until this slot has seen them\n" +
               "all, and never picks one over the limit unless the folder has nothing legal.";
    }

    /// <summary>#388: does this row still need a starter army rolled onto it? Pure, so the rule is
    /// testable away from ImGui.</summary>
    /// <param name="canModify">Whether this machine may write the slot's army at all
    /// (<see cref="ILobbyViewModel.CheckCanModifyPlayerIDInfo"/>): the host owns its own row, its local
    /// humans and the bots, and a client owns only its own row. Nobody rolls for a remote player.</param>
    /// <param name="alreadyServed">The slot has been handed an army - rolled or hand-picked - this
    /// lobby.</param>
    internal static bool NeedsStarterArmy(EPlayerType playerType, bool armyAssigned, bool canModify,
        bool alreadyServed)
    {
        if (!canModify || alreadyServed) return false;

        // A human holding a list keeps it: an army loaded before the folder scan came back, or one that
        // rode in with the slot. A bot is re-served regardless of what it holds - alreadyServed is what
        // tells a fresh bot apart, and it is the only gate a bot ever needed even now that a fresh one
        // arrives unassigned like everyone else (#402).
        return playerType == EPlayerType.AI || !armyAssigned;
    }

    /// <summary>Hands a starter army to every slot this machine owns and hasn't served yet: on the host
    /// its own row, its local humans and the bots; on a client, its own row (#388 - bots only before).
    /// A remote player's row is never touched, and the whole pass is skipped when resuming, where the
    /// saved armies are the point. No-op until the background scan finishes, and no-op forever if there
    /// is no armies folder to scan.</summary>
    private void AutoArmyNewSlots()
    {
        if (_viewModel!.IsResumeMode) return;
        if (!ArmyCatalogReady) return;

        IReadOnlyList<LobbyPlayerInfoSummary> players = _viewModel.PlayerInfos;

        foreach (LobbyPlayerInfoSummary info in players)
        {
            // Star Quest: a human row plays a party of heroes, picked by hand - no starter army.
            if (GameSystemInfo.IsQuest(_viewModel.GameSystem) && info.PlayerType != EPlayerType.AI) continue;
            if (!NeedsStarterArmy(info.PlayerType, info.ArmyListSummary.IsAssigned,
                    _viewModel.CheckCanModifyPlayerIDInfo(info.PlayerID),
                    _autoArmiedSlots.Contains(info.PlayerID.ID)))
            {
                continue;
            }

            AssignRandomArmy(info.PlayerID); //Marks the slot served.
        }

        // A slot that left must not keep its rotation alive. Pruned unconditionally rather than behind a
        // count comparison: a bot leaving as a human joins keeps the count level, and the roster is at
        // most eight rows, so there is nothing to save by guessing.
        var live = players.Select(p => p.PlayerID.ID).ToHashSet();
        foreach (Guid gone in _autoArmiedSlots.Where(id => !live.Contains(id)).ToList())
        {
            _autoArmiedSlots.Remove(gone);
            _botArmyPicker?.Forget(gone);
        }
    }

    /// <summary>Picks and loads the next army for one slot - one being seeded, or any player pressing
    /// Random Army. Silently does nothing when the folder holds no readable armies, or when the chosen
    /// file fails to load, so the slot keeps whatever it had.</summary>
    private void AssignRandomArmy(PlayerID playerID)
    {
        // A rolled army is a deliberate pick, exactly like a hand-loaded one: mark the slot served so
        // AutoArmyNewSlots cannot overwrite the roll on the next frame. It is also what keeps a client
        // from rolling its own row twice while the army it just sent is still in flight to the host.
        _autoArmiedSlots.Add(playerID.ID);

        GameTypeArmies();   // (re)builds the picker for the current Game Type's folder
        if (_botArmyPicker == null) return;

        // What everyone ELSE is holding, by the same name|faction|points identity the catalog uses - the
        // roster is the only view that covers remote clients, whose army file path never crosses the wire.
        // Read fresh rather than from the caller's snapshot (#388): the host applies an army update to
        // its roster synchronously, so when one pass seeds several slots at once - the lobby's own row
        // and a bot that was added before the folder scan landed - each pick sees the ones before it and
        // they no longer all land on the same army.
        var inUseByOthers = _viewModel!.PlayerInfos
            .Where(p => p.PlayerID != playerID && p.ArmyListSummary.IsAssigned)
            .Select(p => ArmyCatalogEntry.KeyFor(
                p.ArmyListSummary.ArmyName, p.ArmyListSummary.FactionName, p.ArmyListSummary.PointCost))
            .ToHashSet();

        if (_botArmyPicker.PickNext(playerID.ID, _viewModel.ArmyPoints,
                _viewModel.AllowedGameSystems, inUseByOthers) is not { } pick)
        {
            // #402: also the "nothing of an allowed system in the folder" case. Left silent for the same
            // reason as an empty folder - the slot keeps whatever it had, and the roster's red Army cell
            // plus the greyed LAUNCH button already say what is missing.
            return;
        }

        if (LoadArmyFile(pick.Path) is { } army) _viewModel.UpdateArmyListFile(playerID, army);
    }

    /// <summary>Reads a .fdgarmy off disk, or null if it can't be read. Deserialized as BuiltArmyFile so a
    /// Forge-built army keeps its embedded book + selections - the #153 launch gate validates them
    /// host-side. A hand-authored army just leaves them null.</summary>
    private static FDG.ArmyBuilding.BuiltArmyFile? LoadArmyFile(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<FDG.ArmyBuilding.BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly Vector4 OverPointsColor  = new(1f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 UnderPointsColor = new(1f, 0.85f, 0.25f, 1f);

    // The Pts cell. Red over the limit (the #153 launch gate flags it), yellow well under it. The rule
    // itself is LobbyPointsStatus.Classify so it can be unit-tested away from ImGui.
    private static void DrawPointsCell(ArmyListSummary summary, int pointsLimit)
    {
        ELobbyPointsStatus status =
            LobbyPointsStatus.Classify(summary.IsAssigned, summary.PointCost, pointsLimit);

        if (status != ELobbyPointsStatus.Ok)
            ImGui.PushStyleColor(ImGuiCol.Text,
                status == ELobbyPointsStatus.Over ? OverPointsColor : UnderPointsColor);
        ImGui.TextUnformatted(summary.PointCost.ToString());
        if (status != ELobbyPointsStatus.Ok) ImGui.PopStyleColor();

        if (ImGui.IsItemHovered()
            && LobbyPointsStatus.Tooltip(status, summary.PointCost, pointsLimit) is string tip)
        {
            UiText.Tooltip(tip);
        }
    }

    // #402: the two roster cells the army gate's blocking half can light up. Same shape as the Pts cell -
    // red text plus a tooltip saying what is wrong and that it stops the launch - so all three blockers
    // read the same way on the row and in the greyed LAUNCH button's tooltip.

    /// <summary>The Army cell. Red when the slot carries no army at all - except on a resume, where
    /// every slot reports none and the save holds the real ones.</summary>
    // #405 -----------------------------------------------------------------------------------------
    // Squishing text to fit its cell. The roster's three text columns (Name / Army / Faction) hold names
    // the player chose, so there is no width that always fits - "Human Defense Force" clipped at Chris's
    // resolution. Rather than truncate (which hides the end of the name) or wrap (which makes one long
    // name grow every row), the text shrinks just enough to fit and stops at a floor, below which it
    // would be unreadable and clipping is the lesser evil.

    /// <summary>#405: the Type cell's label. <see cref="EPlayerType.AI"/> reads as "Bot" - the enum name
    /// is an engine-side detail, and "Bot" is what the button that adds one calls it.</summary>
    internal static string PlayerTypeLabel(EPlayerType type) => type switch
    {
        EPlayerType.AI => "Bot",
        _ => type.ToString(),
    };

    /// <summary>How far text may shrink before clipping is preferable, as a fraction of the row's
    /// normal size.</summary>
    private const float MinFitScale = 0.65f;

    /// <summary>
    /// #405: the font scale that fits <paramref name="renderedTextWidth"/> into
    /// <paramref name="availableWidth"/>. Never above <paramref name="baseScale"/> (text that already
    /// fits is never enlarged) and never below <see cref="MinFitScale"/> of it.
    ///
    /// <para>Both widths are REAL SCREEN PIXELS. <c>CalcTextSize</c> honours
    /// <c>SetWindowFontScale</c> - it and <c>GetFontSize</c> both read the same context font size, so
    /// they cannot disagree - which makes its result the width the text actually renders at, and the
    /// quotient below a factor to apply TO the current scale rather than a scale in itself. (The
    /// "CalcTextSize ignores SetWindowFontScale" comments elsewhere in this file are wrong; measured
    /// 2026-09-12, a doubled window scale doubles the reported width exactly.)</para>
    /// </summary>
    internal static float FitFontScale(float renderedTextWidth, float availableWidth, float baseScale)
    {
        // Nothing to measure against: leave the row alone rather than divide by zero.
        if (renderedTextWidth <= 0f || availableWidth <= 0f || baseScale <= 0f)
            return baseScale;

        if (renderedTextWidth <= availableWidth)
            return baseScale; // already fits at full size

        float fitted = baseScale * (availableWidth / renderedTextWidth);
        return MathF.Max(fitted, baseScale * MinFitScale);
    }

    /// <summary>Draws one line of text, shrunk to fit the remaining cell width when it would clip, and
    /// kept vertically centred on the line it would have occupied at full size - a shrunk line is
    /// shorter than the row, so left alone it rides the top of the cell and reads as misaligned.
    /// Leaves the item on the stack as TextUnformatted would, so callers keep their hover tooltips.</summary>
    private static void TextFitted(string text, float baseScale)
    {
        float scale = FitFontScale(ImGui.CalcTextSize(text).X, ImGui.GetContentRegionAvail().X, baseScale);

        if (scale == baseScale)
        {
            ImGui.TextUnformatted(text);
            return;
        }

        float fullLineH = ImGui.GetTextLineHeight();   // the height this row is sized for
        ImGui.SetWindowFontScale(scale);
        float shrunkLineH = ImGui.GetTextLineHeight(); // GetTextLineHeight tracks the scale, as above

        // Half the slack above, half below. The total (offset + shrunk line) stays under the full line
        // height, so centring never grows the row.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (fullLineH - shrunkLineH) * 0.5f);
        ImGui.TextUnformatted(text);

        ImGui.SetWindowFontScale(baseScale);
    }

    // ── Army dropdown (per Game Type folder) ─────────────────────────────────────────────────────────
    private EGameSystem? _pickerSystem;
    private IReadOnlyList<ArmyCatalogEntry>? _gameTypeArmies;

    /// <summary>The armies in the current Game Type's folder (armies/GDF, armies/GF Firefight, ...),
    /// sorted by name. Empty until the folder scan is in.</summary>
    private IReadOnlyList<ArmyCatalogEntry> GameTypeArmies()
    {
        if (!ArmyCatalogReady) return Array.Empty<ArmyCatalogEntry>();
        EGameSystem system = _viewModel!.GameSystem;
        if (_gameTypeArmies == null || _pickerSystem != system)
        {
            // Star Quest: the enemies are ordinary Grimdark Future armies (the heroes come from the party picker).
            _gameTypeArmies = ArmyPaths.EntriesFor(GameSystemInfo.IsQuest(system) ? EGameSystem.GrimdarkFuture : system,
                    SharedArmyCatalog.Value.Entries)
                // Grouped by 1000-point bracket (0-1000, 1001-2000, ...), then A-Z inside each bracket.
                .OrderBy(e => e.Points <= 0 ? 0 : (e.Points - 1) / 1000)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (_pickerSystem != null && _pickerSystem != system) OnGameTypeChanged();
            _pickerSystem = system;
            _botArmyPicker = null;
        }
        _botArmyPicker ??= new BotArmyPicker(_gameTypeArmies);
        return _gameTypeArmies;
    }

    /// <summary>The Game Type changed: every row this machine owns gets an army from the new folder.</summary>
    private void OnGameTypeChanged()
    {
        if (_viewModel!.IsResumeMode) return;
        foreach (LobbyPlayerInfoSummary info in _viewModel.PlayerInfos)
            if (_viewModel.CheckCanModifyPlayerIDInfo(info.PlayerID))
                _autoArmiedSlots.Remove(info.PlayerID.ID);
    }

    /// <summary>The Army cell: a dropdown of the Game Type folder's armies for rows this machine may change,
    /// otherwise just the army's name.</summary>
    private void DrawArmyPicker(int row, LobbyPlayerInfoSummary info, float baseScale)
    {
        ArmyListSummary summary = info.ArmyListSummary;
        bool canModify = !_viewModel!.IsResumeMode && _viewModel.CheckCanModifyPlayerIDInfo(info.PlayerID);
        if (!canModify)
        {
            DrawArmyCell(summary, _viewModel.IsResumeMode, baseScale);
            return;
        }

        if (GameSystemInfo.IsQuest(_viewModel.GameSystem) && info.PlayerType != EPlayerType.AI)
        {
            DrawPartyPicker(row, info);
            return;
        }

        IReadOnlyList<ArmyCatalogEntry> armies = GameTypeArmies();
        bool missing = LobbyArmySource.IsMissingArmy(summary, false);
        string currentKey = summary.IsAssigned
            ? ArmyCatalogEntry.KeyFor(summary.ArmyName, summary.FactionName, summary.PointCost)
            : "";

        // The Army cell is a BUTTON, not a dropdown: a dropdown gives one narrow column of names and
        // nowhere to search, which stops working somewhere around a dozen lists. Clicking it hands the
        // choice to the full-screen picker, which has room for a search box, a faction filter and a
        // preview of the list you are about to take.
        string cellLabel = summary.IsAssigned ? summary.ArmyName : "Choose an army...";
        if (missing) ImGui.PushStyleColor(ImGuiCol.Text, OverPointsColor);
        bool pressed = UiButton.Navigate($"{cellLabel}##army{row}", new Vector2(-1f, 0f));
        if (missing) ImGui.PopStyleColor();
        if (pressed)
        {
            // Snapshot what the picker needs before leaving the lobby, so it never reaches back into
            // a view model that could be torn down while it is on screen.
            _pickingFor      = info.PlayerID;
            PickPlayerName   = info.PlayerName;
            PickArmies       = armies;
            PickPointsLimit  = _viewModel.ArmyPoints;
            PickCurrentKey   = currentKey;
            OnChooseArmyRequested?.Invoke();
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip($"Choose this player's army from armies\\{ArmyPaths.FolderNameFor(_viewModel.GameSystem)}.");
    }

    // ── Handing the army choice to the Choose Army screen ────────────────────────────────────────────

    /// <summary>A player's Army cell was clicked: show the full-screen picker. Program.cs opens it with
    /// the Pick* snapshot below and calls <see cref="ApplyPickedArmy"/> when a list comes back.</summary>
    public Action? OnChooseArmyRequested;

    /// <summary>Which slot is waiting on the picker. Null when nothing is.</summary>
    private PlayerID? _pickingFor;

    /// <summary>The waiting slot's player name, for the picker's heading.</summary>
    public string PickPlayerName { get; private set; } = "";

    /// <summary>The armies the picker should list - the current Game Type's folder.</summary>
    public IReadOnlyList<ArmyCatalogEntry> PickArmies { get; private set; } = Array.Empty<ArmyCatalogEntry>();

    /// <summary>The game's points limit, so the picker can mark lists that exceed it.</summary>
    public int PickPointsLimit { get; private set; }

    /// <summary>The army the waiting slot already has, so the picker opens on it.</summary>
    public string PickCurrentKey { get; private set; } = "";

    /// <summary>
    /// A list came back from the Choose Army screen: load it and give it to the slot that asked. A
    /// hand pick is never auto-replaced afterwards, the same as picking from the old dropdown was.
    /// </summary>
    public void ApplyPickedArmy(ArmyCatalogEntry entry)
    {
        if (_pickingFor is not { } playerID || _viewModel == null) { _pickingFor = null; return; }
        if (entry.Key != PickCurrentKey && LoadArmyFile(entry.Path) is { } army)
        {
            _autoArmiedSlots.Add(playerID.ID);
            _viewModel.UpdateArmyListFile(playerID, army);
        }
        _pickingFor = null;
    }

    // ── Star Quest party picker ──────────────────────────────────────────────────────────────────────
    private readonly Dictionary<Guid, List<string>> _partyPicks = new();

    /// <summary>Set by the campaign screen: the next party picker starts with these hero files picked.</summary>
    public static List<string>? PendingParty;

    /// <summary>
    /// #410 - set by the campaign screen when "Play next mission" is pressed: the path of the army file
    /// the host should start the next lobby with. Consumed once by
    /// <see cref="ApplyPendingCampaignArmy"/> and cleared, so a lobby opened any other way is unaffected.
    /// </summary>
    public static string? PendingCampaignArmy;

    /// <summary>
    /// #410 - gives the host's own slot the campaign army the campaign screen chose, and raises the
    /// points limit to fit it, because a campaign game has no points limit of its own.
    /// <para>
    /// Called from Program.cs right after the lobby is created and the game system applied - in that
    /// order, because <see cref="ApplyGameSystem"/> resets both the points and the allowed systems, and
    /// the launch gate refuses an army over the limit or from the wrong system. It lives here rather
    /// than in Program.cs because the file loader and the auto-army marks are private to this screen.
    /// </para>
    /// <para>
    /// The host's own row is <c>PlayerInfos[0]</c>: the host view model creates exactly that one slot in
    /// its constructor, before anything else can add a row, and this runs the moment the lobby exists.
    /// (The host's own slot is otherwise indistinguishable from a hot-seat local player - see #405.)
    /// </para>
    /// </summary>
    public void ApplyPendingCampaignArmy()
    {
        string? path = PendingCampaignArmy;
        PendingCampaignArmy = null;
        if (path == null || _viewModel == null) return;
        if (LoadArmyFile(path) is not { } army) return;
        if (_viewModel.PlayerInfos.Count == 0) return;

        PlayerID host = _viewModel.PlayerInfos[0].PlayerID;
        // Campaign games have no points limit; the lobby still has one, so lift it clear of the army.
        if (army.TotalPoints > _viewModel.ArmyPoints) _viewModel.SetArmyPoints(army.TotalPoints);
        _viewModel.UpdateArmyListFile(host, army);
        // Mark it the way every other deliberate assignment does, so the starter-army roll leaves it be.
        _autoArmiedSlots.Add(host.ID);
    }
    private List<(string Path, FDG.Quest.QuestHero Hero)>? _savedHeroes;
    private double _savedHeroesAt = -10;

    /// <summary>Star Quest: the human row picks 2-4 saved heroes (from Star Quest Heroes) as its party.</summary>
    private void DrawPartyPicker(int row, LobbyPlayerInfoSummary info)
    {
        if (_savedHeroes == null || ImGui.GetTime() - _savedHeroesAt > 3.0)
        {
            _savedHeroes = FdgRaylib.Quest.HeroPaths.LoadAll(HeroBuilderScreen.GameType);
            _savedHeroesAt = ImGui.GetTime();
        }
        if (!_partyPicks.TryGetValue(info.PlayerID.ID, out List<string>? picks))
        {
            _partyPicks[info.PlayerID.ID] = picks = new List<string>();
            if (PendingParty != null)
            {
                // A campaign mission: the campaign's party comes pre-picked.
                picks.AddRange(PendingParty.Where(p => _savedHeroes.Any(s => s.Path == p)).Take(4));
                PendingParty = null;
                if (picks.Count > 0) SendParty(info.PlayerID, picks);
            }
        }
        picks.RemoveAll(p => !_savedHeroes.Any(s => s.Path == p));

        string preview = picks.Count == 0 ? "Choose 2-4 heroes..."
            : string.Join(", ", picks.Select(p => _savedHeroes.First(s => s.Path == p).Hero.Name));
        ImGui.SetNextItemWidth(-1f);
        bool short_ = picks.Count < 2;
        if (short_) ImGui.PushStyleColor(ImGuiCol.Text, OverPointsColor);
        bool open = ImGui.BeginCombo($"##party{row}", preview, ImGuiComboFlags.HeightLarge);
        if (short_) ImGui.PopStyleColor();
        if (open)
        {
            if (_savedHeroes.Count == 0)
                UiText.Disabled("No heroes yet - make some in Star Quest Heroes (main menu).");
            bool changed = false;
            foreach (var (path, hero) in _savedHeroes)
            {
                bool on = picks.Contains(path);
                bool full = !on && picks.Count >= 4;
                ImGui.BeginDisabled(full);
                if (ImGui.Checkbox($"{hero.Name}   (Lvl {hero.Level} {hero.Class}, {hero.Faction}, {hero.Points} pts)##{path}", ref on))
                {
                    if (on) picks.Add(path); else picks.Remove(path);
                    changed = true;
                }
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
            if (changed) SendParty(info.PlayerID, picks);
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Your party: pick 2 to 4 heroes made in Star Quest Heroes.");

        // The closed combo only has room for the names, so the party is listed under it in full. You pick
        // a hero by reading its level, race and points, and then those facts disappeared the moment the
        // list shut - exactly when you want to check the party adds up.
        if (picks.Count > 0)
        {
            int total = 0;
            foreach (string path in picks)
            {
                FDG.Quest.QuestHero hero = _savedHeroes.First(s => s.Path == path).Hero;
                total += hero.Points;
                ImGui.TextUnformatted("  " + hero.Name);
                ImGui.SameLine();
                UiText.Disabled($"Lvl {hero.Level} {hero.Class} - {hero.BaseHeroName} ({hero.Faction}) - {hero.Points} pts");
            }
            UiText.Disabled($"  Party: {picks.Count} hero{(picks.Count == 1 ? "" : "es")}, {total} pts");
        }
    }

    private void SendParty(PlayerID playerID, List<string> picks)
    {
        var heroes = picks.Select(p => _savedHeroes!.First(s => s.Path == p).Hero).ToList();
        if (heroes.Count == 0) return;
        _autoArmiedSlots.Add(playerID.ID);
        var books = BookLibrary.Load();
        FDG.ArmyBuilding.BuiltArmyFile party = FDG.Quest.QuestArmy.Build(heroes,
            faction => books.FirstOrDefault(b => b.MatchesFactionName(faction)));   // #412: renames included
        _viewModel!.UpdateArmyListFile(playerID, party);
    }

    private static void DrawArmyCell(ArmyListSummary summary, bool isResume, float baseScale)
    {
        bool missing = LobbyArmySource.IsMissingArmy(summary, isResume);

        if (missing) ImGui.PushStyleColor(ImGuiCol.Text, OverPointsColor);
        TextFitted(summary.ArmyName, baseScale);
        if (missing) ImGui.PopStyleColor();

        if (missing && ImGui.IsItemHovered())
            UiText.Tooltip(LobbyArmySource.NoArmyTooltip);
    }

    /// <summary>The Faction cell. Red when the army is from a game system this lobby doesn't take.</summary>
    private static void DrawFactionCell(ArmyListSummary summary, EAllowedGameSystems allowed, float baseScale)
    {
        bool wrongSystem = LobbyArmySource.IsWrongSystem(summary, allowed);

        if (wrongSystem) ImGui.PushStyleColor(ImGuiCol.Text, OverPointsColor);
        TextFitted(summary.FactionName, baseScale);
        if (wrongSystem) ImGui.PopStyleColor();

        if (wrongSystem && ImGui.IsItemHovered())
            UiText.Tooltip(LobbyArmySource.WrongSystemTooltip(summary, allowed));
    }

    // #221: the colour cell - a swatch of the row's effective colour + a dropdown of the 8 palette options,
    // classic-RTS style. Any colour that is currently another row's - explicit pick OR assigned default -
    // is disabled ("(taken)"), so no pick ever changes someone else's colour. Picks write through the view
    // model (host: applies + rebroadcasts; client: requests its own row from the host), so they sync to
    // every machine. Gated by the same modify-permission as Load Army (host: everyone; client: itself).
    private void DrawColorCell(int rowIdx, LobbyPlayerInfoSummary info,
        IReadOnlyList<LobbyPlayerInfoSummary> players, int[] effectiveColor)
    {
        var (curName, curColor) = PlayerColorOptions.Options[effectiveColor[rowIdx]];
        var swatch = new Vector4(curColor.R / 255f, curColor.G / 255f, curColor.B / 255f, 1f);

        float sz = ImGui.GetTextLineHeight();
        ImGui.ColorButton($"##csw{rowIdx}", swatch,
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(sz, sz));
        ImGui.SameLine();

        bool canModify = _viewModel!.CheckCanModifyPlayerIDInfo(info.PlayerID);
        ImGui.BeginDisabled(!canModify);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo($"##color{rowIdx}", curName))
        {
            for (int j = 0; j < PlayerColorOptions.Count; j++)
            {
                bool takenByOther = PlayerColorOptions.IsTakenByAnother(effectiveColor, rowIdx, j);

                var (name, col) = PlayerColorOptions.Options[j];
                var itemSwatch = new Vector4(col.R / 255f, col.G / 255f, col.B / 255f, 1f);
                ImGui.ColorButton($"##cswi{rowIdx}_{j}", itemSwatch,
                    ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(sz, sz));
                ImGui.SameLine();
                if (ImGui.Selectable(takenByOther ? $"{name} (taken)" : name, j == effectiveColor[rowIdx],
                        takenByOther ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                    _viewModel.SetPlayerColor(info.PlayerID, j);
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
    }

    // #255: the team cell - a dropdown of Team 1..Team N, N = player count capped at the highest team
    // the engine defines (#188; as many teams as players, multiple players per team is the point, but
    // launch is blocked while ALL share one team). Offering a team past the cap would have written an
    // undefined ETeamOption that the host then rejected as out of range. The
    // current value renders faithfully even when out of range (a stale team left by a departed player -
    // kept by design). Picks write through the view model (host: applies + rebroadcasts; client:
    // requests its own row from the host), same gate as Load Army / Color. Disabled when resuming -
    // saved games keep their saved teams.
    private void DrawTeamCell(int rowIdx, LobbyPlayerInfoSummary info, int playerCount)
    {
        bool canModify = _viewModel!.CheckCanModifyPlayerIDInfo(info.PlayerID) && !_viewModel.IsResumeMode;
        ImGui.BeginDisabled(!canModify);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo($"##team{rowIdx}", TeamLabel((int)info.TeamNumber, playerCount)))
        {
            for (int n = 1; n <= Math.Min(playerCount, TeamOptions.MaxTeamNumber); n++)
            {
                if (ImGui.Selectable(TeamLabel(n, playerCount), n == (int)info.TeamNumber))
                    _viewModel.SetPlayerTeam(info.PlayerID, (ETeamOption)n);
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
    }

    /// <summary>"Team 2", or in Horde "Defenders" (the first slot's team) and "Horde".</summary>
    private string TeamLabel(int team, int playerCount)
    {
        if (!GameSystemInfo.IsHordeLike(_viewModel!.GameMode) || _viewModel.PlayerInfos.Count == 0) return $"Team {team}";
        int defenders = (int)_viewModel.PlayerInfos[0].TeamNumber;
        if (team == defenders) return GameSystemInfo.IsQuestMode(_viewModel.GameMode) ? "Heroes" : "Defenders";
        if (GameSystemInfo.IsQuestMode(_viewModel.GameMode)) return "Enemies";
        return playerCount > 2 ? $"Horde ({team})" : "Horde";
    }

    /// <summary>
    /// NOT CURRENTLY DRAWN. The lobby's chat panel and its input row were taken off the screen when
    /// the three-column layout went in: this build is played locally against bots, and the panel was
    /// spending a quarter of the lobby on a conversation with nobody. The engine still receives and
    /// stores chat (<c>ILobbyViewModel.ChatMessages</c>), and this method and <see cref="SubmitChat"/>
    /// still work - restoring chat is a matter of giving them a panel again, not rewriting them. Kept
    /// deliberately, because hosting for another player is still a supported thing to do.
    /// </summary>
    private void DrawChatLog()
    {
        IReadOnlyList<LobbyChatMessage> msgs = _viewModel!.ChatMessages;
        foreach (LobbyChatMessage msg in msgs)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted($"[{msg.SendingPlayerName}] {msg.Message}");
            ImGui.PopTextWrapPos();
        }
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1f);
    }

    private void DrawSettings(float panelW)
    {
        bool isHost = _viewModel!.HasHostPrivileges;
        // #265: resuming a save launches from the SAVED settings - the setup ones are already spent and
        // the rules ones would change a game in progress (see GameSettings.WithResumeOverridesFrom). The
        // engine ignores edits to them either way; disabling the controls stops the panel implying
        // otherwise. The battlefield is the one exception, drawn live below.
        bool locked = !isHost || _viewModel.IsResumeMode;

        float innerPad = 8f;
        ImGui.SetCursorPos(new Vector2(innerPad, innerPad));

        // Host-only: what address to give players. Drawn outside the disabled block so the copy buttons work.
        if (isHost && Section("Connection", defaultOpen: false))
            DrawHostConnectionInfo(panelW - innerPad * 2);

        if (isHost && _viewModel.IsResumeMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
            UiText.Wrapped("Resuming a save - settings are fixed, except the battlefield.");
            ImGui.PopStyleColor();
        }

        ImGui.BeginDisabled(locked);

        if (Section("Game"))
        {
        DrawGameSystemCombo();
        DrawEnumCombo("Game Mode",     _viewModel.GameMode,       _viewModel.SetGameMode,
            displayName: GameModeLabel,
            explicitOrder: GameSystemInfo.Modes(_viewModel.GameSystem),
            tooltip: "Which kind of game to play within the Game Type.\n" +
                     "Standard: the normal game - four rounds, most objectives wins.\n" +
                     "Horde: survive twelve rounds against waves of enemies. The first player slot\n" +
                     "(and anyone on its team) defends from the table centre; every other team is\n" +
                     "the enemy horde, arriving at the table corners and coming back every 3 rounds.");

        // Play Style stays editable when resuming a save (host only): Game Master adds testing tools
        // without changing any rule, so a saved game can be reopened with them on.
        ImGui.EndDisabled();
        ImGui.BeginDisabled(!isHost);
        // Play Style is gone from the lobby: the Game Master tools are in every game now, as a tab in the
        // host's right column (players who join over the network don't get it).
        ImGui.EndDisabled();
        ImGui.BeginDisabled(locked);

        DrawIntField("Army Points",    _viewModel.ArmyPoints,    _viewModel.SetArmyPoints,
            tooltip: "The point budget each player's army is built to. An army over this limit is\n" +
                     "flagged red on its row and blocks the launch. Higher points means bigger battles.",
            step: 250);
        DrawPointPresets();
        // Army Source is no longer offered: each Game Type takes its own game's armies (set with the type).

        }

        if (Section("Table"))
        {
        DrawEnumCombo("Terrain Mode",  _viewModel.TerrainPlacementMode, _viewModel.SetTerrainPlacementMode,
            debugLast: new[] { ETerrainPlacementMode.AutoFromLayout },
            // #301: explicit order keeps the two Alternating modes adjacent (the enum appends
            // AlternatingPoints after LoadFromFile to keep the wire/save values stable).
            explicitOrder: new[]
            {
                ETerrainPlacementMode.AutoFromLayout,
                ETerrainPlacementMode.Alternating,
                ETerrainPlacementMode.AlternatingPoints,
                ETerrainPlacementMode.LoadFromFile,
            },
            displayName: TerrainModeLabel,
            tooltip: "How terrain is put on the board.\n" +
                     "Alternating: One Per: players take turns placing one piece at a time\n" +
                     "(set the count below).\n" +
                     "Alternating: Points: pieces cost 1-3 points by size; players take turns\n" +
                     "spending a per-turn allowance from a shared total (set both below), so one\n" +
                     "big piece or several small ones per turn.\n" +
                     "Custom Map: pick a saved map, or Create / Edit one in the map maker\n" +
                     "(terrain, objectives and deployment zones).\n" +
                     "Auto From Layout: the server places a built-in default pool (fast, for testing).");

        // Conditional sub-options under Terrain Mode.
        switch (_viewModel.TerrainPlacementMode)
        {
            case ETerrainPlacementMode.Alternating:
                DrawTerrainCountSlider(_viewModel.TerrainCount, _viewModel.SetTerrainCount);
                break;
            case ETerrainPlacementMode.AlternatingPoints:
                DrawTerrainPointsSliders(_viewModel);
                break;
            case ETerrainPlacementMode.LoadFromFile:
                DrawMapPicker();
                break;
        }

        // Horde Mode always uses its five fixed markers (centre + each table quarter), so the
        // placement choice does nothing there - shown greyed out rather than hidden, so the panel
        // doesn't jump around when the mode changes.
        bool hordeMode = GameSystemInfo.IsHordeLike(_viewModel.GameMode);
        ImGui.BeginDisabled(hordeMode);
        DrawEnumCombo("Objective Mode", _viewModel.ObjectivePlacementMode, _viewModel.SetObjectivePlacementMode,
            debugLast: new[] { EObjectivePlacementMode.AutoPlaced },
            displayName: ObjectiveModeLabel,
            tooltip: hordeMode
                ? "Horde Mode always places five objectives: one at the table centre and\n" +
                  "one at the centre of each table quarter."
                : "Where the objective markers go. Objectives decide the winner.\n" +
                  "Auto-Placed: the server places them in balanced spots, no input needed.\n" +
                  "Player-Placed: players take turns placing markers.");
        ImGui.EndDisabled();

        ImGui.EndDisabled();

        // #265 table surface: cosmetic only, but a synced lobby setting so everyone sees one board.
        // Host-gated only - being cosmetic is exactly why a resumed game may still re-pick it.
        ImGui.BeginDisabled(!isHost);
        DrawEnumCombo("Battlefield",   _viewModel.TableBackground, _viewModel.SetTableBackground,
            displayName: TableBackgrounds.Label,
            tooltip: "The terrain type / table surface (Forest, Desert, Ice, Mars-Like, Urban, Barren).\n" +
                     "Cosmetic - no effect on terrain placement or any rule. Randomized when the lobby opens.");
        ImGui.EndDisabled();

        ImGui.BeginDisabled(locked);
        }

        if (Section("Rules"))
        {
        DrawEnumCombo("Randomness",    _viewModel.RandomnessType, _viewModel.SetRandomnessType,
            tooltip: "How dice are resolved.\n" +
                     "Realistic: real dice are rolled - whole hits, swingy and unpredictable.\n" +
                     "Probabilistic: results use expected values - smoother, less luck.");
        // Shooting is no longer offered: the official rules have every weapon group's target declared
        // before rolling (Declare First), which the lobby now always uses.

        // #201 cover proximity house rules (default on). Inside the disabled block: host-only.
        bool coverProximity = _viewModel.CoverProximityExceptions;
        if (UiButton.Checkbox("Cover Proximity Rules", ref coverProximity))
            _viewModel.SetCoverProximityExceptions(coverProximity);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("House rule (default on): cover the shooter's muzzle hugs (< 2in to the exit)\n" +
                             "grants nothing unless the target hugs the same piece, and cover shared by\n" +
                             "shooter and target grants nothing when they are closer than 6in.");

        // #384 see-through-allies house rule (default off = official rules).
        bool seeThroughAllies = _viewModel.SeeThroughFriendlyUnits;
        if (UiButton.Checkbox("See-Through Allies", ref seeThroughAllies))
            _viewModel.SetSeeThroughFriendlyUnits(seeThroughAllies);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("House rule (default off): shots see through ALL friendly units.\n" +
                             "Off (official rules): only the shooting unit's own models and the target\n" +
                             "unit are ignored for line of sight - every other unit, friendly or\n" +
                             "enemy, blocks it.");

        // #384 unlimited split fire house rule (default off).
        bool unlimitedSplitFire = _viewModel.UnlimitedSplitFire;
        if (UiButton.Checkbox("Unlimited Split Fire", ref unlimitedSplitFire))
            _viewModel.SetUnlimitedSplitFire(unlimitedSplitFire);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("House rule (default off): a shooting unit may split its fire between any\n" +
                             "number of enemy units. Off: at most 2 distinct units per shoot action.");

        // Friendly-overlap house rule (default off).
        bool friendlyOverlap = _viewModel.FriendlyUnitsMayOverlap;
        if (UiButton.Checkbox("Friendly Units May Overlap", ref friendlyOverlap))
            _viewModel.SetFriendlyUnitsMayOverlap(friendlyOverlap);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("House rule (default ON): your own units may finish a move within 1\" of\n" +
                             "each other and may move past each other. Models still cannot END on top\n" +
                             "of another model, and enemies still need the 1\" gap.\n" +
                             "Turn it OFF for the rules as written: units may only be within 1\" of\n" +
                             "another unit when charging - friendly units included.\n" +
                             "On by default because the written rule is painful in Star Quest and\n" +
                             "Quest, where every hero is its own unit and a party can never close up.");
        }

        ImGui.EndDisabled();

        // Placeholder for optional OPR Advanced Rules - nothing in it yet.
        if (Section("Advanced Rules"))
            UiText.Disabled("Nothing here yet.");
    }

    private bool _resetSections = true;

    /// <summary>A collapsible settings group. Each time the lobby opens, Connection starts closed and the
    /// rest start open, whatever they were left as last time.</summary>
    private bool Section(string title, bool defaultOpen = true)
    {
        ImGui.Spacing();
        if (_resetSections) ImGui.SetNextItemOpen(defaultOpen, ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
        bool open = ImGui.CollapsingHeader(title + "##section");
        ImGui.PopStyleColor();
        return open;
    }

    /// <summary>Quick army sizes for the Game Type: 1k-10k for the big games, 250-2000 for the skirmish
    /// ones. The typed box above stays for anything else.</summary>
    private void DrawPointPresets()
    {
        bool skirmish = GameSystemInfo.IsSkirmish(_viewModel!.GameSystem);
        int step = skirmish ? 50 : 1000, max = skirmish ? 1000 : 10000;
        ImGui.TextUnformatted("Quick Size");
        ImGui.SameLine();
        FillRowItemWidth();
        string Label(int pts) => skirmish ? $"{pts} pts" : $"{pts / 1000}k ({pts} pts)";
        int current = _viewModel.ArmyPoints;
        bool isPreset = current % step == 0 && current >= step && current <= max;
        if (ImGui.BeginCombo("##quicksize", isPreset ? Label(current) : "Custom"))
        {
            for (int pts = step; pts <= max; pts += step)
                if (ImGui.Selectable(Label(pts), pts == current))
                    _viewModel.SetArmyPoints(pts);
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip(skirmish
                ? "Common sizes for skirmish games (50 to 1000 points; Firefight is usually 200 or 300)."
                : "Common army sizes (1000 to 10000 points).");
    }

    /// <summary>"Grimdark Future - Standard - 2000 pts" for the header.</summary>
    private string GameSummary() =>
        $"{GameSystemInfo.DisplayName(_viewModel!.GameSystem)}  -  {GameModeLabel(_viewModel.GameMode)}  -  {_viewModel.ArmyPoints} pts";

    // ── Map preview ─────────────────────────────────────────────────────────────────────────────────
    private string? _previewLayoutPath;
    private FDG.SaveLoad.TerrainLayoutFile? _previewLayout;
    private FDG.SaveLoad.TerrainLayoutFile? _defaultLayout;

    private static uint U32(Raylib_cs.Color c, float alpha = 1f) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f * alpha));

    private static uint U32(int r, int g, int b, int a = 255) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));

    /// <summary>A flat top-down picture of the table the lobby is set up for: surface colour, terrain (when
    /// the layout is known before the game), deployment zones and fixed objectives.</summary>
    private void DrawMapPreview()
    {
        (float tw, float th) = FDG.GameWideConstants.TableSizeFor(_viewModel!.GameSystem);
        bool horde = GameSystemInfo.IsHordeLike(_viewModel!.GameMode);

        FDG.SaveLoad.TerrainLayoutFile? layout = null;
        string terrainNote;
        switch (_viewModel.TerrainPlacementMode)
        {
            case ETerrainPlacementMode.AutoFromLayout:
                layout = _defaultLayout ??= FDG.Stages.DefaultTerrainPool.Get();
                terrainNote = "default terrain layout";
                break;
            case ETerrainPlacementMode.LoadFromFile:
                if (_previewLayoutPath != _viewModel.TerrainLayoutPath)
                {
                    _previewLayoutPath = _viewModel.TerrainLayoutPath;
                    _previewLayout = string.IsNullOrEmpty(_previewLayoutPath) ? null
                        : FDG.SaveLoad.TerrainLayoutLoader.TryLoadFromFile(_previewLayoutPath, out _);
                }
                layout = _previewLayout;
                terrainNote = layout != null ? $"map: {(string.IsNullOrWhiteSpace(layout.Name) ? Path.GetFileNameWithoutExtension(_previewLayoutPath) : layout.Name)}" : "no map chosen";
                break;
            default:
                terrainNote = "terrain placed by the players";
                break;
        }
        bool mapObjectives = !horde && layout != null && layout.Objectives is { Count: > 0 }
            && _viewModel.TerrainPlacementMode == ETerrainPlacementMode.LoadFromFile;
        bool mapZones = !horde && layout != null && layout.DeploymentZones is { Count: >= 2 }
            && _viewModel.TerrainPlacementMode == ETerrainPlacementMode.LoadFromFile;
        string objectiveNote = horde ? "5 fixed objectives"
            : mapObjectives ? $"{layout!.Objectives.Count} objectives set by the map"
            : _viewModel.ObjectivePlacementMode == EObjectivePlacementMode.PlayerPlaced
                ? "objectives placed by the players" : "objectives placed at the start";
        UiText.Disabled($"Map preview - 6' x 4' - {terrainNote}, {objectiveNote}");

        Vector2 p0 = ImGui.GetCursorScreenPos();
        Vector2 avail = ImGui.GetContentRegionAvail();
        float scale = MathF.Max(0.1f, MathF.Min(avail.X / tw, avail.Y / th));
        Vector2 o = p0 + new Vector2((avail.X - tw * scale) / 2f, (avail.Y - th * scale) / 2f);
        Vector2 P(float x, float z) => o + new Vector2(x * scale, (th - z) * scale);

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        TableBackgroundStyle style = TableBackgrounds.For(_viewModel.TableBackground);
        dl.AddRectFilled(P(0, th), P(tw, 0), U32(style.Surface));
        for (float x = 6; x < tw; x += 6) dl.AddLine(P(x, 0), P(x, th), U32(style.GridMinor));
        for (float z = 6; z < th; z += 6) dl.AddLine(P(0, z), P(tw, z), U32(style.GridMinor));

        // Deployment zones (Horde's corner zones are quarter circles: clip everything to the table).
        dl.PushClipRect(P(0, th), P(tw, 0), true);
        uint zoneA = U32(80, 160, 255, 70), zoneB = U32(255, 110, 90, 70);
        if (horde)
        {
            dl.AddCircleFilled(P(tw / 2, th / 2), 9f * scale, zoneA, 40);
            foreach (var (cx, cz) in new[] { (0f, 0f), (tw, 0f), (0f, th), (tw, th) })
                dl.AddCircleFilled(P(cx, cz), 9f * scale, zoneB, 40);
        }
        else if (mapZones)
        {
            for (int k = 0; k < layout!.DeploymentZones.Count; k++)
            {
                FDG.SaveLoad.MapDeploymentZoneEntry z = layout.DeploymentZones[k];
                dl.AddRectFilled(P(MathF.Min(z.Left, z.Right), MathF.Max(z.Bottom, z.Top)),
                    P(MathF.Max(z.Left, z.Right), MathF.Min(z.Bottom, z.Top)), k % 2 == 0 ? zoneA : zoneB);
            }
        }
        else
        {
            float d = FDG.GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            dl.AddRectFilled(P(0, d), P(tw, 0), zoneA);
            dl.AddRectFilled(P(0, th), P(tw, th - d), zoneB);
        }

        // Terrain.
        if (layout != null)
            foreach (FDG.SaveLoad.TerrainPieceEntry piece in layout.Pieces)
                if (piece.Shape != null) DrawPreviewZone(dl, piece.Shape, TerrainPreviewColor(piece.TerrainType), P, scale);
        dl.PopClipRect();

        // Horde's five fixed objectives: centre, then the centre of each table quarter.
        if (horde)
        {
            var spots = new[] { (tw / 2, th / 2), (tw / 4, th / 4), (tw * 3 / 4, th / 4), (tw / 4, th * 3 / 4), (tw * 3 / 4, th * 3 / 4) };
            for (int k = 0; k < spots.Length; k++)
            {
                Vector2 c = P(spots[k].Item1, spots[k].Item2);
                dl.AddCircleFilled(c, MathF.Max(5f, 1.2f * scale), U32(235, 235, 235), 20);
                dl.AddCircle(c, MathF.Max(5f, 1.2f * scale), U32(20, 20, 20), 20, 2f);
                string n = (k + 1).ToString();
                Vector2 ts = ImGui.CalcTextSize(n);
                dl.AddText(c - ts / 2f, U32(20, 20, 20), n);
            }
        }

        // A custom map's own objectives.
        if (mapObjectives)
        {
            for (int k = 0; k < layout!.Objectives.Count; k++)
            {
                Vector2 c = P(layout.Objectives[k].X, layout.Objectives[k].Z);
                dl.AddCircleFilled(c, MathF.Max(5f, 1.2f * scale), U32(235, 235, 235), 20);
                dl.AddCircle(c, MathF.Max(5f, 1.2f * scale), U32(20, 20, 20), 20, 2f);
                string n = (k + 1).ToString();
                Vector2 ts = ImGui.CalcTextSize(n);
                dl.AddText(c - ts / 2f, U32(20, 20, 20), n);
            }
        }

        dl.AddRect(P(0, th), P(tw, 0), U32(style.Border), 0f, ImDrawFlags.None, 3f);
    }

    private static uint TerrainPreviewColor(ETerrainType type)
    {
        if (type.HasFlag(ETerrainType.Dangerous)) return U32(150, 60, 40, 230);
        if (type.HasFlag(ETerrainType.Blocking) || type.HasFlag(ETerrainType.Impassible)) return U32(75, 75, 82, 240);
        if (type.HasFlag(ETerrainType.Cover) && type.HasFlag(ETerrainType.Difficult)) return U32(25, 70, 30, 230);
        if (type.HasFlag(ETerrainType.Cover)) return U32(120, 95, 60, 230);
        if (type.HasFlag(ETerrainType.Difficult)) return U32(95, 85, 55, 220);
        return U32(110, 110, 110, 200);
    }

    private static void DrawPreviewZone(ImDrawListPtr dl, IZone shape, uint color, Func<float, float, Vector2> P, float scale)
    {
        switch (shape)
        {
            case RectangularZone r:
                dl.AddRectFilled(P(r.Left, r.Top), P(r.Right, r.Bottom), color);
                break;
            case CircularZone c:
                dl.AddCircleFilled(P(c.Center.X, c.Center.Y), c.Radius * scale, color, 24);
                break;
            case CompositeZone composite:
                foreach (IZone part in composite.Parts) DrawPreviewZone(dl, part, color, P, scale);
                break;
            case RotatedZoneWrapper rot when rot.Inner is RectangularZone rr:
            {
                float a = rot.AngleDegrees * MathF.PI / 180f, cos = MathF.Cos(a), sin = MathF.Sin(a);
                Vector2 Corner(float x, float z)
                {
                    float dx = x - rot.Pivot.X, dz = z - rot.Pivot.Y;
                    return P(rot.Pivot.X + dx * cos - dz * sin, rot.Pivot.Y + dx * sin + dz * cos);
                }
                dl.AddQuadFilled(Corner(rr.Left, rr.Bottom), Corner(rr.Right, rr.Bottom),
                    Corner(rr.Right, rr.Top), Corner(rr.Left, rr.Top), color);
                break;
            }
            case RotatedZoneWrapper other:
                foreach (IZone primitive in ((IZone)other).Primitives()) DrawPreviewZone(dl, primitive, color, P, scale);
                break;
        }
    }

    /// <summary>A small accent-coloured group heading with a rule under it, e.g. "GAME", "TABLE".</summary>
    private static void SectionHeader(string title)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();
    }

    // After a label + SameLine, size the upcoming control to exactly the space left in the row, so
    // every settings row ends flush at the panel edge whatever the window size. (The old blanket
    // PushItemWidth(panelW) ignored the label already occupying part of the row, so wide labels
    // pushed their controls off the panel - and off the window at small sizes.)
    private static void FillRowItemWidth() =>
        ImGui.SetNextItemWidth(MathF.Max(60f, ImGui.GetContentRegionAvail().X));

    // Shows the addresses a remote player would type into "Host Address", plus the listen port (QF9). LAN
    // is computed once; the public IP is fetched once in the background and filled in when it returns.
    private void DrawHostConnectionInfo(float contentW)
    {
        if (!_publicFetchStarted)
        {
            _publicFetchStarted = true;
            _lanAddresses = ComputeLanAddresses();
            StartPublicIpFetch();
        }

        UiText.Disabled("Share these with players:");

        DrawCopyableAddress("LAN:    ", _lanAddresses ?? "unavailable", "lanip");
        DrawCopyableAddress("Public: ", _publicAddress, "pubip");
        ImGui.TextUnformatted($"Port:   {ListenPort}");
    }

    // One "label address [Copy]" row with Copy pinned to the panel's right edge; a long address
    // slides under the button instead of pushing it off the panel at narrow window sizes. The full
    // string is copied regardless of how much of it is visible.
    private static void DrawCopyableAddress(string label, string address, string id)
    {
        ImGui.TextUnformatted($"{label}{address}");
        float copyW = ImGui.CalcTextSize("Copy").X + ImGui.GetStyle().FramePadding.X * 2f;
        ImGui.SameLine(MathF.Max(0f, ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - copyW));
        if (UiButton.NavigateSmall($"Copy##{id}")) ImGui.SetClipboardText(address);
    }

    // Up, non-loopback IPv4 unicast addresses - the LAN addresses a same-network player can reach.
    private static string ComputeLanAddresses()
    {
        var addresses = new List<string>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ua.Address)) continue;
                    addresses.Add(ua.Address.ToString());
                }
            }
        }
        catch
        {
            // Interface enumeration can throw on some platforms/permissions; fall through to "unavailable".
        }

        return addresses.Count > 0 ? string.Join(", ", addresses.Distinct()) : "unavailable";
    }

    // One-shot best-effort public-IP lookup. async void is deliberate: it's a fire-and-forget UI fetch with
    // its own try/catch, and _publicAddress is volatile so the main thread picks up the result next frame.
    private async void StartPublicIpFetch()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string ip = (await http.GetStringAsync("https://api.ipify.org").ConfigureAwait(false)).Trim();
            _publicAddress = string.IsNullOrWhiteSpace(ip) ? "unavailable" : ip;
        }
        catch
        {
            _publicAddress = "unavailable";
        }
    }

    private void DrawLaunch(float panelW, float panelH)
    {
        bool isHost = _viewModel!.HasHostPrivileges;
        bool resume = _viewModel.IsResumeMode;

        // #402: the gate's BLOCKING half decides whether the button is live at all, so it runs every
        // frame. That is affordable by construction - LaunchGate.BlockingProblems reads each slot's
        // points and system slug and nothing else; the catalog validation that walks a Forge army's
        // embedded book stays on the click path below. Resume skips both halves: those armies are
        // already in play, and a save's slots are not re-crewed with new lists.
        IReadOnlyList<string> blockers = isHost && !resume
            ? _viewModel.BlockingLaunchProblems()
            : Array.Empty<string>();
        bool canLaunch = isHost && blockers.Count == 0;

        Vector2 avail = ImGui.GetContentRegionAvail();
        // Reserve space for the inline error line below the button when present.
        float errorLineH = _lastLaunchError != null ? ImGui.GetTextLineHeightWithSpacing() + 4f : 0f;
        Vector2 buttonSize = new Vector2(avail.X, MathF.Max(0f, avail.Y - errorLineH));

        ImGui.BeginDisabled(!canLaunch);
        bool clicked = UiButton.Confirm(resume ? "RESUME" : "LAUNCH", buttonSize);
        // Ended here rather than around the whole method so the confirm popup below is never drawn
        // inside a disabled scope - its own buttons would inherit the grey.
        ImGui.EndDisabled();

        // AllowWhenDisabled: the greyed-out button is precisely the state that needs explaining (#402).
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)
            && LaunchTooltip(isHost, blockers) is string tip)
        {
            UiText.Tooltip(tip);
        }

        if (clicked)
        {
            // #153 launch gate (decision 9): catalog validation Errors in a Forge army raise a confirm
            // dialog (warn + host override) instead of launching straight away. Resume skips it.
            IReadOnlyList<string> problems = resume
                ? Array.Empty<string>()
                : _viewModel.ValidateArmiesForLaunch();
            if (problems.Count > 0)
            {
                _launchProblems = problems;
                ImGui.OpenPopup("Launch anyway?");
            }
            else
            {
                DoLaunch(resume);
            }
        }

        DrawLaunchConfirm();

        if (_lastLaunchError != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            UiText.Wrapped(_lastLaunchError);
            ImGui.PopStyleColor();
        }
    }

    /// <summary>#402: what a hover over the LAUNCH button says, or null when it is live and needs no
    /// explaining. ImGui-free so the blocked wording is testable.</summary>
    internal static string? LaunchTooltip(bool isHost, IReadOnlyList<string> blockers)
    {
        if (!isHost) return "Only the host can launch.";
        if (blockers.Count == 0) return null;
        return "Cannot launch:\n" + string.Join("\n", blockers.Select(p => "  " + p));
    }

    private void DoLaunch(bool resume)
    {
        string? fail;
        bool started = resume ? _viewModel!.TryResumeGame(out fail) : _viewModel!.TryLaunchGame(out fail);
        _lastLaunchError = started ? null : (fail ?? "Launch failed.");

        if (started) PersistHostSettings();
    }

    // #310: the settings panel as the host left it becomes the default for the next hosted game.
    // Written at the two points a lobby ends - launching and backing out - rather than on every edit,
    // so dragging a slider doesn't write the file once per value. Host-only and never on a resume:
    // a resume lobby's controls are locked to the save's settings, which aren't this player's picks.
    private void PersistHostSettings()
    {
        if (_viewModel == null || !_viewModel.HasHostPrivileges || _viewModel.IsResumeMode) return;

        UserConfig.Current.HostSettings = HostGameSettings.CaptureFrom(_viewModel);
        UserConfig.Save();
    }

    // #153 launch gate: lists each army's hard legality problems; Cancel is the default action, and
    // "Launch anyway" is the explicit house-rules override.
    private void DrawLaunchConfirm()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("Launch anyway?", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextUnformatted("Some armies have problems:");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
        foreach (string problem in _launchProblems)
            UiText.Wrapped(problem);
        ImGui.PopStyleColor();
        ImGui.Spacing();

        // Cancel first and focused — the safe default. #240: edge-only Esc (stuck-key safe).
        if (UiButton.Back("Cancel", new Vector2(140f, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.SetItemDefaultFocus();
        ImGui.SameLine();
        if (UiButton.Confirm("Launch anyway", new Vector2(140f, 0f)))
        {
            ImGui.CloseCurrentPopup();
            DoLaunch(resume: false);
        }

        ImGui.EndPopup();
    }

    private static void DrawTerrainCountSlider(int current, Action<int> setter)
    {
        ImGui.TextUnformatted("Terrain Count");
        ImGui.SameLine();
        FillRowItemWidth();
        int v = current;
        if (ImGui.SliderInt("##TerrainCount", ref v, 0, FDG.Stages.PlaceTerrainStage.MaxAlternatingPieceCount) && v != current)
            setter(v);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("How many terrain pieces the players place, one at a time, alternating.\n" +
                             "More pieces means a denser, more cover-heavy board.");
    }

    private static string ObjectiveModeLabel(EObjectivePlacementMode mode) => mode switch
    {
        EObjectivePlacementMode.AutoPlaced => "Auto-Placed",
        EObjectivePlacementMode.PlayerPlaced => "Player-Placed",
        _ => mode.ToString(),
    };

    // #371. Spelled out rather than left as the enum name, which would read "OneAtATime".
    private static string PlayStyleLabel(EPlayStyle style) => style switch
    {
        EPlayStyle.Standard => "Standard",
        EPlayStyle.GameMaster => "Game Master",
        _ => style.ToString(),
    };

    /// <summary>
    /// The Game Type: which OPR game (Grimdark Future, Age of Fantasy, GF: Firefight...). Games whose rules
    /// aren't in yet are listed greyed out. Picking one also points the Army Source at that game's armies.
    /// </summary>
    private void DrawGameSystemCombo()
    {
        ImGui.TextUnformatted("Game Type");
        ImGui.SameLine();
        FillRowItemWidth();
        EGameSystem current = _viewModel.GameSystem;
        if (ImGui.BeginCombo("##Game Type", GameSystemInfo.DisplayName(current)))
        {
            foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
            {
                bool available = GameSystemInfo.IsAvailable(system);
                string label = GameSystemInfo.DisplayName(system) + (available ? "" : "  (coming soon)");
                ImGui.BeginDisabled(!available);
                if (ImGui.Selectable(label, system == current) && available && system != current)
                    ApplyGameSystem(_viewModel, system);
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Which OPR game to play.\n" +
                           "Grimdark Future / Age of Fantasy: the full-size army games.\n" +
                           "GF: Firefight: the skirmish version of Grimdark Future, played with\n" +
                           "Grimdark Future armies.\n" +
                           "Picking a game also sets the Army Source to that game's armies.");
    }

    /// <summary>
    /// Point a lobby at a Game Type, with everything choosing one implies: the army source, the shooting
    /// mode, a points default that suits the game's scale, and a Game Mode the game actually offers.
    /// Shared by the combo above and by the main menu's Game Type tiles, which make the same choice a
    /// screen earlier - the two must agree, so there is one copy of it.
    /// </summary>
    public static void ApplyGameSystem(ILobbyViewModel viewModel, EGameSystem system)
    {
        EGameSystem current = viewModel.GameSystem;
        viewModel.SetGameSystem(system);
        viewModel.SetAllowedGameSystems(GameSystemInfo.ArmySource(system));
        viewModel.SetShootingMode(EShootingMode.DeclareFirst);
        // Firefight / Skirmish armies are 200-300 pts; moving back to a big game resets to 2000.
        bool wasSkirmish = GameSystemInfo.IsSkirmish(current), isSkirmish = GameSystemInfo.IsSkirmish(system);
        if (isSkirmish && !wasSkirmish) viewModel.SetArmyPoints(300);
        else if (!isSkirmish && wasSkirmish) viewModel.SetArmyPoints(2000);
        if (Array.IndexOf(GameSystemInfo.Modes(system), viewModel.GameMode) < 0)
            viewModel.SetGameMode(GameSystemInfo.Modes(system)[0]);
    }

    private static string GameModeLabel(EGameMode mode) => mode switch
    {
        EGameMode.Standard => "Standard",
        EGameMode.Horde => "Horde",
        EGameMode.StarQuestHorde => "Star Quest Horde",
        EGameMode.StarQuestMission => "Star Quest Mission",
        _ => mode.ToString(),
    };

    private static string ShootingModeLabel(EShootingMode mode) => mode switch
    {
        EShootingMode.OneAtATime => "One At A Time",
        EShootingMode.DeclareFirst => "Declare First",
        _ => mode.ToString(),
    };

    private static string TerrainModeLabel(ETerrainPlacementMode mode) => mode switch
    {
        ETerrainPlacementMode.AutoFromLayout => "Auto From Layout",
        ETerrainPlacementMode.Alternating => "Alternating: One Per",
        ETerrainPlacementMode.AlternatingPoints => "Alternating: Points",
        ETerrainPlacementMode.LoadFromFile => "Custom Map",
        _ => mode.ToString(),
    };

    // #301 Alternating: Points - the two knobs shown only in that mode.
    private static void DrawTerrainPointsSliders(ILobbyViewModel viewModel)
    {
        ImGui.TextUnformatted("Total Points");
        ImGui.SameLine();
        FillRowItemWidth();
        int total = viewModel.TerrainPointsTotal;
        int t = total;
        if (ImGui.SliderInt("##TerrainPointsTotal", ref t, 0, FDG.Stages.PlaceTerrainStage.MaxPointsTotal) && t != total)
            viewModel.SetTerrainPointsTotal(t);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("The total terrain points the players place between them, dealt out in\n" +
                             "placing order a turn's worth at a time - whoever wins the roll-off gets\n" +
                             "any remainder. 0 skips terrain placement entirely.");

        ImGui.TextUnformatted("Points Per Turn");
        ImGui.SameLine();
        FillRowItemWidth();
        int perTurn = viewModel.TerrainPointsPerTurn;
        int p = perTurn;
        if (ImGui.SliderInt("##TerrainPointsPerTurn", ref p, 1, FDG.Stages.PlaceTerrainStage.MaxPointsPerTurn) && p != perTurn)
            viewModel.SetTerrainPointsPerTurn(p);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("How many terrain points each player spends on their turn - one big piece\n" +
                             "or several small ones. A piece costing more than this can still open a\n" +
                             "turn; the difference comes out of the player's next turn.");
    }

    /// <summary>Custom Map picker: a dropdown of saved maps (with "Create new map" and "Browse for a
    /// file..."), plus Create / Edit / Delete buttons. Create and Edit open the map maker.</summary>
    private void DrawMapPicker()
    {
        _mapEditor.OnSaved ??= path =>
        {
            _viewModel?.SetTerrainLayoutPath(path);
            _previewLayoutPath = null;   // force the preview to re-read the (possibly overwritten) file
        };

        string? current = _viewModel!.TerrainLayoutPath;
        bool haveCurrent = !string.IsNullOrEmpty(current) && File.Exists(current);
        string display = string.IsNullOrEmpty(current) ? "(choose a map)" : Path.GetFileNameWithoutExtension(current);

        ImGui.TextUnformatted("Map");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##MapPick", display, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.Selectable("+ Create new map")) _mapEditor.OpenNew();
            ImGui.Separator();
            IReadOnlyList<string> maps = FdgRaylib.SaveLoad.MapPaths.List();
            if (maps.Count == 0) UiText.Disabled("No saved maps yet");
            foreach (string path in maps)
            {
                bool selected = string.Equals(path, current, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(path)}##{path}", selected))
                {
                    _viewModel.SetTerrainLayoutPath(path);
                    _previewLayoutPath = null;
                }
            }
            ImGui.Separator();
            if (ImGui.Selectable("Browse for a file..."))
            {
                var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Map",
                    FdgRaylib.SaveLoad.MapPaths.FolderPath + Path.DirectorySeparatorChar, false, TerrainFilter);
                string? picked = canceled ? null : paths?.FirstOrDefault();
                if (!string.IsNullOrEmpty(picked)) { _viewModel.SetTerrainLayoutPath(picked); _previewLayoutPath = null; }
            }
            ImGui.EndCombo();
        }

        if (UiButton.NavigateSmall("Create")) _mapEditor.OpenNew();
        ImGui.SameLine();
        ImGui.BeginDisabled(!haveCurrent);
        if (UiButton.NavigateSmall("Edit")) _mapEditor.OpenExisting(current!);
        ImGui.SameLine();
        if (UiButton.NavigateSmall("Delete"))
        {
            _mapPendingDelete = current;
            ImGui.OpenPopup("Delete map?");
        }
        ImGui.EndDisabled();

        if (ImGui.BeginPopupModal("Delete map?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Delete the map \"{Path.GetFileNameWithoutExtension(_mapPendingDelete ?? "")}\"?");
            UiText.Disabled("This can't be undone.");
            if (UiButton.Back("Cancel", new Vector2(120, 0))) { _mapPendingDelete = null; ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (UiButton.Confirm("Delete", new Vector2(120, 0)))
            {
                try
                {
                    if (_mapPendingDelete != null && File.Exists(_mapPendingDelete)) File.Delete(_mapPendingDelete);
                    if (string.Equals(_mapPendingDelete, _viewModel.TerrainLayoutPath, StringComparison.OrdinalIgnoreCase))
                        _viewModel.SetTerrainLayoutPath(null);
                    _previewLayoutPath = null;
                    _previewLayout = null;
                }
                catch (Exception ex) { Console.WriteLine($"Could not delete map: {ex.Message}"); }
                _mapPendingDelete = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private static void DrawTerrainLayoutPicker(string? current, Action<string?> setter)
    {
        ImGui.TextUnformatted("Layout File");
        string display = string.IsNullOrEmpty(current) ? "(none selected)" : Path.GetFileName(current);
        ImGui.SameLine();
        if (UiButton.Navigate($"{display}##LayoutPick", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Terrain Layout", "", false, TerrainFilter);
            if (!canceled)
            {
                string? path = paths?.FirstOrDefault();
                if (!string.IsNullOrEmpty(path)) setter(path);
            }
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Pick a saved terrain layout file. Its pieces are placed on the board\n" +
                             "verbatim - no roll-off, no alternating placement.");
    }

    private void SubmitChat()
    {
        string msg = _chatInput.Trim();
        if (!string.IsNullOrEmpty(msg))
            _viewModel!.SendMessage(msg);
        _chatInput = "";
    }

    private void TryLoadArmyForPlayer(PlayerID playerID)
    {
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Army", ArmyPaths.DefaultDialogPath, false, ArmyFilter);
        if (canceled) return;

        string path = paths?.FirstOrDefault() ?? "";
        if (LoadArmyFile(path) is not { } loaded) return;

        // #372: a hand-picked army marks the slot as served, so AutoArmyNewSlots never overwrites it on a
        // later frame (it would, for a slot whose army was loaded before the folder scan came back).
        _autoArmiedSlots.Add(playerID.ID);
        _viewModel!.UpdateArmyListFile(playerID, loaded);
    }

    private void HandleLaunch(IFDGGame game)
    {
        var infos = _viewModel?.PlayerInfos ?? [];
        var players = infos.Select(info => (info.PlayerID, info.PlayerName)).ToList();

        // Host-only save hook (work item #054 will add client-initiated saving).
        Func<string?>? saveGame = _viewModel != null && _viewModel.CanSaveGame ? _viewModel.SaveGameToJson : null;

        // Shared with the --scenario direct launch (#167); the wiring lives in GameGuiWiring.
        // #221: colour picks come from the SYNCED roster (ColorIndex rides LobbyPlayerInfoSummary), so
        // host and clients all launch with the same colours; unpicked slots fill with the free defaults.
        var colorPicks = infos.ToDictionary(info => info.PlayerID, info => info.ColorIndex);
        GameGuiWiring.Launch(game, players, saveGame,
            (tableState, colorForPlayer, log, overlay, taskDisplay, presentationPlayer, save, playerMessageUI) =>
                OnGameLaunched?.Invoke(tableState, colorForPlayer, log, overlay, taskDisplay, presentationPlayer, save, playerMessageUI),
            colorChoiceForPlayer: pid => colorPicks.TryGetValue(pid, out int idx) && idx >= 0 ? idx : null,
            // #201: the synced lobby setting (host-set, broadcast to clients) so previews match the engine.
            coverProximityExceptions: _viewModel!.CoverProximityExceptions,
            // #265: likewise synced, so host and clients play on the same-looking board.
            tableBackground: _viewModel!.TableBackground,
            // #384: likewise synced, so every player's fire-line previews match the engine's LoS.
            seeThroughFriendlyUnits: _viewModel!.SeeThroughFriendlyUnits);
    }

    private static void DrawIntField(string label, int current, Action<int> setter, string? tooltip = null,
        int step = 1)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        FillRowItemWidth();
        int v = current;
        if (ImGui.InputInt($"##{label}", ref v, step, step * 4) && v != current)
            setter(Math.Max(0, v));
        if (tooltip != null && ImGui.IsItemHovered())
            UiText.Tooltip(tooltip);
    }

    // Draws a labeled enum dropdown. `debugLast` names values that are debug conveniences (e.g. the
    // pre-placed / auto-placed setup shortcuts): in a Debug build they keep their declared position -
    // first, for fast iteration - while in a Release build they move to the end so a normal game doesn't
    // lead with them. `explicitOrder` overrides the raw enum-value order entirely (#301: lets related
    // modes sit together when wire-compat forced a new value to the end of the enum); debugLast still
    // applies on top of it. `displayName` supplies a friendly label per value (falls back to the raw
    // enum name). This is presentation only: the enum, saves, and network wire are identical across
    // build types (#243).
    private static void DrawEnumCombo<TEnum>(string label, TEnum current, Action<TEnum> setter,
        TEnum[]? debugLast = null, Func<TEnum, string>? displayName = null, string? tooltip = null,
        TEnum[]? explicitOrder = null)
        where TEnum : struct, Enum
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        FillRowItemWidth();

        TEnum[] values = OrderComboValues(debugLast, explicitOrder);
        string[] labels = values.Select(v => displayName?.Invoke(v) ?? v.ToString()).ToArray();
        int idx = Math.Max(0, Array.IndexOf(values, current));
        if (ImGui.Combo($"##{label}", ref idx, labels, labels.Length))
            setter(values[idx]);
        if (tooltip != null && ImGui.IsItemHovered())
            UiText.Tooltip(tooltip);
    }

    private static TEnum[] OrderComboValues<TEnum>(TEnum[]? debugLast, TEnum[]? explicitOrder = null)
        where TEnum : struct, Enum
    {
        TEnum[] all = explicitOrder ?? Enum.GetValues<TEnum>();
#if DEBUG
        return all;
#else
        if (debugLast is null || debugLast.Length == 0)
            return all;
        var debug = new HashSet<TEnum>(debugLast);
        return all.Where(v => !debug.Contains(v)).Concat(all.Where(v => debug.Contains(v))).ToArray();
#endif
    }
}
