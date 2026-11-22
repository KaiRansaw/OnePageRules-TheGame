using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;
using OprGodot.Forge;

namespace OprGodot.Lab;

/// <summary>
/// The Terrain Workshop (built-in terrain plan, step 2; Kai, 2026-11-20: designed "inside the game"). Pick a terrain
/// kind and a footprint, stand built-in models on it, save it as a piece the Map Maker and battles reuse
/// (<see cref="TerrainPieces"/>, drawn by <see cref="PieceModels"/>). Both model sets are here; Kai works on
/// whichever he likes (2026-11-21).
///
/// <para>Controls on the table: left-click a model to pick it, left-drag to move it, right-drag to turn the view,
/// middle-drag to pan, wheel to zoom. Q / E turn the picked model (Shift: 5 degrees), [ / ] size it, PageUp /
/// PageDown raise it, Delete removes it, Ctrl+D copies it. Ctrl+S saves the piece.</para>
/// </summary>
public partial class TerrainWorkshopScreen : Control
{
    private const float Ground = 30; // the patch of table around the footprint, inches

    private string _style = TerrainPieces.Stylised;
    private TerrainPieces.Piece _piece = NewPiece(TerrainPieces.Stylised);
    private bool _dirty;
    private int _sel = -1;

    private ItemList _pieceList = null!, _palette = null!;
    private OptionButton _styleOpt = null!, _kindOpt = null!, _gameOpt = null!, _groupOpt = null!;
    private LineEdit _name = null!, _search = null!;
    private SpinBox _w = null!, _d = null!, _iScale = null!, _iTurn = null!, _iY = null!;
    private CheckBox _round = null!;
    private Label _status = null!, _selLabel = null!;
    private VBoxContainer _selBox = null!;
    private SubViewportContainer _box = null!;
    private SubViewport _view = null!;
    private Node3D _world = null!, _pieceRoot = null!, _outline = null!;
    private Camera3D _cam = null!;
    private readonly List<Node3D> _nodes = new();
    private List<PieceModels.Model> _shown = new();
    private float _yaw = 30, _pitch = 50, _dist = 26;
    private Vector3 _focus = Vector3.Zero;
    private bool _dragging, _orbiting, _panning;
    private Vector2 _dragOffset;
    private bool _syncing;

    private static readonly string[] Kinds =
    {
        "Forest", "Field", "Hill", "Mountain", "Rubble", "Ruins", "Building", "Wall", "Barricade",
        "LakeShallow", "LakeDeep", "RiverShallow", "RiverDeep", "Swamp", "Lava", TerrainLooks.Wreck, TerrainLooks.Hazard,
    };
    private static readonly (string Id, string Label)[] Games =
    {
        (TerrainPieces.ForBoth, "Both games"), (TerrainPieces.ForGf, "Grimdark Future"), (TerrainPieces.ForAof, "Age of Fantasy"),
    };

    private static TerrainPieces.Piece NewPiece(string style) => new() { Style = style, Name = "New piece", Kind = "Forest", W = 6, D = 6, Round = true };

