using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;

namespace OprGodot.Maps;

/// <summary>
/// Kai, 2026-11-18: "a preview box of it being on a map with MAT 93 as the mat's base so I can see what the model
/// actually looks like". One ticked piece in Match terrain is drawn here exactly as a map draws it (same
/// <see cref="WarhammerModels.Dress"/>, same base trimming, real size) standing on battle mat 93. Drag to turn round
/// it, wheel to zoom. More than one ticked: a "Too many models selected" banner instead.
/// </summary>
public partial class TerrainPreview : PanelContainer
{
    /// <summary>The mat Kai asked for. Falls back to the felt when it isn't downloaded.</summary>
    public const int MatNumber = 93;
    private const float TableW = 72, TableD = 48;

    private SubViewport _view = null!;
    private Node3D _world = null!, _piece = null!;
    private Camera3D _cam = null!;
    private Label _banner = null!, _caption = null!;
    private SubViewportContainer _box = null!;
    private float _yaw = 35, _pitch = 35, _dist = 20;
    private bool _dragging;
    private Vector3 _focus;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", HudKit.PanelBox(8));
        CustomMinimumSize = new Vector2(400, 380);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        col.AddChild(HudKit.Heading("Preview", 14));

        _box = new SubViewportContainer { Stretch = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(380, 300), Name = "PreviewView" };
        _view = new SubViewport { OwnWorld3D = true, Size = new Vector2I(760, 600), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, Msaa3D = Viewport.Msaa.Msaa4X };
        _box.AddChild(_view);
        _world = new Node3D();
        _view.AddChild(_world);
        TableLook.AddLighting(_world);
        string? matLook = WarhammerTerrain.LoadMats()?.FirstOrDefault(m => m.Number == MatNumber)?.Look;
        TableLook.AddBoard(_world, TableW, TableD, matLook, floor: false);
        _piece = new Node3D { Name = "Piece" };
        _world.AddChild(_piece);
        _cam = new Camera3D { Fov = 40, Current = true };
        _world.AddChild(_cam);
        _box.GuiInput += OnInput;

        var stack = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        _box.SetAnchorsPreset(LayoutPreset.FullRect);
        stack.AddChild(_box);
        _banner = HudKit.Text("", 18, HudKit.Gold, HudKit.Exo(700));
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.SetAnchorsPreset(LayoutPreset.FullRect);
        _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_banner);
        col.AddChild(stack);
        _caption = HudKit.Text(matLook != null && WarhammerTerrain.MatFile(matLook) != null ? $"On Mat {MatNumber} - drag to turn, wheel to zoom"
            : $"Mat {MatNumber} isn't downloaded - shown on felt. Drag to turn, wheel to zoom", 12, HudKit.Dim, HudKit.Semi);
        col.AddChild(_caption);
        AddChild(col);
        ShowPiece(null, 0);
    }

    /// <summary>Shows <paramref name="entry"/> (null with <paramref name="ticked"/> 0 = nothing, more than 1 = the banner).</summary>
    public void ShowPiece(WarhammerTerrainLibrary.Entry? entry, int ticked)
    {
        if (_piece == null) return;
        foreach (Node c in _piece.GetChildren()) { _piece.RemoveChild(c); c.QueueFree(); }
        if (ticked > 1)
        {
            _banner.Text = "Too many models selected";
            _box.Visible = false;
            return;
        }
        _box.Visible = true;
        if (entry == null)
        {
            _banner.Text = "Tick a piece to see it here.";
            _box.Visible = false;
            return;
        }
        float w = Mathf.Max(0.5f, entry.Match.W), d = Mathf.Max(0.5f, entry.Match.D);
        var footprint = new[] { new Vector2(-w / 2, -d / 2), new Vector2(w / 2, -d / 2), new Vector2(w / 2, d / 2), new Vector2(-w / 2, d / 2) };
        bool drawn = WarhammerModels.Dress(_piece, entry.Look, footprint);
        _banner.Text = drawn ? "" : entry.Measured ? "Couldn't load this model." : "Still measuring this piece...";
        _focus = new Vector3(0, Mathf.Max(0.5f, entry.Match.H) * 0.4f, 0);
        _dist = Mathf.Clamp(Mathf.Max(Mathf.Max(w, d), entry.Match.H) * 1.8f, 6f, 60f);
        Aim();
    }

    private void OnInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _dragging = b.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _dist = Mathf.Max(3f, _dist * 0.9f); Aim(); AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _dist = Mathf.Min(80f, _dist * 1.1f); Aim(); AcceptEvent();
                break;
            case InputEventMouseMotion m when _dragging:
                _yaw -= m.Relative.X * 0.4f;
                _pitch = Mathf.Clamp(_pitch + m.Relative.Y * 0.3f, 8f, 85f);
                Aim();
                AcceptEvent();
                break;
        }
    }

    private void Aim()
    {
        float yaw = Mathf.DegToRad(_yaw), pitch = Mathf.DegToRad(_pitch);
        var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * _dist;
        _cam.Position = _focus + offset;
        _cam.LookAt(_focus, Vector3.Up);
    }
}
