using FDG;
using FDG.SaveLoad;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Maps;

/// <summary>
/// The Map Maker's 3D editor (Map Maker mockup, approved 2026-11-13, every item kept; layout A):
/// <list type="bullet">
/// <item>the table in 3D in the middle, with the battle's camera (WASD, wheel, right-drag, T top view);</item>
/// <item>left: the book's terrain pieces (Advanced Rulebook "common terrain types", each carrying its rules), objective
/// markers and size-reference bases, and the Book check against a chosen game's advice;</item>
/// <item>right: the selected piece - name, kind, look, size, turn, and the rule ticks with the book's own text;</item>
/// <item>top: name, table size, Rules view, Generate... (the book's terrain generator), undo / redo, Cancel, SAVE MAP.</item>
/// </list>
/// The map itself (undo, generator, book check, saving) is <see cref="MapDraft"/> in FdgApp. The table is flat for now
/// (heights later, Kai's call); a piece's height is only how tall its block is drawn.
/// </summary>
public partial class MapEditorScreen : Control, IBackGuard, IOwnBackdrop, Screens.ISettingsAware
{
    public MapDraft Draft { get; set; } = MapDraft.New(72, 48);

    // Map Maker option A (Kai, 2026-11-17): Box select and Measure join the tools, on a rail beside the table.
    private enum ETool { Select, BoxSelect, Place, Objective, Base, Measure }

    private ETool _tool = ETool.Select;
    private TerrainBook.PieceKind _kind = TerrainBook.Kinds[1];
    // Kai, 2026-11-14: placing a Warhammer model instead of a book shape (the model is the piece).
    private WarhammerTerrainLibrary.Entry? _model;
    private int _modelSet = -1;
    private string _modelKind = "";
    private int _baseSize = 32;
    // Every selected piece (box select, Shift+click). _sel is the one piece when exactly one is picked - the single
    // piece panel, the corner handles and the name label only apply then.
    private readonly List<MapPiece> _picked = new();
    private MapPiece? _sel
    {
        get => _picked.Count == 1 ? _picked[0] : null;
        set { _picked.Clear(); if (value != null) _picked.Add(value); }
    }
    private int _selObjective = -1;
    // The size base that is selected (click it with Select): it drags and deletes like an objective marker. A size base
    // could be dropped but never picked up again - only Undo or clearing the whole table got rid of one.
    private int _selBase = -1;
    private List<MapPiece> _clipboard = new();
    private bool _rulesView, _topView, _grid;
    // Snap step in inches: 1, 0.5, or 0 = off (quarter inches).
    private float _snapStep = 1f;
    private MapDraft.EMirror _mirror = MapDraft.EMirror.TopBottom;
    // Box select: where the drag started (screen), whether Shift adds to the selection, and the drawn rectangle.
    private Vector2? _boxFrom;
    private bool _boxAdd;
    private Panel _boxRect = null!;
    // Measure: the two ends of the ruler, in table inches.
    private Vector2? _measureA, _measureB;
    // Group drag: where the pointer went down, and where each picked piece started.
    private Vector2 _dragStart;
    private List<(MapPiece P, float X, float Z)> _dragFrom = new();
    private VBoxContainer _rail = null!;
    private EGameSystem _checkFor;

    private Node3D _world = null!, _board = null!, _pieces = null!, _marks = null!, _dressed = null!;
    // A piece's 3D look, kept while the piece (and the look style) is unchanged - building a forest's trees on every
    // click would make the editor crawl. Keyed by the piece; the signature says when it is stale.
    private readonly Dictionary<MapPiece, (string Sig, Node3D Node)> _looks = new();
    private Camera3D _camera = null!;
    private MeshInstance3D _ghost = null!;
    private Vector3 _focus;
    private float _yaw, _pitch = 55f, _distance = 80f, _savedPitch = 55f;
    private Vector2? _dragRotate, _dragPan;
    private bool _dragging;
    private int _resizeCorner = -1;
    private Vector2 _dragOffset;

    private LineEdit _name = null!;
    private VBoxContainer _left = null!, _right = null!;
    // Kai, 2026-11-18: placing a piece rebuilds the left panel - keep both scroll positions so the model list doesn't
    // jump back to the top.
    private ScrollContainer? _leftScroll;
    private HBoxContainer _top = null!;
    private Label _status = null!;

    private void SayRefreshed(int n) => Say($"{n} Warhammer piece{(n == 1 ? "" : "s")} updated to the kind {(n == 1 ? "its model is" : "their models are")} matched to now - save the map to keep it.", Palette.Lime);

    private float W => Draft.TableW;
    private float D => Draft.TableH;
    private Vector3 World(float x, float z, float y = 0) => new(x - W / 2, y, D / 2 - z);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // The menu's own 3D table is taken away while this one is up (its camera and light would fight ours).
        MenuBackdrop.Instance?.SetActive(false);
        _checkFor = DefaultGame();
        // Warhammer pieces follow what their models are matched to now (shown on the message line when any changed).
        int refreshed = WarhammerModels.Library?.Refresh(Draft) ?? 0;
        if (refreshed > 0) CallDeferred(nameof(SayRefreshed), refreshed);
        _world = new Node3D { Name = "MapWorld" };
        AddChild(_world);
        TableLook.AddLighting(_world, TableLook.RoomFor(_checkFor), HorizonId, W, D, Horizon.On, Draft.Sky);
        Horizon.ApplyLight(_world, MapAtmosphere.Light.Parse(Draft.Light));
        _board = new Node3D();
        _pieces = new Node3D();
        _marks = new Node3D();
        _dressed = new Node3D { Name = "Looks" };
        _world.AddChild(_board);
        _world.AddChild(_pieces);
        _world.AddChild(_dressed);
        _world.AddChild(_marks);
        _camera = new Camera3D { Fov = Horizon.Lens, Current = true, Far = Horizon.On ? Horizon.Reach * 2.5f : 2000 };
        _world.AddChild(_camera);
        // Kai, 2026-11-18 (camera "4"): the long lens sits further back, and starts a little lower on the battlefield.
        _distance *= Horizon.DistanceScale;
        if (Horizon.On) { _pitch = 45f; _savedPitch = 45f; }
        _ghost = new MeshInstance3D { Visible = false };
        _world.AddChild(_ghost);

