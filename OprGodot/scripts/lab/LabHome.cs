using FdgRaylib.Pictures;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// The Asset Lab's home page (Kai, 2026-11-16: "there's a LOT going on there and it's confusing to navigate";
/// "Menu Art &amp; Themes Mockup" round 3, all Keep). The Lab opens here: one card per job - 3D models, unit pictures,
/// your pictures, terrain, storage - each saying where you're up to, and a list on the left to jump between them.
/// Inside a section only its buttons show, each with a one-line explanation, the main one highlighted. Every button
/// opens what the Lab already had (the same windows); "Browse models" / "Browse terrain" close this page onto the 3D
/// table viewer underneath, and the viewer's "Lab home" button (or Esc) comes back.
/// </summary>
public partial class LabHome : PanelContainer
{
    private readonly AssetLabScreen _lab;
    private VBoxContainer _nav = null!;
    private Control _main = null!;
    private Label _crumb = null!;
    private string _section = "home";
    private LabStats.Snapshot? _stats;
    private bool _measuring;

    public LabHome(AssetLabScreen lab) { _lab = lab; }

    private sealed record Tool(string Name, string Label, string Says, Action Run, bool Main = false);
    private sealed record Section(string Key, string Title, string Blurb, string Icon);

    private static readonly Section[] Sections =
    {
        new("models", "3D models", "Download the Warhammer models for each army, match them to OPR units, and check they face the right way.", "stack"),
        new("pictures", "Unit pictures", "OPR's own pictures for every unit, from Army Forge, and pictures made from the models. Shown on the unit cards and army lists.", "binoculars"),
        new("mine", "Your pictures", "Your backdrops, game tiles and army pictures for the menus - the picture manager.", "flying-flag"),
        new("terrain", "Terrain", "The game's own terrain pieces (Terrain Workshop), Warhammer terrain pieces and table mats, and what each piece is on a map.", "position-marker"),
        new("storage", "Storage", "How much space each kind of download takes, and clearing what's no longer needed.", "gears"),
    };

    private Tool[] ToolsOf(string key) => key switch
    {
        "models" => new Tool[]
        {
            new("DownloadModels", "Download models...", "Pick an army (or all of them): every version, or 1 of each to save space. Age of Fantasy's collection is fetched from the same window (Get Age of Fantasy models).", () => _lab.OpenDownloads(), true),
            new("MatchModels", "Match models...", "Which Warhammer model stands in for each OPR unit. Fix wrong picks and give unused models to a unit.", () => App.I.Open<MatchModelsScreen>(Scenes.MatchModels)),
            new("ReviewFacing", "Review facing...", "Go through the models one by one and turn any that face the wrong way.", () => App.I.Open<FacingReviewScreen>(Scenes.FacingReview)),
            new("Home_BrowseModels", "Browse models", "Every downloaded model on the real table at true size, with a search box - set how one stands, see a unit or an army of it.", () => ShowViewer("")),
            new("ImportStl", "Import printable model...", "Bring in a 3D-printing model (.stl) and its base, made light for the table and saved to a unit.", () => _lab.OpenStlImport()),
            new("Home_SendSettings", "Send my settings to GitHub", "Sends how every model stands (facing, tilt, height, base), your picture framing and your model / terrain matches and your audio folder to GitHub, so every download - your laptop, your friends - gets them. Only from the PC that runs the game from the repo.", SendSettings),
            new("Home_OpenFolder", "Open the models folder", "The UnitModels folder in Windows, to add or rename files yourself.", () => OS.ShellOpen(FdgRaylib.Models3D.UnitModelPaths.Root)),
        },
        "pictures" => new Tool[]
        {
            new("OfficialPictures", "Download official pictures...", "OPR's own picture of every unit, from Army Forge - one game or all six, one army or all. Also where model-made portraits are deleted.", () => _lab.OpenOfficialPictures(), true),
            new("FramePictures", "Frame pictures...", "Move and zoom each unit's picture so the part you want shows on the army cards, army list and unit card.", () => _lab.OpenFraming()),
            new("MakePortraits", "Portraits from models...", "Photograph downloaded 3D models as unit portraits, a whole army at once.", () => _lab.OpenPortraits()),
        },
        "mine" => new Tool[]
        {
            new("Home_PictureManager", "Open the picture manager", "Choose a picture for each place - main menu, game tiles, every army - set the part that matters, and see how it crops everywhere.", OpenPictureManager, true),
        },
        "terrain" => new Tool[]
        {
            new("Home_TerrainWorkshop", "Terrain Workshop", "Build the game's own terrain pieces: pick a kind (forest, ruins, wall...), stand stylised or realistic models on its footprint, save. Maps and battles use them.", () => App.I.Open<TerrainWorkshopScreen>(Scenes.TerrainWorkshop), true),
            new("Home_GetTerrain", "Download terrain...", "Warhammer terrain pieces and table mats: the Download models window's \"Get Warhammer terrain\".", () => _lab.OpenDownloads()),
            new("Home_MatchTerrain", "Match terrain models", "Say what each downloaded terrain piece is (Ruins, Barricade, Forest...) so it can go on maps. The same screen as in the Map Maker.", () => App.I.Open<Maps.MatchTerrainScreen>(Scenes.MatchTerrain)),
            new("Home_BrowseTerrain", "Browse terrain", "Every downloaded terrain piece on the real table.", () => ShowViewer(FdgRaylib.Terrain.WarhammerTerrain.ModelsFolderName)),
        },
        "storage" => new Tool[]
        {
            new("Home_ClearCache", "Clear download cache...", "Deletes the raw downloaded parts. Your finished models stay; downloading a model again fetches its parts again.", ConfirmClearCache, true),
            new("Home_OpenFolderStorage", "Open the models folder", "The UnitModels folder in Windows.", () => OS.ShellOpen(FdgRaylib.Models3D.UnitModelPaths.Root)),
        },
        _ => Array.Empty<Tool>(),
    };

