using FdgRaylib;
using FdgRaylib.Update;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// S01 Main Menu. Since 2026-11-16 ("Menu Art &amp; Themes Mockup", round 3, Kai): both games side by side - Grimdark
/// Future on the left half, Age of Fantasy on the right, each with Kai's backdrop picture (Asset Lab -> Your pictures)
/// in its own look, or the turning 3D table washed in its colour while none is set. Title top centre, the tools in one
/// strip along the bottom. Was (2026-11-07 Option A): two tiles down the left over the 3D table.
/// </summary>
public partial class MainMenuScreen : Control, IOwnBackdrop
{
    private HBoxContainer _tools = null!;
    private Button _update = null!;
    private Label _updateLine = null!;
    private GameUpdater.Status? _updateStatus;
    private bool _updating, _shownAvailable;
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public override void _Ready()
    {
        // Both games side by side (Kai, 2026-11-16, "Menu Art & Themes Mockup" round 3): each half its own picture in its
        // own look - Grimdark Future left, Age of Fantasy right. Click a half for its game screen.
        var halves = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        halves.SetAnchorsPreset(LayoutPreset.FullRect);
        halves.AddThemeConstantOverride("separation", 0);
        AddChild(halves);
        _gfTile = Half(GameScreen.EFamily.GrimdarkFuture);
        _aofTile = Half(GameScreen.EFamily.AgeOfFantasy);
        halves.AddChild(_gfTile);
        halves.AddChild(_aofTile);
        _gfTile.FocusNeighborRight = _gfTile.GetPathTo(_aofTile);
        _aofTile.FocusNeighborLeft = _aofTile.GetPathTo(_gfTile);

        // The title, top centre, on a dark plate so it reads over any picture.
        var titleBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        titleBox.AddThemeConstantOverride("separation", -6);
        titleBox.AddChild(HudKit.Text("ONEPAGERULES", 22, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 12), HorizontalAlignment.Center));
        titleBox.AddChild(HudKit.Text("THE GAME", 64, HudKit.Fg, HudKit.Exo(800), HorizontalAlignment.Center));
        var plate = HudKit.Panel(new ChamferBox { Fill = new Color(0.01f, 0.015f, 0.02f, 0.78f), Border = HudKit.Edge, Cut = 14, ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 10, ContentMarginBottom = 14 }, titleBox);
        plate.MouseFilter = MouseFilterEnum.Ignore;
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(plate);
        ScreenFrame.Place(this, "Title", col, LayoutPreset.CenterTop, new Vector2(0, 28));

