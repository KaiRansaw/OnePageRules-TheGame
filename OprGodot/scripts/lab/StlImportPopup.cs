using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// "Import a printable model" (Kai, 2026-11-15): pick a figure STL, pick its base from the files beside it, see it,
/// and save it to a unit. After that the unit's models can wear it in Army Forge, like any downloaded model.
/// (Colouring it area by area was tried in 2.40 and taken out at Kai's word the same evening.)
/// </summary>
public partial class StlImportPopup : Control
{
    /// <summary>Raised with the saved file, so the lab can show it.</summary>
    public event Action<string>? Saved;

    private string? _figure;
    private List<string> _bases = new();
    private StlKit? _kit;
    private Task<StlKit>? _work;
    private volatile string _progress = "";

    private Label _figureLabel = null!, _status = null!;
    private OptionButton _base = null!, _book = null!, _unit = null!;
    private LineEdit _name = null!;
    private Button _save = null!;
    private Node3D _stage = null!;
    private Camera3D _cam = null!;
    private float _spin;
    private SortedDictionary<string, List<string>> _books = new();
    private static string? _lastBook, _lastUnit;

    public static StlImportPopup Open(Control parent, string? figure = null)
    {
        var p = new StlImportPopup();
        p.SetAnchorsPreset(LayoutPreset.FullRect);
        parent.AddChild(p);
        if (figure != null) p.UseFigure(figure);
        return p;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(veil);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 18);

        // ---- left: the files, the areas, where it goes
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        col.AddThemeConstantOverride("separation", 9);
        col.AddChild(HudKit.Text("IMPORT A PRINTABLE MODEL", 28, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(Wrapped("A 3D-printing model (.stl) and its base, made light enough for the table and saved to a unit. Done once - after that it opens at once.", 14, HudKit.Dim));

        col.AddChild(HudKit.Heading("Figure", 15));
        var figRow = new HBoxContainer();
        figRow.AddThemeConstantOverride("separation", 10);
        _figureLabel = HudKit.Text("No file chosen yet.", 14, HudKit.Fg, HudKit.Medium);
        _figureLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _figureLabel.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        figRow.AddChild(_figureLabel);
        figRow.AddChild(Btn("ChooseFigure", "Choose file...", "Pick the figure's .stl. Its base files are found beside it.", ChooseFigure));
        col.AddChild(figRow);

        col.AddChild(HudKit.Heading("Base", 15));
        _base = new OptionButton { Name = "BasePiece", TooltipText = "The kit's own base to stand the figure on, or none (the game's plain base only)." };
        _base.ItemSelected += _ => StartPrepare();
        col.AddChild(_base);

        col.AddChild(HudKit.Heading("Save it for", 15));
        _book = new OptionButton { Name = "Book", TooltipText = "The army book whose unit this model stands for." };
        _book.ItemSelected += _ => FillUnits();
        col.AddChild(_book);
        _unit = new OptionButton { Name = "Unit", TooltipText = "The unit. The model becomes one of its looks in Army Forge." };
        _unit.ItemSelected += _ => _lastUnit = _unit.GetItemText(_unit.Selected);
        col.AddChild(_unit);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 10);
        nameRow.AddChild(HudKit.Text("Name", 15, HudKit.Dim, HudKit.Semi));
        _name = new LineEdit { Name = "ModelName", SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "What this look is called in Army Forge" };
        nameRow.AddChild(_name);
        col.AddChild(nameRow);

        _status = HudKit.Text("", 15, HudKit.Fg, HudKit.Semi);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_status);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        _save = Btn("SaveImport", "Save", "Save the model into the unit's folder. It shows in Army Forge's looks for that unit.", Save);
        _save.Disabled = true;
        buttons.AddChild(_save);
        buttons.AddChild(Btn("CloseImport", "Close", "Close this window.", QueueFree));
        col.AddChild(buttons);
        cols.AddChild(col);

