using FdgRaylib;
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
        tools.AddChild(Tool("Settings", "cog", "Settings", "Your name, display, gameplay, camera and sound settings.", () => App.I.Open<SettingsScreen>(Scenes.Settings)));
        tools.AddChild(Tool("CloseGame", "stop-sign", "Quit", "Close the game.", App.I.Quit, Palette.WoundsRed));
        HudKit.Place(this, tools, LayoutPreset.BottomLeft, new Vector2(110, -64));

        var foot = new VBoxContainer();
        foot.AddThemeConstantOverride("separation", 0);
        var build = HudKit.Text($"Godot preview  -  {AppVersion.BuildLabel}", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Right);
        build.Name = "Build";
        foot.AddChild(build);
        foot.AddChild(HudKit.Text("Fan-made. Not an official OnePageRules product. Game rules and army data (c) OnePageRules.", 13, HudKit.Dim with { A = 0.7f }, HudKit.Medium, HorizontalAlignment.Right));
        HudKit.Place(this, foot, LayoutPreset.BottomRight, new Vector2(-28, -16));
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
