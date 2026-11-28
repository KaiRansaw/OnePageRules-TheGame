using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib;
using FdgRaylib.Config;
using FdgRaylib.Lobby;
using FdgRaylib.Network;
using FdgRaylib.Rendering;
using FdgRaylib.SaveLoad;
using Godot;
using OprGodot.Hud;
using OprGodot.Forge;

namespace OprGodot;

/// <summary>
/// The lobby (port step 2; Kai's decisions 2026-11-07, screen map Part 5 item 8): a mode button hosts and
/// lands here. Settings tabs on the left (only what this game and mode have, a hover on every setting and
/// option - book wording for rules), the 2x2 seat cards in the middle, and on the right the map preview, the
/// clicked seat's army list and LAUNCH. All the non-drawing work is <see cref="LobbySession"/> (FdgApp).
/// </summary>
public partial class LobbyScreen : Control, Hud.ILooked
{
    public LobbySession Session { get; set; } = null!;
    public Hud.Looks.ELook Look => Hud.Looks.For(Session.Vm.GameSystem);
    private ILobbyViewModel Vm => Session.Vm;

    private Label _title = null!, _sub = null!, _blockers = null!, _seatHeading = null!, _previewNote = null!;
    private TabContainer _tabs = null!;
    private GridContainer _seatGrid = null!;
    private HBoxContainer _teamRow = null!;
    private VBoxContainer _armyRows = null!;
    private LobbyMapPreview _preview = null!;
    private Button _launch = null!;

    private int _selectedSeat;
    private string _seatSig = "", _settingsSig = "", _valueSig = "";
    private double _poll;
    private bool _fastPlayOn, _extraActionsOn;
    private readonly HashSet<string> _advancedOpen = new();

    // The two settings a typed / picked value lives in, refreshed in place so typing isn't interrupted.
    private SpinBox? _points;
    private OptionButton? _quickSize;

    public override void _Ready()
    {
        // Each seat's pictures and model picks reach the other players (Kai, 2026-11-14).
        if (!ReferenceEquals(SharedLooks.Lobby, Vm)) SharedLooks.Reset(Vm);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        margin.AddChild(root);

        // Header: Back, "<Game> - <Mode> - <pts>", whose table.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 18);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(120, 48),
            TooltipText = "Leave the lobby and go back. Friends who joined are disconnected." };
        back.Pressed += App.I.Back;
        header.AddChild(back);
        _title = new Label();
        Ui.ScreenTitle(_title);
        _title.AddThemeFontSizeOverride("font_size", 40);
        header.AddChild(_title);
        _sub = HudKit.Text("", 18, HudKit.Dim, HudKit.Semi);
        _sub.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        header.AddChild(_sub);
        root.AddChild(header);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        root.AddChild(body);

        // Left: settings tabs.
        _tabs = new TabContainer { CustomMinimumSize = new Vector2(470, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(_tabs);

        // Middle: team presets + the seat cards.
        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mid.AddThemeConstantOverride("separation", 10);
        _teamRow = new HBoxContainer();
        _teamRow.AddThemeConstantOverride("separation", 8);
        mid.AddChild(_teamRow);
        _seatGrid = new GridContainer { Columns = 2, SizeFlagsVertical = SizeFlags.ExpandFill };
        _seatGrid.AddThemeConstantOverride("h_separation", 12);
        _seatGrid.AddThemeConstantOverride("v_separation", 12);
        mid.AddChild(_seatGrid);
        body.AddChild(mid);

        // Right: map preview, the clicked seat's army, LAUNCH.
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) };
        right.AddThemeConstantOverride("separation", 10);
        var previewBox = new VBoxContainer();
        previewBox.AddThemeConstantOverride("separation", 4);
        previewBox.AddChild(HudKit.Heading("Map preview", 14));
        _preview = new LobbyMapPreview { CustomMinimumSize = new Vector2(0, 260), MouseFilter = MouseFilterEnum.Ignore };
        previewBox.AddChild(_preview);
        _previewNote = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _previewNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _previewNote.CustomMinimumSize = new Vector2(10, 0);
        previewBox.AddChild(_previewNote);
        var previewPanel = new PanelContainer { ThemeTypeVariation = "CardPanel" }.With(p => p.AddChild(previewBox));
        right.AddChild(previewPanel);

