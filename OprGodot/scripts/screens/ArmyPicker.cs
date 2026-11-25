using FDG;
using FdgRaylib.Pictures;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// The army picture grid (Kai, 2026-11-16, "Menu Art &amp; Themes Mockup", round 1 - all Keep): every army of a game as
/// a wide tile, name on the left and its picture on the right, three across, in Army Forge's own groups and order
/// (<see cref="ArmyGroups"/>) with a search box. One grid for every place an army is picked: the Army Forge, the Lobby
/// (with how many saved lists each army has - none = greyed, still clickable), Hero Builder step 1 and New Campaign.
/// It covers the screen it is opened over; Back / Esc closes it with nothing changed.
/// </summary>
public partial class ArmyPicker : PanelContainer
{
    public EGameSystem System { get; init; }
    public string Title { get; init; } = "Choose an Army";
    public string Crumb { get; init; } = "";
    public string CommitText { get; init; } = "CHOOSE THIS ARMY";
    /// <summary>The armies to offer (book names). Null = every bundled book of <see cref="System"/>.</summary>
    public IReadOnlyList<string>? Armies { get; init; }
    /// <summary>Lobby: saved lists per army; armies with none are greyed. Null = no badges.</summary>
    public IReadOnlyDictionary<string, int>? Counts { get; init; }
    /// <summary>Extra words for the info line about the picked army ("3 saved lists").</summary>
    public Func<string, string>? Describe { get; init; }
    public string? Selected { get; set; }
    public Action<string>? Picked { get; init; }
    public Action? Closed { get; init; }
    /// <summary>
    /// Extra buttons on the bottom bar, left of the main one (the Forge's "Edit a list" / "Import a list", Kai
    /// 2026-11-17). Each gets this picker so it can close it once its job is done.
    /// </summary>
    public IReadOnlyList<(string Name, string Text, string Tip, Action<ArmyPicker> Act)>? Extras { get; init; }
    /// <summary>Army Forge: the games to switch between, as tabs in the top bar (null = no tabs).</summary>
    public IReadOnlyList<EGameSystem>? Games { get; init; }
    /// <summary>A game tab was clicked: the owner reopens the grid for that game.</summary>
    public Action<EGameSystem>? GameChanged { get; init; }

    private GridContainer? _first;
    // One column fewer than before (4 -> 3 at the normal card size) - the info panel takes the right side (2026-11-17).
    private static int Columns => Math.Clamp(Mathf.RoundToInt(4f / HudKit.PictureSize.Scale) - 1, 2, 6);
    private readonly List<(string Name, Button Tile, Control Group, Control Header)> _tiles = new();
    private Label _info = null!;
    private Button _commit = null!;

    /// <summary>Opens the grid over <paramref name="parent"/> (full screen).</summary>
    public static ArmyPicker Show(Control parent, ArmyPicker picker)
    {
        picker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        parent.AddChild(picker);
        return picker;
    }

    private const string LayoutScene = "res://scenes/screens/ArmyPickerLayout.tscn";
    private const string TileScene = "res://scenes/screens/ArmyTile.tscn";
    private static PackedScene? _tileScene;

