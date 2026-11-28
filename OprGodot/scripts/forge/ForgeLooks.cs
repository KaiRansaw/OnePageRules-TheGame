using FdgRaylib.Forge;
using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;
using OprGodot.Lab;

namespace OprGodot.Forge;

/// <summary>
/// Choosing each model's look - mockup A "Squad on the table" (Kai, 2026-11-27; replaces the slot grid of 2026-11-12).
/// The whole squad stands in a real 3D view - both halves of a combined unit, each model in its current version on its
/// game base, in a tidy block. Click a model to pick it, Shift/Ctrl-click to add or take away, drag a box to pick
/// several, double-click to pick every model with the same look. The version cards along the bottom give their version
/// to every picked model at once; hovering a card shows the picked models wearing it first. Only downloaded versions are
/// listed; models nobody picks use the unit's first version. The colour painter and the portrait buttons sit in the
/// "Colours &amp; portrait" side section.
/// <para>The picking and giving is <see cref="LooksPicker"/> (FdgApp, tested). The host keeps the picker, so a rebuild of
/// the screen (Save does one) keeps the picks. Used by the Army Forge and by the Horde Shop's recruits.</para>
/// </summary>
public partial class LooksPanel : VBoxContainer
{
    private readonly LooksPicker _p;
    private readonly Action _done;
    private readonly Action<string, Color> _status;
    private readonly string _where;
    private IReadOnlyList<string> _versions = Array.Empty<string>();
    private SquadView _squad = null!;
    private Label _pickedLine = null!, _cardsHeading = null!;
    private readonly List<(Button Card, Label Count, TextureRect Picture, Label Name, CardRing Ring)> _cards = new();
    private int _hover = -1;
    private Control _side = null!;
    private VBoxContainer _sideBody = null!;

    /// <summary>Whether the Colours &amp; portrait side section is open (kept for the run).</summary>
    private static bool _sideOpen;

    public LooksPanel() : this(null!, () => { }, (_, _) => { }) { }

    /// <param name="where">The screen it opens from, for the top bar ("ARMY FORGE", "HORDE SHOP").</param>
    internal LooksPanel(LooksPicker picker, Action done, Action<string, Color> status, string where = "ARMY FORGE")
    {
        _p = picker;
        _where = where;
        _done = done;
        _status = status;
    }

    private ForgeSession S => _p.Session;

    private int Row => _p.Row;

    /// <summary>The unit's base across, inches (round, as the table plays it).</summary>
    internal float BaseInches => _p.Valid ? S.Detail(Row).Unit.Base.RoundDiameterInches : 1f;