        var armyBox = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        armyBox.AddThemeConstantOverride("separation", 6);
        _seatHeading = HudKit.Text("", 16, Palette.Header, HudKit.Tracked(HudKit.Bold, 1));
        _seatHeading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _seatHeading.CustomMinimumSize = new Vector2(10, 0);
        armyBox.AddChild(_seatHeading);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _armyRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _armyRows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_armyRows);
        armyBox.AddChild(scroll);
        var armyPanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill }.With(p => p.AddChild(armyBox));
        right.AddChild(armyPanel);

        var launchBox = new VBoxContainer();
        launchBox.AddThemeConstantOverride("separation", 10);
        right.AddChild(launchBox);
        _blockers = HudKit.Text("", 15, Palette.WoundsRed, HudKit.Semi);
        _blockers.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _blockers.CustomMinimumSize = new Vector2(10, 0);
        launchBox.AddChild(_blockers);
        _launch = new Button { Name = "Launch", Text = "LAUNCH", ThemeTypeVariation = "CommitButton", CustomMinimumSize = new Vector2(0, 72) };
        _launch.AddThemeFontSizeOverride("font_size", 30);
        _launch.Pressed += OnLaunch;
        launchBox.AddChild(_launch);
        body.AddChild(right);

        // Kai's frame (Lobby.tscn -> Frame), when the scene has one: each panel goes in its box and the code's
        // columns are dropped. Without it (or for a panel without a box) the columns above stay.
        if (ScreenFrame.Framed(this))
        {
            bool all = true;
            all &= ScreenFrame.TryPlace(this, "Header", header, new ScreenFrame.Anchors(0, 0, 1, 0));
            all &= ScreenFrame.TryPlace(this, "SettingsTabs", _tabs, new ScreenFrame.Anchors(0, 0, 0, 1));
            all &= ScreenFrame.TryPlace(this, "Seats", mid, new ScreenFrame.Anchors(0, 0, 1, 1));
            all &= ScreenFrame.TryPlace(this, "MapPreview", previewPanel, new ScreenFrame.Anchors(1, 0, 1, 0));
            all &= ScreenFrame.TryPlace(this, "SeatArmy", armyPanel, new ScreenFrame.Anchors(1, 0, 1, 1));
            all &= ScreenFrame.TryPlace(this, "Launch", launchBox, new ScreenFrame.Anchors(1, 1, 1, 1));
            if (all) margin.QueueFree();
        }
        else
        {
            ScreenFrame.TryPlace(this, "Header", header, new ScreenFrame.Anchors(0, 0, 1, 0));
            ScreenFrame.TryPlace(this, "SettingsTabs", _tabs, new ScreenFrame.Anchors(0, 0, 0, 1));
            ScreenFrame.TryPlace(this, "Seats", mid, new ScreenFrame.Anchors(0, 0, 1, 1));
            ScreenFrame.TryPlace(this, "MapPreview", previewPanel, new ScreenFrame.Anchors(1, 0, 1, 0));
            ScreenFrame.TryPlace(this, "SeatArmy", armyPanel, new ScreenFrame.Anchors(1, 0, 1, 1));
            ScreenFrame.TryPlace(this, "Launch", launchBox, new ScreenFrame.Anchors(1, 1, 1, 1));
        }

        // A Quest campaign mission (PLAY NEXT MISSION): the party, the chapter's enemy, the locked facts.
        if (FdgRaylib.Quest.QuestCampaignSession.Pending is { } launch && Session.Campaign == null && Vm.HasHostPrivileges)
        {
            FdgRaylib.Quest.QuestCampaignSession.Pending = null;
            Session.ApplyCampaign(launch);
        }
        // An army campaign mission (Mission Setup's START MISSION): both campaign armies and the plan, then LAUNCH is
        // pressed for you. If something stops it, the lobby stays up with the reason.
        if (FdgRaylib.Quest.MissionSetupSession.Pending is { } army && Session.ArmyCampaign == null && Vm.HasHostPrivileges)
        {
            FdgRaylib.Quest.MissionSetupSession.Pending = null;
            if (Session.ApplyArmyCampaign(army) is { } problem) ShowLaunchError(problem);
            else _autoLaunch = 3.0;
        }
        if (Session.Campaign != null || Session.ArmyCampaign != null) back.TooltipText = "Back to the campaign. Nothing is played or recorded.";
        Refresh(force: true);
    }

    public override void _ExitTree()
    {
        // Leaving the lobby stops hosting (releases the port) and remembers the settings for next time.
        Session.PersistHostSettings();
        Session.Dispose();
    }

    /// <summary>Seconds left to try the army campaign's automatic LAUNCH (0 = not trying).</summary>
    private double _autoLaunch;

    public override void _Process(double delta)
    {
        Session.Tick();
        TakeLaunch();
        FitTabs();
        if (_autoLaunch > 0)
        {
            _autoLaunch -= delta;
            // The #153 warnings gate is skipped: a campaign army was legal enough to start the campaign with. Hard
            // problems still block - then the lobby stays up with them on screen once the time runs out.
            if (!Session.LaunchStarted && Session.Blockers().Count == 0) { _autoLaunch = 0; StartGame(); }
        }
        _poll += delta;
        if (_poll < 0.25) return;
        _poll = 0;
        SharedLooks.Tick(Session);
        Refresh(force: false);
    }

    // ================================================================== refresh

    private void Refresh(bool force)
    {
        IReadOnlyList<LobbyPlayerInfoSummary> players = Vm.PlayerInfos;
        _selectedSeat = Math.Clamp(_selectedSeat, 0, Math.Max(0, players.Count - 1));

        if (Session.ArmyCampaign is { } ac)
        {
            _title.Text = ac.Title.ToUpperInvariant();
            _sub.Text = $"{GameSystemInfo.DisplayName(Vm.GameSystem)} - Campaign mission - {ac.Line}";
        }
        else if (Session.Campaign is { } camp)
        {
            _title.Text = camp.Title.ToUpperInvariant();
            _sub.Text = $"{GameSystemInfo.DisplayName(Vm.GameSystem)} - Campaign mission - {camp.Line}";
        }
        else
        {
            _title.Text = Session.HeaderTitle.ToUpperInvariant();
            _sub.Text = Vm.HasHostPrivileges ? HostStarter.TableName(UserConfig.Current.PlayerName) : "at " + Vm.ServerName;
        }

        string seatSig = string.Join("|", players.Select(p =>
            $"{p.PlayerID.ID}:{p.PlayerName}:{p.PlayerType}:{p.TeamNumber}:{p.ColorIndex}:{p.ArmyListSummary.ArmyName}:{p.ArmyListSummary.FactionName}:{p.ArmyListSummary.PointCost}:{p.ArmyListSummary.IsAssigned}"))
            + $"#{Vm.ArmyPoints}#{Vm.GameSystem}#{Vm.GameMode}#{LobbySession.ArmyCatalogReady}#{Vm.ExtraMission}#{Vm.AttackerTeam}#"
            + string.Join(",", players.Select(p => Session.PartyOf(p.PlayerID).Count));
        string settingsJson = JsonSerializer.Serialize(HostGameSettings.CaptureFrom(Vm));
        // Settings that add, remove or lock other rows (Mission cards lock the Mission; Attack & Defend locks
        // Side-missions; a side-mission style adds its pick row) rebuild the tabs for the host too.
        string settingsSig = $"{Vm.GameSystem}|{Vm.GameMode}|{Session.CanEdit}|{players.Count}|{_fastPlayOn}|{_extraActionsOn}|{string.Join(",", _advancedOpen)}|{(ulong)Vm.AdvancedRules}|{Vm.TerrainPlacementMode}|{Vm.TerrainLayoutPath}|"
            + $"{Vm.MissionCardGame}|{FDG.Missions.ExtraMissionCatalog.IsAttackDefend(Vm.ExtraMission)}|{Vm.SideMissions == FDG.Missions.ESideMissionPlay.Off}|{FDG.Missions.SideMissionCatalog.IsDash(Vm.SideMissions)}|"
            + (FDG.Missions.ExtraMissionCatalog.IsAttackDefend(Vm.ExtraMission)
                ? $"{Vm.ExtraMission}|{Vm.ArmyPoints}|" + string.Join(",", players.Select(p => $"{p.PlayerName}:{p.TeamNumber}")) : "")
            + (Session.CanEdit ? "" : settingsJson);

        if (force || seatSig != _seatSig)
        {
            _seatSig = seatSig;
            BuildTeamRow();
            BuildSeats();
            BuildSeatArmy();
        }
        if (force || settingsSig != _settingsSig)
        {
            _settingsSig = settingsSig;
            BuildSettings();
        }
        if (force || settingsJson + seatSig != _valueSig)
        {
            _valueSig = settingsJson + seatSig;
            _points?.SetValueNoSignal(Vm.ArmyPoints);
            SyncQuickSize();
            _preview.Session = Session;
            _preview.QueueRedraw();
            CallDeferred(nameof(UpdatePreviewNote));
            UpdateLaunch();
        }
    }

    private void UpdatePreviewNote() => _previewNote.Text = _preview.Note;

    // ================================================================== settings tabs

    private void BuildSettings()
    {
        int current = Math.Max(0, _tabs.CurrentTab);
        foreach (Node n in _tabs.GetChildren()) { _tabs.RemoveChild(n); n.QueueFree(); }
        _points = null; _quickSize = null;

        EGameSystem system = Vm.GameSystem;
        EGameMode mode = Vm.GameMode;
        bool locked = !Session.CanEdit;

        AddTab("Game", "How big the game is: the points each army is built to. Also the bot's Challenge Bonuses.", rows => GameTab(rows, locked));
        AddTab("Table", "The table: map, size, look, deployment" + (LobbyDeploymentPreview.MissionCardsApply(system, mode) ? " and mission cards." : "."), rows => TableTab(rows, locked));
        AddTab("Rules", "Dice, and the house rules that change the books' rules for this game.", rows => RulesTab(rows, locked));
        if (LobbyDeploymentPreview.FastPlayApplies(system) || LobbyDeploymentPreview.KingOfTheHillApplies(system, mode) || LobbyDeploymentPreview.ExtraActionsApply(system, mode))
            AddTab("Advanced", "Optional rules from this game's Advanced Rulebook.", rows => AdvancedTab(rows, locked));
        if (Vm.HasHostPrivileges) AddTab("Connect", "The address friends type into Join a Game to come to this lobby.", ConnectTab);
        _tabs.CurrentTab = Math.Min(current, _tabs.GetTabCount() - 1);
    }

    private int _tabFont;

    /// <summary>
    /// Every settings tab stays in view. A tab bar that runs out of room hides its last tabs behind two small arrows, and
    /// with the Fantasy look's wider letters that was "Connect" - the address friends type to join. The tab letters step
    /// down a size at a time until all the tabs fit (a frame or two after the lobby opens).
    /// </summary>
    private void FitTabs()
    {
        if (!IsVisibleInTree() || !_tabs.GetTabBar().GetOffsetButtonsVisible()) return;
        if (_tabFont == 0) _tabFont = _tabs.GetThemeFontSize("font_size");
        if (_tabFont <= 13) return;
        _tabs.AddThemeFontSizeOverride("font_size", --_tabFont);
    }

    private void AddTab(string name, string tooltip, Action<VBoxContainer> build)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(rows);
        _tabs.AddChild(scroll);
        _tabs.SetTabTooltip(_tabs.GetTabCount() - 1, tooltip);
        if (!Session.CanEdit)
            rows.AddChild(Note(Vm.HasHostPrivileges ? "Resuming a save - the settings are the save's." : "The host sets these. You pick your army and colour on your seat card."));
        build(rows);
    }

    private static Label Note(string s)
    {
        var l = HudKit.Text(s, 15, HudKit.Dim, HudKit.Semi);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(10, 0);
        return l;
    }

    /// <summary>A setting: its name (with the hover) over its control, full width.</summary>
    private static void Row(VBoxContainer rows, string label, string tooltip, Control control)
    {
        var v = new VBoxContainer { TooltipText = tooltip };
        v.AddThemeConstantOverride("separation", 2);
        var l = HudKit.Text(label.ToUpperInvariant(), 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2));
        l.TooltipText = tooltip;
        l.MouseFilter = MouseFilterEnum.Stop;
        v.AddChild(l);
        control.TooltipText = string.IsNullOrEmpty(control.TooltipText) ? tooltip : control.TooltipText;
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        v.AddChild(control);
        rows.AddChild(v);
    }

    private static CheckBox Check(VBoxContainer rows, string label, bool on, string tooltip, bool locked, Action<bool> set, int indent = 0)
    {
        var c = new CheckBox { Text = label, ButtonPressed = on, TooltipText = tooltip, Disabled = locked,
            Name = "Check_" + string.Concat(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')) };
        c.Toggled += v => set(v);
        if (indent > 0) rows.AddChild(HudKit.Pad(c, indent, 0, 0, 0));
        else rows.AddChild(c);
        return c;
    }

    /// <summary>A dropdown of <paramref name="values"/>, each item with its own hover.</summary>
    private static OptionButton Choice<T>(IReadOnlyList<T> values, T current, Func<T, string> name, Func<T, string?>? itemTip, bool locked, Action<T> set)
    {
        var ob = new OptionButton { Disabled = locked, FitToLongestItem = false };
        for (int i = 0; i < values.Count; i++)
        {
            ob.AddItem(name(values[i]), i);
            if (itemTip?.Invoke(values[i]) is { Length: > 0 } tip) ob.GetPopup().SetItemTooltip(i, tip);
            if (EqualityComparer<T>.Default.Equals(values[i], current)) ob.Selected = i;
        }
        ob.ItemSelected += i => set(values[(int)i]);
        return ob;
    }

    private void GameTab(VBoxContainer rows, bool locked)
    {
        if (GameSystemInfo.IsQuest(Vm.GameSystem))
        {
            QuestGameRows(rows);
            return;
        }
        bool skirmish = GameSystemInfo.IsSkirmish(Vm.GameSystem);
        // The arrows step in the usual sizes; a typed number is kept as typed. (With Step = 250 the box could only hold
        // multiples of 250: typing 1100 became 1000, and a campaign mission's 835 showed as 750.)
        _points = new SpinBox { MinValue = 0, MaxValue = 100000, Step = 1, CustomArrowStep = skirmish ? 25 : 250, Value = Vm.ArmyPoints, Editable = !locked, AllowGreater = true, Suffix = "pts" };
        _points.ValueChanged += v => { if ((int)v != Vm.ArmyPoints) Vm.SetArmyPoints((int)v); };
        Row(rows, "Army points", "The point budget each player's army is built to. An army over this limit is flagged red on its seat card and blocks the launch. In a team game the limit is for the whole side.", _points);

        int step = skirmish ? 50 : 1000, max = skirmish ? 1000 : 10000;
        var sizes = Enumerable.Range(1, max / step).Select(i => i * step).ToList();
        _quickSize = new OptionButton { Disabled = locked };
        _quickSize.AddItem("Custom", 0);
        foreach (int pts in sizes) _quickSize.AddItem(skirmish ? $"{pts} pts" : $"{pts / 1000}k ({pts} pts)", pts);
        _quickSize.ItemSelected += i => { int pts = _quickSize.GetItemId((int)i); if (pts > 0) Vm.SetArmyPoints(pts); };
        Row(rows, "Quick size", skirmish ? "Common sizes for skirmish games (50 to 1000 points; Firefight is usually 200 or 300)." : "Common army sizes (1000 to 10000 points).", _quickSize);
        SyncQuickSize();

        if (ChallengeBonuses.Applies(Vm.GameSystem))
        {
            rows.AddChild(HudKit.Gap(0, 6));
            var head = HudKit.Heading(ChallengeBonusesText.Title, 14);
            head.TooltipText = ChallengeBonusesText.Intro;
            head.MouseFilter = MouseFilterEnum.Stop;
            rows.AddChild(head);
            EChallengeBonuses cur = Vm.ChallengeBonuses;
            foreach ((string label, EChallengeBonuses flag, string book) in ChallengeBonusesText.Bonuses)
                Check(rows, label, (cur & flag) != 0, book, locked, on =>
                {
                    EChallengeBonuses now = Vm.ChallengeBonuses;
                    Vm.SetChallengeBonuses(on ? now | flag : now & ~flag);
                }, 16);
        }
    }

    /// <summary>
    /// A Quest game's Game tab. The party has no points limit - the book sizes the enemy waves from the heroes' points
    /// (and, in a mission, the difficulty; see QuestLobbyText). A campaign mission shows what its campaign has set, locked.
    /// </summary>
    private void QuestGameRows(VBoxContainer rows)
    {
        if (Session.Campaign is { } c)
        {
            rows.AddChild(HudKit.Heading("Set by the campaign", 14));
            void Fact(string name, string value, string tip)
            {
                var h = new HBoxContainer { TooltipText = tip, MouseFilter = MouseFilterEnum.Stop };
                h.AddChild(HudKit.Text(name, 16, HudKit.Dim, HudKit.Semi).Also(l => { l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.MouseFilter = MouseFilterEnum.Ignore; }));
                h.AddChild(HudKit.Text(value, 17, HudKit.Fg, HudKit.Bold).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
                rows.AddChild(HudKit.Panel(HudKit.PanelBox(6), HudKit.Pad(h, 10, 6, 10, 6)).Also(p => { p.TooltipText = tip; p.MouseFilter = MouseFilterEnum.Pass; }));
            }
            Fact("Campaign", c.CampaignName, "The campaign this mission belongs to. Its result is recorded there.");
            Fact("Chapter", $"{c.ChapterNumber} - mission {c.MissionNumber} of {c.MissionsInChapter}", "Once a chapter has started, all its missions are played before the next (book).");
            Fact("Difficulty", FdgRaylib.Quest.QuestJournal.DifficultyName(c.Difficulty), "Picked when the chapter started; it is the same for every mission of the chapter (book). It sets the enemy wave size, the hazard pay and the XP.");
            Fact("Primary", c.Primary > 0 ? FdgRaylib.Quest.QuestJournal.PrimaryName(c.Primary) : "Rolled at the start", "The mission's primary objective, from the book's table. The heroes complete it, then all reach one table corner to end the mission.");
            Fact("Secondary", c.Secondary > 0 ? FdgRaylib.Quest.QuestJournal.SecondaryName(c.Secondary) : "Rolled when the primary is done", "Book: the secondary objective is set up after the primary is completed.");
            Fact("Enemy", c.EnemyName, "The chapter's enemy force, on the enemy seat.");
            rows.AddChild(Note("These come from the campaign and can't be changed here. The map, the battlefield look and the rules are still yours."));
            return;
        }
        rows.AddChild(Note(FdgRaylib.Rendering.QuestLobbyText.GameNote(Vm.GameMode)));
    }

    private void SyncQuickSize()
    {
        if (_quickSize == null) return;
        int idx = 0;
        for (int i = 1; i < _quickSize.ItemCount; i++) if (_quickSize.GetItemId(i) == Vm.ArmyPoints) idx = i;
        _quickSize.Selected = idx;
    }

    private void TableTab(VBoxContainer rows, bool locked)
    {
        EGameSystem system = Vm.GameSystem;
        EGameMode mode = Vm.GameMode;

        // Map: Random, or a map drawn for this table size - picked on the full-screen Select Map.
        string? currentMap = Session.HasMap ? Vm.TerrainLayoutPath : null;
        string mapName = currentMap == null ? "Random"
            : TerrainLayoutLoader.TryLoadFromFile(currentMap, out _) is { Name.Length: > 0 } l ? l.Name : Path.GetFileNameWithoutExtension(currentMap);
        var mapButton = Ui.Button(mapName + "  ...", () => OpenMapPicker(locked), "Pick the table: Random, or a map made in Map Maker for this table size.");
        mapButton.Name = "PickMap";
        mapButton.Alignment = HorizontalAlignment.Left;
        Row(rows, "Map", "The table you will fight on.\nRandom: the game lays out terrain when the game starts.\nA map: its terrain and objective markers, exactly as drawn. Only maps made for this game's table size are offered. Make maps in Map Maker.",
            mapButton);
        if (TerrainBook.HasGenerator(system))
            Check(rows, "Dangerous terrain picks (D3 each)", Vm.PickDangerousTerrain,
                "The last step of the Advanced Rulebook's Generic terrain table:\n\n\"" + LobbyBookText.DangerousTerrainPicks + "\"\n\n"
                + "Ticked by itself when the map was rolled on the Generic table in Map Maker. Can be ticked for any table. "
                + "House rules: one D3 for everyone, and Impassable pieces or pieces that are already Dangerous can't be picked.",
                locked, v => Vm.SetPickDangerousTerrain(v), 24);

        if (TableSizing.CanGrow(system, mode))
        {
            (float W, float H)? chosen = Vm.ChosenTableSize;
            (float W, float H) auto = TableSizing.AutoSize(system, mode, Vm.ArmyPoints);
            (float W, float H) book = GameWideConstants.TableSizeFor(system);
            var sizes = new List<(float W, float H)?> { null };
            sizes.AddRange(TableSizing.SizesFor(system).Select(s => ((float, float)?)s));
            (float W, float H)? selected = chosen is { } c ? sizes.FirstOrDefault(s => s is { } v && TableSizing.Same(v, c)) : null;
            Row(rows, "Table size",
                "How big the table is. The books play every game on one table (6' x 4' for Grimdark Future and Age of Fantasy, 4' x 4' for Firefight and Skirmish). Automatic keeps the book table up to a full-size game and makes it bigger for bigger armies. A map always plays on the table it was drawn for. House rule above the book table.",
                Choice(sizes, selected,
                    s => s is { } v ? TableSizing.Label(v.W, v.H) + (TableSizing.Same(v, book) ? " (the book's table)" : "") : $"Automatic - {TableSizing.Label(auto.W, auto.H)}",
                    s => s is { } v ? (TableSizing.Same(v, book) ? "The table the core rules play on." : $"A bigger table - house rule. Roughly right for {TableSizing.SuggestedPoints(system, v.W, v.H)} pts per player.")
                        : $"Picks the table from the army points: {Vm.ArmyPoints} pts per player plays on {TableSizing.Label(auto.W, auto.H)}.",
                    locked || Session.HasMap, s => Vm.SetTableSize(s)));
        }

        // "Battlefield" (the table surface, Forest / Desert...) is not offered here: only the Raylib build draws it. The Godot
        // table takes its ground, sky and weather from the map (Map Maker -> Battlefield...), so the row changed nothing
        // (lane 3 click-walk, 2026-11-27). The setting itself still rides the lobby for the Raylib build.

        if (LobbyDeploymentPreview.DeploymentApplies(system, mode))
        {
            var order = new[] { EDeploymentStyle.Core }
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Standard)).Append(EDeploymentStyle.RandomStandard)
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Strategic)).Append(EDeploymentStyle.RandomStrategic)
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Asymmetric)).Append(EDeploymentStyle.RandomAsymmetric)
                .ToArray();
            Row(rows, "Deployment", "Where each side sets up: the core rules' layout, or one of the Advanced Rulebook's 18 deployment styles for " + GameSystemInfo.DisplayName(system) + ". The map preview shows the zones. Hover an entry for the book's words.",
                Choice(order, Vm.DeploymentStyle, AdvancedDeployment.Name, LobbyBookText.Deployment, locked, s => Vm.SetDeploymentStyle(s)));
        }

        if (LobbyDeploymentPreview.MissionCardsApply(system, mode))
            Row(rows, "Mission cards", LobbyBookText.MissionCardsIntro + "\nHover an entry for that game type.",
                Choice(Enum.GetValues<FDG.Missions.EMissionCardGame>(), Vm.MissionCardGame, FDG.Missions.MissionCardCatalog.Name, LobbyBookText.MissionCards, locked, g => Vm.SetMissionCardGame(g)));

        // Extra Missions (Advanced Rulebooks 3.6.0, Kai 2026-11-22). Mission Cards bring their own objectives and scoring,
        // so the mission is the core one while they are on.
        if (mode == EGameMode.Standard && FDG.Missions.ExtraMissionCatalog.Available(system))
            Row(rows, "Mission", "The Advanced Rulebook's Extra Missions: how many objectives there are, where they go, and how the game is won. "
                + "Duel is the core rules' mission. Not used with Mission cards. Hover an entry for the book's words.",
                Choice(FDG.Missions.ExtraMissionCatalog.LobbyOrder.ToArray(), Vm.ExtraMission, FDG.Missions.ExtraMissionCatalog.Name,
                    FDG.Missions.ExtraMissionCatalog.Text, locked || Vm.MissionCardGame != FDG.Missions.EMissionCardGame.Off,
                    m => Vm.SetExtraMission(m)));

        // Attack & Defend: who attacks, picked here so the lists can be built to the attacker's extra points (Kai, 2026-11-23).
        if (mode == EGameMode.Standard && FDG.Missions.ExtraMissionCatalog.IsAttackDefend(Vm.ExtraMission))
        {
            var teams = Vm.PlayerInfos.Select(p => (int)p.TeamNumber).Where(t => t > 0).Distinct().OrderBy(t => t).ToList();
            string TeamLabel(int t)
            {
                if (t < 0) return "Roll-off at the start";
                string names = string.Join(", ", Vm.PlayerInfos.Where(p => (int)p.TeamNumber == t).Select(p => p.PlayerName));
                return names.Length > 0 ? $"{names} attacks" : $"Team {t} attacks";
            }
            int current = FDG.Missions.AttackDefend.AttackerOf(Vm.AttackerTeam);
            var options = new List<int> { -1 };
            options.AddRange(teams);
            if (!options.Contains(current)) options.Add(current);
            bool bonus = FDG.Missions.ExtraMissionCatalog.Of(Vm.ExtraMission).AttackerBonusPoints;
            Row(rows, "Attacker", "Who attacks in this Attack & Defend mission. The book has the players roll off at the start; picking here lets the lists be built for it."
                + (bonus ? $"\nThis mission gives the attacking army 25% more points: the attacker's seat may take up to {FDG.Missions.AttackDefend.PointsLimit(Vm.ArmyPoints, Vm.ExtraMission, 1, 1)} pts." : ""),
                Choice(options.ToArray(), current, TeamLabel,
                    t => t < 0 ? "The book's way: roll off when the game starts; the winner picks attacker or defender. The lists are built to the same points."
                        : teams.Contains(t) ? $"{TeamLabel(t)}; the other side defends (sets up the marker(s))." : "That side is not in the game - the roll-off is used.",
                    locked, t => Vm.SetAttackerTeam(t)));
        }

        // Meat Grinder recruitment: the starting lists are bought out of half the game size (Kai, 2026-11-25).
        if (mode == EGameMode.Standard && FDG.Missions.MeatGrinder.IsRecruitment(Vm.ExtraMission))
        {
            int start = FDG.Missions.MeatGrinder.StartingPoints(Vm.ArmyPoints);
            var note = HudKit.Text($"Meat Grinder recruitment: each list may be up to {start} pts (half the game size). Unspent points are banked; "
                + $"each round you earn more and may spend up to {FDG.Missions.MeatGrinder.MaxSpend(Vm.ArmyPoints)} pts on new units.", 13, HudKit.Dim, HudKit.Semi);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.CustomMinimumSize = new Vector2(420, 0);
            rows.AddChild(note);
        }

        // Side-Missions (Advanced Rulebooks 3.6.0): extra objectives worth VP; scoring becomes victory points.
        if (mode == EGameMode.Standard && FDG.Missions.SideMissionCatalog.Available(system))
        {
            bool sideLocked = locked || Vm.MissionCardGame != FDG.Missions.EMissionCardGame.Off
                || FDG.Missions.ExtraMissionCatalog.IsAttackDefend(Vm.ExtraMission);
            Row(rows, "Side-missions", "The Advanced Rulebook's Side-Missions: extra goals (36 of them, in 6 categories) worth victory points. "
                + "With them on, the game is won on VP - markers held plus side-missions done. Not with Mission cards or Attack & Defend missions. Hover an entry for the book's words.\n"
                + FDG.Missions.SideMissionCatalog.ScoringText,
                Choice(Enum.GetValues<FDG.Missions.ESideMissionPlay>(), Vm.SideMissions, FDG.Missions.SideMissionCatalog.PlayName,
                    FDG.Missions.SideMissionCatalog.PlayText, sideLocked, p => Vm.SetSideMissions(p)));
            if (Vm.SideMissions != FDG.Missions.ESideMissionPlay.Off && !FDG.Missions.SideMissionCatalog.IsDash(Vm.SideMissions))
                Row(rows, "Side-mission pick", "How each side-mission is chosen. Hover an entry for the book's words.",
                    Choice(Enum.GetValues<FDG.Missions.ESideMissionPick>(), Vm.SideMissionPick, FDG.Missions.SideMissionCatalog.PickName,
                        FDG.Missions.SideMissionCatalog.PickText, sideLocked, p => Vm.SetSideMissionPick(p)));
        }
    }


    private void RulesTab(VBoxContainer rows, bool locked)
    {
        EGameSystem system = Vm.GameSystem;
        Row(rows, "Randomness", "How the game settles dice rolls. An app setting, not a book rule.",
            Choice(Enum.GetValues<ERandomnessType>(), Vm.RandomnessType, r => r.ToString(),
                r => r == ERandomnessType.Realistic
                    ? "Realistic: real dice are rolled - whole hits, swingy and unpredictable. How the books play."
                    : "Probabilistic: results use expected values - smoother, less luck. Not how the books play.",
                locked, r => Vm.SetRandomnessType(r)));

        rows.AddChild(HudKit.Heading("House rules", 14));
        Check(rows, "Cover Proximity Rules", Vm.CoverProximityExceptions, LobbyBookText.WithBook(
            "House rule (default on): cover that the shooter's muzzle hugs (under 2\" to the exit) gives nothing unless the target hugs the same piece, and cover shared by shooter and target gives nothing when they are closer than 6\".",
            LobbyBookText.CoreCover(system)), locked, v => Vm.SetCoverProximityExceptions(v));
        Check(rows, "See-Through Allies", Vm.SeeThroughFriendlyUnits,
            "House rule (default off): shots see through ALL friendly units. Off: only the shooting unit's own models and the target unit are ignored for line of sight - every other unit, friendly or enemy, blocks it.",
            locked, v => Vm.SetSeeThroughFriendlyUnits(v));
        Check(rows, "Unlimited Split Fire", Vm.UnlimitedSplitFire, LobbyBookText.WithBook(
            "House rule (default off): a shooting unit may split its fire between any number of enemy units.", LobbyBookText.CoreShooting(system)),
            locked, v => Vm.SetUnlimitedSplitFire(v));
        Check(rows, "Friendly Units May Overlap", Vm.FriendlyUnitsMayOverlap, LobbyBookText.WithBook(
            "House rule (default ON): your own units may finish a move within 1\" of each other and may move past each other. Models still cannot END on top of another model, and enemies still need the 1\" gap.",
            LobbyBookText.CoreUnitSpacing(system)), locked, v => Vm.SetFriendlyUnitsMayOverlap(v));

        if (LobbyDeploymentPreview.GameLengthApplies(Vm.GameMode))
        {
            int[] lengths = { 4, 5, 6, 8 };
            Row(rows, "Game length", LobbyBookText.WithBook("How many rounds a standard game lasts. Anything but 4 is a house rule.", LobbyBookText.CoreGameLength),
                Choice(lengths, Vm.NumberOfRounds ?? 4, n => n == 4 ? "4 rounds (book)" : $"{n} rounds",
                    n => n == 4 ? "The book: " + LobbyBookText.CoreGameLength : $"House rule: the game ends after {n} rounds instead of 4.",
                    locked, n => Vm.SetNumberOfRounds(n == 4 ? null : n)));
        }
    }

    private void AdvancedTab(VBoxContainer rows, bool locked)
    {
        EGameSystem system = Vm.GameSystem;
        EGameMode mode = Vm.GameMode;

        if (LobbyDeploymentPreview.FastPlayApplies(system))
        {
            bool move = Vm.FastPlayMovement, melee = Vm.FastPlayMelee, shoot = Vm.FastPlayShooting, terrain = Vm.FastPlaySimplifiedTerrain;
            bool on = _fastPlayOn || move || melee || shoot || terrain;
            Check(rows, "Fast-Play", on, LobbyBookText.FastPlayIntro(system) + "\nTurn it on, then pick any of the four rules.", locked, v =>
            {
                _fastPlayOn = v;
                if (!v) { Vm.SetFastPlay(false, false, false); Vm.SetFastPlaySimplifiedTerrain(false); }
            });
            if (on)
            {
                Check(rows, "Leader-Based Movement", move, LobbyBookText.LeaderMovement, locked, v => Vm.SetFastPlay(v, Vm.FastPlayMelee, Vm.FastPlayShooting), 24);
                Check(rows, "Leader-Based Melee", melee, LobbyBookText.LeaderMelee, locked, v => Vm.SetFastPlay(Vm.FastPlayMovement, v, Vm.FastPlayShooting), 24);
                Check(rows, "Leader-Based Shooting", shoot, LobbyBookText.LeaderShooting, locked, v => Vm.SetFastPlay(Vm.FastPlayMovement, Vm.FastPlayMelee, v), 24);
                Check(rows, "Simplified Terrain", terrain, LobbyBookText.SimplifiedTerrain, locked, v => Vm.SetFastPlaySimplifiedTerrain(v), 24);
            }
        }

        // Force Organisation (Kai, 2026-11-15): the Advanced Rulebooks' optional army caps. Not for Quest (heroes) or a
        // campaign mission (the campaign books: "players may bring any number of their units").
        if (!GameSystemInfo.IsQuest(system) && Session.ArmyCampaign == null)
            Check(rows, "Force Organisation", Vm.ForceOrg, LobbyBookText.ForceOrg(system), locked, v => Vm.SetForceOrg(v));

        // Volumetric Line of Sight (Advanced Rulebooks, Tournament Guidelines) - every game with an Advanced Rulebook.
        if (LobbyDeploymentPreview.ExtraActionsApply(system, mode))
            Check(rows, "Volumetric Line of Sight", Vm.VolumetricLineOfSight, LobbyBookText.VolumetricLineOfSight, locked,
                v => Vm.SetVolumetricLineOfSight(v));

        // Advanced Terrain - Solid Buildings (Kai, 2026-11-22).
        if (LobbyDeploymentPreview.ExtraActionsApply(system, mode))
            Check(rows, "Solid Buildings", Vm.SolidBuildings, LobbyBookText.SolidBuildings, locked || Vm.FastPlaySimplifiedTerrain,
                v => Vm.SetSolidBuildings(v));

        if (LobbyDeploymentPreview.KingOfTheHillApplies(system, mode))
            Check(rows, "King of the Hill (3-4 sides)", Vm.KingOfTheHill, LobbyBookText.KingOfTheHill + "\n\n" + LobbyBookText.FreeForAll(system), locked, v => Vm.SetKingOfTheHill(v));

        if (LobbyDeploymentPreview.ExtraActionsApply(system, mode))
        {
            EExtraActions extra = Vm.ExtraActions;
            bool on = _extraActionsOn || extra != EExtraActions.None;
            Check(rows, "Extra Actions", on, LobbyBookText.ExtraActionsIntro + "\nTurn it on, then tick the ones you want.", locked, v =>
            {
                _extraActionsOn = v;
                if (!v) Vm.SetExtraActions(EExtraActions.None);
            });
            if (on)
                foreach ((string name, EExtraActions flag) in FDG.ExtraActions.All)
                    Check(rows, name, (extra & flag) != 0, name + ": " + LobbyBookText.ExtraAction(name), locked, v =>
                    {
                        EExtraActions now = Vm.ExtraActions;
                        Vm.SetExtraActions(v ? now | flag : now & ~flag);
                    }, 24);
        }

        // The rest of the Advanced Rulebook's optional systems (Kai, 2026-11-23: "do everything"): a tick box per
        // chapter, then one per option, each hover the book's words and then what the game does.
        if (LobbyDeploymentPreview.ExtraActionsApply(system, mode))
            foreach (string chapter in FDG.AdvancedRules.Chapters(system, mode))
            {
                FDG.EAdvancedRules ticked = Vm.AdvancedRules & FDG.AdvancedRules.ChapterFlags(chapter);
                bool open = _advancedOpen.Contains(chapter) || ticked != FDG.EAdvancedRules.None;
                Check(rows, chapter, open, FDG.AdvancedRules.ChapterIntro.GetValueOrDefault(chapter, "") + "\nTurn it on, then tick the ones you want.",
                    locked, v =>
                    {
                        if (v) _advancedOpen.Add(chapter);
                        else { _advancedOpen.Remove(chapter); Vm.SetAdvancedRules(Vm.AdvancedRules & ~FDG.AdvancedRules.ChapterFlags(chapter)); }
                    });
                if (!open) continue;
                foreach (FDG.AdvancedRuleOption option in FDG.AdvancedRules.In(chapter, system, mode))
                    Check(rows, option.Name, (Vm.AdvancedRules & option.Flag) != 0,
                        option.Name + ": " + option.BookText + "\n\nIn this game: " + option.GameText
                        + (option.Flag == FDG.EAdvancedRules.CriticalHitsByWeaponType ? "\n\n" + FDG.CriticalHits.AllTablesText(system) : ""), locked, v =>
                        {
                            // 2D6 Casting and Magic Pools can't both be on: ticking one clears the other.
                            Vm.SetAdvancedRules(FDG.AdvancedCasting.Tick(Vm.AdvancedRules, option.Flag, v));
                        }, 24);
                // Command Points: which playstyle - the book's roll, or one picked (bits in AdvancedRules).
                if (chapter == FDG.AdvancedRules.CommandPointsChapter && (Vm.AdvancedRules & FDG.EAdvancedRules.CommandPoints) != 0)
                {
                    var cps = new FDG.GameSettings { GameSystem = system, AdvancedRules = Vm.AdvancedRules };
                    var styles = Enumerable.Range(0, 7).ToList();
                    Row(rows, "Playstyle", "How Command Points are earned. Each book counts per " + FDG.CommandPoints.Step(system) + " pts of army.",
                        Choice(styles, FDG.CommandPoints.Picked(cps),
                            p => p == 0 ? "Roll for it (book)" : $"{p}. {FDG.CommandPoints.PlaystyleName(p)}",
                            p => FDG.CommandPoints.PlaystyleText(p, system),
                            locked, p => Vm.SetAdvancedRules(FDG.CommandPoints.WithPick(Vm.AdvancedRules, p))));
                }
                // Relic Wargear / Magic Items: the selection method - the book's D3, or one picked (bits in AdvancedRules).
                if (chapter == FDG.AdvancedRules.AdvancedCastingChapter && (Vm.AdvancedRules & FDG.EAdvancedRules.RelicWargear) != 0)
                {
                    var rs = new FDG.GameSettings { GameSystem = system, AdvancedRules = Vm.AdvancedRules };
                    Row(rows, "Selection", "How each Hero gets its " + (FDG.RelicWargear.OptionName(system) == "Magic Items" ? "magic item" : "relic") + ". The same for every player.",
                        Choice(Enumerable.Range(0, 4).ToList(), FDG.RelicWargear.Picked(rs),
                            m => m == 0 ? "Roll for it (book)" : $"{m}. {FDG.RelicWargear.MethodName(m)}",
                            m => FDG.RelicWargear.MethodText(m),
                            locked, m => Vm.SetAdvancedRules(FDG.RelicWargear.WithPick(Vm.AdvancedRules, m))));
                }
                // Battlefield Effects: which Special Condition - the book's roll, or one picked (bits in AdvancedRules).
                if (chapter == FDG.AdvancedRules.BattlefieldEffectsChapter && (Vm.AdvancedRules & FDG.EAdvancedRules.SpecialConditions) != 0)
                {
                    var settings = new FDG.GameSettings { GameSystem = system, AdvancedRules = Vm.AdvancedRules };
                    var codes = Enumerable.Range(0, 13).ToList();
                    Row(rows, "Special condition", "Pick the condition, or let the book's dice decide before round 1.",
                        Choice(codes, FDG.BattlefieldEffects.Picked(settings),
                            c => c == 0 ? "Roll for it (book)" : FDG.BattlefieldEffects.Name(c, system),
                            c => c == 0 ? "1-2 None, 3-4 Geological, 5-6 Mysterious, then a die." : FDG.BattlefieldEffects.Text(c, system),
                            locked, c => Vm.SetAdvancedRules(FDG.BattlefieldEffects.WithPick(Vm.AdvancedRules, c))));
                }
            }
    }

    private static void ConnectTab(VBoxContainer rows)
    {
        rows.AddChild(Note("Friends join with Join a Game on their main menu, typing this address:"));
        IReadOnlyList<string> tailscale = TailscaleInfo.Addresses();
        if (tailscale.Count == 0)
        {
            rows.AddChild(Note("Tailscale isn't running on this computer. Start it to play with friends."));
            return;
        }
        foreach (string address in new[] { TailscaleInfo.FullName(), tailscale[0] }.OfType<string>())
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 10);
            h.AddChild(HudKit.Text(address, 20, HudKit.Lime, HudKit.Bold));
            h.AddChild(Ui.Button("Copy", () => DisplayServer.ClipboardSet(address), "Copy it to send to a friend."));
            rows.AddChild(h);
        }
    }

    // ================================================================== seats

    private void BuildTeamRow()
    {
        foreach (Node n in _teamRow.GetChildren()) n.QueueFree();
        _teamRow.AddChild(HudKit.Heading("Seats", 15));
        _teamRow.AddChild(HudKit.Gap(12));
        bool canEdit = Session.CanEdit && Vm.PlayerInfos.Count >= 3;
        var two = Ui.Button("2v2", Session.TeamsTwoVsTwo, "Two sides: the left column against the right column. Needs at least three seats.");
        var ffa = Ui.Button("Free-for-all", Session.TeamsFreeForAll, "Everyone for themselves - each seat on its own team. Needs at least three seats.");
        two.Disabled = ffa.Disabled = !canEdit;
        two.SizeFlagsVertical = ffa.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _teamRow.AddChild(two);
        _teamRow.AddChild(ffa);
        _teamRow.AddChild(HudKit.Text("(or set a seat's team on its card)", 15, HudKit.Dim, HudKit.Semi));
    }

    private void BuildSeats()
    {
        foreach (Node n in _seatGrid.GetChildren()) { _seatGrid.RemoveChild(n); n.QueueFree(); }
        IReadOnlyList<LobbyPlayerInfoSummary> players = Vm.PlayerInfos;
        int[] colours = Session.EffectiveColours();
        int count = Math.Max(LobbySession.SeatGridSize, players.Count);
        for (int seat = 0; seat < count; seat++)
            _seatGrid.AddChild(seat < players.Count ? SeatCard(seat, players[seat], colours) : EmptySeat(seat));
    }

    private static Color SeatColour(int code)
    {
        Rgb c = PlayerColorCodes.ColorFor(code);
        return Color.Color8(c.R, c.G, c.B);
    }

    private Control SeatCard(int seat, LobbyPlayerInfoSummary info, int[] colours)
    {
        bool selected = seat == _selectedSeat;
        Color colour = SeatColour(colours[seat]);
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Name = $"Seat{seat + 1}",
            TooltipText = "Click the card (not a button) to show this seat's army under the map." };
        card.AddThemeStyleboxOverride("panel", SeatCardBox(selected, colour));
        card.SetMeta("seat_colour", colour);
        card.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) SelectSeat(seat); };

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        card.AddChild(col);

        // Faction band in the seat's colour.
        var band = new PanelContainer();
        band.AddThemeStyleboxOverride("panel", new ChamferBox { Fill = colour with { A = 0.55f }, Border = Colors.Transparent, BorderWidth = 0, Cut = 6, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 3 });
        band.AddChild(HudKit.Text(info.ArmyListSummary.IsAssigned ? info.ArmyListSummary.FactionName : "No army yet", 16, Colors.White, HudKit.Bold));
        band.MouseFilter = MouseFilterEnum.Ignore;
        if (info.ArmyListSummary.IsAssigned && info.ArmyListSummary.FactionName is { Length: > 0 } bandArmy)
        {
            // A strip of the army's picture behind the band (Kai, 2026-11-16, "Menu Art & Themes Mockup": yes).
            var strip = new Control { CustomMinimumSize = new Vector2(0, 58), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
            Control art = MenuArt.View(FdgRaylib.Pictures.MenuPictures.Army(Vm.GameSystem, bandArmy), Vm.GameSystem);
            art.SetAnchorsPreset(LayoutPreset.FullRect);
            strip.AddChild(art);
            band.SetAnchorsPreset(LayoutPreset.BottomLeft);
            band.GrowVertical = GrowDirection.Begin;
            band.OffsetBottom = -4; band.OffsetLeft = 4;
            strip.AddChild(band);
            col.AddChild(strip);
        }
        else col.AddChild(band);

        // SEAT n, Player / Bot, Remove.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(HudKit.Text($"SEAT {seat + 1}", 15, Palette.Header, HudKit.Tracked(HudKit.Bold, 2)));
        if (!Vm.IsResumeMode && Vm.CheckCanSetPlayerType(info.PlayerID))
        {
            bool bot = info.PlayerType == EPlayerType.AI;
            var toggle = Ui.Button(bot ? "Bot" : "Player", () => Session.ToggleBot(info),
                bot ? "A bot is playing this seat. Click to take it yourself.\n" + OfficialAiTip
                    : "You are playing this seat. Click to hand it to a bot. Hand every seat to a bot to watch the game play itself.");
            toggle.CustomMinimumSize = new Vector2(90, 0);
            head.AddChild(toggle);
            // Which bot (Kai, 2026-11-15): the official AI by default, Tactical and Expert pickable.
            if (bot && Session.CanPickBrain)
            {
                var brains = LobbySession.Brains.Select(b => b.Profile).ToList();
                var brain = Choice(brains, Session.BrainOf(info),
                    p => LobbySession.Brains.First(b => b.Profile == p).Name,
                    p => LobbySession.Brains.First(b => b.Profile == p).Tip,
                    false, p => Session.SetBrain(info, p));
                brain.Name = "SeatBrain";
                brain.TooltipText = "Which bot plays this seat.";
                head.AddChild(brain);
            }
        }
        else head.AddChild(HudKit.Chip(Session.SeatOwnerLabel(info).ToUpperInvariant(), HudKit.Dim, false, 13));
        head.AddChild(HudKit.Grow());
        if (seat >= 2 && Vm.CheckCanRemovePlayer(info.PlayerID))
        {
            bool canOff = LobbySession.SeatCanSwitchOff(seat, Vm.PlayerInfos.Count);
            var remove = Ui.Button("Remove", () => Vm.RemovePlayer(info.PlayerID),
                canOff ? "Take this seat out of the game, to play with fewer players." : $"Remove seat {Vm.PlayerInfos.Count} first - seats come off the end.", "BackButton");
            remove.Disabled = !canOff;
            head.AddChild(remove);
        }
        col.AddChild(head);

        var name = HudKit.Text(info.PlayerName, 24, HudKit.Fg, HudKit.Bold);
        name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.TooltipText = info.PlayerName;
        name.MouseFilter = MouseFilterEnum.Pass;
        col.AddChild(name);

        // The army: a button that opens the picker, Roll for another, and faction - points.
        bool canModify = !Vm.IsResumeMode && Vm.CheckCanModifyPlayerIDInfo(info.PlayerID);
        bool heroSeat = GameSystemInfo.IsQuest(Vm.GameSystem) && info.PlayerType != EPlayerType.AI;
        if (heroSeat)
            col.AddChild(HeroSeat(info, canModify));
        else
        {
            var armyRow = new HBoxContainer();
            armyRow.AddThemeConstantOverride("separation", 6);
            ArmyListSummary s = info.ArmyListSummary;
            bool missing = LobbyArmySource.IsMissingArmy(s, Vm.IsResumeMode);
            var army = new Button
            {
                Text = s.IsAssigned ? s.ArmyName : (LobbySession.ArmyCatalogReady ? "Choose an army..." : "Reading the armies folder..."),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = !canModify, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, Alignment = HorizontalAlignment.Left,
                TooltipText = canModify ? $"Choose this player's army from armies\\{ArmyPaths.FolderNameFor(Vm.GameSystem)}." : $"{info.PlayerName} picks their own army.",
            };
            if (missing) army.AddThemeColorOverride("font_color", Palette.WoundsRed);
            army.Name = "SeatArmy";
            army.Pressed += () => OpenArmyPicker(info);
            armyRow.AddChild(army);
            var roll = Ui.Button("Roll", () => Session.RollArmy(info.PlayerID),
                "Roll another army from the armies folder, closest to the points limit first. Skips armies other players are using and never repeats one until this seat has seen them all. With no saved army of this game, makes a legal one up from a random army book.");
            roll.Name = "SeatRoll";
            roll.Disabled = !canModify || !LobbySession.ArmyCatalogReady;
            armyRow.AddChild(roll);
            col.AddChild(armyRow);
            if (canModify && !s.IsAssigned && Session.RollProblem is { } problem)
            {
                var why = HudKit.Text(problem, 14, Palette.WoundsRed, HudKit.Semi);
                why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                col.AddChild(why);
            }

            var facts = new HBoxContainer();
            facts.AddThemeConstantOverride("separation", 8);
            facts.AddChild(HudKit.Text(s.IsAssigned ? s.FactionName : "-", 16, HudKit.Dim, HudKit.Semi));
            int seatLimit = Session.PointsLimitFor(info.PlayerID);
            ELobbyPointsStatus status = LobbyPointsStatus.Classify(s.IsAssigned, s.PointCost, seatLimit);
            bool quest = GameSystemInfo.IsQuest(Vm.GameSystem);
            var pts = HudKit.Text(quest ? $"{s.PointCost} pts" : $"{s.PointCost} / {seatLimit} pts", 16,
                !s.IsAssigned || quest ? HudKit.Dim : status == ELobbyPointsStatus.Over ? Palette.WoundsRed : status == ELobbyPointsStatus.Under ? Palette.HitsYellow : HudKit.Lime, HudKit.Bold);
            pts.TooltipText = quest ? "The enemy collection: the waves are drawn from it to each wave's points, by the mission's rules."
                : (LobbyPointsStatus.Tooltip(status, s.PointCost, seatLimit) ?? "Within the points limit.")
                  + (seatLimit != Vm.ArmyPoints ? "\nThe attacker: the book gives the attacking army 25% more points than the defender." : "");
            pts.MouseFilter = MouseFilterEnum.Stop;
            facts.AddChild(HudKit.Grow());
            facts.AddChild(pts);
            col.AddChild(facts);
        }

        col.AddChild(HudKit.Grow());

        // Side and colour.
        var side = new HBoxContainer();
        side.AddThemeConstantOverride("separation", 8);
        int playerCount = Vm.PlayerInfos.Count;
        var teams = Enumerable.Range(1, Math.Min(playerCount, TeamOptions.MaxTeamNumber)).ToList();
        bool canTeam = Vm.CheckCanModifyPlayerIDInfo(info.PlayerID) && !Vm.IsResumeMode;
        var team = Choice(teams, (int)info.TeamNumber, n => Session.TeamLabel(n), n => $"Put {info.PlayerName} on {Session.TeamLabel(n)}. Seats on the same team play as one side.",
            !canTeam, n => Vm.SetPlayerTeam(info.PlayerID, (ETeamOption)n));
        team.TooltipText = "Which side this seat plays on.";
        team.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        side.AddChild(team);
        side.AddChild(ColourChoice(seat, info, colours));
        col.AddChild(side);
        PadSelectsSeat(card, seat);
        return card;
    }

    /// <summary>With a controller there is no clicking a card: the highlight moving onto anything in a seat's card shows
    /// that seat's army under the map (Kai, 2026-11-24). The cards rebuild, and the highlight is put back in the same
    /// spot by the pad driver.</summary>
    private void PadSelectsSeat(Node card, int seat)
    {
        foreach (Node n in card.GetChildren())
        {
            if (n is Control c) c.FocusEntered += () => { if (Pads.PadDriver.Active && seat != _selectedSeat) CallDeferred(MethodName.SelectSeat, seat); };
            PadSelectsSeat(n, seat);
        }
    }

    /// <summary>
    /// A Quest seat played by a person: its party. One-off missions pick 2-4 saved heroes (Choose heroes...); a campaign
    /// mission's party is the campaign's, fixed.
    /// </summary>
    private Control HeroSeat(LobbyPlayerInfoSummary info, bool canModify)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        IReadOnlyList<string> party = Session.PartyOf(info.PlayerID);
        var heroes = party.Select(f => (File: f, Hero: FdgRaylib.Quest.CampaignPaths.LoadHero(f))).ToList();
        foreach (var (file, hero) in heroes)
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 6);
            if (hero == null) { h.AddChild(HudKit.Text("Missing: " + System.IO.Path.GetFileName(file), 13, Palette.WoundsRed, HudKit.Semi)); col.AddChild(h); continue; }
            h.TooltipText = Quest.QuestWidgets.SheetText(hero);
            h.MouseFilter = MouseFilterEnum.Stop;
            h.AddChild(Quest.QuestWidgets.Portrait(hero, 30));
            h.AddChild(Quest.QuestWidgets.Line(hero.Name, 15, HudKit.Fg, 90, HudKit.Bold));
            h.AddChild(Quest.QuestWidgets.Line($"Lv {hero.Level} {hero.Class}" + (hero.Injuries.Count > 0 ? " - " + string.Join(", ", hero.Injuries) : ""), 13,
                hero.Injuries.Count > 0 ? Palette.WoundsRed : HudKit.Dim, 150));
            col.AddChild(h);
        }
        if (Session.Campaign != null)
        {
            col.AddChild(Note("The campaign's party - fixed for its missions."));
            return col;
        }
        if (heroes.Count == 0) col.AddChild(Note("No party yet."));
        var pick = Ui.Button(heroes.Count == 0 ? "Choose heroes..." : "Change heroes...", () => OpenPartyPicker(info),
            canModify ? $"Pick 2 to 4 heroes made with Build Heroes ({GameSystemInfo.DisplayName(Vm.GameSystem)})." : $"{info.PlayerName} picks their own heroes.");
        pick.Name = "ChooseHeroes";
        pick.Disabled = !canModify;
        col.AddChild(pick);
        return col;
    }

    private void OpenPartyPicker(LobbyPlayerInfoSummary info)
    {
        var popup = new Quest.HeroPartyPopup { System = Vm.GameSystem, Picked = Session.PartyOf(info.PlayerID).ToList(), Name = "HeroPartyPopup" };
        popup.Done += picks =>
        {
            if (Session.SetParty(info.PlayerID, picks) is { } why) App.I.Message("Party", why);
            Refresh(force: true);
        };
        // Kai, 2026-11-23: added straight into the lobby it was laid out by the lobby's own layout - a small box in the
        // top-left corner with the hero cards cut off. It opens over the whole screen on its own layer now.
        var layer = new CanvasLayer { Layer = 40 };
        AddChild(layer);
        layer.AddChild(popup);
        popup.TreeExiting += () => { if (IsInstanceValid(layer)) layer.QueueFree(); };
        _partyPopup = popup;
    }

    private Quest.HeroPartyPopup? _partyPopup;

    private const int ChooseColourId = -2;

    private Control ColourChoice(int seat, LobbyPlayerInfoSummary info, int[] colours)
    {
        // Not sized to its longest item ("Choose colour..." with a swatch): that made every seat card 400px wide, so on
        // a 1920x1080 lobby the second column ran under the Map Preview and cut off ROLL and the points (bug hunt run 8).
        var ob = new OptionButton { Disabled = !Vm.CheckCanModifyPlayerIDInfo(info.PlayerID), CustomMinimumSize = new Vector2(150, 0), Name = "SeatColour", FitToLongestItem = false,
            TooltipText = "This seat's army colour. A colour too close to another seat's is greyed out.\nChoose colour... opens a colour wheel." };
        int current = colours[seat];
        ImageTexture Swatch(Color c)
        {
            var img = Image.CreateEmpty(18, 18, false, Image.Format.Rgba8);
            img.Fill(c);
            return ImageTexture.CreateFromImage(img);
        }
        for (int i = 0; i < PlayerColorCodes.Count; i++)
        {
            ob.AddIconItem(Swatch(SeatColour(i)), PlayerColorCodes.Options[i].Name, i);
            if (i != current && PlayerColorCodes.ClashingRow(colours, seat, PlayerColorCodes.Options[i].Color) >= 0) ob.SetItemDisabled(i, true);
            if (i == current) ob.Selected = i;
        }
        if (current >= PlayerColorCodes.Count)
        {
            ob.AddIconItem(Swatch(SeatColour(current)), "Custom", current);
            ob.Selected = ob.ItemCount - 1;
        }
        ob.AddItem("Choose colour...", ChooseColourId);
        ob.ItemSelected += i =>
        {
            int id = ob.GetItemId((int)i);
            if (id == ChooseColourId)
            {
                ob.Selected = Math.Max(0, ob.GetItemIndex(current));
                OpenColourWheel(seat, info, colours);
            }
            else Vm.SetPlayerColor(info.PlayerID, id);
        };
        return ob;
    }

    /// <summary>The colour wheel (Kai, 2026-11-05): any colour, but not one too close to another seat's - the
    /// same rule the host enforces (PlayerColorCodes.ClashingRow), so the button only lights when the pick is legal.</summary>
    internal void OpenColourWheelFor(int seat) => OpenColourWheel(seat, Vm.PlayerInfos[seat], Session.EffectiveColours());

    private void OpenColourWheel(int seat, LobbyPlayerInfoSummary info, int[] colours)
    {
        var popup = new PopupPanel { Name = "ColourWheel" };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        popup.AddChild(HudKit.Pad(col, 14, 12, 14, 12));
        col.AddChild(HudKit.Text($"ARMY COLOUR - {info.PlayerName.ToUpperInvariant()}", 20, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        var picker = new ColorPicker
        {
            PickerShape = ColorPicker.PickerShapeType.HsvWheel, EditAlpha = false, SamplerVisible = false, PresetsVisible = false,
            ColorModesVisible = false, SlidersVisible = false, HexVisible = true, Color = SeatColour(colours[seat]),
        };
        col.AddChild(picker);

        // The wheel only answers mouse drags, so a controller sets the colour here instead (Kai, 2026-11-24): three
        // sliders (left / right moves them) and a row of ready-made colours. They and the wheel follow each other.
        var sliders = new GridContainer { Columns = 2 };
        sliders.AddThemeConstantOverride("h_separation", 10);
        HSlider MakeSlider(string label, string name, float value)
        {
            sliders.AddChild(HudKit.Text(label, 14, HudKit.Dim, HudKit.Semi));
            var sl = new HSlider { Name = name, MinValue = 0, MaxValue = 1, Step = 0.04, Value = value, CustomMinimumSize = new Vector2(260, 24), FocusMode = FocusModeEnum.All };
            sliders.AddChild(sl);
            return sl;
        }
        HSlider hue = MakeSlider("Hue", "ColourHue", picker.Color.H), sat = MakeSlider("Colour", "ColourSaturation", picker.Color.S),
            val = MakeSlider("Brightness", "ColourBrightness", picker.Color.V);
        col.AddChild(sliders);
        bool syncing = false;
        void FromSliders(double _)
        {
            if (syncing) return;
            syncing = true;
            picker.Color = Color.FromHsv((float)hue.Value, (float)sat.Value, (float)val.Value);
            picker.EmitSignal(ColorPicker.SignalName.ColorChanged, picker.Color);
            syncing = false;
        }
        hue.ValueChanged += FromSliders; sat.ValueChanged += FromSliders; val.ValueChanged += FromSliders;
        picker.ColorChanged += c =>
        {
            if (syncing) return;
            syncing = true;
            hue.Value = c.H; sat.Value = c.S; val.Value = c.V;
            syncing = false;
        };
        var swatches = new HFlowContainer();
        swatches.AddThemeConstantOverride("h_separation", 4);
        swatches.AddThemeConstantOverride("v_separation", 4);
        for (int i = 0; i < PlayerColorCodes.Count; i++)
        {
            Color sw = SeatColour(i);
            var img = Image.CreateEmpty(22, 22, false, Image.Format.Rgba8);
            img.Fill(sw);
            var b = new Button { Icon = ImageTexture.CreateFromImage(img), Name = $"Swatch{i}", TooltipText = PlayerColorCodes.Options[i].Name, CustomMinimumSize = new Vector2(36, 32), FocusMode = FocusModeEnum.All };
            b.Pressed += () => { picker.Color = sw; picker.EmitSignal(ColorPicker.SignalName.ColorChanged, sw); };
            swatches.AddChild(b);
        }
        col.AddChild(swatches);

        var others = new HBoxContainer();
        others.AddThemeConstantOverride("separation", 8);
        others.AddChild(HudKit.Text("Taken:", 14, HudKit.Dim, HudKit.Semi));
        for (int k = 0; k < colours.Length && k < Vm.PlayerInfos.Count; k++)
            if (k != seat) others.AddChild(HudKit.Chip(Vm.PlayerInfos[k].PlayerName, SeatColour(colours[k]), true, 13));
        col.AddChild(others);
        var why = HudKit.Text("", 14, Palette.WoundsRed, HudKit.Semi);
        why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        why.CustomMinimumSize = new Vector2(320, 0);
        col.AddChild(why);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Ui.Button("Cancel", () => popup.QueueFree(), variation: "BackButton"));
        var use = Ui.Button("Use this colour", () => { }, variation: "CommitButton");
        use.Name = "UseColour";
        buttons.AddChild(use);
        col.AddChild(buttons);

        Rgb Pick() => new((byte)picker.Color.R8, (byte)picker.Color.G8, (byte)picker.Color.B8);
        void Check()
        {
            int clash = PlayerColorCodes.ClashingRow(colours, seat, Pick());
            use.Disabled = clash >= 0;
            why.Text = clash < 0 ? "" : $"Too close to {(clash < Vm.PlayerInfos.Count ? Vm.PlayerInfos[clash].PlayerName : $"seat {clash + 1}")}'s colour - the armies would look alike.";
        }
        picker.ColorChanged += _ => Check();
        use.Pressed += () =>
        {
            Rgb c = Pick();
            Vm.SetPlayerColor(info.PlayerID, PlayerColorCodes.EncodeCustom(c.R, c.G, c.B));
            popup.QueueFree();
        };
        popup.PopupHide += () => popup.QueueFree();
        Check();
        AddChild(popup);
        popup.PopupCentered();
        if (Pads.PadDriver.Active) hue.CallDeferred(Control.MethodName.GrabFocus);
    }

    private Control EmptySeat(int seat)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Name = $"Seat{seat + 1}" };
        card.AddThemeStyleboxOverride("panel", new ChamferBox { Fill = new Color(0, 0, 0, 0.35f), Border = HudKit.Edge with { A = 0.4f }, Cut = 14, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12 });
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(HudKit.Text($"SEAT {seat + 1}", 15, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2), HorizontalAlignment.Center));
        bool canAdd = Session.CanEdit && LobbySession.SeatCanSwitchOn(seat, Vm.PlayerInfos.Count);
        var add = Ui.Button("+ Add a seat", Session.AddSeat,
            !Session.CanEdit ? "Only the host adds seats (and a resumed save keeps its seats)."
            : canAdd ? "Add a player in this seat. It arrives as a bot; the Player/Bot button on its card changes that."
            : $"Add seat {Vm.PlayerInfos.Count + 1} first - seats fill in order.");
        add.Name = $"AddSeat{seat + 1}";
        add.Disabled = !canAdd;
        add.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        add.CustomMinimumSize = new Vector2(220, 56);
        col.AddChild(add);
        col.AddChild(HudKit.Text("Empty - not in the game.", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
        card.AddChild(col);
        return card;
    }

    private static ChamferBox SeatCardBox(bool selected, Color colour) => new()
    {
        Fill = selected ? Palette.Panel with { A = 0.92f } : HudKit.Glass, Border = selected ? HudKit.Lime : HudKit.Edge,
        Glow = selected ? HudKit.Lime with { A = 0.35f } : Colors.Transparent, Cut = 14, Accent = colour, AccentLength = 90, AccentThickness = 4,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
    };

    /// <summary>Shows another seat's army under the map. The cards are only re-framed, not rebuilt: rebuilding them threw
    /// the controller's highlight off the card it had just moved onto (Kai, 2026-11-24: "now i cant go to the right").</summary>
    private void SelectSeat(int seat)
    {
        if (seat == _selectedSeat) return;
        _selectedSeat = seat;
        foreach (Node n in _seatGrid.GetChildren())
            if (n is PanelContainer card && card.HasMeta("seat_colour") && card.Name.ToString().StartsWith("Seat")
                && int.TryParse(card.Name.ToString()[4..], out int number))
                card.AddThemeStyleboxOverride("panel", SeatCardBox(number - 1 == seat, card.GetMeta("seat_colour").AsColor()));
        BuildSeatArmy();
    }

    private const string OfficialAiTip = "The enemy side, playing OnePageRules' official Solo & Co-Op rules: it takes and holds objectives, shoots the closest target it can see, and favours enemies that haven't moved yet.";

    // ================================================================== the clicked seat's army

    private void BuildSeatArmy()
    {
        foreach (Node n in _armyRows.GetChildren()) { _armyRows.RemoveChild(n); n.QueueFree(); }
        IReadOnlyList<LobbyPlayerInfoSummary> players = Vm.PlayerInfos;
        if (players.Count == 0) { _seatHeading.Text = "NO SEATS YET"; return; }
        LobbyPlayerInfoSummary info = players[_selectedSeat];
        ArmyListSummary s = info.ArmyListSummary;
        _seatHeading.Text = $"SEAT {_selectedSeat + 1} - {info.PlayerName.ToUpperInvariant()}" + (s.IsAssigned ? $"  -  {s.ArmyName}  -  {s.PointCost} pts" : "");

        (ArmyListFile? army, string why) = Session.SeatArmy(info.PlayerID);
        if (army == null || army.Units.Count == 0) { _armyRows.AddChild(Note(why.Length > 0 ? why : LobbySession.NoSeatArmyText)); return; }
        Color team = SeatColour(Session.EffectiveColours()[_selectedSeat]);
        foreach (UnitFileEntry unit in army.Units) _armyRows.AddChild(UnitRow(unit, army.Faction, team, info.PlayerID));
    }

    private static Control UnitRow(UnitFileEntry u, string faction, Color team, PlayerID? seatId = null)
    {
        string? tough = u.SpecialRules.Select(r => r.PrintableName).FirstOrDefault(n => n.StartsWith("Tough("));
        bool hero = u.SpecialRules.Any(r => r.PrintableName == "Hero");
        int toughN = tough != null && int.TryParse(tough[6..^1], out int t) ? t : 1;
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 10);
        h.AddChild(HudKit.Portrait(faction, u.Name, HudKit.KindIcon(u.Name, hero, u.Weapons.Any(w => w.RangeInches > 0), toughN), (int)HudKit.PictureSize.LobbyList, team, look: FdgRaylib.Models3D.UnitPortraits.LookOf(u.ModelLooks), owner: seatId));
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 0);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        var name = HudKit.Text(u.Name.ToUpperInvariant() + (u.ModelCount > 1 ? $"  [{u.ModelCount}]" : ""), 16, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1));
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        top.AddChild(name);
        top.AddChild(HudKit.Chip($"Q{u.Quality}+", HudKit.Fg, true, 13));
        top.AddChild(HudKit.Chip($"D{u.Defense}+", HudKit.Fg, true, 13));
        if (tough != null) top.AddChild(HudKit.Chip(tough, HudKit.Fg, true, 13));
        top.AddChild(HudKit.Text($"{u.PointCost} pts", 15, HudKit.Lime, HudKit.Bold));
        v.AddChild(top);
        string weapons = string.Join(", ", u.Weapons.Select(WeaponLine));
        var w = HudKit.Text(weapons, 13, HudKit.Dim, HudKit.Medium);
        // Every weapon shows - the line wraps rather than being cut off at the panel's edge (lane 3, 2026-11-27).
        w.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        w.CustomMinimumSize = new Vector2(10, 0);
        v.AddChild(w);
        h.AddChild(v);
        var row = HudKit.Panel(new ChamferBox { Fill = new Color(0, 0, 0, 0.25f), Border = HudKit.Edge with { A = 0.35f }, Cut = 6, ContentMarginLeft = 4, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 }, h);
        row.TooltipText = $"{u.Name} [{u.ModelCount}] - {u.PointCost} pts\nQuality {u.Quality}+   Defense {u.Defense}+\n\n"
            + string.Join("\n", u.Weapons.Select(WeaponLine))
            + (u.SpecialRules.Count > 0 ? "\n\n" + string.Join(", ", u.SpecialRules.Select(r => r.PrintableName)) : "");
        return row;
    }

    private static string WeaponLine(WeaponFileEntry w)
    {
        var stats = new List<string>();
        if (w.RangeInches > 0) stats.Add($"{w.RangeInches}\"");
        stats.Add($"A{w.Attacks}");
        if (w.ArmorPenetration != 0) stats.Add($"AP({w.ArmorPenetration})");
        stats.AddRange(w.SpecialRules.Select(r => r.PrintableName));
        return $"{(w.Quantity > 1 ? w.Quantity + "x " : "")}{w.Name} ({string.Join(", ", stats)})";
    }

    // ================================================================== launch

    private void UpdateLaunch()
    {
        IReadOnlyList<string> blockers = Session.Blockers();
        _launch.Text = Vm.IsResumeMode ? "RESUME" : "LAUNCH";
        _launch.Disabled = !Vm.HasHostPrivileges || blockers.Count > 0;
        _launch.TooltipText = !Vm.HasHostPrivileges ? "Only the host can launch."
            : blockers.Count > 0 ? "Cannot launch:\n" + string.Join("\n", blockers.Select(b => "  " + b)) : "Start the game.";
        _blockers.Text = !Vm.HasHostPrivileges ? "Waiting for the host to launch."
            : blockers.Count > 0 ? "Can't launch yet: " + blockers[0] + (blockers.Count > 1 ? $"  (+{blockers.Count - 1} more - hover LAUNCH)" : "")
            : "";
        _blockers.AddThemeColorOverride("font_color", Vm.HasHostPrivileges ? Palette.WoundsRed : HudKit.Dim);
    }

    private void OnLaunch()
    {
        IReadOnlyList<string> warnings = Session.Warnings();
        if (warnings.Count == 0) { StartGame(); return; }
        var confirm = new ConfirmationDialog
        {
            Title = "Launch anyway?", OkButtonText = "Launch anyway", CancelButtonText = "Go back",
            DialogText = "Some armies have problems:\n\n" + string.Join("\n", warnings.Select(w => "  " + w)),
        };
        confirm.Confirmed += StartGame;
        confirm.Canceled += () => confirm.QueueFree();
        AddChild(confirm);
        confirm.PopupCentered();
    }

    /// <summary>
    /// The table is port step 3, so LAUNCH does not start the engine yet - a game with no table to show
    /// would wait for answers nobody can give. The settings are remembered for the next lobby, and the page
    /// says where the game is played today.
    /// </summary>
    private void StartGame()
    {
        // The engine builds the game on its own thread and hands it back through the lobby's OnLaunched;
        // _Process picks it up and opens the table.
        if (!Session.Launch())
            ShowLaunchError(Session.LastLaunchError ?? "Launch failed.");
    }

    private void ShowLaunchError(string text)
    {
        var d = new AcceptDialog { Title = "Can't launch", DialogText = text };
        d.Confirmed += () => d.QueueFree();
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
    }

    /// <summary>A launched game (host or client) goes straight to the table.</summary>
    private void TakeLaunch()
    {
        if (Session.TakeLaunchedGame() is not { } game) return;
        FdgRaylib.Game.GameSession session;
        try { session = FdgRaylib.Game.GameSession.Start(game, Session, Battle.BattleResolvers.Register); }
        catch (Exception e)
        {
            // Never lose a launched game silently (Kai, 2026-11-17: bot vs bot LAUNCH did nothing, then "already
            // launched") - say what went wrong; the lobby can't launch this game again, so Back out and start a new one.
            GD.PrintErr("Opening the table failed: " + e);
            ShowLaunchError("The game started but its table couldn't open:\n" + e.Message + "\n\nGo Back and set up the game again, and send Claude a screenshot of this.");
            return;
        }
        // The versus loading screen goes up first, then the table loads under it (Kai, 2026-11-16).
        VersusScreen.Show(GetTree(), session, Session, () => App.I.Open<Battle.BattleScreen>(Scenes.Battle, s => s.Session = session));
    }
}

internal static class ControlExtensions
{
    public static T With<T>(this T node, Action<T> change) where T : Node
    {
        change(node);
        return node;
    }
}
