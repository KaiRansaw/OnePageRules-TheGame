using FdgRaylib.Models3D;
using FdgRaylib.Paper;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// Match paper models (lane 5, Kai 2026-11-27: "They're not named, so I need to manually match them to their units").
/// Like Match models, for the paper figures: a picture grid of the cut-outs (show unmatched / matched / all, one sheet
/// or all), click a figure - Shift+click for several - then pick the game, the army (in Army Forge's faction groups)
/// and the unit, and Give. A unit may have several figures; a squad mixes them on the table, in turn. A figure's
/// matches can be taken off here too (Unmatch), or off one unit in "This unit has".
///
/// <para>A Grimdark Future unit's match carries to Firefight and Star Quest by name, an Age of Fantasy one to Skirmish
/// and Quest (<see cref="PaperUnitChoices.AliasesFor"/>). Everything saves at once to UnitModels/_Paper/paper-matches.json;
/// the Asset Lab's "Send my settings to GitHub" ships it with the pictures.</para>
/// </summary>
public partial class PaperMatchScreen : Control
{
    private string _root = "";
    private PaperMatches _matches = new();
    private PaperMatches _shipped = new();
    private string _matchesPath = "";
    private readonly PaperUnitChoices _choices = new();
    private List<(PaperSheet Sheet, PaperFigure Figure, string Root)> _all = new();
    private readonly List<string> _shown = new();
    private readonly HashSet<string> _picked = new();
    private string? _hover;
    private readonly List<(string Book, string Unit)> _unitRows = new();
    private (string Book, string Unit)? _unit;
    private readonly Dictionary<string, Texture2D?> _thumbs = new();

    private OptionButton _filter = null!, _sheetPick = null!, _game = null!, _faction = null!;
    private LineEdit _search = null!;
    private HFlowContainer _grid = null!;
    private Label _count = null!, _pickInfo = null!, _target = null!, _hasTitle = null!, _status = null!;
    private TextureRect _big = null!;
    private ItemList _units = null!;
    private HFlowContainer _has = null!;
    private Button _give = null!;
    private readonly List<string> _factionBooks = new();

    private SubViewport _vp = null!;
    private Node3D _stage = null!;
    private Camera3D _cam = null!;
    private float _spin;