    public override void _Ready()
    {
        if (_p == null || !_p.Valid) return;
        AddThemeConstantOverride("separation", 12);
        var (unit, _) = S.Detail(Row);
        _versions = S.Versions(ForgeWidgets.Models(), unit.Name);
        var slots = _p.Slots();

        // ---- top bar: ARMY FORGE - LOOKS, the unit and its model count, DONE
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 16);
        var kicker = HudKit.Text(_where + " · LOOKS", 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 3));
        kicker.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(kicker);
        var name = new HBoxContainer();
        name.AddThemeConstantOverride("separation", 8);
        name.AddChild(HudKit.Text(unit.Name.ToUpperInvariant(), 26, HudKit.Fg, HudKit.Bold));
        name.AddChild(HudKit.Text($"[{slots.Count}]" + (slots.Count > unit.ModelCount ? "  COMBINED" : ""), 26, HudKit.Dim, HudKit.Semi));
        head.AddChild(name);
        head.AddChild(HudKit.Grow());
        var more = new Button
        {
            Name = "LooksMore", Text = "Colours & portrait", ToggleMode = true, ButtonPressed = _sideOpen, FocusMode = FocusModeEnum.None,
            TooltipText = "Open or close the side section: repaint the picked models' colours, and take the portrait the game shows for a version.",
            CustomMinimumSize = new Vector2(0, 42),
        };
        more.Toggled += on => { _sideOpen = on; _side.Visible = on; if (on) RefreshSide(); };
        head.AddChild(more);
        var done = Ui.Button("DONE", _done, "Back to the army (Esc). Looks are kept in the list - save the army to keep them in the file.", "CommitButton");
        done.Name = "Done";
        done.CustomMinimumSize = new Vector2(120, 42);
        head.AddChild(done);
        AddChild(head);

        // ---- the squad on the table, with the side section beside it
        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 12);
        AddChild(middle);
        var squadCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        squadCol.AddThemeConstantOverride("separation", 10);
        squadCol.AddChild(ForgeWidgets.Wrapped("The whole squad, standing in 3D. Click a model to pick it, Shift or Ctrl-click to add or take one away, drag a box to pick several, double-click a model to pick every model with the same look. Right-drag turns the table, the mouse wheel zooms.", 14, HudKit.Dim));
        _squad = new SquadView(this) { Name = "Squad", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        squadCol.AddChild(_squad);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Ui.Button("Pick all", () => { _p.PickAll(); Sync(); }, "Pick every model of the unit.").Also(b => b.Name = "PickAll"));
        buttons.AddChild(Ui.Button("Pick none", () => { _p.PickNone(); Sync(); }, "Pick no models.").Also(b => b.Name = "PickNone"));
        var mix = Ui.Button("Mix all versions evenly", Mix,
            "Deal the downloaded versions round the whole squad - model 1 the first version, model 2 the second, and round again - so each version is worn by as many models as the others.");
        mix.Name = "MixEvenly";
        mix.Disabled = _versions.Count < 2;
        buttons.AddChild(mix);
        buttons.AddChild(HudKit.Grow());
        _pickedLine = HudKit.Text("", 16, HudKit.Lime, HudKit.Bold);
        _pickedLine.Name = "PickedLine";
        buttons.AddChild(_pickedLine);
        squadCol.AddChild(buttons);
        middle.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(squadCol, 16, 14, 16, 12)).Also(p => { p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.SizeFlagsVertical = SizeFlags.ExpandFill; }));

        _sideBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sideBody.AddThemeConstantOverride("separation", 10);
        var sideScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        sideScroll.AddChild(_sideBody);
        _side = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(sideScroll, 14, 12, 14, 12));
        _side.Name = "LooksSide";
        _side.CustomMinimumSize = new Vector2(360, 0);
        _side.Visible = _sideOpen;
        middle.AddChild(_side);

        // ---- the version cards
        var strip = new VBoxContainer();
        strip.AddThemeConstantOverride("separation", 10);
        _cardsHeading = HudKit.Text("", 14, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2));
        strip.AddChild(_cardsHeading);
        if (_versions.Count == 0)
            strip.AddChild(ForgeWidgets.Wrapped("No models are downloaded for this unit yet, so every model stands as a token. Get them in the Asset Lab (Download models / Match models); they show up here.", 15, Palette.HitsYellow));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        for (int v = 0; v < _versions.Count; v++) row.AddChild(Card(v));
        var cardScroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, _versions.Count == 0 ? 0 : 214) };
        cardScroll.AddChild(row);
        strip.AddChild(cardScroll);
        AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(strip, 16, 12, 16, 12)));

        Sync();
        if (_sideOpen) RefreshSide();
        _ = MakeCardPictures();
    }

    // ================================================================== the cards

    private Control Card(int v)
    {
        string file = _versions[v];
        var b = new Button { Name = $"Version{v + 1}", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(210, 206), ClipContents = true };
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 4);
        var frame = new Control { CustomMinimumSize = new Vector2(0, 120), MouseFilter = MouseFilterEnum.Ignore };
        var back = new TextureRect { Texture = HudKit.Backdrop, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(back);
        var pic = new TextureRect { Name = "Picture", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(pic);
        col.AddChild(frame);
        var title = HudKit.Text($"Version {v + 1}" + (v == 0 ? "  (first)" : ""), 16, HudKit.Fg, HudKit.Bold);
        title.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(title);
        var fileName = HudKit.Text(Path.GetFileNameWithoutExtension(file), 12, HudKit.Dim, HudKit.Semi);
        fileName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        fileName.CustomMinimumSize = new Vector2(180, 0);
        fileName.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(fileName);
        var count = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        count.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(count);
        b.AddChild(HudKit.Pad(col, 10, 10, 10, 8).Also(p => { p.MouseFilter = MouseFilterEnum.Ignore; p.SetAnchorsPreset(LayoutPreset.FullRect); }));
        var ring = new CardRing { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        ring.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        b.AddChild(ring);
        b.Pressed += () => Give(v);
        b.MouseEntered += () => HoverVersion(v);
        b.MouseExited += () => { if (_hover == v) HoverVersion(-1); };
        _cards.Add((b, count, pic, title, ring));
        return b;
    }

    /// <summary>Each card's picture: the version's own portrait when one was taken, else a picture of the model made
    /// here (kept for the run).</summary>
    private async Task MakeCardPictures()
    {
        for (int v = 0; v < _versions.Count && v < _cards.Count; v++)
        {
            string file = _versions[v];
            if (CardPicture(file) is { } known) { _cards[v].Picture.Texture = known; continue; }
            var studio = new PortraitShot.Studio(384);
            AddChild(studio);
            try
            {
                if (!studio.Show(file, out _)) continue;
                studio.Aim(PortraitShot.Portrait);
                for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!IsInsideTree()) return;
                var tex = ImageTexture.CreateFromImage(studio.Grab(256));
                Thumbs[file] = tex;
                if (v < _cards.Count && IsInstanceValid(_cards[v].Picture)) _cards[v].Picture.Texture = tex;
            }
            finally { studio.QueueFree(); }
        }
    }

    private static readonly Dictionary<string, Texture2D> Thumbs = new();

    private static Texture2D? CardPicture(string file)
    {
        if (UnitPortraits.IsChosen(file) && Image.LoadFromFile(UnitPortraits.PathFor(file)) is { } img) return ImageTexture.CreateFromImage(img);
        return Thumbs.TryGetValue(file, out var t) ? t : null;
    }

    // ================================================================== picking and giving

    /// <summary>Puts every label, card and model in step with the picks and looks.</summary>
    public void Sync()
    {
        if (_squad == null || !_p.Valid) return;
        var slots = _p.Slots();
        int picked = _p.Picked.Count;
        _pickedLine.Text = _p.PickedLine();
        _pickedLine.AddThemeColorOverride("font_color", picked > 0 ? HudKit.Lime : HudKit.Dim);
        _cardsHeading.Text = _versions.Count == 0 ? "VERSIONS - NONE DOWNLOADED"
            : picked == 0 ? "VERSIONS - PICK MODELS ABOVE, THEN CLICK ONE TO GIVE IT TO THEM"
            : $"VERSIONS - CLICK ONE TO GIVE IT TO THE {picked} PICKED MODEL{(picked == 1 ? "" : "S")}";
        int on = _p.PickedVersion(_versions);
        for (int v = 0; v < _cards.Count; v++)
        {
            int n = _p.Wearing(v, _versions);
            _cards[v].Count.Text = $"{n} model{(n == 1 ? "" : "s")}";
            bool lit = v == on;
            // The card on every picked model is ringed lime (drawn over the card: the theme's frame art shows no state on a tall box).
            var box = new ChamferBox { Fill = HudKit.Well, Border = HudKit.Edge, Cut = 8 };
            _cards[v].Ring.Visible = lit;
            _cards[v].Name.AddThemeColorOverride("font_color", lit ? HudKit.Lime : HudKit.Fg);
            _cards[v].Count.AddThemeColorOverride("font_color", lit ? HudKit.Lime : HudKit.Dim);
            HudKit.StyleButton(_cards[v].Card, box, HudKit.Fg, 14);
            _cards[v].Card.TooltipText = picked == 0
                ? $"Version {v + 1}. Pick models above first, then click this to give it to them."
                : $"Give version {v + 1} to the {picked} picked model{(picked == 1 ? "" : "s")}.";
        }
        _squad.Show(Files(), Paints(slots), _p.Picked);
        if (_sideOpen) RefreshSide();
    }

    /// <summary>The file each model shows now: its own look, or - while a card is hovered - that card's version on the
    /// picked models.</summary>
    private List<string?> Files()
    {
        var slots = _p.Slots();
        var files = slots.Select(s => LooksPicker.FileOf(s, _versions)).ToList();
        if (_hover >= 0 && _hover < _versions.Count)
            foreach (int i in _p.Picked) if (i < files.Count) files[i] = _versions[_hover];
        return files;
    }

    private static List<string?> Paints(List<ForgeSession.ModelSlot> slots) => slots.Select(s => s.Paint).ToList();

    private void HoverVersion(int v)
    {
        if (_hover == v) return;
        _hover = v;
        _squad?.Show(Files(), Paints(_p.Slots()), _p.Picked);
    }

    /// <summary>A click / box / double-click in the 3D view (<see cref="SquadView"/>).</summary>
    internal void ClickModel(int i, bool add)
    {
        if (i < 0) { if (!add) _p.PickNone(); }
        else _p.Click(i, add);
        Sync();
    }

    internal void DoubleClickModel(int i)
    {
        if (i < 0) return;
        _p.PickSameLook(i, _versions);
        _status($"{_p.PickedLine()} - every model wearing {(LooksPicker.FileOf(_p.Slots()[i], _versions) is { } f ? ModelLooks.Label(_versions, f) : "the token")}.", Palette.Lime);
        Sync();
    }

    internal void BoxModels(IEnumerable<int> inside, bool add)
    {
        _p.Box(inside, add);
        Sync();
    }

    /// <summary>Gives version <paramref name="v"/> (0 = first) to every picked model.</summary>
    public void Give(int v)
    {
        if (v < 0 || v >= _versions.Count) return;
        int n = _p.Give(v, _versions);
        if (n == 0) _status("Pick one or more models first (click a model, drag a box, or Pick all), then click a version.", Palette.HitsYellow);
        else _status($"{n} model{(n == 1 ? "" : "s")} now wear{(n == 1 ? "s" : "")} {ModelLooks.Label(_versions, _versions[v])}.", Palette.Lime);
        Sync();
    }

    public void Mix()
    {
        if (_versions.Count < 2) { _status("Mixing needs two or more downloaded versions.", Palette.HitsYellow); return; }
        _p.MixEvenly(_versions);
        _status($"The {_p.Slots().Count} models now wear the {_versions.Count} versions in turn.", Palette.Lime);
        Sync();
    }

    // ---- scripted runs ("forge:sel:0;1", "forge:version:2", "forge:mix", "forge:click:3", "forge:dbl:3", "forge:box:2;3", "forge:hover:2")

    /// <summary>Picks exactly these models (0-based).</summary>
    public void Select(IEnumerable<int> models) { _p.PickExactly(models); Sync(); }

    public void SelectAll() { _p.PickAll(); Sync(); }

    /// <summary>Gives version <paramref name="number"/> (1 = first) to the picked models.</summary>
    public void PickVersion(int number) => Give(number - 1);

    /// <summary>Every model back to the first version.</summary>
    public void Reset()
    {
        S.SetLook(_p.Slots(), null);
        Sync();
    }

    /// <summary>A click on model <paramref name="i"/> (0-based) in the 3D view, as the mouse does it.</summary>
    public void DriveClick(int i, bool add) => _squad.ClickAt(_squad.ScreenSpot(i), add, doubleClick: false);

    public void DriveDoubleClick(int i) => _squad.ClickAt(_squad.ScreenSpot(i), add: false, doubleClick: true);

    /// <summary>A box dragged round these models in the 3D view.</summary>
    public void DriveBox(IEnumerable<int> models, bool add) => _squad.BoxAround(models.ToList(), add);

    /// <summary>Hovers card <paramref name="number"/> (1 = first; 0 = none).</summary>
    public void DriveHover(int number) => HoverVersion(number - 1);

    // ================================================================== the side section: colours and portrait

    /// <summary>The version the painter and the portrait buttons work on: the last-clicked picked model's.</summary>
    private string? Target => _p.FocusVersion(_versions) is int v && v >= 0 ? _versions[v] : null;

    private void RefreshSide()
    {
        if (_sideBody == null || !IsInstanceValid(_sideBody)) return;
        foreach (Node c in _sideBody.GetChildren()) { _sideBody.RemoveChild(c); c.QueueFree(); }
        string? target = Target;
        _sideBody.AddChild(HudKit.Heading(target == null ? "Colours & portrait" : ModelLooks.Label(_versions, target), 16));
        if (target == null)
        {
            _sideBody.AddChild(ForgeWidgets.Wrapped(_versions.Count == 0
                ? "No models are downloaded for this unit yet."
                : "Pick a model to repaint its colours or take the portrait of the version it wears.", 14, HudKit.Dim));
            return;
        }
        _sideBody.AddChild(PortraitRow(target));
        _sideBody.AddChild(ColoursBox(target));
        _sideBody.AddChild(ForgeWidgets.Wrapped("Looks and colours are saved in the army file; a friend sees them if they have those models downloaded.", 13, HudKit.Dim));
    }

    private Control ColoursBox(string target)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        int picked = _p.Picked.Count;
        box.AddChild(HudKit.Heading($"Colours - repaint the {picked} picked model{(picked == 1 ? "" : "s")}", 15));
        if (SquadView.Source(target) is not { } shown)
        {
            box.AddChild(ForgeWidgets.Wrapped("This model would not load, so its colours can't be read.", 13, HudKit.Dim));
            return box;
        }
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 6);
        int n = 0;
        foreach (var (colour, share) in ModelPainter.MainColours(shown))
        {
            int from = ModelPainter.Rgb(colour);
            var swatch = new ColorPickerButton
            {
                Name = $"Colour{++n}", Color = colour, EditAlpha = false, CustomMinimumSize = new Vector2(52, 36), FocusMode = FocusModeEnum.None,
                TooltipText = $"About {share * 100:0}% of this model. Click to choose what this colour becomes on the picked models.",
            };
            // Deferred: the repaint rebuilds this section, and freeing the swatch while its own popup is still closing
            // crashed the game (Kai, 2026-11-15, Build 2.36 - "Parameter data.tree is null", then signal 11).
            swatch.PopupClosed += () => { int to = ModelPainter.Rgb(swatch.Color); Callable.From(() => Repaint(from, to)).CallDeferred(); };
            row.AddChild(swatch);
        }
        box.AddChild(row);
        if (n == 0) box.AddChild(ForgeWidgets.Wrapped("This model has no colours the painter can read.", 13, HudKit.Dim));

        // What the focused model has now, each swap with a button to undo it.
        var slots = _p.Slots();
        int focus = _p.Anchor >= 0 && _p.IsPicked(_p.Anchor) ? _p.Anchor : _p.Picked.FirstOrDefault(-1);
        foreach (ColourSwap sw in focus >= 0 && focus < slots.Count ? ModelPaint.Parse(slots[focus].Paint) : new List<ColourSwap>())
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new ColorRect { Color = ModelPainter.ColorOf(sw.From), CustomMinimumSize = new Vector2(28, 22) });
            line.AddChild(HudKit.Text("->", 15, HudKit.Dim, HudKit.Bold));
            line.AddChild(new ColorRect { Color = ModelPainter.ColorOf(sw.To), CustomMinimumSize = new Vector2(28, 22) });
            int from = sw.From;
            line.AddChild(Ui.Button("Undo", () => Callable.From(() => Repaint(from, null)).CallDeferred(), "Take this colour change off the picked models.", "BackButton"));
            box.AddChild(line);
        }
        var clear = Ui.Button("Original colours", () => Callable.From(() =>
        {
            if (_p.Picked.Count == 0) return;
            var now = _p.Slots();
            S.ClearPaint(_p.Picked.Where(i => i < now.Count).Select(i => now[i]));
            Sync();
        }).CallDeferred(), "Take every colour change off the picked models.", "BackButton");
        clear.Name = "ClearPaint";
        clear.Disabled = picked == 0;
        box.AddChild(clear);
        box.AddChild(ForgeWidgets.Wrapped("Up to 4 colour changes a model. Other players see them too.", 13, HudKit.Dim));
        return box;
    }

    /// <summary>Screenshot driver: opens swatch <paramref name="n"/>'s colour popup, picks <paramref name="rgb"/> and closes it.</summary>
    public void DriveSwatch(int n, int rgb)
    {
        if (!_sideOpen) { _sideOpen = true; _side.Visible = true; RefreshSide(); }
        if (FindChild($"Colour{n}", true, false) is not ColorPickerButton b) { _status($"No swatch {n}.", Palette.WoundsRed); return; }
        b.GetPopup().Popup();
        b.Color = ModelPainter.ColorOf(rgb);
        b.GetPopup().Hide();
    }

    /// <summary>Gives the picked models a colour swap (or, with <paramref name="to"/> null, takes it off).</summary>
    public void Repaint(int from, int? to)
    {
        if (_p.Picked.Count == 0) { _status("Pick one or more models first, then click a colour.", Palette.HitsYellow); return; }
        if (to == from) return;
        var slots = _p.Slots();
        S.SetPaint(_p.Picked.Where(i => i < slots.Count).Select(i => slots[i]), from, to);
        int n = _p.Picked.Count;
        _status(to == null ? "Colour change taken off." : $"{n} model{(n == 1 ? "" : "s")} repainted.", Palette.Lime);
        Sync();
    }

    // ---- the version's portrait (Kai, 2026-11-14: "choose which model you want THEN make the portrait that the game
    // then uses for that model variant")

    private Control PortraitRow(string target)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        var frame = new Control { CustomMinimumSize = new Vector2(150, 150), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        var back = new TextureRect { Texture = HudKit.Backdrop, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(back);
        bool chosen = UnitPortraits.IsChosen(target);
        var portrait = new TextureRect
        {
            Name = "VersionPortrait", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = chosen && Image.LoadFromFile(UnitPortraits.PathFor(target)) is { } img ? ImageTexture.CreateFromImage(img) : null,
        };
        portrait.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(portrait);
        col.AddChild(HudKit.Panel(HudKit.PanelBox(6), frame).Also(p => p.SizeFlagsHorizontal = SizeFlags.ShrinkCenter));
        string which = ModelLooks.Label(_versions, target);
        col.AddChild(ForgeWidgets.Wrapped(chosen
            ? $"{which} has its own portrait - the game shows it for models wearing this look. Take it again to redo it."
            : $"No portrait of {which} yet - the unit shows its official picture.", 13, HudKit.Dim));
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        // Kai, 2026-11-14: "more control - zoom in on the model, take a screenshot of just the model from that angle".
        var camera = Ui.Button("Take portrait...", () => OpenCamera(),
            "Open a camera on just this version: turn, move and zoom until it looks right, then take the picture. It becomes this unit's portrait whenever a model wears this look - ahead of the official picture.");
        camera.Name = "PortraitCamera";
        camera.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(camera);
        var quick = Ui.Button("Quick portrait", () => _ = MakePortrait(), "Take it at once from the usual angle, without the camera.");
        quick.Name = "MakePortrait";
        quick.Disabled = _making;
        buttons.AddChild(quick);
        col.AddChild(buttons);
        return col;
    }

    private PortraitCameraPopup? OpenCamera()
    {
        if (Target is not { } file) return null;
        string unit = S.Detail(Row).Unit.Name;
        var cam = PortraitCameraPopup.Open(this, file, $"{unit} - {ModelLooks.Label(_versions, file)}", S.Book.Faction, unit);
        cam.Saved += () => { HudKit.ForgetPictures(); RefreshCardPicture(file); RefreshSide(); _status($"Portrait taken for {ModelLooks.Label(_versions, file)}.", Palette.Lime); };
        return cam;
    }

    /// <summary>Screenshot driver: open the portrait camera, turn / tilt / zoom it, and optionally take the picture.</summary>
    public void DriveCamera(float yaw, float pitch, float zoom, bool take)
    {
        var cam = OpenCamera();
        if (cam != null) Callable.From(() => cam.Drive(yaw, pitch, zoom, take)).CallDeferred();
    }

    private bool _making;

    private async Task MakePortrait()
    {
        if (_making || Target is not { } file) return;
        _making = true;
        var studio = new PortraitShot.Studio();
        AddChild(studio);
        try
        {
            if (!studio.Show(file, out string? why)) { _status("The model would not load: " + why, Palette.WoundsRed); return; }
            studio.Aim(PortraitShot.Portrait);
            for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!IsInsideTree()) return;
            string path = UnitPortraits.PathFor(file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Error e = studio.Grab().SavePng(path);
            if (e != Error.Ok) { _status("Could not save the portrait: " + e, Palette.WoundsRed); return; }
            UnitPortraits.MarkChosen(file);
            HudKit.ForgetPictures();
            RefreshCardPicture(file);
            _status($"Portrait made for {ModelLooks.Label(_versions, file)}.", Palette.Lime);
        }
        finally
        {
            studio.QueueFree();
            _making = false;
            RefreshSide();
        }
    }

    private void RefreshCardPicture(string file)
    {
        int v = _versions.ToList().IndexOf(file);
        if (v >= 0 && v < _cards.Count && CardPicture(file) is { } t) _cards[v].Picture.Texture = t;
    }

    /// <summary>Screenshot driver: make the portrait of the focused version.</summary>
    public void DriveMakePortrait() => _ = MakePortrait();
}

