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
    /// <summary>Army Forge: the games to switch between, as tabs in the top bar (null = no tabs).</summary>
    public IReadOnlyList<EGameSystem>? Games { get; init; }
    /// <summary>A game tab was clicked: the owner reopens the grid for that game.</summary>
    public Action<EGameSystem>? GameChanged { get; init; }

    private GridContainer? _first;
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

    public override void _Ready()
    {
        Name = "ArmyPicker";
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink });
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 32);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        margin.AddChild(col);

        // Top: title, where you are, search.
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 20);
        var title = new Label { Text = Title };
        Ui.ScreenTitle(title);
        title.AddThemeFontSizeOverride("font_size", 40);
        top.AddChild(title);
        var crumb = HudKit.Text(Crumb.Length > 0 ? Crumb : GameSystemInfo.DisplayName(System), 18, HudKit.Lime, HudKit.Semi);
        crumb.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(crumb);
        HBoxContainer tabs = top;
        if (Games != null)
        {
            // The game tabs get a row of their own under the title, so the top bar never runs off the screen.
            tabs = new HBoxContainer();
            tabs.AddThemeConstantOverride("separation", 8);
        }
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
        top.AddChild(Ui.Grow());
        var search = new LineEdit { Name = "ArmySearch", PlaceholderText = "Search armies...", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(300, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        search.TextChanged += Filter;
        top.AddChild(search);
        col.AddChild(top);
        if (tabs != top) col.AddChild(tabs);
        col.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(0, 1) });

        // The grid, folded into Army Forge's groups.
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(body);
        col.AddChild(scroll);
        var names = Armies ?? BookIndex.Books(System).Select(b => b.Name).ToList();
        foreach (ArmyGroups.Group g in ArmyGroups.Arrange(System, names))
        {
            var head = HudKit.Heading(g.Name, 16);
            body.AddChild(head);
            var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 16);
        var back = Ui.Button("Back", Close, "Close without picking (Esc).", "BackButton");
        back.Name = "ArmyPickerBack";
        back.CustomMinimumSize = new Vector2(130, 50);
        bottom.AddChild(back);
        _info = HudKit.Text("Pick an army", 20, HudKit.Dim, HudKit.Semi);
        _info.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottom.AddChild(_info);
        _commit = Ui.Button(CommitText, Commit, "Use the picked army (or double-click a tile).", "CommitButton");
        _commit.Name = "ArmyPickerCommit";
        _commit.CustomMinimumSize = new Vector2(300, 54);
        bottom.AddChild(_commit);
        col.AddChild(bottom);

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

    private Button Tile(string army)
    {
        int? count = Counts == null ? null : Counts.TryGetValue(army, out int n) ? n : 0;
        var b = new Button
        {
            Name = "Army_" + Forge.ForgeWidgets.SafeName(army), CustomMinimumSize = new Vector2(0, 112), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipContents = true, FocusMode = FocusModeEnum.All, TooltipText = army,
        };
        var box = new ChamferBox { Fill = Palette.Panel, Border = HudKit.Edge, Cut = 10, BorderWidth = 1 };
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box.With(x => x.Border = HudKit.EdgeHot));
        b.AddThemeStyleboxOverride("pressed", box.With(x => x.Border = HudKit.Lime));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        // The picture on the right ~70%, faded into the panel on the left so the name reads.
        Control art = MenuArt.View(MenuPictures.Army(System, army), System);
        art.MouseFilter = MouseFilterEnum.Ignore;
        art.AnchorLeft = 0.28f; art.AnchorRight = 1; art.AnchorBottom = 1;
        art.OffsetLeft = 1; art.OffsetTop = 1; art.OffsetRight = -1; art.OffsetBottom = -1;
        b.AddChild(art);
        var fade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { Palette.Panel, Palette.Panel with { A = 0 } }, Offsets = new[] { 0.25f, 0.62f } },
                FillTo = new Vector2(1, 0), Width = 256, Height = 8,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
        };
        fade.SetAnchorsPreset(LayoutPreset.FullRect);
        fade.OffsetLeft = 1; fade.OffsetTop = 1; fade.OffsetRight = -1; fade.OffsetBottom = -1;
        b.AddChild(fade);
        var name = HudKit.Text(army, 24, HudKit.Fg, HudKit.Bold);
        name.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        name.AddThemeConstantOverride("shadow_offset_y", 2);
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.SetAnchorsPreset(LayoutPreset.LeftWide);
        name.AnchorRight = 0.7f;
        name.OffsetLeft = 18;
        name.VerticalAlignment = VerticalAlignment.Center;
        b.AddChild(name);
        if (count is { } c)
        {
            var badge = HudKit.Chip(c == 0 ? "no lists" : c == 1 ? "1 list" : $"{c} lists", c == 0 ? HudKit.Dim : HudKit.Lime, c > 0, 15);
            badge.MouseFilter = MouseFilterEnum.Ignore;
            badge.SetAnchorsPreset(LayoutPreset.BottomRight);
            badge.GrowHorizontal = GrowDirection.Begin; badge.GrowVertical = GrowDirection.Begin;
            badge.OffsetRight = -10; badge.OffsetBottom = -8;
            b.AddChild(badge);
            if (c == 0) b.Modulate = new Color(0.55f, 0.55f, 0.55f);
        }
        b.Pressed += () => Select(army);
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
        }
        if (_info == null) return;
        _commit.Disabled = army == null;
        _info.Text = army == null ? "Pick an army" : army + (Describe?.Invoke(army) is { Length: > 0 } more ? "  -  " + more : "");
        _info.AddThemeColorOverride("font_color", army == null ? HudKit.Dim : HudKit.Fg);
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
