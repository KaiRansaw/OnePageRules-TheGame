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

namespace OprGodot;

/// <summary>
/// The lobby (port step 2; Kai's decisions 2026-11-07, screen map Part 5 item 8): a mode button hosts and
/// lands here. Settings tabs on the left (only what this game and mode have, a hover on every setting and
/// option - book wording for rules), the 2x2 seat cards in the middle, and on the right the map preview, the
/// clicked seat's army list and LAUNCH. All the non-drawing work is <see cref="LobbySession"/> (FdgApp).
/// </summary>
public partial class LobbyScreen : Control
{
    public LobbySession Session { get; set; } = null!;
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

    // The two settings a typed / picked value lives in, refreshed in place so typing isn't interrupted.
    private SpinBox? _points;
    private OptionButton? _quickSize;

    public override void _Ready()
    {
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
        right.AddChild(new PanelContainer { ThemeTypeVariation = "CardPanel" }.With(p => p.AddChild(previewBox)));

        var armyBox = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        armyBox.AddThemeConstantOverride("separation", 6);
        _seatHeading = HudKit.Text("", 16, Palette.Header, HudKit.Tracked(HudKit.Bold, 1));
        _seatHeading.ClipText = true;
        armyBox.AddChild(_seatHeading);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _armyRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _armyRows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_armyRows);
        armyBox.AddChild(scroll);
        right.AddChild(new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill }.With(p => p.AddChild(armyBox)));

        _blockers = HudKit.Text("", 15, Palette.WoundsRed, HudKit.Semi);
        _blockers.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _blockers.CustomMinimumSize = new Vector2(10, 0);
        right.AddChild(_blockers);
        _launch = new Button { Name = "Launch", Text = "LAUNCH", ThemeTypeVariation = "CommitButton", CustomMinimumSize = new Vector2(0, 72) };
        _launch.AddThemeFontSizeOverride("font_size", 30);
        _launch.Pressed += OnLaunch;
        right.AddChild(_launch);
        body.AddChild(right);

        Refresh(force: true);
    }

    public override void _ExitTree()
    {
        // Leaving the lobby stops hosting (releases the port) and remembers the settings for next time.
        Session.PersistHostSettings();
        Session.Dispose();
    }

    public override void _Process(double delta)
    {
        Session.Tick();
        TakeLaunch();
        _poll += delta;
        if (_poll < 0.25) return;
        _poll = 0;
        Refresh(force: false);
    }

    // ================================================================== refresh

    private void Refresh(bool force)
    {
        IReadOnlyList<LobbyPlayerInfoSummary> players = Vm.PlayerInfos;
        _selectedSeat = Math.Clamp(_selectedSeat, 0, Math.Max(0, players.Count - 1));

        _title.Text = Session.HeaderTitle.ToUpperInvariant();
        _sub.Text = Vm.HasHostPrivileges ? HostStarter.TableName(UserConfig.Current.PlayerName) : "at " + Vm.ServerName;

        string seatSig = string.Join("|", players.Select(p =>
            $"{p.PlayerID.ID}:{p.PlayerName}:{p.PlayerType}:{p.TeamNumber}:{p.ColorIndex}:{p.ArmyListSummary.ArmyName}:{p.ArmyListSummary.FactionName}:{p.ArmyListSummary.PointCost}:{p.ArmyListSummary.IsAssigned}"))
            + $"#{_selectedSeat}#{Vm.ArmyPoints}#{Vm.GameSystem}#{Vm.GameMode}#{LobbySession.ArmyCatalogReady}";
        string settingsJson = JsonSerializer.Serialize(HostGameSettings.CaptureFrom(Vm));
        string settingsSig = $"{Vm.GameSystem}|{Vm.GameMode}|{Session.CanEdit}|{players.Count}|{_fastPlayOn}|{_extraActionsOn}|{Vm.TerrainPlacementMode}|{Vm.TerrainLayoutPath}|"
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
        var c = new CheckBox { Text = label, ButtonPressed = on, TooltipText = tooltip, Disabled = locked };
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
        bool skirmish = GameSystemInfo.IsSkirmish(Vm.GameSystem);
        _points = new SpinBox { MinValue = 0, MaxValue = 100000, Step = skirmish ? 25 : 250, Value = Vm.ArmyPoints, Editable = !locked, AllowGreater = true, Suffix = "pts" };
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

        // Map: Random, or a map drawn for this table size (Select Map's full picker comes later).
        (float tw, float th) = Session.TableWithoutMap();
        var maps = MapPaths.List()
            .Select(p => (Path: p, Layout: TerrainLayoutLoader.TryLoadFromFile(p, out _)))
            .Where(m => m.Layout != null && MathF.Abs(m.Layout.TableWidthInches - tw) < 0.01f && MathF.Abs(m.Layout.TableHeightInches - th) < 0.01f)
            .ToList();
        var options = new List<string?> { null };
        options.AddRange(maps.Select(m => (string?)m.Path));
        string? currentMap = Session.HasMap ? Vm.TerrainLayoutPath : null;
        if (currentMap != null && !options.Contains(currentMap)) options.Add(currentMap);
        string MapName(string? p) => p == null ? "Random" : maps.FirstOrDefault(m => m.Path == p).Layout is { Name.Length: > 0 } l ? l.Name : Path.GetFileNameWithoutExtension(p);
        Row(rows, "Map", "The table you will fight on.\nRandom: the game lays out terrain when the game starts.\nA map: its terrain and objective markers, exactly as drawn. Only maps made for this game's table size are offered. Make maps in Map Maker.",
            Choice(options, currentMap, MapName, p => p == null ? "The game lays out terrain at the start." : "Its terrain and objectives, as drawn.", locked, p => Session.SetMap(p)));

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

        Row(rows, "Battlefield", "The look of the table surface. Cosmetic - no effect on terrain or any rule. Picked at random each time a lobby opens.",
            Choice(Enum.GetValues<ETableBackground>(), Vm.TableBackground, TableBackgroundName, b => "Cosmetic only.", !Vm.HasHostPrivileges, b => Vm.SetTableBackground(b)));

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
    }

    private static string TableBackgroundName(ETableBackground b) => b == ETableBackground.MarsLike ? "Mars-Like" : b.ToString();

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
        card.AddThemeStyleboxOverride("panel", new ChamferBox
        {
            Fill = selected ? new Color(0.07f, 0.11f, 0.11f, 0.92f) : HudKit.Glass, Border = selected ? HudKit.Lime : HudKit.Edge,
            Glow = selected ? HudKit.Lime with { A = 0.35f } : Colors.Transparent, Cut = 14, Accent = colour, AccentLength = 90, AccentThickness = 4,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        card.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) SelectSeat(seat); };

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        card.AddChild(col);

        // Faction band in the seat's colour.
        var band = new PanelContainer();
        band.AddThemeStyleboxOverride("panel", new ChamferBox { Fill = colour with { A = 0.55f }, Border = Colors.Transparent, BorderWidth = 0, Cut = 6, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 3 });
        band.AddChild(HudKit.Text(info.ArmyListSummary.IsAssigned ? info.ArmyListSummary.FactionName : "No army yet", 16, Colors.White, HudKit.Bold));
        band.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(band);

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
        name.ClipText = true;
        col.AddChild(name);

        // The army: a button that opens the picker, Roll for another, and faction - points.
        bool canModify = !Vm.IsResumeMode && Vm.CheckCanModifyPlayerIDInfo(info.PlayerID);
        bool heroSeat = GameSystemInfo.IsQuest(Vm.GameSystem) && info.PlayerType != EPlayerType.AI;
        if (heroSeat)
            col.AddChild(Note("Hero party: the party picker comes with the Quest lobby (next part of port step 2). Use the Raylib build for Quest games until then."));
        else
        {
            var armyRow = new HBoxContainer();
            armyRow.AddThemeConstantOverride("separation", 6);
            ArmyListSummary s = info.ArmyListSummary;
            bool missing = LobbyArmySource.IsMissingArmy(s, Vm.IsResumeMode);
            var army = new Button
            {
                Text = s.IsAssigned ? s.ArmyName : (LobbySession.ArmyCatalogReady ? "Choose an army..." : "Reading the armies folder..."),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = !canModify, ClipText = true, Alignment = HorizontalAlignment.Left,
                TooltipText = canModify ? $"Choose this player's army from armies\\{ArmyPaths.FolderNameFor(Vm.GameSystem)}." : $"{info.PlayerName} picks their own army.",
            };
            if (missing) army.AddThemeColorOverride("font_color", Palette.WoundsRed);
            army.Pressed += () => OpenArmyPicker(info);
            armyRow.AddChild(army);
            var roll = Ui.Button("Roll", () => Session.RollArmy(info.PlayerID),
                "Roll another army from the armies folder, closest to the points limit first. Skips armies other players are using and never repeats one until this seat has seen them all.");
            roll.Disabled = !canModify || !LobbySession.ArmyCatalogReady;
            armyRow.AddChild(roll);
            col.AddChild(armyRow);

            var facts = new HBoxContainer();
            facts.AddThemeConstantOverride("separation", 8);
            facts.AddChild(HudKit.Text(s.IsAssigned ? s.FactionName : "-", 16, HudKit.Dim, HudKit.Semi));
            ELobbyPointsStatus status = LobbyPointsStatus.Classify(s.IsAssigned, s.PointCost, Vm.ArmyPoints);
            var pts = HudKit.Text($"{s.PointCost} / {Vm.ArmyPoints} pts", 16,
                !s.IsAssigned ? HudKit.Dim : status == ELobbyPointsStatus.Over ? Palette.WoundsRed : status == ELobbyPointsStatus.Under ? Palette.HitsYellow : HudKit.Lime, HudKit.Bold);
            pts.TooltipText = LobbyPointsStatus.Tooltip(status, s.PointCost, Vm.ArmyPoints) ?? "Within the points limit.";
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
        return card;
    }

    private Control ColourChoice(int seat, LobbyPlayerInfoSummary info, int[] colours)
    {
        var ob = new OptionButton { Disabled = !Vm.CheckCanModifyPlayerIDInfo(info.PlayerID), CustomMinimumSize = new Vector2(150, 0),
            TooltipText = "This seat's army colour. A colour another seat has is greyed out." };
        int current = colours[seat];
        for (int i = 0; i < PlayerColorCodes.Count; i++)
        {
            var img = Image.CreateEmpty(18, 18, false, Image.Format.Rgba8);
            img.Fill(SeatColour(i));
            ob.AddIconItem(ImageTexture.CreateFromImage(img), PlayerColorCodes.Options[i].Name, i);
            if (PlayerColorCodes.IsTakenByAnother(colours, seat, i)) ob.SetItemDisabled(i, true);
            if (i == current) ob.Selected = i;
        }
        if (current >= PlayerColorCodes.Count)
        {
            var img = Image.CreateEmpty(18, 18, false, Image.Format.Rgba8);
            img.Fill(SeatColour(current));
            ob.AddIconItem(ImageTexture.CreateFromImage(img), "Custom", current);
            ob.Selected = ob.ItemCount - 1;
        }
        ob.ItemSelected += i => Vm.SetPlayerColor(info.PlayerID, ob.GetItemId((int)i));
        return ob;
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

    private void SelectSeat(int seat)
    {
        if (seat == _selectedSeat) return;
        _selectedSeat = seat;
        _seatSig = "";   // rebuild on the next poll
        Refresh(force: false);
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
        foreach (UnitFileEntry unit in army.Units) _armyRows.AddChild(UnitRow(unit, army.Faction, team));
    }

    private static Control UnitRow(UnitFileEntry u, string faction, Color team)
    {
        string? tough = u.SpecialRules.Select(r => r.PrintableName).FirstOrDefault(n => n.StartsWith("Tough("));
        bool hero = u.SpecialRules.Any(r => r.PrintableName == "Hero");
        int toughN = tough != null && int.TryParse(tough[6..^1], out int t) ? t : 1;
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 10);
        h.AddChild(HudKit.Portrait(faction, u.Name, HudKit.KindIcon(u.Name, hero, u.Weapons.Any(w => w.RangeInches > 0), toughN), 44, team));
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 0);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        var name = HudKit.Text(u.Name.ToUpperInvariant() + (u.ModelCount > 1 ? $"  [{u.ModelCount}]" : ""), 16, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1));
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        top.AddChild(name);
        top.AddChild(HudKit.Chip($"Q{u.Quality}+", HudKit.Fg, true, 13));
        top.AddChild(HudKit.Chip($"D{u.Defense}+", HudKit.Fg, true, 13));
        if (tough != null) top.AddChild(HudKit.Chip(tough, HudKit.Fg, true, 13));
        top.AddChild(HudKit.Text($"{u.PointCost} pts", 15, HudKit.Lime, HudKit.Bold));
        v.AddChild(top);
        string weapons = string.Join(", ", u.Weapons.Select(WeaponLine));
        var w = HudKit.Text(weapons, 13, HudKit.Dim, HudKit.Medium);
        w.ClipText = true;
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

    // ================================================================== army picker

    private void OpenArmyPicker(LobbyPlayerInfoSummary info)
    {
        IReadOnlyList<ArmyCatalogEntry> armies = Session.Armies();
        string currentKey = LobbySession.ArmyKey(info.ArmyListSummary);

        var popup = new PopupPanel { Size = new Vector2I(760, 640) };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        popup.AddChild(col);
        col.AddChild(HudKit.Text($"CHOOSE AN ARMY - {info.PlayerName.ToUpperInvariant()}", 22, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        col.AddChild(Note($"From armies\\{ArmyPaths.FolderNameFor(Vm.GameSystem)}. Red = over the {Vm.ArmyPoints} pts limit. Build lists in Army Forge (Raylib build for now)."));
        var search = new LineEdit { PlaceholderText = "Search name or faction..." };
        col.AddChild(search);
        var list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 460) };
        col.AddChild(list);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        var cancel = Ui.Button("Cancel", () => popup.QueueFree(), variation: "BackButton");
        var take = Ui.Button("Take this army", () => { }, variation: "CommitButton");
        buttons.AddChild(cancel);
        buttons.AddChild(take);
        col.AddChild(buttons);

        var shown = new List<ArmyCatalogEntry>();
        void Fill(string filter)
        {
            list.Clear();
            shown.Clear();
            foreach (ArmyCatalogEntry e in armies)
            {
                if (filter.Length > 0 && !e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !e.Faction.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                int i = list.AddItem($"{e.Name}   -   {e.Faction}   -   {e.Points} pts");
                if (e.Points > Vm.ArmyPoints) list.SetItemCustomFgColor(i, Palette.WoundsRed);
                if (e.Key == currentKey) list.Select(i);
                shown.Add(e);
            }
            if (shown.Count == 0) list.AddItem(LobbySession.ArmyCatalogReady ? "No armies match." : "Reading the armies folder...", selectable: false);
        }
        Fill("");
        search.TextChanged += Fill;
        void Take()
        {
            int[] sel = list.GetSelectedItems();
            if (sel.Length == 0 || sel[0] >= shown.Count) return;
            Session.PickArmy(info.PlayerID, shown[sel[0]]);
            popup.QueueFree();
        }
        take.Pressed += Take;
        list.ItemActivated += _ => Take();
        popup.PopupHide += () => popup.QueueFree();
        AddChild(popup);
        popup.PopupCentered();
        search.GrabFocus();
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
        var session = FdgRaylib.Game.GameSession.Start(game, Session, Battle.BattleResolvers.Register);
        App.I.Open<Battle.BattleScreen>(Scenes.Battle, s => s.Session = session);
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
