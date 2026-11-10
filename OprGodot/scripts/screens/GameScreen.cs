using FDG;
using FdgRaylib;
using Godot;

namespace OprGodot;

/// <summary>
/// The game screen behind each main-menu tile (Kai, 2026-11-07, "Main Menu Mockup" Option A): the family's
/// three games side by side, every mode as a button under its game. Replaces the old Mode Select and Quest
/// Landing screens. Mode buttons go straight to the lobby (no Host screen - decision 5).
/// </summary>
public partial class GameScreen : Control
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
        GetNode<Button>("%Back").Pressed += App.I.Back;
        GetNode<Label>("%Title").Text = FamilyName(Family);

        var games = GetNode<HBoxContainer>("%Games");
        foreach (EGameSystem system in GamesOf(Family))
            games.AddChild(Column(system));
    }

    private static Control Column(EGameSystem system)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardPanel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        card.AddChild(col);

        col.AddChild(Ui.Label(GameSystemInfo.DisplayName(system), "HeaderLabel"));
        col.AddChild(Ui.Label(GameBlurb(system), wrap: true));
        col.AddChild(Ui.Label(Library(system), "DimLabel", wrap: true));

        // The space above the buttons is kept for faction art, as in the mockup.
        col.AddChild(Ui.Grow());

        foreach (EMode mode in ModesOf(system))
        {
            Button b = Ui.Button(ModeLabel(mode), () => OpenMode(system, mode), ModeBlurb(system, mode), "ModeButton");
            b.Name = $"{system}_{mode}";
            b.CustomMinimumSize = new Vector2(0, 64);
            col.AddChild(b);
        }
        return card;
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
            string folder = Path.Combine(armies, ArmyPaths.FolderNameFor(system));
            if (Directory.Exists(folder)) lists = Directory.GetFiles(folder, "*.fdgarmy").Length;
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
                App.I.OpenNotYet($"{game} - Quest Campaigns",
                    "Campaign list, party cards, chapters and history; Structure and Journal. Port step 5.");
                break;
            case EMode.Campaign:
                App.I.OpenNotYet($"{game} - Campaign",
                    "Campaign list with the scoreboard, the 4-tab New Campaign, the Camp and Mission Setup. Port step 5.");
                break;
            case EMode.BuildHeroes:
                App.I.OpenNotYet($"{game} - Hero Builder",
                    "My Heroes cards, New Hero in 4 steps (Hero & Name / Class / Starter Skills / Gear), the Shop. Port step 5.");
                break;
            default:
                App.I.OpenNotYet($"Lobby - {game} - {ModeLabel(mode)}",
                    "Tabbed settings (Game / Table / Rules / Advanced / Connect) with a tooltip on every setting, "
                    + "2x2 seat cards, the clicked seat's army list under the map preview, LAUNCH. Port step 2 - next.");
                break;
        }
    }
}
