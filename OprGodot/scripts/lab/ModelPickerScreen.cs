using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// "Choose models..." (Kai, 2026-11-12: let players pick which models to download, with a preview).
///
/// <para>
/// Left: an army, its units, and under each unit its versions with a tick box. Click a version and it is fetched (a few
/// MB, kept in the download cache) and shown turning on the table beside a 28mm person. Tick the ones you want and
/// press "Download ticked": the Download models window opens over this screen and saves exactly those. Models already
/// downloaded show as such and can't be ticked again. Choices are not remembered as anyone's default (Kai's call).
/// </para>
/// </summary>
public partial class ModelPickerScreen : Control, IOwnBackdrop
{
    private ForceOrgCatalog _cat = null!;
    private UnitModelMap _map = null!;
    private string? _startBook, _catPath, _mapPath;

    private Node3D _world = null!, _stage = null!, _refs = null!;
    private Camera3D _camera = null!;
    private OptionButton _book = null!;
    private Tree _tree = null!;
    private Label _title = null!, _info = null!, _ticked = null!, _loading = null!;
    private Button _download = null!;

    private readonly HashSet<(string Book, string Unit, int Id)> _tickedSet = new();
    private readonly Dictionary<int, ForceOrgAssembler.Result> _previews = new();
    private readonly List<int> _previewOrder = new();
    private ModelDownloader? _dl;
    private int _showing = -1;
    private float _spin;
    private float _distance = 6, _focusY = 0.8f;

    public void Setup(ForceOrgCatalog cat, UnitModelMap map, string? book, string? catPath, string? mapPath)
    {
        _cat = cat; _map = map; _startBook = book; _catPath = catPath; _mapPath = mapPath;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _dl = new ModelDownloader();
        _world = new Node3D { Name = "PickerWorld" };
        AddChild(_world);
        TableLook.AddLighting(_world);
        TableLook.AddBoard(_world, 72, 48);
        _stage = new Node3D { Name = "Stage" };
        _world.AddChild(_stage);
        _refs = new Node3D { Name = "Refs" };
        _world.AddChild(_refs);
        _camera = new Camera3D { Fov = 40, Current = true, Far = 2000, Near = 0.05f };
        _world.AddChild(_camera);
        _camera.MakeCurrent();
        Person();
        BuildHud();
        ApplyCamera();
    }

    public override void _ExitTree()
    {
        _dl?.Dispose();
        foreach (var r in _previews.Values) r.Root?.QueueFree();
    }

    // ================================================================== HUD

    private void BuildHud()
    {
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(100, 40), TooltipText = "Back to the Asset Lab (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("CHOOSE MODELS", 32, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text("Click a version to see it; tick the ones to download", 14, HudKit.Dim, HudKit.Semi));
        head.AddChild(t);
        left.AddChild(head);

        _book = new OptionButton { Name = "Book", FocusMode = FocusModeEnum.None, TooltipText = "The army to choose models for." };
        foreach (string b in _map.Books) _book.AddItem(b);
        int start = _startBook == null ? 0 : Math.Max(0, _map.Books.ToList().IndexOf(_startBook));
        _book.Select(start);
        _book.ItemSelected += _ => FillTree();
        left.AddChild(_book);

        _tree = new Tree { Name = "Units", HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 400), SelectMode = Tree.SelectModeEnum.Single };
        _tree.ItemSelected += OnSelected;
        _tree.ItemEdited += OnTicked;
        left.AddChild(_tree);

        var tickRow = new HBoxContainer();
        tickRow.AddThemeConstantOverride("separation", 6);
        tickRow.AddChild(Btn("TickUnit", "Tick all of this unit", "Tick every version of the unit you're on.", TickUnit));
        tickRow.AddChild(Btn("ClearTicks", "Clear ticks", "Untick everything.", ClearTicks));
        left.AddChild(tickRow);
        _ticked = HudKit.Text("", 15, HudKit.Lime, HudKit.Semi);
        left.AddChild(_ticked);
        _download = Btn("DownloadTicked", "Download ticked", "Save every ticked model under its OPR unit name.", DownloadTicked);
        _download.CustomMinimumSize = new Vector2(0, 44);
        left.AddChild(_download);

