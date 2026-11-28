using FDG;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering;

/// <summary>
/// Which lobby settings a game and mode have, and the deployment zones the map previews draw for the
/// lobby's Deployment pick. Pure (no ImGui) so the rules are pinned by tests.
///
/// <para>
/// The zones come from the engine's own <see cref="AdvancedDeployment"/> - the book diagrams for THIS game
/// (GF / AoF draw the long-table pages, GF: Firefight / AoF: Skirmish the square-table pages) - and from
/// the same order of precedence the engine's ChooseMapSideStage uses: three or four sides play the
/// free-for-all corners; two sides use the pick. A map never decides deployment (Kai, 2026-11-07) - zones
/// an old map still carries are ignored by the engine, so the preview ignores them too.
/// </para>
/// </summary>
internal static class LobbyDeploymentPreview
{
    /// <summary>The Deployment setting: a standard game of a game whose Advanced pages are in the engine.
    /// Horde and the Quest modes place both sides their own way, so it is not shown there.</summary>
    internal static bool DeploymentApplies(EGameSystem system, EGameMode mode) =>
        mode == EGameMode.Standard && AdvancedDeployment.Available(system);

    /// <summary>Mission Cards: the four 3.6.0 games, standard game only.</summary>
    internal static bool MissionCardsApply(EGameSystem system, EGameMode mode) =>
        mode == EGameMode.Standard && FDG.Missions.MissionCardCatalog.Available(system);

    /// <summary>Game Length: standard games only - Horde and the Quest modes keep their own length.</summary>
    internal static bool GameLengthApplies(EGameMode mode) => mode == EGameMode.Standard;

    /// <summary>Fast-Play: only Grimdark Future and Age of Fantasy print it.</summary>
    internal static bool FastPlayApplies(EGameSystem system) => FastPlay.BooksHaveIt(system);

    /// <summary>Extra Actions: every game with an Advanced Rulebook; the Quest games have none.</summary>
    internal static bool ExtraActionsApply(EGameSystem system, EGameMode mode) =>
        !GameSystemInfo.IsQuest(system) && !GameSystemInfo.IsQuestMode(mode);

    /// <summary>King of the Hill: a 3-4 player standard game of the four Advanced Rulebook games.</summary>
    internal static bool KingOfTheHillApplies(EGameSystem system, EGameMode mode) =>
        mode == EGameMode.Standard && !GameSystemInfo.IsQuest(system);

    /// <summary>What a Horde-like mode's preview draws: the five fixed objectives and the centre / corner circles
    /// (with their radius), or nothing fixed at all, and the note under the picture.</summary>
    internal readonly record struct HordeLayout(bool FixedObjectives, float ZoneRadiusInches, string Note);

    /// <summary>
    /// Horde-like modes, read from their own books (null for any other mode):
    /// <list type="bullet">
    /// <item>Horde Mode v3.6.0: five objectives (centre and quarter centres); defenders within 9" of the centre and
    /// enemies within 9" of the corners in GF / AoF, 6" in GF: Firefight / AoF: Skirmish.</item>
    /// <item>Star Quest / AoF: Quest Horde Mode v2.0.0: the same five objectives; heroes "within 6" of the table
    /// centre", waves "within 6" of a random table corner".</item>
    /// <item>Star Quest / AoF: Quest missions (Campaign Builder v2.1.0): nothing is fixed. The primary objective sets
    /// the markers, sentries go to rolled quarter centres and the heroes to a rolled corner - so the preview drew
    /// Horde's five numbered objectives and centre circle for a game that has neither.</item>
    /// </list>
    /// </summary>
    internal static HordeLayout? HordeLike(EGameSystem system, EGameMode mode) => mode switch
    {
        EGameMode.StarQuestMission => new HordeLayout(false, 0f,
            "objectives, sentries and the heroes' corner are rolled when the mission starts"),
        EGameMode.StarQuestHorde => new HordeLayout(true, 6f, "5 fixed objectives"),
        EGameMode.Horde => new HordeLayout(true, GameSystemInfo.IsSkirmish(system) ? 6f : 9f, "5 fixed objectives"),
        _ => null,
    };

    /// <summary>What the preview draws: zero, or two zones (A first), and a line saying where they come from.</summary>
    internal readonly record struct Plan(IReadOnlyList<IZone> Zones, string Note);

    /// <summary>
    /// The deployment the game will use, for a standard game - whatever map is chosen, since a map has no
    /// say in deployment. Horde-like modes return no zones: their callers draw Horde's own circles.
    /// </summary>
    internal static Plan For(EGameSystem system, EGameMode mode, EDeploymentStyle style, int sides,
        float tableW, float tableH)
    {
        if (GameSystemInfo.IsHordeLike(mode)) return new Plan(Array.Empty<IZone>(), "");

        if (sides >= 3 && !GameSystemInfo.IsQuest(system))
            return new Plan(Array.Empty<IZone>(),
                "Free-for-all: each side picks a table corner when deployment starts.");

        if (!AdvancedDeployment.Available(system) || style == EDeploymentStyle.Core)
        {
            (RectangularZone a, RectangularZone b) = AdvancedDeployment.Zones(EDeploymentStyle.Core, system, tableW, tableH);
            return new Plan(new IZone[] { a, b }, "Deployment: core rules, 12\" from your table edge.");
        }

        if (style is EDeploymentStyle.RandomStandard or EDeploymentStyle.RandomStrategic or EDeploymentStyle.RandomAsymmetric)
        {
            (RectangularZone a, RectangularZone b) = AdvancedDeployment.Zones(EDeploymentStyle.Core, system, tableW, tableH);
            return new Plan(new IZone[] { a, b },
                $"Deployment: {AdvancedDeployment.Name(style)} - rolled when deployment starts (core zones shown).");
        }

        (RectangularZone za, RectangularZone zb) = AdvancedDeployment.Zones(style, system, tableW, tableH);
        return new Plan(new IZone[] { za, zb }, $"Deployment: {AdvancedDeployment.Name(style)}.");
    }

    /// <summary>The number of sides: distinct teams among the seats (a seat with no team is its own side).</summary>
    internal static int Sides(IEnumerable<int> teamNumbers)
    {
        var teams = new HashSet<int>();
        int loose = 0;
        foreach (int t in teamNumbers)
        {
            if (t <= 0) loose++;
            else teams.Add(t);
        }
        return teams.Count + loose;
    }

    /// <summary>The preview note's objectives part when no map or Horde layout fixes them. Mission Cards v3.6.0: "Place 6
    /// objective markers" (bug hunt 2026-11-28: the note gave no count, the same as the core rules' D3+2).</summary>
    internal static string ObjectivesNote(EGameSystem system, EGameMode mode, FDG.Missions.EMissionCardGame cards,
        EObjectivePlacementMode placement)
    {
        string who = placement == EObjectivePlacementMode.PlayerPlaced ? "placed by the players" : "placed at the start";
        return MissionCardsApply(system, mode) && cards != FDG.Missions.EMissionCardGame.Off
            ? $"6 objectives {who} (Mission Cards)"
            : $"objectives {who}";
    }
}
