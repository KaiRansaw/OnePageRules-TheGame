using FDG;

namespace FdgRaylib.Quest;

/// <summary>
/// #418 - the folder-safe name a Quest system's heroes and campaigns are filed under, and the only
/// place that string is decided.
///
/// <para>
/// It is a string rather than the enum because it is <b>on disk</b>: <c>heroes/&lt;game type&gt;/</c>,
/// <c>heroes/&lt;game type&gt;/campaigns/</c>, and <see cref="FDG.Quest.QuestHero.GameType"/> inside
/// every saved <c>.fdghero</c> file. Changing one of these names orphans every hero a player has
/// built, so the value is pinned by a test rather than typed out in each screen - it used to be a
/// <c>const</c> on the Hero Builder that five other files reached into, which is why AoF: Quest heroes
/// could not exist.
/// </para>
/// <para>
/// Deliberately the same spelling as <see cref="ArmyPaths.FolderNameFor"/>'s army folders, so a system
/// keeps one name across <c>armies/</c>, <c>heroes/</c> and <c>Assets/Books/</c>. A test asserts the two
/// agree rather than one calling the other, because they are allowed to diverge if a future system
/// needs it - they just must not diverge by accident.
/// </para>
/// </summary>
public static class QuestGameTypes
{
    public const string StarQuest = "GF Star Quest";
    public const string AoFQuest = "AoF Quest";

    /// <summary>The game-type string for a Quest system. A non-Quest system reads as Star Quest's,
    /// because only a Quest game has heroes to file - callers gate on
    /// <see cref="GameSystemInfo.IsQuest"/>.</summary>
    public static string For(EGameSystem system) => system switch
    {
        EGameSystem.AoFQuest => AoFQuest,
        _ => StarQuest,
    };

    /// <summary>The Quest system a saved hero or campaign belongs to. An unrecognised or absent value
    /// reads as Star Quest, which is what every hero saved before #418 is.</summary>
    public static EGameSystem SystemFor(string? gameType) =>
        string.Equals(gameType, AoFQuest, StringComparison.OrdinalIgnoreCase)
            ? EGameSystem.AoFQuest
            : EGameSystem.GFStarQuest;

    /// <summary>Both game types, newest last, for a picker or a fixture that walks them.</summary>
    public static readonly string[] All = { StarQuest, AoFQuest };
}
