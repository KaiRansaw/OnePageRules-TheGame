using FdgRaylib.Models3D;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// The Asset Lab (Kai, 2026-11-11: "a Godot environment to test assets and how they look in game").
///
/// <para>
/// Open any 3D model or picture - from the UnitModels folder, or dragged onto the window from anywhere - and see it
/// on the real table: the battle's own felt, inch grid, lighting and base, at true tabletop scale (1 world unit = 1
/// inch, bases in mm). Beside it stand known sizes to judge it against: a 28mm person, the game's current stand-in
/// token, the common bases, a 3" wall and a 6" ruler. Then:
/// </para>
/// <list type="bullet">
/// <item>fix how it stands (up axis, facing, height in mm, base) and <b>save those numbers beside the file</b> -
/// <see cref="UnitModelSettings"/>, the normalisation data the table will read when real models go in;</item>
/// <item>see it as one model, a unit of 5 or 10 in 1" coherency, or an army of 100 (with the frame rate);</item>
/// <item>look from up close, from normal play distance, the whole table, or straight down;</item>
/// <item>play its animations, spin it, swap seat colours, and see a picture as it will look in the HUD.</item>
/// </list>
/// A file that changes on disk (re-exported from Blender) reloads by itself.
/// </summary>
public partial class AssetLabScreen : Control, IOwnBackdrop
{
    private const float TableW = 72, TableD = 48;

    private Node3D _world = null!, _lights = null!, _group = null!, _refs = null!;
    private Camera3D _camera = null!;
    private Label3D _sizeLabel = null!;

    private ItemList _files = null!;
    private LineEdit _filter = null!;
    private Label _folderLabel = null!, _info = null!, _fps = null!, _saved = null!, _hint = null!;
    private OptionButton _base = null!, _up = null!, _copies = null!, _seat = null!, _light = null!, _anim = null!;
    private SpinBox _height = null!, _yaw = null!;
    private CheckBox _centre = null!, _showRefs = null!, _showToken = null!, _spin = null!;
    private Control _hudPreview = null!;
    private VBoxContainer _hudSlots = null!;

    private string _root = "";
    private List<string> _list = new();
    private string? _open;
    private DateTime _openStamp;
    private LoadedAsset? _asset;
    private UnitModelSettings _settings = new();
    private bool _quiet;            // true while the settings boxes are being filled in from code
    private double _scanTimer;

    // Camera, the battle screen's way: orbit a focus point.
    private Vector3 _focus = Vector3.Zero;
    private float _yawCam = 20, _pitch = 30, _distance = 9;
    private bool _topView;
    private Vector2? _dragRotate, _dragPan;

    public override void _Ready()
    {
        MenuBackdrop.Instance?.SetActive(false);
        MouseFilter = MouseFilterEnum.Stop; // the 3D view takes the clicks no panel takes
        BuildWorld();
        BuildPanels();
        _root = UnitModelPaths.Root;
        _folderLabel.Text = _root;
        Rescan();
        GetWindow().FilesDropped += OnFilesDropped;
        // A screen opened on top (the facing review) hides this one; a hidden Control still draws its 3D world, so the
        // world comes out of the tree while hidden and goes back, with its camera, when this screen shows again.
        VisibilityChanged += () =>
        {
            if (!Visible && _world.GetParent() == this) RemoveChild(_world);
            else if (Visible && _world.GetParent() == null) { AddChild(_world); MoveChild(_world, 0); _camera.MakeCurrent(); _list.Clear(); Rescan(); if (_open != null) Open(_open); }
        };
        ApplyCamera();
        if (_list.FirstOrDefault(UnitModelPaths.IsModel) is { } first) Open(first);
        else ShowNothing();
    }

    public override void _ExitTree()
    {
        GetWindow().FilesDropped -= OnFilesDropped;
        if (_world.GetParent() == null) _world.QueueFree();
        MenuBackdrop.Instance?.SetActive(true);
    }

    // ================================================================== the 3D table

    private void BuildWorld()
    {
        _world = new Node3D { Name = "LabWorld" };
        AddChild(_world);
        _lights = new Node3D { Name = "Lights" };
        _world.AddChild(_lights);
        TableLook.AddLighting(_lights);
        TableLook.AddBoard(_world, TableW, TableD);
        _group = new Node3D { Name = "Models" };
        _world.AddChild(_group);
        _refs = new Node3D { Name = "References" };
        _world.AddChild(_refs);
        _camera = new Camera3D { Fov = 44, Current = true, Far = 2000, Near = 0.05f };
        _world.AddChild(_camera);
        _sizeLabel = new Label3D
        {
            Font = HudKit.Bold, FontSize = 40, PixelSize = 0.0035f, OutlineSize = 10, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineModulate = new Color(0, 0, 0, 0.85f), Modulate = HudKit.Lime,
        };
        _world.AddChild(_sizeLabel);
    }

