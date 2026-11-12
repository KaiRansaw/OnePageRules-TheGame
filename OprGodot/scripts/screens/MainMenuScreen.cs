using FdgRaylib;
using FdgRaylib.Update;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// S01 Main Menu (Kai, 2026-11-07, "Main Menu Mockup" Option A) in the tactical look (2026-11-10): the title
/// and the two game tiles - Grimdark Future and Age of Fantasy - down the left over the slowly turning 3D
/// table, the tools as icon buttons along the bottom.
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
        // Darken the left of the picture so the menu reads, and the bottom for the tools.
        AddChild(Shade(new Color(0.01f, 0.015f, 0.02f, 0.97f), new Color(0.01f, 0.015f, 0.02f, 0f), true, 0, 0, 0.72f, 1));
        AddChild(Shade(new Color(0.01f, 0.015f, 0.02f, 0f), new Color(0.01f, 0.015f, 0.02f, 0.9f), false, 0, 0.62f, 1, 1));

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        col.AddChild(HudKit.Text("ONEPAGERULES", 26, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 12)));
        col.AddChild(HudKit.Text("THE GAME", 104, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(HudKit.Gap(0, 6));
        var rule = new HBoxContainer();
        rule.AddThemeConstantOverride("separation", 6);
        rule.AddChild(new ColorRect { Color = HudKit.Lime, CustomMinimumSize = new Vector2(60, 3) });
        rule.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(420, 1), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        col.AddChild(rule);
        col.AddChild(HudKit.Gap(0, 34));
        col.AddChild(HudKit.Heading("Choose a game", 18));
        col.AddChild(HudKit.Gap(0, 12));
        col.AddChild(GameTile(GameScreen.EFamily.GrimdarkFuture, "fire-ray"));
        col.AddChild(HudKit.Gap(0, 14));
        col.AddChild(GameTile(GameScreen.EFamily.AgeOfFantasy, "sword-clash"));
        HudKit.Place(this, col, LayoutPreset.CenterLeft, new Vector2(110, -40));

        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 10);
        tools.AddChild(Tool("LoadGame", "open-book", "Load Game", "Resume a game you saved.", () => App.I.Open<LoadGameScreen>(Scenes.LoadGame)));
        tools.AddChild(Tool("JoinGame", "radar-sweep", "Join a Game", "Join a friend's game over Tailscale.", OpenJoin));
        // The full builder is port step 5; until then the button imports a list from the Army Forge website.
        tools.AddChild(Tool("ArmyForge", "rally-the-troops", "Army Forge", "Import an army list from the Army Forge website (paste its share link). The full builder comes later.",
            () => ImportArmyPopup.Open(this)));
        tools.AddChild(Tool("MapMaker", "position-marker", "Map Maker", "Make and edit your own maps.", () => App.I.OpenNotYet("Map Maker",
            "Maps grouped by table size, preview, Edit / Copy / Delete; the 3D editor with heights comes with the terrain work. Port step 6.")));
        tools.AddChild(Tool("AssetLab", "stack", "Asset Lab", "Look at 3D models and pictures on the real table, at true scale, and set how each one stands.",
            () => App.I.Open<Lab.AssetLabScreen>(Scenes.AssetLab)));
        _tools = tools;
        _update = UpdateButton(false);
        tools.AddChild(_update);
        tools.AddChild(Tool("Settings", "cog", "Settings", "Your name, display, gameplay, camera and sound settings.", () => App.I.Open<SettingsScreen>(Scenes.Settings)));
        tools.AddChild(Tool("CloseGame", "stop-sign", "Quit", "Close the game.", App.I.Quit, Palette.WoundsRed));
        HudKit.Place(this, tools, LayoutPreset.BottomLeft, new Vector2(110, -64));

        var foot = new VBoxContainer();
        foot.AddThemeConstantOverride("separation", 0);
        var build = HudKit.Text($"Godot preview  -  {AppVersion.BuildLabel}", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Right);
        build.Name = "Build";
        foot.AddChild(build);
        _updateLine = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Right);
        _updateLine.Name = "UpdateLine";
        foot.AddChild(_updateLine);
        foot.AddChild(HudKit.Text("Fan-made. Not an official OnePageRules product. Game rules and army data (c) OnePageRules.", 13, HudKit.Dim with { A = 0.7f }, HudKit.Medium, HorizontalAlignment.Right));
        HudKit.Place(this, foot, LayoutPreset.BottomRight, new Vector2(-28, -16));

        // Checks quietly on opening (Kai, 2026-11-12); the button lights up when there is something new.
        if (!ScriptedRun.IsRequested || OS.GetCmdlineUserArgs().Contains("--check-update")) _ = CheckForUpdate();
    }

    // ================================================================== Update

    private static string GameDir => AppAssets.Root;

    private Button UpdateButton(bool available)
    {
        string tip = available
            ? "A new version is on GitHub. Click to update."
            : "Get the newest version from GitHub.";
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
        _shownAvailable = s.Available;
        int at = _update.GetIndex();
        var fresh = UpdateButton(s.Available);
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
        Line("Checking for updates...", HudKit.Dim);
        var s = await Check();
        if (IsInsideTree()) SetUpdateStatus(s);
    }

    private async Task OnUpdatePressed()
    {
        if (_updating) return;
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
            DialogText = s.Summary + "\n\nIt downloads the new build, then the game closes, updates itself and opens again.\n" +
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
            string exe = Path.GetFileName(OS.GetExecutablePath());
            string bat = GameUpdater.WriteApplyScript(source, GameDir, staging, OS.GetProcessId(), exe);
            GameUpdater.RunApplyScript(bat);
            Line("Closing to finish the update - the game reopens by itself.", HudKit.Lime);
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            App.I.Quit();
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

    /// <summary>A big game tile: icon, family name, its three games underneath.</summary>
    private static Button GameTile(GameScreen.EFamily family, string icon)
    {
        var b = new Button
        {
            Name = family.ToString(), ThemeTypeVariation = "TileButton", CustomMinimumSize = new Vector2(640, 118),
            TooltipText = family == GameScreen.EFamily.GrimdarkFuture
                ? "Sci-fi: Grimdark Future, GF: Firefight and GF: Star Quest."
                : "Fantasy: Age of Fantasy, AoF: Skirmish and AoF: Quest.",
        };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 26; row.OffsetRight = -22;
        row.AddThemeConstantOverride("separation", 22);
        var ic = HudKit.Icon(icon, 64, HudKit.Teal);
        ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(ic);
        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", -4);
        v.AddChild(HudKit.Text(GameScreen.FamilyName(family).ToUpperInvariant(), 44, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 3)));
        v.AddChild(HudKit.Text(GameScreen.FamilyBlurb(family), 19, HudKit.Dim, HudKit.Semi));
        row.AddChild(v);
        var arrow = HudKit.Text(">", 40, HudKit.Lime, HudKit.Bold);
        arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(arrow);
        b.AddChild(row);
        b.Pressed += () => App.I.Open<GameScreen>(Scenes.GameScreen, s => s.Family = family);
        return b;
    }

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
