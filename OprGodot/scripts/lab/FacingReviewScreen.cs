using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// Facing review (Kai, 2026-11-12): go through every model in UnitModels one at a time and make sure it faces the
/// right way before models go on the battle table.
///
/// <para>
/// The model stands big in the middle on its base, with a lime arrow on the table showing "front" (towards you). The
/// models before and after it stand either side. <b>A / D</b> turn it 90 degrees (Shift for 45), <b>X</b> 180; each turn is
/// saved to the model's settings file at once. <b>Space</b> marks it right and moves on; <b>Up</b> goes back,
/// <b>N</b> skips. "Give every unchecked model this facing" turns the rest to match the one on screen and makes that
/// the facing new downloads get - for when the whole mod faces the same wrong way.
/// </para>
/// </summary>
public partial class FacingReviewScreen : Control, IOwnBackdrop
{
    private Node3D _world = null!, _stage = null!;
    private Camera3D _camera = null!;
    private Label _title = null!, _sub = null!, _count = null!, _state = null!, _loading = null!;
    private LineEdit _filter = null!;
    private CheckBox _uncheckedFirst = null!;

    private string _root = "";

    /// <summary>Starts with the Asset Lab's filter (set before the screen opens).</summary>
    public string InitialFilter { get; set; } = "";
    private List<string> _files = new();
    private int _index;
    private readonly Dictionary<string, LoadedAsset> _cache = new();
    private readonly List<string> _cacheOrder = new();
    private bool _pendingShow;

    // Orbit camera round the current model.
    private float _yawCam = 15, _pitch = 30, _distance = 8, _focusY = 1, _focusZ = 1;
    private Vector2? _drag;

    public override void _Ready()
    {
        // Opened from the Asset Lab, which has already put the menu's turning table away.
        MouseFilter = MouseFilterEnum.Stop;
        _root = UnitModelPaths.Root;

        _world = new Node3D { Name = "ReviewWorld" };
        AddChild(_world);
        TableLook.AddLighting(_world);
        TableLook.AddBoard(_world, 72, 48);
        _stage = new Node3D { Name = "Stage" };
        _world.AddChild(_stage);
        _camera = new Camera3D { Fov = 40, Current = true, Far = 2000, Near = 0.05f };
        _world.AddChild(_camera);

        BuildHud();
        _camera.MakeCurrent();
        Refresh();
    }

    public override void _ExitTree()
    {
        foreach (var a in _cache.Values) a.Model?.QueueFree();
    }

    // ================================================================== HUD

    private void BuildHud()
    {
        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(100, 40), TooltipText = "Back to the Asset Lab (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("FACING REVIEW", 34, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text("Every model should face the lime arrow", 15, HudKit.Dim, HudKit.Semi));
        head.AddChild(t);
        top.AddChild(head);
        _filter = new LineEdit { Name = "Filter", PlaceholderText = "Only these (e.g. Alien Hives)", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(360, 0), TooltipText = "Review only files whose folder or name contains this." };
        _filter.Text = InitialFilter;
        _filter.TextSubmitted += _ => { Refresh(); _filter.ReleaseFocus(); };
        _filter.TextChanged += _ => Refresh();
        top.AddChild(_filter);
        _uncheckedFirst = new CheckBox { Name = "UncheckedFirst", Text = "Not yet checked first", ButtonPressed = true, TooltipText = "Put the models nobody has checked at the front, so you can stop and pick up later." };
        _uncheckedFirst.Toggled += _ => Refresh();
        top.AddChild(_uncheckedFirst);
        _count = HudKit.Text("", 16, HudKit.Lime, HudKit.Bold);
        top.AddChild(_count);
        var topPanel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(top, 16, 14, 16, 14));
        HudKit.Place(this, topPanel, LayoutPreset.TopLeft, new Vector2(16, 16));

        var keys = HudKit.Text("A / D  turn 90  (Shift: 45)     X  turn 180     Space  looks right, next     W  next     S  back     Right-drag / wheel  look round     Every change saves itself", 14, HudKit.Dim, HudKit.Semi);
        HudKit.Place(this, keys, LayoutPreset.TopRight, new Vector2(-24, 24));