    public override void _Ready()
    {
        Name = "TerrainWorkshop";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new Panel { MouseFilter = MouseFilterEnum.Ignore }.Also(p => p.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect)));
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 14);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        margin.AddChild(col);

        // ---- top bar
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        top.AddChild(Ui.Button("Back", Leave, "Back to the Asset Lab (Esc).", "BackButton").Also(b => b.Name = "WorkshopBack"));
        top.AddChild(new Label { Text = "Terrain Workshop" }.Also(Ui.ScreenTitle));
        _styleOpt = new OptionButton { Name = "WorkshopStyle" };
        _styleOpt.AddItem("Stylised models");
        _styleOpt.AddItem("Realistic models");
        _styleOpt.TooltipText = "Which model set you are building with. Each set has its own pieces; the player picks the set in Settings (Terrain style).";
        _styleOpt.ItemSelected += i => SwitchStyle(i == 1 ? TerrainPieces.Realistic : TerrainPieces.Stylised);
        top.AddChild(_styleOpt);
        top.AddChild(Ui.Grow());
        top.AddChild(Ui.Button("New", () => Confirmed(() => Load(NewPiece(_style))), "Start a new piece.").Also(b => b.Name = "WorkshopNew"));
        top.AddChild(Ui.Button("Copy", CopyPiece, "Start a new piece from this one.").Also(b => b.Name = "WorkshopCopy"));
        top.AddChild(Ui.Button("Delete", DeletePiece, "Delete this piece.").Also(b => b.Name = "WorkshopDelete"));
        top.AddChild(Ui.Button("Save", Save, "Save the piece (Ctrl+S). Maps and battles use it from the next time they draw the table.").Also(b => b.Name = "WorkshopSave"));
        col.AddChild(top);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        col.AddChild(body);

        // ---- left: pieces + the piece's settings
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        left.AddThemeConstantOverride("separation", 6);
        left.AddChild(HudKit.Heading("Pieces", 15));
        _pieceList = new ItemList { Name = "PieceList", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 200) };
        _pieceList.ItemSelected += i => { var p = PiecesShown().ElementAtOrDefault((int)i); if (p != null && p.Id != _piece.Id) Confirmed(() => Load(p.Copy()), RefreshPieceList); };
        left.AddChild(_pieceList);
        left.AddChild(HudKit.Heading("This piece", 15));
        _name = new LineEdit { Name = "PieceName", PlaceholderText = "Name" };
        _name.TextChanged += t => { if (_syncing) return; _piece.Name = t; Touch(); };
        left.AddChild(Labelled("Name", _name));
        _kindOpt = new OptionButton { Name = "PieceKind" };
        foreach (string k in Kinds) _kindOpt.AddItem(KindLabel(k));
        _kindOpt.ItemSelected += i => { _piece.Kind = Kinds[i]; Touch(); };
        left.AddChild(Labelled("Terrain kind", _kindOpt, "The book's terrain kind this piece is drawn for. A map's Forest is drawn with a Forest piece, and so on."));
        _gameOpt = new OptionButton { Name = "PieceGame" };
        foreach (var g in Games) _gameOpt.AddItem(g.Label);
        _gameOpt.ItemSelected += i => { _piece.Game = Games[i].Id; Touch(); };
        left.AddChild(Labelled("For", _gameOpt, "Which games use it. Nature usually suits both; ruins and buildings one or the other."));
        _w = Spin("PieceW", 1, 24, 0.5, v => { _piece.W = (float)v; if (_piece.Round) _d.Value = v; Touch(); DrawOutline(); });
        _d = Spin("PieceD", 1, 24, 0.5, v => { _piece.D = (float)v; Touch(); DrawOutline(); });
        var size = new HBoxContainer();
        size.AddThemeConstantOverride("separation", 6);
        size.AddChild(_w);
        size.AddChild(new Label { Text = "x" });
        size.AddChild(_d);
        size.AddChild(new Label { Text = "in" });
        left.AddChild(Labelled("Footprint", size, "The size you build on. A map's piece of another size stretches this one a little (up to half as big again or two thirds as small); past that the game looks for another piece."));
        _round = new CheckBox { Name = "PieceRound", Text = "Round footprint" };
        _round.Toggled += on => { _piece.Round = on; if (on) _d.Value = _w.Value; Touch(); DrawOutline(); };
        left.AddChild(_round);
        body.AddChild(new PanelContainer().Also(p => p.AddChild(left)));

        // ---- middle: the table
        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mid.AddThemeConstantOverride("separation", 4);
        _box = new SubViewportContainer { Name = "WorkshopView", Stretch = true, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All };
        _view = new SubViewport { OwnWorld3D = true, Size = new Vector2I(1200, 800), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, Msaa3D = Viewport.Msaa.Msaa4X };
        _box.AddChild(_view);
        _world = new Node3D();
        _view.AddChild(_world);
        TableLook.AddLighting(_world, null, "Hospitable", Ground, Ground, battlefield: true);
        TableLook.AddBoard(_world, Ground, Ground, horizon: "Hospitable", battlefield: true);
        _outline = new Node3D { Name = "Footprint" };
        _world.AddChild(_outline);
        _pieceRoot = new Node3D { Name = "Piece" };
        _world.AddChild(_pieceRoot);
        // A 28mm figure on a 32mm base beside the footprint, for scale.
        var mini = new Node3D { Name = "ScaleFigure" };
        _world.AddChild(mini);
        TableLook.AddBase(mini, 0.63f, new Color(0.85f, 0.75f, 0.2f));
        TableLook.Mesh(mini, new CapsuleMesh { Radius = 0.22f, Height = 1.1f }, TableLook.Mat(new Color(0.6f, 0.62f, 0.66f)), new Vector3(0, 0.7f, 0));
        _cam = new Camera3D { Fov = 36, Near = 0.1f, Far = 4000, Current = true };
        _world.AddChild(_cam);
        _box.GuiInput += OnViewInput;
        mid.AddChild(_box);
        mid.AddChild(HudKit.Text("Click a model to pick it, drag to move. Q/E turn (Shift: 5 deg)  [ ] size  PgUp/PgDn raise  Del remove  Ctrl+D copy.  Right-drag turns the view, middle-drag pans, wheel zooms. The figure is a 28mm model on a 32mm base.", 13, HudKit.Dim, HudKit.Semi).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        _status = HudKit.Text("", 14, HudKit.Lime, HudKit.Semi);
        _status.Name = "WorkshopStatus";
        mid.AddChild(_status);
        body.AddChild(mid);

        // ---- right: model palette + the picked model
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        right.AddThemeConstantOverride("separation", 6);
        right.AddChild(HudKit.Heading("Models", 15));
        _groupOpt = new OptionButton { Name = "ModelGroup" };
        _groupOpt.ItemSelected += _ => FillPalette();
        right.AddChild(_groupOpt);
        _search = new LineEdit { Name = "ModelSearch", PlaceholderText = "Search models", ClearButtonEnabled = true };
        _search.TextChanged += _ => FillPalette();
        right.AddChild(_search);
        _palette = new ItemList { Name = "ModelList", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220) };
        _palette.ItemActivated += i => AddModel((int)i);
        _palette.TooltipText = "Double-click to put a model in the middle of the footprint.";
        right.AddChild(_palette);
        right.AddChild(Ui.Button("Add to piece", () => { if (_palette.GetSelectedItems() is { Length: > 0 } s) AddModel(s[0]); }, "Puts the picked model in the middle of the footprint.").Also(b => b.Name = "AddModel"));
        _selBox = new VBoxContainer { Name = "Picked" };
        _selBox.AddThemeConstantOverride("separation", 6);
        _selBox.AddChild(HudKit.Heading("Picked model", 15));
        _selLabel = HudKit.Text("", 14, HudKit.Fg, HudKit.Semi);
        _selBox.AddChild(_selLabel);
        _iScale = Spin("ItemScale", 0.01, 20, 0.01, v => EditSel(i => i.Scale = (float)v));
        _selBox.AddChild(Labelled("Size (inches per unit)", _iScale, "0.7 is true 28mm scale for a model made in metres. Tabletop terrain is often a bit bigger than true scale."));
        _iTurn = Spin("ItemTurn", -360, 360, 1, v => EditSel(i => i.Turn = (float)v));
        _selBox.AddChild(Labelled("Turn (degrees)", _iTurn));
        _iY = Spin("ItemRaise", -6, 12, 0.05, v => EditSel(i => i.Y = (float)v));
        _selBox.AddChild(Labelled("Raise / sink (inches)", _iY, "Lift a model (a roof on its walls) or sink it into the ground."));
        var selButtons = new HBoxContainer();
        selButtons.AddThemeConstantOverride("separation", 6);
        selButtons.AddChild(Ui.Button("Copy", DuplicateSel, "Copy this model (Ctrl+D).").Also(b => b.Name = "ItemCopy"));
        selButtons.AddChild(Ui.Button("Remove", RemoveSel, "Take it off the piece (Delete).").Also(b => b.Name = "ItemRemove"));
        _selBox.AddChild(selButtons);
        right.AddChild(_selBox);
        body.AddChild(new PanelContainer().Also(p => p.AddChild(right)));

        SwitchStyle(_style, keepPiece: true);
        ApplyCamera();
    }

    // ------------------------------------------------------------------ pieces

    private IEnumerable<TerrainPieces.Piece> PiecesShown() => TerrainPieces.All.Where(p => p.Style == _style);

    private void RefreshPieceList()
    {
        _pieceList.Clear();
        int at = -1;
        foreach (var p in PiecesShown())
        {
            string game = p.Game == TerrainPieces.ForGf ? "GF" : p.Game == TerrainPieces.ForAof ? "AoF" : "both";
            _pieceList.AddItem($"{p.Name}  ({KindLabel(p.Kind)}, {game}, {p.W:0.#}x{p.D:0.#}\")");
            if (p.Id == _piece.Id) at = _pieceList.ItemCount - 1;
        }
        if (at >= 0) _pieceList.Select(at);
        else _pieceList.DeselectAll();
    }

    private void SwitchStyle(string style, bool keepPiece = false)
    {
        void Go()
        {
            _style = style;
            _styleOpt.Selected = style == TerrainPieces.Realistic ? 1 : 0;
            _groupOpt.Clear();
            _groupOpt.AddItem("All models");
            foreach (string g in PieceModels.GroupsOf(style)) _groupOpt.AddItem(g);
            FillPalette();
            Load(keepPiece ? _piece : PiecesShown().FirstOrDefault()?.Copy() ?? NewPiece(style));
        }
        if (keepPiece) Go(); else Confirmed(Go, () => _styleOpt.Selected = _style == TerrainPieces.Realistic ? 1 : 0);
    }

    private void Load(TerrainPieces.Piece p)
    {
        _piece = p;
        _piece.Style = _style;
        _syncing = true;
        _name.Text = p.Name;
        _kindOpt.Selected = Math.Max(0, Array.FindIndex(Kinds, k => k.Equals(p.Kind, StringComparison.OrdinalIgnoreCase)));
        _gameOpt.Selected = Math.Max(0, Array.FindIndex(Games, g => g.Id == p.Game));
        _w.Value = p.W;
        _d.Value = p.D;
        _round.ButtonPressed = p.Round;
        _syncing = false;
        _dirty = false;
        Rebuild();
        Select(-1);
        RefreshPieceList();
        Say(p.Id.Length == 0 ? "A new piece - pick models on the right." : $"{p.Name}: {p.Items.Count} models.");
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_piece.Name)) _piece.Name = KindLabel(_piece.Kind);
        if (_piece.Items.Count == 0) { Say("Add at least one model first."); return; }
        if (TerrainPieces.Save(_piece) is { } err) { Say("Couldn't save: " + err); return; }
        _dirty = false;
        RefreshPieceList();
        Say($"Saved \"{_piece.Name}\". Send it to GitHub from the Asset Lab (Send my settings) so friends get it.");
    }

    private void CopyPiece()
    {
        var c = _piece.Copy();
        c.Id = "";
        c.Name = _piece.Name + " (copy)";
        Load(c);
        _dirty = true;
    }

    private void DeletePiece()
    {
        if (_piece.Id.Length == 0) { Load(NewPiece(_style)); return; }
        var dlg = new ConfirmationDialog { Title = "Delete piece", DialogText = $"Delete \"{_piece.Name}\"? Maps that picked it go back to the game's own choice." };
        dlg.Confirmed += () => { TerrainPieces.Delete(_piece); Load(PiecesShown().FirstOrDefault()?.Copy() ?? NewPiece(_style)); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    /// <summary>Runs <paramref name="go"/>, first asking when there are unsaved changes.</summary>
    private void Confirmed(Action go, Action? cancelled = null)
    {
        if (!_dirty) { go(); return; }
        var dlg = new ConfirmationDialog { Title = "Unsaved piece", DialogText = $"\"{_piece.Name}\" has changes that aren't saved. Leave them?", OkButtonText = "Leave them" };
        dlg.Confirmed += () => { _dirty = false; go(); dlg.QueueFree(); };
        dlg.Canceled += () => { cancelled?.Invoke(); dlg.QueueFree(); };
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void Leave() => Confirmed(App.I.Back);

    private void Touch() { if (!_syncing) _dirty = true; }

    // ------------------------------------------------------------------ palette

    private void FillPalette()
    {
        string group = _groupOpt.Selected <= 0 ? "" : _groupOpt.GetItemText(_groupOpt.Selected);
        string q = _search.Text.Trim();
        _shown = PieceModels.Catalogue.Where(m => m.Style == _style && (group == "" || m.Group == group)
            && (q.Length == 0 || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        _palette.Clear();
        foreach (var m in _shown) _palette.AddItem(group == "" ? $"{m.Name}  ({m.Group})" : m.Name);
    }

    private void AddModel(int index)
    {
        if (index < 0 || index >= _shown.Count) return;
        var item = new TerrainPieces.Item { Model = _shown[index].Path, Scale = 0.7f, Turn = 0 };
        _piece.Items.Add(item);
        Touch();
        Rebuild();
        Select(_piece.Items.Count - 1);
        Say($"Added {_shown[index].Name}. Drag it into place.");
    }

    // ------------------------------------------------------------------ the picked model

    private void Select(int i)
    {
        _sel = i >= 0 && i < _piece.Items.Count ? i : -1;
        _selBox.Visible = _sel >= 0;
        if (_sel < 0) { _selLabel.Text = ""; DrawOutline(); return; }
        var it = _piece.Items[_sel];
        _syncing = true;
        _selLabel.Text = System.IO.Path.GetFileNameWithoutExtension(it.Model).Replace('_', ' ');
        _iScale.Value = it.Scale;
        _iTurn.Value = it.Turn;
        _iY.Value = it.Y;
        _syncing = false;
        DrawOutline();
    }

    private void EditSel(Action<TerrainPieces.Item> change)
    {
        if (_syncing || _sel < 0) return;
        change(_piece.Items[_sel]);
        Touch();
        Reposition(_sel);
        DrawOutline();
    }

    private void DuplicateSel()
    {
        if (_sel < 0) return;
        var c = _piece.Items[_sel].Copy();
        c.X += 1; c.Z += 1;
        _piece.Items.Add(c);
        Touch();
        Rebuild();
        Select(_piece.Items.Count - 1);
    }

    private void RemoveSel()
    {
        if (_sel < 0) return;
        _piece.Items.RemoveAt(_sel);
        Touch();
        Rebuild();
        Select(-1);
    }

    // ------------------------------------------------------------------ 3D

    private void Rebuild()
    {
        foreach (Node n in _pieceRoot.GetChildren()) n.QueueFree();
        _nodes.Clear();
        int missing = 0;
        foreach (var it in _piece.Items)
        {
            Node3D? n = PieceModels.Place(_pieceRoot, it);
            if (n == null) { missing++; n = new Node3D(); _pieceRoot.AddChild(n); }
            _nodes.Add(n);
        }
        if (missing > 0) Say($"{missing} model(s) of this piece aren't in this copy of the game.");
    }

    private void Reposition(int i)
    {
        if (i < 0 || i >= _nodes.Count) return;
        var it = _piece.Items[i];
        _nodes[i].Position = new Vector3(it.X, it.Y, it.Z);
        _nodes[i].RotationDegrees = new Vector3(0, it.Turn, 0);
        _nodes[i].Scale = Vector3.One * it.Scale;
    }

    private void DrawOutline()
    {
        foreach (Node n in _outline.GetChildren()) n.QueueFree();
        var line = TableLook.Mat(new Color(1, 1, 1), 0.5f, new Color(1, 1, 1), 0.6f);
        if (_piece.Round)
        {
            const int n = 48;
            float r = _piece.W / 2;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.Tau / n, b = (i + 1) * Mathf.Tau / n;
                Segment(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r), new Vector2(Mathf.Cos(b) * r, Mathf.Sin(b) * r), line);
            }
        }
        else
        {
            float hw = _piece.W / 2, hd = _piece.D / 2;
            var c = new[] { new Vector2(-hw, -hd), new Vector2(hw, -hd), new Vector2(hw, hd), new Vector2(-hw, hd) };
            for (int i = 0; i < 4; i++) Segment(c[i], c[(i + 1) % 4], line);
        }
        if (_world.FindChild("ScaleFigure", false, false) is Node3D fig) fig.Position = new Vector3(_piece.W / 2 + 1.5f, 0, _piece.D / 2 + 0.5f);
        // The picked model's footprint, so it's clear which one the keys move.
        if (_sel >= 0 && _sel < _nodes.Count && AssetLoader.Bounds(_nodes[_sel], _world) is { } box)
        {
            var gold = TableLook.Mat(new Color(1, 0.8f, 0.2f), 0.5f, new Color(1, 0.8f, 0.2f), 1f);
            var p = box.Position; var s = box.Size;
            var q = new[] { new Vector2(p.X, p.Z), new Vector2(p.X + s.X, p.Z), new Vector2(p.X + s.X, p.Z + s.Z), new Vector2(p.X, p.Z + s.Z) };
            for (int i = 0; i < 4; i++) Segment(q[i], q[(i + 1) % 4], gold, 0.06f);
        }
    }

    private void Segment(Vector2 a, Vector2 b, Material mat, float y = 0.04f)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 1e-3f) return;
        var m = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(len, 0.04f, 0.08f) }, MaterialOverride = mat };
        m.Position = new Vector3((a.X + b.X) / 2, y, (a.Y + b.Y) / 2);
        m.Rotation = new Vector3(0, Mathf.Atan2(-d.Y, d.X), 0);
        _outline.AddChild(m);
    }

    private void ApplyCamera()
    {
        float p = Mathf.DegToRad(_pitch), y = Mathf.DegToRad(_yaw);
        _cam.Position = _focus + new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p)) * _dist;
        _cam.LookAt(_focus, Vector3.Up);
        Horizon.FollowCamera(_world, _dist);
    }

    private Vector2 ViewPos(Vector2 local) => local * new Vector2(_view.Size.X / Mathf.Max(1, _box.Size.X), _view.Size.Y / Mathf.Max(1, _box.Size.Y));

    private Vector3? GroundAt(Vector2 local)
    {
        Vector2 at = ViewPos(local);
        Vector3 o = _cam.ProjectRayOrigin(at), d = _cam.ProjectRayNormal(at);
        if (Mathf.Abs(d.Y) < 1e-4f) return null;
        float t = -o.Y / d.Y;
        return t > 0 ? o + d * t : null;
    }

    private int PickAt(Vector2 local)
    {
        Vector2 at = ViewPos(local);
        Vector3 o = _cam.ProjectRayOrigin(at), d = _cam.ProjectRayNormal(at);
        int best = -1;
        float bestT = float.MaxValue;
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (AssetLoader.Bounds(_nodes[i], _world) is not { } box) continue;
            if (RayBox(o, d, box) is float t && t < bestT) { bestT = t; best = i; }
        }
        return best;
    }

    private static float? RayBox(Vector3 o, Vector3 d, Aabb b)
    {
        float t0 = 0, t1 = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float inv = 1f / (Mathf.Abs(d[a]) < 1e-8f ? 1e-8f : d[a]);
            float n = (b.Position[a] - o[a]) * inv, f = (b.End[a] - o[a]) * inv;
            if (n > f) (n, f) = (f, n);
            t0 = Mathf.Max(t0, n); t1 = Mathf.Min(t1, f);
            if (t0 > t1) return null;
        }
        return t0;
    }

    private void OnViewInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { _dist = Mathf.Max(4, _dist * 0.9f); ApplyCamera(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { _dist = Mathf.Min(120, _dist * 1.1f); ApplyCamera(); }
            else if (mb.ButtonIndex == MouseButton.Right) _orbiting = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.Middle) _panning = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                _box.GrabFocus();
                if (mb.Pressed)
                {
                    int hit = PickAt(mb.Position);
                    Select(hit);
                    if (hit >= 0 && GroundAt(mb.Position) is { } g)
                    {
                        _dragging = true;
                        _dragOffset = new Vector2(_piece.Items[hit].X - g.X, _piece.Items[hit].Z - g.Z);
                    }
                }
                else _dragging = false;
            }
            _box.AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (_orbiting) { _yaw -= mm.Relative.X * 0.4f; _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.3f, 8, 88); ApplyCamera(); }
            else if (_panning)
            {
                float yr = Mathf.DegToRad(_yaw);
                Vector3 right = new(Mathf.Cos(yr), 0, -Mathf.Sin(yr)), fwd = new(Mathf.Sin(yr), 0, Mathf.Cos(yr));
                _focus -= (right * mm.Relative.X + fwd * mm.Relative.Y) * _dist * 0.0015f;
                ApplyCamera();
            }
            else if (_dragging && _sel >= 0 && GroundAt(mm.Position) is { } g)
            {
                var it = _piece.Items[_sel];
                it.X = Snap(g.X + _dragOffset.X);
                it.Z = Snap(g.Z + _dragOffset.Y);
                Touch();
                Reposition(_sel);
                DrawOutline();
            }
        }
        else if (e is InputEventKey { Pressed: true } k)
        {
            if (HandleKey(k)) _box.AcceptEvent();
        }
    }

    private static float Snap(float v) => Mathf.Round(v * 10) / 10;

    private bool HandleKey(InputEventKey k)
    {
        if (k.Keycode == Key.S && k.CtrlPressed) { Save(); return true; }
        if (_sel < 0) return false;
        var it = _piece.Items[_sel];
        float turn = k.ShiftPressed ? 5 : 15;
        switch (k.Keycode)
        {
            case Key.Q: it.Turn -= turn; break;
            case Key.E: it.Turn += turn; break;
            case Key.Bracketleft: it.Scale = Mathf.Max(0.01f, it.Scale / 1.1f); break;
            case Key.Bracketright: it.Scale *= 1.1f; break;
            case Key.Pageup: it.Y += 0.1f; break;
            case Key.Pagedown: it.Y -= 0.1f; break;
            case Key.Delete: RemoveSel(); return true;
            case Key.D when k.CtrlPressed: DuplicateSel(); return true;
            default: return false;
        }
        Touch();
        Reposition(_sel);
        Select(_sel);
        return true;
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (!IsVisibleInTree()) return;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { Leave(); GetViewport().SetInputAsHandled(); }
        else if (e is InputEventKey { Pressed: true } k && HandleKey(k)) GetViewport().SetInputAsHandled();
    }

    /// <summary>Test driver (ScriptedRun "ws:"): add:&lt;model name part&gt;@x;z;turn;scale | name:&lt;text&gt; | kind:&lt;Kind&gt; | size:w;d | save | pick:&lt;n&gt; | delete.</summary>
    internal void Drive(string step)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string arg = step.Contains(':') ? step[(step.IndexOf(':') + 1)..] : "";
        switch (step.Split(':')[0])
        {
            case "add":
                string[] at = arg.Split('@');
                var m = PieceModels.Catalogue.FirstOrDefault(c => c.Style == _style && (c.Name.Contains(at[0], StringComparison.OrdinalIgnoreCase) || c.Path.Contains(at[0], StringComparison.OrdinalIgnoreCase)));
                if (m == null) { GD.Print($"Workshop: no model like {at[0]}"); return; }
                var it = new TerrainPieces.Item { Model = m.Path };
                if (at.Length > 1)
                {
                    float[] v = at[1].Split(';').Select(x => float.Parse(x, inv)).ToArray();
                    it.X = v[0]; it.Z = v[1];
                    if (v.Length > 2) it.Turn = v[2];
                    if (v.Length > 3) it.Scale = v[3];
                }
                _piece.Items.Add(it);
                Touch();
                Rebuild();
                Select(_piece.Items.Count - 1);
                break;
            case "name": _name.Text = arg; _piece.Name = arg; Touch(); break;
            case "kind": _kindOpt.Selected = Math.Max(0, Array.IndexOf(Kinds, arg)); _piece.Kind = arg; Touch(); break;
            case "size": var wd = arg.Split(';'); _round.ButtonPressed = false; _piece.Round = false; _w.Value = double.Parse(wd[0], inv); _d.Value = double.Parse(wd[1], inv); break;
            case "style": SwitchStyle(arg); break;
            case "save": Save(); break;
            case "open" when TerrainPieces.All.FirstOrDefault(x => x.Id == arg) is { } op:
                if (op.Style != _style) SwitchStyle(op.Style);
                Load(op.Copy());
                break;
            case "view": var cv = arg.Split(';').Select(x => float.Parse(x, inv)).ToArray(); _yaw = cv[0]; _pitch = cv[1]; _dist = cv[2]; ApplyCamera(); break;
            case "pick": Select(int.Parse(arg)); break;
            case "delete": TerrainPieces.Delete(_piece); Load(NewPiece(_style)); break;
        }
        GD.Print($"Workshop: {step} -> {_status.Text}");
    }

    // ------------------------------------------------------------------ small helpers

    private void Say(string text) => _status.Text = text;

    private static string KindLabel(string k) => k switch
    {
        "LakeShallow" => "Lake (shallow)", "LakeDeep" => "Lake (deep)", "RiverShallow" => "River (shallow)", "RiverDeep" => "River (deep)",
        _ => k,
    };

    private static SpinBox Spin(string name, double min, double max, double step, Action<double> changed)
    {
        var s = new SpinBox { Name = name, MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(110, 0) };
        s.ValueChanged += v => changed(v);
        return s;
    }

    private static Control Labelled(string label, Control field, string? tip = null)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var l = HudKit.Text(label, 13, HudKit.Dim, HudKit.Semi);
        if (tip != null) { l.TooltipText = tip; l.MouseFilter = MouseFilterEnum.Stop; field.TooltipText = tip; }
        box.AddChild(l);
        box.AddChild(field);
        return box;
    }
}