/// <summary>The lime ring round the version card every picked model wears.</summary>
public partial class CardRing : Control
{
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.One * 1.5f, Size - Vector2.One * 3), HudKit.Lime, false, 3);
        DrawRect(new Rect2(Vector2.One * 5, Size - Vector2.One * 10), HudKit.Lime with { A = 0.3f }, false, 2);
    }
}

/// <summary>
/// The squad standing on a patch of table in its own 3D world (the Looks screen's main view): every model on its game
/// base in <see cref="SquadFormation"/>, "Model N" under it, picked models outlined in lime with a lime base ring and
/// label. Left-click picks, a left-drag draws a box, a double-click picks the same look, right-drag turns, the wheel zooms.
/// </summary>
public partial class SquadView : SubViewportContainer
{
    private readonly LooksPanel _panel;
    private SubViewport _vp = null!;
    private Node3D _world = null!, _turn = null!;
    private Camera3D _cam = null!;
    private BoxOverlay _overlay = null!;
    private readonly List<Spot> _spots = new();
    private float _yaw = 0.2f, _zoom = 1f, _fit = 10f, _focusY = 0.6f;
    private float _baseInches = 1f;
    private Vector2 _pressAt;
    private bool _pressing, _boxing, _turning, _skipRelease;

    private sealed class Spot
    {
        public Node3D Root = null!, Holder = null!, Base = null!;
        public Label3D Label = null!;
        public string? File = "\u0001", Paint;
        public bool Picked;
        public Aabb Box; // in the spot's own space
    }