        var hud = new Control { MouseFilter = MouseFilterEnum.Ignore };
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(hud);
        _top = new HBoxContainer();
        _top.AddThemeConstantOverride("separation", 10);
        var topPanel = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(_top, 12, 6, 12, 6));
        topPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        topPanel.OffsetLeft = 16; topPanel.OffsetRight = -16; topPanel.OffsetTop = 12;
        hud.AddChild(topPanel);

        _left = new VBoxContainer();
        _left.AddThemeConstantOverride("separation", 6);
        var ls = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _leftScroll = ls;
        _left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ls.AddChild(_left);
        var leftPanel = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(ls, 10, 8, 10, 8));
        leftPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        leftPanel.OffsetLeft = 16; leftPanel.OffsetTop = 92; leftPanel.OffsetBottom = -48; leftPanel.OffsetRight = 16 + 290;
        hud.AddChild(leftPanel);

        _rail = new VBoxContainer();
        _rail.AddThemeConstantOverride("separation", 6);
        var railPanel = HudKit.Panel(HudKit.PanelBox(10), HudKit.Pad(_rail, 6, 8, 6, 8));
        railPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        railPanel.OffsetLeft = 16 + 290 + 8; railPanel.OffsetTop = 92;
        hud.AddChild(railPanel);
        // The left panel is as wide as its widest row (the Objective / Size base / base size row), which is wider than
        // the 290 it is given - so a rail parked at 290 lay over the right-hand piece buttons ("Lake (shallow)", "River
        // (shallow)"). The rail follows the panel's real right edge instead.
        leftPanel.Resized += () => railPanel.Position = new Vector2(leftPanel.Position.X + leftPanel.Size.X + 8, railPanel.Position.Y);

        _right = new VBoxContainer();
        _right.AddThemeConstantOverride("separation", 6);
        var rs = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rs.AddChild(_right);
        var rightPanel = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(rs, 10, 8, 10, 8));
        rightPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide);
        rightPanel.OffsetRight = -16; rightPanel.OffsetTop = 92; rightPanel.OffsetBottom = -48; rightPanel.OffsetLeft = -16 - 320;
        hud.AddChild(rightPanel);

        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 330; _status.OffsetRight = -350; _status.OffsetTop = -40; _status.OffsetBottom = -14;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.MouseFilter = MouseFilterEnum.Ignore;
        hud.AddChild(_status);
        _boxRect = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore, Name = "BoxSelectRect" };
        _boxRect.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = HudKit.Lime with { A = 0.10f }, BorderColor = HudKit.Lime, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2 });
        hud.AddChild(_boxRect);
        var keys = HudKit.Text("Box-drag to select many - Shift+click adds - drag to move - Ctrl+D duplicate - R rotate 90 - Q / E turn (Shift: 5) - L lock - M measure - Del removes - Ctrl+Z / Y undo / redo - right-drag turns the camera - T top view", 12, HudKit.Dim, HudKit.Semi);
        keys.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        keys.OffsetLeft = 330; keys.OffsetRight = -350; keys.OffsetTop = -18; keys.OffsetBottom = -2;
        keys.HorizontalAlignment = HorizontalAlignment.Center;
        keys.MouseFilter = MouseFilterEnum.Ignore;
        hud.AddChild(keys);

        BuildBoard();
        ApplyCamera();
        Rebuild();
    }

    public override void _ExitTree() => MenuBackdrop.Instance?.SetActive(true);

    private EGameSystem DefaultGame() =>
        GameSystemInfo.DisplayOrder.FirstOrDefault(s => TableSizing.SizesFor(s).Any(z => TableSizing.Same(z, (W, D))), EGameSystem.GrimdarkFuture);

    private void Say(string text, Color? c = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c ?? HudKit.Dim);
    }

    // ================================================================== back / cancel

    public bool AllowBack()
    {
        if (!Draft.Dirty) return true;
        var dlg = new ConfirmationDialog { Title = "Leave without saving?", DialogText = "This map has changes that aren't saved. Leave and lose them?", OkButtonText = "Leave", CancelButtonText = "Keep editing" };
        dlg.Confirmed += () => { Draft = MapDraft.New(W, D); dlg.QueueFree(); App.I.Back(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
        return false;
    }

    private void SaveMap()
    {
        string? path = Draft.Save(out string? error);
        if (path == null) { Say(error ?? "Couldn't save the map.", Palette.WoundsRed); return; }
        Say($"Saved \"{Draft.Name.Trim()}\".", Palette.Lime);
        Rebuild();
    }

    // ================================================================== panels

    private void Rebuild()
    {
        TopBar();
        ToolRail();
        LeftPanel();
        RightPanel();
        Sync3D();
    }

    private static void Clear(Container c) { foreach (Node n in c.GetChildren()) { c.RemoveChild(n); n.QueueFree(); } }

    private void TopBar()
    {
        Clear(_top);
        _top.AddChild(Ui.Button("Back", App.I.Back, "Back to the map list (asks if there are unsaved changes).", "BackButton").Also(b => { b.Name = "Back"; b.CustomMinimumSize = new Vector2(100, 40); }));
        // (No "MAP MAKER" title since option A's extra buttons: the bar needs the room, and the screen says what it is.)
        _name = new LineEdit { Name = "MapName", Text = Draft.Name, CustomMinimumSize = new Vector2(220, 0), MaxLength = 48, PlaceholderText = "map name", TooltipText = "The map's name (plain letters)." };
        _name.TextChanged += t => Draft.Name = t;
        _top.AddChild(_name);
        var sizes = TableSizing.Sizes.ToList();
        var size = new OptionButton { Name = "TableSize", TooltipText = MapDraft.SizeGames(W, D) };
        foreach (var s in sizes) size.AddItem(MapDraft.SizeName(s.W, s.H));
        int si = sizes.FindIndex(s => TableSizing.Same(s, (W, D)));
        if (si < 0) { size.AddItem(MapDraft.SizeName(W, D)); si = size.ItemCount - 1; }
        size.Selected = si;
        size.ItemSelected += i =>
        {
            if (i >= sizes.Count) return;
            SetTableSize(sizes[(int)i].W, sizes[(int)i].H);
        };
        _top.AddChild(size);
        // Kai, 2026-11-18: mat, horizon, sky, light, weather and ground live in one Battlefield window (the top bar ran off
        // the screen).
        var mats = MatsHere();
        string matName = mats.FirstOrDefault(m => m.Look == Draft.Mat)?.Name ?? Draft.Mat switch
        {
            "" or MapAtmosphere.GroundMat => string.Join(" + ", Horizon.Pick(MapAtmosphere.HorizonFor(HorizonId), Draft.Ground).Select(g => g.Name)) is { Length: > 0 } g ? g : "Ground",
            MapAtmosphere.FeltMat => "Plain felt",
            _ => "Mat (not on this PC)",
        };
        string weatherName = MapAtmosphere.WeatherById(Draft.Weather)?.Name ?? "Clear";
        _top.AddChild(Ui.Button("Battlefield...", OpenBattlefield,
            $"The battlefield's look, saved with the map: play area (mat or ground), the ground and how grounds mix, the horizon, sky, light and weather. Only for looks.\nNow: {matName}, {weatherName}.")
            .Also(b => b.Name = "BattlefieldButton"));
        _top.AddChild(Ui.Grow());
        var rv = new Button { Name = "RulesView", Text = "Rules view", ToggleMode = true, ButtonPressed = _rulesView, FocusMode = FocusModeEnum.None,
            TooltipText = "Hide the scenery and colour every piece by what it does." };
        rv.Toggled += on => { _rulesView = on; Sync3D(); };
        _top.AddChild(rv);
        _top.AddChild(Ui.Button("Generate...", OpenGenerate, TerrainBook.HasGenerator(_checkFor)
            ? "The Advanced Rulebook's terrain generator: rolls a themed table, then you edit it."
            : $"{GameSystemInfo.DisplayName(_checkFor)}'s book has no terrain generator - pick a game that has one under Book check.").Also(b => { b.Name = "Generate"; b.Disabled = !TerrainBook.HasGenerator(_checkFor); }));
        _top.AddChild(Ui.Button("Undo", () => { if (Draft.Undo()) AfterUndo(); }, "Undo (Ctrl+Z).").Also(b => { b.Name = "Undo"; b.Disabled = !Draft.CanUndo; }));
        _top.AddChild(Ui.Button("Redo", () => { if (Draft.Redo()) AfterUndo(); }, "Redo (Ctrl+Y).").Also(b => { b.Name = "Redo"; b.Disabled = !Draft.CanRedo; }));
        _top.AddChild(Ui.Button("Copy", CopyPicked, "Copy the selected pieces (Ctrl+C).").Also(b => { b.Name = "CopyPieces"; b.Disabled = _picked.Count == 0; }));
        _top.AddChild(Ui.Button("Paste", Paste, "Paste the copied pieces, a little down and right (Ctrl+V).").Also(b => { b.Name = "PastePieces"; b.Disabled = _clipboard.Count == 0; }));
        _top.AddChild(Ui.Button("Duplicate", DuplicatePicked, "Copy and paste the selected pieces in one go (Ctrl+D).").Also(b => { b.Name = "DuplicatePieces"; b.Disabled = _picked.Count == 0; }));
        _top.AddChild(SnapButton(1f, "Snap 1\"", "Moves and sizes land on whole inches. Click again for no snap."));
        _top.AddChild(SnapButton(0.5f, "Snap 1/2\"", "Moves and sizes land on half inches. Click again for no snap."));
        var grid = new Button { Name = "Grid", Text = "Grid 6\"", ToggleMode = true, ButtonPressed = _grid, FocusMode = FocusModeEnum.None,
            TooltipText = "Faint lines every 6\" on the table, to judge gaps and distances. Editor only." };
        grid.Toggled += on => { _grid = on; Sync3D(); };
        _top.AddChild(grid);
        _top.AddChild(Ui.Button("SAVE MAP", SaveMap, Draft.SaveProblem() ?? "Save the map to your Maps folder.", "CommitButton").Also(b => { b.Name = "SaveMap"; b.CustomMinimumSize = new Vector2(150, 40); }));
    }

    /// <summary>A different table: everything is brought back onto it (pieces, objective markers and size bases - the
    /// markers used to be left hanging off a smaller table), and the Book check moves to a game that plays on it.</summary>
    private void SetTableSize(float w, float d)
    {
        Draft.Remember();
        Draft.TableW = w; Draft.TableH = d;
        Draft.ClampAll();
        _checkFor = DefaultGame();
        BuildBoard(); ApplyCamera(); Rebuild();
    }

    private Button SnapButton(float step, string text, string tip)
    {
        var b = new Button { Name = step == 1f ? "Snap1" : "SnapHalf", Text = text, ToggleMode = true, ButtonPressed = _snapStep == step, FocusMode = FocusModeEnum.None, TooltipText = tip };
        b.Pressed += () =>
        {
            _snapStep = _snapStep == step ? 0f : step;
            Say(_snapStep == 0 ? "Snap off - pieces move in quarter inches." : $"Snapping to {(_snapStep == 1 ? "1\"" : "1/2\"")}.");
            TopBar();
        };
        return b;
    }

    private float Snap(float v) => MapDraft.SnapTo(v, _snapStep);

    // ================================================================== the tool rail (option A)

    private void ToolRail()
    {
        Clear(_rail);
        void Add(ETool t, string text, string tip, Action? pick = null)
        {
            var b = new Button { Name = "Tool" + t, Text = text, ToggleMode = true, ButtonPressed = _tool == t, FocusMode = FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(96, 38) };
            b.Pressed += pick ?? (() => SetTool(t));
            _rail.AddChild(b);
        }
        Add(ETool.Select, "Select", "Click a piece to pick it, drag to move it. Shift+click adds or removes a piece; drag an empty spot to box-select.");
        Add(ETool.BoxSelect, "Box select", "Drag a box: every piece whose middle is inside gets selected (Shift: add to what's selected).");
        Add(ETool.Place, "Place", "Place the piece picked on the left - click the table. Right-click stops.", () => { SetTool(ETool.Place); Say(_model != null ? $"Placing {_model.Name} - click the table." : $"Placing {_kind.Name} - click the table. Pick another on the left."); });
        Add(ETool.Objective, "Objective", "Fixed objective markers. With none, the game rolls D3+2 and places them the usual way.");
        Add(ETool.Base, "Size base", "A model base, only to check a unit fits through a gap. The game ignores these.");
        Add(ETool.Measure, "Measure", "Drag on the table to measure in inches (M). Editor only.");
    }

    private void SetTool(ETool t)
    {
        _tool = t;
        if (t != ETool.Measure) { _measureA = _measureB = null; }
        if (t is ETool.Place or ETool.Objective or ETool.Base) { _picked.Clear(); _selObjective = -1; _selBase = -1; }
        Say(t switch
        {
            ETool.BoxSelect => "Drag a box over the pieces to select them. Shift adds to the selection.",
            ETool.Objective => "Click the table to place objective markers. Right-click to stop.",
            ETool.Base => "Click the table to drop a size base. Right-click to stop.",
            ETool.Measure => "Drag on the table to measure. Right-click to stop.",
            _ => "",
        });
        Rebuild();
    }

    private void AfterUndo()
    {
        _sel = null; _selObjective = -1; _selBase = -1;
        BuildBoard(); Rebuild();
    }

    /// <summary>Sets a rebuilt list's scroll back once its containers have laid it out (two frames).</summary>
    private async void RestoreScroll(ScrollContainer s, int y)
    {
        if (y <= 0) return;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(s)) s.ScrollVertical = y;
    }

    private void LeftPanel()
    {
        int leftY = _leftScroll?.ScrollVertical ?? 0;
        KeepPalette();
        Clear(_left);
        if (_leftScroll is { } outer) RestoreScroll(outer, leftY);
        _left.AddChild(HudKit.Heading("Pieces (the book's)", 13));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 5);
        grid.AddThemeConstantOverride("v_separation", 5);
        foreach (TerrainBook.PieceKind k in TerrainBook.Kinds)
        {
            var kind = k;
            bool on = _tool == ETool.Place && _model == null && _kind == k;
            var b = new Button { Name = "Kind_" + k.Id, Text = k.Name, ToggleMode = true, ButtonPressed = on, FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(126, 34), TooltipText = $"{k.BookLine}\n(Advanced Rulebook 3.6.0)\nClick it, then click the table. Right-click stops placing." };
            b.AddThemeColorOverride("font_color", BattleTable.TerrainColour(k.Rules).Lightened(0.45f));
            b.Pressed += () => { _tool = ETool.Place; _kind = kind; _model = null; _sel = null; _selObjective = -1; _selBase = -1; Say($"Placing {kind.Name} - click the table. Right-click to stop."); Rebuild(); };
            grid.AddChild(b);
        }
        _left.AddChild(grid);
        ModelPalette();
        _left.AddChild(HudKit.Heading("Also", 13));
        var also = new HBoxContainer();
        also.AddThemeConstantOverride("separation", 5);
        also.AddChild(new Button { Name = "ToolObjective", Text = "Objective", ToggleMode = true, ButtonPressed = _tool == ETool.Objective, FocusMode = FocusModeEnum.None,
            TooltipText = "Fixed objective markers. With none, the game rolls D3+2 and places them the usual way." }
            .Also(b => b.Pressed += () => SetTool(ETool.Objective)));
        also.AddChild(new Button { Name = "ToolBase", Text = "Size base", ToggleMode = true, ButtonPressed = _tool == ETool.Base, FocusMode = FocusModeEnum.None,
            TooltipText = "A model base, only to check a unit fits through a gap. The game ignores these." }
            .Also(b => b.Pressed += () => SetTool(ETool.Base)));
        var bs = new OptionButton { TooltipText = "Base size for the next size base." };
        int[] sizes = { 25, 32, 40, 50, 60 };
        foreach (int s in sizes) bs.AddItem($"{s}mm");
        bs.Selected = Array.IndexOf(sizes, _baseSize);
        bs.ItemSelected += i => _baseSize = sizes[i];
        also.AddChild(bs);
        _left.AddChild(also);
        _left.AddChild(HudKit.Text($"{Draft.Pieces.Count} pieces - {Draft.Objectives.Count} objectives - {Draft.Bases.Count} size bases", 13, HudKit.Dim, HudKit.Semi));
        if (Draft.Objectives.Count == 0) _left.AddChild(ForgeWidgets.Wrapped("No objectives: the game rolls D3+2 and places them the usual way.", 12, HudKit.Dim));
        _left.AddChild(Ui.Button("Clear the table", AskClear, "Remove every piece, objective and size base (undo brings them back).", "BackButton").Also(b => b.Name = "ClearTable"));

        // Book check
        _left.AddChild(HudKit.Heading("Book check", 13));
        var game = new OptionButton { Name = "CheckGame", TooltipText = "Which game's book to check this table against." };
        var games = GameSystemInfo.DisplayOrder.ToList();
        foreach (var g in games) game.AddItem(GameSystemInfo.DisplayName(g));
        game.Selected = games.IndexOf(_checkFor);
        game.ItemSelected += i => { _checkFor = games[(int)i]; Rebuild(); };
        _left.AddChild(game);
        foreach (var line in Draft.BookCheck(_checkFor))
        {
            var r = new VBoxContainer();
            r.AddThemeConstantOverride("separation", -2);
            r.AddChild(HudKit.Text(line.Label, 13, HudKit.Dim, HudKit.Semi));
            r.AddChild(ForgeWidgets.Wrapped(line.Value, 13, line.Ok ? Palette.Lime : Palette.WoundsRed));
            _left.AddChild(r);
        }
        if (TerrainBook.GuidanceFor(_checkFor) is { } gd)
            _left.AddChild(ForgeWidgets.Wrapped($"{gd.Book}. Advice only - it never stops you saving. Each player picking 1 piece to be Dangerous happens in the game, not the map.", 11, HudKit.Dim));
    }

    /// <summary>
    /// Kai, 2026-11-14: the downloaded Warhammer models, matched on the Match terrain screen - each one is a piece:
    /// its size is the model's, its rules the kind it was matched to. Hidden and unmatched pieces are left out.
    /// </summary>
    // Kai, 2026-11-18: picking or placing a piece must not touch the model list at all (no jump, no flicker) - so the
    // list is built once and kept, moved back into the rebuilt left panel; only which tile is lit changes. It is built
    // again only when what it shows changes (the set / kind filters, or the matches being re-read).
    private VBoxContainer? _palette;
    private string _paletteSig = "";
    private readonly Dictionary<int, Button> _paletteButtons = new();

    /// <summary>Takes the kept model list out of the left panel before the panel is cleared, so it survives.</summary>
    private void KeepPalette()
    {
        if (_palette != null && IsInstanceValid(_palette) && _palette.GetParent() == _left) _left.RemoveChild(_palette);
    }

    private void ModelPalette()
    {
        if (WarhammerModels.Library is not { } lib || lib.Entries.Count == 0) return;
        // Not the library object itself: saving a sink re-reads it, and the list must not be rebuilt for that.
        string sig = $"{lib.Folder}|{lib.Entries.Count}|{_modelSet}|{_modelKind}";
        if (sig == _paletteSig && _palette != null && IsInstanceValid(_palette))
        {
            _left.AddChild(_palette);
            foreach (var (id, btn) in _paletteButtons)
                if (IsInstanceValid(btn)) btn.SetPressedNoSignal(_tool == ETool.Place && _model?.Id == id);
            return;
        }
        if (_palette != null && IsInstanceValid(_palette)) _palette.QueueFree();
        _paletteButtons.Clear();
        _paletteSig = sig;
        var box = _palette = new VBoxContainer { Name = "ModelPalette" };
        box.AddThemeConstantOverride("separation", 6);
        _left.AddChild(box);
        BuildPalette(box, lib);
    }

    private void BuildPalette(VBoxContainer box, WarhammerTerrainLibrary lib)
    {
        box.AddChild(HudKit.Heading("Warhammer models", 13));
        var all = lib.Placeable.ToList();
        if (all.Count == 0)
        {
            box.AddChild(ForgeWidgets.Wrapped("None matched yet - Map Maker -> Match terrain models.", 12, HudKit.Dim));
            return;
        }
        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 5);
        var sets = lib.Sets.OrderBy(k => k.Key).ToList();
        var set = new OptionButton { Name = "ModelSet", TooltipText = "Which of the mod's terrain sets to show." };
        set.AddItem("All sets");
        foreach (var kv in sets) set.AddItem(kv.Value);
        set.Selected = Math.Max(0, sets.FindIndex(kv => kv.Key == _modelSet) + 1);
        set.ItemSelected += i => { _modelSet = i <= 0 ? -1 : sets[(int)i - 1].Key; Rebuild(); };
        filters.AddChild(set);
        var kinds = WarhammerTerrainLibrary.Kinds.Where(k => all.Any(e => e.Match.Kind == k.Id)).ToList();
        var kind = new OptionButton { Name = "ModelKind", TooltipText = "Only pieces matched to this kind." };
        kind.AddItem("All kinds");
        foreach (var k in kinds) kind.AddItem(k.Name);
        kind.Selected = Math.Max(0, kinds.FindIndex(k => k.Id == _modelKind) + 1);
        kind.ItemSelected += i => { _modelKind = i <= 0 ? "" : kinds[(int)i - 1].Id; Rebuild(); };
        filters.AddChild(kind);
        box.AddChild(filters);

        var shown = all.Where(e => (_modelSet < 0 || e.Set == _modelSet) && (_modelKind.Length == 0 || e.Match.Kind == _modelKind))
            .OrderBy(e => e.Match.Kind).ThenBy(e => e.Name).ToList();
        // Kai, 2026-11-14: a grid of pictures with names, not a plain list, so you can see what you are picking.
        var list = new GridContainer { Columns = 3 };
        list.AddThemeConstantOverride("h_separation", 5);
        list.AddThemeConstantOverride("v_separation", 5);
        foreach (var e in shown)
        {
            var entry = e;
            string kindName = WarhammerTerrainLibrary.Kinds.FirstOrDefault(k => k.Id == e.Match.Kind).Name ?? e.Match.Kind;
            string size = e.Measured ? $"{e.Match.W:0.#} x {e.Match.D:0.#}\"" : "";
            var b = new Button { Name = "Model_" + e.Id, ToggleMode = true, FocusMode = FocusModeEnum.None,
                ButtonPressed = _tool == ETool.Place && _model?.Id == e.Id, CustomMinimumSize = new Vector2(128, 152),
                TooltipText = $"{e.Name}\n{e.SetName}\n{kindName}" + (size.Length > 0 ? $" - {size}" : "") + (e.Keywords.Length > 0 ? $"\nWarhammer: {e.Keywords}" : "") + "\nClick it, then click the table." };
            var tile = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            tile.AddThemeConstantOverride("separation", 1);
            tile.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            tile.OffsetLeft = 4; tile.OffsetRight = -4; tile.OffsetTop = 4; tile.OffsetBottom = -3;
            var pic = new TextureRect { Texture = ModelThumb(lib, e.Id), CustomMinimumSize = new Vector2(0, 92), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
            tile.AddChild(pic);
            if (pic.Texture == null) tile.AddChild(HudKit.Text("(no picture yet)", 11, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
            var name = HudKit.Text(e.Name, 12, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center);
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            name.MaxLinesVisible = 2;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.MouseFilter = MouseFilterEnum.Ignore;
            tile.AddChild(name);
            var sub = HudKit.Text(kindName + (size.Length > 0 ? " - " + size : ""), 11,
                BattleTable.TerrainColour(WarhammerTerrainLibrary.RulesFor(e.Match.Kind)).Lightened(0.45f), HudKit.Semi, HorizontalAlignment.Center);
            sub.ClipText = true;
            sub.MouseFilter = MouseFilterEnum.Ignore;
            tile.AddChild(sub);
            b.AddChild(tile);
            b.Pressed += () => { _tool = ETool.Place; _model = entry; _sel = null; _selObjective = -1; _selBase = -1; Say($"Placing {entry.Name} - click the table. Right-click to stop."); Rebuild(); };
            _paletteButtons[e.Id] = b;
            list.AddChild(b);
        }
        var scroll = new ScrollContainer { Name = "ModelScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 470) };
        scroll.AddChild(list);
        box.AddChild(scroll);
        box.AddChild(HudKit.Text($"{shown.Count} of {all.Count} matched pieces", 12, HudKit.Dim, HudKit.Semi));
    }

    // ================================================================== battle mats

    /// <summary>The Warhammer Map Builder mats whose picture is on this computer.</summary>
    private static List<WarhammerTerrain.Mat> MatsHere() =>
        (WarhammerTerrain.LoadMats() ?? new()).Where(m => WarhammerTerrain.MatFile(m.Look) != null).ToList();

    /// <summary>A small copy of a mat's picture for the picker, made once and kept in _mats/_thumbs.</summary>
    private static Texture2D? MatThumb(WarhammerTerrain.Mat m)
    {
        string f = Path.Combine(WarhammerTerrain.MatsFolder, "_thumbs", m.Id + ".png");
        if (Thumbs.TryGetValue(f, out var have) && have != null) return have;
        if (!File.Exists(f))
        {
            if (WarhammerTerrain.MatFile(m.Look) is not { } src || Image.LoadFromFile(src) is not { } big || big.IsEmpty()) return null;
            if (big.GetHeight() > big.GetWidth()) big.Rotate90(ClockDirection.Clockwise);
            big.Resize(240, Math.Max(1, 240 * big.GetHeight() / big.GetWidth()), Image.Interpolation.Bilinear);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            big.SavePng(f);
        }
        Texture2D? t = Image.LoadFromFile(f) is { } img ? ImageTexture.CreateFromImage(img) : null;
        return Thumbs[f] = t;
    }

    /// <summary>The old name for the Battlefield window (ScriptedRun "mats").</summary>
    public void OpenMatPicker() => OpenBattlefield();

    /// <summary>
    /// Kai, 2026-11-18: everything about how the battlefield looks, in one window - the play area (each of the style's
    /// grounds is a mat by itself, plus the current mix, the plain felt and the downloaded mats), the ground mix, the
    /// horizon and weather, the sky picture and its turn, and the light. All saved with the map; all looks only.
    /// </summary>
    public void OpenBattlefield()
    {
        GetNodeOrNull("Battlefield")?.QueueFree();
        var theme = MapAtmosphere.HorizonFor(HorizonId);
        var grounds = Horizon.Grounds(theme);
        var skies = Horizon.Skies(theme);
        var light = MapAtmosphere.Light.Parse(Draft.Light);
        var veil = new Control { Name = "Battlefield", MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(1120, 0) };
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(HudKit.Text("BATTLEFIELD", 26, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(ForgeWidgets.Wrapped($"How the battlefield looks - saved with the map, looks only. New pictures go in OprGodot/horizon/{theme}/: grounds named ground*.jpg (ground2.jpg, ground-mud.jpg...), skies named sky* (sky2.exr...). Press Look again after adding some.", 13, HudKit.Dim));
        void Reopen() { veil.QueueFree(); Rebuild(); OpenBattlefield(); }

        // ---- horizon, weather
        var row1 = new HBoxContainer();
        row1.AddThemeConstantOverride("separation", 10);
        var horizon = new OptionButton { Name = "HorizonPick", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(230, 0),
            TooltipText = "The style beyond the table (Settings -> Around the table = Battlefield): its land, sky and grounds. Auto follows the map's terrain style (Generate...)." };
        foreach (string h in MapAtmosphere.Horizons) horizon.AddItem("Horizon: " + (h.Length == 0 ? "Auto" : h));
        horizon.Selected = Math.Max(0, MapAtmosphere.Horizons.ToList().IndexOf(Draft.Horizon));
        horizon.ItemSelected += i => { Draft.Remember(); Draft.Horizon = MapAtmosphere.Horizons[(int)i]; RebuildWorld(); Reopen(); };
        row1.AddChild(horizon);
        var weather = new OptionButton { Name = "WeatherPick", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(230, 0),
            TooltipText = "The weather over the table (rain, snow, fog...). Players can turn weather off in Settings." };
        var weathers = MapAtmosphere.Weathers;
        foreach (var w in weathers) weather.AddItem("Weather: " + w.Name);
        weather.Selected = Math.Max(0, weathers.ToList().FindIndex(w => w.Id == Draft.Weather));
        weather.ItemSelected += i => { Draft.Remember(); Draft.Weather = weathers[(int)i].Id; BuildBoard(); };
        row1.AddChild(weather);
        row1.AddChild(Ui.Grow());
        row1.AddChild(Ui.Button("Look again", () => { Horizon.Rescan(); RebuildWorld(); Reopen(); }, "Read the style's folder again after adding ground or sky pictures.").Also(b => b.Name = "LookAgain"));
        col.AddChild(row1);

        // ---- sky + light
        col.AddChild(HudKit.Heading("Sky and light", 14));
        var skyRow = new HBoxContainer();
        skyRow.AddThemeConstantOverride("separation", 10);
        var sky = new OptionButton { Name = "SkyPick", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(230, 0),
            TooltipText = "The 360-degree sky picture (the style's sky* files). A style with none has a painted sky." };
        if (skies.Count == 0) { sky.AddItem("Sky: painted"); sky.Disabled = true; }
        foreach (var k in skies) sky.AddItem("Sky: " + k.Name);
        sky.Selected = Math.Max(0, skies.ToList().FindIndex(k => k.Id.Equals(Draft.Sky, StringComparison.OrdinalIgnoreCase)));
        sky.ItemSelected += i => { Draft.Remember(); Draft.Sky = i == 0 ? "" : skies[(int)i].Id; LightChanged(); };
        skyRow.AddChild(sky);
        col.AddChild(skyRow);
        var sliders = new GridContainer { Columns = 3 };
        sliders.AddThemeConstantOverride("h_separation", 12);
        sliders.AddThemeConstantOverride("v_separation", 4);
        void Slider(string name, string label, string tip, double min, double max, double step, float value, Func<float, MapAtmosphere.Light> with, Func<float, string> show)
        {
            sliders.AddChild(HudKit.Text(label, 14, HudKit.Fg, HudKit.Semi).Also(l => { l.TooltipText = tip; l.MouseFilter = MouseFilterEnum.Stop; l.CustomMinimumSize = new Vector2(150, 0); }));
            var s = new HSlider { Name = name, MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(560, 24), TooltipText = tip };
            var val = HudKit.Text(show(value), 14, HudKit.Fg, HudKit.Semi).Also(l => l.CustomMinimumSize = new Vector2(80, 0));
            s.ValueChanged += v =>
            {
                val.Text = show((float)v);
                light = with((float)v);
                Draft.Light = light.ToText();
                LightChanged();
            };
            s.DragStarted += () => Draft.Remember();
            sliders.AddChild(s);
            sliders.AddChild(val);
        }
        string Deg(float v) => $"{v:0} deg";
        Slider("SunHeight", "Sun height", "How high the sun is: low = long shadows and a golden-hour feel, high = midday.", 5, 90, 1, light.SunHeight, v => light with { SunHeight = v }, Deg);
        Slider("SunTurn", "Sun direction", "Which side of the table the sun shines from.", 0, 360, 1, ((light.SunTurn % 360) + 360) % 360, v => light with { SunTurn = v }, Deg);
        Slider("Brightness", "Brightness", "How bright the whole scene is - below 1 for dusk or a gloomy world.", 0.2, 2, 0.05, light.Brightness, v => light with { Brightness = v }, v => $"{v:0.00}");
        Slider("Warmth", "Warmth", "The sunlight's colour: left cold and blue (night, winter, alien worlds), right warm orange (sunset, fire).", -1, 1, 0.05, light.Warmth, v => light with { Warmth = v }, v => v == 0 ? "neutral" : v < 0 ? $"cold {-v:0.00}" : $"warm {v:0.00}");
        Slider("SkyTurn", "Sky turn", "Turns the sky picture round the table - put its sun or its best clouds where you want them.", 0, 360, 1, ((light.SkyTurn % 360) + 360) % 360, v => light with { SkyTurn = v }, Deg);
        col.AddChild(sliders);
        col.AddChild(Ui.Button("Default light", () => { Draft.Remember(); Draft.Light = ""; LightChanged(); Reopen(); }, "Back to the normal light and an unturned sky.").Also(b => { b.Name = "DefaultLight"; b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; }));

        // ---- ground mix
        col.AddChild(HudKit.Heading("Ground", 14));
        var current = Horizon.Pick(theme, Draft.Ground).Select(g => g.Id).ToList();
        var pickers = new List<OptionButton>();
        var groundRow = new HBoxContainer();
        groundRow.AddThemeConstantOverride("separation", 10);
        string[] labels = { "Main", "Mixed with", "And" };
        for (int r = 0; r < 3; r++)
        {
            int row = r;
            groundRow.AddChild(HudKit.Text(labels[row], 14, HudKit.Fg, HudKit.Semi));
            var pick = new OptionButton { Name = "GroundPick" + row, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(190, 0),
                TooltipText = row == 0 ? "The ground that covers the field." : "Another ground showing through the main one in big natural patches." };
            if (row > 0) pick.AddItem("(none)");
            foreach (var g in grounds) pick.AddItem(g.Name);
            string? want = current.ElementAtOrDefault(row);
            int at = want == null ? -1 : grounds.ToList().FindIndex(g => g.Id == want);
            pick.Selected = row == 0 ? Math.Max(0, at) : at + 1;
            pick.Disabled = row > 0 && grounds.Count < 2;
            pick.ItemSelected += _ =>
            {
                var ids = pickers.Select((o, i) => i == 0 ? grounds.ElementAtOrDefault(o.Selected)?.Id : o.Selected > 0 ? grounds.ElementAtOrDefault(o.Selected - 1)?.Id : null)
                    .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string spec = ids.Count == 1 && ids[0] == grounds.FirstOrDefault()?.Id ? "" : string.Join("+", ids);
                if (spec == Draft.Ground) return;
                Draft.Remember();
                Draft.Ground = spec;
                RebuildWorld();
                Reopen();
            };
            pickers.Add(pick);
            groundRow.AddChild(pick);
        }
        col.AddChild(groundRow);

        // ---- play area: every ground is a mat by itself, the current mix, the felt, the downloaded mats
        col.AddChild(HudKit.Heading("Play area", 14));
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        Button Tile(string name, string nodeName, bool on, Texture2D? texture, Action pick)
        {
            var b = new Button { Name = nodeName, ToggleMode = true, ButtonPressed = on, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(172, 136), TooltipText = name };
            var tile = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            tile.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            tile.OffsetLeft = 4; tile.OffsetRight = -4; tile.OffsetTop = 4; tile.OffsetBottom = -3;
            tile.AddChild(new TextureRect { Name = "Pic", Texture = texture, CustomMinimumSize = new Vector2(0, 104), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore });
            tile.AddChild(HudKit.Text(name, 12, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center).Also(l => { l.MouseFilter = MouseFilterEnum.Ignore; l.ClipText = true; }));
            b.AddChild(tile);
            b.Pressed += () => { pick(); Reopen(); };
            grid.AddChild(b);
            return b;
        }
        bool groundMat = Draft.Mat is "" or MapAtmosphere.GroundMat;
        var mix = Horizon.Pick(theme, Draft.Ground);
        foreach (var g in grounds)
        {
            var gg = g;
            Tile(g.Name, "Mat_" + g.Id, groundMat && mix.Count == 1 && mix[0].Id == g.Id, Horizon.LoadPicture(g.Color), () =>
            {
                Draft.Remember();
                Draft.Mat = "";
                Draft.Ground = gg.Id == grounds[0].Id ? "" : gg.Id;
                RebuildWorld();
            });
        }
        if (mix.Count > 1)
            Tile("Mix: " + string.Join(" + ", mix.Select(g => g.Name)), "Mat_Mix", groundMat, Horizon.LoadPicture(mix[0].Color), () => { Draft.Remember(); Draft.Mat = ""; BuildBoard(); });
        Tile("Plain felt", "Mat_felt", Draft.Mat == MapAtmosphere.FeltMat, TableLook.Felt(24, 16), () => { Draft.Remember(); Draft.Mat = MapAtmosphere.FeltMat; BuildBoard(); });
        var pics = new List<(TextureRect Pic, WarhammerTerrain.Mat Mat)>();
        foreach (var m in MatsHere())
        {
            var mm = m;
            var b = Tile(m.Name, "Mat_" + m.Look[WarhammerTerrain.MatPrefix.Length..], Draft.Mat == m.Look, null, () => { Draft.Remember(); Draft.Mat = mm.Look; BuildBoard(); });
            if (b.FindChild("Pic", true, false) is TextureRect pic) pics.Add((pic, m));
        }
        col.AddChild(grid);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddChild(Ui.Button("Done", () => { veil.QueueFree(); Rebuild(); }, "Close.").Also(b => b.Name = "CloseBattlefield"));
        col.AddChild(buttons);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(1160, 860) };
        scroll.AddChild(HudKit.Pad(col, 20, 16, 20, 16));
        AddChild(veil);
        HudKit.Place(veil, HudKit.Panel(HudKit.PanelBox(14), scroll), LayoutPreset.Center);
        _ = FillMatThumbs(veil, pics);
    }

    /// <summary>Back from Settings (Kai, 2026-11-18: settings take effect at once): room or battlefield, weather, room
    /// brightness and the lens are set again; the map is untouched.</summary>
    public void SettingsChanged()
    {
        TableLook.RemoveLighting(_world);
        TableLook.AddLighting(_world, TableLook.RoomFor(_checkFor), HorizonId, W, D, Horizon.On, Draft.Sky);
        Horizon.ApplyLight(_world, MapAtmosphere.Light.Parse(Draft.Light));
        BuildBoard();
        _camera.Fov = Horizon.Lens;
        _camera.Far = Horizon.On ? Horizon.Reach * 2.5f : 2000;
        foreach (var (_, look) in _looks.Values) look.QueueFree();
        _looks.Clear();
        Sync3D();
        ApplyCamera();
    }

    /// <summary>The sky and light changed: sky, haze and sun set again, then the weather dims the new light.</summary>
    private void LightChanged()
    {
        Atmosphere();
        Horizon.ApplyLight(_world, MapAtmosphere.Light.Parse(Draft.Light));
        Weather.Overcast(_world, Draft.Weather);
    }

    /// <summary>Horizon or ground changed: the sky and the land (and the table's ground) are made again.</summary>
    private void RebuildWorld()
    {
        Atmosphere();
        BuildBoard();
        Horizon.ApplyLight(_world, MapAtmosphere.Light.Parse(Draft.Light));
        Weather.Overcast(_world, Draft.Weather);
    }

    /// <summary>Loads the picker's pictures a few a frame, so it opens at once (the first time makes the small copies).</summary>
    private async Task FillMatThumbs(Control picker, List<(TextureRect Pic, WarhammerTerrain.Mat Mat)> pics)
    {
        foreach (var (pic, mat) in pics)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(picker) || !IsInstanceValid(pic)) return;
            pic.Texture = MatThumb(mat);
        }
    }

    private static readonly Dictionary<string, Texture2D?> Thumbs = new();

    /// <summary>A piece's picture from the match screen (UnitModels/Terrain - Warhammer/_thumbs), or null before it is made.</summary>
    private static Texture2D? ModelThumb(WarhammerTerrainLibrary lib, int id)
    {
        string f = Path.Combine(lib.Folder, "_thumbs", id + ".png");
        if (Thumbs.TryGetValue(f, out var t) && t != null) return t;
        t = File.Exists(f) && Image.LoadFromFile(f) is { } img ? ImageTexture.CreateFromImage(img) : null;
        return Thumbs[f] = t;
    }

    private void RightPanel()
    {
        Clear(_right);
        if (_picked.Count > 1)
        {
            GroupPanel();
            return;
        }
        if (_sel is { } p)
        {
            _right.AddChild(HudKit.Heading("Selected piece", 13));
            if (p.Locked) _right.AddChild(ForgeWidgets.Wrapped("Locked - it can't be dragged, turned or resized. L unlocks it.", 12, LockColour));
            var name = new LineEdit { Name = "PieceName", Text = p.Name, MaxLength = 40, TooltipText = "What the piece is called in the game (plain letters)." };
            name.TextSubmitted += t => { Draft.Remember(); p.Name = t.Trim(); Rebuild(); };
            name.FocusExited += () => { if (name.Text.Trim() != p.Name) { Draft.Remember(); p.Name = name.Text.Trim(); Sync3D(); } };
            _right.AddChild(name);

            var kinds = TerrainBook.Kinds.ToList();
            var kind = new OptionButton { Name = "PieceKind", TooltipText = "Which of the book's terrain kinds it is. Changing it gives the piece that kind's rules." };
            foreach (var k in kinds) kind.AddItem(k.Name);
            kind.Selected = Math.Max(0, kinds.FindIndex(k => k.Id == p.Kind));
            kind.ItemSelected += i =>
            {
                Draft.Remember();
                var k = kinds[(int)i];
                // A Warhammer model piece keeps its model and its height; only its rules change with the kind.
                bool model = WarhammerTerrainLibrary.IdOf(p.Look) != null;
                p.Kind = k.Id; p.Rules = k.Rules;
                if (!model) { p.Height = k.Height; p.Look = ""; }
                if (p.Name.Length == 0 || TerrainBook.Kinds.Any(o => o.Name == p.Name)) p.Name = k.Name;
                Rebuild();
            };
            _right.AddChild(Row("Kind", kind));

            if (WarhammerModels.Library?.ForLook(p.Look) is { } shownModel)
                _right.AddChild(ForgeWidgets.Wrapped($"Warhammer model: {shownModel.Name} ({shownModel.SetName}). Width and depth stretch it.", 12, HudKit.Lime));
            else if (WarhammerTerrainLibrary.IdOf(p.Look) != null)
                _right.AddChild(ForgeWidgets.Wrapped("A Warhammer model this computer doesn't have - shown as the plain piece.", 12, HudKit.Dim));
            bool modelPiece = WarhammerTerrainLibrary.IdOf(p.Look) != null;
            var looks = new List<TerrainLooks.Look>(); // stand-ins only for now - real looks come from the Terrain Lab
            var look = new OptionButton { Name = "PieceLook", TooltipText = "The 3D models it shows as. Cosmetic - the rules don't change. Auto lets the game pick." };
            look.AddItem("Auto");
            foreach (var l in looks) look.AddItem(l.Name);
            int li = looks.ToList().FindIndex(l => l.Id == p.Look);
            look.Selected = li < 0 ? 0 : li + 1;
            look.ItemSelected += i =>
            {
                Draft.Remember();
                p.Look = i <= 0 ? "" : looks[(int)i - 1].Id;
                Sync3D();
            };
            look.Disabled = looks.Count == 0;
            if (!modelPiece) _right.AddChild(Row("Look", look));
            if (!modelPiece) _right.AddChild(ForgeWidgets.Wrapped($"Plain stand-in models for now, in {(Theme == TerrainLooks.ETheme.Grimdark ? "Grimdark Future" : "Age of Fantasy")} colours (the Book check game). Real looks come from the Terrain Lab.", 11, HudKit.Dim));

            _right.AddChild(Row("Width", Spin(p.W, 1, 24, v => { p.W = v; if (p.Round) p.D = v; })));
            if (!p.Round) _right.AddChild(Row("Depth", Spin(p.D, 1, 24, v => p.D = v)));
            if (!p.Round) _right.AddChild(Row("Turned", Spin(p.Angle, -180, 180, v => p.Angle = v, 5, "deg")));
            _right.AddChild(Row("Rules height", Spin(p.Height, 0, 12, v => p.Height = v, 0.5f)).Also(r => r.TooltipText = "How tall the piece counts as for the rules - it doesn't move the model. The table is flat for now, so height only matters to Climbable: over 3\" tall it counts as Impassable."));
            // Kai, 2026-11-18: sink a Warhammer model so its sculpted base goes under the ground. Saved on the MODEL (its
            // match), so every piece using it - here, on other maps, in battle - follows.
            if (modelPiece && WarhammerModels.Library?.ForLook(p.Look) is { } model)
            {
                var sink = new SpinBox { Name = "SinkModel", MinValue = -4, MaxValue = 4, Step = 0.05, Value = model.Match.Sink, Suffix = "\"",
                    TooltipText = "Above 0 sinks this model into the ground (hides a sculpted base); below 0 raises it (a model sitting too deep). Saved for the model everywhere it's used. Looks only." };
                string sinkLook = p.Look;
                sink.ValueChanged += v =>
                {
                    // Saving makes WarhammerModels re-read the match file, so the model is fetched fresh every time
                    // (holding the first copy meant only the first change ever stuck - Kai, 2026-11-18).
                    if (WarhammerModels.Library is not { } lib || lib.ForLook(sinkLook) is not { } now) return;
                    now.Match.Sink = (float)v;
                    now.Match.Checked = true;
                    try { lib.Save(); } catch (Exception ex) { Say("Couldn't save the sink: " + ex.Message, Palette.WoundsRed); }
                    Sync3D();
                };
                _right.AddChild(Row("Sink model", sink));
            }

            _right.AddChild(HudKit.Heading("Rules (from the book)", 13));
            foreach (ETerrainType flag in TerrainBook.RuleFlags)
            {
                var f = flag;
                string note = TerrainBook.NeedsHeights(f) ? "  (needs heights - later)"
                    : f == ETerrainType.Climbable && TerrainBook.Plays(f, _checkFor) ? (p.Height > TerrainBook.ClimbableMaxInches ? "  (over 3\": Impassable)" : "  (3\" or less: crossable)") : !TerrainBook.Plays(f, _checkFor) ? $"  (not in {GameSystemInfo.DisplayName(_checkFor)})" : "";
                var cb = new CheckBox { Name = "Rule_" + f, Text = TerrainBook.RuleName(f) + note, ButtonPressed = p.Rules.HasFlag(f), FocusMode = FocusModeEnum.None,
                    TooltipText = TerrainBook.RuleName(f).ToUpperInvariant() + " TERRAIN\n" + TerrainBook.RuleText(f) };
                cb.Toggled += on => { Draft.Remember(); p.Rules = on ? p.Rules | f : p.Rules & ~f; Rebuild(); };
                _right.AddChild(cb);
            }
            _right.AddChild(HudKit.Heading("Do", 13));
            GroupActions(false);
        }
        else if (_selObjective >= 0 && _selObjective < Draft.Objectives.Count)
        {
            var o = Draft.Objectives[_selObjective];
            _right.AddChild(HudKit.Heading($"Objective {_selObjective + 1}", 13));
            _right.AddChild(HudKit.Text($"At {o.X:0.#}\", {o.Z:0.#}\" - drag it to move.", 14, HudKit.Fg, HudKit.Semi));
            _right.AddChild(Ui.Button("Delete", DeleteSelected, "Remove the objective (Del).", "BackButton"));
        }
        else if (_selBase >= 0 && _selBase < Draft.Bases.Count)
        {
            MapBaseMarkerEntry sb = Draft.Bases[_selBase];
            _right.AddChild(HudKit.Heading("Size base", 13));
            _right.AddChild(HudKit.Text($"{(sb.Label.Length > 0 ? sb.Label : "Base")} at {sb.X:0.#}\", {sb.Z:0.#}\" - drag it to move.", 14, HudKit.Fg, HudKit.Semi));
            _right.AddChild(ForgeWidgets.Wrapped("Only to check a unit fits through a gap. The game ignores it.", 13, HudKit.Dim));
            _right.AddChild(Ui.Button("Delete", DeleteSelected, "Remove the size base (Del).", "BackButton").Also(b => b.Name = "DeleteBase"));
        }
        else
        {
            _right.AddChild(HudKit.Heading("Selected", 13));
            _right.AddChild(ForgeWidgets.Wrapped("Click a piece on the table to change it, or pick a piece on the left to place one.", 14, HudKit.Dim));
            _right.AddChild(ForgeWidgets.Wrapped("To change many at once: drag a box over them (or Box select), or Shift+click each one. Ctrl+A selects every piece.", 13, HudKit.Dim));
            if (_tool == ETool.Measure && _measureA is { } a && _measureB is { } b)
                _right.AddChild(HudKit.Text($"Measured: {a.DistanceTo(b):0.0}\"", 16, HudKit.Lime, HudKit.Semi).Also(l => l.Name = "MeasureReadout"));
        }
    }

    private static readonly Color LockColour = new(1f, 0.62f, 0.25f);

    /// <summary>Option A's right panel for several pieces: what they are, and what can be done to them together.</summary>
    private void GroupPanel()
    {
        int n = _picked.Count, locked = _picked.Count(p => p.Locked);
        _right.AddChild(HudKit.Heading($"{n} pieces selected", 13).Also(h => h.Name = "GroupHeading"));
        _right.AddChild(ForgeWidgets.Wrapped("Drag any of them to move them all. Shift+click adds or removes a piece; drag an empty spot to box-select.", 13, HudKit.Dim));
        if (locked > 0) _right.AddChild(ForgeWidgets.Wrapped($"{locked} of them locked - those stay put.", 12, LockColour));
        // What's in the selection, by name ("Ruins x2, Forest").
        string what = string.Join(", ", _picked.GroupBy(p => p.Name.Length > 0 ? p.Name : p.Kind).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
        _right.AddChild(ForgeWidgets.Wrapped(what, 13, HudKit.Fg));
        GroupActions(true);
    }

    /// <summary>The buttons that act on every picked piece (one piece gets the same ones, minus the group-only ones).</summary>
    private void GroupActions(bool many)
    {
        int n = _picked.Count;
        Button Wide(string name, string text, Action act, string tip, string? variation = null)
        {
            var b = Ui.Button(text, act, tip, variation);
            b.Name = name;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _right.AddChild(b);
            return b;
        }
        Wide("Rotate90", many ? "Rotate 90 together (R)" : "Rotate 90 (R)", () => RotatePicked(90), "Turn them a quarter turn about their middle, keeping how they sit to each other.");
        Wide("Duplicate", "Duplicate (Ctrl+D)", DuplicatePicked, "A copy, a little down and to the right, selected so you can drag it.");
        Wide("MirrorCopy", "Mirror copy", MirrorPicked, "A mirror-image copy on the other side of the table, so both players get the same terrain.");
        var how = new OptionButton { Name = "MirrorHow", TooltipText = "Which way the mirror copy goes." };
        // In MapDraft.EMirror's order.
        how.AddItem("left / right");
        how.AddItem("top / bottom");
        how.AddItem("corner to corner");
        how.Selected = (int)_mirror;
        how.ItemSelected += i => _mirror = (MapDraft.EMirror)(int)i;
        _right.AddChild(Row("Mirror", how));
        if (many)
        {
            Wide("LineUpLeft", "Line up: left edges", () => GroupDo(ps => Draft.LineUp(ps, true)), "Slide them sideways so their left edges line up with the leftmost one.");
            Wide("LineUpTop", "Line up: top edges", () => GroupDo(ps => Draft.LineUp(ps, false)), "Slide them up or down so their top edges line up with the highest one.");
            Wide("Spread", "Spread evenly", () => GroupDo(ps => Draft.SpreadEvenly(ps)), "Even gaps between them, between the two outermost (needs 3 or more).").Disabled = n < 3;
        }
        bool anyFree = _picked.Any(p => !p.Locked);
        Wide("Lock", anyFree ? "Lock (L)" : "Unlock (L)", ToggleLock, "A locked piece can still be selected and edited here, but not dragged, turned or resized on the table. Saved with the map.");
        _right.AddChild(HudKit.Heading("Rotate", 13));
        var turn = new HBoxContainer();
        turn.AddThemeConstantOverride("separation", 5);
        foreach (int d in new[] { -15, -5, 5, 15 })
        {
            int deg = d;
            turn.AddChild(Ui.Button(d > 0 ? $"+{d}" : d.ToString(), () => RotatePicked(deg), $"Turn {Math.Abs(d)} deg (Q / E, Shift for 5).").Also(b => { b.Name = "Turn" + (d < 0 ? "M" : "P") + Math.Abs(d); b.SizeFlagsHorizontal = SizeFlags.ExpandFill; }));
        }
        _right.AddChild(turn);
        Wide("DeletePiece", many ? $"Delete all {n}" : "Delete", DeleteSelected, "Remove them (Del). Locked pieces are kept.", "BackButton");
    }

    private void GroupDo(Action<List<MapPiece>> act)
    {
        if (_picked.All(p => p.Locked)) { Say("They're all locked - L unlocks them.", LockColour); return; }
        Draft.Remember();
        act(_picked);
        Rebuild();
    }

    private void RotatePicked(float degrees)
    {
        if (_picked.Count == 0) return;
        if (_picked.All(p => p.Locked)) { Say("Locked - L unlocks it.", LockColour); return; }
        Draft.Remember();
        Draft.RotateGroup(_picked, degrees);
        Rebuild();
    }

    private void CopyPicked()
    {
        if (_picked.Count == 0) return;
        _clipboard = _picked.Select(p => p.Clone()).ToList();
        Say(_picked.Count == 1 ? "Copied." : $"Copied {_picked.Count} pieces.");
        TopBar();
    }

    private void DuplicatePicked()
    {
        if (_picked.Count == 0) return;
        Draft.Remember();
        var copies = Draft.Duplicate(_picked);
        _picked.Clear();
        _picked.AddRange(copies);
        _selObjective = -1; _selBase = -1;
        Say(copies.Count == 1 ? "Duplicated - drag the copy where you want it." : $"Duplicated {copies.Count} pieces - drag them where you want them.");
        Rebuild();
    }

    private void MirrorPicked()
    {
        if (_picked.Count == 0) return;
        Draft.Remember();
        var copies = Draft.MirrorCopy(_picked, _mirror);
        if (copies.Count == 0) { Say("That piece is an old multi-part shape the editor can't mirror."); return; }
        _picked.Clear();
        _picked.AddRange(copies);
        Say($"{copies.Count} mirror cop{(copies.Count == 1 ? "y" : "ies")} placed on the other side - now selected.", Palette.Lime);
        Rebuild();
    }

    private void ToggleLock()
    {
        if (_picked.Count == 0) return;
        bool lockThem = _picked.Any(p => !p.Locked);
        Draft.Remember();
        foreach (MapPiece p in _picked) p.Locked = lockThem;
        Say(lockThem ? "Locked - they can't be dragged, turned or resized by accident." : "Unlocked.", lockThem ? LockColour : null);
        Rebuild();
    }

    private static Control Row(string label, Control c)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", 8);
        r.AddChild(HudKit.Text(label, 14, HudKit.Dim, HudKit.Semi).Also(l => l.CustomMinimumSize = new Vector2(64, 0)));
        c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        r.AddChild(c);
        return r;
    }

    private SpinBox Spin(float value, float min, float max, Action<float> set, float step = 0.5f, string suffix = "\"")
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix };
        s.ValueChanged += v => { Draft.Remember(); set((float)v); Sync3D(); LeftPanel(); };
        return s;
    }

    private void AskClear()
    {
        var dlg = new ConfirmationDialog { Title = "Clear the table?", DialogText = "Remove every piece, objective and size base? (Undo brings them back.)", OkButtonText = "Clear" };
        dlg.Confirmed += () => { Draft.Remember(); Draft.Pieces.Clear(); Draft.Objectives.Clear(); Draft.Bases.Clear(); _picked.Clear(); _selObjective = -1; _selBase = -1; Rebuild(); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void OpenGenerate()
    {
        var dlg = new ConfirmationDialog { Title = "Generate terrain", OkButtonText = "Generate" };
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(ForgeWidgets.Wrapped($"The {GameSystemInfo.DisplayName(_checkFor)} Advanced Rulebook's terrain generator. It replaces the terrain on this table (objectives stay); undo brings it back.", 15, HudKit.Fg));
        var themes = Enum.GetValues<TerrainBook.ETheme>();
        var pick = new OptionButton { Name = "Theme" };
        foreach (var t in themes) pick.AddItem(TerrainBook.ThemeName(t, _checkFor));
        var text = ForgeWidgets.Wrapped(TerrainBook.ThemeText(themes[0], _checkFor), 14, HudKit.Dim);
        pick.ItemSelected += i => text.Text = TerrainBook.ThemeText(themes[i], _checkFor);
        col.AddChild(pick);
        col.AddChild(text);
        // The count follows the picked theme: Firefight's Death Planets take 4D3 where its other themes take 6D3, and the
        // line used to say the first theme's number whatever was picked.
        string Takes(TerrainBook.ETheme t) => $"Takes {TerrainBook.FixedCount(_checkFor)} pieces, then {TerrainBook.ExtraDice(_checkFor, t)}D3 more rolled on 2D6, as the book says. Where they go is up to the editor: spread out, 6\" apart where the table allows.";
        var takes = ForgeWidgets.Wrapped(Takes(themes[0]), 13, HudKit.Dim);
        pick.ItemSelected += i => takes.Text = Takes(themes[i]);
        col.AddChild(takes);
        // Kai, 2026-11-14: the Warhammer sets as maps - the generated table dressed with one set's models.
        var lib = WarhammerModels.Library;
        var whSets = lib?.Sets.Where(kv => lib.Placeable.Any(e => e.Set == kv.Key && e.Measured)).OrderBy(kv => kv.Key).ToList() ?? new();
        var useSet = new OptionButton { Name = "WarhammerSet", TooltipText = "Swap each generated piece for a model from one Warhammer set, of the same kind and close in size. Kinds the set has no model for stay plain." };
        useSet.AddItem("Plain pieces");
        foreach (var kv in whSets) useSet.AddItem("Warhammer: " + kv.Value);
        if (whSets.Count > 0) col.AddChild(useSet);
        // A window is sized from its contents before they are laid out, when wrapped text is still as narrow as it can
        // be - one word a line. That made this window taller than the screen, with Generate and Cancel off the bottom.
        // Telling each paragraph its width first gives the window its real height.
        foreach (Label wrapped in col.GetChildren().OfType<Label>()) wrapped.CustomMinimumSize = new Vector2(520, 0);
        dlg.AddChild(col);
        dlg.Confirmed += () =>
        {
            Generate(themes[pick.Selected]);
            if (useSet.Selected > 0 && lib != null)
            {
                Draft.Remember();
                int n = lib.DressFromSet(Draft, whSets[useSet.Selected - 1].Key, new Random());
                Say($"{n} of {Draft.Pieces.Count} pieces use {whSets[useSet.Selected - 1].Value} models; the rest have no matched model of their kind.", Palette.Lime);
                Rebuild();
            }
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void Generate(TerrainBook.ETheme theme)
    {
        string line = Draft.Generate(_checkFor, theme, new Random());
        _sel = null;
        Say(line, Palette.Lime);
        Rebuild();
    }

    private void DeleteSelected()
    {
        if (_picked.Count > 0)
        {
            var gone = _picked.Where(p => !p.Locked).ToList();
            int kept = _picked.Count - gone.Count;
            if (gone.Count > 0) { Draft.Remember(); foreach (MapPiece p in gone) Draft.Pieces.Remove(p); }
            _picked.RemoveAll(p => !p.Locked);
            if (kept > 0) Say($"{kept} locked piece{(kept == 1 ? " was" : "s were")} kept - unlock with L to delete.", LockColour);
        }
        else if (_selObjective >= 0) { Draft.Remember(); Draft.Objectives.RemoveAt(_selObjective); _selObjective = -1; _selBase = -1; }
        else if (_selBase >= 0 && _selBase < Draft.Bases.Count) { Draft.Remember(); Draft.Bases.RemoveAt(_selBase); _selBase = -1; }
        Rebuild();
    }

    private void Paste()
    {
        if (_clipboard.Count == 0) return;
        Draft.Remember();
        // Each paste lands 3" further on, so pasting twice doesn't stack copies on top of each other.
        var copies = Draft.Duplicate(_clipboard);
        _clipboard = copies.Select(c => c.Clone()).ToList();
        _picked.Clear();
        _picked.AddRange(copies);
        _selObjective = -1; _selBase = -1;
        Rebuild();
    }

    // ================================================================== 3D

    private void BuildBoard()
    {
        foreach (Node n in _board.GetChildren()) { _board.RemoveChild(n); n.QueueFree(); }
        TableLook.AddBoard(_board, W, D, Draft.Mat, floor: TableLook.RoomFor(_checkFor) == null, horizon: HorizonId, battlefield: Horizon.On, ground: Draft.Ground);
        Weather.Add(_board, W, D, Draft.Weather);
    }

    /// <summary>The horizon the map shows: its own pick, else the style it was generated in.</summary>
    private string HorizonId => Draft.Horizon.Length > 0 ? Draft.Horizon : Draft.GeneratedFrom;

    /// <summary>Re-points the sky and haze after the horizon or table size changes.</summary>
    private void Atmosphere()
    {
        if (TableLook.RoomFor(_checkFor) != null || !Horizon.On) return;
        if (_world.GetNodeOrNull<WorldEnvironment>("Environment") is { Environment: { } env })
            Horizon.SkyAndHaze(env, FdgRaylib.Terrain.MapAtmosphere.HorizonFor(HorizonId), W, D, Draft.Sky);
    }

    private void Sync3D()
    {
        foreach (Node n in _pieces.GetChildren()) { _pieces.RemoveChild(n); n.QueueFree(); }
        foreach (Node n in _marks.GetChildren()) { _marks.RemoveChild(n); n.QueueFree(); }
        foreach (MapPiece gone in _looks.Keys.Where(k => !Draft.Pieces.Contains(k) || _rulesView).ToList())
        {
            _looks[gone].Node.QueueFree();
            _looks.Remove(gone);
        }
        foreach (MapPiece p in Draft.Pieces) DrawPiece(p);
        for (int i = 0; i < Draft.Objectives.Count; i++)
        {
            var o = Draft.Objectives[i];
            var c = new Color(0.94f, 0.76f, 0.31f);
            TableLook.Mesh(_marks, new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.25f }, TableLook.Mat(c, 0.4f, c, i == _selObjective ? 2.2f : 0.8f), World(o.X, o.Z, 0.14f));
            TableLook.Mesh(_marks, new TorusMesh { InnerRadius = 2.9f, OuterRadius = 3.0f, Rings = 48 }, TableLook.Mat(c with { A = 0.5f }), World(o.X, o.Z, 0.05f));
            var lbl = new Label3D { Text = (i + 1).ToString(), FontSize = 64, PixelSize = 0.012f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = World(o.X, o.Z, 1.4f), NoDepthTest = true };
            _marks.AddChild(lbl);
        }
        for (int i = 0; i < Draft.Bases.Count; i++)
        {
            MapBaseMarkerEntry b = Draft.Bases[i];
            var at = new Node3D { Position = World(b.X, b.Z) };
            _marks.AddChild(at);
            TableLook.AddBase(at, b.Base.DiameterInches / 2, i == _selBase ? HudKit.Lime : new Color(0.6f, 0.7f, 0.75f));
        }
        if (_grid) DrawGrid();
        if (_measureA is { } ma && _measureB is { } mb) DrawRuler(ma, mb);
        UpdateGhost(GetViewport().GetMousePosition());
    }

    /// <summary>Faint lines every 6" (the Grid 6" toggle), plus the table's middle lines a little brighter.</summary>
    private void DrawGrid()
    {
        var faint = TableLook.Mat(new Color(1, 1, 1, 0.16f), 0.8f);
        var mid = TableLook.Mat(new Color(1, 1, 1, 0.32f), 0.8f);
        for (float x = 6; x < W - 0.01f; x += 6)
            TableLook.Mesh(_marks, new BoxMesh { Size = new Vector3(0.06f, 0.01f, D) }, Mathf.Abs(x - W / 2) < 0.01f ? mid : faint, World(x, D / 2, 0.025f));
        for (float z = 6; z < D - 0.01f; z += 6)
            TableLook.Mesh(_marks, new BoxMesh { Size = new Vector3(W, 0.01f, 0.06f) }, Mathf.Abs(z - D / 2) < 0.01f ? mid : faint, World(W / 2, z, 0.025f));
    }

    /// <summary>The Measure tool's ruler: a line between the two ends, and the distance in inches above its middle.</summary>
    private void DrawRuler(Vector2 a, Vector2 b)
    {
        Color c = HudKit.Lime;
        var mat = TableLook.Mat(c, 0.4f, c, 1.4f);
        float len = a.DistanceTo(b);
        Vector3 pa = World(a.X, a.Y, 0.15f), pb = World(b.X, b.Y, 0.15f);
        TableLook.Mesh(_marks, new SphereMesh { Radius = 0.3f, Height = 0.6f }, mat, pa);
        TableLook.Mesh(_marks, new SphereMesh { Radius = 0.3f, Height = 0.6f }, mat, pb);
        if (len > 0.05f)
        {
            var line = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.06f, len) }, MaterialOverride = mat };
            _marks.AddChild(line);
            line.Position = (pa + pb) / 2;
            line.LookAt(pb, Vector3.Up);
        }
        _marks.AddChild(new Label3D { Text = $"{len:0.0}\"", FontSize = 72, PixelSize = 0.014f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = (pa + pb) / 2 + new Vector3(0, 1.3f, 0), NoDepthTest = true, Modulate = c, OutlineSize = 12 });
    }

    /// <summary>The look style the editor shows: the Book check game's (Grimdark Future games sci-fi, Age of Fantasy
    /// games fantasy) - the same piece dresses itself in each game's style on the table.</summary>
    private TerrainLooks.ETheme Theme => TerrainLooks.ThemeFor(_checkFor);

    private string KindOf(MapPiece p) => TerrainLooks.GuessKind(p.Kind, p.Name, p.Rules, p.Height, p.Area);

    /// <summary>The piece's 3D look (<see cref="TerrainDresser"/>), reused while nothing about it changed. False when
    /// the style has no look for it - the plain block is drawn. The table's size is part of "nothing changed": a look is
    /// built where the piece stands measured from the table's middle, so on a resized table an untouched piece's look
    /// stayed where the old table had it.</summary>
    private bool Dress(MapPiece p)
    {
        string sig = $"{p.X:0.###}|{p.Z:0.###}|{p.W:0.###}|{p.D:0.###}|{p.Angle:0.#}|{p.Round}|{p.Height:0.##}|{p.Kind}|{p.Look}|{p.Name}|{Theme}|{WarhammerModels.Library?.ForLook(p.Look)?.Match.Sink:0.##}|{W:0.#}x{D:0.#}";
        if (_looks.TryGetValue(p, out var have))
        {
            if (have.Sig == sig) return true;
            have.Node.QueueFree();
            _looks.Remove(p);
        }
        var polys = FdgRaylib.Rendering.ZoneOutlines.Polygons(p.ToZone()).Select(poly => poly.Select(q => new Vector2(q.X - W / 2, D / 2 - q.Z)).ToArray()).ToList();
        var node = new Node3D();
        _dressed.AddChild(node);
        if (polys.Count == 0 || !WarhammerModels.Dress(node, p.Look, polys[0]))
        {
            TerrainDresser.DressBasic(node, polys, Mathf.Max(0.15f, p.Height), KindOf(p), Theme, TerrainLooks.SeedFor(p.X, p.Z, p.Name), p.Name);
            // Built-in terrain only (Kai, 2026-11-18: never the Warhammer models). Off until he picks.
            GroundBlend.Add(node, polys, KindOf(p), TerrainLooks.SeedFor(p.X, p.Z, p.Name), MapAtmosphere.HorizonFor(HorizonId));
        }
        _looks[p] = (sig, node);
        return true;
    }

    private void DrawPiece(MapPiece p)
    {
        bool sel = _picked.Contains(p);
        Color c = BattleTable.TerrainColour(p.Rules);
        // Turned by +Angle: the table's z runs the other way to Godot's (World), so the engine's turn shows as a positive
        // turn here. It was -Angle, which drew every turned outline (and the Rules view) mirrored against the piece the
        // game actually plays - and against its model, now that the model turns the game's way (Kai, 2026-11-19).
        var root = new Node3D { Position = World(p.X, p.Z), RotationDegrees = new Vector3(0, p.Angle, 0) };
        _pieces.AddChild(root);
        if (_rulesView)
        {
            Color rc = TableLook.RulesColour(p.Rules);
            // A model piece that stops models or sight plays only on its solid parts (Kai, 2026-11-19) - show those.
            if (TerrainSolids.Applies(p) && TerrainSolids.Provider?.Invoke(p.Look) is { Count: > 0 } parts)
                foreach (var (u0, v0, u1, v1) in parts)
                {
                    float cx = (-0.5f + (u0 + u1) / 2) * p.W, cz = (0.5f - (v0 + v1) / 2) * p.D;
                    TableLook.Mesh(root, new BoxMesh { Size = new Vector3((u1 - u0) * p.W, 0.12f, (v1 - v0) * p.D) },
                        TableLook.Mat(rc with { A = 0.55f }, 0.6f, rc, 0.5f), new Vector3(cx, 0.07f, -cz));
                }
            else
            {
                Mesh flat = p.Round ? new CylinderMesh { TopRadius = p.W / 2, BottomRadius = p.W / 2, Height = 0.12f } : new BoxMesh { Size = new Vector3(p.W, 0.12f, p.D) };
                TableLook.Mesh(root, flat, TableLook.Mat(rc with { A = 0.55f }, 0.6f, rc, 0.5f), new Vector3(0, 0.07f, 0));
            }
            root.AddChild(new Label3D { Text = TableLook.RulesShort(p.Rules), FontSize = 48, PixelSize = 0.011f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.2f, 0), NoDepthTest = true, Modulate = rc.Lightened(0.5f) });
        }
        else if (Dress(p))
        {
            if (sel) root.AddChild(new Label3D { Text = p.Name, FontSize = 40, PixelSize = 0.01f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, Mathf.Max(0.15f, p.Height) + 1.2f, 0), Modulate = new Color(1, 1, 1, 0.85f) });
        }
        else
        {
            float h = Mathf.Max(0.15f, p.Height);
            Mesh m = p.Round ? new CylinderMesh { TopRadius = p.W / 2 * 0.92f, BottomRadius = p.W / 2, Height = h } : new BoxMesh { Size = new Vector3(p.W, h, p.D) };
            TableLook.Mesh(root, m, TableLook.Mat(c, 0.9f), new Vector3(0, h / 2, 0));
            root.AddChild(new Label3D { Text = p.Name, FontSize = 40, PixelSize = 0.01f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, h + 0.8f, 0), Modulate = new Color(1, 1, 1, 0.85f) });
        }
        // The exact footprint: a thin outline on the felt (lime when selected).
        // Locked pieces are always outlined in amber, so you can see which ones won't move.
        Color edge = p.Locked ? (sel ? LockColour : LockColour with { A = 0.6f }) : sel ? HudKit.Lime : new Color(1, 1, 1, 0.35f);
        if (p.Round)
            TableLook.Mesh(root, new TorusMesh { InnerRadius = p.W / 2 - 0.06f, OuterRadius = p.W / 2 + 0.06f, Rings = 64 }, TableLook.Mat(edge, 0.5f, edge, sel ? 1.5f : 0.2f), new Vector3(0, 0.03f, 0));
        else
            foreach (var (a, b) in new[] { (0, 1), (1, 2), (2, 3), (3, 0) })
            {
                var k = new[] { new Vector2(-p.W / 2, -p.D / 2), new Vector2(p.W / 2, -p.D / 2), new Vector2(p.W / 2, p.D / 2), new Vector2(-p.W / 2, p.D / 2) };
                Vector2 mid = (k[a] + k[b]) / 2;
                float len = k[a].DistanceTo(k[b]);
                bool alongX = Mathf.Abs(k[a].Y - k[b].Y) < 0.01f;
                TableLook.Mesh(root, new BoxMesh { Size = alongX ? new Vector3(len, 0.04f, 0.12f) : new Vector3(0.12f, 0.04f, len) }, TableLook.Mat(edge, 0.5f, edge, sel ? 1.5f : 0.2f), new Vector3(mid.X, 0.03f, -mid.Y));
            }
        if (p.Locked)
            root.AddChild(new Label3D { Text = "LOCKED", FontSize = 64, PixelSize = 0.025f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, Mathf.Max(0.15f, p.Height) + 0.5f, 0), NoDepthTest = true, Modulate = LockColour, OutlineSize = 10 });
        if (p == _sel && p.Raw == null && !p.Locked)
            foreach (var (x, z) in p.Corners())
                TableLook.Mesh(_marks, new SphereMesh { Radius = 0.45f, Height = 0.9f }, TableLook.Mat(HudKit.Lime, 0.3f, HudKit.Lime, 1.5f), World(x, z, 0.4f));
    }

    private void UpdateGhost(Vector2 screen)
    {
        bool show = _tool == ETool.Place && TableAt(screen) is not null && GetViewport().GuiGetHoveredControl() == this;
        _ghost.Visible = show;
        if (!show || TableAt(screen) is not { } at) return;
        float h = Mathf.Max(0.15f, _kind.Height);
        if (_model != null)
        {
            MapPiece m = WarhammerTerrainLibrary.PieceFor(_model, 0, 0);
            h = Mathf.Max(0.15f, m.Height);
            _ghost.Mesh = new BoxMesh { Size = new Vector3(m.W, h, m.D) };
        }
        else
            _ghost.Mesh = _kind.Round ? new CylinderMesh { TopRadius = _kind.Width / 2, BottomRadius = _kind.Width / 2, Height = h } : new BoxMesh { Size = new Vector3(_kind.Width, h, _kind.Depth) };
        _ghost.MaterialOverride = TableLook.Mat(HudKit.Lime with { A = 0.35f }, 0.5f, HudKit.Lime, 0.4f);
        _ghost.Position = World(Snap(at.X), Snap(at.Y), h / 2);
    }

    // ================================================================== camera

    /// <summary>For scripted screenshot checks: sets the camera's tilt (degrees) and distance (inches).</summary>
    public void SetView(float pitch, float distance) { _pitch = pitch; _distance = distance; _topView = false; ApplyCamera(); }

    private void ApplyCamera()
    {
        float pitch = _topView ? 89.5f : _pitch;
        Vector3 offset = new Vector3(0, 0, _distance).Rotated(Vector3.Right, -Mathf.DegToRad(pitch)).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        _camera.Position = _focus + offset;
        _camera.LookAt(_focus, _topView ? Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(_yaw)) : Vector3.Up);
        Horizon.FollowCamera(_world, _distance);
    }

    private void ToggleTopView()
    {
        _topView = !_topView;
        if (_topView) { _savedPitch = _pitch; _distance = Mathf.Max(_distance, Mathf.Max(W * 0.8f, D * 1.25f) * Horizon.DistanceScale); }
        else _pitch = _savedPitch;
        ApplyCamera();
    }

    private Vector2? TableAt(Vector2 screen)
    {
        Vector3 from = _camera.ProjectRayOrigin(screen), dir = _camera.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 1e-4f) return null;
        float t = -from.Y / dir.Y;
        if (t < 0) return null;
        Vector3 p = from + dir * t;
        float x = p.X + W / 2, z = D / 2 - p.Z;
        if (x < 0 || z < 0 || x > W || z > D) return null;
        return new Vector2(x, z);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsVisibleInTree()) return;   // not while another screen is open over the editor
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) && !Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) && !Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) && !Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (dir == Vector2.Zero) return;
        Vector3 right = new Vector3(1, 0, 0).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        Vector3 fwd = new Vector3(0, 0, 1).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        // Settings -> Camera: "Camera key speed" and "Invert tilt" are for every table camera, this one included.
        _focus += (right * dir.X + fwd * dir.Y) * (float)delta * _distance / Horizon.DistanceScale * 0.8f * Battle.BattleScreen.CameraKeySpeed;
        _focus.X = Mathf.Clamp(_focus.X, -W / 2 - 10, W / 2 + 10);
        _focus.Z = Mathf.Clamp(_focus.Z, -D / 2 - 10, D / 2 + 10);
        ApplyCamera();
    }

    // ================================================================== input

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouse) return;
        bool onTable = GetViewport().GuiGetHoveredControl() == this;
        if (e is InputEventMouseButton { Pressed: true } && !onTable) return;
        // A press on the table ends any typing. The name box (or a size box) otherwise kept the keyboard after the pointer
        // went back to the table: W/A/S/D, Q/E, R and Del did nothing - or typed into the map's name - until a click
        // happened to rebuild the top bar.
        if (e is InputEventMouseButton { Pressed: true } && GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox)
            GetViewport().GuiReleaseFocus();
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Max(8f * Horizon.DistanceScale, _distance * 0.9f); ApplyCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Min(260f * Horizon.DistanceScale, _distance * 1.1f); ApplyCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                if (mb.Pressed) { _dragRotate = mb.Position; _rightDown = mb.Position; }
                else
                {
                    _dragRotate = null;
                    if (mb.Position.DistanceTo(_rightDown) < 4 && _tool != ETool.Select) { _tool = ETool.Select; Say(""); Rebuild(); }
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragPan = mb.Pressed ? mb.Position : null; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed) LeftDown(mb.Position, mb.ShiftPressed);
                else if (_boxFrom != null) FinishBox(mb.Position);
                else { bool was = _dragging; _dragging = false; _resizeCorner = -1; if (was || _selObjective >= 0 || _selBase >= 0 || _picked.Count > 0) Rebuild(); }
                break;
            case InputEventMouseMotion mm when _boxFrom != null:
                UpdateBox(mm.Position);
                break;
            case InputEventMouseMotion mm when _dragRotate is { } r:
                _yaw -= (mm.Position.X - r.X) * 0.3f;
                if (!_topView) _pitch = Mathf.Clamp(_pitch + (mm.Position.Y - r.Y) * 0.25f * Battle.BattleScreen.TiltDirection, 15f, 88f);
                _dragRotate = mm.Position;
                ApplyCamera();
                break;
            case InputEventMouseMotion mm when _dragPan is { } p:
            {
                Vector2 d = (mm.Position - p) * -_distance / Horizon.DistanceScale * 0.0016f;
                _focus += new Vector3(1, 0, 0).Rotated(Vector3.Up, Mathf.DegToRad(_yaw)) * d.X + new Vector3(0, 0, 1).Rotated(Vector3.Up, Mathf.DegToRad(_yaw)) * d.Y;
                _dragPan = mm.Position;
                ApplyCamera();
                break;
            }
            case InputEventMouseMotion mm when _dragging:
                DragTo(mm.Position);
                break;
            case InputEventMouseMotion mm:
                UpdateGhost(mm.Position);
                break;
        }
        AcceptEvent();
    }

    private Vector2 _rightDown;

    private void LeftDown(Vector2 screen, bool shift)
    {
        if (_tool == ETool.BoxSelect) { StartBox(screen, shift); return; }
        if (TableAt(screen) is not { } at)
        {
            if (_tool == ETool.Select) StartBox(screen, shift);
            return;
        }
        if (_tool == ETool.Measure)
        {
            _measureA = _measureB = at;
            _dragging = true;
            Sync3D();
            return;
        }
        float x = Snap(at.X), z = Snap(at.Y);
        switch (_tool)
        {
            case ETool.Place:
                Draft.Remember();
                var p = _model != null ? WarhammerTerrainLibrary.PieceFor(_model, x, z) : MapPiece.FromKind(_kind, x, z);
                Draft.Pieces.Add(p);
                _sel = p;
                Rebuild();
                return;
            case ETool.Objective:
                Draft.Remember();
                Draft.Objectives.Add((x, z));
                _selObjective = Draft.Objectives.Count - 1;
                _selBase = -1;
                Rebuild();
                return;
            case ETool.Base:
                Draft.Remember();
                Draft.Bases.Add(new MapBaseMarkerEntry { X = x, Z = z, Label = $"{_baseSize}mm", Base = new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = _baseSize / 25.4f } });
                Rebuild();
                return;
        }
        // Select: a corner handle of the selected piece first, then an objective, then a piece, else a box select.
        if (!shift && _sel is { Raw: null, Locked: false } s)
        {
            var corners = s.Corners();
            for (int i = 0; i < corners.Length; i++)
                if (new Vector2(corners[i].X, corners[i].Z).DistanceTo(at) < 1.2f)
                {
                    Draft.Remember();
                    _resizeCorner = i;
                    _dragging = true;
                    return;
                }
        }
        int obj = Draft.ObjectiveAt(at.X, at.Y);
        if (obj >= 0)
        {
            _sel = null; _selObjective = obj; _selBase = -1;
            Draft.Remember();
            _dragging = true;
            _dragOffset = Vector2.Zero;
            Rebuild();
            return;
        }
        int sizeBase = Draft.BaseAt(at.X, at.Y);
        if (sizeBase >= 0)
        {
            _sel = null; _selObjective = -1; _selBase = sizeBase;
            Draft.Remember();
            _dragging = true;
            _dragOffset = Vector2.Zero;
            Rebuild();
            return;
        }
        _selObjective = -1; _selBase = -1;
        MapPiece? hit = Draft.PieceAt(at.X, at.Y);
        if (hit == null)
        {
            if (!shift) _picked.Clear();
            Rebuild();
            StartBox(screen, shift);
            return;
        }
        if (shift)
        {
            if (!_picked.Remove(hit)) _picked.Add(hit);
            Rebuild();
            return;
        }
        if (!_picked.Contains(hit)) _sel = hit;
        if (_picked.Any(p => !p.Locked))
        {
            Draft.Remember();
            _dragging = true;
            _dragStart = at;
            _dragOffset = new Vector2(hit.X, hit.Z) - at;
            _dragFrom = _picked.Select(p => (p, p.X, p.Z)).ToList();
        }
        else Say("Locked - it can't be dragged. L unlocks it.", LockColour);
        Rebuild();
    }

    // ------------------------------------------------------------------ box select

    private void StartBox(Vector2 screen, bool add)
    {
        _boxFrom = screen;
        _boxAdd = add;
        UpdateBox(screen);
        _boxRect.Visible = true;
    }

    private void UpdateBox(Vector2 screen)
    {
        if (_boxFrom is not { } a) return;
        var r = new Rect2(a, Vector2.Zero).Expand(screen);
        _boxRect.Position = r.Position;
        _boxRect.Size = r.Size;
    }

    /// <summary>Selects every piece whose middle is inside the dragged box, as seen from the camera.</summary>
    private void FinishBox(Vector2 screen)
    {
        if (_boxFrom is not { } a) return;
        _boxFrom = null;
        _boxRect.Visible = false;
        var r = new Rect2(a, Vector2.Zero).Expand(screen);
        if (r.Size.X < 4 && r.Size.Y < 4) { Rebuild(); return; }
        if (!_boxAdd) _picked.Clear();
        _selObjective = -1; _selBase = -1;
        foreach (MapPiece p in Draft.Pieces)
        {
            Vector3 w = World(p.X, p.Z);
            if (_camera.IsPositionBehind(w) || !r.HasPoint(_camera.UnprojectPosition(w)) || _picked.Contains(p)) continue;
            _picked.Add(p);
        }
        Say(_picked.Count == 0 ? "Nothing in the box." : _picked.Count == 1 ? "1 piece selected." : $"{_picked.Count} pieces selected.");
        Rebuild();
    }

    private void DragTo(Vector2 screen)
    {
        if (TableAt(screen) is not { } at) return;
        if (_selObjective >= 0 && _selObjective < Draft.Objectives.Count)
        {
            Draft.Objectives[_selObjective] = (Snap(at.X), Snap(at.Y));
            Sync3D();
            return;
        }
        if (_selBase >= 0 && _selBase < Draft.Bases.Count)
        {
            Draft.Bases[_selBase].X = Snap(at.X);
            Draft.Bases[_selBase].Z = Snap(at.Y);
            Sync3D();
            return;
        }
        if (_tool == ETool.Measure)
        {
            _measureB = at;
            Say($"{_measureA!.Value.DistanceTo(at):0.0}\" - drag again to measure something else. Right-click to stop.", HudKit.Lime);
            Sync3D();
            return;
        }
        if (_resizeCorner >= 0 && _sel is { } p)
        {
            // The corner follows the pointer; the opposite corner stays put.
            float a = p.Angle * Mathf.Pi / 180f, c = Mathf.Cos(a), s = Mathf.Sin(a);
            var opp = p.Corners()[(_resizeCorner + 2) % 4];
            float dx = at.X - opp.X, dz = at.Y - opp.Z;
            float lx = dx * c + dz * s, lz = -dx * s + dz * c;
            float w = Mathf.Max(1, Snap(Mathf.Abs(lx))), d = Mathf.Max(1, Snap(Mathf.Abs(lz)));
            if (p.Round) { w = d = Mathf.Max(w, d); }
            float mx = Mathf.Sign(lx == 0 ? 1 : lx) * w / 2, mz = Mathf.Sign(lz == 0 ? 1 : lz) * d / 2;
            p.W = w; p.D = d;
            p.X = opp.X + mx * c - mz * s;
            p.Z = opp.Z + mx * s + mz * c;
        }
        else if (_dragFrom.Count == 1)
        {
            // One piece: its middle lands on the snap.
            var (one, _, _) = _dragFrom[0];
            if (one.Locked) return;
            one.X = Snap(at.X + _dragOffset.X);
            one.Z = Snap(at.Y + _dragOffset.Y);
            Draft.Clamp(one);
        }
        else
        {
            // Several: they all move by the same snapped step, so they keep how they sit to each other.
            float dx = Snap(at.X - _dragStart.X), dz = Snap(at.Y - _dragStart.Y);
            foreach (var (q, x0, z0) in _dragFrom)
            {
                if (q.Locked) continue;
                q.X = x0 + dx; q.Z = z0 + dz;
                Draft.Clamp(q);
            }
        }
        Sync3D();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsVisibleInTree()) return;   // keys are for the screen that is showing
        if (e is not InputEventKey { Pressed: true } k || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
        bool handled = true;
        switch (k.Keycode)
        {
            case Key.Z when k.CtrlPressed: if (Draft.Undo()) AfterUndo(); break;
            case Key.Y when k.CtrlPressed: if (Draft.Redo()) AfterUndo(); break;
            case Key.C when k.CtrlPressed: CopyPicked(); break;
            case Key.V when k.CtrlPressed: Paste(); break;
            case Key.D when k.CtrlPressed: if (!k.Echo) DuplicatePicked(); break;
            case Key.A when k.CtrlPressed: _picked.Clear(); _picked.AddRange(Draft.Pieces); _selObjective = -1; _selBase = -1; _tool = ETool.Select; Say($"All {_picked.Count} pieces selected."); Rebuild(); break;
            case Key.S when k.CtrlPressed: SaveMap(); break;
            case Key.Delete or Key.Backspace: DeleteSelected(); break;
            case Key.Escape when _picked.Count > 0 || _measureA != null: _picked.Clear(); _measureA = _measureB = null; Rebuild(); break;
            case Key.R when !k.Echo && _picked.Count > 0: RotatePicked(90); break;
            case Key.L when !k.Echo && _picked.Count > 0: ToggleLock(); break;
            case Key.M when !k.Echo: SetTool(_tool == ETool.Measure ? ETool.Select : ETool.Measure); break;
            case Key.Q or Key.E when _picked.Count > 1 || _sel is { Round: false, Raw: null }:
                if (_picked.All(p => p.Locked)) { Say("Locked - L unlocks it.", LockColour); break; }
                if (!k.Echo) Draft.Remember();
                Draft.RotateGroup(_picked, (k.Keycode == Key.Q ? 1 : -1) * (k.ShiftPressed ? 5 : 15));
                Rebuild();
                break;
            case Key.Q: _yaw += 5 * Battle.BattleScreen.CameraKeySpeed; ApplyCamera(); break;
            case Key.E: _yaw -= 5 * Battle.BattleScreen.CameraKeySpeed; ApplyCamera(); break;
            case Key.T when !k.Echo: ToggleTopView(); break;
            default: handled = false; break;
        }
        if (handled) GetViewport().SetInputAsHandled();
    }

    // ================================================================== screenshot driver

    /// <summary>What the editor holds right now, for scripted checks.</summary>
    public string State =>
        FormattableString.Invariant($"pieces={Draft.Pieces.Count} picked={_picked.Count} objectives={Draft.Objectives.Count} bases={Draft.Bases.Count} tool={_tool} dirty={Draft.Dirty} undo={Draft.CanUndo} redo={Draft.CanRedo} table={W:0}x{D:0} yaw={_yaw:0.#} pitch={_pitch:0.#} focus={_focus.X:0.#};{_focus.Z:0.#}")
        + (_sel is { } s ? FormattableString.Invariant($" sel={s.Name}@{s.X:0.##};{s.Z:0.##} size={s.W:0.##}x{s.D:0.##} turned={s.Angle:0.#} locked={s.Locked}") : "")
        + (_selObjective >= 0 && _selObjective < Draft.Objectives.Count ? FormattableString.Invariant($" objective={Draft.Objectives[_selObjective].X:0.##};{Draft.Objectives[_selObjective].Z:0.##}") : "")
        + (_selBase >= 0 && _selBase < Draft.Bases.Count ? FormattableString.Invariant($" sizebase={Draft.Bases[_selBase].X:0.##};{Draft.Bases[_selBase].Z:0.##}") : "");

    /// <summary>Real mouse events: press at <paramref name="from"/>, move to <paramref name="to"/> in steps, release.</summary>
    private static void DriveMouse(MouseButton button, Vector2 from, Vector2 to, bool shift)
    {
        MouseButtonMask mask = button == MouseButton.Right ? MouseButtonMask.Right : MouseButtonMask.Left;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = from, GlobalPosition = from, ShiftPressed = shift, ButtonMask = mask });
        if (from != to)
            for (int i = 1; i <= 4; i++)
            {
                Vector2 p = from.Lerp(to, i / 4f);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = mask, ShiftPressed = shift });
            }
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = to, GlobalPosition = to, ShiftPressed = shift });
    }

    /// <summary>
    /// "place:Kind@x;z", "obj:x;z" (";" because the press list splits on commas), "select:i", "generate:Theme", "rules", "top", "turn:deg", "check:System",
    /// "name:X", "save", "size:i".
    /// </summary>
    public void Drive(string cmd)
    {
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        static (float, float) XZ(string s) { var p = s.Split(',', ';', 'x'); return (float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)); }
        switch (verb)
        {
            case "place" when arg.Split('@') is { Length: 2 } a && TerrainBook.Kind(a[0]) is { } k:
            {
                var (x, z) = XZ(a[1]);
                Draft.Remember();
                var placed = MapPiece.FromKind(k, x, z);
                Draft.Pieces.Add(placed);
                _sel = placed;
                break;
            }
            // "model:<id>@x;z" - a Warhammer model piece (the id from the terrain list).
            case "model" when arg.Split('@') is { Length: 2 } m && int.TryParse(m[0], out int mid) && WarhammerModels.Library?.ById(mid) is { } entry:
            {
                var (x, z) = XZ(m[1]);
                Draft.Remember();
                var placed = WarhammerTerrainLibrary.PieceFor(entry, x, z);
                Draft.Pieces.Add(placed);
                _sel = placed;
                break;
            }
            case "obj": { var (x, z) = XZ(arg); Draft.Objectives.Add((x, z)); break; }
            case "select" when int.TryParse(arg, out int i) && i < Draft.Pieces.Count: _sel = Draft.Pieces[i]; break;
            case "generate" when Enum.TryParse(arg, out TerrainBook.ETheme t): Generate(t); return;
            // "mat:<number>" puts the mod's mat with that number on the table (0 = plain felt); "mats" opens the picker.
            case "mat" when int.TryParse(arg, out int mn):
                Draft.Mat = mn <= 0 ? "" : WarhammerTerrain.LoadMats()?.FirstOrDefault(m => m.Number == mn)?.Look ?? "";
                BuildBoard();
                break;
            case "mats": OpenMatPicker(); return;
            case "rules": _rulesView = !_rulesView; break;
            case "top": ToggleTopView(); break;
            case "turn" when _sel is { } ts && float.TryParse(arg, out float deg): ts.Angle = deg; break;
            // "pick:0;2;3" selects several pieces by number; "measure:x;z;x;z" lays the ruler; "grid"; "snap:0.5";
            // "lock", "rotate:90", "mirror:0|1|2", "dup", "lineup", "spread" act on the picked pieces.
            case "pick":
                _picked.Clear();
                foreach (string t in arg.Split(';')) if (int.TryParse(t, out int pi) && pi < Draft.Pieces.Count) _picked.Add(Draft.Pieces[pi]);
                break;
            case "measure":
            {
                var m = arg.Split(';').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                _tool = ETool.Measure; _picked.Clear();
                _measureA = new Vector2(m[0], m[1]); _measureB = new Vector2(m[2], m[3]);
                break;
            }
            case "grid": _grid = !_grid; break;
            case "snap" when float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float st): _snapStep = st; break;
            case "lock": ToggleLock(); return;
            case "rotate" when float.TryParse(arg, out float rd): RotatePicked(rd); return;
            case "mirror" when int.TryParse(arg, out int mi): _mirror = (MapDraft.EMirror)mi; MirrorPicked(); return;
            case "dup": DuplicatePicked(); return;
            case "lineup": GroupDo(ps => Draft.LineUp(ps, true)); return;
            case "spread": GroupDo(ps => Draft.SpreadEvenly(ps)); return;
            case "check" when Enum.TryParse(arg, out EGameSystem sys): _checkFor = sys; break;
            case "name": Draft.Name = arg; break;
            case "save": SaveMap(); return;
            // "click:x;z" / "shiftclick:x;z" press and release the real left button on that table point, "drag:x;z>x2;z2"
            // drags between two points, "rdrag:dx;dy" right-drags the pointer by that many pixels (the camera), and
            // "state" prints what the editor holds - so a scripted run can use the mouse the way a player does.
            case "click" or "shiftclick":
            {
                var (x, z) = XZ(arg);
                Vector2 at = _camera.UnprojectPosition(World(x, z));
                DriveMouse(MouseButton.Left, at, at, verb == "shiftclick");
                return;
            }
            case "drag" when arg.Split('>') is { Length: 2 } ends:
            {
                var (x, z) = XZ(ends[0]);
                var (x2, z2) = XZ(ends[1]);
                DriveMouse(MouseButton.Left, _camera.UnprojectPosition(World(x, z)), _camera.UnprojectPosition(World(x2, z2)), false);
                return;
            }
            case "rdrag":
            {
                var (dx, dy) = XZ(arg);
                Vector2 from = _camera.UnprojectPosition(World(W / 2, D / 2));
                DriveMouse(MouseButton.Right, from, from + new Vector2(dx, dy), false);
                return;
            }
            case "state": GD.Print("MapEditor: " + State); return;
            case "size" when int.TryParse(arg, out int si) && si < TableSizing.Sizes.Count():
                var size = TableSizing.Sizes.ElementAt(si);
                SetTableSize(size.W, size.H);
                return;
        }
        Rebuild();
    }
}
