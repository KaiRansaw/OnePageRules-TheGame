using FDG;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Maps;

/// <summary>
/// The Map Maker's list (mockup #17, approved 2026-11-07): maps grouped by table size, a preview of the one picked with
/// Edit / Copy / Delete, and + NEW MAP. The editor is <see cref="MapEditorScreen"/> (3D, Map Maker mockup 2026-11-13).
/// </summary>
public partial class MapMakerScreen : Control
{
    private List<MapDraft.ListEntry> _maps = new();
    private string? _selected;
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        top.AddChild(Ui.Button("Back", App.I.Back, "Back to the main menu (Esc).", "BackButton").Also(b => { b.Name = "Back"; b.CustomMinimumSize = new Vector2(130, 44); }));
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("MAP MAKER", 30, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text("Your maps - terrain and objective markers. Deployment is picked in the lobby.", 14, HudKit.Dim, HudKit.Semi));
        top.AddChild(t);
        top.AddChild(Ui.Grow());
        top.AddChild(Ui.Button("+ NEW MAP", () => OpenEditor(MapDraft.New(72, 48)), "Start an empty map (pick its table size in the editor).", "CommitButton")
            .Also(b => { b.Name = "NewMap"; b.CustomMinimumSize = new Vector2(170, 44); }));
        root.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(top, 14, 8, 14, 8)));

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        var ls = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        ls.AddChild(_list);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(ls, 12, 10, 12, 10)).Also(p => p.CustomMinimumSize = new Vector2(440, 0)));
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 8);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(_detail, 16, 12, 16, 12)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        root.AddChild(body);
        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        root.AddChild(_status);
        VisibilityChanged += () => { if (Visible) Refresh(); };
        Refresh();
    }

    private void Say(string text, Color? c = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c ?? HudKit.Dim);
    }

    private void Refresh()
    {
        _maps = MapDraft.ListAll();
        if (_selected == null || _maps.All(m => m.Path != _selected)) _selected = _maps.FirstOrDefault()?.Path;
        foreach (Node c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        if (_maps.Count == 0) _list.AddChild(ForgeWidgets.Wrapped("No maps yet. Press + NEW MAP.", 15, HudKit.Dim));
        foreach (var group in _maps.GroupBy(m => (m.W, m.H)).OrderBy(g => g.Key.W * g.Key.H))
        {
            _list.AddChild(HudKit.Heading(MapDraft.SizeName(group.Key.W, group.Key.H), 13).Also(h => h.TooltipText = MapDraft.SizeGames(group.Key.W, group.Key.H)));
            foreach (var m in group)
            {
                var path = m.Path;
                var b = new Button { Name = ForgeWidgets.SafeName("Map_" + m.Name), ToggleMode = true, ButtonPressed = m.Path == _selected, Alignment = HorizontalAlignment.Left,
                    Text = $"{m.Name}   -   {m.Pieces} terrain, {m.Objectives} objectives", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) };
                b.Pressed += () => { _selected = path; Refresh(); };
                _list.AddChild(b);
            }
        }
        Detail();
    }

    private void Detail()
    {
        foreach (Node c in _detail.GetChildren()) { _detail.RemoveChild(c); c.QueueFree(); }
        if (_maps.FirstOrDefault(m => m.Path == _selected) is not { } sel)
        {
            _detail.AddChild(ForgeWidgets.Wrapped("Make a map: place the book's terrain pieces in 3D, add objective markers, and play it from the lobby's map picker.", 16, HudKit.Dim));
            return;
        }
        _detail.AddChild(HudKit.Text(sel.Name.ToUpperInvariant(), 26, HudKit.Fg, HudKit.Exo(800)));
        _detail.AddChild(HudKit.Text($"{MapDraft.SizeName(sel.W, sel.H)} - {sel.Pieces} terrain pieces, {sel.Objectives} objectives", 15, HudKit.Dim, HudKit.Semi));
        _detail.AddChild(HudKit.Text(MapDraft.SizeGames(sel.W, sel.H), 14, HudKit.Dim, HudKit.Semi));
        if (MapDraft.Load(sel.Path, out _) is { } draft)
            _detail.AddChild(new MapPreview { Draft = draft, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 360) });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(Ui.Button("Delete", () => AskDelete(sel), "Delete this map (asks first).", "BackButton").Also(b => b.Name = "Delete"));
        row.AddChild(Ui.Grow());
        row.AddChild(Ui.Button("Copy", () => { if (MapDraft.CopyOf(sel.Path, out string? e) is { } d) OpenEditor(d); else Say(e ?? "Couldn't read the map.", Palette.WoundsRed); },
            "Open a copy of this map as a new one.").Also(b => b.Name = "Copy"));
        row.AddChild(Ui.Button("EDIT", () => { if (MapDraft.Load(sel.Path, out string? e) is { } d) OpenEditor(d); else Say(e ?? "Couldn't read the map.", Palette.WoundsRed); },
            "Open this map in the editor.", "CommitButton").Also(b => { b.Name = "Edit"; b.CustomMinimumSize = new Vector2(160, 48); }));
        _detail.AddChild(row);
    }

    private void AskDelete(MapDraft.ListEntry m)
    {
        var dlg = new ConfirmationDialog { Title = "Delete map?", DialogText = $"Delete \"{m.Name}\"? This can't be undone.", OkButtonText = "Delete" };
        dlg.Confirmed += () => { Say(MapDraft.Delete(m.Path) ?? $"Deleted \"{m.Name}\"."); _selected = null; Refresh(); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void OpenEditor(MapDraft draft) => App.I.Open<MapEditorScreen>(Scenes.MapEditor, e => e.Draft = draft);

    /// <summary>Screenshot driver: "new", "edit:i", "copy:i".</summary>
    public void Drive(string cmd)
    {
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c];
        int.TryParse(c < 0 ? "" : cmd[(c + 1)..], out int n);
        if (verb == "new") OpenEditor(MapDraft.New(72, 48));
        else if (n < _maps.Count && (verb == "edit" ? MapDraft.Load(_maps[n].Path, out _) : MapDraft.CopyOf(_maps[n].Path, out _)) is { } d) OpenEditor(d);
    }
}
