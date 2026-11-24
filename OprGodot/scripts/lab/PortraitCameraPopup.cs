using FdgRaylib.Models3D;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// Kai, 2026-11-14: "more control over making the portrait for the 3D models - zoom in on the model, take a screenshot of
/// just the model from that angle, and have that be the portrait." A square camera on the model alone (no table, no
/// stand-in person), starting at the usual portrait angle: drag to turn round it, right-drag to move, wheel to zoom.
/// Take portrait saves exactly what the square shows (the model on a clear background - the game puts its backdrop
/// behind it) as the portrait of that model version.
/// </summary>
public partial class PortraitCameraPopup : Control
{
    public event Action? Saved;

    /// <summary>Where the picture is saved instead of the model version's portrait (a Quest hero's own portrait).</summary>
    public string? SaveTo { get; set; }

    private readonly string _model;
    private readonly string _title;
    private readonly string _faction, _unit;
    private CropBoxes _boxes = null!;
    private VSlider _height = null!;
    private PortraitShot.Studio _studio = null!;
    private TextureRect _view = null!;
    private Label _status = null!;
    private Vector3 _focus;
    private float _yaw, _pitch, _distance;
    private bool _orbit, _pan;
    private const int Side = 640;

    public PortraitCameraPopup() : this("", "", "", "") { }

    public PortraitCameraPopup(string model, string title, string faction, string unit)
    {
        _model = model;
        _title = title;
        _faction = faction;
        _unit = unit;
    }

