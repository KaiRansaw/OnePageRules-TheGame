using FDG;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Bug hunt 2026-11-27 (run 3): the lobby's map preview drew Horde Mode's layout - five numbered objectives, a
/// 9" centre circle and 9" corner circles - for every Horde-like mode, including a Star Quest mission, which has
/// no fixed objectives or zones at all (Campaign Builder v2.1.0 rolls them), and the skirmish and Quest Horde
/// games, whose books say 6".
/// </summary>
[TestFixture]
public class LobbyHordePreviewTests
{
    [TestCase(EGameSystem.GFStarQuest)]
    [TestCase(EGameSystem.AoFQuest)]
    public void QuestMission_HasNoFixedObjectivesOrZones(EGameSystem system)
    {
        var layout = LobbyDeploymentPreview.HordeLike(system, EGameMode.StarQuestMission);
        Assert.That(layout, Is.Not.Null);
        Assert.That(layout!.Value.FixedObjectives, Is.False);
        Assert.That(layout.Value.Note, Does.Not.Contain("5 fixed objectives"));
    }

    // GF / AoF Horde Mode v3.6.0: "within 9" of the table centre"; GFF / AoFS: "within 6""; Quest Horde v2.0.0: 6".
    [TestCase(EGameSystem.GrimdarkFuture, EGameMode.Horde, 9f)]
    [TestCase(EGameSystem.AgeOfFantasy, EGameMode.Horde, 9f)]
    [TestCase(EGameSystem.GFFirefight, EGameMode.Horde, 6f)]
    [TestCase(EGameSystem.AoFSkirmish, EGameMode.Horde, 6f)]
    [TestCase(EGameSystem.GFStarQuest, EGameMode.StarQuestHorde, 6f)]
    [TestCase(EGameSystem.AoFQuest, EGameMode.StarQuestHorde, 6f)]
    public void HordeModes_DrawTheirBooksDeploymentRadius(EGameSystem system, EGameMode mode, float radius)
    {
        var layout = LobbyDeploymentPreview.HordeLike(system, mode);
        Assert.That(layout!.Value.FixedObjectives, Is.True);
        Assert.That(layout.Value.ZoneRadiusInches, Is.EqualTo(radius));
    }

    [Test]
    public void StandardGames_AreNotHordeLike() =>
        Assert.That(LobbyDeploymentPreview.HordeLike(EGameSystem.GrimdarkFuture, EGameMode.Standard), Is.Null);
}
