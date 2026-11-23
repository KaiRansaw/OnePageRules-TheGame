using FdgRaylib.Models3D;
using FDG;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Forge;
using OprGodot.Hud;
using OprGodot.Lab;

namespace OprGodot.Maps;

/// <summary>
/// Match terrain (Kai, 2026-11-14: "match the map assets to what kind of terrain they are"). Every downloaded Warhammer
/// terrain piece as a card - its picture, its name, the kind it is, hide - with a guess filled in from its Warhammer
/// keywords where it has any. The kind decides the piece's rules on a map; its size comes from the model, measured
/// here the first time (with the picture). Saves as it goes.
/// <para>RENTAL: plain cards in a grid. The decisions (what a piece is, what it's called, whether it's used) are the
/// permanent part and live in <see cref="WarhammerTerrainLibrary"/>.</para>
/// </summary>
public partial class MatchTerrainScreen : Control
{
    private WarhammerTerrainLibrary? _lib;
    private OptionButton _set = null!, _show = null!;
    private GridContainer _grid = null!;
    private Label _status = null!;
    private readonly Dictionary<int, Card> _cards = new();
    private bool _dirty;
    private double _sinceChange;
    private PortraitShot.Studio? _studio;
    private bool _picturesRunning;
    private int _picturesDone, _picturesTotal;

    private sealed record Card(VBoxContainer Root, Control Frame, TextureRect Picture, Label Size, OptionButton Kind, CheckBox Hide, CheckBox Pick);
    private TerrainPreview _preview = null!;

    // Kai, 2026-11-18: "select multiple terrain pieces, and mark them all as a kind of terrain" - then "instead of
    // clicking on each model, a tickable box". Tick a card's box to pick it, Shift+tick picks everything between, and
    // the bar above sets the kind for every picked piece at once. One ticked piece shows in the preview.
    private readonly HashSet<int> _picked = new();
    private int? _lastClicked;
    private HBoxContainer _bulk = null!;
    private Label _bulkCount = null!;
    private OptionButton _bulkKind = null!;

    /// <summary>Looking down a little more than a unit portrait: terrain is read from above.</summary>
    private static readonly PortraitShot.Angle Angle = new("T", "Terrain", 30, 32, 1f, 1f);

