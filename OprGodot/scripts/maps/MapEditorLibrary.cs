using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Forge;
using OprGodot.Hud;
using OprGodot.Lab;

namespace OprGodot.Maps;

/// <summary>
/// The Map Maker's piece library (Kai, 2026-11-26, mockup A of "Map Maker Piece Browser Mockups"): every piece in one
/// left panel - tabs for the book's rules pieces, the game's single models, your Terrain Workshop pieces and the
/// Warhammer models - with search, Set and Kind filters, sorting, recently used, a picture grid, and a bigger preview
/// on the right while the mouse is over a tile.
/// </summary>
public partial class MapEditorScreen
{
    private enum ELibTab { Rules, Models, Mine, Warhammer }

    /// <summary>One tile: what it is, how it's filtered, and what picking it does.</summary>
    private sealed record LibItem(string Key, string Name, string Set, string Kind, ETerrainType Rules, float W, float D, float H,
        string Source, Color Tint, Func<Texture2D?> Picture, Action Pick, Func<bool> Lit);

    private static ELibTab _libTab = ELibTab.Rules;
    private static string _libSet = "", _libKind = "", _libSort = "Name";
    private static readonly List<string> Recent = new();
    private LineEdit? _libSearch;
    private VBoxContainer? _library;
    private GridContainer? _libGrid;
    private ScrollContainer? _libScroll;
    private string _libSig = "";
    private LibItem? _hover;
    private readonly Dictionary<string, Button> _libButtons = new();

    // ------------------------------------------------------------------ the items

    private List<LibItem> LibItems(ELibTab tab) => tab switch
    {
        ELibTab.Rules => TerrainBook.Kinds.Select(k => new LibItem("kind:" + k.Id, k.Name, "The book's kinds", k.Id, k.Rules,
            k.Width, k.Round ? k.Width : k.Depth, k.Height, "Advanced Rulebook 3.6.0", BattleTable.TerrainColour(k.Rules), () => null,
            () => PickKind(k), () => _tool == ETool.Place && _model == null && _single == null && _workshop == null && _kind == k)).ToList(),
        ELibTab.Models => PieceModels.Catalogue.Select(m =>
        {
            var size = KnownSize(ModelKey(m));
            string kind = ModelKind(m.Name, size.H, size.W * size.D);
            var rules = TerrainBook.Kind(kind)?.Rules ?? WarhammerTerrainLibrary.RulesFor(kind);
            return new LibItem("model:" + m.Path, m.Name, (m.Style == TerrainPieces.Realistic ? "Realistic: " : "") + m.Group, kind, rules,
                size.W, size.D, size.H, m.Style == TerrainPieces.Realistic ? "Poly Haven (CC0)" : "The game's stylised packs (CC0)",
                BattleTable.TerrainColour(rules), () => Thumb(ModelKey(m)), () => PickSingle(m), () => _tool == ETool.Place && _single == m);
        }).ToList(),
        ELibTab.Mine => TerrainPieces.All.Where(p => p.Suits(Theme)).Select(p =>
        {
            var k = TerrainBook.Kind(p.Kind);
            var rules = k?.Rules ?? WarhammerTerrainLibrary.RulesFor(p.Kind);
            return new LibItem("piece:" + p.Id, p.Name, p.Style == TerrainPieces.Realistic ? "Realistic" : "Stylised", p.Kind, rules,
                p.W, p.D, k?.Height ?? 2f, "Your Terrain Workshop piece", BattleTable.TerrainColour(rules), () => Thumb(PieceKey(p)),
                () => PickWorkshop(p), () => _tool == ETool.Place && _workshop?.Id == p.Id);
        }).ToList(),
        ELibTab.Warhammer => WarhammerModels.Library is { } lib
            ? lib.Placeable.Select(e => new LibItem("wh:" + e.Id, e.Name, e.SetName, e.Match.Kind, WarhammerTerrainLibrary.RulesFor(e.Match.Kind),
                e.Match.W, e.Match.D, e.Match.H, "Warhammer Map Builder (your download)", BattleTable.TerrainColour(WarhammerTerrainLibrary.RulesFor(e.Match.Kind)),
                () => ModelThumb(lib, e.Id), () => PickModel(e), () => _tool == ETool.Place && _model?.Id == e.Id)).ToList()
            : new List<LibItem>(),
        _ => new List<LibItem>(),
    };

