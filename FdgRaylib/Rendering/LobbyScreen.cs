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
    // (SetViewModel touches it), because re-reading 12 MB of JSON every frame would be waste. The
    // ROTATION is per-lobby, so a fresh lobby re-rolls freely.
    //
    // #421: it used to be a `static readonly Lazy<>` - scanned once per LAUNCH and never again, on the
    // reasoning that "the armies folder doesn't change under us". The in-game Army Forge makes that
    // false: save a list, come back here, and it is not in the picker until the game is restarted.
    // ArmyCatalogCache keeps the caching and adds an Invalidate, which OnShown below calls - so walking
    // Forge -> Save -> lobby always rescans.

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

    /// <summary>
    /// #421: the lobby is reached by navigating, so this fires every time it comes back into view -
    /// including the walk the owner reported as broken: Army Forge, build a list, Save, back here. The
    /// armies folder CAN change while the app runs, so the cached scan is dropped and the per-system
    /// slice rebuilt; the next read starts a fresh background scan.
    /// </summary>
    public void OnShown()
    {
        // RefreshIfChanged rather than Invalidate: this also fires on every return from the army
        // picker, and a blind invalidate would reparse ~12 MB of lists each time. The check is a
        // directory walk comparing names, sizes and write times.
        if (!ArmyCatalogCache.RefreshIfChanged()) return;
        _gameTypeArmies = null;
        _botArmyPicker = null;
    }

    public void SetViewModel(ILobbyViewModel viewModel)
    {
        _viewModel?.Dispose();
        _viewModel = viewModel;
        _launchStarted = false;
        _chatInput = "";
        _pendingGame = null;

        // #372: start the armies scan as soon as a lobby exists (it runs on a background task) and give
        // this lobby its own rotation, so re-opening one starts the army cycle over.
        _botArmyPicker = null;
        _autoArmiedSlots.Clear();
        _resetSections = true;   // Connection closed, the rest open, every time the lobby opens
        _ = ArmyCatalogCache.Current;
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

        // #435: a campaign mission launches itself. Before DrawScaled, so the lobby is never drawn on
        // the way through - the one frame it would take is still a visible flash of a screen the
        // campaign flow says is gone.
        if (TryAutoLaunch()) return;

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
        // The right column runs to the bottom margin, because LAUNCH lives inside it now (under the
        // map preview) rather than in a full-width bar below everything. The left and middle columns
        // stop short of the bottom to leave the "can't launch yet" line its own band - the one thing
        // still worth the full width, since a blocker can be a sentence long.
        float contentH     = MathF.Max(80f, screenH - contentY - margin);
        float columnsH     = MathF.Max(80f, contentH - actionH - margin);

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
        ImGui.BeginChild("##settings", new Vector2(leftW, columnsH),
            ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        ImGui.SetWindowFontScale(panelScale);  // settings fields ~50% bigger than the rest of the lobby
        DrawSettings(leftW);
        ImGui.EndChild();
        _resetSections = false;

        // ── Middle column: players and their armies ───────────────────────────
        float midX = margin * 2f + leftW;
        ImGui.SetCursorPos(new Vector2(midX, contentY));
        ImGui.BeginChild("##players", new Vector2(midW, columnsH), ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(panelScale);  // rows + font ~50% bigger than the rest of the lobby
        DrawPlayerList(midW, panelScale);
        ImGui.EndChild();

        // ── Right column: map preview, with LAUNCH directly under it ──────────
        // Kai's mock, 2026-11-02: "put it as a button below Map Preview". The two belong together -
        // the map is the last thing you look at before starting, so the button that starts it sits
        // under the picture rather than across the bottom of the whole screen.
        float rightX = margin * 3f + leftW + midW;
        float previewH = MathF.Max(80f, contentH - actionH - margin);
        ImGui.SetCursorPos(new Vector2(rightX, contentY));
        ImGui.BeginChild("##mappreview", new Vector2(rightW, previewH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetWindowFontScale(panelScale * 0.8f);
        DrawMapPreview();
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(rightX, contentY + previewH + margin));
        // No border on the launch child - the button itself fills the panel and provides the visual edge.
        ImGui.BeginChild("##launch", new Vector2(rightW, actionH), ImGuiChildFlags.None);
        ImGui.SetWindowFontScale(panelScale);  // match the other panels
        DrawLaunch(rightW, actionH);
        ImGui.EndChild();

        // ── Why LAUNCH is greyed, along the bottom under the other two columns ─
        IReadOnlyList<string> launchBlockers = _viewModel.HasHostPrivileges && !_viewModel.IsResumeMode
            ? _viewModel.BlockingLaunchProblems()
            : Array.Empty<string>();

        float blockerW = leftW + midW + margin;
        if (launchBlockers.Count > 0 && blockerW > 0f)
        {
            ImGui.SetCursorPos(new Vector2(margin, screenH - margin - actionH));
            ImGui.BeginChild("##blockers", new Vector2(blockerW, actionH), ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.SetWindowFontScale(panelScale * 0.8f);
            ImGui.PushStyleColor(ImGuiCol.Text, OverPointsColor);
            string more = launchBlockers.Count > 1 ? $"  (+{launchBlockers.Count - 1} more - hover LAUNCH)" : "";
            UiText.Wrapped("Can't launch yet: " + launchBlockers[0] + more);
            ImGui.PopStyleColor();
            ImGui.EndChild();
        }

        ImGui.End();
        ImGui.PopStyleVar(5);
    }

    /// <summary>
    /// The seats, as Kai's mock has them (2026-11-02, screen "1.21"): a 2x2 grid with one player per
    /// square, rather than the table of rows that was here before.
    ///
    /// <para>
    /// <b>Four seats, two of them switchable.</b> Seats 1 and 2 are always there - a game needs two
    /// sides - and seats 3 and 4 carry an Active switch, so turning both off gives a 1v1 and turning
    /// one on gives a three-player game. A seat can only be switched off while it is the LAST one,
    /// because the engine's roster is a list and dropping the middle of it would slide seat 4 up into
    /// seat 3's square while you were looking at it.
    /// </para>
    ///
    /// <para>
    /// <b>The columns are the two sides.</b> On a 2x2 that is what a column reads as, so the headers
    /// name the teams and the 2v2 button pairs seats down the columns (1+3 against 2+4). The per-seat
    /// Team control stays underneath it: 2v2 and FFA are two presets of the same thing, and keeping the
    /// real control visible is what makes 1v2 - which Kai asked about and parked - a setting rather than
    /// a rewrite.
    /// </para>
    ///
    /// <para>
    /// Deliberately plain under the working agreement: squares, labels and existing controls moved into
    /// them. What is NOT throwaway is which seat shows what, and that every seat - the host's own
    /// included - can be handed to a bot.
    /// </para>
    /// </summary>
    private void DrawPlayerList(float panelW, float panelScale)
    {
        AutoArmyNewSlots();
        EnsureTwoSeats();

        IReadOnlyList<LobbyPlayerInfoSummary> players = _viewModel!.PlayerInfos;

        // #221: effective colour per seat this frame, resolved from the SYNCED picks (ColorIndex rides
        // LobbyPlayerInfoSummary through the lobby protocol, so every machine computes the same result) -
        // explicit picks win, unpicked seats fill with the free defaults in palette order.
        var chosen = new int?[players.Count];
        for (int i = 0; i < players.Count; i++)
            chosen[i] = players[i].ColorIndex >= 0 ? players[i].ColorIndex : null;
        int[] effectiveColor = PlayerColorOptions.ResolveIndices(chosen);

        DrawTeamPresets(players);

        // Two columns of squares. The grid is sized from what is left of the panel so the four cards
        // fill it; a short panel scrolls rather than squashing the cards into illegibility.
        float gap = 10f;
        float availW = ImGui.GetContentRegionAvail().X;
        float availH = ImGui.GetContentRegionAvail().Y;
        float cardW = MathF.Max(150f, (availW - gap) / 2f);
        float headerH = ImGui.GetTextLineHeightWithSpacing() + 4f;
        // A card needs: header, name, army button, faction/points, team, actions - six lines plus
        // padding. Below that floor the grid scrolls instead of clipping a card's buttons away.
        float cardH = MathF.Max(ImGui.GetTextLineHeightWithSpacing() * 6.4f + 24f,
                                (availH - headerH - gap * 2f) / 2f);

        // Column headers - the thing Kai asked for above the lists.
        ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
        ImGui.TextUnformatted(ColumnHeader(players, column: 0));
        ImGui.SameLine(cardW + gap);
        ImGui.TextUnformatted(ColumnHeader(players, column: 1));
        ImGui.PopStyleColor();

        for (int seat = 0; seat < SeatGridSize; seat++)
        {
            if (seat % 2 == 1) ImGui.SameLine(0f, gap);
            DrawSeatCard(seat, players, effectiveColor, new Vector2(cardW, cardH), panelScale);
        }

        // Anything beyond four seats keeps working - the engine has no four-player cap and a loaded
        // save or a networked lobby can hold more - it just has no square of its own, so it is listed
        // under the grid rather than silently hidden.
        if (players.Count > SeatGridSize)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
            ImGui.TextUnformatted($"ALSO PLAYING ({players.Count - SeatGridSize})");
            ImGui.PopStyleColor();
            for (int seat = SeatGridSize; seat < players.Count; seat++)
                DrawSeatCard(seat, players, effectiveColor, new Vector2(cardW * 2f + gap, cardH), panelScale);
        }
    }

    /// <summary>How many seats get a square of their own.</summary>
    internal const int SeatGridSize = 4;

    /// <summary>
    /// Whether seat <paramref name="seat"/> (0-based) may be switched OFF, given how many seats the
    /// lobby currently holds.
    ///
    /// <para>
    /// Two rules, and both of them are there because of something that would otherwise look like a
    /// bug rather than a rule. Seats 1 and 2 never switch off: a game needs two sides. And only the
    /// LAST seat switches off, because the engine's roster is a list - dropping seat 3 of four would
    /// slide seat 4 up into seat 3's square, so the card you clicked would stay filled (with somebody
    /// else) and the card you never touched would empty.
    /// </para>
    /// </summary>
    internal static bool SeatCanSwitchOff(int seat, int seatCount) =>
        seat >= 2 && seat == seatCount - 1;

    /// <summary>
    /// Whether the empty square for seat <paramref name="seat"/> may be switched ON. Seats fill from the
    /// left: switching on seat 4 while 3 is off would put the new player in seat 3, leaving the switch
    /// you actually clicked still off - a button that visibly does the wrong thing.
    /// </summary>
    internal static bool SeatCanSwitchOn(int seat, int seatCount) =>
        seat == seatCount && seat < SeatGridSize;

    /// <summary>
    /// A game needs two sides, so the lobby opens with two seats whether or not anything added the
    /// second one. The new seat is a bot, which is the thing somebody opening a lobby alone almost
    /// always wants, and the Player/Bot switch on its card changes that in one click.
    /// </summary>
    private void EnsureTwoSeats()
    {
        if (!_viewModel!.HasHostPrivileges || _viewModel.IsResumeMode) return;
        if (_viewModel.PlayerInfos.Count >= 2) return;
        _viewModel.AddAiPlayer(GameSystemInfo.IsQuest(_viewModel.GameSystem)
            ? EAiProfile.StarQuest : EAiProfile.SoloCoOp);
    }

    /// <summary>What a column of the grid is called. In 2v2 the column IS a team, which is the whole
    /// reason the preset pairs down the columns; in a free-for-all a column means nothing, so it says
    /// so rather than labelling it with one seat's team.</summary>
    private string ColumnHeader(IReadOnlyList<LobbyPlayerInfoSummary> players, int column)
    {
        var inColumn = new List<int>();
        for (int seat = column; seat < Math.Min(players.Count, SeatGridSize); seat += 2)
            inColumn.Add((int)players[seat].TeamNumber);

        if (inColumn.Count == 0) return "";
        if (inColumn.Distinct().Count() == 1)
            return TeamLabel(inColumn[0], players.Count).ToUpperInvariant();
        return "FREE-FOR-ALL";
    }

    /// <summary>
    /// 2v2 and FFA: two presets over the per-seat Team control, not a replacement for it. 2v2 pairs the
    /// grid's columns (seats 1+3 against 2+4) because that is what a column of a 2x2 reads as; FFA
    /// gives every seat its own team number.
    /// </summary>
    private void DrawTeamPresets(IReadOnlyList<LobbyPlayerInfoSummary> players)
    {
        bool canEdit = _viewModel!.HasHostPrivileges && !_viewModel.IsResumeMode;
        ImGui.BeginDisabled(!canEdit || players.Count < 3);

        if (UiButton.NavigateSmall("2v2"))
        {
            for (int seat = 0; seat < players.Count; seat++)
                _viewModel.SetPlayerTeam(players[seat].PlayerID, (ETeamOption)(seat % 2 + 1));
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Two sides: the grid's left column against its right column.\n" +
                           "Needs at least three seats.");

        ImGui.SameLine();
        if (UiButton.NavigateSmall("Free-for-all"))
        {
            for (int seat = 0; seat < players.Count; seat++)
                _viewModel.SetPlayerTeam(players[seat].PlayerID,
                    (ETeamOption)Math.Min(seat + 1, TeamOptions.MaxTeamNumber));
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Everyone for themselves - each seat on its own team.\n" +
                           "Needs at least three seats.");

        ImGui.EndDisabled();

        // Each seat still carries its own Team control below, so anything the two presets do not cover
        // - 1v2, or three players with two of them allied - is set seat by seat.
        ImGui.SameLine();
        UiText.Disabled("  (or set a seat's team on its card)");
    }

    /// <summary>One square of the grid: who is in the seat, what they are bringing, and their side.</summary>
    private void DrawSeatCard(int seat, IReadOnlyList<LobbyPlayerInfoSummary> players,
        int[] effectiveColor, Vector2 size, float panelScale)
    {
        ImGui.PushID($"seat{seat}");

        if (seat >= players.Count)
        {
            DrawEmptySeatCard(seat, size);
            ImGui.PopID();
            return;
        }

        LobbyPlayerInfoSummary info = players[seat];
        ImGui.BeginChild($"##card", size, ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        // ── Header: which seat, who is holding the controls, and the Active switch ──
        ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
        ImGui.TextUnformatted($"SEAT {seat + 1}");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        DrawSeatCrewToggle(info, panelScale);

        bool canSwitchOff = SeatCanSwitchOff(seat, players.Count);
        if (seat >= 2 && _viewModel!.CheckCanRemovePlayer(info.PlayerID))
        {
            string label = "Active";
            float switchW = ImGui.CalcTextSize(label).X + ImGui.GetFrameHeight() + 14f;
            ImGui.SameLine(MathF.Max(0f, ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - switchW));
            ImGui.BeginDisabled(!canSwitchOff);
            bool active = true;
            if (UiButton.Checkbox(label, ref active) && !active)
                _viewModel.RemovePlayer(info.PlayerID);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                UiText.Tooltip(canSwitchOff
                    ? "Switch this seat off to play with fewer players."
                    : $"Switch seat {players.Count} off first - seats come off the end.");
        }

        ImGui.Separator();

        // ── Who they are ──
        TextFitted(info.PlayerName, panelScale);

        // ── What they are bringing ──
        DrawArmyPicker(seat, info, panelScale);

        // Faction and points on one line: both are facts about the army above, and the points number is
        // the one that turns red and blocks the launch.
        DrawFactionCell(info.ArmyListSummary, _viewModel!.AllowedGameSystems, panelScale);
        ImGui.SameLine();
        UiText.Disabled("-");
        ImGui.SameLine();
        DrawPointsCell(info.ArmyListSummary,
            GameSystemInfo.IsQuest(_viewModel.GameSystem) ? int.MaxValue : _viewModel.ArmyPoints);

        // ── Side and colour ──
        // Each on its own full-width line. They used to share one line, but the team dropdown sizes itself
        // to the whole remaining width, so the colour control was pushed past the card's right edge and
        // clipped - which is why Kai found the colour picker "gone" (2026-11-05). Own line = can't recur.
        ImGui.Spacing();
        DrawTeamCell(seat, info, players.Count);
        DrawColorCell(seat, info, players, effectiveColor);

        // ── Re-crewing a resumed save keeps its old four buttons: a save's seats are fixed in number
        // but not in who plays them, and the house bots are still selectable there.
        if (_viewModel.IsResumeMode)
        {
            ImGui.Spacing();
            ImGui.BeginDisabled(!_viewModel.HasHostPrivileges);
            if (UiButton.NavigateSmall("Local"))
                _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.Local);
            ImGui.SameLine();
            if (UiButton.NavigateSmall("Tactician"))
                _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.Tactician);
            ImGui.SameLine();
            if (UiButton.NavigateSmall("Strategist"))
                _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.Strategist);
            ImGui.SameLine();
            if (UiButton.NavigateSmall("DerpBot"))
                _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.SoloRules);
            ImGui.EndDisabled();
        }

        ImGui.EndChild();
        ImGui.PopID();
    }

    /// <summary>
    /// Player or Bot, for this seat. Offered on EVERY seat including the host's own (Kai, 2026-11-02):
    /// hand them all to bots and you watch the two armies fight, with nothing asked of you.
    ///
    /// <para>A connected network player's seat shows what it is and offers no switch - taking a remote
    /// player's controls away is a kick, which the engine refuses from here.</para>
    /// </summary>
    private void DrawSeatCrewToggle(LobbyPlayerInfoSummary info, float panelScale)
    {
        if (_viewModel!.IsResumeMode || !_viewModel.CheckCanSetPlayerType(info.PlayerID))
        {
            ImGui.SetWindowFontScale(panelScale * 0.65f);
            UiText.Disabled(PlayerTypeLabel(info.PlayerType).ToUpperInvariant());
            ImGui.SetWindowFontScale(panelScale);
            return;
        }

        bool isBot = info.PlayerType == EPlayerType.AI;
        if (UiButton.NavigateSmall(isBot ? "Bot" : "Player"))
        {
            if (isBot) _viewModel.SetPlayerType(info.PlayerID, EPlayerType.Local);
            else
                _viewModel.SetPlayerType(info.PlayerID, EPlayerType.AI,
                    GameSystemInfo.IsQuest(_viewModel.GameSystem)
                        ? EAiProfile.StarQuest : EAiProfile.SoloCoOp);
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip(isBot
                ? "A bot is playing this seat. Click to take it yourself.\n" + OfficialAiTooltip
                : "You are playing this seat. Click to hand it to a bot.\n" +
                  "Hand every seat to a bot to watch the game play itself.");
    }

    /// <summary>An off seat: the switch that turns it on, and nothing else to read.</summary>
    private void DrawEmptySeatCard(int seat, Vector2 size)
    {
        ImGui.BeginChild("##card", size, ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        UiText.Disabled($"SEAT {seat + 1}");
        ImGui.Separator();

        bool canAdd = _viewModel!.HasHostPrivileges && !_viewModel.IsResumeMode;
        bool isNextSeat = SeatCanSwitchOn(seat, _viewModel.PlayerInfos.Count);

        ImGui.BeginDisabled(!canAdd || !isNextSeat);
        bool active = false;
        if (UiButton.Checkbox("Active", ref active) && active)
        {
            _viewModel.AddAiPlayer(GameSystemInfo.IsQuest(_viewModel.GameSystem)
                ? EAiProfile.StarQuest : EAiProfile.SoloCoOp);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            UiText.Tooltip(isNextSeat
                ? "Switch this seat on to add a player. It arrives as a bot;\n" +
                  "the Player/Bot button on its card changes that."
                : $"Switch seat {_viewModel.PlayerInfos.Count + 1} on first - seats fill in order.");

        if (canAdd && isNextSeat)
        {
            ImGui.Spacing();
            UiText.Disabled("Off - not in the game.");
        }

        ImGui.EndChild();
    }

    // #372/#388 ---------------------------------------------------------------------------------------
    // Starter armies. Every new slot, bot or human, arrives with no army at all (#402 stopped the engine
    // from stamping bots with a "Test Army" stub), so the lobby fills one in from the armies folder as
    // soon as the scan is in. App-side on purpose: the armies FOLDER is an app concept (ArmyPaths), and
    // the engine has no business reading the user's disk. A slot the roll cannot fill stays visibly
    // empty and blocks the launch, rather than quietly playing a list nobody picked.

    private static bool ArmyCatalogReady => ArmyCatalogCache.Current.IsLoaded;

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

    // #419: the lobby's only AI button. Two tooltips because the two rules families print different
    // flowcharts - the Quest systems carry theirs in their core rules, everything else uses the
    // Solo & Co-Op book.
    internal const string OfficialAiTooltip =
        "The enemy side, playing OnePageRules' official Solo & Co-Op rules:\n" +
        "it takes and holds objectives, shoots the closest target it can see,\n" +
        "and favours enemies that haven't moved yet.";

    internal const string QuestAiTooltip =
        "The enemy side, following the official Quest AI flowchart: shoot or\n" +
        "charge the closest hero it can see, otherwise head for its AI Goal.";

    /// <summary>#405: the add-button tooltips, so the ASCII guard can sweep them all. The three house-bot
    /// tooltips stay listed - the bots are still reachable with --ai-profile (plan G4), so their text is
    /// still shipped text and still has to be ASCII.</summary>
    internal static IReadOnlyList<string> AddPlayerTooltips { get; } = new[]
    {
        LocalPlayerTooltip, StrategistBotTooltip, TacticianBotTooltip, DerpBotTooltip,
        OfficialAiTooltip, QuestAiTooltip,
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
            // #414: Star Quest's enemies come from its OWN folder. This used to redirect every Quest
            // system to armies/GDF, from before Star Quest had books of its own - so the dropdown listed
            // Grimdark Future armies that the launch gate then refused, while armies/GF Star Quest went
            // unread. The heroes still come from the party picker, not from here.
            _gameTypeArmies = ArmyPaths.EntriesFor(system, ArmyCatalogCache.Current.Entries)
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
    /// #428 - set beside <see cref="PendingCampaignArmy"/> when the campaign has a bot opponent: the
    /// path of the ENEMY campaign army. The lobby seats a bot for it and gives it that army, so the
    /// same enemy comes back mission after mission with the levels, injuries and recruits it earned.
    /// Consumed and cleared by <see cref="ApplyPendingCampaignArmy"/> like the player's.
    /// </summary>
    public static string? PendingCampaignEnemyArmy;

    /// <summary>
    /// #435 - the terrain mode Mission Setup chose, applied beside the campaign armies. Null when the
    /// lobby was reached any other way, in which case the saved host settings decide as before.
    /// </summary>
    public static ETerrainPlacementMode? PendingTerrainMode;

    /// <summary>#435 - the custom map Mission Setup chose, for <see cref="PendingTerrainMode"/> =
    /// <see cref="ETerrainPlacementMode.LoadFromFile"/>. Null for every other mode.</summary>
    public static string? PendingTerrainLayoutPath;

    /// <summary>
    /// #435 - set by Mission Setup: launch as soon as the lobby is ready, without drawing it. The
    /// campaign flow is meant to go from Mission Setup straight into the game ("The Table is gone from
    /// the campaign flow"), and the lobby is the only place that knows how to launch one - so it is
    /// still used, just never seen.
    /// <para>
    /// Consumed by <see cref="TryAutoLaunch"/> on the first frame the lobby can launch. If the lobby
    /// finds a real reason not to - a missing army file, a list the gate refuses - the flag is dropped
    /// and the player is left in the lobby with the greyed LAUNCH button explaining why, which is more
    /// use than a silent failure back on Mission Setup.
    /// </para>
    /// </summary>
    public static bool PendingAutoLaunch;

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
        string? enemyPath = PendingCampaignEnemyArmy;
        // #435: read and cleared with the armies, before any early return, so a campaign army that fails
        // to load cannot leave a map choice behind to surprise the next lobby.
        ETerrainPlacementMode? terrainMode = PendingTerrainMode;
        string? layoutPath = PendingTerrainLayoutPath;
        PendingCampaignArmy = null;
        PendingCampaignEnemyArmy = null;
        PendingTerrainMode = null;
        PendingTerrainLayoutPath = null;
        if (path == null || _viewModel == null) return;
        if (LoadArmyFile(path) is not { } army) return;
        if (_viewModel.PlayerInfos.Count == 0) return;

        PlayerID host = _viewModel.PlayerInfos[0].PlayerID;
        // Campaign games have no points limit; the lobby still has one, so lift it clear of the army.
        if (army.TotalPoints > _viewModel.ArmyPoints) _viewModel.SetArmyPoints(army.TotalPoints);
        _viewModel.UpdateArmyListFile(host, army);
        // Mark it the way every other deliberate assignment does, so the starter-army roll leaves it be.
        _autoArmiedSlots.Add(host.ID);

        // #428: the campaign's own enemy, if it has one. Seated as a bot and handed ITS campaign army,
        // so the opponent is the same army that fought the last mission rather than a fresh list rolled
        // from the folder. Added after the host's own row so PlayerInfos[0] stays the host (#405).
        // #435: the map Mission Setup chose. Applied here rather than in SetViewModel because the saved
        // host settings are re-applied over the top there (see the note on this method).
        if (terrainMode is { } mode)
        {
            _viewModel.SetTerrainPlacementMode(mode);
            _viewModel.SetTerrainLayoutPath(mode == ETerrainPlacementMode.LoadFromFile ? layoutPath : null);
        }

        if (enemyPath == null || LoadArmyFile(enemyPath) is not { } enemy) return;
        _viewModel.AddAiPlayer(FDG.Ai.EAiProfile.SoloRules);
        // The bot that was just added is the AI row with no army on it - #402 means a fresh bot arrives
        // army-less, and the starter-army roll has not run yet. Found by that rather than by taking the
        // last row, so this does not depend on the roster's ordering.
        if (_viewModel.PlayerInfos.FirstOrDefault(p =>
                p.PlayerID.ID != host.ID && p.PlayerType == EPlayerType.AI
                && !_autoArmiedSlots.Contains(p.PlayerID.ID)) is not { } botRow) return;
        if (enemy.TotalPoints > _viewModel.ArmyPoints) _viewModel.SetArmyPoints(enemy.TotalPoints);
        _viewModel.UpdateArmyListFile(botRow.PlayerID, enemy);
        _autoArmiedSlots.Add(botRow.PlayerID.ID);
    }
    private List<(string Path, FDG.Quest.QuestHero Hero)>? _savedHeroes;
    private double _savedHeroesAt = -10;

    /// <summary>
    /// A Quest game: the human row picks 2-4 saved heroes as its party. #418 - the heroes come from the
    /// folder of the system THIS LOBBY is set to, not from a const: it used to always read Star Quest's,
    /// so an AoF: Quest lobby would have offered Star Quest heroes and then refused the party it built
    /// from them.
    /// </summary>
    private void DrawPartyPicker(int row, LobbyPlayerInfoSummary info)
    {
        if (_savedHeroes == null || ImGui.GetTime() - _savedHeroesAt > 3.0)
        {
            _savedHeroes = FdgRaylib.Quest.HeroPaths.LoadAll(FdgRaylib.Quest.QuestGameTypes.For(_viewModel.GameSystem));
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
        // #414: the faction's Star Quest edition, not whichever edition the library scan reached first.
        FDG.ArmyBuilding.BuiltArmyFile party = FDG.Quest.QuestArmy.Build(heroes,
            faction => FDG.Quest.QuestArmy.BookForHeroFaction(books, faction, _viewModel.GameSystem),
            _viewModel.GameSystem);
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

    // #221: the colour cell. Kai, 2026-11-05: "Just a button that says Choose Color and when you click it, a
    // small popup appears with a color wheel you can use to choose your color." So: the row's current colour
    // as a swatch, a Choose Color button, and a popup with a hue wheel. Any colour goes, except one too close
    // to another seat's (explicit pick or default alike - PlayerColorOptions.ClashingRow), because two armies
    // you can't tell apart is the one thing a colour must not do. Picks write through the view model (host:
    // applies + rebroadcasts; client: requests its own row from the host), so they sync to every machine.
    // Gated by the same modify-permission as Load Army (host: everyone; client: itself).
    private int _colorPopupRow = -1;
    private Vector3 _colorDraft;

    private void DrawColorCell(int rowIdx, LobbyPlayerInfoSummary info,
        IReadOnlyList<LobbyPlayerInfoSummary> players, int[] effectiveColor)
    {
        Color cur = PlayerColorOptions.ColorFor(effectiveColor[rowIdx]);
        var swatch = new Vector4(cur.R / 255f, cur.G / 255f, cur.B / 255f, 1f);

        float sz = ImGui.GetFrameHeight();
        ImGui.ColorButton($"##csw{rowIdx}", swatch,
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(sz, sz));
        ImGui.SameLine();

        bool canModify = _viewModel!.CheckCanModifyPlayerIDInfo(info.PlayerID);
        ImGui.BeginDisabled(!canModify);
        string popupId = $"Choose Color##colorpop{rowIdx}";
        if (ImGui.Button($"Choose Color##colorbtn{rowIdx}", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            _colorPopupRow = rowIdx;
            _colorDraft = new Vector3(swatch.X, swatch.Y, swatch.Z);
            ImGui.OpenPopup(popupId);
        }
        ImGui.EndDisabled();

        if (ImGui.BeginPopup(popupId))
        {
            if (_colorPopupRow != rowIdx) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }

            ImGui.TextUnformatted($"Base colour for {info.PlayerName}");
            ImGui.SetNextItemWidth(220f);
            ImGui.ColorPicker3($"##wheel{rowIdx}", ref _colorDraft,
                ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview
                | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha);

            var pick = new Color((byte)Math.Round(Math.Clamp(_colorDraft.X, 0f, 1f) * 255f),
                                 (byte)Math.Round(Math.Clamp(_colorDraft.Y, 0f, 1f) * 255f),
                                 (byte)Math.Round(Math.Clamp(_colorDraft.Z, 0f, 1f) * 255f), (byte)255);
            int clash = PlayerColorOptions.ClashingRow(effectiveColor, rowIdx, pick);

            // Old and new side by side, so the choice is made against what it replaces.
            ImGui.ColorButton($"##old{rowIdx}", swatch, ImGuiColorEditFlags.NoTooltip, new Vector2(40f, 24f));
            ImGui.SameLine();
            UiText.Disabled("->");
            ImGui.SameLine();
            ImGui.ColorButton($"##new{rowIdx}", new Vector4(_colorDraft, 1f), ImGuiColorEditFlags.NoTooltip,
                new Vector2(40f, 24f));

            if (clash >= 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
                UiText.Wrapped($"Too close to {players[clash].PlayerName}'s colour - pick something more different.");
                ImGui.PopStyleColor();
            }

            ImGui.BeginDisabled(clash >= 0);
            if (ImGui.Button("OK", new Vector2(100f, 0f)))
            {
                _viewModel.SetPlayerColor(info.PlayerID, PlayerColorOptions.EncodeCustom(pick.R, pick.G, pick.B));
                _colorPopupRow = -1;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100f, 0f)))
            {
                _colorPopupRow = -1;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
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
        // Kai's mock, 2026-11-02: one Select Map button where Terrain Mode, its sub-options and
        // Objective Mode used to be. The picker is the whole decision now - a map carries its terrain,
        // its objective markers and its deployment zones, so three settings collapse into one choice.
        //
        // HOUSE RULE, recorded in claude/house-rules.md: this drops the books' alternating terrain
        // placement and alternating objective placement, which are real pre-game steps in all four
        // books. Kai's call, knowingly: a drawn map is a better table than one two people assemble by
        // clicking, and that placement is the slowest part of starting a game. Nothing is deleted - the
        // modes still exist in the engine and a saved game resumes onto whatever it was launched with.
        DrawMapChoice();

        // Out of the `locked` gate. The terrain block that used to be here ended it before the
        // battlefield's own host-only gate, and deleting that block took this line with it - so the
        // Table section pushed one disabled level every frame and never popped it, and ImGui asserted
        // "Missing EndDisabled()" the moment the lobby drew. Guarded by DisabledScopeBalanceTests.
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

        // The Advanced Rulebooks' 18 deployment styles (Kai's screenshots, 2026-11-05). GF: Firefight and
        // AoF: Skirmish print one square-table set, Grimdark Future and Age of Fantasy their own long-table
        // set; the Quest games have no Advanced book, so no dropdown there.
        if (AdvancedDeployment.Available(_viewModel.GameSystem))
        {
            ImGui.BeginDisabled(!isHost);
            var order = new[]
            {
                EDeploymentStyle.Core, EDeploymentStyle.RandomStandard, EDeploymentStyle.RandomStrategic,
                EDeploymentStyle.RandomAsymmetric,
            }.Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Standard))
             .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Strategic))
             .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Asymmetric)).ToArray();
            DrawEnumCombo("Deployment", _viewModel.DeploymentStyle, _viewModel.SetDeploymentStyle,
                displayName: AdvancedDeployment.Name, explicitOrder: order,
                tooltip: "Where each side deploys - the Advanced Rules' deployment styles.\n" +
                         "Standard and Strategic are balanced; Asymmetric leaves one side surrounded or\n" +
                         "outnumbered. A Random choice rolls one die at the start of the game.\n" +
                         "Two-player games only; a map with its own deployment zones keeps them.");
            ImGui.EndDisabled();
        }

        // The Mission Cards (Kai, 2026-11-05): the two fixed-deck games. A standard game only.
        if (FDG.Missions.MissionCardCatalog.Available(_viewModel.GameSystem) && _viewModel.GameMode == EGameMode.Standard)
        {
            ImGui.BeginDisabled(!isHost);
            DrawEnumCombo("Mission Cards", _viewModel.MissionCardGame, _viewModel.SetMissionCardGame,
                displayName: FDG.Missions.MissionCardCatalog.Name,
                tooltip: "Play with the Mission Cards: six objectives, four rounds, and victory points from cards.\n" +
                         "Battle of Wits - each player has their own deck and three cards of their own.\n" +
                         "Total Domination - one shared deck; three cards everyone races to complete.\n" +
                         "At the end you also get 1 VP per objective you control. Most VP wins.");
            ImGui.EndDisabled();
        }
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

        // #456: the Advanced Rules section is live now that the 3.6.0 Advanced books are in the project
        // (2026-11-02). It holds the Fast-Play rules - the only Advanced rules built so far - and says plainly
        // when the game being set up does not have them (Firefight, Skirmish and the Quests: their books do
        // not print Fast-Play).
        if (Section("Advanced Rules", defaultOpen: false))
        {
            EGameSystem system = _viewModel.GameSystem;
            UiText.Colored(ImGuiTheme.HeaderAccent, "Fast-Play");
            if (!FastPlay.BooksHaveIt(system))
            {
                UiText.Wrapped($"{GameSystemInfo.DisplayName(system)} doesn't have the Fast-Play rules - only "
                               + "Grimdark Future and Age of Fantasy do.");
            }
            else
            {
                int from = FastPlay.RecommendedFromPoints(system) ?? 0;
                UiText.Wrapped($"Speeds up big games: each unit is measured from one leader model. OPR "
                               + $"recommends these from {from} pts. Every unit gets a leader you can change "
                               + "before the game, and the leader is always the last of its models to die.");
                bool move = _viewModel.FastPlayMovement, melee = _viewModel.FastPlayMelee,
                     shoot = _viewModel.FastPlayShooting;
                bool changed = false;
                changed |= UiButton.Checkbox("Leader-Based Shooting", ref shoot);
                if (ImGui.IsItemHovered())
                    UiText.Tooltip("Range and line of sight are measured from the leader only. If the leader can\n"
                                   + "see the target, every weapon in range of it from the leader's spot may fire.");
                changed |= UiButton.Checkbox("Leader-Based Melee", ref melee);
                if (ImGui.IsItemHovered())
                    UiText.Tooltip("Charge with the leader; the rest of the unit is placed around the enemy for you,\n"
                                   + "and the target's models close in the same way. In melee every model strikes,\n"
                                   + "wherever it stands.");
                changed |= UiButton.Checkbox("Leader-Based Movement", ref move);
                if (ImGui.IsItemHovered())
                    UiText.Tooltip("Only the leader's move is measured. Place the rest of the unit anywhere you like,\n"
                                   + "as long as it stays in coherency - and a charge still needs the leader to reach.");
                // Simplified Terrain is #460, later by Kai's call - shown greyed so the set reads as the book
                // prints it.
                ImGui.BeginDisabled(true);
                bool terrain = false;
                UiButton.Checkbox("Simplified Terrain (later)", ref terrain);
                ImGui.EndDisabled();
                if (changed) _viewModel.SetFastPlay(move, melee, shoot);
            }
        }

        ImGui.EndDisabled();
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
        (float gameW, float gameH) = FDG.GameWideConstants.TableSizeFor(_viewModel!.GameSystem);
        float tw = gameW, th = gameH;
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
                // Draw the map's own table. A map drawn for another size is still playable - the game
                // sets the table from the SYSTEM, not the map - but previewing it at the game's size
                // would show a board that is not the one that was drawn.
                if (layout is { TableWidthInches: > 0f, TableHeightInches: > 0f })
                {
                    tw = layout.TableWidthInches;
                    th = layout.TableHeightInches;
                    if (MathF.Abs(tw - gameW) > 0.01f || MathF.Abs(th - gameH) > 0.01f)
                    {
                        terrainNote += $"  -  drawn for {tw / 12f:0.#}' x {th / 12f:0.#}',"
                            + $" this game is {gameW / 12f:0.#}' x {gameH / 12f:0.#}'";
                    }
                }
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
                // Every piece - a zone may be an L or a T, and the preview has to be the map.
                foreach (FDG.SaveLoad.MapZoneRect r in layout.DeploymentZones[k].AllParts())
                {
                    dl.AddRectFilled(P(MathF.Min(r.Left, r.Right), MathF.Max(r.Bottom, r.Top)),
                        P(MathF.Max(r.Left, r.Right), MathF.Min(r.Bottom, r.Top)), k % 2 == 0 ? zoneA : zoneB);
                }
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
        // Greyed once this lobby has launched: the engine refuses a second launch anyway (it used to start a
        // second game on the same bus), and a button that still looks live invites the double click.
        bool canLaunch = isHost && blockers.Count == 0 && !_launchStarted;

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

    /// <summary>
    /// #435 - Mission Setup's "never show the lobby" launch. Returns true while it is still working, so
    /// the caller skips drawing.
    ///
    /// <para>
    /// The flag is consumed on the first frame either way, so this can never loop: it launches when the
    /// lobby says it can, and otherwise hands the player the lobby with the greyed LAUNCH button and its
    /// tooltip, which is the only place that can explain what is wrong with an army.
    /// </para>
    /// <para>
    /// The #153 launch gate is deliberately skipped here. It raises a modal for catalog warnings in a
    /// Forge army and asks the host to override - and a campaign army was built in this app, from a
    /// bundled book, and was legal enough to start a campaign with. Stopping a mission on it would mean
    /// a dialog the campaign flow has no screen for. Hard problems still block: those are
    /// <see cref="ILobbyViewModel.BlockingLaunchProblems"/>, checked below.
    /// </para>
    /// </summary>
    private bool TryAutoLaunch()
    {
        if (!PendingAutoLaunch) return false;
        PendingAutoLaunch = false;

        if (_viewModel is not { HasHostPrivileges: true } vm) return false;
        if (vm.BlockingLaunchProblems().Count > 0) return false;

        DoLaunch(vm.IsResumeMode);
        // A failed launch leaves _lastLaunchError set and nothing started, so fall through to the lobby
        // where that error is on screen.
        return _lastLaunchError == null;
    }

    private bool _launchStarted;

    private void DoLaunch(bool resume)
    {
        string? fail;
        bool started = resume ? _viewModel!.TryResumeGame(out fail) : _viewModel!.TryLaunchGame(out fail);
        _lastLaunchError = started ? null : (fail ?? "Launch failed.");

        if (started)
        {
            _launchStarted = true;
            PersistHostSettings();
        }
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

    private string GameModeLabel(EGameMode mode) => GameSystemInfo.ModeLabel(mode, _viewModel!.GameSystem);

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

    /// <summary>
    /// Raised when the lobby's Select Map button is pressed. Program.cs opens
    /// <see cref="MapPickerScreen"/> and hands the answer back through <see cref="SetMap"/>.
    /// </summary>
    public Action<EGameSystem, ETableBackground, bool, string?>? OnSelectMapRequested;

    /// <summary>
    /// The chosen map applied to the lobby. Null is Random - which is <c>AutoFromLayout</c>, the mode
    /// that has the server lay a table out, and the reason the picker is useful before any map exists.
    /// </summary>
    public void SetMap(string? path)
    {
        if (_viewModel == null) return;
        if (string.IsNullOrEmpty(path))
        {
            _viewModel.SetTerrainPlacementMode(ETerrainPlacementMode.AutoFromLayout);
            _viewModel.SetTerrainLayoutPath(null);
        }
        else
        {
            _viewModel.SetTerrainPlacementMode(ETerrainPlacementMode.LoadFromFile);
            _viewModel.SetTerrainLayoutPath(path);
        }
        _previewLayoutPath = null;   // force the preview to re-read
    }

    /// <summary>
    /// The map row: what is chosen, and the button that opens the picker. The map's own objectives and
    /// deployment zones come with it - CustomMapRules.FixedObjectives is already what the objective
    /// stages read - so there is nothing else to set here.
    /// </summary>
    private void DrawMapChoice()
    {
        string? current = _viewModel!.TerrainLayoutPath;
        bool isMap = _viewModel.TerrainPlacementMode == ETerrainPlacementMode.LoadFromFile
                     && !string.IsNullOrEmpty(current);

        ImGui.TextUnformatted("Map");
        ImGui.SameLine();
        FillRowItemWidth();
        if (UiButton.Navigate(isMap ? MapDisplayName(current!) : "Random", new Vector2(-1f, 0f)))
        {
            OnSelectMapRequested?.Invoke(_viewModel.GameSystem, _viewModel.TableBackground,
                GameSystemInfo.IsHordeLike(_viewModel.GameMode), isMap ? current : null);
        }
        if (ImGui.IsItemHovered())
        {
            UiText.Tooltip("The table you will fight on.\n"
                + "Random: the game lays out terrain when the mission starts.\n"
                + "A map: its terrain, objective markers and deployment zones, exactly as drawn.\n"
                + "Make maps in Map Maker, on the main menu.");
        }
    }

    /// <summary>A map's own name when it has one, else the file's - the same answer the picker gives.</summary>
    private string MapDisplayName(string path)
    {
        if (_previewLayoutPath != path)
        {
            _previewLayoutPath = path;
            _previewLayout = FDG.SaveLoad.TerrainLayoutLoader.TryLoadFromFile(path, out _);
        }
        return _previewLayout is { Name: { Length: > 0 } name } ? name
            : Path.GetFileNameWithoutExtension(path);
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