    private string ThumbFolder => Path.Combine(_lib!.Folder, "_thumbs");
    private string ThumbFor(int id) => Path.Combine(ThumbFolder, id + ".png");

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _lib = WarhammerTerrainLibrary.Load();
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        head.AddChild(Ui.Button("Back", Leave, "Back to the Map Maker (saves first).", "BackButton").Also(b => { b.Name = "Back"; b.CustomMinimumSize = new Vector2(110, 44); }));
        var title = new VBoxContainer();
        title.AddThemeConstantOverride("separation", -6);
        title.AddChild(HudKit.Text("MATCH TERRAIN", 32, HudKit.Fg, HudKit.Exo(800)));
        title.AddChild(HudKit.Text("What each Warhammer piece is on a map", 14, HudKit.Dim, HudKit.Semi));
        head.AddChild(title);
        head.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _set = new OptionButton { Name = "Set", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 0), TooltipText = "Which of the mod's terrain sets to show." };
        _set.AddItem("All sets");
        var sets = _lib?.Sets.OrderBy(k => k.Key).ToList() ?? new();
        foreach (var kv in sets) _set.AddItem(kv.Value);
        _set.ItemSelected += _ => Fill();
        head.AddChild(_set);
        _show = new OptionButton { Name = "Show", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(200, 0), TooltipText = "Which pieces to show." };
        foreach (string s in new[] { "All pieces", "No kind yet", "Not checked yet", "Hidden" }) _show.AddItem(s);
        _show.ItemSelected += _ => Fill();
        head.AddChild(_show);
        var sheetToggle = new Button { Name = "CheatSheet", Text = "Cheat sheet", ToggleMode = true, ButtonPressed = _sheetOpen, FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(150, 0), TooltipText = "Show or hide what each kind of terrain does (the Advanced Rulebook's words)." };
        head.AddChild(sheetToggle);
        col.AddChild(head);

        var help = ForgeWidgets.Wrapped("Tick pieces to pick them (Shift+tick picks a run), then set their kind all at once from the bar; one ticked piece shows in the preview. " +
            "Pick what each piece is - its kind gives it that kind's rules on a map (Ruins: cover + unstable, Building: impassable + blocks sight...). " +
            "Decoration has no rules. Hide pieces you don't want in the Map Maker. A kind already filled in is a guess from the piece's Warhammer keywords - check it. " +
            "Pictures and sizes are made the first time (it takes a few minutes); everything saves as you go.", 14, HudKit.Dim);
        col.AddChild(help);
        _status = HudKit.Text("", 14, HudKit.Lime, HudKit.Semi);
        col.AddChild(_status);
        col.AddChild(BulkBar());

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        _grid = new GridContainer { Columns = 7, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(_grid);
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        body.AddChild(scroll);
        var sideCol = new VBoxContainer { CustomMinimumSize = new Vector2(410, 0), Name = "Side" };
        sideCol.AddThemeConstantOverride("separation", 10);
        _preview = new TerrainPreview { Name = "Preview" };
        _preview.SinkChanged += Changed;
        sideCol.AddChild(_preview);
        Control sheet = CheatSheet();
        sheet.Visible = _sheetOpen;
        sideCol.AddChild(sheet);
        body.AddChild(sideCol);
        sheetToggle.Toggled += on => _sheetOpen = sheet.Visible = on;
        _grid.Columns = 5;
        col.AddChild(body);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        margin.AddChild(HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(col, 18, 14, 18, 14)));
        AddChild(margin);

        if (_lib == null || _lib.Entries.Count == 0)
        {
            _status.Text = "No Warhammer terrain on this computer yet: Asset Lab -> Download models -> Get Warhammer terrain.";
            _status.AddThemeColorOverride("font_color", HudKit.Gold);
            return;
        }
        Fill();
        _studio = new PortraitShot.Studio(512);
        AddChild(_studio);
        MakePictures();
    }

    private static bool _sheetOpen = true;

    private Control BulkBar()
    {
        _bulk = new HBoxContainer { Name = "BulkBar" };
        _bulk.AddThemeConstantOverride("separation", 10);
        _bulkCount = HudKit.Text("", 15, HudKit.Lime, HudKit.Exo(700));
        _bulkCount.CustomMinimumSize = new Vector2(150, 0);
        _bulk.AddChild(_bulkCount);
        _bulk.AddChild(HudKit.Text("Set kind:", 14, HudKit.Dim, HudKit.Semi));
        _bulkKind = new OptionButton { Name = "BulkKind", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 0),
            TooltipText = "Give every picked piece this kind." };
        _bulkKind.AddItem("Pick a kind...");
        foreach (var k in WarhammerTerrainLibrary.Kinds) _bulkKind.AddItem(k.Name);
        _bulkKind.ItemSelected += i =>
        {
            if (i <= 0) return;
            SetKindOfPicked(WarhammerTerrainLibrary.Kinds[(int)i - 1].Id);
            _bulkKind.Selected = 0;
        };
        _bulk.AddChild(_bulkKind);
        _bulk.AddChild(Ui.Button("Hide them", () => SetHiddenOfPicked(true), "Leave every picked piece out of the Map Maker.").Also(b => b.Name = "BulkHide"));
        _bulk.AddChild(Ui.Button("Unhide them", () => SetHiddenOfPicked(false), "Put every picked piece back in the Map Maker.").Also(b => b.Name = "BulkUnhide"));
        _bulk.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _bulk.AddChild(Ui.Button("Pick all shown", PickAllShown, "Pick every piece the filters are showing.").Also(b => b.Name = "PickAll"));
        _bulk.AddChild(Ui.Button("Clear picks", ClearPicks, "Drop every pick.").Also(b => b.Name = "ClearPicks"));
        RefreshBulk();
        return _bulk;
    }

    private void RefreshBulk()
    {
        int n = _picked.Count;
        _bulkCount.Text = n == 0 ? "None picked" : $"{n} picked";
        foreach (Node c in _bulk.GetChildren())
            if (c is BaseButton b && b.Name != "PickAll") b.Disabled = n == 0;
        foreach (var (id, card) in _cards)
        {
            card.Frame.SelfModulate = _picked.Contains(id) ? HudKit.Lime : Colors.White;
            card.Pick.SetPressedNoSignal(_picked.Contains(id));
        }
        _preview?.ShowPiece(n == 1 ? _lib?.ById(_picked.First()) : null, n);
    }

    private void OnTicked(int id, bool on)
    {
        bool shift = Input.IsKeyPressed(Key.Shift);
        if (shift && on && _lastClicked is { } from && _cards.ContainsKey(from))
        {
            var order = _cards.Keys.ToList(); // card order = grid order
            int a = order.IndexOf(from), b = order.IndexOf(id);
            for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) _picked.Add(order[i]);
        }
        else if (on) _picked.Add(id);
        else _picked.Remove(id);
        _lastClicked = id;
        RefreshBulk();
    }

    private void PickAllShown() { foreach (int id in _cards.Keys) _picked.Add(id); RefreshBulk(); }

    private void ClearPicks() { _picked.Clear(); _lastClicked = null; RefreshBulk(); }

    private IEnumerable<WarhammerTerrainLibrary.Entry> PickedEntries() =>
        _lib?.Entries.Where(e => _picked.Contains(e.Id)) ?? Enumerable.Empty<WarhammerTerrainLibrary.Entry>();

    private void SetKindOfPicked(string kind)
    {
        int ki = WarhammerTerrainLibrary.Kinds.ToList().FindIndex(k => k.Id == kind);
        foreach (var e in PickedEntries())
        {
            e.Match.Kind = kind;
            if (_cards.TryGetValue(e.Id, out Card? card)) { card.Kind.Selected = ki + 1; Colour(card.Kind, kind); }
            Changed(e);
        }
    }

    private void SetHiddenOfPicked(bool hidden)
    {
        foreach (var e in PickedEntries())
        {
            e.Match.Hidden = hidden;
            if (_cards.TryGetValue(e.Id, out Card? card)) card.Hide.SetPressedNoSignal(hidden);
            Changed(e);
        }
    }

    /// <summary>
    /// Kai, 2026-11-14: a cheat sheet of what each kind does, beside the cards. Each kind with the rules the Advanced
    /// Rulebook gives it, then each rule in the book's own words (<see cref="TerrainBook.RuleText"/>).
    /// </summary>
    private static Control CheatSheet()
    {
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        list.AddChild(HudKit.Heading("Kinds", 14));
        foreach (TerrainBook.PieceKind k in TerrainBook.Kinds)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(HudKit.Text(k.Name, 13, BattleTable.TerrainColour(k.Rules).Lightened(0.45f), HudKit.Semi).Also(l => l.CustomMinimumSize = new Vector2(120, 0)));
            row.AddChild(ForgeWidgets.Wrapped(TerrainBook.Describe(k.Rules), 13, HudKit.Fg).Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
            list.AddChild(row);
        }
        var deco = new HBoxContainer();
        deco.AddThemeConstantOverride("separation", 8);
        deco.AddChild(HudKit.Text("Decoration", 13, HudKit.Dim, HudKit.Semi).Also(l => l.CustomMinimumSize = new Vector2(120, 0)));
        deco.AddChild(ForgeWidgets.Wrapped("No rules - only there to look at", 13, HudKit.Dim).Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        list.AddChild(deco);

        list.AddChild(HudKit.Gap(0, 6));
        list.AddChild(HudKit.Heading("What the rules do", 14));
        foreach (ETerrainType f in TerrainBook.RuleFlags)
        {
            string name = TerrainBook.RuleName(f) + (TerrainBook.NeedsHeights(f) ? "  (needs heights - not in the game yet)"
                : f == ETerrainType.Climbable ? "  (over 3\" tall is Impassable; up to 3\" is climbed and stood on)" : "");
            list.AddChild(HudKit.Text(name, 13, TableLook.RulesColour(f).Lightened(0.3f), HudKit.Exo(700)));
            list.AddChild(ForgeWidgets.Wrapped(TerrainBook.RuleText(f), 12, HudKit.Dim));
            list.AddChild(HudKit.Gap(0, 2));
        }
        list.AddChild(ForgeWidgets.Wrapped("Advanced Rulebook v3.6.0, Terrain.", 11, HudKit.Dim));

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(400, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill, Name = "CheatSheetPanel" };
        scroll.AddChild(HudKit.Pad(list, 4, 0, 10, 0).Also(c => c.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return scroll;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsVisibleInTree()) return;   // keys are for the screen that is showing
        if (e.IsActionPressed("ui_cancel")) { Leave(); GetViewport().SetInputAsHandled(); }
    }

    private void Leave()
    {
        SaveNow();
        App.I.Back();
    }

    private IEnumerable<WarhammerTerrainLibrary.Entry> Shown()
    {
        if (_lib == null) return Enumerable.Empty<WarhammerTerrainLibrary.Entry>();
        var sets = _lib.Sets.OrderBy(k => k.Key).ToList();
        int set = _set.Selected <= 0 ? -1 : sets[_set.Selected - 1].Key;
        return _lib.Entries.Where(e => set < 0 || e.Set == set).Where(e => _show.Selected switch
        {
            1 => !e.Match.Hidden && e.Match.Kind.Length == 0,
            2 => !e.Match.Hidden && !e.Match.Checked,
            3 => e.Match.Hidden,
            _ => true,
        }).OrderBy(e => e.Set).ThenBy(e => NaturalKey(e.Name));
    }

    // "Piece 2" before "Piece 10".
    private static string NaturalKey(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\d+", m => m.Value.PadLeft(6, '0'));

    private void Fill()
    {
        foreach (Node n in _grid.GetChildren()) { _grid.RemoveChild(n); n.QueueFree(); }
        _cards.Clear();
        var shown = Shown().ToList();
        foreach (var e in shown) _grid.AddChild(MakeCard(e));
        _picked.IntersectWith(_cards.Keys); // picks the filters now hide are dropped, so "Set kind" never touches unseen pieces
        RefreshBulk();
        UpdateStatus();
    }

    private Control MakeCard(WarhammerTerrainLibrary.Entry e)
    {
        var card = new VBoxContainer { Name = "Piece_" + e.Id, CustomMinimumSize = new Vector2(200, 0) };
        card.AddThemeConstantOverride("separation", 4);
        var pic = new TextureRect { CustomMinimumSize = new Vector2(200, 170), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = $"{e.OriginalName}\n{e.SetName}" + (e.Keywords.Length > 0 ? $"\nWarhammer: {e.Keywords}" : "") };
        if (LoadThumb(e.Id) is { } tex) pic.Texture = tex;
        var frame = HudKit.Panel(HudKit.PanelBox(8), pic);
        var pick = new CheckBox { Name = "Pick", Text = "Pick", FocusMode = FocusModeEnum.None, ButtonPressed = _picked.Contains(e.Id),
            TooltipText = "Tick to mark it along with the others (Shift+tick picks every piece between). One ticked piece shows in the preview." };
        pick.Toggled += on => OnTicked(e.Id, on);
        card.AddChild(pick);
        card.AddChild(frame);

        var name = new LineEdit { Name = "Name", Text = e.Name, MaxLength = 40, TooltipText = "What it is called on the map (plain letters)." };
        name.TextChanged += t => { e.Match.Name = Ascii(t.Trim()) == e.OriginalName ? "" : Ascii(t.Trim()); Changed(e); };
        card.AddChild(name);

        var kind = new OptionButton { Name = "Kind", FocusMode = FocusModeEnum.None, TooltipText = "Which terrain kind it is - this gives it its rules on a map." };
        kind.AddItem("? (pick a kind)");
        foreach (var k in WarhammerTerrainLibrary.Kinds) kind.AddItem(k.Name);
        int ki = WarhammerTerrainLibrary.Kinds.ToList().FindIndex(k => k.Id == e.Match.Kind);
        kind.Selected = ki + 1;
        Colour(kind, e.Match.Kind);
        kind.ItemSelected += i =>
        {
            e.Match.Kind = i <= 0 ? "" : WarhammerTerrainLibrary.Kinds[(int)i - 1].Id;
            Colour(kind, e.Match.Kind);
            Changed(e);
        };
        card.AddChild(kind);

        var row = new HBoxContainer();
        var hide = new CheckBox { Name = "Hide", Text = "Hide", ButtonPressed = e.Match.Hidden, FocusMode = FocusModeEnum.None, TooltipText = "Leave it out of the Map Maker (a duplicate, a broken model, something you don't want)." };
        hide.Toggled += on => { e.Match.Hidden = on; Changed(e); };
        row.AddChild(hide);
        var size = HudKit.Text(SizeText(e), 12, HudKit.Dim, HudKit.Semi);
        size.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        size.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(size);
        card.AddChild(row);

        // Kai, 2026-11-14: some pieces load upside down or lying over - pick which way is up; the picture, size and every
        // map redraw with it.
        var up = new OptionButton { Name = "WayUp", FocusMode = FocusModeEnum.None, TooltipText = "If the picture shows it upside down or lying over, pick the way that stands it up. The picture and size are made again." };
        foreach (var (_, label) in WayUp) up.AddItem(label);
        up.Selected = Math.Max(0, Array.FindIndex(WayUp, w => w.Axis == WarhammerModels.UpOf(e.File)));
        up.ItemSelected += i =>
        {
            WarhammerModels.SetUp(e.File, WayUp[(int)i].Axis);
            e.Match.W = e.Match.D = e.Match.H = 0; // measured again, the right way up
            try { File.Delete(ThumbFor(e.Id)); } catch (IOException) { }
            pic.Texture = null;
            size.Text = SizeText(e);
            Changed(e);
            MakePictures();
        };
        card.AddChild(up);
        // Kai, 2026-11-18: the sculpted scenic base some models stand on (the woods' dark disc) - cut off by default
        // when the game finds one (TerrainBaseTrim); this overrides it for the pieces it gets wrong.
        var baseCut = new OptionButton { Name = "Base", FocusMode = FocusModeEnum.None, TooltipText = "The round base some models were sculpted on. Auto: cut off when the game finds one." };
        foreach (string label in new[] { "Base: auto", "Base: cut off", "Base: keep" }) baseCut.AddItem(label);
        baseCut.Selected = e.Match.CutBase switch { true => 1, false => 2, _ => 0 };
        baseCut.ItemSelected += i =>
        {
            e.Match.CutBase = i switch { 1 => true, 2 => false, _ => null };
            WarhammerModels.Forget(e.File);
            try { File.Delete(ThumbFor(e.Id)); } catch (IOException) { }
            pic.Texture = null;
            Changed(e);
            SaveNow();
            MakePictures();
            RefreshBulk();
        };
        card.AddChild(baseCut);
        _cards[e.Id] = new Card(card, frame, pic, size, kind, hide, pick);
        if (_picked.Contains(e.Id)) frame.SelfModulate = HudKit.Lime;
        return card;
    }

    private static readonly (EUpAxis Axis, string Label)[] WayUp =
    {
        (EUpAxis.YUp, "Way up: as loaded"),
        (EUpAxis.YDown, "Flip upside down"),
        (EUpAxis.ZUp, "Tip back"),
        (EUpAxis.ZDown, "Tip forward"),
        (EUpAxis.XUp, "Roll left"),
        (EUpAxis.XDown, "Roll right"),
    };

    private static string Ascii(string s) => new(s.Where(c => c >= ' ' && c <= '~').ToArray());

    private static string SizeText(WarhammerTerrainLibrary.Entry e) =>
        e.Measured ? $"{e.Match.W:0.#} x {e.Match.D:0.#}\" , {e.Match.H:0.#}\" tall" : "measuring...";

    private static void Colour(OptionButton b, string kind) =>
        b.AddThemeColorOverride("font_color", kind.Length == 0 ? HudKit.Gold : BattleTable.TerrainColour(WarhammerTerrainLibrary.RulesFor(kind)).Lightened(0.5f));

    private void Changed(WarhammerTerrainLibrary.Entry e)
    {
        e.Match.Checked = true;
        _dirty = true;
        _sinceChange = 0;
        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        if (!_dirty) return;
        _sinceChange += delta;
        if (_sinceChange > 1.5) SaveNow();
    }

    private void SaveNow()
    {
        if (_lib == null || !_dirty) return;
        try { _lib.Save(); _dirty = false; WarhammerModels.Reload(); }
        catch (Exception ex) { _status.Text = "Couldn't save: " + ex.Message; }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_lib == null) return;
        int all = _lib.Entries.Count;
        int kinded = _lib.Entries.Count(e => e.Match.Kind.Length > 0 && !e.Match.Hidden);
        int hidden = _lib.Entries.Count(e => e.Match.Hidden);
        int checkedCount = _lib.Entries.Count(e => e.Match.Checked);
        string pics = _picturesRunning ? $"  -  making pictures {_picturesDone}/{_picturesTotal}" : "";
        _status.Text = $"{all} pieces: {kinded} with a kind, {hidden} hidden, {checkedCount} checked by you  -  showing {_cards.Count}" + pics + (_dirty ? "  -  saving..." : "");
    }

    private Texture2D? LoadThumb(int id)
    {
        string f = ThumbFor(id);
        if (!File.Exists(f)) return null;
        var img = Image.LoadFromFile(f);
        return img == null || img.IsEmpty() ? null : ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Every piece without a picture or a size gets both, one at a time in the background, the pieces on screen first.
    /// The size is the model's own box in table inches - the footprint it gets on a map.
    /// </summary>
    private bool _picturesAgain;

    private async void MakePictures()
    {
        if (_lib == null || _studio == null) return;
        if (_picturesRunning) { _picturesAgain = true; return; } // the running pass picks it up when it ends
        Directory.CreateDirectory(ThumbFolder);
        var todo = Shown().Concat(_lib.Entries).Distinct().Where(e => !e.Measured || !File.Exists(ThumbFor(e.Id))).ToList();
        if (todo.Count == 0) return;
        _picturesRunning = true;
        _picturesTotal = todo.Count;
        _picturesDone = 0;
        for (int frame = 0; frame < 2; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        foreach (var e in todo)
        {
            if (!IsInsideTree()) return;
            if (_studio.Show(e.File, out _, trimTerrainBase: e.Match.CutBase != false, forceCut: e.Match.CutBase))
            {
                if (!e.Measured)
                {
                    Aabb? box = WarhammerModels.Measure(_studio, e.File);
                    if (box is { } b)
                    {
                        e.Match.W = MathF.Round(b.Size.X * 100) / 100;
                        e.Match.D = MathF.Round(b.Size.Z * 100) / 100;
                        e.Match.H = MathF.Round(b.Size.Y * 100) / 100;
                        _dirty = true;
                    }
                }
                _studio.Aim(Angle);
                for (int frame = 0; frame < 3; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!IsInsideTree()) return;
                _studio.Grab(256).SavePng(ThumbFor(e.Id));
                if (_cards.TryGetValue(e.Id, out Card? card))
                {
                    card.Picture.Texture = LoadThumb(e.Id);
                    card.Size.Text = SizeText(e);
                }
            }
            _picturesDone++;
            if (_picturesDone % 10 == 0) SaveNow();
            UpdateStatus();
        }
        _picturesRunning = false;
        SaveNow();
        UpdateStatus();
        if (_picturesAgain) { _picturesAgain = false; MakePictures(); }
    }

    public override void _ExitTree() => SaveNow();
}
