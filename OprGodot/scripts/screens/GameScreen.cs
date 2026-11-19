using FDG;
using FdgRaylib;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// The game screen behind each half of the main menu. Since 2026-11-16 ("Menu Art &amp; Themes Mockup" round 2, Kai): one
/// big tile for army vs army (named after the system) and the other two games stacked small beside it, each with Kai's
/// picture, over the family's backdrop; clicking a game pops its modes up in a box. Drawn in the family's look. Was
/// (2026-11-07): the three games side by side with every mode as a button. Mode buttons go straight to the lobby.
/// </summary>
public partial class GameScreen : Control, ILooked, IBackGuard, IOwnBackdrop
{
    public enum EFamily { GrimdarkFuture, AgeOfFantasy }

    public enum EMode { ArmyVsArmy, Horde, Campaign, Mission, BuildHeroes }

    public EFamily Family { get; set; }

    public static string FamilyName(EFamily family) =>
        family == EFamily.GrimdarkFuture ? "Grimdark Future" : "Age of Fantasy";

    public static string FamilyBlurb(EFamily family) => family == EFamily.GrimdarkFuture
        ? "Grimdark Future - GF: Firefight - GF: Star Quest"
        : "Age of Fantasy - AoF: Skirmish - AoF: Quest";

    public static IReadOnlyList<EGameSystem> GamesOf(EFamily family) => family == EFamily.GrimdarkFuture
        ? new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest }
        : new[] { EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest };

    public static IReadOnlyList<EMode> ModesOf(EGameSystem system) => GameSystemInfo.IsQuest(system)
        ? new[] { EMode.Mission, EMode.Horde, EMode.Campaign, EMode.BuildHeroes }
        : new[] { EMode.ArmyVsArmy, EMode.Horde, EMode.Campaign };

    public static string ModeLabel(EMode mode) => mode switch
    {
        EMode.ArmyVsArmy => "Army vs Army",
        EMode.Horde => "Horde Mode",
        EMode.Campaign => "Campaign",
        EMode.Mission => "Play a Mission",
        _ => "Build Heroes",
    };

    public static string GameBlurb(EGameSystem system) => system switch
    {
        EGameSystem.GrimdarkFuture => "Full-scale sci-fi battles.",
        EGameSystem.AgeOfFantasy => "Full-scale fantasy battles.",
        EGameSystem.GFFirefight => "Small-squad sci-fi skirmish.",
        EGameSystem.AoFSkirmish => "Small-warband fantasy skirmish.",
        _ => "A party of heroes against the enemy's forces.",
    };

    public static string ModeBlurb(EGameSystem system, EMode mode)
    {
        bool quest = GameSystemInfo.IsQuest(system);
        bool skirmish = system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;
        return mode switch
        {
            EMode.ArmyVsArmy => skirmish
                ? "Two warbands, one table. The usual game. Goes straight to the lobby."
                : "Two armies, one table. The usual game. Goes straight to the lobby.",
            EMode.Horde => quest
                ? "Hold out against waves of enemies until the party falls. Goes straight to the lobby."
                : "Hold out against waves of enemies that get bigger every time. Goes straight to the lobby.",
            EMode.Campaign => quest
                ? "Chapters of missions with the same party, carried between them."
                : "A run of missions. Your force levels up, earns points and remembers losing.",
            EMode.Mission => "One mission from the Campaign Builder for your party. Goes straight to the lobby.",
            _ => "Make, level and equip the heroes your party is made of.",
        };
    }

    public override void _Ready()
    {
        // Kai's backdrop for this game family, dimmed behind the tiles (Menu Art & Themes, round 2); none set = the 3D
        // table under a veil, as before.
        var slot = FdgRaylib.Pictures.MenuPictures.Backdrop(Family == EFamily.AgeOfFantasy
            ? FdgRaylib.Pictures.MenuPictures.EFamily.AgeOfFantasy : FdgRaylib.Pictures.MenuPictures.EFamily.GrimdarkFuture);
        Control under;
        if (MenuArt.Texture(slot) is { } tex)
        {
            var e = FdgRaylib.Pictures.MenuPictures.Get(slot)!;
            under = new PictureView { Picture = tex, FocusX = e.FocusX, FocusY = e.FocusY, Zoom = e.Zoom, Dim = 0.55f };
        }
        else under = new ColorRect { Color = Palette.Ink with { A = 0.82f } };
        under.MouseFilter = MouseFilterEnum.Ignore;
        under.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(under);
        MoveChild(under, 0);

        GetNode<Button>("%Back").Pressed += App.I.Back;
        var title = GetNode<Label>("%Title");
        title.Text = FamilyName(Family);
        Ui.ScreenTitle(title);

        // One big tile (army vs army, named after the system) and the other two stacked small beside it - the layout of
        // Total War: Warhammer 3's campaign picker that Kai sent.
        var games = GetNode<HBoxContainer>("%Games");
        games.AddThemeConstantOverride("separation", 24);
        var systems = GamesOf(Family);
        var small = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3f };
        small.AddThemeConstantOverride("separation", 24);
        small.AddChild(GameTile(systems[1], false));
        small.AddChild(GameTile(systems[2], false));
        games.AddChild(small);
        var big = GameTile(systems[0], true);
        big.SizeFlagsStretchRatio = 7f;
        games.AddChild(big);
        UiJuice.Attach(this);
    }

    public Looks.ELook Look => Looks.For(Family);

    /// <summary>A game: its picture with a name bar along the bottom (name, what it is, how many books / lists).</summary>
    private Button GameTile(EGameSystem system, bool big)
    {
        var b = new Button
        {
            Name = "Game_" + system, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true,
            TooltipText = GameBlurb(system) + " Click to pick a mode.",
        };
        var box = new ChamferBox { Fill = HudKit.Glass, Border = HudKit.Edge, BorderWidth = 2, Cut = 12 };
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box.With(x => x.Border = HudKit.EdgeHot));
        b.AddThemeStyleboxOverride("pressed", box.With(x => x.Border = HudKit.Lime));
        b.AddThemeStyleboxOverride("focus", box.With(x => x.Border = HudKit.Lime));

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        // Inset so the theme's frame line shows all the way round the picture.
        col.OffsetLeft = 6; col.OffsetTop = 6; col.OffsetRight = -6; col.OffsetBottom = -6;
        col.AddThemeConstantOverride("separation", 0);
        Control art = MenuArt.View(FdgRaylib.Pictures.MenuPictures.GameTile(system));
        art.SizeFlagsVertical = SizeFlags.ExpandFill;
        art.MouseFilter = MouseFilterEnum.Ignore;
        if (MenuArt.Texture(FdgRaylib.Pictures.MenuPictures.GameTile(system)) == null)
        {
            // No picture yet: the game's icon in the middle of its colour, so the tile still says what it is.
            var holder = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            art.SetAnchorsPreset(LayoutPreset.FullRect);
            holder.AddChild(art);
            var ic = HudKit.Icon(GameIcon(system), big ? 180 : 110, HudKit.Lime with { A = 0.35f });
            ic.SetAnchorsPreset(LayoutPreset.Center);
            ic.GrowHorizontal = GrowDirection.Both; ic.GrowVertical = GrowDirection.Both;
            holder.AddChild(ic);
            art = holder;
        }
        col.AddChild(art);

        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = HudKit.Glass with { A = 0.92f }, BorderColor = HudKit.Edge, BorderWidthTop = 1, ContentMarginTop = big ? 14 : 10, ContentMarginBottom = big ? 16 : 10, ContentMarginLeft = 16, ContentMarginRight = 16 });
        var words = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", 0);
        words.AddChild(HudKit.Text(GameSystemInfo.DisplayName(system), big ? 40 : 28, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center));
        words.AddChild(HudKit.Text(GameBlurb(system) + "  -  " + Library(system), 16, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
        bar.AddChild(words);
        col.AddChild(bar);
        b.AddChild(col);
        b.Pressed += () => OpenModes(system);
        return b;
    }

    private static string GameIcon(EGameSystem system) => system switch
    {
        EGameSystem.GrimdarkFuture => "fire-ray",
        EGameSystem.AgeOfFantasy => "sword-clash",
        EGameSystem.GFFirefight or EGameSystem.AoFSkirmish => "crossed-swords",
        _ => "person",
    };

    // ================================================================== the modes box

    private Control? _modes;

    /// <summary>Clicking a game pops its modes up in a box over the screen (Kai: Keep). Esc or a click outside closes it.</summary>
    public void OpenModes(EGameSystem system)
    {
        CloseModes();
        var veil = new PanelContainer { Name = "Modes", MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f) });
        veil.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) CloseModes(); };
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        veil.AddChild(center);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        col.AddThemeConstantOverride("separation", 10);
        var head = new HBoxContainer();
        head.AddChild(HudKit.Text(GameSystemInfo.DisplayName(system).ToUpperInvariant(), 36, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2)));
        head.AddChild(Ui.Grow());
        var close = Ui.Button("Close", CloseModes, "Back to the games (Esc).", "BackButton");
        close.Name = "ModesClose";
        head.AddChild(close);
        col.AddChild(head);
        col.AddChild(Ui.Label(GameBlurb(system) + "  " + Library(system), "DimLabel", wrap: true));
        col.AddChild(HudKit.Gap(0, 4));
        foreach (EMode mode in ModesOf(system)) col.AddChild(ModeButton(system, mode));
        var panel = HudKit.Panel(HudKit.PanelBox(14).With(x => { x.Border = HudKit.Lime; x.ContentMarginLeft = x.ContentMarginRight = 26; x.ContentMarginTop = x.ContentMarginBottom = 22; }), col);
        panel.MouseFilter = MouseFilterEnum.Stop;
        center.AddChild(panel);
        AddChild(veil);
        _modes = veil;
        if (UiJuice.KeyboardMode) col.GetChild<Control>(3).CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CloseModes()
    {
        if (_modes != null && IsInstanceValid(_modes)) _modes.QueueFree();
        _modes = null;
    }

    public bool AllowBack()
    {
        if (_modes == null || !IsInstanceValid(_modes)) return true;
        CloseModes();
        return false;
    }

    private static string ModeIcon(EMode mode) => mode switch
    {
        EMode.ArmyVsArmy => "crossed-swords",
        EMode.Horde => "skull-crack",
        EMode.Campaign => "flying-flag",
        EMode.Mission => "flag-objective",
        _ => "person",
    };

    /// <summary>A mode: icon, name, and the one-line "what is this" under it, so the choice reads without hovering.</summary>
    private static Button ModeButton(EGameSystem system, EMode mode)
    {
        var b = new Button
        {
            Name = $"{system}_{mode}", ThemeTypeVariation = "ModeButton", TooltipText = ModeBlurb(system, mode),
            CustomMinimumSize = new Vector2(0, 92),
        };
        b.Pressed += () => OpenMode(system, mode);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 18; row.OffsetRight = -14;
        row.AddThemeConstantOverride("separation", 16);
        var ic = HudKit.Icon(ModeIcon(mode), 44, HudKit.Teal);
        ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(ic);
        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 0);
        v.AddChild(HudKit.Text(ModeLabel(mode).ToUpperInvariant(), 22, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        var blurb = HudKit.Text(ModeBlurb(system, mode).Replace(" Goes straight to the lobby.", ""), 15, HudKit.Dim, HudKit.Semi);
        blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        blurb.CustomMinimumSize = new Vector2(10, 0);
        v.AddChild(blurb);
        row.AddChild(v);
        var arrow = HudKit.Text(">", 30, HudKit.Lime, HudKit.Bold);
        arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(arrow);
        b.AddChild(row);
        return b;
    }

    /// <summary>
    /// "48 army books - 3 saved lists": read through the shared app library, so this line is also the
    /// proof the Godot build sees the same books and armies folder as the Raylib build.
    /// </summary>
    private static string Library(EGameSystem system)
    {
        string booksFolder = Path.Combine(AppAssets.AssetsFolder, "Books", BookFolder(system));
        int books = Directory.Exists(booksFolder) ? Directory.GetFiles(booksFolder, "*.fdgbook").Length : 0;

        int lists = 0;
        if (ArmyPaths.FolderPath is { } armies)
        {
            // Counted the way the lobby finds them (ArmyCatalog scans sub-folders too): lists are filed in a folder per
            // army, so counting only the top of the folder said "4 saved lists" where the lobby offers 26.
            string folder = Path.Combine(armies, ArmyPaths.FolderNameFor(system));
            if (Directory.Exists(folder)) lists = Directory.EnumerateFiles(folder, "*.fdgarmy", SearchOption.AllDirectories).Count();
        }

        string listWord = GameSystemInfo.IsQuest(system) ? "enemy forces" : "saved lists";
        return $"{books} army books - {lists} {listWord}";
    }

    private static string BookFolder(EGameSystem system) => system switch
    {
        EGameSystem.AgeOfFantasy => "Age of Fantasy",
        EGameSystem.AoFSkirmish => "AoF Skirmish",
        EGameSystem.AoFQuest => "AoF Quest",
        EGameSystem.GFFirefight => "GF Firefight",
        EGameSystem.GFStarQuest => "GF Star Quest",
        _ => "Grimdark Future",
    };

    private static void OpenMode(EGameSystem system, EMode mode)
    {
        string game = GameSystemInfo.DisplayName(system);
        switch (mode)
        {
            case EMode.Campaign when GameSystemInfo.IsQuest(system):
                App.I.Open<Quest.QuestCampaignsScreen>(Scenes.QuestCampaigns, s => s.System = system);
                break;
            case EMode.Campaign:
                App.I.Open<Campaign.ArmyCampaignsScreen>(Scenes.ArmyCampaigns, s => s.System = system);
                break;
            case EMode.BuildHeroes:
                App.I.Open<Quest.HeroBuilderScreen>(Scenes.HeroBuilder, s => s.System = system);
                break;
            default:
                OpenLobby(system, mode switch
                {
                    EMode.Horde => GameSystemInfo.IsQuest(system) ? EGameMode.StarQuestHorde : EGameMode.Horde,
                    EMode.Mission => EGameMode.StarQuestMission,
                    _ => EGameMode.Standard,
                });
                break;
        }
    }

    /// <summary>A mode that plays a game hosts straight away and opens the lobby (no Host screen - decision 5).
    /// Only a taken port stops it, and that says so in plain words.</summary>
    private static void OpenLobby(EGameSystem system, EGameMode mode)
    {
        if (FdgRaylib.Lobby.LobbySession.TryHost(system, mode, out var session, out string error) && session != null)
            App.I.Open<LobbyScreen>(Scenes.Lobby, s => s.Session = session);
        else
            App.I.OpenNotYet("Could not start the game", error);
    }
}
