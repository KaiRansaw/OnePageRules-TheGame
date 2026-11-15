using FdgRaylib.Models3D;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// The Asset Lab's "Frame pictures" window (Kai, 2026-11-14: "an in-game editor to change where the portrait pictures
/// are centred and how zoomed in they are, with a preview, drag and drop to edit"). Pick a unit's picture, drag it to
/// move it, roll the mouse wheel (or the slider) to zoom; the army card, army list row and unit card next to it show
/// the result as the game will draw it. Saved as you go (<see cref="PictureFraming"/>).
/// </summary>
public partial class PictureFramingPopup : Control
{
    public event Action? Saved;

    private OptionButton _army = null!;
    private ItemList _units = null!;
    private Control _edit = null!;
    private TextureRect _editPic = null!;
    private HSlider _zoom = null!;
    private VBoxContainer _previews = null!;
    private Label _hint = null!;
    private readonly List<string> _armyFolders = new();
    private readonly List<string> _files = new();
    private Texture2D? _pic;
    private string _faction = "", _unit = "";
    private PictureFraming.Frame _frame = PictureFraming.Frame.Default;
    private bool _dragging, _quiet;

    // The edit area has the unit card's shape, drawn half as big again.
    private static readonly Vector2 CardSize = new(316, HudKit.PictureSize.UnitCard);
    private static readonly Vector2 EditSize = CardSize * 1.5f;

    public static PictureFramingPopup Open(Control parent)
    {
        var p = new PictureFramingPopup();
        p.SetAnchorsPreset(LayoutPreset.FullRect);
        parent.AddChild(p);
        return p;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(veil);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(HudKit.Text("FRAME PICTURES", 30, HudKit.Fg, HudKit.Exo(800)));
        var intro = HudKit.Text("Pick a unit, then drag its picture to move it and roll the mouse wheel to zoom. The army card, army list row and unit card " +
                                "on the right show it as the game will. Saved as you go.", 15, HudKit.Dim, HudKit.Semi);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(1300, 0);
        col.AddChild(intro);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 18);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        left.AddThemeConstantOverride("separation", 6);
        _army = new OptionButton { Name = "FrameArmy", TooltipText = "The army whose pictures to frame." };
        _army.ItemSelected += _ => FillUnits();
        left.AddChild(_army);
        _units = new ItemList { Name = "FrameUnits", CustomMinimumSize = new Vector2(280, 520), SelectMode = ItemList.SelectModeEnum.Single };
        _units.ItemSelected += i => Pick((int)i);
        left.AddChild(_units);
        body.AddChild(left);

