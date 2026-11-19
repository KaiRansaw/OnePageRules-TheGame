using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Forge;
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
    private HBoxContainer _sinkRow = null!;
    private HSlider _sink = null!;
    private Label _sinkValue = null!;
    private WarhammerTerrainLibrary.Entry? _entry;

    /// <summary>Kai, 2026-11-18: the shown piece's sink was changed (already written to its match - save it).</summary>
    public event Action<WarhammerTerrainLibrary.Entry>? SinkChanged;
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
        // Kai, 2026-11-18: "adjust its base point, so if a model DOES have a base, the base is under the map".
        _sinkRow = new HBoxContainer { Name = "SinkRow" };
        _sinkRow.AddThemeConstantOverride("separation", 8);
        _sinkRow.AddChild(HudKit.Text("Sink / raise", 13, HudKit.Fg, HudKit.Semi).Also(l => l.TooltipText =
            "Right lowers the model so a sculpted base goes under the ground and out of sight; left of 0 raises a model that sits too deep. Saved for this model everywhere it's used (maps, battles). Looks only - the rules footprint doesn't move."));
        _sink = new HSlider { Name = "Sink", MinValue = -4, MaxValue = 4, Step = 0.05, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _sinkValue = HudKit.Text("0\"", 13, HudKit.Fg, HudKit.Semi);
        _sinkValue.CustomMinimumSize = new Vector2(48, 0);
        _sink.ValueChanged += v =>
        {
            _sinkValue.Text = $"{v:0.##}\"";
            if (_entry == null || Mathf.IsEqualApprox((float)v, _entry.Match.Sink)) return;
            _entry.Match.Sink = (float)v;
            Redraw();
            SinkChanged?.Invoke(_entry);
        };
        _sinkRow.AddChild(_sink);
        _sinkRow.AddChild(_sinkValue);
        col.AddChild(_sinkRow);
        AddChild(col);
        ShowPiece(null, 0);
    }

    /// <summary>Shows <paramref name="entry"/> (null with <paramref name="ticked"/> 0 = nothing, more than 1 = the banner).</summary>
    public void ShowPiece(WarhammerTerrainLibrary.Entry? entry, int ticked)
    {
        if (_piece == null) return;
        _entry = ticked == 1 ? entry : null;
        _sinkRow.Visible = _entry != null;
        if (_entry != null) _sink.SetValueNoSignal(_entry.Match.Sink);
        _sinkValue.Text = $"{_sink.Value:0.##}\"";
        if (ticked > 1)
        {
            Clear();
            _banner.Text = "Too many models selected";
            _box.Visible = false;
            return;
        }
        _box.Visible = true;
        if (entry == null)
        {
            Clear();
            _banner.Text = "Tick a piece to see it here.";
            _box.Visible = false;
            return;
        }
        float w = Mathf.Max(0.5f, entry.Match.W), d = Mathf.Max(0.5f, entry.Match.D);
        Redraw();
        _focus = new Vector3(0, Mathf.Max(0.5f, entry.Match.H) * 0.4f, 0);
        _dist = Mathf.Clamp(Mathf.Max(Mathf.Max(w, d), entry.Match.H) * 1.8f, 6f, 60f);
        Aim();
    }

    private void Clear()
    {
        foreach (Node c in _piece.GetChildren()) { _piece.RemoveChild(c); c.QueueFree(); }
    }

    /// <summary>The shown piece drawn again (its sink changed), keeping the camera where it is.</summary>
    private void Redraw()
    {
        Clear();
        if (_entry == null) return;
        float w = Mathf.Max(0.5f, _entry.Match.W), d = Mathf.Max(0.5f, _entry.Match.D);
        var footprint = new[] { new Vector2(-w / 2, -d / 2), new Vector2(w / 2, -d / 2), new Vector2(w / 2, d / 2), new Vector2(-w / 2, d / 2) };
        bool drawn = WarhammerModels.Dress(_piece, _entry.Look, footprint, _entry.Match.Sink);
        _banner.Text = drawn ? "" : _entry.Measured ? "Couldn't load this model." : "Still measuring this piece...";
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