    public override void _Ready()
    {
        Name = "ArmyPicker";
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink });
        // The layout lives in scenes/screens/ArmyPickerLayout.tscn (editable in the Godot editor; Kai, 2026-11-17); this
        // fills it in. Panels and buttons come from the UI theme, so the grid matches every other screen.
        var layout = GD.Load<PackedScene>(LayoutScene).Instantiate<Control>();
        AddChild(layout);

        var title = layout.GetNode<Label>("%Title");
        title.Text = Title;
        Ui.ScreenTitle(title);
        title.AddThemeFontSizeOverride("font_size", 40);
        var crumb = layout.GetNode<Label>("%Crumb");
        crumb.Text = Crumb.Length > 0 ? Crumb : GameSystemInfo.DisplayName(System);
        crumb.AddThemeFontOverride("font", HudKit.Semi);
        crumb.AddThemeFontSizeOverride("font_size", 18);
        crumb.AddThemeColorOverride("font_color", HudKit.Lime);

        // The game tabs (Army Forge only) get a row of their own under the title, so the top bar never runs off the screen.
        var tabs = layout.GetNode<HBoxContainer>("%Tabs");
        tabs.Visible = Games != null;
        if (Games != null)
            foreach (EGameSystem g in Games)
            {
                EGameSystem game = g;
                var tab = Ui.Button(GameSystemInfo.DisplayName(g), () => { if (game != System) { GameChanged?.Invoke(game); QueueFree(); } },
                    $"Show {GameSystemInfo.DisplayName(g)}'s army books.", g == System ? "CommitButton" : null);
                tab.Name = "GameTab_" + g;
                tab.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                tabs.AddChild(tab);
            }
        layout.GetNode<LineEdit>("%ArmySearch").TextChanged += Filter;

        // The grid, folded into Army Forge's groups.
        var body = layout.GetNode<VBoxContainer>("%Body");
        var names = Armies ?? BookIndex.Books(System).Select(b => b.Name).ToList();
        foreach (ArmyGroups.Group g in ArmyGroups.Arrange(System, names))
        {
            var head = HudKit.Heading(g.Name, 16);
            body.AddChild(head);
            // 4 across at the normal card size (Kai, 2026-11-17); the Card size slider makes tiles bigger or smaller.
            var grid = new GridContainer { Columns = Columns, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 12);
            body.AddChild(grid);
            _first ??= grid;
            foreach (string army in g.Books)
            {
                Button tile = Tile(army);
                grid.AddChild(tile);
                _tiles.Add((army, tile, grid, head));
            }
            body.AddChild(HudKit.Gap(0, 6));
        }

        // Bottom: Back, what is picked, the main button.
        var back = layout.GetNode<Button>("%ArmyPickerBack");
        back.TooltipText = "Close without picking (Esc).";
        back.Pressed += Close;
        _info = layout.GetNode<Label>("%Info");
        _info.AddThemeFontOverride("font", HudKit.Semi);
        _info.AddThemeFontSizeOverride("font_size", 20);
        _commit = layout.GetNode<Button>("%ArmyPickerCommit");
        foreach (var (name, text, tip, act) in Extras ?? Array.Empty<(string, string, string, Action<ArmyPicker>)>())
        {
            var x = new Button { Name = name, Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(Math.Max(170, text.Length * 12 + 40), 0) };
            x.Pressed += () => act(this);
            _commit.GetParent().AddChild(x);
            _commit.GetParent().MoveChild(x, _commit.GetIndex());
        }
        _commit.Text = CommitText;
        _commit.TooltipText = "Use the picked army (or double-click a tile).";
        _commit.Pressed += Commit;

        _layout = layout;
        StyleInfo();
        Select(Selected);
        if (Selected != null && _tiles.FirstOrDefault(t => t.Name == Selected).Tile is { } sel)
            CallDeferred(nameof(ScrollTo), sel);
    }

    private void ScrollTo(Button tile)
    {
        if (!IsInstanceValid(tile)) return;
        for (Node? n = tile.GetParent(); n != null; n = n.GetParent())
            if (n is ScrollContainer sc) { sc.EnsureControlVisible(tile); return; }
    }

    /// <summary>One army: scenes/screens/ArmyTile.tscn filled in - its picture over the whole tile, a shade behind the name.</summary>
    private Button Tile(string army)
    {
        int? count = Counts == null ? null : Counts.TryGetValue(army, out int n) ? n : 0;
        _tileScene ??= GD.Load<PackedScene>(TileScene);
        var b = _tileScene.Instantiate<Button>();
        b.Name = "Army_" + Forge.ForgeWidgets.SafeName(army);
        b.CustomMinimumSize = new Vector2(0, 96 * HudKit.PictureSize.Scale);
        b.FocusMode = FocusModeEnum.All;
        var box = new ChamferBox { Fill = Palette.Panel, Border = HudKit.Edge, Cut = 10, BorderWidth = 1 };
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box.With(x => x.Border = HudKit.EdgeHot));
        b.AddThemeStyleboxOverride("pressed", box.With(x => x.Border = HudKit.Lime));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        // The picture fills the tile inside its frame (Kai, 2026-11-17: no empty space on the left).
        Control art = MenuArt.View(MenuPictures.Army(System, army), System);
        art.MouseFilter = MouseFilterEnum.Ignore;
        art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        b.GetNode<Control>("%Art").AddChild(art);
        var name = b.GetNode<Label>("%ArmyName");
        name.Text = army;
        name.AddThemeFontOverride("font", HudKit.Bold);
        name.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(21 * Math.Max(0.85f, HudKit.PictureSize.Scale)));
        name.AddThemeColorOverride("font_color", HudKit.Fg);
        name.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        name.AddThemeConstantOverride("shadow_offset_y", 2);
        name.MouseFilter = MouseFilterEnum.Ignore;
        if (count is { } c)
        {
            var badge = HudKit.Chip(c == 0 ? "no lists" : c == 1 ? "1 list" : $"{c} lists", c == 0 ? HudKit.Dim : HudKit.Lime, c > 0, 15);
            badge.MouseFilter = MouseFilterEnum.Ignore;
            badge.SetAnchorsPreset(LayoutPreset.BottomRight);
            badge.GrowHorizontal = GrowDirection.Begin; badge.GrowVertical = GrowDirection.Begin;
            b.GetNode<Control>("%Badge").AddChild(badge);
            if (c == 0) b.Modulate = new Color(0.55f, 0.55f, 0.55f);
        }
        // A controller has no double click: A on a faction opens its lists straight away (Kai, 2026-11-24), in either
        // controller mode. The mouse still picks with one click and opens with two.
        b.Pressed += () => { Select(army); if (Pads.PadDriver.Active && !_commit.Disabled) Commit(); };
        b.GuiInput += e => { if (e is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left }) { Select(army); Commit(); } };
        return b;
    }

    private void Select(string? army)
    {
        Selected = army;
        foreach (var t in _tiles)
        {
            var box = (ChamferBox)t.Tile.GetThemeStylebox("normal");
            bool on = t.Name == army;
            t.Tile.AddThemeStyleboxOverride("normal", box.With(x => { x.Border = on ? HudKit.Lime : HudKit.Edge; x.BorderWidth = on ? 3 : 1; }));
            // The picked army's name turns the go colour - the frame alone hides behind the picture.
            if (t.Tile.GetNodeOrNull<Label>("%ArmyName") is { } nm) nm.AddThemeColorOverride("font_color", on ? HudKit.Lime : HudKit.Fg);
        }
        if (_info == null) return;
        ShowInfo(army);
        _commit.Disabled = army == null;
        _info.Text = army == null ? "Pick an army" : army + (Describe?.Invoke(army) is { Length: > 0 } more ? "  -  " + more : "");
        _info.AddThemeColorOverride("font_color", army == null ? HudKit.Dim : HudKit.Fg);
    }

    // ================================================================== the info panel (option C, Kai 2026-11-17)

    private Control? _layout;
    private int _infoAsk;

    private void StyleInfo()
    {
        if (_layout == null) return;
        var name = _layout.GetNode<Label>("%InfoName");
        name.AddThemeFontOverride("font", HudKit.Exo(800));
        name.AddThemeFontSizeOverride("font_size", 34);
        name.AddThemeColorOverride("font_color", HudKit.Fg);
        name.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        name.AddThemeConstantOverride("shadow_offset_y", 2);
        var line = _layout.GetNode<Label>("%InfoLine");
        line.AddThemeFontOverride("font", HudKit.Semi);
        line.AddThemeFontSizeOverride("font_size", 17);
        line.AddThemeColorOverride("font_color", HudKit.Lime);
        line.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        line.AddThemeConstantOverride("shadow_offset_y", 2);
        var about = _layout.GetNode<Label>("%InfoAbout");
        about.AddThemeFontOverride("font", HudKit.Medium);
        about.AddThemeFontSizeOverride("font_size", 17);
        about.AddThemeColorOverride("font_color", HudKit.Fg with { A = 0.9f });
        foreach (string head in new[] { "%SignatureHead", "%RulesHead" })
        {
            var l = _layout.GetNode<Label>(head);
            l.AddThemeFontOverride("font", HudKit.Tracked(HudKit.Bold, 2));
            l.AddThemeFontSizeOverride("font_size", 15);
            l.AddThemeColorOverride("font_color", Palette.Header);
        }
    }

    /// <summary>Fills the right-hand panel for the clicked army: its picture, Army Forge's description, three signature
    /// units and its army rule written out. Nothing picked = a line saying to click one.</summary>
    private async void ShowInfo(string? army)
    {
        if (_layout == null) return;
        int ask = ++_infoAsk;
        var art = _layout.GetNode<Control>("%InfoArt");
        foreach (Node n in art.GetChildren()) { art.RemoveChild(n); n.QueueFree(); }
        var signature = _layout.GetNode<HBoxContainer>("%Signature");
        var rules = _layout.GetNode<VBoxContainer>("%Rules");
        foreach (Node n in signature.GetChildren()) { signature.RemoveChild(n); n.QueueFree(); }
        foreach (Node n in rules.GetChildren()) { rules.RemoveChild(n); n.QueueFree(); }
        var name = _layout.GetNode<Label>("%InfoName");
        var line = _layout.GetNode<Label>("%InfoLine");
        var about = _layout.GetNode<Label>("%InfoAbout");
        foreach (string n in new[] { "%SignatureHead", "%RulesHead" }) _layout.GetNode<Control>(n).Visible = false;
        if (army == null)
        {
            name.Text = "";
            line.Text = "";
            about.Text = "Click an army to read about it.";
            return;
        }

        // The picture and name show at once; the book's facts follow when the library has loaded.
        Control pic = MenuArt.View(MenuPictures.Army(System, army), System);
        pic.MouseFilter = MouseFilterEnum.Ignore;
        pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        art.AddChild(pic);
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = new Gradient { Colors = new[] { new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.85f) }, Offsets = new[] { 0.35f, 1f } },
                FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1), Width = 8, Height = 128 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        art.AddChild(shade);
        name.Text = army.ToUpperInvariant();
        line.Text = ArmyGroups.GroupOf(System, army);
        about.Text = "";

        ArmyInfo.Info? data;
        try { data = await ArmyInfo.GetAsync(System, army); }
        catch (Exception) { data = null; }
        if (ask != _infoAsk || !IsInstanceValid(this) || !IsInsideTree() || data == null) return;

        var bits = new List<string> { data.Group, $"{data.Units} units" };
        if (data.Spells > 0) bits.Add($"{data.Spells} spells");
        if (Counts != null) { int c = Counts.TryGetValue(army, out int k) ? k : 0; bits.Add(c == 1 ? "1 saved list" : $"{c} saved lists"); }
        line.Text = string.Join("  -  ", bits);
        about.Text = data.About.Length > 0 ? data.About : "Army Forge has no description for this army.";

        _layout.GetNode<Control>("%SignatureHead").Visible = data.Signature.Count > 0;
        foreach (ArmyInfo.Unit u in data.Signature) signature.AddChild(SignatureCard(army, u));
        var rulesHead = _layout.GetNode<Label>("%RulesHead");
        rulesHead.Visible = data.ArmyRules.Count > 0;
        rulesHead.Text = data.ArmyRules.Count > 1 ? "ARMY RULES" : "ARMY RULE";
        foreach (ArmyInfo.Rule r in data.ArmyRules)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            box.AddChild(HudKit.Text(r.Name, 19, Palette.RuleGold, HudKit.Bold));
            var text = HudKit.Text(r.Text, 16, HudKit.Fg with { A = 0.85f }, HudKit.Medium);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.CustomMinimumSize = new Vector2(460, 0);
            box.AddChild(text);
            rules.AddChild(box);
        }
    }

    /// <summary>One signature unit: its picture, name and points, in the theme's panel.</summary>
    private static Control SignatureCard(string army, ArmyInfo.Unit u)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = $"{u.Name} - {u.Category}" };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        Control pic;
        if (HudKit.UnitPicture(army, u.Name) is { } tex)
            pic = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
        else
        {
            var holder = new CenterContainer();
            holder.AddChild(HudKit.Icon("person", 40, HudKit.Teal));
            pic = holder;
        }
        pic.CustomMinimumSize = new Vector2(0, 86);
        pic.ClipContents = true;
        pic.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(pic);
        var nm = HudKit.Text(u.Name, 16, HudKit.Fg, HudKit.Bold);
        nm.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        nm.CustomMinimumSize = new Vector2(120, 0);
        col.AddChild(nm);
        col.AddChild(HudKit.Text($"{u.Points} pts", 15, HudKit.Lime, HudKit.Semi));
        card.AddChild(col);
        return card;
    }

    private void Filter(string q)
    {
        q = q.Trim();
        foreach (var t in _tiles) t.Tile.Visible = q.Length == 0 || t.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
        foreach (var grp in _tiles.GroupBy(t => t.Group))
        {
            bool any = grp.Any(t => t.Tile.Visible);
            grp.Key.Visible = any;
            grp.First().Header.Visible = any;
        }
    }

    private void Commit()
    {
        if (Selected is not { } army) return;
        Picked?.Invoke(army);
        QueueFree();
    }

    /// <summary>Closes the grid without picking and without the Closed callback (its job was done another way).</summary>
    public void Dismiss() => QueueFree();

    public void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }

    /// <summary>Esc closes the grid before anything under it.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsVisibleInTree() || !e.IsActionPressed("ui_cancel")) return;
        Close();
        GetViewport().SetInputAsHandled();
    }
}