    public SquadView() : this(null!) { }

    internal SquadView(LooksPanel panel)
    {
        _panel = panel;
        Stretch = true;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(400, 300);
    }

    public override void _Ready()
    {
        _vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
        AddChild(_vp);
        _world = new Node3D();
        _vp.AddChild(_world);
        TableLook.AddLighting(_world);
        _turn = new Node3D();
        _world.AddChild(_turn);
        _cam = new Camera3D { Fov = 34, Near = 0.05f, Far = 800, Current = true };
        _world.AddChild(_cam);
        _overlay = new BoxOverlay { MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);
        Resized += Aim;
    }

    // ---------------------------------------------------------------- models

    private static readonly Dictionary<string, (Node3D? Model, UnitModelSettings Settings)> Loaded = new();

    /// <summary>A version as loaded (its own sculpted base cut off, as on the table) - the colour painter reads it. Kept for the run.</summary>
    public static Node3D? Source(string file) => Load(file).Model;

    private static (Node3D? Model, UnitModelSettings Settings) Load(string file)
    {
        if (Loaded.TryGetValue(file, out var got) && (got.Model == null || IsInstanceValid(got.Model))) return got;
        LoadedAsset a = AssetLoader.Load(file);
        UnitModelSettings s = UnitModelSettings.Load(file)?.Copy() ?? new UnitModelSettings { HeightMm = 32, BaseMm = 25 };
        if (s.BaseMm <= 0) s.BaseMm = 1; // feet on the game's base, whatever base the model was saved with (as the table)
        Node3D? model = a.Model == null ? null : OwnBase.Strip(a.Model, s).Model;
        return Loaded[file] = (model, s);
    }