        // The tools in one strip along the bottom.
        var strip = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        strip.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.02f, 0.03f, 0.035f, 0.9f), BorderColor = HudKit.Edge, BorderWidthTop = 1, ContentMarginTop = 10, ContentMarginBottom = 10 });
        strip.SetAnchorsPreset(LayoutPreset.BottomWide);
        strip.GrowVertical = GrowDirection.Begin;
        AddChild(strip);
        ScreenFrame.TryPlace(this, "Tools", strip, new ScreenFrame.Anchors(0, 1, 1, 1));
        var tools = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tools.AddThemeConstantOverride("separation", 10);
        strip.AddChild(tools);
        tools.AddChild(Tool("LoadGame", "open-book", "Load Game", "Resume a game you saved.", () => App.I.Open<LoadGameScreen>(Scenes.LoadGame)));
        tools.AddChild(Tool("JoinGame", "radar-sweep", "Join a Game", "Join a friend's game over Tailscale.", OpenJoin));
        tools.AddChild(Tool("ArmyForge", "rally-the-troops", "Army Forge", "Build an army from the army books, pick each model's look, or import a list from the Army Forge website.",
            () => App.I.Open<Forge.ArmyForgeScreen>(Scenes.ArmyForge)));
        tools.AddChild(Tool("MapMaker", "position-marker", "Map Maker", "Make and edit your own maps.", () => App.I.Open<Maps.MapMakerScreen>(Scenes.MapMaker)));
        tools.AddChild(Tool("AssetLab", "stack", "Asset Lab", "3D models, unit pictures, your menu pictures and terrain - downloads and checks.",
            () => App.I.Open<Lab.AssetLabScreen>(Scenes.AssetLab)));
        _tools = tools;
        _update = UpdateButton(false);
        tools.AddChild(_update);
        tools.AddChild(Tool("Settings", "cog", "Settings", "Your name, display, gameplay, camera and sound settings.", () => App.I.Open<SettingsScreen>(Scenes.Settings)));
        tools.AddChild(Tool("Credits", "scroll-unfurled", "Credits", "Who made the rules, icons, fonts and tools this game uses.", () => App.I.Open<CreditsScreen>(Scenes.Credits)));
        tools.AddChild(Tool("CloseGame", "stop-sign", "Quit", "Close the game.", App.I.Quit, Palette.WoundsRed));

        // Build number, the update line and the auto-update switch: top right, clear of both halves' names.
        var foot = new VBoxContainer();
        foot.AddThemeConstantOverride("separation", 0);
        _offSwitch = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        foot.AddChild(_offSwitch);
        foot.AddChild(HudKit.Gap(0, 4));
        var build = HudKit.Text($"Godot preview  -  {AppVersion.BuildLabel}", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Right);
        build.Name = "Build";
        foot.AddChild(build);
        _updateLine = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Right);
        _updateLine.Name = "UpdateLine";
        foot.AddChild(_updateLine);
        foot.AddChild(HudKit.Text("Fan-made. Not an official OnePageRules product. Game rules and army data (c) OnePageRules.", 13, HudKit.Dim with { A = 0.7f }, HudKit.Medium, HorizontalAlignment.Right));
        ScreenFrame.Place(this, "BuildInfo", foot, LayoutPreset.TopRight, new Vector2(-24, 20));

        ShowOffSwitch();
        // The frame's boxes sit over the two halves, not under them.
        if (GetNodeOrNull<Control>("Frame") is { } frame) MoveChild(frame, -1);

        // The feel (2026-11-15): every button here lifts, brightens, clicks, and can be reached with the keyboard or a
        // controller - arrows / d-pad move, Enter / A presses. Down from either half goes to the tools strip.
        UiJuice.Attach(this);
        foreach (Button half in new[] { _gfTile, _aofTile }) half.FocusNeighborBottom = half.GetPathTo(tools.GetChild(0));
        foreach (Node t in tools.GetChildren()) ((Control)t).FocusNeighborTop = ((Control)t).GetPathTo(_gfTile);
        GetViewport().GuiFocusChanged += c => { if (IsInstanceValid(this) && IsAncestorOf(c)) _lastFocus = c; };
        VisibilityChanged += () =>
        {
            if (!Visible) return;
            // Pictures set in the Asset Lab show as soon as you come back.
            if (_picturesVersion != FdgRaylib.Pictures.MenuPictures.Version) RefreshHalves();
            if (UiJuice.KeyboardMode) (_lastFocus ?? _gfTile).CallDeferred(Control.MethodName.GrabFocus);
        };
        _picturesVersion = FdgRaylib.Pictures.MenuPictures.Version;
        if (!ScriptedRun.IsRequested) Arrive(col, tools, foot);
        // Checks quietly on opening (Kai, 2026-11-12); the button lights up when there is something new. Not at all when
        // auto update detection is turned off (Kai, 2026-11-14).
        if (Offline) ShowDisconnected();
        else if (!ScriptedRun.IsRequested || OS.GetCmdlineUserArgs().Contains("--check-update")) _ = CheckForUpdate();
    }

    private int _picturesVersion;

    private void RefreshHalves()
    {
        _picturesVersion = FdgRaylib.Pictures.MenuPictures.Version;
        foreach (Button half in new[] { _gfTile, _aofTile })
        {
            var family = half == _gfTile ? GameScreen.EFamily.GrimdarkFuture : GameScreen.EFamily.AgeOfFantasy;
            Node art = half.GetNode("Art");
            int at = art.GetIndex();
            half.RemoveChild(art);
            art.QueueFree();
            Control fresh = HalfArt(family);
            half.AddChild(fresh);
            half.MoveChild(fresh, at);
        }
    }

    private Button _gfTile = null!, _aofTile = null!;
    private Control? _lastFocus;

    /// <summary>The menu builds itself in on opening: title, then each tile, then the tools, a beat apart.</summary>
    private async void Arrive(VBoxContainer col, Control tools, Control foot)
    {
        // Wait for the containers to lay the pieces out, so they slide to where they really sit.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree()) return;
        UiJuice.Stagger(col.GetChildren().OfType<Control>().Append(tools).Append(foot), 0.035f);
    }

    /// <summary>The first arrow / d-pad press with nothing picked puts the cursor on the first tile.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        Control? owner = GetViewport().GuiGetFocusOwner();
        if (owner != null && IsAncestorOf(owner)) return;
        foreach (string a in new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next" })
            if (e.IsActionPressed(a))
            {
                (_lastFocus ?? _gfTile).GrabFocus();
                GetViewport().SetInputAsHandled();
                return;
            }
    }

    // ================================================================== Auto update detection switch

    private HBoxContainer _offSwitch = null!;

    private static bool Offline => FdgRaylib.Config.UserConfig.Current.AutoUpdateDetectionOff;

    /// <summary>
    /// Kai, 2026-11-14: "TURN OFF AUTO UPDATE DETECTION" - glowing green and ON means the game does not connect to
    /// GitHub at all, and says so. Saved, so it stays off between runs.
    /// </summary>
    private void ShowOffSwitch()
    {
        foreach (Node n in _offSwitch.GetChildren()) { _offSwitch.RemoveChild(n); n.QueueFree(); }
        bool on = Offline;
        var b = new Button { Name = "AutoUpdateSwitch", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(430, 44),
            TooltipText = on
                ? "ON: the game does not connect to GitHub - it won't look for updates, and Update is off. Click to turn auto update detection back on."
                : "OFF: the game checks GitHub for a newer version once, when it opens (and when you press Update). It is not connected the rest of the time. Click to stop it connecting at all." };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 10, ContentMarginLeft = 14, ContentMarginRight = 10 };
        if (on) box = box.With(x => { x.Border = HudKit.Lime; x.Glow = HudKit.Lime with { A = 0.6f }; x.Fill = new Color(0.12f, 0.19f, 0.08f, 0.96f); });
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 14; row.OffsetRight = -8;
        row.AddThemeConstantOverride("separation", 10);
        var label = HudKit.Text("TURN OFF AUTO UPDATE DETECTION", 15, on ? HudKit.Lime : HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1));
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(label);
        var state = HudKit.Chip(on ? "ON" : "OFF", on ? HudKit.Lime : HudKit.Dim, on, 15);
        state.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        state.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(state);
        b.AddChild(row);
        b.Pressed += () =>
        {
            FdgRaylib.Config.UserConfig.Current.AutoUpdateDetectionOff = !Offline;
            FdgRaylib.Config.UserConfig.Save();
            ShowOffSwitch();
            ReplaceUpdateButton(false);
            if (Offline) ShowDisconnected();
            else _ = CheckForUpdate();
        };
        _offSwitch.AddChild(b);
    }

    private void ShowDisconnected() =>
        Line("DISCONNECTED - auto update detection is off. The game does not connect to GitHub.", HudKit.Gold);

    // ================================================================== Update

    private static string GameDir => AppAssets.Root;

    private Button UpdateButton(bool available)
    {
        string tip = available
            ? "A new version is on GitHub. Click to update."
            : "Get the newest version from GitHub.";
        if (Offline)
        {
            var off = HudKit.ActionButton("Update", "clockwise-rotation", "Offline", "", HudKit.Dim, 96, disabled: true);
            off.CustomMinimumSize = new Vector2(118, 96);
            off.TooltipText = "Auto update detection is turned off, so the game is not connected to GitHub. Turn it back on (bottom right) to update.";
            return off;
        }
        var b = HudKit.ActionButton("Update", "clockwise-rotation", available ? "Update!" : "Update", "", available ? HudKit.Lime : HudKit.Teal, 96, selected: available);
        b.CustomMinimumSize = new Vector2(118, 96);
        b.TooltipText = tip;
        b.Pressed += () => _ = OnUpdatePressed();
        return b;
    }

    private void SetUpdateStatus(GameUpdater.Status s)
    {
        _updateStatus = s;
        Line(s.Summary, s.Error != null ? Palette.WoundsRed : s.Available ? HudKit.Lime : HudKit.Dim);
        if (s.Available == _shownAvailable) return;
        ReplaceUpdateButton(s.Available);
    }

    private void ReplaceUpdateButton(bool available)
    {
        _shownAvailable = available;
        int at = _update.GetIndex();
        var fresh = UpdateButton(available);
        _tools.RemoveChild(_update);
        _update.QueueFree();
        _update = fresh;
        _tools.AddChild(_update);
        _tools.MoveChild(_update, at);
    }

    private void Line(string text, Color colour)
    {
        _updateLine.Text = text;
        _updateLine.Modulate = colour;
    }

    private async Task<GameUpdater.Status> Check()
    {
        string? repo = GameUpdater.RepoRoot(GameDir);
        try
        {
            return repo != null ? await GameUpdater.CheckGitAsync(repo) : await GameUpdater.CheckReleaseAsync(Http, GameDir);
        }
        catch (Exception e) { return GameUpdater.Status.Failed("Couldn't check for updates: " + e.Message); }
    }

    private async Task CheckForUpdate()
    {
        if (Offline) { ShowDisconnected(); return; }
        Line("Checking GitHub for updates...", HudKit.Dim);
        var s = await Check();
        if (!IsInsideTree() || Offline) return; // turned off while it was checking
        SetUpdateStatus(s);
        // Kai, 2026-11-14: say plainly that it is not connected all the time.
        if (s.Error == null) Line(s.Summary + "  (checked once when the game opened - not connected the rest of the time)", s.Available ? HudKit.Lime : HudKit.Dim);
    }

    private async Task OnUpdatePressed()
    {
        if (_updating || Offline) return;
        _updating = true;
        try
        {
            string? repo = GameUpdater.RepoRoot(GameDir);
            if (repo != null)
            {
                // Kai's copy: git pull. The Godot editor rebuilds on the next Play.
                Line("Updating from GitHub...", HudKit.Lime);
                var r = await GameUpdater.PullAsync(repo);
                if (!IsInsideTree()) return;
                SetUpdateStatus(r);
                if (r.Error == null) Line(r.Summary, HudKit.Lime);
                Tell(r.Error == null ? "Updated" : "Couldn't update", r.Summary);
                return;
            }
            if (!OS.HasFeature("windows"))
            {
                OS.ShellOpen(GameUpdater.ReleasePage);
                Line("Opened the download page - unzip the new build over this one.", HudKit.Dim);
                return;
            }
            Line("Checking GitHub...", HudKit.Dim);
            var s = await Check();
            if (!IsInsideTree()) return;
            SetUpdateStatus(s);
            // A click always gets an answer you can see (Kai, 2026-11-12: "clicking Update doesnt do anything").
            if (!s.Available) { Tell(s.Error == null ? "No update" : "Couldn't check", s.Error == null ? $"You have the newest build ({AppVersion.BuildLabel})." : s.Summary); return; }
            ConfirmDownload(s);
        }
        finally { _updating = false; }
    }

    private void Tell(string title, string text)
    {
        var dlg = new AcceptDialog { Title = title, DialogText = text, OkButtonText = "OK" };
        dlg.Confirmed += () => dlg.QueueFree();
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void ConfirmDownload(GameUpdater.Status s)
    {
        var dlg = new ConfirmationDialog
        {
            Title = "Update the game?",
            DialogText = s.Summary + "\n\nIt downloads and installs the new build, then the game closes - open it again to play.\n" +
                         "Your saved armies, games, settings and downloaded models stay as they are.",
            OkButtonText = "Update",
        };
        dlg.Confirmed += () => { dlg.QueueFree(); _ = DownloadAndApply(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    /// <summary>A friend's copy: download the zip, unpack it beside the game, hand over to the script and close.</summary>
    private async Task DownloadAndApply()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var release = await GameUpdater.LatestReleaseAsync(Http);
            if (release?.WindowsZipUrl == null) { Line("The new build has no Windows download yet - try again in a few minutes.", Palette.WoundsRed); return; }
            string staging = Path.Combine(GameDir, GameUpdater.StagingFolder);
            string zip = Path.Combine(Path.GetTempPath(), GameUpdater.WindowsAsset);
            var progress = new Progress<double>(p => Line($"Downloading the update... {p * 100:0}%", HudKit.Lime));
            await GameUpdater.DownloadAsync(Http, release.WindowsZipUrl, zip, progress);
            Line("Unpacking...", HudKit.Lime);
            string source = await Task.Run(() => GameUpdater.Stage(zip, staging));
            try { File.Delete(zip); } catch (IOException) { }
            Line("Installing...", HudKit.Lime);
            int files = await Task.Run(() => GameUpdater.ApplyInPlace(source, GameDir));
            try { Directory.Delete(staging, true); } catch (Exception) { } // whatever is left goes on the next start
            Line("Updated - open the game again.", HudKit.Lime);
            // No automatic restart: Windows' Smart App Control can block a game starting itself. The player reopens it.
            var done = new AcceptDialog
            {
                Title = "Updated",
                DialogText = $"The new build is installed ({files} files). The game will close now - open it again to play the new version.",
                OkButtonText = "Close the game",
            };
            done.Confirmed += App.I.Quit;
            done.Canceled += App.I.Quit;
            AddChild(done);
            done.PopupCentered();
        }
        catch (Exception e) { if (IsInsideTree()) { Line("The update failed: " + e.Message, Palette.WoundsRed); Tell("Update failed", e.Message); } }
        finally { _updating = false; }
    }

    private static TextureRect Shade(Color from, Color to, bool horizontal, float l, float t, float r, float b)
    {
        var rect = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { from, to }, Offsets = new[] { horizontal ? 0.35f : 0f, 1f } },
                FillFrom = Vector2.Zero, FillTo = horizontal ? new Vector2(1, 0) : new Vector2(0, 1), Width = 256, Height = 256,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore, AnchorLeft = l, AnchorTop = t, AnchorRight = r, AnchorBottom = b,
        };
        return rect;
    }

    private static FdgRaylib.Pictures.MenuPictures.EFamily PicFamily(GameScreen.EFamily f) =>
        f == GameScreen.EFamily.AgeOfFantasy ? FdgRaylib.Pictures.MenuPictures.EFamily.AgeOfFantasy : FdgRaylib.Pictures.MenuPictures.EFamily.GrimdarkFuture;

    /// <summary>
    /// Kai's backdrop picture for this half. With none set, the turning 3D table shows through, washed in the half's
    /// colour so the two halves still read as two games.
    /// </summary>
    private static Control HalfArt(GameScreen.EFamily family)
    {
        var slot = FdgRaylib.Pictures.MenuPictures.Backdrop(PicFamily(family));
        Control art;
        if (MenuArt.Texture(slot) is { } tex)
        {
            var e = FdgRaylib.Pictures.MenuPictures.Get(slot)!;
            art = new PictureView { Picture = tex, FocusX = e.FocusX, FocusY = e.FocusY, Zoom = e.Zoom };
        }
        else
        {
            Color wash = Looks.Get(Looks.For(family)).Accent;
            art = new ColorRect { Color = wash with { A = 0.28f }, MouseFilter = MouseFilterEnum.Ignore };
        }
        art.Name = "Art";
        art.MouseFilter = MouseFilterEnum.Ignore;
        art.SetAnchorsPreset(LayoutPreset.FullRect);
        return art;
    }

    /// <summary>One half of the main menu: its picture, a shade at the bottom, its name and three games - in its own look.</summary>
    private static Button Half(GameScreen.EFamily family) => Looks.With(Looks.For(family), () =>
    {
        var b = new Button
        {
            Name = family.ToString(), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true,
            TooltipText = family == GameScreen.EFamily.GrimdarkFuture
                ? "Sci-fi: Grimdark Future, GF: Firefight and GF: Star Quest."
                : "Fantasy: Age of Fantasy, AoF: Skirmish and AoF: Quest.",
        };
        var clear = new StyleBoxEmpty();
        foreach (string st in new[] { "normal", "pressed", "disabled", "focus" }) b.AddThemeStyleboxOverride(st, clear);
        // Hover / focus: an outline in the half's go colour.
        var outline = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = HudKit.Lime, DrawCenter = false };
        outline.SetBorderWidthAll(4);
        b.AddThemeStyleboxOverride("hover", outline);
        b.AddThemeStyleboxOverride("focus", outline);

        b.AddChild(HalfArt(family));
        // A shade from the bottom up, in the half's own panel colour, so the name reads; a lighter veil over the rest.
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { Palette.Ink with { A = 0.15f }, Palette.Ink with { A = 0.95f } }, Offsets = new[] { 0.45f, 0.92f } },
                FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1), Width = 8, Height = 256,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        b.AddChild(shade);

        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        v.SetAnchorsPreset(LayoutPreset.FullRect);
        v.OffsetBottom = -170;
        v.AddThemeConstantOverride("separation", 2);
        var name = HudKit.Text(GameScreen.FamilyName(family).ToUpperInvariant(), 64, HudKit.Fg, HudKit.Exo(800), HorizontalAlignment.Center);
        name.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        name.AddThemeConstantOverride("shadow_offset_y", 3);
        v.AddChild(name);
        string games = family == GameScreen.EFamily.GrimdarkFuture ? "Grimdark Future  -  Firefight  -  Star Quest" : "Age of Fantasy  -  Skirmish  -  Quest";
        v.AddChild(HudKit.Text(games, 22, HudKit.Lime, HudKit.Semi, HorizontalAlignment.Center));
        b.AddChild(v);
        b.Pressed += () => App.I.Open<GameScreen>(Scenes.GameScreen, s => s.Family = family);
        return b;
    });

    private static Button Tool(string name, string icon, string label, string tip, Action onPressed, Color? tint = null)
    {
        var b = HudKit.ActionButton(name, icon, label, "", tint ?? HudKit.Teal, 96);
        b.CustomMinimumSize = new Vector2(118, 96);
        b.TooltipText = tip;
        b.Pressed += onPressed;
        return b;
    }

    private void OpenJoin()
    {
        var popup = GD.Load<PackedScene>(Scenes.JoinPopup).Instantiate<JoinPopup>();
        AddChild(popup);
    }
}
