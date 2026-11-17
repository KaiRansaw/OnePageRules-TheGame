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
    private static int Columns => Math.Clamp(Mathf.RoundToInt(4f / HudKit.PictureSize.Scale), 2, 7);
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
        _commit.Text = CommitText;
        _commit.TooltipText = "Use the picked army (or double-click a tile).";
        _commit.Pressed += Commit;

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
        b.TooltipText = army;
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