    /// <summary>Stands the squad: <paramref name="files"/> (null = a token) and paints per model, the picked ones lit.</summary>
    internal void Show(IReadOnlyList<string?> files, IReadOnlyList<string?> paints, IReadOnlyCollection<int> picked)
    {
        if (_turn == null) { CallDeferred(nameof(ShowLater)); _pending = (files, paints, picked); return; }
        bool laid = _spots.Count != files.Count;
        if (laid) Lay(files.Count);
        for (int i = 0; i < _spots.Count; i++)
        {
            Spot sp = _spots[i];
            string? paint = i < paints.Count ? paints[i] : null;
            bool on = picked.Contains(i);
            if (sp.File != files[i] || sp.Paint != paint)
            {
                sp.File = files[i];
                sp.Paint = paint;
                Body(sp);
                sp.Picked = !on; // outline again below
            }
            if (sp.Picked != on)
            {
                sp.Picked = on;
                Light(sp);
            }
        }
        if (laid) Aim();
    }

    private (IReadOnlyList<string?>, IReadOnlyList<string?>, IReadOnlyCollection<int>)? _pending;

    private void ShowLater()
    {
        if (_pending is { } p) { _pending = null; Show(p.Item1, p.Item2, p.Item3); }
    }

    /// <summary>Lays out <paramref name="count"/> spots in formation on a patch of table sized to fit them.</summary>
    private void Lay(int count)
    {
        foreach (Node c in _turn.GetChildren()) { _turn.RemoveChild(c); c.QueueFree(); }
        _spots.Clear();
        _baseInches = _panel?.BaseInches ?? 1f;
        var spots = SquadFormation.Spots(count, _baseInches);
        float w = spots.Count == 0 ? 6 : spots.Max(s => Mathf.Abs(s.X)) * 2 + _baseInches + 4;
        float d = spots.Count == 0 ? 6 : spots.Max(s => Mathf.Abs(s.Z)) * 2 + _baseInches + 4;
        TableLook.AddBoard(_turn, Mathf.Max(w, 8), Mathf.Max(d, 6));
        for (int i = 0; i < spots.Count; i++)
        {
            var root = new Node3D { Name = $"Model{i + 1}", Position = new Vector3(spots[i].X, 0, spots[i].Z) };
            _turn.AddChild(root);
            var holder = new Node3D();
            root.AddChild(holder);
            var label = new Label3D
            {
                Text = $"Model {i + 1}", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, NoDepthTest = true,
                PixelSize = 0.0007f, FontSize = 30, OutlineSize = 10, Font = HudKit.Bold, RenderPriority = 2, OutlineRenderPriority = 1,
                Position = new Vector3(0, 0.02f, _baseInches / 2 + 0.3f), Modulate = HudKit.Dim, OutlineModulate = new Color(0, 0, 0, 0.85f),
            };
            root.AddChild(label);
            _spots.Add(new Spot { Root = root, Holder = holder, Label = label });
        }
        _fit = 0;
        Aim();
    }

