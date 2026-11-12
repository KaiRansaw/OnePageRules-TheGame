using FdgRaylib.Lobby;
using FdgRaylib.SaveLoad;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// Load Game (Kai's screen map, 2026-11-07: "save tiles with a board picture, All / Grimdark Future / Age of
/// Fantasy filters, a big preview, LOAD and Delete"). The saves are read in the background; picking one shows
/// the board as it was left, who played and the score. LOAD opens the lobby around the save (the armies and
/// settings are the save's; LAUNCH carries on) - the same flow the Raylib build uses.
/// </summary>
public partial class LoadGameScreen : Control
{
    private enum Filter { All, Grimdark, Fantasy }

    private List<SavedGameInfo> _saves = new();
    private readonly HashSet<SavedGameInfo> _read = new(), _failed = new();
    private readonly object _gate = new();
    private Filter _filter = Filter.All;
    private SavedGameInfo? _picked;
    private VBoxContainer _list = null!, _details = null!;
    private SavePreview _preview = null!;
    private Label _error = null!;
    private HBoxContainer _filters = null!;
    private int _generation, _shownRead = -1;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 40);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 18);
        margin.AddChild(col);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 20);
        var back = Ui.Button("Back", App.I.Back, "Back to the previous screen (Esc).", "BackButton");
        back.CustomMinimumSize = new Vector2(140, 48);
        header.AddChild(back);
        var title = new Label { Text = "Load Game", VerticalAlignment = VerticalAlignment.Center };
        Ui.ScreenTitle(title);
        header.AddChild(title);
        header.AddChild(Ui.Grow());
        _filters = new HBoxContainer();
        _filters.AddThemeConstantOverride("separation", 8);
        header.AddChild(_filters);
        col.AddChild(header);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 24);
        col.AddChild(body);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(560, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);
        body.AddChild(scroll);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 12);
        _preview = new SavePreview { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 360) };
        right.AddChild(_preview);
        _details = new VBoxContainer();
        _details.AddThemeConstantOverride("separation", 6);
        right.AddChild(_details);
        body.AddChild(right);

        _error = new Label { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _error.AddThemeColorOverride("font_color", Palette.WoundsRed);
        col.AddChild(_error);

        Rescan();
    }

    private void Rescan()
    {
        int gen = ++_generation;
        _saves = SavedGameInfo.List();
        lock (_gate) { _read.Clear(); _failed.Clear(); }
        _picked = _saves.FirstOrDefault();
        List<SavedGameInfo> toRead = _saves;
        Task.Run(() =>
        {
            foreach (SavedGameInfo s in toRead)
            {
                if (gen != _generation) return;
                bool ok;
                try { s.Read(); ok = true; }
                catch (Exception ex) { Console.WriteLine($"Could not read save {s.Path}: {ex.GetBaseException().Message}"); ok = false; }
                lock (_gate) { if (ok) _read.Add(s); else _failed.Add(s); }
            }
        });
        Rebuild();
    }

    public override void _Process(double delta)
    {
        int n;
        lock (_gate) n = _read.Count + _failed.Count;
        if (n != _shownRead) { _shownRead = n; Rebuild(); }
    }

    private bool Ready(SavedGameInfo s) { lock (_gate) return _read.Contains(s); }
    private bool Failed(SavedGameInfo s) { lock (_gate) return _failed.Contains(s); }

    private bool Shown(SavedGameInfo s) => _filter switch
    {
        Filter.Grimdark => Ready(s) && s.IsGrimdark == true,
        Filter.Fantasy => Ready(s) && s.IsGrimdark == false,
        _ => true,
    };

    private void Rebuild()
    {
        foreach (Node c in _filters.GetChildren()) c.QueueFree();
        foreach (var (f, label) in new[] { (Filter.All, "All"), (Filter.Grimdark, "Grimdark Future"), (Filter.Fantasy, "Age of Fantasy") })
        {
            var b = Ui.Button(label, () => { _filter = f; Rebuild(); }, $"Show {label.ToLowerInvariant()} saves.", f == _filter ? "CommitButton" : "BackButton");
            _filters.AddChild(b);
        }

        foreach (Node c in _list.GetChildren()) c.QueueFree();
        var shown = _saves.Where(Shown).ToList();
        if (shown.Count == 0)
            _list.AddChild(Ui.Label(_saves.Count == 0 ? "No saved games yet. Save one from the in-game menu (Esc), or with Ctrl+S." : "No saves for this game.", "DimLabel", wrap: true));
        foreach (SavedGameInfo s in shown) _list.AddChild(Tile(s));
        if (_picked != null && !shown.Contains(_picked)) _picked = shown.FirstOrDefault();
        ShowPicked();
    }

    private Control Tile(SavedGameInfo s)
    {
        bool picked = ReferenceEquals(s, _picked);
        var b = new Button { CustomMinimumSize = new Vector2(0, 84), FocusMode = FocusModeEnum.None, Name = "Save" };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = picked ? HudKit.Lime : HudKit.Edge, Cut = 10 };
        if (picked) box = box.With(x => x.Glow = HudKit.Lime with { A = 0.35f });
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        v.OffsetLeft = 16; v.OffsetTop = 8; v.OffsetRight = -12;
        v.AddThemeConstantOverride("separation", 0);
        v.AddChild(HudKit.Text(s.Title, 18, picked ? HudKit.Lime : HudKit.Fg, HudKit.Bold));
        string what = Ready(s) ? $"{s.GameLabel}  -  {s.ModeLabel}  -  {s.RoundLabel}"
            : Failed(s) ? "Couldn't be read" : "Reading...";
        v.AddChild(HudKit.Text(what, 14, Failed(s) || Ready(s) && !s.CanResume ? Palette.WoundsRed : HudKit.Dim, HudKit.Semi));
        string who = Ready(s) ? string.Join("   vs   ", s.Players.Select(p => $"{p.Name} ({p.Faction})")) : "";
        v.AddChild(HudKit.Text($"{s.Saved:yyyy-MM-dd HH:mm}" + (who.Length > 0 ? "   " + who : ""), 13, HudKit.Dim, HudKit.Medium).With(l => l.ClipText = true));
        b.AddChild(v);
        b.Pressed += () => { _picked = s; _error.Text = ""; Rebuild(); };
        return b;
    }

    private void ShowPicked()
    {
        foreach (Node c in _details.GetChildren()) c.QueueFree();
        _preview.Save = _picked != null && Ready(_picked) ? _picked : null;
        _preview.QueueRedraw();
        if (_picked is not { } s) return;
        _details.AddChild(HudKit.Text(s.Title, 26, HudKit.Fg, HudKit.Exo(800)));
        if (Ready(s))
        {
            _details.AddChild(HudKit.Text($"{s.GameLabel}  -  {s.ModeLabel}  -  {s.ArmyPoints} pts  -  {s.RoundLabel}", 16, HudKit.Dim, HudKit.Semi));
            var grid = new GridContainer { Columns = 4 };
            grid.AddThemeConstantOverride("h_separation", 28);
            foreach (string h in new[] { "PLAYER", "ARMY", "OBJECTIVES", s.Players.Any(p => p.Vp != null) ? "VP" : "" })
                grid.AddChild(HudKit.Text(h, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
            for (int i = 0; i < s.Players.Count; i++)
            {
                SavedGameInfo.Player p = s.Players[i];
                grid.AddChild(HudKit.Text(p.Name, 17, SavePreview.Seat(i), HudKit.Bold));
                grid.AddChild(HudKit.Text(p.Army, 16, HudKit.Fg, HudKit.Semi));
                grid.AddChild(HudKit.Text(p.Objectives.ToString(), 17, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center));
                grid.AddChild(HudKit.Text(p.Vp?.ToString() ?? "", 17, HudKit.Gold, HudKit.Bold, HorizontalAlignment.Center));
            }
            _details.AddChild(grid);
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var load = Ui.Button("LOAD", () => Load(s), "Open the lobby around this save; LAUNCH carries on from where it was saved.", "CommitButton");
        load.Name = "LoadSave";
        load.CustomMinimumSize = new Vector2(200, 52);
        load.Disabled = !Ready(s) || !s.CanResume;
        row.AddChild(load);
        row.AddChild(Ui.Button("Delete", () => AskDelete(s), "Delete this save for good.", "BackButton"));
        _details.AddChild(row);
    }

    private void Load(SavedGameInfo s)
    {
        if (!LobbySession.TryResume(s.Path, out LobbySession? session, out string error) || session == null)
        {
            _error.Text = error;
            return;
        }
        App.I.Open<LobbyScreen>(Scenes.Lobby, l => l.Session = session);
    }

    private void AskDelete(SavedGameInfo s)
    {
        var d = new ConfirmationDialog { Title = "Delete save?", DialogText = $"Delete \"{s.Title}\"? This can't be undone.", OkButtonText = "Delete", CancelButtonText = "Keep it" };
        d.Confirmed += () =>
        {
            try { File.Delete(s.Path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _error.Text = "Couldn't delete it: " + ex.Message; }
            d.QueueFree();
            Rescan();
        };
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
    }

    public override void _ExitTree() => _generation++;   // stops the background reader
}

/// <summary>The board as the save left it: table, terrain, objectives (owner's colour) and every model.</summary>
public partial class SavePreview : Control
{
    public SavedGameInfo? Save;

    public static Color Seat(int i) => i switch
    {
        0 => HudKit.Friendly, 1 => HudKit.Enemy, 2 => new Color("4fd1bd"), _ => new Color("f0c050"),
    };

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.05f, 0.055f));
        if (Save is not { } s) { DrawString(HudKit.Semi, new Vector2(20, 34), "Pick a save to see its board.", HorizontalAlignment.Left, -1, 16, HudKit.Dim); return; }
        float tw = s.TableW, th = s.TableH;
        float scale = Mathf.Max(0.1f, Mathf.Min(Size.X / tw, Size.Y / th));
        Vector2 o = new((Size.X - tw * scale) / 2f, (Size.Y - th * scale) / 2f);
        Vector2 P(float x, float z) => o + new Vector2(x * scale, (th - z) * scale);
        var table = new Rect2(P(0, th), new Vector2(tw * scale, th * scale));
        DrawRect(table, new Color(0.115f, 0.14f, 0.125f));
        for (float x = 6; x < tw; x += 6) DrawLine(P(x, 0), P(x, th), new Color(1, 1, 1, x % 12 == 0 ? 0.1f : 0.04f));
        for (float z = 6; z < th; z += 6) DrawLine(P(0, z), P(tw, z), new Color(1, 1, 1, z % 12 == 0 ? 0.1f : 0.04f));
        foreach (SavedGameInfo.Piece t in s.Terrain)
            foreach (var poly in FdgRaylib.Rendering.ZoneOutlines.Polygons(t.Shape))
            {
                if (poly.Length < 3) continue;
                var pts = poly.Select(p => P(p.X, p.Z)).ToArray();
                if (Geometry2D.TriangulatePolygon(pts).Length > 0) DrawColoredPolygon(pts, Battle.BattleTable.TerrainColour(t.Type) with { A = 0.85f });
            }
        foreach (SavedGameInfo.Marker m in s.Objectives)
        {
            Vector2 c = P(m.X, m.Z);
            float r = Mathf.Max(6f, 1.1f * scale);
            DrawCircle(c, r, m.Owner is { } ow ? Seat(ow) : new Color(0.9f, 0.9f, 0.9f));
            DrawArc(c, r, 0, Mathf.Tau, 24, Colors.Black, 2f, true);
        }
        foreach (SavedGameInfo.Figure f in s.Models)
            DrawCircle(P(f.X, f.Z), Mathf.Max(2f, f.Radius * scale), Seat(f.Owner));
        DrawRect(table, HudKit.Edge, false, 2f);
    }
}