    private static string KindName(string id) => TerrainBook.Kind(id)?.Name
        ?? WarhammerTerrainLibrary.Kinds.FirstOrDefault(k => k.Id == id).Name ?? id;

    private static string ModelKind(string name, float h, float area)
    {
        string n = name.ToLowerInvariant();
        bool plant = new[] { "tree", "pine", "sapling", "bush", "fern", "plant", "oak", "birch", "willow", "palm", "shrub", "flower", "grass", "mushroom" }.Any(n.Contains);
        return plant ? "Forest" : TerrainLooks.GuessKind(null, name, ETerrainType.None, h > 0 ? h : 1f, area > 0 ? area : 16f);
    }

    private static string RulesText(ETerrainType r)
    {
        var parts = new List<string>();
        if (r.HasFlag(ETerrainType.Impassible)) parts.Add("Impassable");
        if (r.HasFlag(ETerrainType.Blocking)) parts.Add("blocks sight");
        if (r.HasFlag(ETerrainType.Cover)) parts.Add("Cover");
        if (r.HasFlag(ETerrainType.Difficult)) parts.Add("Difficult");
        if (r.HasFlag(ETerrainType.Dangerous)) parts.Add("Dangerous");
        if (r.HasFlag(ETerrainType.Climbable)) parts.Add("Climbable");
        if (r.HasFlag(ETerrainType.Steep)) parts.Add("Steep");
        if (r.HasFlag(ETerrainType.Unstable)) parts.Add("Unstable");
        if (r.HasFlag(ETerrainType.Dense)) parts.Add("Dense");
        return parts.Count == 0 ? "No rules (looks only)" : string.Join(", ", parts);
    }

    private static string SizeText(LibItem i) => i.W > 0 ? $"{i.W:0.#} x {i.D:0.#}\"" + (i.H > 0 ? $", {i.H:0.#}\" tall" : "") : "size not measured yet";

    // ------------------------------------------------------------------ picking

    private TerrainPieces.Piece? _workshop;

    private void Picked(string key, string name)
    {
        Recent.Remove(key);
        Recent.Insert(0, key);
        while (Recent.Count > 6) Recent.RemoveAt(Recent.Count - 1);
        _tool = ETool.Place; _sel = null; _selObjective = -1; _selBase = -1;
        Say($"Placing {name} - click the table. Right-click to stop.");
        Rebuild();
    }

    private void PickKind(TerrainBook.PieceKind k) { _kind = k; _model = null; _single = null; _workshop = null; Picked("kind:" + k.Id, k.Name); }
    private void PickSingle(PieceModels.Model m) { _single = m; _model = null; _workshop = null; Picked("model:" + m.Path, m.Name); }
    private void PickModel(WarhammerTerrainLibrary.Entry e) { _model = e; _single = null; _workshop = null; Picked("wh:" + e.Id, e.Name); }
    private void PickWorkshop(TerrainPieces.Piece p) { _workshop = p; _model = null; _single = null; Picked("piece:" + p.Id, p.Name); }

    /// <summary>A map piece showing a Workshop piece by name (the same for every player): its kind's rules, its own footprint.</summary>
    private MapPiece WorkshopPiece(TerrainPieces.Piece p, float x, float z)
    {
        var k = TerrainBook.Kind(p.Kind) ?? _kind;
        var m = MapPiece.FromKind(k, x, z);
        m.Name = p.Name; m.Look = p.LookId; m.Round = p.Round;
        m.W = p.W; m.D = p.Round ? p.W : p.D;
        m.Points = MapPiece.PointsFor(m.W, m.D);
        return m;
    }

    // ------------------------------------------------------------------ the panel