    private void Body(Spot sp)
    {
        foreach (Node c in sp.Holder.GetChildren()) { sp.Holder.RemoveChild(c); c.QueueFree(); }
        float rad = Mathf.Max(0.4f, _baseInches / 2);
        if (sp.File is { } file && Load(file) is { Model: { } source } got)
        {
            var pivot = new Node3D();
            sp.Holder.AddChild(pivot);
            var body = (Node3D)source.Duplicate();
            pivot.AddChild(body);
            ModelPainter.Apply(body, sp.Paint);
            var (_, box) = ModelPlacement.Place(pivot, body, got.Settings);
            sp.Box = box.Merge(new Aabb(new Vector3(-rad, 0, -rad), new Vector3(rad * 2, 0.3f, rad * 2)));
        }
        else
        {
            // No model downloaded: the table's token.
            var mat = TableLook.SharedMat(new Color(0.42f, 0.45f, 0.48f), 0.55f);
            TableLook.Mesh(sp.Holder, TableLook.SharedMesh($"tokcap:{rad}:1.05", () => new CapsuleMesh { Radius = rad * 0.42f, Height = 1.05f }), mat, new Vector3(0, 0.25f + 0.525f, 0));
            TableLook.Mesh(sp.Holder, TableLook.SharedMesh($"tokhead:{rad}", () => new SphereMesh { Radius = rad * 0.3f, Height = rad * 0.6f }), mat, new Vector3(0, 0.3f + 1.05f, 0));
            sp.Box = new Aabb(new Vector3(-rad, 0, -rad), new Vector3(rad * 2, 1.35f + rad * 0.3f, rad * 2));
        }
    }

    private static Shader? _outline;