    /// <summary>Opens over the whole screen, whatever <paramref name="from"/> sits in.</summary>
    public static PortraitCameraPopup Open(Node from, string model, string title, string faction = "", string unit = "")
    {
        var layer = new CanvasLayer { Layer = 50 };
        from.GetTree().Root.AddChild(layer);
        var p = new PortraitCameraPopup(model, title, faction, unit);
        p.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(p);
        p.TreeExiting += () => layer.QueueFree();
        return p;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(veil);

        _studio = new PortraitShot.Studio(1024);
        AddChild(_studio);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(HudKit.Text("PORTRAIT CAMERA", 30, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(HudKit.Text(_title, 16, HudKit.Dim, HudKit.Semi));
        col.AddChild(HudKit.Text("Drag to turn round the model - right-drag to move it - mouse wheel to zoom. The square is the portrait.", 15, HudKit.Dim, HudKit.Semi));

        var frame = new Control { CustomMinimumSize = new Vector2(Side, Side), ClipContents = true };
        var back = new TextureRect { Texture = HudKit.Backdrop, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(back);
        _view = new TextureRect { Name = "PortraitView", Texture = _studio.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        _view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _view.GuiInput += ViewInput;
        frame.AddChild(_view);
        // Kai, 2026-11-14: boxes showing the part of the portrait each card shows, to line the model up in them.
        _boxes = new CropBoxes(PictureFraming.For(_faction, _unit)) { MouseFilter = MouseFilterEnum.Ignore };
        _boxes.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(_boxes);
        var viewRow = new HBoxContainer();
        viewRow.AddThemeConstantOverride("separation", 10);
        viewRow.AddChild(HudKit.Panel(HudKit.PanelBox(8), frame));
        // The boxes' height: where the cards' crop sits on the portrait (saved as the unit's framing, as in Frame pictures).
        var heightCol = new VBoxContainer();
        heightCol.AddChild(HudKit.Text("Box", 13, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
        heightCol.AddChild(HudKit.Text("height", 13, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
        _height = new VSlider { Name = "BoxHeight", MinValue = 0, MaxValue = 1, Step = 0.005, Value = 1 - _boxes.Frame.Y, SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter, TooltipText = "Move the card boxes up or down the portrait - where the unit card and army cards cut it. Saved for this unit." };
        _height.ValueChanged += v =>
        {
            _boxes.Frame = _boxes.Frame with { Y = 1 - (float)v };
            _boxes.QueueRedraw();
            if (_unit.Length > 0) PictureFraming.Set(_faction, _unit, _boxes.Frame);
            HudKit.ForgetPictures();
        };
        heightCol.AddChild(_height);
        viewRow.AddChild(heightCol);
        col.AddChild(viewRow);

        _status = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        col.AddChild(_status);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Btn("ResetCamera", "Reset view", "Back to the usual portrait angle.", StartView));
        var show = new CheckBox { Name = "ShowCropBoxes", Text = "Show card boxes", ButtonPressed = true, FocusMode = FocusModeEnum.None,
            TooltipText = "Outline the part of the portrait the unit card and the army cards / list show." };
        show.Toggled += on => _boxes.Visible = on;
        buttons.AddChild(show);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons.AddChild(Btn("TakePortrait", "Take portrait", "Save what the square shows as this version's portrait.", () => _ = Take()).Also(b => b.ThemeTypeVariation = "CommitButton"));
        buttons.AddChild(Btn("ClosePortraitCamera", "Close", "Close without taking a portrait.", QueueFree));
        col.AddChild(buttons);
        HudKit.Place(this, HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(col, 22, 18, 22, 18)), LayoutPreset.Center);

        Callable.From(Load).CallDeferred();
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(150, 40), FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    private void Load()
    {
        if (!_studio.Show(_model, out string? why)) { _status.Text = "The model would not load: " + why; return; }
        StartView();
    }

    /// <summary>The usual portrait angle (the automatic one), read back into turn / tilt / distance so dragging carries on from it.</summary>
    private void StartView()
    {
        _studio.Aim(PortraitShot.Portrait);
        Camera3D cam = _studio.Camera;
        Aabb crop = PortraitShot.Crop(_studio.Box, PortraitShot.Portrait);
        _focus = crop.GetCenter();
        Vector3 d = cam.Position - _focus;
        _distance = d.Length();
        _yaw = Mathf.Atan2(d.X, d.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(d.Y / Mathf.Max(_distance, 1e-4f), -1, 1));
        cam.Fov = PortraitShot.Portrait.Fov;
        Aim();
        _status.Text = "";
    }

    private void Aim()
    {
        Camera3D cam = _studio.Camera;
        var dir = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
        cam.Position = _focus + dir * _distance;
        cam.LookAt(_focus, Vector3.Up);
    }

    private void ViewInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb: _orbit = mb.Pressed; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } mb: _pan = mb.Pressed; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }: _distance *= 0.92f; Aim(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }: _distance *= 1.08f; Aim(); break;
            case InputEventMouseMotion mm when _orbit:
                _yaw -= mm.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.01f, -1.2f, 1.45f);
                Aim();
                break;
            case InputEventMouseMotion mm when _pan:
            {
                // Move the point looked at across the view, so the model follows the mouse.
                Camera3D cam = _studio.Camera;
                float perPixel = 2 * _distance * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2) / Side;
                _focus += (-cam.GlobalBasis.X * mm.Relative.X + cam.GlobalBasis.Y * mm.Relative.Y) * perPixel;
                Aim();
                break;
            }
            default: return;
        }
        _view.AcceptEvent();
    }

    /// <summary>Screenshot driver: turn, tilt (degrees) and zoom (1 = the start), then take the portrait.</summary>
    public void Drive(float yawDeg, float pitchDeg, float zoom, bool take)
    {
        StartView();
        _yaw += Mathf.DegToRad(yawDeg);
        _pitch = Mathf.Clamp(_pitch + Mathf.DegToRad(pitchDeg), -1.2f, 1.45f);
        _distance /= Mathf.Max(0.1f, zoom);
        Aim();
        if (take) Callable.From(() => { _ = Take(); }).CallDeferred();
    }

    private async Task Take()
    {
        for (int i = 0; i < 2; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsInsideTree()) return;
        try
        {
            Image img = _studio.GetTexture().GetImage();
            img.Convert(Image.Format.Rgba8);
            img.Resize(512, 512, Image.Interpolation.Lanczos);
            string path = SaveTo ?? UnitPortraits.PathFor(_model);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Error err = img.SavePng(path);
            if (err != Error.Ok) { _status.Text = "Could not save it: " + err; return; }
            if (SaveTo == null) UnitPortraits.MarkChosen(_model);
            HudKit.ForgetPictures();
            _status.Text = "Saved - this is the portrait for this version now.";
            _status.Modulate = HudKit.Lime;
            Saved?.Invoke();
        }
        catch (Exception ex) { _status.Text = "Could not take it: " + ex.Message; }
    }

    /// <summary>
    /// The outlines over the camera: the unit card's banner (wide, the top of it shows over the name) and the army
    /// cards' / army list's banner (wider still), as the game crops this square picture - with the unit's framing from
    /// Frame pictures, if it has one.
    /// </summary>
    private sealed partial class CropBoxes : Control
    {
        public PictureFraming.Frame Frame;

        public CropBoxes() : this(PictureFraming.Frame.Default) { }

        public CropBoxes(PictureFraming.Frame frame) { Frame = frame; }

        public override void _Draw()
        {
            Vector2 s = Size;
            void Box(float aspect, Color c, string label, bool labelBelow)
            {
                var (x, y, w, h) = PictureFraming.Region(1, 1, aspect, Frame);
                var r = new Rect2(x * s.X, y * s.Y, w * s.X, h * s.Y);
                DrawRect(r.Grow(1), new Color(0, 0, 0, 0.5f), false, 3);
                DrawRect(r, c, false, 2);
                Font f = HudKit.Semi;
                Vector2 at = labelBelow ? new Vector2(r.Position.X + 6, r.End.Y - 8) : new Vector2(r.Position.X + 6, r.Position.Y + 18);
                DrawString(f, at + new Vector2(1, 1), label, HorizontalAlignment.Left, -1, 15, new Color(0, 0, 0, 0.8f));
                DrawString(f, at, label, HorizontalAlignment.Left, -1, 15, c);
            }
            Box(316f / HudKit.PictureSize.UnitCard, HudKit.Lime, "Unit card", false);
            Box(HudKit.PictureSize.BannerAspect, HudKit.Friendly, "Army cards / army list", true);
        }
    }
}