        var mid = new VBoxContainer();
        mid.AddThemeConstantOverride("separation", 8);
        mid.AddChild(HudKit.Heading("Drag to move - wheel to zoom", 14));
        _edit = new Control { Name = "FrameEdit", CustomMinimumSize = EditSize, ClipContents = true, MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        _edit.GuiInput += EditInput;
        var back = new ColorRect { Color = new Color(0.03f, 0.05f, 0.06f), MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(LayoutPreset.FullRect);
        _edit.AddChild(back);
        _editPic = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        _editPic.SetAnchorsPreset(LayoutPreset.FullRect);
        _edit.AddChild(_editPic);
        mid.AddChild(HudKit.Panel(HudKit.PanelBox(8), _edit));
        var zoomRow = new HBoxContainer();
        zoomRow.AddThemeConstantOverride("separation", 10);
        zoomRow.AddChild(HudKit.Text("Zoom", 15, HudKit.Dim, HudKit.Semi));
        _zoom = new HSlider { Name = "FrameZoom", MinValue = 1, MaxValue = PictureFraming.MaxZoom, Step = 0.01, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _zoom.ValueChanged += v => { if (!_quiet) SetFrame(_frame with { Zoom = (float)v }); };
        zoomRow.AddChild(_zoom);
        zoomRow.AddChild(Btn("FrameReset", "Reset", "Back to centred, not zoomed.", () => SetFrame(PictureFraming.Frame.Default)));
        mid.AddChild(zoomRow);
        _hint = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        mid.AddChild(_hint);
        body.AddChild(mid);

        _previews = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        _previews.AddThemeConstantOverride("separation", 10);
        body.AddChild(_previews);
        col.AddChild(body);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddChild(Btn("CloseFraming", "Close", "Close this window.", () => { Saved?.Invoke(); QueueFree(); }));
        col.AddChild(buttons);
        HudKit.Place(this, HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(col, 22, 18, 22, 18)), LayoutPreset.Center);

        string root = OfficialPictures.Root;
        foreach (string d in Directory.EnumerateDirectories(root).OrderBy(d => d))
            if (Pictures(d).Any()) { _armyFolders.Add(d); _army.AddItem(Path.GetFileName(d)); }
        if (_armyFolders.Count == 0)
        {
            _hint.Text = "No unit pictures yet - Asset Lab -> Official pictures... downloads them.";
            _edit.Visible = false;
            return;
        }
        _army.Selected = 0;
        FillUnits();
    }

    private static IEnumerable<string> Pictures(string armyFolder)
    {
        string p = Path.Combine(armyFolder, "Portraits");
        return Directory.Exists(p)
            ? Directory.EnumerateFiles(p).Where(f => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f)
            : Enumerable.Empty<string>();
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(110, 36), FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    private void FillUnits()
    {
        _units.Clear();
        _files.Clear();
        if (_army.Selected < 0) return;
        _faction = Path.GetFileName(_armyFolders[_army.Selected]);
        foreach (string f in Pictures(_armyFolders[_army.Selected]))
        {
            _files.Add(f);
            string unit = Path.GetFileNameWithoutExtension(f);
            int i = _units.AddItem(unit + (PictureFraming.For(_faction, unit).IsDefault ? "" : "  (framed)"));
            _units.SetItemTooltip(i, unit);
        }
        if (_files.Count > 0) { _units.Select(0); Pick(0); }
    }

    private void Pick(int i)
    {
        if (i < 0 || i >= _files.Count) return;
        _unit = Path.GetFileNameWithoutExtension(_files[i]);
        Image? img = Image.LoadFromFile(_files[i]);
        _pic = img == null || img.IsEmpty() ? null : ImageTexture.CreateFromImage(img);
        _frame = PictureFraming.For(_faction, _unit);
        Redraw();
    }

    /// <summary>Screenshot driver: pick an army and unit by name, then move / zoom ("labframe:Alien Hives|Hive Lord|0.4|0.3|1.8").</summary>
    public void Drive(string army, string unit, PictureFraming.Frame? frame)
    {
        int a = _armyFolders.FindIndex(d => OfficialPictures.Normalise(Path.GetFileName(d)) == OfficialPictures.Normalise(army));
        if (a < 0) return;
        _army.Selected = a;
        FillUnits();
        int u = _files.FindIndex(f => OfficialPictures.Normalise(Path.GetFileNameWithoutExtension(f)) == OfficialPictures.Normalise(unit));
        if (u < 0) return;
        _units.Select(u);
        Pick(u);
        if (frame != null) SetFrame(frame);
    }

    private void SetFrame(PictureFraming.Frame f)
    {
        _frame = PictureFraming.Clamp(f);
        if (_unit.Length > 0) PictureFraming.Set(_faction, _unit, _frame);
        Redraw();
    }

    private void EditInput(InputEvent e)
    {
        if (_pic == null) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                SetFrame(_frame with { Zoom = _frame.Zoom * 1.08f });
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                SetFrame(_frame with { Zoom = _frame.Zoom / 1.08f });
                break;
            case InputEventMouseMotion mm when _dragging:
            {
                // The picture follows the mouse: moving it right shows more of its left, so the centre moves left.
                var (_, _, rw, rh) = PictureFraming.Region(_pic.GetWidth(), _pic.GetHeight(), EditSize.X / EditSize.Y, _frame);
                float dx = mm.Relative.X * rw / EditSize.X / _pic.GetWidth();
                float dy = mm.Relative.Y * rh / EditSize.Y / _pic.GetHeight();
                // Keep the centre where the crop can actually reach, so dragging back responds at once.
                var moved = KeepReachable(_frame with { X = _frame.X - dx, Y = _frame.Y - dy }, rw, rh);
                SetFrame(moved);
                break;
            }
        }
        _edit.AcceptEvent();
    }

    private PictureFraming.Frame KeepReachable(PictureFraming.Frame f, float rw, float rh)
    {
        float hw = rw / 2 / _pic!.GetWidth(), hh = rh / 2 / _pic.GetHeight();
        return f with { X = Mathf.Clamp(f.X, hw, 1 - hw), Y = Mathf.Clamp(f.Y, hh, 1 - hh) };
    }

    private void Redraw()
    {
        _quiet = true;
        _zoom.Value = _frame.Zoom;
        _quiet = false;
        foreach (Node n in _previews.GetChildren()) { _previews.RemoveChild(n); n.QueueFree(); }
        if (_pic == null) { _editPic.Texture = null; _hint.Text = "That picture could not be read."; return; }
        var (x, y, w, h) = PictureFraming.Region(_pic.GetWidth(), _pic.GetHeight(), EditSize.X / EditSize.Y, _frame);
        _editPic.Texture = new AtlasTexture { Atlas = _pic, Region = new Rect2(x, y, w, h) };
        _hint.Text = $"{_unit}: zoom {_frame.Zoom:0.0}x" + (_frame.IsDefault ? " (as it comes)" : "");

        _previews.AddChild(HudKit.Heading("Army Forge card", 13));
        _previews.AddChild(CardPreview());
        _previews.AddChild(HudKit.Heading("Battle army list", 13));
        _previews.AddChild(RowPreview());
        _previews.AddChild(HudKit.Heading("Unit card", 13));
        _previews.AddChild(UnitCardPreview());
    }

    // The three shapes, drawn the way ForgeWidgets.Card, BattleScreen.ArmyRow and BattleScreen.UnitInfo draw them.
    private Control CardPreview()
    {
        float h = HudKit.PictureSize.ForgeCard, w = h * HudKit.PictureSize.BannerAspect;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", -(int)(w * 0.3f));
        row.AddChild(HudKit.Banner(_pic, "person", w, h, HudKit.Friendly, frame: _frame));
        var t = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        t.AddChild(HudKit.OverPicture(HudKit.Text(_unit, 16, HudKit.Fg, HudKit.Bold)));
        t.AddChild(HudKit.OverPicture(HudKit.Text("pts", 14, HudKit.Dim, HudKit.Semi)));
        row.AddChild(t);
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 6, ContentMarginBottom = 6 };
        return HudKit.Panel(box, row).Also(p => p.CustomMinimumSize = new Vector2(w + 150, h + 12));
    }

