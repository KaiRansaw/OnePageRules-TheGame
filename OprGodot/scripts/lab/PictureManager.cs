using OprGodot.Forge;
using FdgRaylib.Pictures;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// The picture manager (Asset Lab -> Your pictures; Kai, 2026-11-16, "Menu Art &amp; Themes Mockup" round 2, all Keep -
/// "better than just a folder full of pictures"). Left: every place a picture can go, grouped (main-menu backdrops, the
/// six game tiles, every army of both families), ticked when set, with an All / Missing filter. Middle: the picture,
/// where a click or drag sets the focus point, a zoom slider, and Choose picture / Paste / Remove. Right: live previews
/// of every place that picture shows, each cut to its own shape around the focus point. The game keeps its own copy
/// (<see cref="MenuPictures"/>), and the copies ship with the game so friends see the same art.
/// </summary>
public partial class PictureManager : PanelContainer
{
    public event Action? Closed;

    private MenuPictures.Slot _slot = MenuPictures.Backdrop(MenuPictures.EFamily.GrimdarkFuture);
    private bool _missingOnly;
    private VBoxContainer _list = null!, _previews = null!;
    private Label _slotTitle = null!, _file = null!, _where = null!;
    private PictureCanvas _canvas = null!;
    private HSlider _zoom = null!;
    private Button _remove = null!, _all = null!, _missing = null!;
    private FileDialog? _dialog;
    private readonly List<PictureView> _views = new();