    /// <summary>The library, kept across rebuilds (so the grid and its scroll don't jump); only the lit tile changes.</summary>
    private void Library()
    {
        var items = LibItems(_libTab);
        string q = _libSearch?.Text.Trim() ?? "";
        string sig = $"{_libTab}|{_libSet}|{_libKind}|{_libSort}|{q}|{items.Count}|{string.Join(",", Recent)}|{_thumbsDone}";
        if (sig == _libSig && _library != null && IsInstanceValid(_library))
        {
            _left.AddChild(_library);
            foreach (var (key, btn) in _libButtons)
                if (IsInstanceValid(btn) && btn.GetMeta("lit", false).AsBool() != IsLit(key)) btn.SetPressedNoSignal(IsLit(key));
            return;
        }
        int scroll = _libScroll?.ScrollVertical ?? 0;
        bool searchHadFocus = _libSearch != null && IsInstanceValid(_libSearch) && _libSearch.HasFocus();
        int caret = _libSearch?.CaretColumn ?? 0;
        if (_library != null && IsInstanceValid(_library)) _library.QueueFree();
        _libSig = sig;
        _libButtons.Clear();
        var box = _library = new VBoxContainer { Name = "Library" };
        box.AddThemeConstantOverride("separation", 7);
        _left.AddChild(box);

        // Search, kept with its text and focus.
        _libSearch = new LineEdit { Name = "LibrarySearch", PlaceholderText = "Search pieces...", Text = q, ClearButtonEnabled = true };
        _libSearch.TextChanged += _ => Rebuild();
        box.AddChild(_libSearch);
        if (searchHadFocus) Callable.From(() => { if (IsInstanceValid(_libSearch)) { _libSearch.GrabFocus(); _libSearch.CaretColumn = caret; } }).CallDeferred();

        // Tabs.
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        foreach (var (tab, label) in new[] { (ELibTab.Rules, "Rules"), (ELibTab.Models, "Models"), (ELibTab.Mine, "My pieces"), (ELibTab.Warhammer, "Warhammer") })
        {
            if (tab == ELibTab.Warhammer && (WarhammerModels.Library is not { } wl || !wl.Placeable.Any())) continue;
            var t = tab;
            int count = LibItems(t).Count(i => Matches(i, q));
            var b = new Button { Name = "LibTab_" + t, Text = q.Length > 0 ? $"{label} ({count})" : label, ToggleMode = true, ButtonPressed = _libTab == t,
                FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true,
                TooltipText = t switch
                {
                    ELibTab.Rules => "The book's terrain kinds (Advanced Rulebook 3.6.0): each plays by its rules and shows the game's model for it.",
                    ELibTab.Models => "The game's own terrain models, one at a time. Each takes the rules of the kind it looks like.",
                    ELibTab.Mine => "Pieces built in the Terrain Workshop (Asset Lab -> Terrain). Placed by name: everyone sees the same piece.",
                    _ => "The Warhammer terrain you downloaded and matched (Match terrain models).",
                } };
            b.Pressed += () => { _libTab = t; _libSet = ""; _libKind = ""; Rebuild(); };
            tabs.AddChild(b);
        }
        box.AddChild(tabs);

        // Set and Kind chips, from what this tab has.
        var sets = items.Select(i => i.Set).Distinct().OrderBy(s => s).ToList();
        if (sets.Count > 1) box.AddChild(Chips("Set", sets, _libSet, s => _libSet = s, s => s));
        var kinds = items.Where(i => _libSet.Length == 0 || i.Set == _libSet).Select(i => i.Kind).Distinct().OrderBy(KindName).ToList();
        if (kinds.Count > 1 && _libTab != ELibTab.Rules) box.AddChild(Chips("Kind", kinds, _libKind, k => _libKind = k, KindName));

        // Sort and recent.
        var sortRow = new HBoxContainer();
        sortRow.AddThemeConstantOverride("separation", 5);
        sortRow.AddChild(HudKit.Text("Sort", 12, HudKit.Dim, HudKit.Semi));
        var sort = new OptionButton { Name = "LibrarySort", FocusMode = FocusModeEnum.None };
        string[] sorts = { "Name", "Size", "Kind" };
        foreach (string s in sorts) sort.AddItem(s);
        sort.Selected = Array.IndexOf(sorts, _libSort);
        sort.ItemSelected += i => { _libSort = sorts[(int)i]; Rebuild(); };
        sortRow.AddChild(sort);
        box.AddChild(sortRow);
        var recentItems = Recent.Select(FindItem).Where(i => i != null).Cast<LibItem>().ToList();
        if (recentItems.Count > 0)
        {
            var recent = new HFlowContainer();
            recent.AddThemeConstantOverride("h_separation", 4);
            recent.AddThemeConstantOverride("v_separation", 4);
            recent.AddChild(HudKit.Text("Recent", 12, HudKit.Dim, HudKit.Semi));
            foreach (LibItem r in recentItems)
            {
                var item = r;
                var rb = new Button { Text = r.Name, FocusMode = FocusModeEnum.None, TooltipText = $"{r.Name} - {RulesText(r.Rules)}" };
                rb.AddThemeFontSizeOverride("font_size", 12);
                rb.Pressed += item.Pick;
                recent.AddChild(rb);
            }
            box.AddChild(recent);
        }

        // The grid.
        var shown = items.Where(i => Matches(i, q) && (_libSet.Length == 0 || i.Set == _libSet) && (_libKind.Length == 0 || i.Kind == _libKind));
        shown = _libSort switch
        {
            "Size" => shown.OrderByDescending(i => i.W * i.D).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "Kind" => shown.OrderBy(i => KindName(i.Kind)).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            _ => shown.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };
        var list = shown.ToList();
        box.AddChild(HudKit.Text(list.Count == items.Count ? $"{list.Count} pieces" : $"{list.Count} of {items.Count} pieces", 12, HudKit.Dim, HudKit.Semi));
        if (list.Count == 0)
        {
            string elsewhere = string.Join(", ", Enum.GetValues<ELibTab>().Where(t => t != _libTab)
                .Select(t => (t, n: LibItems(t).Count(i => Matches(i, q)))).Where(x => x.n > 0).Select(x => $"{x.t switch { ELibTab.Rules => "Rules", ELibTab.Models => "Models", ELibTab.Mine => "My pieces", _ => "Warhammer" }} ({x.n})"));
            box.AddChild(ForgeWidgets.Wrapped(q.Length > 0 && elsewhere.Length > 0 ? $"Nothing here. Found in: {elsewhere}." : _libTab == ELibTab.Mine
                ? "No Workshop pieces for this game yet: Asset Lab -> Terrain -> Terrain Workshop." : "Nothing matches.", 12, HudKit.Dim));
        }
        _libGrid = new GridContainer { Columns = 3 };
        _libGrid.AddThemeConstantOverride("h_separation", 5);
        _libGrid.AddThemeConstantOverride("v_separation", 5);
        foreach (LibItem i in list) _libGrid.AddChild(Tile(i));
        _libScroll = new ScrollContainer { Name = "LibraryScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 430) };
        _libScroll.AddChild(_libGrid);
        box.AddChild(_libScroll);
        RestoreScroll(_libScroll, scroll);
        if (_libTab is ELibTab.Models or ELibTab.Mine) MakeThumbs();
    }

    private bool IsLit(string key) => FindItem(key) is { } i && i.Lit();

    private LibItem? FindItem(string key)
    {
        int c = key.IndexOf(':');
        ELibTab tab = key[..Math.Max(0, c)] switch { "kind" => ELibTab.Rules, "model" => ELibTab.Models, "piece" => ELibTab.Mine, _ => ELibTab.Warhammer };
        return LibItems(tab).FirstOrDefault(i => i.Key == key);
    }

    private static bool Matches(LibItem i, string q) =>
        q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Set.Contains(q, StringComparison.OrdinalIgnoreCase)
        || KindName(i.Kind).Contains(q, StringComparison.OrdinalIgnoreCase);

    private Control Chips(string title, List<string> values, string current, Action<string> set, Func<string, string> label)
    {
        var flow = new HFlowContainer { Name = "Lib" + title };
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        flow.AddChild(HudKit.Text(title, 12, HudKit.Dim, HudKit.Semi).Also(l => l.CustomMinimumSize = new Vector2(34, 0)));
        foreach (string v in new[] { "" }.Concat(values))
        {
            string value = v;
            var b = new Button { Name = $"Lib{title}_{(v.Length == 0 ? "All" : v.Replace(' ', '_'))}", Text = v.Length == 0 ? "All" : label(v),
                ToggleMode = true, ButtonPressed = current == v, FocusMode = FocusModeEnum.None };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Pressed += () => { set(current == value ? "" : value); Rebuild(); };
            flow.AddChild(b);
        }
        return flow;
    }

    private Button Tile(LibItem i)
    {
        string name = i.Key.StartsWith("kind:") ? "Kind_" + i.Key[5..] : i.Key.StartsWith("wh:") ? "Model_" + i.Key[3..] : "Lib_" + i.Name.Replace(' ', '_');
        var b = new Button { Name = name, ToggleMode = true, FocusMode = FocusModeEnum.None, ButtonPressed = i.Lit(), CustomMinimumSize = new Vector2(124, 148),
            TooltipText = $"{i.Name}\n{KindName(i.Kind)} - {RulesText(i.Rules)}\n{SizeText(i)}\nClick it, then click the table." };
        b.SetMeta("lit", i.Lit());
        var tile = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tile.AddThemeConstantOverride("separation", 1);
        tile.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        tile.OffsetLeft = 4; tile.OffsetRight = -4; tile.OffsetTop = 4; tile.OffsetBottom = -3;
        tile.AddChild(Picture(i, 90));
        var label = HudKit.Text(i.Name, 12, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MaxLinesVisible = 2;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.MouseFilter = MouseFilterEnum.Ignore;
        tile.AddChild(label);
        var sub = HudKit.Text(i.Key.StartsWith("kind:") ? RulesText(i.Rules) : KindName(i.Kind), 11, i.Tint.Lightened(0.45f), HudKit.Semi, HorizontalAlignment.Center);
        sub.ClipText = true;
        sub.MouseFilter = MouseFilterEnum.Ignore;
        tile.AddChild(sub);
        b.AddChild(tile);
        b.Pressed += i.Pick;
        b.MouseEntered += () => { _hover = i; HoverPreview(); };
        b.MouseExited += () => { if (_hover == i) { _hover = null; HoverPreview(); } };
        _libButtons[i.Key] = b;
        return b;
    }

    /// <summary>The piece's picture, or (rules pieces, pictures not made yet) a block in its rules' colour, to scale.</summary>
    private static Control Picture(LibItem i, float height)
    {
        if (i.Picture() is { } tex)
            return new TextureRect { Texture = tex, CustomMinimumSize = new Vector2(0, height), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        var holder = new CenterContainer { CustomMinimumSize = new Vector2(0, height), MouseFilter = MouseFilterEnum.Ignore };
        float big = Mathf.Max(1f, Mathf.Max(i.W, i.D));
        float scale = (height - 14) / Mathf.Max(big, 6f);
        holder.AddChild(new ColorRect { Color = i.Tint, CustomMinimumSize = new Vector2(Mathf.Max(8, i.W * scale), Mathf.Max(6, i.D * scale)), MouseFilter = MouseFilterEnum.Ignore });
        return holder;
    }

    /// <summary>The right panel while the mouse is over a tile and nothing on the table is selected.</summary>
    private void HoverPreview()
    {
        if (_sel != null || _picked.Count > 0 || _selObjective >= 0 || _selBase >= 0 || _tool == ETool.Scenery) return;
        if (_hover == null) { RightPanel(); return; }
        Clear(_right);
        LibItem i = _hover;
        _right.AddChild(HudKit.Heading(i.Name, 13));
        _right.AddChild(Picture(i, 180));
        _right.AddChild(HudKit.Text($"{KindName(i.Kind)} - {RulesText(i.Rules)}", 14, i.Tint.Lightened(0.45f), HudKit.Semi).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        _right.AddChild(HudKit.Text(SizeText(i), 13, HudKit.Fg, HudKit.Semi));
        _right.AddChild(ForgeWidgets.Wrapped($"{i.Set}. {i.Source}.", 12, HudKit.Dim));
        if (TerrainBook.Kind(i.Kind) is { } k) _right.AddChild(ForgeWidgets.Wrapped(k.BookLine, 12, HudKit.Dim));
        _right.AddChild(ForgeWidgets.Wrapped("Click it, then click the table to place it.", 12, HudKit.Dim));
    }

    // ------------------------------------------------------------------ pictures for models and Workshop pieces

    private static string ThumbFolder => ProjectSettings.GlobalizePath("user://piece-thumbs");
    private static string ModelKey(PieceModels.Model m) => "m-" + Hash(m.Path);
    private static string PieceKey(TerrainPieces.Piece p)
    {
        long stamp = 0;
        try { stamp = File.GetLastWriteTimeUtc(TerrainPieces.PathOf(p)).Ticks; } catch (IOException) { }
        return "p-" + Hash(p.Id + "|" + stamp);
    }

    private static string Hash(string s)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    private static readonly Dictionary<string, Texture2D?> LibThumbs = new();
    private static Dictionary<string, float[]>? _sizes;
    private static int _thumbsDone;
    private static bool _thumbsRunning;

    private static Dictionary<string, float[]> Sizes()
    {
        if (_sizes != null) return _sizes;
        _sizes = new();
        try
        {
            string f = Path.Combine(ThumbFolder, "sizes.json");
            if (File.Exists(f)) _sizes = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, float[]>>(File.ReadAllText(f)) ?? new();
        }
        catch (Exception) { _sizes = new(); }
        return _sizes;
    }

    private static (float W, float D, float H) KnownSize(string key) =>
        Sizes().TryGetValue(key, out float[]? s) && s.Length == 3 ? (s[0], s[1], s[2]) : (0f, 0f, 0f);

    private static Texture2D? Thumb(string key)
    {
        if (LibThumbs.TryGetValue(key, out var t) && t != null) return t;
        string f = Path.Combine(ThumbFolder, key + ".png");
        t = File.Exists(f) && Image.LoadFromFile(f) is { } img ? ImageTexture.CreateFromImage(img) : null;
        if (t != null) LibThumbs[key] = t;
        return t;
    }

    private static readonly PortraitShot.Angle ThumbAngle = new("T", "Terrain", 30, 32, 1f, 1f);
    private PortraitShot.Studio? _thumbStudio;

    /// <summary>Makes the missing pictures for the Models and My pieces tabs in the background, a few a frame, and
    /// measures each model's size on the way (kept with the pictures).</summary>
    private async void MakeThumbs()
    {
        if (_thumbsRunning) return;
        var todo = new List<(string Key, Func<Node3D?> Make, string? ModelPath)>();
        foreach (var m in PieceModels.Catalogue)
            if (!File.Exists(Path.Combine(ThumbFolder, ModelKey(m) + ".png")) || !Sizes().ContainsKey(ModelKey(m)))
            { var model = m; todo.Add((ModelKey(m), () => PieceModels.Instance(model.Path), model.Path)); }
        foreach (var p in TerrainPieces.All)
            if (!File.Exists(Path.Combine(ThumbFolder, PieceKey(p) + ".png")))
            {
                var piece = p;
                todo.Add((PieceKey(p), () =>
                {
                    var root = new Node3D();
                    foreach (var item in piece.Items) PieceModels.Place(root, item);
                    return root.GetChildCount() > 0 ? root : null;
                }, null));
            }
        if (todo.Count == 0) return;
        _thumbsRunning = true;
        Directory.CreateDirectory(ThumbFolder);
        if (_thumbStudio == null || !IsInstanceValid(_thumbStudio)) { _thumbStudio = new PortraitShot.Studio(256); AddChild(_thumbStudio); }
        for (int frame = 0; frame < 2; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        int made = 0;
        foreach (var (key, make, path) in todo)
        {
            if (!IsInsideTree()) { _thumbsRunning = false; return; }
            if (path != null && PieceModels.Measure(path) is { } size) Sizes()[key] = new[] { size.W, size.D, size.H };
            if (make() is { } node && _thumbStudio.ShowNode(node))
            {
                _thumbStudio.Aim(ThumbAngle);
                for (int frame = 0; frame < 3; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!IsInsideTree()) { _thumbsRunning = false; return; }
                _thumbStudio.Grab(160).SavePng(Path.Combine(ThumbFolder, key + ".png"));
            }
            if (++made % 12 == 0) { SaveSizes(); _thumbsDone++; Rebuild(); }
        }
        SaveSizes();
        _thumbsRunning = false;
        _thumbsDone++;
        if (IsInsideTree()) Rebuild();
    }

    private static void SaveSizes()
    {
        try { File.WriteAllText(Path.Combine(ThumbFolder, "sizes.json"), Newtonsoft.Json.JsonConvert.SerializeObject(Sizes())); }
        catch (Exception) { }
    }
}