    private void SetLighting(int mode)
    {
        foreach (Node n in _lights.GetChildren()) n.QueueFree();
        if (mode == 0) { TableLook.AddLighting(_lights); return; }
        // Studio: flat, bright, neutral - for checking the paint job and the textures, not for judging the in-game look.
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.32f, 0.34f, 0.36f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.75f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        _lights.AddChild(new WorldEnvironment { Environment = env });
        var key = new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = true };
        key.RotationDegrees = new Vector3(-45, 30, 0);
        _lights.AddChild(key);
        var fill = new DirectionalLight3D { LightEnergy = 0.5f };
        fill.RotationDegrees = new Vector3(-20, -140, 0);
        _lights.AddChild(fill);
    }

    // ================================================================== panels

    private void BuildPanels()
    {
        // ---- left: files and how this one stands
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(100, 40), TooltipText = "Back to the main menu (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var title = new VBoxContainer();
        title.AddThemeConstantOverride("separation", -6);
        title.AddChild(HudKit.Text("ASSET LAB", 34, HudKit.Fg, HudKit.Exo(800)));
        title.AddChild(HudKit.Text("Models and pictures on the real table", 15, HudKit.Dim, HudKit.Semi));
        head.AddChild(title);
        left.AddChild(head);

        left.AddChild(HudKit.Heading("Files", 15));
        _folderLabel = HudKit.Text("", 12, HudKit.Dim, HudKit.Medium);
        _folderLabel.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        _folderLabel.TooltipText = "The UnitModels folder. Put models in UnitModels/<Faction>/<Unit Name>.glb, the same way pictures go in UnitImages.";
        _folderLabel.MouseFilter = MouseFilterEnum.Pass;
        left.AddChild(_folderLabel);
        _filter = new LineEdit { Name = "Filter", PlaceholderText = "Filter files (e.g. Alien Hives, Grunts)", ClearButtonEnabled = true, TooltipText = "Show only files whose folder or name contains this." };
        _filter.TextChanged += _ => { _list.Clear(); Rescan(); };
        left.AddChild(_filter);
        _files = new ItemList { Name = "Files", CustomMinimumSize = new Vector2(0, 250), SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single };
        _files.ItemSelected += i => { if (i >= 0 && i < _list.Count) Open(_list[(int)i]); };
        left.AddChild(_files);
        var fileRow = new HBoxContainer();
        fileRow.AddThemeConstantOverride("separation", 6);
        fileRow.AddChild(Btn("OpenFolder", "Open folder", "Open the UnitModels folder in Windows, to add or rename files.", () => OS.ShellOpen(_root)));
        fileRow.AddChild(Btn("Rescan", "Refresh", "Look for new files now (it also checks by itself every couple of seconds).", Rescan));
        left.AddChild(fileRow);
        var dl = Btn("DownloadModels", "Download models...", "Fetch Warhammer 40k models from the ForceOrg Tabletop Simulator mod for an army, saved under each OPR unit's name.", () => OpenDownloads());
        left.AddChild(dl);
        left.AddChild(Btn("ReviewFacing", "Review facing...", "Go through every model one at a time and turn the ones that don't face the right way.",
            () => App.I.Open<FacingReviewScreen>(Scenes.FacingReview, r => r.InitialFilter = _filter.Text)));
        _hint = HudKit.Text("Drag a .glb, .gltf, .obj (with its .mtl / .png) or a picture onto the window to copy it in and open it.", 14, HudKit.Dim, HudKit.Semi);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(_hint);

        left.AddChild(HudKit.Gap(0, 4));
        left.AddChild(HudKit.Heading("How it stands", 15));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);

        _base = new OptionButton { Name = "Base", TooltipText = "The round base it stands on, in mm. OPR's army data gives each unit its base size; a vehicle with no base uses None." };
        _base.AddItem("None");
        foreach (float mm in UnitModelSettings.RoundBasesMm) _base.AddItem($"{mm:0.#} mm");
        _base.ItemSelected += _ => { if (!_quiet) { _settings.BaseMm = _base.Selected == 0 ? 0 : UnitModelSettings.RoundBasesMm[_base.Selected - 1]; Rebuild(); } };
        Row(grid, "Base", _base);

        _height = new SpinBox { Name = "Height", MinValue = 0, MaxValue = 1000, Step = 0.5, Suffix = "mm", TooltipText = "How tall it stands, foot to top, in mm. A 28mm-scale person is about 32. 0 = the file's own size (1 unit = 1 inch)." };
        _height.ValueChanged += v => { if (!_quiet) { _settings.HeightMm = (float)v; Rebuild(); } };
        Row(grid, "Height", _height);

        _up = new OptionButton { Name = "UpAxis", TooltipText = "Which way the file thinks is up. If the model lies on its back or face, try Z up or Z down." };
        foreach (string s in new[] { "Y up (standard)", "Z up", "Z down", "Upside down", "X up", "X down" }) _up.AddItem(s);
        _up.ItemSelected += i => { if (!_quiet) { _settings.UpAxis = (EUpAxis)(int)i; Rebuild(); } };
        Row(grid, "Up", _up);

        var yawRow = new HBoxContainer();
        yawRow.AddThemeConstantOverride("separation", 4);
        yawRow.AddChild(Btn("TurnLeft", "<", "Turn it 45 degrees.", () => _yaw.Value = Wrap(_yaw.Value - 45)));
        _yaw = new SpinBox { Name = "Facing", MinValue = -180, MaxValue = 180, Step = 5, Suffix = "deg", SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Which way it faces. At 0 its front should face you in the starting view, the same as every other model." };
        _yaw.ValueChanged += v => { if (!_quiet) { _settings.YawDegrees = (float)v; Rebuild(); } };
        yawRow.AddChild(_yaw);
        yawRow.AddChild(Btn("TurnRight", ">", "Turn it 45 degrees.", () => _yaw.Value = Wrap(_yaw.Value + 45)));
        Row(grid, "Facing", yawRow);

        _centre = new CheckBox { Name = "Centre", Text = "Centre on base", ButtonPressed = true, TooltipText = "Stand the middle of the model over the middle of the base. Turn off for a model whose own centre point was placed on purpose (a rider leaning past its base)." };
        _centre.Toggled += on => { if (!_quiet) { _settings.CentreOnBase = on; Rebuild(); } };
        grid.AddChild(HudKit.Gap(0));
        grid.AddChild(_centre);
        left.AddChild(grid);

        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 6);
        saveRow.AddChild(Btn("Save", "Save settings", "Save these numbers beside the file (<file>.model.json). The game reads them when it puts this model on the table.", SaveSettings));
        saveRow.AddChild(Btn("Reset", "Reset", "Back to a first guess from the base size.", ResetSettings));
        left.AddChild(saveRow);
        _saved = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        left.AddChild(_saved);

        var leftPanel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(left, 16, 14, 16, 14));
        leftPanel.CustomMinimumSize = new Vector2(400, 0);
        leftPanel.AnchorTop = 0; leftPanel.AnchorBottom = 1; leftPanel.AnchorLeft = 0; leftPanel.AnchorRight = 0;
        leftPanel.OffsetLeft = 16; leftPanel.OffsetTop = 16; leftPanel.OffsetBottom = -16; leftPanel.OffsetRight = 416;
        AddChild(leftPanel);

        // ---- right: what to show, the camera, the numbers
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 8);
        right.AddChild(HudKit.Heading("Show", 15));
        var g2 = new GridContainer { Columns = 2 };
        g2.AddThemeConstantOverride("h_separation", 10);
        g2.AddThemeConstantOverride("v_separation", 6);
        _copies = new OptionButton { Name = "Copies", TooltipText = "One model, a unit in coherency (1\" apart), or a 100-model army to check how it looks massed - and whether the frame rate holds." };
        foreach (string s in new[] { "1 model", "Unit of 5", "Unit of 10", "Unit of 20", "Army of 100" }) _copies.AddItem(s);
        _copies.ItemSelected += _ => Rebuild();
        Row(g2, "Copies", _copies);
        _seat = new OptionButton { Name = "Seat", TooltipText = "The seat colour on the base rim, as in a game." };
        foreach (var (name, _) in PlayerColorCodes.Options) _seat.AddItem(name);
        _seat.ItemSelected += _ => Rebuild();
        Row(g2, "Seat colour", _seat);
        _light = new OptionButton { Name = "Lighting", TooltipText = "In-game: exactly the battle's light. Studio: bright and neutral, for checking paint and textures." };
        _light.AddItem("In-game");
        _light.AddItem("Studio");
        _light.ItemSelected += i => SetLighting((int)i);
        Row(g2, "Lighting", _light);
        _anim = new OptionButton { Name = "Animation", TooltipText = "Animations in the file, if it has any.", Disabled = true };
        _anim.ItemSelected += _ => PlayAnimation();
        Row(g2, "Animation", _anim);
        right.AddChild(g2);
        _showRefs = new CheckBox { Name = "ShowRefs", Text = "Size comparisons", ButtonPressed = true, TooltipText = "A 28mm person, the common bases, a 3\" wall and a 6\" ruler beside it." };
        _showRefs.Toggled += on => _refs.Visible = on;
        right.AddChild(_showRefs);
        _showToken = new CheckBox { Name = "ShowToken", Text = "Today's stand-in beside it", ButtonPressed = true, TooltipText = "The plain token the table draws now, on the same base - to see the difference the real model makes." };
        _showToken.Toggled += _ => BuildReferences();
        right.AddChild(_showToken);
        _spin = new CheckBox { Name = "Spin", Text = "Turntable", TooltipText = "Spin it slowly to see every side." };
        right.AddChild(_spin);

        right.AddChild(HudKit.Gap(0, 4));
        right.AddChild(HudKit.Heading("Camera", 15));
        var cams = new GridContainer { Columns = 2 };
        cams.AddThemeConstantOverride("h_separation", 6);
        cams.AddThemeConstantOverride("v_separation", 6);
        cams.AddChild(Btn("ViewClose", "Close up", "Right up to the model (F).", () => View(0)));
        cams.AddChild(Btn("ViewPlay", "Play distance", "About where the camera sits while you play.", () => View(1)));
        cams.AddChild(Btn("ViewTable", "Whole table", "The battle's opening view of the full 6' x 4' table.", () => View(2)));
        cams.AddChild(Btn("ViewTop", "Top view", "Straight down, like the old 2D view (T).", () => View(3)));
        right.AddChild(cams);
        right.AddChild(HudKit.Text("Right-drag turn, middle-drag pan, wheel zoom, Q/E turn.", 13, HudKit.Dim, HudKit.Medium));

        right.AddChild(HudKit.Gap(0, 4));
        right.AddChild(HudKit.Heading("This file", 15));
        _info = HudKit.Text("", 15, HudKit.Fg, HudKit.Semi);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _info.CustomMinimumSize = new Vector2(300, 0);
        right.AddChild(_info);

        var rightPanel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(right, 16, 14, 16, 14));
        rightPanel.CustomMinimumSize = new Vector2(340, 0);
        HudKit.Place(this, rightPanel, LayoutPreset.TopRight, new Vector2(-16, 16));

        _fps = HudKit.Text("", 15, HudKit.Dim, HudKit.Bold, HorizontalAlignment.Right);
        HudKit.Place(this, _fps, LayoutPreset.BottomRight, new Vector2(-20, -12));

        // ---- bottom-centre: a picture as the HUD will show it
        var hud = new VBoxContainer();
        hud.AddThemeConstantOverride("separation", 6);
        hud.AddChild(HudKit.Heading("In the HUD", 15));
        _hudSlots = new VBoxContainer();
        _hudSlots.AddThemeConstantOverride("separation", 8);
        hud.AddChild(_hudSlots);
        _hudPreview = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(hud, 14, 10, 14, 12));
        HudKit.Place(this, _hudPreview, LayoutPreset.CenterBottom, new Vector2(120, -16));
    }

    private static double Wrap(double deg) => deg > 180 ? deg - 360 : deg < -180 ? deg + 360 : deg;

    private static void Row(GridContainer g, string label, Control c)
    {
        var l = HudKit.Text(label, 15, HudKit.Dim, HudKit.Semi);
        l.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        l.CustomMinimumSize = new Vector2(84, 0);
        g.AddChild(l);
        c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        g.AddChild(c);
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    // ================================================================== files

    private void Rescan()
    {
        string filter = _filter?.Text.Trim() ?? "";
        var now = UnitModelPaths.Browse(_root).Where(x => filter.Length == 0 || Path.GetRelativePath(_root, x).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (now.SequenceEqual(_list) && _files.ItemCount == now.Count) return;
        _list = now;
        _files.Clear();
        foreach (string f in _list)
        {
            string rel = Path.GetRelativePath(_root, f);
            bool saved = File.Exists(UnitModelSettings.PathFor(f));
            int i = _files.AddItem((UnitModelPaths.IsImage(f) ? "[pic]  " : "") + rel + (saved ? "   *" : ""));
            _files.SetItemTooltip(i, rel + (saved ? "\nHas saved settings." : ""));
            if (f == _open) _files.Select(i);
        }
        if (_list.Count == 0) _files.AddItem("(empty - drag files onto the window)");
    }

    private void OnFilesDropped(string[] files)
    {
        // Copy in, keeping a dropped folder's own name, so UnitModels/<Faction>/... works by dropping a faction folder.
        string? firstModel = null;
        string dropDir = Path.Combine(_root, "_Dropped");
        foreach (string src in files)
        {
            if (Directory.Exists(src))
            {
                string dest = Path.Combine(_root, Path.GetFileName(src.TrimEnd('/', '\\')));
                foreach (string f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    string to = Path.Combine(dest, Path.GetRelativePath(src, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(f, to, true);
                    if (firstModel == null && UnitModelPaths.IsModel(to)) firstModel = to;
                }
                continue;
            }
            if (!File.Exists(src)) continue;
            string target = src.StartsWith(_root, StringComparison.OrdinalIgnoreCase) ? src : Path.Combine(dropDir, Path.GetFileName(src));
            if (target != src)
            {
                Directory.CreateDirectory(dropDir);
                File.Copy(src, target, true);
            }
            if (firstModel == null && (UnitModelPaths.IsModel(target) || (files.Length == 1 && UnitModelPaths.IsImage(target)))) firstModel = target;
        }
        Rescan();
        if (firstModel != null) Open(firstModel);
    }

    /// <summary>Opens the Download models window (the screenshot driver can point it at a test catalogue and list).</summary>
    public ModelDownloadPopup OpenDownloads(string? catalogue = null, string? map = null)
    {
        var p = ModelDownloadPopup.Open(this, catalogue, map);
        p.Saved += () => { _list.Clear(); Rescan(); };
        return p;
    }

    /// <summary>Opens a file (also used by the screenshot driver: <c>lab:&lt;path&gt;</c>).</summary>
    public void Open(string file)
    {
        if (!File.Exists(file)) return;
        _asset?.Model?.QueueFree();
        _open = file;
        _openStamp = File.GetLastWriteTimeUtc(file);
        _asset = AssetLoader.Load(file);
        int idx = _list.IndexOf(file);
        if (idx >= 0) _files.Select(idx);

        _settings = UnitModelSettings.Load(file)?.Copy() ?? FirstGuess(file, _asset);
        FillSettingBoxes();
        _saved.Text = File.Exists(UnitModelSettings.PathFor(file)) ? "Saved settings loaded." : "Not saved yet - these are a first guess.";
        _saved.Modulate = File.Exists(UnitModelSettings.PathFor(file)) ? HudKit.Lime : HudKit.Dim;

        _anim.Clear();
        if (_asset.Animations.Count == 0) { _anim.AddItem("(none)"); _anim.Disabled = true; }
        else { _anim.AddItem("Still"); foreach (string a in _asset.Animations) _anim.AddItem(a); _anim.Disabled = false; }

        Rebuild();
        View(_copies.Selected >= 3 ? 1 : 0);
    }

    private static UnitModelSettings FirstGuess(string file, LoadedAsset? asset)
    {
        // Lying down? A model much deeper than it is tall was almost certainly exported Z-up (most Tabletop
        // Simulator and CAD files) - stand it up. A wide flat thing (a tank) can fool this; the dropdown fixes that.
        EUpAxis up = EUpAxis.YUp;
        if (asset?.Model != null && RawBox(asset.Model) is { } box && box.Size.Z > box.Size.Y * 1.3f && box.Size.Z > box.Size.X * 0.8f)
            up = EUpAxis.ZUp;

        // A base size in the name ("Grunt 32mm.glb") is a strong hint; otherwise infantry on 25mm.
        float baseMm = 25;
        foreach (float mm in UnitModelSettings.RoundBasesMm.OrderByDescending(x => x))
            if (Path.GetFileNameWithoutExtension(file).Contains($"{mm:0.#}mm", StringComparison.OrdinalIgnoreCase)) { baseMm = mm; break; }
        return new UnitModelSettings { BaseMm = baseMm, HeightMm = UnitModelSettings.GuessHeightMm(baseMm), UpAxis = up };
    }

    /// <summary>The model's own box, before any turning (computed from the meshes, not in the scene).</summary>
    private static Aabb? RawBox(Node3D model)
    {
        Aabb? box = null;
        void Walk(Node n, Transform3D parent)
        {
            Transform3D t = n is Node3D n3 ? parent * n3.Transform : parent;
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                Aabb b = t * mi.GetAabb();
                box = box is { } x ? x.Merge(b) : b;
            }
            foreach (Node c in n.GetChildren()) Walk(c, t);
        }
        Walk(model, Transform3D.Identity);
        return box;
    }

    private void FillSettingBoxes()
    {
        _quiet = true;
        int b = Array.IndexOf(UnitModelSettings.RoundBasesMm, _settings.BaseMm);
        if (b < 0 && _settings.BaseMm > 0)
        {
            // An unusual size from a saved file: nearest listed one for the dropdown, the saved number still wins.
            b = Array.IndexOf(UnitModelSettings.RoundBasesMm, UnitModelSettings.RoundBasesMm.OrderBy(x => Math.Abs(x - _settings.BaseMm)).First());
        }
        _base.Select(_settings.BaseMm <= 0 ? 0 : b + 1);
        _height.Value = _settings.HeightMm;
        _up.Select((int)_settings.UpAxis);
        _yaw.Value = _settings.YawDegrees;
        _centre.ButtonPressed = _settings.CentreOnBase;
        _quiet = false;
    }

    private void SaveSettings()
    {
        if (_open == null) return;
        try
        {
            _settings.Save(_open);
            _saved.Text = "Saved beside the file.";
            _saved.Modulate = HudKit.Lime;
            _list.Clear(); // forces the list to redraw, so the file gets its * mark
            Rescan();
        }
        catch (Exception e)
        {
            _saved.Text = "Could not save: " + e.Message;
            _saved.Modulate = Palette.WoundsRed;
        }
    }

    private void ResetSettings()
    {
        if (_open == null) return;
        _settings = FirstGuess(_open, _asset);
        FillSettingBoxes();
        _saved.Text = "Reset to a first guess (not saved).";
        _saved.Modulate = HudKit.Dim;
        Rebuild();
    }

    private void ShowNothing()
    {
        _info.Text = "Nothing open yet.\n\nPut models in the UnitModels folder (Open folder), or drag a file onto this window.";
        _sizeLabel.Visible = false;
        _hudPreview.Visible = false;
        BuildReferences();
    }

    // ================================================================== putting it on the table

    private int CopyCount => _copies.Selected switch { 1 => 5, 2 => 10, 3 => 20, 4 => 100, _ => 1 };
    private Color Team => PlayerColorCodes.Options[Math.Max(0, _seat.Selected)].Color is var c ? Color.Color8(c.R, c.G, c.B) : Colors.White;
    private float BaseInches => _settings.BaseMm / UnitModelSettings.MmPerInch;
    private float FootprintInches => Mathf.Max(BaseInches, 0.8f);

    /// <summary>Rebuilds the models from the open file and the settings.</summary>
    private void Rebuild()
    {
        foreach (Node old in _group.GetChildren()) { _group.RemoveChild(old); old.QueueFree(); }
        _group.Rotation = Vector3.Zero;
        if (_asset == null) { ShowNothing(); return; }
        if (_asset.Error != null)
        {
            _info.Text = _asset.Error;
            _info.Modulate = Palette.WoundsRed;
            _sizeLabel.Visible = false;
            _hudPreview.Visible = false;
            BuildReferences();
            return;
        }
        _info.Modulate = Colors.White;

        int n = CopyCount, perRow = Math.Min(n, 10), rows = (n + perRow - 1) / perRow;
        float step = FootprintInches + 1f; // 1" between bases: in coherency, the way a unit is set up
        Transform3D? pivot = null;
        Aabb shape = new();
        for (int i = 0; i < n; i++)
        {
            int r = i / perRow, c = i % perRow;
            var holder = new Node3D { Position = new Vector3((c - (perRow - 1) / 2f) * step, 0, (r - (rows - 1) / 2f) * step) };
            _group.AddChild(holder);
            if (_settings.BaseMm > 0) TableLook.AddBase(holder, BaseInches / 2, Team);
            Node3D body = _asset.IsPicture ? Standee(_asset.Picture!) : (Node3D)_asset.Model!.Duplicate();
            var p = new Node3D { Name = "Pivot" };
            holder.AddChild(p);
            p.AddChild(body);
            if (pivot == null) (pivot, shape) = Normalise(p, body);
            else p.Transform = pivot.Value;
        }
        PlayAnimation();

        // Its finished size, on a label above the first model.
        float heightIn = shape.Size.Y;
        var first = (Node3D)_group.GetChild(0);
        _sizeLabel.Visible = true;
        _sizeLabel.Text = $"{heightIn * UnitModelSettings.MmPerInch:0} mm  /  {heightIn:0.00}\"";
        _sizeLabel.Position = first.Position + new Vector3(0, TableLook.BaseTop + heightIn + 0.35f, 0);

        _info.Text = InfoText(shape);
        ShowHud();
        BuildReferences();
    }

    /// <summary>
    /// Stands the model up, turns it, sizes it and puts its feet on the base - the same steps, from the same saved
    /// numbers, the table will follow. Returns the pivot's transform and the finished model's box.
    /// </summary>
    private (Transform3D, Aabb) Normalise(Node3D pivot, Node3D body)
    {
        if (_asset!.IsPicture)
        {
            // A picture is sized by its own sprite (Standee); it only needs turning. Its box is the picture's own.
            pivot.Rotation = new Vector3(0, Mathf.DegToRad(_settings.YawDegrees), 0);
            Texture2D tex = _asset.Picture!;
            float hIn = StandeeHeightInches, wIn = hIn * tex.GetWidth() / Mathf.Max(1, tex.GetHeight());
            float foot = _settings.BaseMm > 0 ? TableLook.BaseTop : 0;
            return (pivot.Transform, new Aabb(new Vector3(-wIn / 2, foot, -0.01f), new Vector3(wIn, hIn, 0.02f)));
        }
        return ModelPlacement.Place(pivot, body, _settings);
    }

    /// <summary>A picture standing up on the base, always turned to the camera - how the Raylib build shows a unit's picture.</summary>
    private Node3D Standee(Texture2D tex)
    {
        float hIn = StandeeHeightInches;
        var s = new Sprite3D
        {
            Texture = tex, PixelSize = hIn / Mathf.Max(1, tex.GetHeight()), Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Shaded = false, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        s.Position = new Vector3(0, (_settings.BaseMm > 0 ? TableLook.BaseTop : 0) + hIn / 2, 0);
        var root = new Node3D();
        root.AddChild(s);
        return root;
    }

    private float StandeeHeightInches => (_settings.HeightMm > 0 ? _settings.HeightMm : UnitModelSettings.GuessHeightMm(_settings.BaseMm)) / UnitModelSettings.MmPerInch;

    private void PlayAnimation()
    {
        string? name = _anim.Disabled || _anim.Selected <= 0 ? null : _anim.GetItemText(_anim.Selected);
        foreach (AnimationPlayer p in AssetLoader.All<AnimationPlayer>(_group))
        {
            if (name == null) { p.Stop(); continue; }
            if (p.GetAnimation(name) is { } a) a.LoopMode = Animation.LoopModeEnum.Linear;
            p.Play(name);
        }
    }

    private string InfoText(Aabb shape)
    {
        var a = _asset!;
        var lines = new List<string> { Path.GetFileName(a.File) };
        if (a.IsPicture) lines.Add("Picture - shown standing on the base, and below as the HUD shows a unit picture.");
        else
        {
            lines.Add($"{a.Triangles:N0} triangles, {a.Meshes} mesh{(a.Meshes == 1 ? "" : "es")}, {a.Materials} material{(a.Materials == 1 ? "" : "s")}");
            int copies = CopyCount;
            if (copies > 1) lines.Add($"x{copies} on the table = {a.Triangles * (long)copies:N0} triangles");
            if (a.Animations.Count > 0) lines.Add($"{a.Animations.Count} animation{(a.Animations.Count == 1 ? "" : "s")}");
        }
        lines.Add($"Stands {shape.Size.Y * UnitModelSettings.MmPerInch:0} mm tall, {shape.Size.X * UnitModelSettings.MmPerInch:0} x {shape.Size.Z * UnitModelSettings.MmPerInch:0} mm across");
        if (_settings.BaseMm > 0 && (shape.Size.X > BaseInches * 1.6f || shape.Size.Z > BaseInches * 1.6f))
            lines.Add($"Hangs well past its {_settings.BaseMm:0.#} mm base - right base size, or a bigger base?");
        foreach (string t in a.Textures.Take(4)) lines.Add("Skin: " + t);
        foreach (string w in a.Warnings) lines.Add("! " + w);
        return string.Join("\n", lines);
    }

    // ================================================================== the HUD preview

    private void ShowHud()
    {
        foreach (Node c in _hudSlots.GetChildren()) c.QueueFree();
        Texture2D? pic = _asset?.IsPicture == true ? _asset.Picture : null;
        _hudPreview.Visible = pic != null;
        if (pic == null) return;
        string name = Path.GetFileNameWithoutExtension(_asset!.File);
        // The army-list row (36), the unit card (46) and the battle report line (26), at the sizes the battle uses.
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        foreach (var (size, label) in new[] { (26f, "Report"), (36f, "Army list"), (46f, "Unit card"), (96f, "Big") })
        {
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 2);
            var p = HudKit.Portrait(pic, "person", size, Team);
            p.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            col.AddChild(p);
            col.AddChild(HudKit.Text(label, 12, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
            row.AddChild(col);
        }
        _hudSlots.AddChild(row);
        var listRow = new HBoxContainer();
        listRow.AddThemeConstantOverride("separation", 8);
        listRow.AddChild(HudKit.Portrait(pic, "person", 36, HudKit.Lime));
        listRow.AddChild(HudKit.Text(name.ToUpperInvariant(), 17, HudKit.Fg, HudKit.Bold));
        listRow.AddChild(HudKit.Chip("Q4+", HudKit.Teal, true));
        listRow.AddChild(HudKit.Chip("D4+", HudKit.Teal, true));
        _hudSlots.AddChild(listRow);
    }

    // ================================================================== things of known size

    private void BuildReferences()
    {
        foreach (Node n in _refs.GetChildren()) { _refs.RemoveChild(n); n.QueueFree(); }
        int n2 = CopyCount, perRow = Math.Min(n2, 10), rows = (n2 + perRow - 1) / perRow;
        float step = FootprintInches + 1f;
        float halfW = (perRow - 1) / 2f * step + FootprintInches / 2, halfD = (rows - 1) / 2f * step + FootprintInches / 2;
        Color grey = new(0.62f, 0.64f, 0.66f);

        // Left of the models, from near to far: today's stand-in, a 28mm person, the bases, a 3" wall.
        float x = -halfW - 1.6f;
        if (_showToken.ButtonPressed && _settings.BaseMm > 0)
        {
            float r = Mathf.Max(0.4f, BaseInches / 2);
            x -= r;
            var tok = new Node3D { Position = new Vector3(x, 0, 0) };
            _refs.AddChild(tok);
            TableLook.AddBase(tok, BaseInches / 2, Team);
            var body = TableLook.Mat(Team.Darkened(0.45f).Lerp(new Color(0.55f, 0.55f, 0.55f), 0.25f), 0.55f);
            float h = 1.05f; // the table's stand-in for a one-wound model (BattleTable.MakeModel)
            TableLook.Mesh(tok, new CapsuleMesh { Radius = r * 0.42f, Height = h }, body, new Vector3(0, 0.25f + h / 2, 0));
            TableLook.Mesh(tok, new SphereMesh { Radius = r * 0.3f, Height = r * 0.6f }, body, new Vector3(0, 0.3f + h, 0));
            Tag(tok.Position + new Vector3(0, 2.2f, 0), "Today's token");
            x -= r + 1.4f;
        }
        {
            // A 28mm-scale person: 32 mm to the top of the head, on a 25 mm base.
            float baseR = 25 / UnitModelSettings.MmPerInch / 2, hIn = 32 / UnitModelSettings.MmPerInch;
            x -= baseR;
            var man = new Node3D { Position = new Vector3(x, 0, 0) };
            _refs.AddChild(man);
            TableLook.AddBase(man, baseR, new Color(0.5f, 0.5f, 0.5f));
            var m = TableLook.Mat(grey, 0.7f);
            float y0 = TableLook.BaseTop, head = hIn * 0.13f, legs = hIn * 0.47f, torso = hIn - head * 2 - legs;
            TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, m, new Vector3(-0.08f, y0 + legs / 2, 0));
            TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, m, new Vector3(0.08f, y0 + legs / 2, 0));
            TableLook.Mesh(man, new CapsuleMesh { Radius = 0.17f, Height = torso + 0.1f }, m, new Vector3(0, y0 + legs + torso / 2, 0));
            TableLook.Mesh(man, new SphereMesh { Radius = head, Height = head * 2 }, m, new Vector3(0, y0 + hIn - head, 0));
            Tag(man.Position + new Vector3(0, y0 + hIn + 0.45f, 0), "28mm person (32mm)");
            x -= baseR + 1.4f;
        }
        foreach (float mm in new[] { 25f, 32, 40, 50, 60 })
        {
            float r = mm / UnitModelSettings.MmPerInch / 2;
            x -= r;
            var b = new Node3D { Position = new Vector3(x, 0, 0) };
            _refs.AddChild(b);
            TableLook.AddBase(b, r, new Color(0.5f, 0.5f, 0.5f));
            Tag(b.Position + new Vector3(0, 0.75f, 0), $"{mm:0}mm");
            x -= r + 0.7f;
        }
        {
            // 3" tall wall - a typical cover / ruin height on the table.
            x -= 0.6f;
            TableLook.Mesh(_refs, new BoxMesh { Size = new Vector3(1, 3, 3) }, TableLook.Mat(TableTerrainGrey, 0.9f), new Vector3(x, 1.5f, 0));
            Tag(new Vector3(x, 3.5f, 0), "3\" wall");
        }

        // A 6" ruler in front of the models, inch marks and numbers.
        float z = halfD + 1.4f, x0 = -3f;
        var rulerMat = TableLook.Mat(new Color(0.55f, 0.46f, 0.26f), 0.6f);
        TableLook.Mesh(_refs, new BoxMesh { Size = new Vector3(6, 0.06f, 0.5f) }, rulerMat, new Vector3(0, 0.04f, z));
        var tick = TableLook.Mat(new Color(0.1f, 0.1f, 0.1f), 0.8f);
        for (int i = 0; i <= 6; i++)
        {
            TableLook.Mesh(_refs, new BoxMesh { Size = new Vector3(0.04f, 0.08f, i % 3 == 0 ? 0.36f : 0.22f) }, tick, new Vector3(x0 + i, 0.06f, z - 0.07f));
            var l = new Label3D
            {
                Text = i.ToString(), Font = HudKit.Bold, FontSize = 32, PixelSize = 0.006f, Modulate = new Color(0.1f, 0.1f, 0.1f),
                Position = new Vector3(x0 + i, 0.075f, z + 0.12f), RotationDegrees = new Vector3(-90, 0, 0), OutlineSize = 0,
            };
            _refs.AddChild(l);
        }
        Tag(new Vector3(3.6f, 0.4f, z), "6\"");
        _refs.Visible = _showRefs.ButtonPressed;
    }

    private static readonly Color TableTerrainGrey = BattleTable.TerrainColour(FDG.ETerrainType.Blocking);

    private void Tag(Vector3 at, string text)
    {
        _refs.AddChild(new Label3D
        {
            Text = text, Font = HudKit.Bold, FontSize = 30, PixelSize = 0.0035f, OutlineSize = 8, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = at, Modulate = HudKit.Fg, OutlineModulate = new Color(0, 0, 0, 0.8f),
        });
    }

    /// <summary>Screenshot driver: 0-4 = 1 / 5 / 10 / 20 / 100 copies.</summary>
    public void SetCopies(int i) { _copies.Select(Math.Clamp(i, 0, 4)); Rebuild(); }

    /// <summary>Screenshot driver: set a settings box by node name (Height, Facing, Base, UpAxis, Lighting, Seat, ShowRefs, Spin...).</summary>
    public void SetBox(string box, string value)
    {
        switch (FindChild(box, true, false))
        {
            case SpinBox sb when double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d): sb.Value = d; break;
            case OptionButton ob when int.TryParse(value, out int i): ob.Select(i); ob.EmitSignal(OptionButton.SignalName.ItemSelected, i); break;
            case CheckBox cb: cb.ButtonPressed = value is "1" or "true" or "on"; break;
        }
    }

    // ================================================================== camera and input

    /// <summary>0 close up, 1 play distance, 2 whole table, 3 top view.</summary>
    public void View(int which)
    {
        float groupSize = Mathf.Max(1f, (Math.Min(CopyCount, 10) - 1) * (FootprintInches + 1f));
        float modelH = _sizeLabel.Visible ? _sizeLabel.Position.Y : 1.5f;
        _topView = which == 3;
        switch (which)
        {
            // Close up takes in the model and the 28mm person beside it, for a size check at a glance.
            case 0: _focus = new Vector3(-groupSize * 0.15f - 1.2f, modelH * 0.45f, 0); _distance = Mathf.Max(5f, Mathf.Max(modelH * 3.2f, groupSize * 1.1f)); _pitch = 16; _yawCam = 12; break;
            case 1: _focus = new Vector3(-3, 0, 0); _distance = Mathf.Max(30f, groupSize * 1.4f); _pitch = 48; _yawCam = 0; break;
            case 2: _focus = Vector3.Zero; _distance = Mathf.Max(TableW, TableD) * 0.95f + 10f; _pitch = 55; _yawCam = 0; break;
            case 3: _focus = Vector3.Zero; _distance = Mathf.Max(24f, groupSize * 1.6f); _pitch = 89.9f; _yawCam = 0; break;
        }
        ApplyCamera();
    }

    private void ApplyCamera()
    {
        float pitch = _topView ? 89.9f : _pitch;
        Vector3 dir = new(Mathf.Sin(Mathf.DegToRad(_yawCam)) * Mathf.Cos(Mathf.DegToRad(pitch)), Mathf.Sin(Mathf.DegToRad(pitch)),
            Mathf.Cos(Mathf.DegToRad(_yawCam)) * Mathf.Cos(Mathf.DegToRad(pitch)));
        _camera.Position = _focus + dir * _distance;
        _camera.LookAt(_focus, _topView ? Vector3.Forward : Vector3.Up);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouse) return;
        bool onView = GetViewport().GuiGetHoveredControl() == this;
        if (e is InputEventMouseButton { Pressed: true } && !onView) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Max(1.2f, _distance * 0.9f); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Min(220f, _distance * 1.1f); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                _dragRotate = mb.Pressed ? mb.Position : null; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragPan = mb.Pressed ? mb.Position : null; break;
            case InputEventMouseMotion mm when _dragRotate is { } r:
                _yawCam -= (mm.Position.X - r.X) * 0.3f;
                if (!_topView) _pitch = Mathf.Clamp(_pitch + (mm.Position.Y - r.Y) * 0.25f, 2f, 88f);
                _dragRotate = mm.Position;
                break;
            case InputEventMouseMotion mm when _dragPan is { } p:
                Vector2 d = (mm.Position - p) * -_distance * 0.0016f;
                Vector3 right = _camera.GlobalBasis.X with { Y = 0 }, fwd = (-_camera.GlobalBasis.Z) with { Y = 0 };
                if (_topView) fwd = -_camera.GlobalBasis.Y with { Y = 0 };
                _focus += right.Normalized() * d.X - fwd.Normalized() * d.Y;
                _dragPan = mm.Position;
                break;
            default: return;
        }
        ApplyCamera();
        AcceptEvent();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.T }: View(_topView ? 1 : 3); break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F }: View(0); break;
            case InputEventKey { Pressed: true, Keycode: Key.Q }: _yawCam += 5; ApplyCamera(); break;
            case InputEventKey { Pressed: true, Keycode: Key.E }: _yawCam -= 5; ApplyCamera(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_spin.ButtonPressed) _group.RotateY((float)delta * 0.6f);
        _fps.Text = $"{Engine.GetFramesPerSecond():0} FPS";

        // New files, and the open one changing on disk (saved again from Blender): look every couple of seconds.
        _scanTimer += delta;
        if (_scanTimer < 2) return;
        _scanTimer = 0;
        Rescan();
        if (_open != null && File.Exists(_open) && File.GetLastWriteTimeUtc(_open) != _openStamp) Open(_open);
    }
}