    public override void _Ready()
    {
        Name = "PictureManager";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink });
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 28);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 22);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        margin.AddChild(col);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 18);
        var back = Ui.Button("Back", Close, "Back to the Asset Lab (Esc). Every change is already saved.", "BackButton");
        back.Name = "PicturesBack";
        back.CustomMinimumSize = new Vector2(130, 48);
        top.AddChild(back);
        var title = new Label { Text = "Your pictures" };
        Ui.ScreenTitle(title);
        top.AddChild(title);
        top.AddChild(HudKit.Text("Asset Lab  >  Your pictures", 20, HudKit.Lime, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        top.AddChild(Ui.Grow());
        _all = Ui.Button("All", () => { _missingOnly = false; FillList(); }, "Every place a picture can go.");
        _all.Name = "FilterAll";
        _missing = Ui.Button("Missing", () => { _missingOnly = true; FillList(); }, "Only the places still without a picture.");
        _missing.Name = "FilterMissing";
        top.AddChild(_all);
        top.AddChild(_missing);
        col.AddChild(top);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 20);
        col.AddChild(body);

        // Left: the slots.
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(_list);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(10), scroll));

        // Middle: the picture and its controls.
        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.6f };
        mid.AddThemeConstantOverride("separation", 12);
        _slotTitle = HudKit.Text("", 30, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1));
        mid.AddChild(_slotTitle);
        _where = HudKit.Text("", 16, HudKit.Dim, HudKit.Semi);
        _where.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _where.CustomMinimumSize = new Vector2(10, 0);
        mid.AddChild(_where);
        var frame = new AspectRatioContainer { Ratio = 16f / 9f, SizeFlagsVertical = SizeFlags.ExpandFill };
        _canvas = new PictureCanvas { Name = "PictureCanvas" };
        _canvas.FocusMoved += (x, y, done) => { foreach (var v in _views) { v.FocusX = x; v.FocusY = y; } if (done) SaveFraming(); };
        frame.AddChild(_canvas);
        mid.AddChild(frame);
        var zoomRow = new HBoxContainer();
        zoomRow.AddThemeConstantOverride("separation", 12);
        zoomRow.AddChild(HudKit.Text("Zoom", 18, HudKit.Fg, HudKit.Semi));
        _zoom = new HSlider { Name = "Zoom", MinValue = 1, MaxValue = 3, Step = 0.05, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "How close every place crops in. 1 = the picture just fills the shape." };
        _zoom.ValueChanged += z => { foreach (var v in _views) v.Zoom = (float)z; };
        _zoom.DragEnded += _ => SaveFraming();
        zoomRow.AddChild(_zoom);
        mid.AddChild(zoomRow);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        var choose = Ui.Button("Choose picture...", ChooseFile, "Pick a PNG, JPG or WEBP picture. The game keeps its own copy.", "CommitButton");
        choose.Name = "ChoosePicture";
        choose.CustomMinimumSize = new Vector2(240, 50);
        buttons.AddChild(choose);
        var paste = Ui.Button("Paste", Paste, "Use a picture you've copied (in a browser: right-click the picture -> Copy image).");
        paste.Name = "PastePicture";
        paste.CustomMinimumSize = new Vector2(140, 50);
        buttons.AddChild(paste);
        _remove = Ui.Button("Remove", Remove, "Take this picture out. The place goes back to its stand-in.", "BackButton");
        _remove.Name = "RemovePicture";
        _remove.CustomMinimumSize = new Vector2(140, 50);
        buttons.AddChild(_remove);
        mid.AddChild(buttons);
        _file = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        mid.AddChild(_file);
        body.AddChild(mid);

        // Right: how it looks in every place it's used.
        var rightScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _previews = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _previews.AddThemeConstantOverride("separation", 6);
        rightScroll.AddChild(_previews);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(10), HudKit.Pad(rightScroll, 14, 12, 14, 12)).Also(p => { p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.SizeFlagsStretchRatio = 0.9f; }));

        FillList();
        Pick(_slot);
    }

    // ------------------------------------------------------------------ the list

    private static IEnumerable<(string Group, List<MenuPictures.Slot> Slots)> Groups()
    {
        yield return ("Main menu backdrops", new() { MenuPictures.Backdrop(MenuPictures.EFamily.GrimdarkFuture), MenuPictures.Backdrop(MenuPictures.EFamily.AgeOfFantasy) });
        yield return ("Game tiles", new[] { FDG.EGameSystem.GrimdarkFuture, FDG.EGameSystem.GFFirefight, FDG.EGameSystem.GFStarQuest, FDG.EGameSystem.AgeOfFantasy, FDG.EGameSystem.AoFSkirmish, FDG.EGameSystem.AoFQuest }
            .Select(MenuPictures.GameTile).ToList());
        foreach (var f in new[] { MenuPictures.EFamily.GrimdarkFuture, MenuPictures.EFamily.AgeOfFantasy })
            yield return ($"Armies - {MenuPictures.FamilyName(f)}", BookIndex.ArmiesOf(f).Select(a => MenuPictures.Army(f, a)).ToList());
    }

    private void FillList()
    {
        foreach (Node n in _list.GetChildren()) { _list.RemoveChild(n); n.QueueFree(); }
        int missing = 0;
        foreach (var (group, slots) in Groups())
        {
            var shown = slots.Where(s => !_missingOnly || MenuPictures.Get(s) == null).ToList();
            missing += slots.Count(s => MenuPictures.Get(s) == null);
            if (shown.Count == 0) continue;
            _list.AddChild(HudKit.Pad(HudKit.Heading(group, 14), 14, 12, 8, 4));
            foreach (var s in shown) _list.AddChild(SlotRow(s));
        }
        _missing.Text = $"Missing ({missing})";
        _all.ThemeTypeVariation = _missingOnly ? "" : "CommitButton";
        _missing.ThemeTypeVariation = _missingOnly ? "CommitButton" : "";
    }

    private Button SlotRow(MenuPictures.Slot s)
    {
        bool on = s.Key == _slot.Key, set = MenuPictures.Get(s) != null;
        var b = new Button { Name = "Slot_" + MenuPictures.SafeName(s.Key), Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 34), FocusMode = FocusModeEnum.None, TooltipText = s.Label };
        var box = new StyleBoxFlat { BgColor = on ? new Color(HudKit.Lime, 0.16f) : Colors.Transparent, BorderColor = on ? HudKit.Lime : Colors.Transparent, BorderWidthLeft = 4, ContentMarginLeft = 18, ContentMarginRight = 12 };
        b.AddThemeStyleboxOverride("normal", box);
        var hover = (StyleBoxFlat)box.Duplicate();
        hover.BgColor = new Color(HudKit.Lime, 0.1f);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", on ? HudKit.Fg : HudKit.Dim);
        b.AddThemeFontSizeOverride("font_size", 17);
        b.Text = s.Kind == MenuPictures.EKind.Backdrop ? MenuPictures.FamilyName(s.Family) : s.Label;
        var mark = HudKit.Text(set ? "SET" : "auto", 14, set ? HudKit.Lime : HudKit.Dim with { A = 0.6f }, HudKit.Bold, HorizontalAlignment.Right);
        mark.SetAnchorsPreset(LayoutPreset.RightWide);
        mark.OffsetRight = -12; mark.OffsetLeft = -80;
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.MouseFilter = MouseFilterEnum.Ignore;
        b.AddChild(mark);
        b.Pressed += () => Pick(s);
        return b;
    }

    // ------------------------------------------------------------------ the picked slot

    private void Pick(MenuPictures.Slot slot)
    {
        _slot = slot;
        FillList();
        var e = MenuPictures.Get(slot);
        Texture2D? tex = MenuArt.Texture(slot);
        _slotTitle.Text = slot.Label.ToUpperInvariant();
        _where.Text = slot.Kind switch
        {
            MenuPictures.EKind.Backdrop => $"The {MenuPictures.FamilyName(slot.Family)} half of the main menu, and behind its game screen.",
            MenuPictures.EKind.GameTile => $"This game's tile on the {MenuPictures.FamilyName(slot.Family)} game screen.",
            _ => $"This army's tile wherever an army is picked, its lobby seat strip and its side of the loading screen - in all three {MenuPictures.FamilyName(slot.Family)} games.",
        };
        _canvas.Picture = tex;
        _canvas.FocusX = e?.FocusX ?? 0.5f;
        _canvas.FocusY = e?.FocusY ?? 0.5f;
        _zoom.SetValueNoSignal(e?.Zoom ?? 1f);
        _zoom.Editable = tex != null;
        _remove.Disabled = tex == null;
        _file.Text = e == null ? "No picture yet - this place shows its stand-in." : $"{e.File}  -  {(tex != null ? $"{tex.GetWidth()} x {tex.GetHeight()}" : "can't be read")}";
        BuildPreviews(tex, e);
    }

    private void BuildPreviews(Texture2D? tex, MenuPictures.Entry? e)
    {
        _views.Clear();
        foreach (Node n in _previews.GetChildren()) { _previews.RemoveChild(n); n.QueueFree(); }
        (string, float)[] shapes = _slot.Kind switch
        {
            MenuPictures.EKind.Backdrop => new[] { ("Main menu (its half of the screen)", 0.89f), ("Game screen, behind the tiles", 16f / 9f) },
            MenuPictures.EKind.GameTile => new[] { ("Big tile (army vs army)", 1.55f), ("Small tile", 1.45f) },
            _ => new[] { ("Army tile", 3.6f), ("Lobby seat strip", 6f), ("Loading screen (its half)", 0.89f) },
        };
        foreach (var (label, ratio) in shapes)
        {
            _previews.AddChild(HudKit.Text(label, 16, HudKit.Dim, HudKit.Semi));
            var arc = new AspectRatioContainer { Ratio = ratio, CustomMinimumSize = new Vector2(0, ratio < 1.2f ? 300 : 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, AlignmentHorizontal = AspectRatioContainer.AlignmentMode.Begin };
            if (ratio >= 1.2f) arc.CustomMinimumSize = new Vector2(0, 460 / ratio);
            Control inner;
            if (tex != null)
            {
                var v = new PictureView { Picture = tex, FocusX = e!.FocusX, FocusY = e.FocusY, Zoom = e.Zoom };
                _views.Add(v);
                inner = v;
            }
            else inner = MenuArt.View(_slot, _slot.Kind == MenuPictures.EKind.Army ? (_slot.Family == MenuPictures.EFamily.AgeOfFantasy ? FDG.EGameSystem.AgeOfFantasy : FDG.EGameSystem.GrimdarkFuture) : null);
            if (_slot.Kind == MenuPictures.EKind.Army && ratio > 3)
            {
                // The tile's fade and name, as the army grid draws them.
                var holder = new Control { ClipContents = true };
                inner.SetAnchorsPreset(LayoutPreset.FullRect);
                inner.AnchorLeft = 0.28f;
                holder.AddChild(inner);
                var fade = new TextureRect
                {
                    Texture = new GradientTexture2D { Gradient = new Gradient { Colors = new[] { Palette.Panel, Palette.Panel with { A = 0 } }, Offsets = new[] { 0.25f, 0.62f } }, FillTo = new Vector2(1, 0), Width = 256, Height = 8 },
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
                };
                fade.SetAnchorsPreset(LayoutPreset.FullRect);
                holder.AddChild(fade);
                var name = HudKit.Text(_slot.Army!, 20, HudKit.Fg, HudKit.Bold);
                name.SetAnchorsPreset(LayoutPreset.CenterLeft);
                name.OffsetLeft = 14; name.OffsetTop = -14;
                holder.AddChild(name);
                inner = holder;
            }
            arc.AddChild(inner);
            _previews.AddChild(arc);
            _previews.AddChild(HudKit.Gap(0, 6));
        }
    }

    // ------------------------------------------------------------------ changes

    private void SaveFraming()
    {
        if (MenuPictures.Get(_slot) == null) return;
        MenuPictures.SetFraming(_slot, _canvas.FocusX, _canvas.FocusY, (float)_zoom.Value);
    }

    private void ChooseFile()
    {
        _dialog?.QueueFree();
        _dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = $"Choose a picture for {_slot.Label}", Filters = new[] { "*.png, *.jpg, *.jpeg, *.webp ; Pictures" },
        };
        _dialog.FileSelected += path => Store(() => MenuPictures.SetFromFile(_slot, path));
        AddChild(_dialog);
        _dialog.PopupCentered(new Vector2I(1100, 700));
    }

    private void Paste()
    {
        if (!DisplayServer.ClipboardHasImage() || DisplayServer.ClipboardGetImage() is not { } img || img.IsEmpty())
        {
            App.I.Message("Nothing to paste", "There's no picture on the clipboard. Copy one first (in a browser: right-click the picture -> Copy image).");
            return;
        }
        Store(() => MenuPictures.SetFromBytes(_slot, img.SavePngToBuffer()));
    }

    private void Store(Func<MenuPictures.Entry> set)
    {
        try { set(); }
        catch (Exception ex) { App.I.Message("Couldn't use that picture", ex.Message); return; }
        if (MenuArt.Texture(_slot) == null)
        {
            MenuPictures.Remove(_slot);
            App.I.Message("Couldn't read that picture", "The file isn't a picture the game can open. Try a PNG or JPG.");
        }
        Pick(_slot);
    }

    private void Remove()
    {
        MenuPictures.Remove(_slot);
        Pick(_slot);
    }

    private void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsVisibleInTree() || !e.IsActionPressed("ui_cancel")) return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>For the screenshot driver: pick a slot by key and give it a picture file.</summary>
    public void Drive(string key, string? file)
    {
        var slot = Groups().SelectMany(g => g.Slots).FirstOrDefault(s => s.Key == key);
        if (slot == null) return;
        Pick(slot);
        if (file != null) Store(() => MenuPictures.SetFromFile(slot, file));
    }
}

