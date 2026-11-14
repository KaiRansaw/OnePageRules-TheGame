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
public partial class MapEditorScreen : Control, IBackGuard, IOwnBackdrop
{
    public MapDraft Draft { get; set; } = MapDraft.New(72, 48);

    private enum ETool { Select, Place, Objective, Base }

    private ETool _tool = ETool.Select;
    private TerrainBook.PieceKind _kind = TerrainBook.Kinds[1];
    private int _baseSize = 32;
    private MapPiece? _sel;
    private int _selObjective = -1;
    private MapPiece? _clipboard;
    private bool _rulesView, _snap = true, _topView;
    private EGameSystem _checkFor;

    private Node3D _world = null!, _board = null!, _pieces = null!, _marks = null!;
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
    private HBoxContainer _top = null!;
    private Label _status = null!;

    private float W => Draft.TableW;
    private float D => Draft.TableH;
    private Vector3 World(float x, float z, float y = 0) => new(x - W / 2, y, D / 2 - z);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // The menu's own 3D table is taken away while this one is up (its camera and light would fight ours).
        MenuBackdrop.Instance?.SetActive(false);
        _checkFor = DefaultGame();
        _world = new Node3D { Name = "MapWorld" };
        AddChild(_world);
        TableLook.AddLighting(_world);
        _board = new Node3D();
        _pieces = new Node3D();
        _marks = new Node3D();
        _world.AddChild(_board);
        _world.AddChild(_pieces);
        _world.AddChild(_marks);
        _camera = new Camera3D { Fov = 44, Current = true, Far = 2000 };
        _world.AddChild(_camera);
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
        _left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ls.AddChild(_left);
        var leftPanel = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(ls, 10, 8, 10, 8));
        leftPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        leftPanel.OffsetLeft = 16; leftPanel.OffsetTop = 92; leftPanel.OffsetBottom = -48; leftPanel.OffsetRight = 16 + 290;
        hud.AddChild(leftPanel);

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
        var keys = HudKit.Text("Click to place - drag to move - Q / E turn (Shift: 5) - corner handles resize - Del removes - Ctrl+Z undo - right-drag turns the camera - T top view", 12, HudKit.Dim, HudKit.Semi);
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
        LeftPanel();
        RightPanel();
        Sync3D();
    }

    private static void Clear(Container c) { foreach (Node n in c.GetChildren()) { c.RemoveChild(n); n.QueueFree(); } }

    private void TopBar()
    {
        Clear(_top);
        _top.AddChild(Ui.Button("Back", App.I.Back, "Back to the map list (asks if there are unsaved changes).", "BackButton").Also(b => { b.Name = "Back"; b.CustomMinimumSize = new Vector2(100, 40); }));
        _top.AddChild(HudKit.Text("MAP MAKER", 22, HudKit.Fg, HudKit.Exo(800)).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        _name = new LineEdit { Name = "MapName", Text = Draft.Name, CustomMinimumSize = new Vector2(240, 0), MaxLength = 48, PlaceholderText = "map name", TooltipText = "The map's name (plain letters)." };
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
            Draft.Remember();
            Draft.TableW = sizes[(int)i].W; Draft.TableH = sizes[(int)i].H;
            foreach (MapPiece p in Draft.Pieces) Draft.Clamp(p);
            _checkFor = DefaultGame();
            BuildBoard(); ApplyCamera(); Rebuild();
        };
        _top.AddChild(size);
        _top.AddChild(Ui.Grow());
        var rv = new Button { Name = "RulesView", Text = "Rules view", ToggleMode = true, ButtonPressed = _rulesView, FocusMode = FocusModeEnum.None,
            TooltipText = "Hide the scenery and colour every piece by what it does." };
        rv.Toggled += on => { _rulesView = on; Sync3D(); };
        _top.AddChild(rv);
        _top.AddChild(Ui.Button("Generate...", OpenGenerate, TerrainBook.HasGenerator(_checkFor)
            ? "The Advanced Rulebook's terrain generator: rolls a themed table, then you edit it."
            : $"{GameSystemInfo.DisplayName(_checkFor)}'s book has no terrain generator - pick a game that has one under Book check.").Also(b => { b.Name = "Generate"; b.Disabled = !TerrainBook.HasGenerator(_checkFor); }));
        _top.AddChild(Ui.Button("Undo", () => { if (Draft.Undo()) AfterUndo(); }, "Undo (Ctrl+Z).").Also(b => b.Disabled = !Draft.CanUndo));
        _top.AddChild(Ui.Button("Redo", () => { if (Draft.Redo()) AfterUndo(); }, "Redo (Ctrl+Y).").Also(b => b.Disabled = !Draft.CanRedo));
        _top.AddChild(Ui.Button("SAVE MAP", SaveMap, Draft.SaveProblem() ?? "Save the map to your Maps folder.", "CommitButton").Also(b => { b.Name = "SaveMap"; b.CustomMinimumSize = new Vector2(150, 40); }));
    }

    private void AfterUndo()
    {
        _sel = null; _selObjective = -1;
        BuildBoard(); Rebuild();
    }

    private void LeftPanel()
    {
        Clear(_left);
        _left.AddChild(HudKit.Heading("Pieces (the book's)", 13));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 5);
        grid.AddThemeConstantOverride("v_separation", 5);
        foreach (TerrainBook.PieceKind k in TerrainBook.Kinds)
        {
            var kind = k;
            bool on = _tool == ETool.Place && _kind == k;
            var b = new Button { Name = "Kind_" + k.Id, Text = k.Name, ToggleMode = true, ButtonPressed = on, FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(126, 34), TooltipText = $"{k.BookLine}\n(Advanced Rulebook 3.6.0)\nClick it, then click the table. Right-click stops placing." };
            b.AddThemeColorOverride("font_color", BattleTable.TerrainColour(k.Rules).Lightened(0.45f));
            b.Pressed += () => { _tool = ETool.Place; _kind = kind; _sel = null; _selObjective = -1; Say($"Placing {kind.Name} - click the table. Right-click to stop."); Rebuild(); };
            grid.AddChild(b);
        }
        _left.AddChild(grid);
        _left.AddChild(HudKit.Heading("Also", 13));
        var also = new HBoxContainer();
        also.AddThemeConstantOverride("separation", 5);
        also.AddChild(new Button { Name = "ToolObjective", Text = "Objective", ToggleMode = true, ButtonPressed = _tool == ETool.Objective, FocusMode = FocusModeEnum.None,
            TooltipText = "Fixed objective markers. With none, the game rolls D3+2 and places them the usual way." }
            .Also(b => b.Pressed += () => { _tool = ETool.Objective; _sel = null; Say("Click the table to place objective markers. Right-click to stop."); Rebuild(); }));
        also.AddChild(new Button { Name = "ToolBase", Text = "Size base", ToggleMode = true, ButtonPressed = _tool == ETool.Base, FocusMode = FocusModeEnum.None,
            TooltipText = "A model base, only to check a unit fits through a gap. The game ignores these." }
            .Also(b => b.Pressed += () => { _tool = ETool.Base; _sel = null; Say("Click the table to drop a size base. Right-click to stop."); Rebuild(); }));
        var bs = new OptionButton { TooltipText = "Base size for the next size base." };
        int[] sizes = { 25, 32, 40, 50, 60 };
        foreach (int s in sizes) bs.AddItem($"{s}mm");
        bs.Selected = Array.IndexOf(sizes, _baseSize);
        bs.ItemSelected += i => _baseSize = sizes[i];
        also.AddChild(bs);
        _left.AddChild(also);
        _left.AddChild(HudKit.Text($"{Draft.Pieces.Count} pieces - {Draft.Objectives.Count} objectives - {Draft.Bases.Count} size bases", 13, HudKit.Dim, HudKit.Semi));
        if (Draft.Objectives.Count == 0) _left.AddChild(ForgeWidgets.Wrapped("No objectives: the game rolls D3+2 and places them the usual way.", 12, HudKit.Dim));
        _left.AddChild(Ui.Button("Clear the table", AskClear, "Remove every piece, objective and size base (undo brings them back).", "BackButton"));

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

    private void RightPanel()
    {
        Clear(_right);
        if (_sel is { } p)
        {
            _right.AddChild(HudKit.Heading("Selected piece", 13));
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
                p.Kind = k.Id; p.Rules = k.Rules; p.Height = k.Height; p.Look = "";
                if (p.Name.Length == 0 || TerrainBook.Kinds.Any(o => o.Name == p.Name)) p.Name = k.Name;
                Rebuild();
            };
            _right.AddChild(Row("Kind", kind));

            var look = new OptionButton { Name = "PieceLook", TooltipText = "The 3D model it shows as. The rules don't change." };
            look.AddItem("Plain block");
            look.Disabled = true;
            _right.AddChild(Row("Look", look));
            _right.AddChild(ForgeWidgets.Wrapped("No terrain models downloaded yet - every piece shows as a block. Models come later from the Asset Lab.", 11, HudKit.Dim));

            _right.AddChild(Row("Width", Spin(p.W, 1, 24, v => { p.W = v; if (p.Round) p.D = v; })));
            if (!p.Round) _right.AddChild(Row("Depth", Spin(p.D, 1, 24, v => p.D = v)));
            if (!p.Round) _right.AddChild(Row("Turned", Spin(p.Angle, -180, 180, v => p.Angle = v, 5, "deg")));
            _right.AddChild(Row("Height", Spin(p.Height, 0, 12, v => p.Height = v, 0.5f)).Also(r => r.TooltipText = "How tall it is drawn. The table is flat for now, so height has no rules effect yet."));

            _right.AddChild(HudKit.Heading("Rules (from the book)", 13));
            foreach (ETerrainType flag in TerrainBook.RuleFlags)
            {
                var f = flag;
                string note = TerrainBook.NeedsHeights(f) ? "  (needs heights - later)" : !TerrainBook.Plays(f, _checkFor) ? $"  (not in {GameSystemInfo.DisplayName(_checkFor)})" : "";
                var cb = new CheckBox { Name = "Rule_" + f, Text = TerrainBook.RuleName(f) + note, ButtonPressed = p.Rules.HasFlag(f), FocusMode = FocusModeEnum.None,
                    TooltipText = TerrainBook.RuleName(f).ToUpperInvariant() + " TERRAIN\n" + TerrainBook.RuleText(f) };
                cb.Toggled += on => { Draft.Remember(); p.Rules = on ? p.Rules | f : p.Rules & ~f; Rebuild(); };
                _right.AddChild(cb);
            }
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(Ui.Button("Copy", () => { _clipboard = p.Clone(); Paste(); }, "Copy the piece (Ctrl+C, Ctrl+V)."));
            row.AddChild(Ui.Button("Delete", DeleteSelected, "Remove the piece (Del).", "BackButton").Also(b => b.Name = "DeletePiece"));
            _right.AddChild(row);
        }
        else if (_selObjective >= 0 && _selObjective < Draft.Objectives.Count)
        {
            var o = Draft.Objectives[_selObjective];
            _right.AddChild(HudKit.Heading($"Objective {_selObjective + 1}", 13));
            _right.AddChild(HudKit.Text($"At {o.X:0.#}\", {o.Z:0.#}\" - drag it to move.", 14, HudKit.Fg, HudKit.Semi));
            _right.AddChild(Ui.Button("Delete", DeleteSelected, "Remove the objective (Del).", "BackButton"));
        }
        else
        {
            _right.AddChild(HudKit.Heading("Selected", 13));
            _right.AddChild(ForgeWidgets.Wrapped("Click a piece on the table to change it, or pick a piece on the left to place one.", 14, HudKit.Dim));
            var snap = new CheckBox { Name = "Snap", Text = "Snap to 1\"", ButtonPressed = _snap, FocusMode = FocusModeEnum.None, TooltipText = "Positions and sizes land on whole inches." };
            snap.Toggled += on => _snap = on;
            _right.AddChild(snap);
        }
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
        dlg.Confirmed += () => { Draft.Remember(); Draft.Pieces.Clear(); Draft.Objectives.Clear(); Draft.Bases.Clear(); _sel = null; _selObjective = -1; Rebuild(); dlg.QueueFree(); };
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
        col.AddChild(ForgeWidgets.Wrapped($"Takes {TerrainBook.FixedCount(_checkFor)} pieces, then {TerrainBook.ExtraDice(_checkFor, themes[0])}D3 more rolled on 2D6, as the book says. Where they go is up to the editor: spread out, 6\" apart where the table allows.", 13, HudKit.Dim));
        dlg.AddChild(col);
        dlg.Confirmed += () => { Generate(themes[pick.Selected]); dlg.QueueFree(); };
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
        if (_sel is { } p) { Draft.Remember(); Draft.Pieces.Remove(p); _sel = null; }
        else if (_selObjective >= 0) { Draft.Remember(); Draft.Objectives.RemoveAt(_selObjective); _selObjective = -1; }
        Rebuild();
    }

    private void Paste()
    {
        if (_clipboard == null) return;
        Draft.Remember();
        var p = _clipboard.Clone();
        p.X += 3; p.Z += 3;
        Draft.Clamp(p);
        Draft.Pieces.Add(p);
        _sel = p;
        _clipboard = p.Clone();
        Rebuild();
    }

    // ================================================================== 3D

    private void BuildBoard()
    {
        foreach (Node n in _board.GetChildren()) { _board.RemoveChild(n); n.QueueFree(); }
        TableLook.AddBoard(_board, W, D);
    }

    private void Sync3D()
    {
        foreach (Node n in _pieces.GetChildren()) { _pieces.RemoveChild(n); n.QueueFree(); }
        foreach (Node n in _marks.GetChildren()) { _marks.RemoveChild(n); n.QueueFree(); }
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
        foreach (MapBaseMarkerEntry b in Draft.Bases)
        {
            var at = new Node3D { Position = World(b.X, b.Z) };
            _marks.AddChild(at);
            TableLook.AddBase(at, b.Base.DiameterInches / 2, new Color(0.6f, 0.7f, 0.75f));
        }
        UpdateGhost(GetViewport().GetMousePosition());
    }

    private void DrawPiece(MapPiece p)
    {
        bool sel = p == _sel;
        Color c = BattleTable.TerrainColour(p.Rules);
        var root = new Node3D { Position = World(p.X, p.Z), RotationDegrees = new Vector3(0, -p.Angle, 0) };
        _pieces.AddChild(root);
        if (_rulesView)
        {
            Color rc = TableLook.RulesColour(p.Rules);
            Mesh flat = p.Round ? new CylinderMesh { TopRadius = p.W / 2, BottomRadius = p.W / 2, Height = 0.12f } : new BoxMesh { Size = new Vector3(p.W, 0.12f, p.D) };
            TableLook.Mesh(root, flat, TableLook.Mat(rc with { A = 0.55f }, 0.6f, rc, 0.5f), new Vector3(0, 0.07f, 0));
            root.AddChild(new Label3D { Text = TableLook.RulesShort(p.Rules), FontSize = 48, PixelSize = 0.011f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.2f, 0), NoDepthTest = true, Modulate = rc.Lightened(0.5f) });
        }
        else
        {
            float h = Mathf.Max(0.15f, p.Height);
            Mesh m = p.Round ? new CylinderMesh { TopRadius = p.W / 2 * 0.92f, BottomRadius = p.W / 2, Height = h } : new BoxMesh { Size = new Vector3(p.W, h, p.D) };
            TableLook.Mesh(root, m, TableLook.Mat(c, 0.9f), new Vector3(0, h / 2, 0));
            root.AddChild(new Label3D { Text = p.Name, FontSize = 40, PixelSize = 0.01f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, h + 0.8f, 0), Modulate = new Color(1, 1, 1, 0.85f) });
        }
        // The exact footprint: a thin outline on the felt (lime when selected).
        Color edge = sel ? HudKit.Lime : new Color(1, 1, 1, 0.35f);
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
        if (sel && p.Raw == null)
            foreach (var (x, z) in p.Corners())
                TableLook.Mesh(_marks, new SphereMesh { Radius = 0.45f, Height = 0.9f }, TableLook.Mat(HudKit.Lime, 0.3f, HudKit.Lime, 1.5f), World(x, z, 0.4f));
    }

    private void UpdateGhost(Vector2 screen)
    {
        bool show = _tool == ETool.Place && TableAt(screen) is not null && GetViewport().GuiGetHoveredControl() == this;
        _ghost.Visible = show;
        if (!show || TableAt(screen) is not { } at) return;
        float h = Mathf.Max(0.15f, _kind.Height);
        _ghost.Mesh = _kind.Round ? new CylinderMesh { TopRadius = _kind.Width / 2, BottomRadius = _kind.Width / 2, Height = h } : new BoxMesh { Size = new Vector3(_kind.Width, h, _kind.Depth) };
        _ghost.MaterialOverride = TableLook.Mat(HudKit.Lime with { A = 0.35f }, 0.5f, HudKit.Lime, 0.4f);
        _ghost.Position = World(MapDraft.SnapInch(at.X, _snap), MapDraft.SnapInch(at.Y, _snap), h / 2);
    }

    // ================================================================== camera

    private void ApplyCamera()
    {
        float pitch = _topView ? 89.5f : _pitch;
        Vector3 offset = new Vector3(0, 0, _distance).Rotated(Vector3.Right, -Mathf.DegToRad(pitch)).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        _camera.Position = _focus + offset;
        _camera.LookAt(_focus, _topView ? Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(_yaw)) : Vector3.Up);
    }

    private void ToggleTopView()
    {
        _topView = !_topView;
        if (_topView) { _savedPitch = _pitch; _distance = Mathf.Max(_distance, Mathf.Max(W * 0.8f, D * 1.25f)); }
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
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) && !Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (dir == Vector2.Zero) return;
        Vector3 right = new Vector3(1, 0, 0).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        Vector3 fwd = new Vector3(0, 0, 1).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        _focus += (right * dir.X + fwd * dir.Y) * (float)delta * _distance * 0.8f;
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
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Max(8f, _distance * 0.9f); ApplyCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Min(260f, _distance * 1.1f); ApplyCamera(); break;
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
                if (mb.Pressed) LeftDown(mb.Position);
                else { _dragging = false; _resizeCorner = -1; if (_selObjective >= 0 || _sel != null) Rebuild(); }
                break;
            case InputEventMouseMotion mm when _dragRotate is { } r:
                _yaw -= (mm.Position.X - r.X) * 0.3f;
                if (!_topView) _pitch = Mathf.Clamp(_pitch + (mm.Position.Y - r.Y) * 0.25f, 15f, 88f);
                _dragRotate = mm.Position;
                ApplyCamera();
                break;
            case InputEventMouseMotion mm when _dragPan is { } p:
            {
                Vector2 d = (mm.Position - p) * -_distance * 0.0016f;
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

    private void LeftDown(Vector2 screen)
    {
        if (TableAt(screen) is not { } at) return;
        float x = MapDraft.SnapInch(at.X, _snap), z = MapDraft.SnapInch(at.Y, _snap);
        switch (_tool)
        {
            case ETool.Place:
                Draft.Remember();
                var p = MapPiece.FromKind(_kind, x, z);
                Draft.Pieces.Add(p);
                _sel = p;
                Rebuild();
                return;
            case ETool.Objective:
                Draft.Remember();
                Draft.Objectives.Add((x, z));
                _selObjective = Draft.Objectives.Count - 1;
                Rebuild();
                return;
            case ETool.Base:
                Draft.Remember();
                Draft.Bases.Add(new MapBaseMarkerEntry { X = x, Z = z, Label = $"{_baseSize}mm", Base = new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = _baseSize / 25.4f } });
                Rebuild();
                return;
        }
        // Select: a corner handle of the selected piece first, then an objective, then a piece.
        if (_sel is { Raw: null } s)
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
            _sel = null; _selObjective = obj;
            Draft.Remember();
            _dragging = true;
            _dragOffset = Vector2.Zero;
            Rebuild();
            return;
        }
        _selObjective = -1;
        _sel = Draft.PieceAt(at.X, at.Y);
        if (_sel != null)
        {
            Draft.Remember();
            _dragging = true;
            _dragOffset = new Vector2(_sel.X, _sel.Z) - at;
        }
        Rebuild();
    }

    private void DragTo(Vector2 screen)
    {
        if (TableAt(screen) is not { } at) return;
        if (_selObjective >= 0 && _selObjective < Draft.Objectives.Count)
        {
            Draft.Objectives[_selObjective] = (MapDraft.SnapInch(at.X, _snap), MapDraft.SnapInch(at.Y, _snap));
            Sync3D();
            return;
        }
        if (_sel is not { } p) return;
        if (_resizeCorner >= 0)
        {
            // The corner follows the pointer; the opposite corner stays put.
            float a = p.Angle * Mathf.Pi / 180f, c = Mathf.Cos(a), s = Mathf.Sin(a);
            var opp = p.Corners()[(_resizeCorner + 2) % 4];
            float dx = at.X - opp.X, dz = at.Y - opp.Z;
            float lx = dx * c + dz * s, lz = -dx * s + dz * c;
            float w = Mathf.Max(1, MapDraft.SnapInch(Mathf.Abs(lx), _snap)), d = Mathf.Max(1, MapDraft.SnapInch(Mathf.Abs(lz), _snap));
            if (p.Round) { w = d = Mathf.Max(w, d); }
            float mx = Mathf.Sign(lx == 0 ? 1 : lx) * w / 2, mz = Mathf.Sign(lz == 0 ? 1 : lz) * d / 2;
            p.W = w; p.D = d;
            p.X = opp.X + mx * c - mz * s;
            p.Z = opp.Z + mx * s + mz * c;
        }
        else
        {
            p.X = MapDraft.SnapInch(at.X + _dragOffset.X, _snap);
            p.Z = MapDraft.SnapInch(at.Y + _dragOffset.Y, _snap);
            Draft.Clamp(p);
        }
        Sync3D();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
        bool handled = true;
        switch (k.Keycode)
        {
            case Key.Z when k.CtrlPressed: if (Draft.Undo()) AfterUndo(); break;
            case Key.Y when k.CtrlPressed: if (Draft.Redo()) AfterUndo(); break;
            case Key.C when k.CtrlPressed: if (_sel != null) { _clipboard = _sel.Clone(); Say("Copied."); } break;
            case Key.V when k.CtrlPressed: Paste(); break;
            case Key.S when k.CtrlPressed: SaveMap(); break;
            case Key.Delete or Key.Backspace: DeleteSelected(); break;
            case Key.Q or Key.E when _sel is { Round: false, Raw: null } p:
                if (!k.Echo) Draft.Remember();
                p.Angle = Mathf.Wrap(p.Angle + (k.Keycode == Key.Q ? 1 : -1) * (k.ShiftPressed ? 5 : 15), -180, 180);
                Rebuild();
                break;
            case Key.Q: _yaw += 5; ApplyCamera(); break;
            case Key.E: _yaw -= 5; ApplyCamera(); break;
            case Key.T when !k.Echo: ToggleTopView(); break;
            default: handled = false; break;
        }
        if (handled) GetViewport().SetInputAsHandled();
    }

    // ================================================================== screenshot driver

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
                _sel = MapPiece.FromKind(k, x, z);
                Draft.Pieces.Add(_sel);
                break;
            }
            case "obj": { var (x, z) = XZ(arg); Draft.Objectives.Add((x, z)); break; }
            case "select" when int.TryParse(arg, out int i) && i < Draft.Pieces.Count: _sel = Draft.Pieces[i]; break;
            case "generate" when Enum.TryParse(arg, out TerrainBook.ETheme t): Generate(t); return;
            case "rules": _rulesView = !_rulesView; break;
            case "top": ToggleTopView(); break;
            case "turn" when _sel != null && float.TryParse(arg, out float deg): _sel.Angle = deg; break;
            case "check" when Enum.TryParse(arg, out EGameSystem sys): _checkFor = sys; break;
            case "name": Draft.Name = arg; break;
            case "save": SaveMap(); return;
            case "size" when int.TryParse(arg, out int si) && si < TableSizing.Sizes.Count():
                var size = TableSizing.Sizes.ElementAt(si);
                Draft.TableW = size.W; Draft.TableH = size.H; _checkFor = DefaultGame(); BuildBoard(); ApplyCamera();
                break;
        }
        Rebuild();
    }
}