    /// <summary>A lime shell drawn round a picked model: its own surfaces pushed out a little, back faces only.</summary>
    private static ShaderMaterial Outline()
    {
        _outline ??= new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_front, depth_draw_opaque;
                uniform vec4 colour : source_color = vec4(0.72, 0.94, 0.29, 1.0);
                uniform float width = 0.035;
                void vertex() {
                    vec3 n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
                    vec4 w = MODEL_MATRIX * vec4(VERTEX, 1.0);
                    w.xyz += n * width;
                    POSITION = PROJECTION_MATRIX * VIEW_MATRIX * w;
                }
                void fragment() { ALBEDO = colour.rgb; }
                """,
        };
        var m = new ShaderMaterial { Shader = _outline };
        m.SetShaderParameter("colour", HudKit.Lime);
        return m;
    }

    private static ShaderMaterial? _outlineMat;

    private void Light(Spot sp)
    {
        if (sp.Base != null && IsInstanceValid(sp.Base)) { sp.Root.RemoveChild(sp.Base); sp.Base.QueueFree(); }
        sp.Base = new Node3D();
        sp.Root.AddChild(sp.Base);
        TableLook.AddBase(sp.Base, _baseInches / 2, sp.Picked ? HudKit.Lime : new Color(0.32f, 0.38f, 0.42f));
        _outlineMat ??= Outline();
        foreach (MeshInstance3D mi in AssetLoader.All<MeshInstance3D>(sp.Holder)) mi.MaterialOverlay = sp.Picked ? _outlineMat : null;
        sp.Label.Modulate = sp.Picked ? HudKit.Lime : HudKit.Dim;
    }

    // ---------------------------------------------------------------- camera

    private const float Pitch = 52f;

    /// <summary>Frames the squad: the camera pulled in or back until every model fits the view with a small margin
    /// (measured by projecting each model's box, as the portrait studio does).</summary>
    private void Aim()
    {
        if (_cam == null || _spots.Count == 0 || Size.X < 10 || Size.Y < 10) return;
        float tall = Mathf.Max(1.2f, _spots.Max(s => s.Box.End.Y));
        _focusY = tall * 0.3f;
        _fit = 30;
        float vTan = Mathf.Tan(Mathf.DegToRad(_cam.Fov) / 2), hTan = vTan * Size.X / Size.Y;
        for (int pass = 0; pass < 4; pass++)
        {
            _zoom = 1;
            Place();
            float extent = 0;
            Transform3D toCam = _cam.GlobalTransform.AffineInverse();
            foreach (Spot sp in _spots)
                for (int c = 0; c < 8; c++)
                {
                    Vector3 local = toCam * (sp.Root.GlobalTransform * sp.Box.GetEndpoint(c));
                    float depth = Mathf.Max(-local.Z, 1e-3f);
                    extent = Mathf.Max(extent, Mathf.Max(Mathf.Abs(local.X) / (depth * hTan), Mathf.Abs(local.Y) / (depth * vTan)));
                }
            if (extent <= 0) break;
            _fit *= Mathf.Lerp(1f, extent * 1.12f, 0.9f);
        }
        Place();
    }

    private void Place()
    {
        float pitch = Mathf.DegToRad(Pitch);
        float dist = _fit * _zoom;
        Vector3 focus = new(0, _focusY, 0);
        _cam.Position = focus + new Vector3(0, Mathf.Sin(pitch), Mathf.Cos(pitch)) * dist;
        _cam.LookAt(focus, Vector3.Up);
        _turn.Rotation = new Vector3(0, _yaw, 0);
    }

    // ---------------------------------------------------------------- input

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }: _zoom = Mathf.Max(0.35f, _zoom * 0.9f); Place(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }: _zoom = Mathf.Min(3f, _zoom * 1.1f); Place(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } t: _turning = t.Pressed; break;
            case InputEventMouseMotion mm when _turning: _yaw += mm.Relative.X * 0.01f; Place(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                if (down.DoubleClick) { _skipRelease = true; _pressing = false; ClickAt(down.Position, add: false, doubleClick: true); break; }
                _pressing = true; _boxing = false; _pressAt = down.Position;
                break;
            case InputEventMouseMotion move when _pressing:
                if (!_boxing && move.Position.DistanceTo(_pressAt) > 6) _boxing = true;
                if (_boxing) _overlay.Set(new Rect2(_pressAt, move.Position - _pressAt).Abs());
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up:
                bool add = up.ShiftPressed || up.CtrlPressed;
                if (_skipRelease) { _skipRelease = false; break; }
                if (!_pressing) break;
                _pressing = false;
                if (_boxing)
                {
                    _boxing = false;
                    var rect = new Rect2(_pressAt, up.Position - _pressAt).Abs();
                    _overlay.Set(null);
                    _panel.BoxModels(Inside(rect), add);
                }
                else ClickAt(up.Position, add, doubleClick: false);
                break;
            default: return;
        }
        AcceptEvent();
    }

    /// <summary>The model under a point of the view; -1 for empty table. Each model is an upright cylinder as wide as
    /// its base (or its model, if wider) and as tall as its model; the first one the pointer's ray meets wins, as a
    /// person expects - the model in front, not the one behind it.</summary>
    internal int Hit(Vector2 at)
    {
        if (_cam == null) return -1;
        Vector3 from = _cam.ProjectRayOrigin(at), dir = _cam.ProjectRayNormal(at);
        int best = -1;
        float bestT = float.MaxValue;
        for (int i = 0; i < _spots.Count; i++)
        {
            Spot sp = _spots[i];
            Transform3D inv = sp.Root.GlobalTransform.AffineInverse();
            Vector3 o = inv * from, d = inv.Basis * dir;
            float rad = Mathf.Max(_baseInches / 2, Mathf.Max(sp.Box.Size.X, sp.Box.Size.Z) / 2 * 0.8f) + 0.05f;
            float top = Mathf.Max(0.4f, sp.Box.End.Y);
            if (Cylinder(o, d, rad, top) is float t && t < bestT) { bestT = t; best = i; }
        }
        return best;
    }

    /// <summary>Where a ray first meets an upright cylinder round the origin (radius <paramref name="r"/>, from the
    /// table up to <paramref name="top"/>), or null.</summary>
    private static float? Cylinder(Vector3 o, Vector3 d, float r, float top)
    {
        float? best = null;
        void Try(float t) { if (t >= 0 && (best == null || t < best)) best = t; }
        float a = d.X * d.X + d.Z * d.Z, b = 2 * (o.X * d.X + o.Z * d.Z), c = o.X * o.X + o.Z * o.Z - r * r;
        if (a > 1e-8f)
        {
            float disc = b * b - 4 * a * c;
            if (disc >= 0)
            {
                float sq = Mathf.Sqrt(disc);
                foreach (float t in new[] { (-b - sq) / (2 * a), (-b + sq) / (2 * a) })
                {
                    float y = o.Y + d.Y * t;
                    if (y >= 0 && y <= top) Try(t);
                }
            }
        }
        if (Mathf.Abs(d.Y) > 1e-8f)
            foreach (float plane in new[] { 0f, top })
            {
                float t = (plane - o.Y) / d.Y;
                Vector3 p = o + d * t;
                if (p.X * p.X + p.Z * p.Z <= r * r) Try(t);
            }
        return best;
    }

    internal void ClickAt(Vector2 at, bool add, bool doubleClick)
    {
        int i = Hit(at);
        if (doubleClick) _panel.DoubleClickModel(i);
        else _panel.ClickModel(i, add);
    }

    /// <summary>The models whose base centre is inside a box drawn on the view.</summary>
    private List<int> Inside(Rect2 rect)
    {
        var inside = new List<int>();
        for (int i = 0; i < _spots.Count; i++)
        {
            Vector3 p = _spots[i].Root.GlobalPosition + new Vector3(0, 0.2f, 0);
            if (_cam.IsPositionBehind(p)) continue;
            Vector2 s = _cam.UnprojectPosition(p);
            if (rect.HasPoint(s) || (ScreenRect(i) is { } r && rect.Encloses(r))) inside.Add(i);
        }
        return inside;
    }

    /// <summary>Where model <paramref name="i"/> covers the view (its box's corners projected), or null when behind the camera.</summary>
    private Rect2? ScreenRect(int i)
    {
        Spot sp = _spots[i];
        Rect2? r = null;
        Transform3D t = sp.Root.GlobalTransform;
        for (int c = 0; c < 8; c++)
        {
            Vector3 p = t * sp.Box.GetEndpoint(c);
            if (_cam.IsPositionBehind(p)) return null;
            Vector2 s = _cam.UnprojectPosition(p);
            r = r is { } x ? x.Expand(s) : new Rect2(s, Vector2.Zero);
        }
        return r;
    }

    /// <summary>Scripted runs: the middle of model <paramref name="i"/> on the view.</summary>
    internal Vector2 ScreenSpot(int i) => i >= 0 && i < _spots.Count && _cam != null
        ? _cam.UnprojectPosition(_spots[i].Root.GlobalTransform * new Vector3(0, Mathf.Max(0.4f, _spots[i].Box.End.Y) * 0.75f, 0))
        : new Vector2(-100, -100);

    /// <summary>Scripted runs: a box round these models, dragged as the mouse does.</summary>
    internal void BoxAround(List<int> models, bool add)
    {
        Rect2? r = null;
        foreach (int i in models)
            if (i >= 0 && i < _spots.Count && _cam != null)
            {
                Vector2 s = _cam.UnprojectPosition(_spots[i].Root.GlobalPosition + new Vector3(0, 0.2f, 0));
                r = r is { } x ? x.Expand(s) : new Rect2(s, Vector2.Zero);
            }
        _panel.BoxModels(r is { } box ? Inside(box.Grow(3)) : new List<int>(), add);
    }

    /// <summary>The rubber-band box drawn over the view while dragging.</summary>
    private sealed partial class BoxOverlay : Control
    {
        private Rect2? _r;

        public void Set(Rect2? r) { _r = r; QueueRedraw(); }

        public override void _Draw()
        {
            if (_r is not { } r) return;
            DrawRect(r, HudKit.Lime with { A = 0.12f });
            DrawRect(r, HudKit.Lime, false, 2);
        }
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

    /// <summary>The model on show as loaded - unpainted - for reading its own colours (the painter). Null before anything is shown.</summary>
    public Node3D? Shown { get; private set; }

    /// <summary>Shows a model file, painted with <paramref name="paint"/>'s colour swaps; returns a short line about it,
    /// or the reason it can't.</summary>
    public string? Show(string file, string? paint = null)
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
        // As on the table: the model's own sculpted base hidden, standing on the game's base (Kai, 2026-11-19).
        Node3D source = OwnBase.Strip(a.Model, s).Model;
        var body = (Node3D)source.Duplicate();
        if (source != a.Model) source.Free();
        pivot.AddChild(body);
        Shown = a.Model; // the unpainted original: the painter reads the model's own colours from it
        ModelPainter.Apply(body, paint);
        var (_, box) = ModelPlacement.Place(pivot, body, s);
        _focusY = Mathf.Max(0.5f, box.Size.Y * 0.45f);
        _distance = Mathf.Max(4.5f, Mathf.Max(box.Size.Y, Mathf.Max(box.Size.X, box.Size.Z)) * 2.4f + 2f);
        Aim();
        return $"{(s.BaseMm > 0 ? $"{s.BaseMm:0.#} mm base, " : "")}stands {box.Size.Y * UnitModelSettings.MmPerInch:0} mm tall";
    }

    public override void _Process(double delta) => Spin(delta);

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