        var panel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(left, 16, 14, 16, 14));
        panel.AnchorTop = 0; panel.AnchorBottom = 1; panel.AnchorLeft = 0; panel.AnchorRight = 0;
        panel.OffsetLeft = 16; panel.OffsetTop = 16; panel.OffsetBottom = -16; panel.OffsetRight = 476;
        AddChild(panel);

        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 4);
        _title = HudKit.Text("Pick a version on the left", 24, HudKit.Fg, HudKit.Bold);
        _info = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _info.CustomMinimumSize = new Vector2(420, 0);
        info.AddChild(_title);
        info.AddChild(_info);
        HudKit.Place(this, HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(info, 16, 12, 16, 12)), LayoutPreset.TopRight, new Vector2(-16, 16));

        _loading = HudKit.Text("Fetching...", 28, HudKit.Lime, HudKit.Bold, HorizontalAlignment.Center);
        _loading.Visible = false;
        HudKit.Place(this, _loading, LayoutPreset.Center, new Vector2(240, 0));

        FillTree();
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    private string Book => _book.GetItemText(_book.Selected);

    private void FillTree()
    {
        _tree.Clear();
        TreeItem root = _tree.CreateItem();
        foreach (var u in _map.ForBook(Book))
        {
            TreeItem unit = _tree.CreateItem(root);
            unit.SetText(0, $"{u.Unit}   ({u.Models.Count} version{(u.Models.Count == 1 ? "" : "s")})");
            unit.SetTooltipText(0, $"Wiki: {u.Warhammer}");
            unit.SetMetadata(0, u.Unit);
            unit.Collapsed = true;
            foreach (var m in u.Models)
            {
                TreeItem v = _tree.CreateItem(unit);
                bool have = File.Exists(UnitModelMap.FileFor(UnitModelPaths.Root, Book, u.Unit, m));
                v.SetCellMode(0, TreeItem.TreeCellMode.Check);
                v.SetText(0, $"{m.Name}  #{m.Id}{(have ? "   - downloaded" : "")}");
                v.SetEditable(0, !have);
                v.SetChecked(0, have || _tickedSet.Contains((Book, u.Unit, m.Id)));
                v.SetMetadata(0, m.Id);
                if (have) v.SetCustomColor(0, HudKit.Dim);
            }
        }
        UpdateTicked();
    }

    // ================================================================== ticks

    private (UnitModelMap.Entry Unit, UnitModelMap.ModelRef Model)? Lookup(TreeItem item)
    {
        if (item.GetParent() is not { } parent || parent == _tree.GetRoot()) return null;
        string unit = parent.GetMetadata(0).AsString();
        int id = item.GetMetadata(0).AsInt32();
        var e = _map.ForBook(Book).FirstOrDefault(x => x.Unit == unit);
        var m = e?.Models.FirstOrDefault(x => x.Id == id);
        return e != null && m != null ? (e, m) : null;
    }

    private void OnTicked()
    {
        if (_tree.GetEdited() is not { } item || Lookup(item) is not { } l) return;
        var key = (Book, l.Unit.Unit, l.Model.Id);
        if (item.IsChecked(0)) _tickedSet.Add(key); else _tickedSet.Remove(key);
        UpdateTicked();
    }

    private void TickUnit()
    {
        TreeItem? sel = _tree.GetSelected();
        TreeItem? unit = sel == null ? null : sel.GetParent() == _tree.GetRoot() ? sel : sel.GetParent();
        if (unit == null) return;
        foreach (TreeItem v in unit.GetChildren())
            if (v.IsEditable(0) && Lookup(v) is { } l)
            {
                v.SetChecked(0, true);
                _tickedSet.Add((Book, l.Unit.Unit, l.Model.Id));
            }
        UpdateTicked();
    }

    private void ClearTicks()
    {
        _tickedSet.Clear();
        FillTree();
    }

    private void UpdateTicked()
    {
        int n = _tickedSet.Count;
        // ~21 MB per finished model, from Kai's Alien Hives download.
        _ticked.Text = n == 0 ? "Nothing ticked yet." : $"{n} ticked in {_tickedSet.Select(t => t.Book).Distinct().Count()} army(ies) - about {n * 21} MB";
        _download.Disabled = n == 0;
    }

    private void DownloadTicked()
    {
        var jobs = new List<(string, UnitModelMap.Entry, UnitModelMap.ModelRef, string)>();
        foreach (var (book, unit, id) in _tickedSet)
        {
            var e = _map.ForBook(book).FirstOrDefault(x => x.Unit == unit);
            var m = e?.Models.FirstOrDefault(x => x.Id == id);
            if (e == null || m == null) continue;
            string path = UnitModelMap.FileFor(UnitModelPaths.Root, book, unit, m);
            if (!File.Exists(path)) jobs.Add((book, e, m, path));
        }
        var popup = ModelDownloadPopup.Open(this, _catPath, _mapPath);
        popup.TreeExited += () => { if (IsInsideTree()) { _tickedSet.Clear(); FillTree(); } };
        popup.RunJobs(jobs, $"{jobs.Count} chosen model(s)");
    }

    /// <summary>Screenshot driver: select a unit's nth version (0 = first).</summary>
    public void SelectVersion(string unit, int n)
    {
        foreach (TreeItem u in _tree.GetRoot().GetChildren())
            if (u.GetMetadata(0).AsString() == unit && n < u.GetChildCount())
            {
                u.Collapsed = false;
                u.GetChild(n).Select(0);
                return;
            }
    }

    public void TickSelectedUnit() => TickUnit();

    // ================================================================== preview

    private void OnSelected()
    {
        if (_tree.GetSelected() is not { } item) return;
        if (Lookup(item) is not { } l) { item.Collapsed = !item.Collapsed; return; }
        _ = Preview(l.Unit, l.Model);
    }

    private async Task Preview(UnitModelMap.Entry unit, UnitModelMap.ModelRef mref)
    {
        _showing = mref.Id;
        _title.Text = mref.Name;
        _info.Text = $"{Book}  -  {unit.Unit}\nWiki: {unit.Warhammer}";
        string saved = UnitModelMap.FileFor(UnitModelPaths.Root, Book, unit.Unit, mref);
        Clear();

        if (File.Exists(saved))
        {
            // Already downloaded: show the saved file with its own settings (its facing too).
            LoadedAsset a = AssetLoader.Load(saved);
            if (a.Model != null) Show(a.Model, UnitModelSettings.Load(saved) ?? new UnitModelSettings { HeightMm = 0 }, $"{a.Triangles:N0} triangles - already downloaded");
            return;
        }
        if (!_previews.TryGetValue(mref.Id, out var r))
        {
            if (!_cat.ById.TryGetValue(mref.Id, out var model)) { _info.Text += "\nNot in this catalogue."; return; }
            _loading.Visible = true;
            Dictionary<string, string> files;
            List<string> failed;
            try { (files, failed) = await _dl!.FetchModelAsync(model, CancellationToken.None); }
            catch (Exception e) { _loading.Visible = false; _info.Text += "\nCould not fetch: " + e.Message; return; }
            _loading.Visible = false;
            if (_showing != mref.Id) return; // clicked on to another one meanwhile
            r = ForceOrgAssembler.Assemble(model, files);
            if (r.Root == null) { _info.Text += "\nCould not show it: " + (r.Error ?? string.Join("; ", failed)); return; }
            _previews[mref.Id] = r;
            _previewOrder.Add(mref.Id);
            while (_previewOrder.Count > 8) { int old = _previewOrder[0]; _previewOrder.RemoveAt(0); if (_previews.Remove(old, out var gone)) gone.Root?.QueueFree(); }
        }
        var s = new UnitModelSettings { HeightMm = 0, BaseMm = r.BaseMm ?? 25, YawDegrees = ModelDownloadDefaults.Load().YawDegrees, CentreOnBase = false };
        Show(r.Root!, s, $"{r.Triangles:N0} triangles, {(r.BaseMm ?? 25):0.#} mm base");
    }

    private void Clear()
    {
        foreach (Node c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
    }

    private void Show(Node3D source, UnitModelSettings s, string line)
    {
        Clear();
        var holder = new Node3D();
        _stage.AddChild(holder);
        if (s.BaseMm > 0) TableLook.AddBase(holder, s.BaseMm / UnitModelSettings.MmPerInch / 2, HudKit.Lime);
        var pivot = new Node3D();
        holder.AddChild(pivot);
        var body = (Node3D)source.Duplicate();
        pivot.AddChild(body);
        var (_, box) = ModelPlacement.Place(pivot, body, s);
        _info.Text += $"\n{line}\nStands {box.Size.Y * UnitModelSettings.MmPerInch:0} mm tall";
        _focusY = Mathf.Max(0.5f, box.Size.Y * 0.45f);
        _distance = Mathf.Max(4.5f, Mathf.Max(box.Size.Y, Mathf.Max(box.Size.X, box.Size.Z)) * 2.4f + 2f);
        ApplyCamera();
    }

    /// <summary>A 28mm person (32mm to the top of the head) to judge size against.</summary>
    private void Person()
    {
        float baseR = 25 / UnitModelSettings.MmPerInch / 2, hIn = 32 / UnitModelSettings.MmPerInch;
        var man = new Node3D { Position = new Vector3(-2.2f, 0, 0) };
        _refs.AddChild(man);
        TableLook.AddBase(man, baseR, new Color(0.5f, 0.5f, 0.5f));
        var m = TableLook.Mat(new Color(0.62f, 0.64f, 0.66f), 0.7f);
        float y0 = TableLook.BaseTop, head = hIn * 0.13f, legs = hIn * 0.47f, torso = hIn - head * 2 - legs;
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, m, new Vector3(-0.08f, y0 + legs / 2, 0));
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, m, new Vector3(0.08f, y0 + legs / 2, 0));
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.17f, Height = torso + 0.1f }, m, new Vector3(0, y0 + legs + torso / 2, 0));
        TableLook.Mesh(man, new SphereMesh { Radius = head, Height = head * 2 }, m, new Vector3(0, y0 + hIn - head, 0));
    }

    private void ApplyCamera()
    {
        // Looks from the front-right; the panel on the left covers some of the view, so the model sits right of centre.
        Vector3 focus = new(-1.2f, _focusY, 0);
        Vector3 dir = new Vector3(0.35f, 0.42f, 1f).Normalized();
        _camera.Position = focus + dir * _distance;
        _camera.LookAt(focus, Vector3.Up);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (GetViewport().GuiGetHoveredControl() != this) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }: _distance = Mathf.Max(1.5f, _distance * 0.9f); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }: _distance = Mathf.Min(80f, _distance * 1.1f); break;
            default: return;
        }
        ApplyCamera();
        AcceptEvent();
    }

    public override void _Process(double delta)
    {
        // Turntable, so every side shows.
        _spin += (float)delta * 0.5f;
        _stage.Rotation = new Vector3(0, _spin, 0);
    }
}