    public override void _Ready()
    {
        Name = "PaperMatch";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _root = PaperStore.LocalRoot;
        _matchesPath = PaperMatches.LocalPath(_root);
        _matches = PaperMatches.Load(_matchesPath);
        _shipped = PaperMatches.Load(PaperMatches.ShippedPath);
        // Shipped matches start this computer's file, so a fresh PC can unmatch / add to what came with the game.
        if (!System.IO.File.Exists(_matchesPath) && _shipped.Units.Count > 0) _matches = PaperMatches.Merged(_shipped, new PaperMatches());
        LoadFigures();
        var bg = new ColorRect { Color = Palette.Ink, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        Build();
        FillSheets();
        FillGrid();
        FillFactions();
    }

    /// <summary>Every figure: this computer's sheets, then any shipped sheet this computer doesn't have.</summary>
    private void LoadFigures()
    {
        _all = new();
        var local = PaperStore.LoadSheets(_root);
        foreach (var s in local) foreach (var f in s.Figures) _all.Add((s, f, _root));
        foreach (var s in PaperStore.LoadSheets(PaperStore.ShippedRoot).Where(s => local.All(l => l.Name != s.Name)))
            foreach (var f in s.Figures) _all.Add((s, f, PaperStore.ShippedRoot));
    }

    private string IdOf((PaperSheet Sheet, PaperFigure Figure, string Root) x) => x.Sheet.IdOf(x.Figure);

    // ================================================================== layout

    private void Build()
    {
        var row = new HBoxContainer();
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 16; row.OffsetTop = 16; row.OffsetRight = -16; row.OffsetBottom = -16;
        row.AddThemeConstantOverride("separation", 14);
        AddChild(row);

        // ---- left: the figures
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(100, 40), TooltipText = "Back (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("MATCH PAPER MODELS", 30, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text("Give the cut-out figures to units", 14, HudKit.Dim, HudKit.Semi));
        head.AddChild(t);
        left.AddChild(head);
        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 8);
        _filter = new OptionButton { Name = "Show", FocusMode = FocusModeEnum.None, TooltipText = "Which figures to list." };
        _filter.AddItem("Not matched yet");
        _filter.AddItem("Matched");
        _filter.AddItem("All figures");
        _filter.ItemSelected += _ => FillGrid();
        filters.AddChild(_filter);
        _sheetPick = new OptionButton { Name = "SheetFilter", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = "Only one sheet's figures." };
        _sheetPick.ItemSelected += _ => FillGrid();
        filters.AddChild(_sheetPick);
        var all = new Button { Name = "PickAllShown", Text = "Pick all shown", FocusMode = FocusModeEnum.None, TooltipText = "Pick every figure listed (to give a whole sheet's squad to one unit)." };
        all.Pressed += () => { foreach (string id in _shown) _picked.Add(id); RefreshPicks(); };
        filters.AddChild(all);
        var none = new Button { Name = "ClearPicks", Text = "Clear", FocusMode = FocusModeEnum.None, TooltipText = "Pick nothing." };
        none.Pressed += () => { _picked.Clear(); RefreshPicks(); };
        filters.AddChild(none);
        left.AddChild(filters);
        _count = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        left.AddChild(_count);
        var scroll = new ScrollContainer { Name = "GridScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _grid = new HFlowContainer { Name = "Grid", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(_grid);
        left.AddChild(scroll);
        var leftPanel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(left, 14, 12, 14, 12));
        leftPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(leftPanel);

        // ---- right: the picture, the unit
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(470, 0) };
        right.AddThemeConstantOverride("separation", 6);
        var look = new HBoxContainer { CustomMinimumSize = new Vector2(0, 250) };
        look.AddThemeConstantOverride("separation", 8);
        _big = new TextureRect { Name = "BigPicture", SizeFlagsHorizontal = SizeFlags.ExpandFill, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TooltipText = "The figure under the pointer (or the first picked), bigger." };
        look.AddChild(_big);
        look.AddChild(Preview3D());
        right.AddChild(look);
        _pickInfo = HudKit.Text("", 14, HudKit.Fg, HudKit.Semi);
        _pickInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_pickInfo);
        var un = new Button { Name = "Unmatch", Text = "Unmatch picked (from every unit)", FocusMode = FocusModeEnum.None, TooltipText = "Take the picked figures off every unit they're given to." };
        un.Pressed += Unmatch;
        right.AddChild(un);

        right.AddChild(HudKit.Heading("Unit", 15));
        _game = new OptionButton { Name = "Game", FocusMode = FocusModeEnum.None, TooltipText = "The game whose armies to list. A Grimdark Future match also counts in Firefight and Star Quest (same unit name); an Age of Fantasy one in Skirmish and Quest." };
        foreach (var g in PaperUnitChoices.Games) _game.AddItem(g.Name);
        _game.ItemSelected += _ => { FillFactions(); };
        right.AddChild(_game);
        _faction = new OptionButton { Name = "Faction", FocusMode = FocusModeEnum.None, TooltipText = "The army, in Army Forge's faction groups." };
        _faction.ItemSelected += _ => { _unit = null; FillUnits(); };
        right.AddChild(_faction);
        _search = new LineEdit { Name = "Search", PlaceholderText = "Search every army in this game (unit or army name)", ClearButtonEnabled = true };
        _search.TextChanged += _ => { _unit = null; FillUnits(); };
        right.AddChild(_search);
        _units = new ItemList { Name = "Units", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160), FocusMode = FocusModeEnum.None, AllowSearch = false };
        _units.ItemSelected += i => { if (i >= 0 && i < _unitRows.Count) { _unit = _unitRows[(int)i]; FillHas(); UpdateTarget(); } };
        right.AddChild(_units);
        _target = HudKit.Text("", 14, HudKit.Fg, HudKit.Bold);
        _target.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_target);
        _give = new Button { Name = "Give", Text = "Give to this unit (Space)", ThemeTypeVariation = "CommitButton", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), TooltipText = "Give the picked figures to the unit picked above." };
        _give.Pressed += Give;
        right.AddChild(_give);
        _hasTitle = HudKit.Text("This unit has", 15, HudKit.Teal, HudKit.Bold);
        right.AddChild(_hasTitle);
        var hasScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 104), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _has = new HFlowContainer { Name = "Has", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        hasScroll.AddChild(_has);
        right.AddChild(hasScroll);
        _status = HudKit.Text("Saved as you go. Asset Lab -> Send my settings to GitHub puts your matches and figures in the game.", 13, HudKit.Dim, HudKit.Semi);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_status);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(right, 14, 12, 14, 12)));
    }

    /// <summary>The figure as it stands on the table: a standee on a 25 mm base beside a 28 mm person, turning slowly so its
    /// back shows too.</summary>
    private Control Preview3D()
    {
        var box = new SubViewportContainer { Name = "StandeePreview", Stretch = true, CustomMinimumSize = new Vector2(210, 0), TooltipText = "On the table: the standee on its base, beside a 28 mm person. It turns, to show its back." };
        _vp = new SubViewport { OwnWorld3D = true, TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa2X };
        box.AddChild(_vp);
        var world = new Node3D();
        _vp.AddChild(world);
        TableLook.AddLighting(world);
        TableLook.AddBoard(world, 12, 12);
        _stage = new Node3D { Name = "Stage" };
        world.AddChild(_stage);
        // a 28 mm person for scale
        float baseR = 25 / UnitModelSettings.MmPerInch / 2, hIn = 32 / UnitModelSettings.MmPerInch;
        var man = new Node3D { Position = new Vector3(-1.4f, 0, 0) };
        world.AddChild(man);
        TableLook.AddBase(man, baseR, new Color(0.5f, 0.5f, 0.5f));
        var mat = TableLook.Mat(new Color(0.62f, 0.64f, 0.66f), 0.7f);
        float y0 = TableLook.BaseTop, head = hIn * 0.13f, legs = hIn * 0.47f, torso = hIn - head * 2 - legs;
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, mat, new Vector3(0, y0 + legs / 2, 0));
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.17f, Height = torso + 0.1f }, mat, new Vector3(0, y0 + legs + torso / 2, 0));
        TableLook.Mesh(man, new SphereMesh { Radius = head, Height = head * 2 }, mat, new Vector3(0, y0 + hIn - head, 0));
        _cam = new Camera3D { Fov = 35, Current = true, Near = 0.05f, Far = 200 };
        world.AddChild(_cam);
        return box;
    }

    public override void _Process(double delta)
    {
        if (_cam == null) return;
        _spin += (float)delta * 0.6f;
        Vector3 focus = new(-0.6f, 0.9f, 0);
        _cam.Position = focus + new Vector3(Mathf.Sin(_spin) * 5.5f, 2.6f, Mathf.Cos(_spin) * 5.5f);
        _cam.LookAt(focus, Vector3.Up);
    }

    private void ShowStandee(string? id)
    {
        foreach (Node c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
        if (id == null || Find(id) is not { } x) return;
        var holder = new Node3D { Position = new Vector3(0.2f, 0, 0) };
        _stage.AddChild(holder);
        TableLook.AddBase(holder, 0.492f, HudKit.Lime);
        string front = PaperStore.FigurePath(x.Root, x.Sheet.Name, x.Figure.N), back = PaperStore.FigurePath(x.Root, x.Sheet.Name, x.Figure.N, back: true);
        var (w, h) = PaperLibrary.PngSize(front);
        Standee.Add(holder, new PaperLibrary.Figure(id, front, System.IO.File.Exists(back) ? back : null, w, h), 0.492f);
    }

    // ================================================================== the grid

    private (PaperSheet Sheet, PaperFigure Figure, string Root)? Find(string id)
    {
        foreach (var x in _all) if (IdOf(x) == id) return x;
        return null;
    }

    private void FillSheets()
    {
        _sheetPick.Clear();
        _sheetPick.AddItem("Every sheet");
        foreach (string s in _all.Select(x => x.Sheet.Name).Distinct()) _sheetPick.AddItem(s);
    }

    private void FillGrid()
    {
        foreach (Node c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }
        _shown.Clear();
        string? sheet = _sheetPick.Selected > 0 ? _sheetPick.GetItemText(_sheetPick.Selected) : null;
        foreach (var x in _all)
        {
            string id = IdOf(x);
            bool matched = _matches.IsMatched(id);
            if (_filter.Selected == 0 && matched || _filter.Selected == 1 && !matched) continue;
            if (sheet != null && x.Sheet.Name != sheet) continue;
            _shown.Add(id);
            _grid.AddChild(Tile(x, id, matched));
        }
        int total = _all.Count, done = _all.Count(x => _matches.IsMatched(IdOf(x)));
        _count.Text = total == 0 ? "No paper models yet: Asset Lab -> Paper models -> Import PDF sheets..."
            : $"{_shown.Count} shown  -  {done} of {total} figures matched  -  click to pick, Shift+click to pick several";
        RefreshPicks();
    }

    private Texture2D? Thumb(string path)
    {
        if (_thumbs.TryGetValue(path, out var t)) return t;
        t = System.IO.File.Exists(path) && Image.LoadFromFile(path) is { } img ? ImageTexture.CreateFromImage(img) : null;
        return _thumbs[path] = t;
    }

    private Control Tile((PaperSheet Sheet, PaperFigure Figure, string Root) x, string id, bool matched)
    {
        var b = new Button { Name = "Fig_" + Safe(id), CustomMinimumSize = new Vector2(112, 168), FocusMode = FocusModeEnum.None, TooltipText = TipFor(id) };
        b.SetMeta("id", id);
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        v.OffsetLeft = 4; v.OffsetTop = 4; v.OffsetRight = -4; v.OffsetBottom = -4;
        var pic = new TextureRect { Texture = Thumb(PaperStore.FigurePath(x.Root, x.Sheet.Name, x.Figure.N)), SizeFlagsVertical = SizeFlags.ExpandFill, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        v.AddChild(pic);
        var l = HudKit.Text($"{Short(x.Sheet.Name)} {x.Figure.N}", 11, matched ? HudKit.Lime : HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center);
        l.ClipText = true;
        l.MouseFilter = MouseFilterEnum.Ignore;
        v.AddChild(l);
        b.AddChild(v);
        // A plain lime frame on a picked figure (the UI theme's art can hide a border change).
        var frame = new ReferenceRect { Name = "Picked", BorderColor = HudKit.Lime, BorderWidth = 3, EditorOnly = false, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        b.AddChild(frame);
        b.Pressed += () => Click(id);
        b.MouseEntered += () => { _hover = id; ShowBig(); };
        b.MouseExited += () => { if (_hover == id) { _hover = null; ShowBig(); } };
        return b;
    }

    private static string Short(string s) => s.Length > 12 ? s[..11] + "." : s;

    private static string Safe(string id) => new(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private string TipFor(string id)
    {
        var units = _matches.UnitsOf(id).Select(u => $"{u.Book}: {u.Unit}").ToList();
        return id + (units.Count == 0 ? "\nNot matched yet." : "\nGiven to " + string.Join(", ", units));
    }

    private void Click(string id)
    {
        bool add = Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl);
        if (!add) { bool only = _picked.Count == 1 && _picked.Contains(id); _picked.Clear(); if (!only) _picked.Add(id); }
        else if (!_picked.Remove(id)) _picked.Add(id);
        RefreshPicks();
    }

    private void RefreshPicks()
    {
        foreach (Node n in _grid.GetChildren())
            if (n is Button b && b.HasMeta("id") && b.GetNodeOrNull<ReferenceRect>("Picked") is { } frame) frame.Visible = _picked.Contains((string)b.GetMeta("id"));
        if (_picked.Count == 0) _pickInfo.Text = "Pick figures on the left.";
        else if (_picked.Count == 1)
        {
            string id = _picked.First();
            var units = _matches.UnitsOf(id).Select(u => $"{u.Book}: {u.Unit}").ToList();
            _pickInfo.Text = $"{id}  -  " + (units.Count == 0 ? "not matched yet" : "given to " + string.Join(", ", units));
        }
        else _pickInfo.Text = $"{_picked.Count} figures picked.";
        ShowBig();
        UpdateTarget();
    }

    private void ShowBig()
    {
        string? id = _hover ?? _picked.FirstOrDefault();
        _big.Texture = id != null && Find(id) is { } x ? Thumb(PaperStore.FigurePath(x.Root, x.Sheet.Name, x.Figure.N)) : null;
        ShowStandee(id);
    }

    // ================================================================== the unit side

    private PaperUnitChoices.GameChoice Game => PaperUnitChoices.Games[Math.Max(0, _game.Selected)];

    private void FillFactions()
    {
        _faction.Clear();
        _factionBooks.Clear();
        foreach (var g in _choices.Groups(Game))
        {
            _faction.AddSeparator(g.Name);
            _factionBooks.Add("");
            foreach (string b in g.Books) { _faction.AddItem(b); _factionBooks.Add(b); }
        }
        int first = _factionBooks.FindIndex(b => b.Length > 0);
        if (first >= 0) _faction.Select(first);
        _unit = null;
        FillUnits();
    }

    private string Book => _faction.Selected >= 0 && _faction.Selected < _factionBooks.Count ? _factionBooks[_faction.Selected] : "";

    private void FillUnits()
    {
        _units.Clear();
        _unitRows.Clear();
        var lib = PaperLibrary.Build(_root, PaperStore.ShippedRoot);
        IEnumerable<(string Book, string Unit)> rows = _search.Text.Trim().Length > 0
            ? _choices.Search(Game, _search.Text)
            : _choices.Units(Game, Book).Select(u => (Book, u));
        bool many = _search.Text.Trim().Length > 0;
        foreach (var (book, unit) in rows)
        {
            int n = lib.FiguresFor(book, unit).Count;
            _unitRows.Add((book, unit));
            int i = _units.AddItem((many ? book + ": " : "") + unit + (n > 0 ? $"   [{n}]" : ""));
            if (n > 0) _units.SetItemCustomFgColor(i, HudKit.Lime);
            if (_unit is { } u && u.Book == book && u.Unit == unit) _units.Select(i);
        }
        FillHas();
        UpdateTarget();
    }

    private void UpdateTarget()
    {
        _target.Text = _unit is not { } u ? "Pick a unit." : _picked.Count == 0 ? $"{u.Unit} ({u.Book}) - pick figures to give it."
            : $"Give {(_picked.Count == 1 ? "this figure" : _picked.Count + " figures")} to {u.Unit} ({Game.Name}: {u.Book})";
        _give.Disabled = _unit == null || _picked.Count == 0;
    }

    private void FillHas()
    {
        foreach (Node c in _has.GetChildren()) { _has.RemoveChild(c); c.QueueFree(); }
        if (_unit is not { } u) { _hasTitle.Text = "This unit has"; return; }
        var m = _matches.Find(Game.Name, u.Book, u.Unit);
        // a smaller game's unit also shows what its main game's match gives it
        var lib = PaperLibrary.Build(_root, PaperStore.ShippedRoot);
        var figs = lib.FiguresFor(u.Book, u.Unit);
        _hasTitle.Text = figs.Count == 0 ? $"{u.Unit} has no paper figure yet" : $"{u.Unit} has {figs.Count}";
        foreach (var f in figs)
        {
            var cell = new VBoxContainer { CustomMinimumSize = new Vector2(64, 96) };
            cell.AddChild(new TextureRect { Texture = Thumb(f.FrontPath), CustomMinimumSize = new Vector2(64, 70), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TooltipText = f.Id });
            bool own = m != null && m.Figures.Contains(f.Id);
            var x = new Button { Text = own ? "Remove" : "(other game)", Disabled = !own, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(64, 22),
                TooltipText = own ? "Take this figure off the unit." : "Given through the main game's unit - remove it there." };
            x.AddThemeFontSizeOverride("font_size", 11);
            string id = f.Id;
            x.Pressed += () => { _matches.Unassign(Game.Name, u.Book, u.Unit, new[] { id }); Saved($"Took {id} off {u.Unit}."); };
            cell.AddChild(x);
            _has.AddChild(cell);
        }
    }

    public void Give()
    {
        if (_unit is not { } u || _picked.Count == 0) return;
        _matches.Assign(Game.Name, u.Book, u.Unit, _picked.OrderBy(Order), _choices.AliasesFor(Game, u.Book, u.Unit));
        int n = _picked.Count;
        if (_filter.Selected == 0) _picked.Clear(); // they leave the "not matched" list
        Saved($"Gave {n} figure{(n == 1 ? "" : "s")} to {u.Unit}.");
    }

    /// <summary>Picked figures in grid order (so a squad mixes them in the order they sit on the sheet).</summary>
    private int Order(string id) { int i = _all.FindIndex(x => IdOf(x) == id); return i < 0 ? int.MaxValue : i; }

    private void Unmatch()
    {
        if (_picked.Count == 0) return;
        int n = _matches.UnassignEverywhere(_picked);
        Saved($"Took the picked figures off {n} unit match{(n == 1 ? "" : "es")}.");
    }

    private void Saved(string what)
    {
        _matches.Save(_matchesPath);
        PaperLibrary.Reload();
        HudKit.ForgetPictures();
        _status.Text = what + "  Saved.";
        int scroll = (int)_grid.GetParent<ScrollContainer>().ScrollVertical;
        FillGrid();
        _grid.GetParent<ScrollContainer>().SetDeferred("scroll_vertical", scroll);
        FillUnits();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Space } && !(_search.HasFocus())) { Give(); AcceptEvent(); }
    }

    /// <summary>
    /// The test driver (ScriptedRun "pm:&lt;command&gt;"): "show:&lt;0-2&gt;", "sheet:&lt;i&gt;", "pick:&lt;id&gt;[;&lt;id&gt;]",
    /// "pickall", "game:&lt;name&gt;", "book:&lt;name&gt;", "search:&lt;text&gt;", "unit:&lt;name&gt;", "give", "unmatch", "hover:&lt;id&gt;".
    /// </summary>
    public void Drive(string cmd)
    {
        string arg = cmd.Contains(':') ? cmd[(cmd.IndexOf(':') + 1)..] : "";
        switch (cmd.Split(':')[0])
        {
            case "show": if (int.TryParse(arg, out int s)) { _filter.Select(s); FillGrid(); } break;
            case "sheet": if (int.TryParse(arg, out int sh)) { _sheetPick.Select(sh); FillGrid(); } break;
            case "pick": _picked.Clear(); foreach (string id in arg.Split(';', StringSplitOptions.RemoveEmptyEntries)) _picked.Add(id); RefreshPicks(); break;
            case "pickall": foreach (string id in _shown) _picked.Add(id); RefreshPicks(); break;
            case "game":
                int gi = Array.FindIndex(PaperUnitChoices.Games, g => g.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (gi >= 0) { _game.Select(gi); FillFactions(); }
                break;
            case "book":
                int bi = _factionBooks.FindIndex(b => b.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (bi >= 0) { _faction.Select(bi); _unit = null; FillUnits(); }
                break;
            case "search": _search.Text = arg; _unit = null; FillUnits(); break;
            case "unit":
                int ui = _unitRows.FindIndex(r => r.Unit.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (ui >= 0) { _units.Select(ui); _unit = _unitRows[ui]; FillHas(); UpdateTarget(); }
                break;
            case "give": Give(); break;
            case "unmatch": Unmatch(); break;
            case "hover": _hover = arg.Length == 0 ? null : arg; ShowBig(); break;
        }
    }
}
