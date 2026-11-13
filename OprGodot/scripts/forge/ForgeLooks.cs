using FdgRaylib.Forge;
using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;
using OprGodot.Lab;

namespace OprGodot.Forge;

/// <summary>
/// Choosing each model's look (mockup screens B / B2, "Way 1 - every model, select several"; Kai, 2026-11-12).
/// Every model of the unit is a slot, rows of five - a combined unit's both halves. Click a slot to select it,
/// Shift+click or drag across to select several, or Select all; then click a version to give it to every selected
/// model. Only downloaded versions are listed. Models nobody picks use the unit's first version on the table.
/// The turning preview on the right shows the version under the mouse / last picked, beside a 28mm person.
/// </summary>
public partial class ArmyForgeScreen
{
    private int _looksRow;
    private readonly SortedSet<int> _selected = new();
    private int _anchor = -1;
    private bool _dragSelecting;
    private LookPreview? _preview;
    private string? _previewFile;

    private Control BuildLooks()
    {
        var s = _s!;
        int row = _looksRow;
        var unit = s.Detail(row).Unit;
        var slots = s.Slots(row);
        IReadOnlyList<string> versions = s.Versions(Models(), unit.Name);
        _selected.RemoveWhere(i => i >= slots.Count);

        string armyName = string.IsNullOrWhiteSpace(s.List.Name) ? s.Book.Name : $"{s.Book.Name} - {s.List.Name}";
        TopTitle(armyName);
        _top.AddChild(Ui.Grow());
        _top.AddChild(TopButton("Save", "Save", "Name the army and save it.", OpenSave));

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 16);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 10);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var title = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeConstantOverride("separation", -4);
        title.AddChild(HudKit.Text(unit.Name.ToUpperInvariant() + (slots.Count > unit.ModelCount ? " (COMBINED)" : ""), 26, HudKit.Fg, HudKit.Bold));
        title.AddChild(HudKit.Text($"{slots.Count} models - {_selected.Count} selected", 16, HudKit.Dim, HudKit.Semi));
        head.AddChild(title);
        head.AddChild(Ui.Button("Select all", () => { SelectAll(); Rebuild(); }, "Select every model.").Also(b => b.Name = "SelectAll"));
        head.AddChild(Ui.Button("Clear", () => { _selected.Clear(); Rebuild(); }, "Select none.").Also(b => b.Name = "ClearSel"));
        var done = Ui.Button("Done", () => { _view = View.Main; Rebuild(); }, "Back to the army (Esc).", "CommitButton");
        done.Name = "Done";
        done.CustomMinimumSize = new Vector2(120, 42);
        head.AddChild(done);
        left.AddChild(head);

        left.AddChild(HudKit.Heading("Models - click one, Shift+click or drag across for several", 15));
        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        for (int i = 0; i < slots.Count; i++) grid.AddChild(Slot(i, slots[i], versions));
        var gs = new ScrollContainer { CustomMinimumSize = new Vector2(0, Math.Min(4, (slots.Count + 4) / 5) * 92 + 8), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        gs.AddChild(grid);
        left.AddChild(gs);

        var special = _selected.Where(i => slots[i].Special.Count > 0).Select(i => $"Model {i + 1} carries the {string.Join(" and ", slots[i].Special)}.").ToList();
        if (special.Count == 0) special = slots.Select((sl, i) => (sl, i)).Where(p => p.sl.Special.Count > 0).Take(3).Select(p => $"Model {p.i + 1} carries the {string.Join(" and ", p.sl.Special)}.").ToList();
        foreach (string line in special) left.AddChild(Wrapped(line, 14, Palette.RuleGold));

        left.AddChild(HudKit.Heading($"Versions on your PC - {versions.Count}", 15));
        if (versions.Count == 0)
            left.AddChild(Wrapped("No models are downloaded for this unit yet. Get them in the Asset Lab (Download models / Match models); they show up here.", 15, Palette.HitsYellow));
        var vlist = new VBoxContainer();
        vlist.AddThemeConstantOverride("separation", 4);
        for (int v = 0; v < versions.Count; v++)
        {
            int vi = v;
            string file = versions[v];
            int using_ = slots.Count(sl => ModelLooks.Pick(versions, sl.Look) == file);
            var b = new Button
            {
                Name = $"Version{v + 1}", Text = $"{ModelLooks.Label(versions, file)}{(v == 0 ? "  (first version)" : "")}   -   {using_} model{(using_ == 1 ? "" : "s")}",
                Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34),
                TooltipText = _selected.Count == 0 ? "Select models above first, then click a version to give it to them." : $"Give this look to the {_selected.Count} selected model{(_selected.Count == 1 ? "" : "s")}.",
            };
            if (file == _previewFile) b.AddThemeColorOverride("font_color", Palette.Lime);
            b.Pressed += () => PickVersion(vi + 1);
            b.MouseEntered += () => ShowPreview(file);
            vlist.AddChild(b);
        }
        var vs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        vlist.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vs.AddChild(vlist);
        left.AddChild(vs);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        var all = Ui.Button($"Use this look for all {slots.Count}", () =>
        {
            if (_previewFile == null) return;
            s.SetLook(slots, ModelLooks.NameOf(_previewFile) == ModelLooks.NameOf(versions[0]) ? null : ModelLooks.NameOf(_previewFile));
            Rebuild();
        }, "Give the version in the preview to every model of the unit.");
        all.Name = "UseForAll";
        all.Disabled = _previewFile == null;
        buttons.AddChild(all);
        var reset = Ui.Button("Reset all to first version", ResetLooks, "Every model back to the unit's first version.", "BackButton");
        reset.Name = "ResetLooks";
        buttons.AddChild(reset);
        left.AddChild(buttons);
        left.AddChild(Wrapped("Models you don't pick use the unit's first version. Looks are saved in the army file; a friend sees them if they have those models downloaded.", 13, HudKit.Dim));
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(left, 16, 12, 16, 12)).Also(p => { p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.CustomMinimumSize = new Vector2(620, 0); }));

        // ---- right: the 3D preview
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 6);
        _preview = new LookPreview { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(_preview);
        _previewLabel = HudKit.Text("", 17, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center);
        right.AddChild(_previewLabel);
        right.AddChild(HudKit.Text("Turns slowly - drag to turn it - mouse wheel to zoom - 28mm person for scale", 13, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(right, 8, 8, 8, 8)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));

        string? show = _previewFile != null && versions.Contains(_previewFile) ? _previewFile
            : _selected.Count > 0 ? ModelLooks.Pick(versions, slots[_selected.Min].Look) : versions.FirstOrDefault();
        CallDeferred(nameof(ShowPreviewDeferred), show ?? "");
        return body;
    }

    private Label? _previewLabel;

    private void ShowPreviewDeferred(string file) { if (file.Length > 0) ShowPreview(file); }

    private void ShowPreview(string file)
    {
        if (_preview == null || !IsInstanceValid(_preview)) return;
        _previewFile = file;
        string? line = _preview.Show(file);
        var versions = _s!.Versions(Models(), _s.Detail(_looksRow).Unit.Name);
        if (_previewLabel != null && IsInstanceValid(_previewLabel))
            _previewLabel.Text = ModelLooks.Label(versions, file) + (line != null ? "  -  " + line : "");
    }

    private Control Slot(int i, ForgeSession.ModelSlot slot, IReadOnlyList<string> versions)
    {
        bool sel = _selected.Contains(i);
        var b = new Button { Name = $"Slot{i + 1}", CustomMinimumSize = new Vector2(112, 84), FocusMode = FocusModeEnum.None };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4 };
        if (sel) box = box.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); x.Glow = HudKit.Lime with { A = 0.4f }; });
        HudKit.StyleButton(b, box, HudKit.Fg, 14);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", -2);
        col.AddChild(HudKit.Text((i + 1).ToString(), 20, sel ? HudKit.Lime : HudKit.Fg, HudKit.Bold).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
        string look = versions.Count == 0 ? "token" : slot.Look == null ? "first version" : Path.GetFileNameWithoutExtension(ModelLooks.Pick(versions, slot.Look) ?? slot.Look);
        var lk = HudKit.Text(look, 13, slot.Look == null ? HudKit.Dim : HudKit.Fg, HudKit.Semi);
        lk.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        lk.CustomMinimumSize = new Vector2(96, 0);
        lk.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(lk);
        if (slot.Special.Count > 0)
        {
            var sp = HudKit.Text(string.Join(", ", slot.Special), 12, Palette.RuleGold, HudKit.Semi);
            sp.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            sp.CustomMinimumSize = new Vector2(96, 0);
            sp.MouseFilter = MouseFilterEnum.Ignore;
            col.AddChild(sp);
        }
        b.AddChild(HudKit.Pad(col, 8, 4, 6, 4).Also(p => { p.MouseFilter = MouseFilterEnum.Ignore; p.SetAnchorsPreset(LayoutPreset.FullRect); }));
        b.TooltipText = slot.Special.Count > 0 ? $"Model {i + 1} carries the {string.Join(" and ", slot.Special)}." : $"Model {i + 1}";
        b.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
            {
                if (mb.ShiftPressed && _anchor >= 0)
                    for (int k = Math.Min(_anchor, i); k <= Math.Max(_anchor, i); k++) _selected.Add(k);
                else if (mb.CtrlPressed) { if (!_selected.Remove(i)) _selected.Add(i); }
                else { _selected.Clear(); _selected.Add(i); }
                _anchor = i;
                _dragSelecting = true;
                if (versions.Count > 0 && ModelLooks.Pick(versions, slot.Look) is { } f) _previewFile = f;
                b.AcceptEvent();
                CallDeferred(nameof(Rebuild));
            }
        };
        b.MouseEntered += () =>
        {
            if (_dragSelecting && Input.IsMouseButtonPressed(MouseButton.Left) && _selected.Add(i)) CallDeferred(nameof(Rebuild));
        };
        return b;
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) _dragSelecting = false;
    }

    private void SelectAll()
    {
        _selected.Clear();
        int n = _s!.Slots(_looksRow).Count;
        for (int i = 0; i < n; i++) _selected.Add(i);
    }

    /// <summary>Gives version <paramref name="number"/> (1 = first) to the selected models.</summary>
    private void PickVersion(int number)
    {
        var s = _s!;
        var slots = s.Slots(_looksRow);
        var versions = s.Versions(Models(), s.Detail(_looksRow).Unit.Name);
        if (number < 1 || number > versions.Count) return;
        string file = versions[number - 1];
        _previewFile = file;
        if (_selected.Count == 0)
        {
            Status("Select one or more models first (click a slot, Shift+click, or Select all), then pick a version.", Palette.HitsYellow);
            Rebuild();
            return;
        }
        s.SetLook(_selected.Where(i => i < slots.Count).Select(i => slots[i]), number == 1 ? null : ModelLooks.NameOf(file));
        Status($"{_selected.Count} model{(_selected.Count == 1 ? "" : "s")} now use {ModelLooks.Label(versions, file)}.", Palette.Lime);
        Rebuild();
    }

    private void ResetLooks()
    {
        _s!.SetLook(_s.Slots(_looksRow), null);
        Rebuild();
    }
}

