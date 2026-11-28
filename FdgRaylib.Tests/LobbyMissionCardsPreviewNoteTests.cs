using FDG;
using FDG.Missions;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Bug hunt 2026-11-28 (AoF: Skirmish + Mission Cards): with Mission Cards on, the lobby's map preview still said
/// "objectives placed at the start" - the same words as the core rules' D3+2. Mission Cards v3.6.0, General Rules:
/// "Place 6 objective markers."
/// </summary>
[TestFixture]
public class LobbyMissionCardsPreviewNoteTests
{
    [TestCase(EGameSystem.AoFSkirmish)]
    [TestCase(EGameSystem.GFFirefight)]
    [TestCase(EGameSystem.GrimdarkFuture)]
    [TestCase(EGameSystem.AgeOfFantasy)]
    public void MissionCards_NoteSaysSixObjectives(EGameSystem system) =>
        Assert.That(LobbyDeploymentPreview.ObjectivesNote(system, EGameMode.Standard, EMissionCardGame.DirectChallenge,
            EObjectivePlacementMode.AutoPlaced), Does.StartWith("6 objectives"));

    [Test]
    public void CoreObjectives_NoteGivesNoCount() =>
        Assert.That(LobbyDeploymentPreview.ObjectivesNote(EGameSystem.AoFSkirmish, EGameMode.Standard, EMissionCardGame.Off,
            EObjectivePlacementMode.PlayerPlaced), Is.EqualTo("objectives placed by the players"));

    [Test]
    public void Horde_IgnoresALeftoverMissionCardsPick() =>
        Assert.That(LobbyDeploymentPreview.ObjectivesNote(EGameSystem.AoFSkirmish, EGameMode.Horde, EMissionCardGame.BattleOfWits,
            EObjectivePlacementMode.AutoPlaced), Does.Not.Contain("6"));
}