        var bottom = new VBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        _title = HudKit.Text("", 26, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center);
        _sub = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center);
        _state = HudKit.Text("", 16, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center);
        bottom.AddChild(_title);
        bottom.AddChild(_sub);
        bottom.AddChild(_state);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Btn("Prev", "< Back (S)", "The model before this one.", () => Step(-1)));
        row.AddChild(Btn("TurnLeft", "Turn left 90 (A)", "Turn it a quarter to the left (A; Shift+A for 45). Saved at once.", () => Turn(90)));
        row.AddChild(Btn("Turn180", "Turn 180 (X)", "Turn it round (X). Saved at once.", () => Turn(180)));
        row.AddChild(Btn("TurnRight", "Turn right 90 (D)", "Turn it a quarter to the right (D; Shift+D for 45). Saved at once.", () => Turn(-90)));
        var ok = Btn("LooksRight", "Looks right - next (Space)", "It faces the arrow: mark it checked and go to the next one.", MarkAndNext);
        ok.AddThemeColorOverride("font_color", HudKit.Lime);
        row.AddChild(ok);
        row.AddChild(Btn("Skip", "Next (W)", "Next model without marking this one.", () => Step(1)));
        bottom.AddChild(row);
        var bulk = Btn("ApplyToRest", "Give every unchecked model this facing...", "Turns every model in this list that nobody has checked yet the same way as this one, and makes it the facing new downloads get.", ConfirmBulk);
        bulk.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        bottom.AddChild(bulk);
        var bottomPanel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(bottom, 18, 12, 18, 14));
        HudKit.Place(this, bottomPanel, LayoutPreset.CenterBottom, new Vector2(0, -16));

        _loading = HudKit.Text("Loading...", 28, HudKit.Lime, HudKit.Bold, HorizontalAlignment.Center);
        HudKit.Place(this, _loading, LayoutPreset.Center);
        _loading.Visible = false;
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    // ================================================================== the list

    private static bool IsChecked(string file) => UnitModelSettings.Load(file)?.FacingChecked == true;

    /// <summary>Rebuilds the list from the folder and the filter, keeping the current model if it is still in it.</summary>
    public void Refresh()
    {
        string? current = _files.Count > 0 ? _files[Math.Clamp(_index, 0, _files.Count - 1)] : null;
        string f = _filter.Text.Trim();
        var all = UnitModelPaths.Browse(_root).Where(UnitModelPaths.IsModel)
            .Where(x => f.Length == 0 || Path.GetRelativePath(_root, x).Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        _files = _uncheckedFirst.ButtonPressed ? all.OrderBy(IsChecked).ToList() : all;
        _index = current != null && _files.IndexOf(current) is var i and >= 0 ? i : 0;
        Show();
    }

    private void Step(int by)
    {
        if (_files.Count == 0) return;
        _index = Math.Clamp(_index + by, 0, _files.Count - 1);
        Show();
    }

    private string? Current => _files.Count == 0 ? null : _files[_index];

    // ================================================================== editing

    private UnitModelSettings SettingsFor(string file) =>
        UnitModelSettings.Load(file)?.Copy() ?? new UnitModelSettings { BaseMm = 25, HeightMm = UnitModelSettings.GuessHeightMm(25) };

    private static float Wrap(float d) { d %= 360; if (d > 180) d -= 360; if (d <= -180) d += 360; return d; }

    /// <summary>Turns the current model and saves it (positive = to the left, seen from above).</summary>
    public void Turn(float degrees)
    {
        if (Current is not { } file) return;
        var s = SettingsFor(file);
        s.YawDegrees = Wrap(s.YawDegrees + degrees);
        s.FacingChecked = false; // a turned model wants a fresh look
        s.Save(file);
        Show();
        _state.Text = $"Saved - turned {s.YawDegrees:0} degrees. Space when it faces the arrow.";
        _state.Modulate = HudKit.Lime;
    }

    /// <summary>Marks the current model as facing right and goes to the next.</summary>
    public void MarkAndNext()
    {
        if (Current is not { } file) return;
        var s = SettingsFor(file);
        s.FacingChecked = true;
        s.Save(file);
        if (_index < _files.Count - 1) _index++;
        Show();
    }

    private void ConfirmBulk()
    {
        if (Current is not { } file) return;
        float yaw = SettingsFor(file).YawDegrees;
        var targets = _files.Where(x => x != file && !IsChecked(x)).ToList();
        var dlg = new ConfirmationDialog
        {
            Title = "Turn the unchecked models?",
            DialogText = $"Give {targets.Count} unchecked model(s) in this list the same facing as \"{Path.GetFileNameWithoutExtension(file)}\" " +
                         $"({yaw:0} degrees)?\nNew downloads will get this facing too.\nThey stay unchecked, so you can still look through them.",
            OkButtonText = "Turn them",
        };
        dlg.Confirmed += () => { ApplyToAll(yaw, targets); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    /// <summary>Gives these models this facing, and new downloads too (also used by the screenshot driver).</summary>
    public void ApplyToAll(float yaw, IEnumerable<string>? targets = null)
    {
        targets ??= _files.Where(x => x != Current && !IsChecked(x)).ToList();
        int n = 0;
        foreach (string f in targets)
        {
            var s = SettingsFor(f);
            s.YawDegrees = yaw;
            s.Save(f);
            n++;
        }
        new ModelDownloadDefaults { YawDegrees = yaw }.Save(_root);
        _state.Text = $"{n} model(s) turned; new downloads will face this way too.";
        Show(keepState: true);
    }

    // ================================================================== showing

    private void Show(bool keepState = false)
    {
        int done = _files.Count(IsChecked);
        _count.Text = _files.Count == 0 ? "No models here yet - download some first." : $"{done} of {_files.Count} checked";
        if (Current is not { } file)
        {
            _title.Text = ""; _sub.Text = ""; if (!keepState) _state.Text = "";
            foreach (Node c in _stage.GetChildren()) c.QueueFree();
            return;
        }
        string rel = Path.GetRelativePath(_root, file);
        string unit = Path.GetDirectoryName(rel) ?? "";
        _title.Text = Path.GetFileNameWithoutExtension(file);
        _sub.Text = $"{unit.Replace(Path.DirectorySeparatorChar.ToString(), "  -  ")}    ({_index + 1} of {_files.Count})";
        var s = SettingsFor(file);
        if (!keepState)
        {
            _state.Text = s.FacingChecked ? $"Checked - faces the arrow (turned {s.YawDegrees:0} degrees)" : $"Not checked yet (turned {s.YawDegrees:0} degrees)";
            _state.Modulate = s.FacingChecked ? HudKit.Lime : HudKit.Fg;
        }
        // Loading a big file stalls a frame or two: say so first, load next frame.
        _loading.Visible = !_cache.ContainsKey(file);
        _pendingShow = true;
    }

    private LoadedAsset? Get(string file)
    {
        if (_cache.TryGetValue(file, out var a)) { _cacheOrder.Remove(file); _cacheOrder.Add(file); return a; }
        a = AssetLoader.Load(file);
        _cache[file] = a;
        _cacheOrder.Add(file);
        // Keep the last few (the neighbours come round again); free the rest - these files can be 100 MB.
        while (_cacheOrder.Count > 6)
        {
            string old = _cacheOrder[0];
            _cacheOrder.RemoveAt(0);
            if (_cache.Remove(old, out var gone)) gone.Model?.QueueFree();
        }
        return a;
    }

    private void Build()
    {
        foreach (Node c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
        if (Current is not { } file) return;
        Aabb mid = Stand(file, Vector3.Zero, true);
        float half = Mathf.Max(mid.Size.X, mid.Size.Z) / 2 + 1.5f;
        // The ones before and after, either side, smaller in the eye only by distance.
        if (_index > 0)
        {
            string prev = _files[_index - 1];
            Stand(prev, new Vector3(-half - 1.8f, 0, -1), false);
        }
        if (_index < _files.Count - 1)
        {
            string next = _files[_index + 1];
            Stand(next, new Vector3(half + 1.8f, 0, -1), false);
        }
        Arrow(mid);
        // Frame the model and its arrow together, a little above the bottom panel.
        _focusY = Mathf.Max(0.4f, mid.Size.Y * 0.25f);
        _focusZ = (Mathf.Max(mid.End.Z, 0.6f) + 1.4f) * 0.5f;
        _distance = Mathf.Max(7f, Mathf.Max(mid.Size.Y, Mathf.Max(mid.Size.X, mid.Size.Z)) * 3f + 4f);
        ApplyCamera();
    }

    private Aabb Stand(string file, Vector3 at, bool main)
    {
        var holder = new Node3D { Position = at };
        _stage.AddChild(holder);
        var s = SettingsFor(file);
        if (s.BaseMm > 0) TableLook.AddBase(holder, s.BaseMm / UnitModelSettings.MmPerInch / 2, main ? HudKit.Lime : new Color(0.45f, 0.5f, 0.52f));
        LoadedAsset? a = Get(file);
        if (a?.Model == null)
        {
            if (main) _state.Text = a?.Error ?? "Could not open this file.";
            return new Aabb(Vector3.Zero, Vector3.One);
        }
        var pivot = new Node3D();
        holder.AddChild(pivot);
        var body = (Node3D)a.Model.Duplicate();
        pivot.AddChild(body);
        var (_, box) = ModelPlacement.Place(pivot, body, s);
        if (!main)
        {
            var tag = new Label3D
            {
                Text = (_files.IndexOf(file) < _index ? "< " : "") + Path.GetFileNameWithoutExtension(file) + (_files.IndexOf(file) > _index ? " >" : ""),
                Font = HudKit.Bold, FontSize = 30, PixelSize = 0.006f, OutlineSize = 8, NoDepthTest = true, Modulate = HudKit.Dim,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, box.End.Y + 0.4f, 0),
            };
            holder.AddChild(tag);
        }
        return box;
    }

    /// <summary>A lime arrow on the table in front of the model, pointing the way it should face (towards the camera).</summary>
    private void Arrow(Aabb box)
    {
        var mat = TableLook.Mat(HudKit.Lime, 0.4f, HudKit.Lime, 1.6f);
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        float z0 = Mathf.Max(box.End.Z, 0.6f) + 0.4f;
        TableLook.Mesh(_stage, new BoxMesh { Size = new Vector3(0.18f, 0.03f, 1.6f) }, mat, new Vector3(0, 0.03f, z0 + 0.8f));
        var head = new ArrayMesh();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        foreach (Vector3 v in new[] { new Vector3(-0.5f, 0.04f, z0 + 1.6f), new Vector3(0.5f, 0.04f, z0 + 1.6f), new Vector3(0, 0.04f, z0 + 2.5f) }) st.AddVertex(v);
        st.Commit(head);
        var hm = new MeshInstance3D { Mesh = head, MaterialOverride = mat };
        _stage.AddChild(hm);
        _stage.AddChild(new Label3D
        {
            Text = "FRONT", Font = HudKit.Bold, FontSize = 40, PixelSize = 0.006f, Modulate = HudKit.Lime, OutlineSize = 8,
            Position = new Vector3(0, 0.05f, z0 + 3.0f), RotationDegrees = new Vector3(-90, 0, 0),
        });
    }

    // ================================================================== camera and keys

    private void ApplyCamera()
    {
        Vector3 focus = new(0, _focusY, _focusZ);
        Vector3 dir = new(Mathf.Sin(Mathf.DegToRad(_yawCam)) * Mathf.Cos(Mathf.DegToRad(_pitch)), Mathf.Sin(Mathf.DegToRad(_pitch)),
            Mathf.Cos(Mathf.DegToRad(_yawCam)) * Mathf.Cos(Mathf.DegToRad(_pitch)));
        _camera.Position = focus + dir * _distance;
        _camera.LookAt(focus, Vector3.Up);
    }

    public override void _GuiInput(InputEvent e)
    {
        bool onView = GetViewport().GuiGetHoveredControl() == this;
        // A click on the view gives the keys back from the filter box (W/A/S/D would otherwise type into it).
        if (e is InputEventMouseButton { Pressed: true } && onView) _filter.ReleaseFocus();
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } when onView:
                _distance = Mathf.Max(1.5f, _distance * 0.9f); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } when onView:
                _distance = Mathf.Min(120f, _distance * 1.1f); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                _drag = mb.Pressed && onView ? mb.Position : null; break;
            case InputEventMouseMotion mm when _drag is { } d:
                _yawCam -= (mm.Position.X - d.X) * 0.3f;
                _pitch = Mathf.Clamp(_pitch + (mm.Position.Y - d.Y) * 0.25f, 2f, 85f);
                _drag = mm.Position;
                break;
            default: return;
        }
        ApplyCamera();
        AcceptEvent();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || _filter.HasFocus()) return;
        switch (k.Keycode)
        {
            // Kai, 2026-11-12: A / D turn (Shift = 45 for fine tuning), W / S next / back, Space confirms.
            case Key.A: Turn(k.ShiftPressed ? 45 : 90); break;
            case Key.D: Turn(k.ShiftPressed ? -45 : -90); break;
            case Key.X: Turn(180); break;
            case Key.W: Step(1); break;
            case Key.S: Step(-1); break;
            case Key.Space or Key.Enter or Key.KpEnter: MarkAndNext(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private int _frames;

    public override void _Process(double delta)
    {
        if (!_pendingShow) return;
        // Let "Loading..." draw for a frame before a big file stalls the next one.
        if (_loading.Visible && ++_frames < 2) return;
        _frames = 0;
        _pendingShow = false;
        Build();
        _loading.Visible = false;
    }
}