        // ---- right: the model, turning
        var view = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(560, 620) };
        var vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
        view.AddChild(vp);
        var world = new Node3D();
        vp.AddChild(world);
        TableLook.AddLighting(world);
        _stage = new Node3D();
        world.AddChild(_stage);
        _cam = new Camera3D { Fov = 35, Near = 1f, Far = 2000, Current = true };
        world.AddChild(_cam);
        cols.AddChild(view);

        var panel = HudKit.Panel(HudKit.ModalBox(14), HudKit.Pad(cols, 22, 18, 22, 18));
        HudKit.Place(this, panel, LayoutPreset.Center);

        try { _books = GfBookUnits.LoadAll(); } catch (Exception e) { _status.Text = "The army books could not be read: " + e.Message; }
        foreach (string b in _books.Keys) _book.AddItem(b);
        int at = _lastBook != null ? _books.Keys.ToList().IndexOf(_lastBook) : -1;
        if (at >= 0) _book.Select(at);
        FillUnits();
        if (_figure != null) ShowFigure();
    }

    private void FillUnits()
    {
        _unit.Clear();
        if (_book.Selected < 0) return;
        _lastBook = _book.GetItemText(_book.Selected);
        if (!_books.TryGetValue(_lastBook, out var units)) return;
        foreach (string u in units) _unit.AddItem(u);
        int at = _lastUnit != null ? units.IndexOf(_lastUnit) : -1;
        _unit.Select(at >= 0 ? at : 0);
    }

    private void ChooseFigure()
    {
        var dlg = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.stl ; 3D printing model" },
            Title = "Choose the figure's .stl", UseNativeDialog = true,
        };
        dlg.FileSelected += path => { UseFigure(path); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered(new Vector2I(900, 600));
    }

    /// <summary>Takes this figure (a base file picked instead finds its figure: the first non-base file beside it).</summary>
    public void UseFigure(string path)
    {
        if (StlKit.IsBaseFile(path))
        {
            string? dir = Path.GetDirectoryName(path);
            string? fig = dir == null ? null : Directory.EnumerateFiles(dir, "*.stl").FirstOrDefault(f => !StlKit.IsBaseFile(f));
            if (fig != null) path = fig;
        }
        _figure = path;
        if (IsNodeReady()) ShowFigure();
    }

    private void ShowFigure()
    {
        if (_figure == null) return;
        _figureLabel.Text = _figure;
        _name.Text = Path.GetFileNameWithoutExtension(_figure).Replace('_', ' ');
        _bases = StlKit.BasesBeside(_figure);
        _base.Clear();
        _base.AddItem("None - the game's plain base only");
        foreach (string b in _bases) _base.AddItem(Path.GetRelativePath(Path.GetDirectoryName(_figure)!, b));
        // The scenic base when there is one, else the first.
        int pick = _bases.FindIndex(b => b.Contains("scenic", StringComparison.OrdinalIgnoreCase));
        _base.Select(_bases.Count == 0 ? 0 : (pick >= 0 ? pick : 0) + 1);
        StartPrepare();
    }

    /// <summary>Reads and lightens the files in the background; <see cref="_Process"/> shows it when ready.</summary>
    private void StartPrepare()
    {
        if (_figure == null || _work is { IsCompleted: false }) return;
        string figure = _figure;
        string? basePiece = _base.Selected > 0 ? _bases[_base.Selected - 1] : null;
        _kit = null;
        _save.Disabled = true;
        foreach (Node c in _stage.GetChildren()) c.QueueFree();
        _progress = "Starting...";
        _work = Task.Run(() => StlKit.Prepare(figure, basePiece, p => _progress = p));
    }

    public override void _Process(double delta)
    {
        if (_work != null)
        {
            if (!_work.IsCompleted) { _status.Text = _progress + "  (a detailed model takes up to a minute - only this once)"; _status.Modulate = Colors.White; }
            else
            {
                var w = _work;
                _work = null;
                if (w.IsFaulted) { _status.Text = "Could not read it: " + (w.Exception?.InnerException?.Message ?? "unknown error"); _status.Modulate = Palette.WoundsRed; }
                else { _kit = w.Result; ShowKit(); }
            }
        }
        _spin += (float)delta * 0.4f;
        _stage.Rotation = new Vector3(0, _spin, 0);
    }

    private void ShowKit()
    {
        if (_kit == null || _stage == null) return;
        foreach (Node c in _stage.GetChildren()) c.QueueFree();
        Node3D model = _kit.Build();
        var b = _kit.Light.Bounds();
        // Centred on the turntable, feet on the floor.
        model.Position = new Vector3(-(b.X0 + b.X1) / 2, -b.Y0, -(b.Z0 + b.Z1) / 2);
        _stage.AddChild(model);
        float h = b.Y1 - b.Y0, size = Math.Max(h, Math.Max(b.X1 - b.X0, b.Z1 - b.Z0));
        _cam.Position = new Vector3(0, h * 0.75f, size * 2.3f);
        _cam.LookAt(new Vector3(0, h * 0.45f, 0), Vector3.Up);
        _status.Text = $"Ready: {_kit.FullTriangles:N0} triangles made {_kit.Light.Triangles:N0}, {h:0} mm tall"
                       + (_kit.BaseWidthMm > 0 ? $", base {_kit.BaseWidthMm:0} mm across." : ".");
        _status.Modulate = Palette.Lime;
        _save.Disabled = false;
    }

    private void Save()
    {
        if (_kit == null || _book.Selected < 0 || _unit.Selected < 0) return;
        string name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : Path.GetFileNameWithoutExtension(_kit.FigureFile);
        string path = Path.Combine(UnitModelPaths.Root, UnitModelMap.SafeName(_book.GetItemText(_book.Selected)),
            UnitModelMap.SafeName(_unit.GetItemText(_unit.Selected)), UnitModelMap.SafeName(name) + ".glb");
        string? error = _kit.Save(path);
        if (error != null) { _status.Text = error; _status.Modulate = Palette.WoundsRed; return; }
        ForgeWidgets.ForgetModels();
        _status.Text = $"Saved for {_unit.GetItemText(_unit.Selected)}. In Army Forge: that unit -> Choose looks -> pick \"{name}\".";
        _status.Modulate = Palette.Lime;
        Saved?.Invoke(path);
    }

    /// <summary>Screenshot driver: chooses the base (index in the list, 0 = none), the army and unit.</summary>
    public void Drive(int? basePick, string? book, string? unit)
    {
        if (basePick is int bp && bp < _base.ItemCount) { _base.Select(bp); StartPrepare(); }
        if (book != null && _books.Keys.ToList().IndexOf(book) is int bi and >= 0) { _book.Select(bi); FillUnits(); }
        if (unit != null) for (int i = 0; i < _unit.ItemCount; i++) if (_unit.GetItemText(i) == unit) _unit.Select(i);
    }

    public void DriveSave() => Save();

    /// <summary>Still reading or lightening the files.</summary>
    public bool Busy => _work != null;

    private static Label Wrapped(string s, int size, Color colour)
    {
        var l = HudKit.Text(s, size, colour, HudKit.Semi);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(130, 38), FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }
}