    private bool _sending;

    private void SendSettings()
    {
        if (_sending) return;
        _sending = true;
        Toast("Sending your settings to GitHub...");
        System.Threading.Tasks.Task.Run(FdgRaylib.Models3D.SharedSettingsSync.Publish).ContinueWith(t =>
            Callable.From(() => { _sending = false; Toast(t.IsFaulted ? "Couldn't send: " + t.Exception?.GetBaseException().Message : t.Result); }).CallDeferred());
    }

    private void Toast(string text)
    {
        var dlg = new AcceptDialog { Title = "Settings", DialogText = text };
        dlg.Confirmed += () => dlg.QueueFree();
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    public override void _Ready()
    {
        Name = "LabHome";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink with { A = 0.985f } });
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 28);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 22);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        margin.AddChild(col);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 18);
        var back = Ui.Button("Back", App.I.Back, "Back to the main menu (Esc).", "BackButton");
        back.Name = "LabBack";
        back.CustomMinimumSize = new Vector2(130, 48);
        top.AddChild(back);
        var title = new Label { Text = "Asset Lab" };
        Ui.ScreenTitle(title);
        top.AddChild(title);
        _crumb = HudKit.Text("Home", 20, HudKit.Lime, HudKit.Semi);
        _crumb.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(_crumb);
        col.AddChild(top);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        col.AddChild(body);
        _nav = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        _nav.AddThemeConstantOverride("separation", 6);
        body.AddChild(new PanelContainer().With(p => p.AddChild(_nav)));
        var mainPad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mainPad.AddThemeConstantOverride("margin_left", 28);
        body.AddChild(mainPad);
        _main = new VBoxContainer();
        mainPad.AddChild(_main);
        Show("home");
        Measure();
        VisibilityChanged += () => { if (Visible) { Measure(); Show(_section); } };
    }

    /// <summary>Counts in the background; the cards fill in when it's done.</summary>
    private async void Measure()
    {
        if (_measuring) return;
        _measuring = true;
        try { _stats = await Task.Run(LabStats.Measure); }
        catch (Exception) { _stats = null; }
        _measuring = false;
        if (IsInsideTree() && Visible) Show(_section);
    }

    private void Show(string key)
    {
        _section = key;
        foreach (Node n in _nav.GetChildren()) { _nav.RemoveChild(n); n.QueueFree(); }
        _nav.AddChild(NavItem("home", "Home"));
        foreach (Section s in Sections) _nav.AddChild(NavItem(s.Key, s.Title));
        foreach (Node n in _main.GetChildren()) { _main.RemoveChild(n); n.QueueFree(); }
        Section? sec = Sections.FirstOrDefault(s => s.Key == key);
        _crumb.Text = sec?.Title ?? "Home";
        if (sec == null) _main.AddChild(Cards());
        else _main.AddChild(SectionPage(sec));
    }

    private Button NavItem(string key, string label)
    {
        bool on = key == _section;
        var b = new Button { Name = "Nav_" + key, Text = label.ToUpperInvariant(), Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 50), FocusMode = FocusModeEnum.All };
        HudKit.RowStyle(b, on);
        b.TooltipText = Sections.FirstOrDefault(s => s.Key == key)?.Blurb ?? "The Asset Lab's home page: every section at a glance.";
        b.AddThemeFontOverride("font", HudKit.Tracked(HudKit.Bold, 2));
        b.AddThemeFontSizeOverride("font_size", 20);
        b.AddThemeColorOverride("font_color", on ? HudKit.Fg : HudKit.Dim);
        b.AddThemeColorOverride("font_hover_color", HudKit.Fg);
        b.Pressed += () => Show(key);
        return b;
    }

    // ------------------------------------------------------------------ home cards

    private Control Cards()
    {
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 18);
        foreach (Section s in Sections)
        {
            (string status, float progress) = Status(s.Key);
            var b = new Button { Name = "Card_" + s.Key, CustomMinimumSize = new Vector2(0, 230), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = s.Blurb };
            var box = new ChamferBox { Fill = HudKit.Glass, Border = HudKit.Edge, Cut = 12 };
            b.AddThemeStyleboxOverride("normal", box);
            b.AddThemeStyleboxOverride("hover", box.With(x => { x.Border = HudKit.EdgeHot; x.Fill = x.Fill.Lightened(0.05f); }));
            b.AddThemeStyleboxOverride("pressed", box.With(x => x.Border = HudKit.Lime));
            b.AddThemeStyleboxOverride("focus", box.With(x => x.Border = HudKit.Lime));
            var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = 22; v.OffsetTop = 18; v.OffsetRight = -22; v.OffsetBottom = -20;
            v.AddThemeConstantOverride("separation", 6);
            var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            head.AddThemeConstantOverride("separation", 12);
            head.AddChild(HudKit.Icon(IconOr(s.Icon), 36, HudKit.Teal));
            head.AddChild(HudKit.Text(s.Title.ToUpperInvariant(), 26, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
            v.AddChild(head);
            v.AddChild(HudKit.Text(status, 19, HudKit.Lime, HudKit.Bold));
            var blurb = HudKit.Text(s.Blurb, 16, HudKit.Dim, HudKit.Semi);
            blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            blurb.CustomMinimumSize = new Vector2(10, 0);
            blurb.SizeFlagsVertical = SizeFlags.ExpandFill;
            v.AddChild(blurb);
            var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore };
            bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.5f), BorderColor = HudKit.Edge, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = HudKit.Lime });
            v.AddChild(bar);
            b.AddChild(v);
            string key = s.Key;
            b.Pressed += () => Show(key);
            grid.AddChild(b);
        }
        return grid;
    }

    private static string IconOr(string icon) => ResourceLoader.Exists($"res://icons/{icon}.svg") ? icon : "stack";

    /// <summary>A card's status line and how full its bar is.</summary>
    private (string, float) Status(string key)
    {
        if (key == "mine")
        {
            var slots = AllPictureSlots();
            int set = MenuPictures.CountSet(slots);
            return ($"{set} of {slots.Count} set", slots.Count == 0 ? 0 : set / (float)slots.Count);
        }
        if (_stats is not { } st) return ("Counting...", 0);
        return key switch
        {
            "models" => (st.Models == 0 ? "No models downloaded yet" : $"{st.Models:N0} models - {st.ModelArmies} armies", st.Models > 0 ? 1 : 0),
            "pictures" => (st.OfficialPictures == 0 ? "No unit pictures yet" : $"{st.OfficialPictures:N0} unit pictures - {st.PictureArmies} armies", st.OfficialPictures > 0 ? 1 : 0),
            "terrain" => (st.TerrainPieces == 0 ? "No terrain downloaded yet" : $"{st.TerrainPieces:N0} pieces", st.TerrainPieces > 0 ? 1 : 0),
            "storage" => ($"{LabStats.Bytes(st.ModelBytes + st.CacheBytes + st.TerrainBytes + st.PictureBytes + st.MenuPictureBytes)} used",
                st.CacheBytes > 0 ? Math.Min(1f, st.CacheBytes / (float)Math.Max(1, st.ModelBytes + st.CacheBytes + st.TerrainBytes)) : 0),
            _ => ("", 0),
        };
    }

    // ------------------------------------------------------------------ a section

    private Control SectionPage(Section sec)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        v.AddChild(HudKit.Text(sec.Title.ToUpperInvariant(), 34, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        var blurb = HudKit.Text(sec.Blurb + "  " + Status(sec.Key).Item1 + ".", 18, HudKit.Dim, HudKit.Semi);
        blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        blurb.CustomMinimumSize = new Vector2(10, 0);
        v.AddChild(blurb);
        if (sec.Key == "storage") v.AddChild(StorageTable());
        v.AddChild(HudKit.Gap(0, 6));
        foreach (Tool t in ToolsOf(sec.Key))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 20);
            var b = Ui.Button(t.Label, t.Run, t.Says, t.Main ? "CommitButton" : null);
            b.Name = t.Name;
            b.CustomMinimumSize = new Vector2(380, 54);
            row.AddChild(b);
            var says = HudKit.Text(t.Says, 17, HudKit.Dim, HudKit.Semi);
            says.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            says.CustomMinimumSize = new Vector2(10, 0);
            says.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            says.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(says);
            v.AddChild(HudKit.Panel(HudKit.PanelBox(10).With(x => { x.ContentMarginLeft = x.ContentMarginRight = 14; x.ContentMarginTop = x.ContentMarginBottom = 10; }), row));
        }
        return v;
    }

    private Control StorageTable()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 30);
        if (_stats is not { } st) { grid.AddChild(HudKit.Text("Counting...", 18, HudKit.Dim, HudKit.Semi)); return grid; }
        foreach ((string what, long bytes) in new[]
                 {
                     ("3D models", st.ModelBytes), ("Download cache (raw parts)", st.CacheBytes), ("Terrain", st.TerrainBytes),
                     ("Unit pictures", st.PictureBytes), ("Your pictures", st.MenuPictureBytes),
                 })
        {
            grid.AddChild(HudKit.Text(what, 18, HudKit.Fg, HudKit.Semi));
            grid.AddChild(HudKit.Text(LabStats.Bytes(bytes), 18, HudKit.Lime, HudKit.Bold));
        }
        return grid;
    }

    // ------------------------------------------------------------------ actions

    private void ShowViewer(string filter)
    {
        Visible = false;
        _lab.ShowViewer(filter);
    }

    private void OpenPictureManager()
    {
        var pm = new PictureManager();
        pm.Closed += () => { if (IsInsideTree()) Show(_section); };
        _lab.AddChild(pm);
    }

    private void ConfirmClearCache()
    {
        string dir = Path.Combine(FdgRaylib.Models3D.UnitModelPaths.Root, FdgRaylib.Models3D.ModelDownloader.CacheFolderName);
        long bytes = _stats?.CacheBytes ?? 0;
        var dlg = new ConfirmationDialog
        {
            Title = "Clear the download cache?",
            DialogText = $"Delete {LabStats.Bytes(bytes)} of raw downloaded parts? Your finished models stay. Downloading a model again fetches its parts again.",
            OkButtonText = "Delete them",
        };
        dlg.Confirmed += () =>
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception e) { App.I.Message("Couldn't clear it", e.Message); }
            dlg.QueueFree();
            Measure();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    /// <summary>Every picture slot (for the "x of y set" count): 2 backdrops, 6 game tiles, every army of both families.</summary>
    public static List<MenuPictures.Slot> AllPictureSlots()
    {
        var all = new List<MenuPictures.Slot>
        {
            MenuPictures.Backdrop(MenuPictures.EFamily.GrimdarkFuture), MenuPictures.Backdrop(MenuPictures.EFamily.AgeOfFantasy),
        };
        foreach (FDG.EGameSystem g in new[] { FDG.EGameSystem.GrimdarkFuture, FDG.EGameSystem.GFFirefight, FDG.EGameSystem.GFStarQuest, FDG.EGameSystem.AgeOfFantasy, FDG.EGameSystem.AoFSkirmish, FDG.EGameSystem.AoFQuest })
            all.Add(MenuPictures.GameTile(g));
        foreach (var f in new[] { MenuPictures.EFamily.GrimdarkFuture, MenuPictures.EFamily.AgeOfFantasy })
            all.AddRange(BookIndex.ArmiesOf(f).Select(a => MenuPictures.Army(f, a)));
        return all;
    }
}
