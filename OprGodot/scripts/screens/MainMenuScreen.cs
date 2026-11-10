using FdgRaylib;
using Godot;

namespace OprGodot;

/// <summary>
/// S01 Main Menu (Kai, 2026-11-07, "Main Menu Mockup" Option A): two tiles - Grimdark Future and Age of
/// Fantasy - each opening its game screen, and the tools along the bottom.
/// </summary>
public partial class MainMenuScreen : Control
{
    public override void _Ready()
    {
        Dress(GetNode<Button>("%GrimdarkFuture"), GameScreen.EFamily.GrimdarkFuture);
        Dress(GetNode<Button>("%AgeOfFantasy"), GameScreen.EFamily.AgeOfFantasy);
        GetNode<Button>("%GrimdarkFuture").Pressed += () => OpenFamily(GameScreen.EFamily.GrimdarkFuture);
        GetNode<Button>("%AgeOfFantasy").Pressed += () => OpenFamily(GameScreen.EFamily.AgeOfFantasy);

        GetNode<Button>("%LoadGame").Pressed += () => App.I.OpenNotYet("Load Game",
            "Save tiles with a board picture, All / Grimdark Future / Age of Fantasy filters, a big preview, LOAD and Delete. Comes with the in-game table (port step 3).");
        GetNode<Button>("%JoinGame").Pressed += OpenJoin;
        GetNode<Button>("%ArmyForge").Pressed += () => App.I.OpenNotYet("Army Forge",
            "Total War-style: unit info and upgrades on the left, Your Army portrait strip with the points bar, the book's roster in category boxes. Port step 5.");
        GetNode<Button>("%MapMaker").Pressed += () => App.I.OpenNotYet("Map Maker",
            "Maps grouped by table size, preview, Edit / Copy / Delete; the 3D editor with heights comes with the terrain work. Port step 6.");
        GetNode<Button>("%Settings").Pressed += () => App.I.Open<SettingsScreen>(Scenes.Settings);
        GetNode<Button>("%CloseGame").Pressed += App.I.Quit;

        GetNode<Label>("%Build").Text = $"Godot preview - {AppVersion.BuildLabel}";
    }

    /// <summary>The tile's text: the family name large, its three games small underneath. The space above is
    /// kept for faction art, as in the mockup.</summary>
    private static void Dress(Button tile, GameScreen.EFamily family)
    {
        tile.Text = "";
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        Label name = Ui.Label(GameScreen.FamilyName(family));
        name.AddThemeFontSizeOverride("font_size", 44);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        Label games = Ui.Label(GameScreen.FamilyBlurb(family), "DimLabel");
        games.AddThemeFontSizeOverride("font_size", 20);
        games.HorizontalAlignment = HorizontalAlignment.Center;
        name.MouseFilter = games.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(name);
        col.AddChild(games);
        center.AddChild(col);
        tile.AddChild(center);
    }

    private static void OpenFamily(GameScreen.EFamily family) =>
        App.I.Open<GameScreen>(Scenes.GameScreen, s => s.Family = family);

    private void OpenJoin()
    {
        var popup = GD.Load<PackedScene>(Scenes.JoinPopup).Instantiate<JoinPopup>();
        AddChild(popup);
    }
}