/// <summary>A small 3D stage for one model file: its own world, a felt square, the model on its base turning slowly
/// beside a grey 28mm person. Drag turns it, the wheel zooms. Loaded models are kept while the screen is open.</summary>
public partial class LookPreview : SubViewportContainer
{
    private SubViewport _vp = null!;
    private Node3D _stage = null!;
    private Camera3D _cam = null!;
    private float _spin, _distance = 6, _focusY = 0.8f;
    private bool _dragging;
    private static readonly Dictionary<string, LoadedAsset> Cache = new();

    public override void _Ready()
    {
        Stretch = true;
        CustomMinimumSize = new Vector2(360, 360);
        MouseFilter = MouseFilterEnum.Stop;
        _vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
        AddChild(_vp);
        var world = new Node3D();
        _vp.AddChild(world);
        TableLook.AddLighting(world);
        TableLook.AddBoard(world, 10, 8);
        _stage = new Node3D();
        world.AddChild(_stage);
        _cam = new Camera3D { Fov = 38, Near = 0.05f, Far = 500, Current = true };
        world.AddChild(_cam);
        Person(world);
        Aim();
    }

    /// <summary>Shows a model file; returns a short line about it, or the reason it can't.</summary>
    public string? Show(string file)
    {
        if (_stage == null) return null;
        foreach (Node c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
        if (!Cache.TryGetValue(file, out var a)) Cache[file] = a = AssetLoader.Load(file);
        if (a.Model == null) return "can't show it: " + (a.Error ?? "unreadable");
        // A file nobody has set up yet (no .model.json) is shown at a plain 32mm, so it fits beside the person.
        UnitModelSettings s = UnitModelSettings.Load(file)?.Copy() ?? new UnitModelSettings { HeightMm = 32, BaseMm = 25 };
        var holder = new Node3D();
        _stage.AddChild(holder);
        if (s.BaseMm > 0) TableLook.AddBase(holder, s.BaseMm / UnitModelSettings.MmPerInch / 2, HudKit.Lime);
        var pivot = new Node3D();
        holder.AddChild(pivot);
        var body = (Node3D)a.Model.Duplicate();
        pivot.AddChild(body);
        var (_, box) = ModelPlacement.Place(pivot, body, s);
        _focusY = Mathf.Max(0.5f, box.Size.Y * 0.45f);
        _distance = Mathf.Max(4.5f, Mathf.Max(box.Size.Y, Mathf.Max(box.Size.X, box.Size.Z)) * 2.4f + 2f);
        Aim();
        return $"{(s.BaseMm > 0 ? $"{s.BaseMm:0.#} mm base, " : "")}stands {box.Size.Y * UnitModelSettings.MmPerInch:0} mm tall";
    }

    public void Spin(double delta)
    {
        if (_stage == null || _dragging) return;
        _spin += (float)delta * 0.5f;
        _stage.Rotation = new Vector3(0, _spin, 0);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }: _distance = Mathf.Max(1.5f, _distance * 0.9f); Aim(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }: _distance = Mathf.Min(60f, _distance * 1.1f); Aim(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb: _dragging = mb.Pressed; break;
            case InputEventMouseMotion mm when _dragging: _spin += mm.Relative.X * 0.01f; _stage.Rotation = new Vector3(0, _spin, 0); break;
            default: return;
        }
        AcceptEvent();
    }

    private void Aim()
    {
        if (_cam == null) return;
        Vector3 focus = new(-0.9f, _focusY, 0);
        Vector3 dir = new Vector3(0.15f, 0.42f, 1f).Normalized();
        _cam.Position = focus + dir * _distance;
        _cam.LookAt(focus, Vector3.Up);
    }

    /// <summary>A 28mm person (32mm to the top of the head), the Match-models stand-in.</summary>
    private static void Person(Node3D world)
    {
        float baseR = 25 / UnitModelSettings.MmPerInch / 2, hIn = 32 / UnitModelSettings.MmPerInch;
        var man = new Node3D { Position = new Vector3(-2.0f, 0, 0) };
        world.AddChild(man);
        TableLook.AddBase(man, baseR, new Color(0.5f, 0.5f, 0.5f));
        var mat = TableLook.Mat(new Color(0.62f, 0.64f, 0.66f), 0.7f);
        float y0 = TableLook.BaseTop, head = hIn * 0.13f, legs = hIn * 0.47f, torso = hIn - head * 2 - legs;
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, mat, new Vector3(-0.08f, y0 + legs / 2, 0));
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, mat, new Vector3(0.08f, y0 + legs / 2, 0));
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.17f, Height = torso + 0.1f }, mat, new Vector3(0, y0 + legs + torso / 2, 0));
        TableLook.Mesh(man, new SphereMesh { Radius = head, Height = head * 2 }, mat, new Vector3(0, y0 + hIn - head, 0));
    }
}