/// <summary>The whole picture, fitted, with the focus point as a dot; click or drag to move the dot.</summary>
public partial class PictureCanvas : Control
{
    /// <summary>x, y (0-1) and whether the drag has finished.</summary>
    public event Action<float, float, bool>? FocusMoved;
    private Texture2D? _picture;
    public Texture2D? Picture { get => _picture; set { _picture = value; QueueRedraw(); } }
    public float FocusX { get; set; } = 0.5f;
    public float FocusY { get; set; } = 0.5f;
    private bool _drag;

    public PictureCanvas()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Cross;
        Resized += QueueRedraw;
        TooltipText = "Click or drag to set the focus point - the part every place keeps in view.";
    }

    private Rect2 Fitted()
    {
        if (_picture == null) return new Rect2(Vector2.Zero, Size);
        Vector2 p = _picture.GetSize();
        float s = Math.Min(Size.X / p.X, Size.Y / p.Y);
        Vector2 sz = p * s;
        return new Rect2((Size - sz) / 2, sz);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.5f));
        DrawRect(new Rect2(Vector2.Zero, Size), HudKit.Edge, false, 1);
        if (_picture == null)
        {
            Font f = HudKit.Semi;
            string t = "No picture yet - Choose picture... or Paste";
            Vector2 ts = f.GetStringSize(t, HorizontalAlignment.Left, -1, 22);
            DrawString(f, (Size - ts) / 2 + new Vector2(0, ts.Y * 0.75f), t, HorizontalAlignment.Left, -1, 22, HudKit.Dim);
            return;
        }
        Rect2 r = Fitted();
        DrawTextureRect(_picture, r, false);
        Vector2 dot = r.Position + new Vector2(FocusX * r.Size.X, FocusY * r.Size.Y);
        DrawCircle(dot, 13, new Color(0, 0, 0, 0.55f));
        DrawCircle(dot, 10, Colors.White);
        DrawCircle(dot, 7, HudKit.Lime);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_picture == null) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            _drag = mb.Pressed;
            Move(mb.Position, !mb.Pressed);
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _drag) { Move(mm.Position, false); AcceptEvent(); }
    }

    private void Move(Vector2 at, bool done)
    {
        Rect2 r = Fitted();
        FocusX = Math.Clamp((at.X - r.Position.X) / r.Size.X, 0, 1);
        FocusY = Math.Clamp((at.Y - r.Position.Y) / r.Size.Y, 0, 1);
        QueueRedraw();
        FocusMoved?.Invoke(FocusX, FocusY, done);
    }
}