    private Control RowPreview()
    {
        float h = HudKit.PictureSize.ArmyList, w = h * HudKit.PictureSize.BannerAspect;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", -(int)(w * 0.32f));
        row.AddChild(HudKit.Banner(_pic, "person", w, h, HudKit.Friendly, frame: _frame));
        var t = HudKit.OverPicture(HudKit.Text(_unit.ToUpperInvariant(), 15, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1)));
        t.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(t);
        var box = new ChamferBox { Fill = new Color(0, 0, 0, 0.25f), Border = HudKit.Edge with { A = 0.4f }, Cut = 6, ContentMarginLeft = 1, ContentMarginRight = 8, ContentMarginTop = 1, ContentMarginBottom = 1 };
        return HudKit.Panel(box, row).Also(p => p.CustomMinimumSize = new Vector2(286, 0));
    }

    private Control UnitCardPreview()
    {
        var head = new Control { CustomMinimumSize = CardSize };
        var banner = HudKit.Banner(_pic, "person", 0, CardSize.Y, HudKit.Friendly, down: true, frame: _frame);
        banner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        head.AddChild(banner);
        var names = new VBoxContainer();
        names.AddChild(HudKit.OverPicture(HudKit.Text(_unit.ToUpperInvariant(), 22, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1))));
        names.AddChild(HudKit.OverPicture(HudKit.Text(_faction, 14, HudKit.Dim, HudKit.Semi)));
        names.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        names.GrowVertical = GrowDirection.Begin;
        names.OffsetLeft = 4;
        head.AddChild(names);
        return HudKit.Panel(HudKit.PanelBox(8), head);
    }
}
