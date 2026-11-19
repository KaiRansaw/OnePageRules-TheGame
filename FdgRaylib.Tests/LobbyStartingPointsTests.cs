using FDG;
using FdgRaylib.Config;
using FdgRaylib.Lobby;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-19 ("Start at 1500"): an Age of Fantasy lobby opened at the 2000 pts left over from Grimdark Future (or
/// from the first-run default). The AoF Advanced Rulebook's full match is 1500 pts; above that the table grows past the
/// 6' x 4' every bundled map is drawn for. A game you switch to now opens at its own book's full match, and the game you
/// played last keeps the size you played it at.
/// </summary>
[TestFixture, NonParallelizable]
public class LobbyStartingPointsTests
{
    private EGameSystem _system;
    private int _points;
    private ETerrainPlacementMode _mode;

    [SetUp]
    public void SetUp()
    {
        var h = UserConfig.Current.HostSettings;
        (_system, _points, _mode) = (h.GameSystem, h.ArmyPoints, h.TerrainPlacementMode);
        h.TerrainPlacementMode = ETerrainPlacementMode.AutoFromLayout;
    }

    [TearDown]
    public void TearDown()
    {
        var h = UserConfig.Current.HostSettings;
        (h.GameSystem, h.ArmyPoints, h.TerrainPlacementMode) = (_system, _points, _mode);
    }

    [TestCase(EGameSystem.GrimdarkFuture, 2000, EGameSystem.AgeOfFantasy, 1500, TestName = "AfterGrimdarkFuture_AgeOfFantasyOpensAtItsBooks1500")]
    [TestCase(EGameSystem.AoFSkirmish, 300, EGameSystem.AgeOfFantasy, 1500, TestName = "AfterSkirmish_AgeOfFantasyOpensAtItsBooks1500")]
    [TestCase(EGameSystem.AgeOfFantasy, 1500, EGameSystem.GrimdarkFuture, 2000, TestName = "AfterAgeOfFantasy_GrimdarkFutureOpensAtItsBooks2000")]
    [TestCase(EGameSystem.AgeOfFantasy, 1000, EGameSystem.AgeOfFantasy, 1000, TestName = "TheSameGameAgain_KeepsTheSizeYouLastPlayed")]
    [TestCase(EGameSystem.GrimdarkFuture, 3000, EGameSystem.GrimdarkFuture, 3000, TestName = "ABigGrimdarkFutureGame_IsStillBigNextTime")]
    [TestCase(EGameSystem.GrimdarkFuture, 2000, EGameSystem.GFFirefight, 300, TestName = "AFirefightLobby_StillOpensSmall")]
    public async Task ALobbyOpensAt(EGameSystem last, int lastPoints, EGameSystem opened, int expected)
    {
        var h = UserConfig.Current.HostSettings;
        h.GameSystem = last;
        h.ArmyPoints = lastPoints;

        LobbySession? s = null;
        string err = "";
        for (int i = 0; i < 50 && s == null; i++)   // the port is let go as the lobby before this one closes
            if (!LobbySession.TryHost(opened, EGameMode.Standard, out s, out err)) { s = null; await Task.Delay(100); }
        Assert.That(s, Is.Not.Null, err);
        using LobbySession lobby = s!;

        Assert.That(lobby.Vm.ArmyPoints, Is.EqualTo(expected));
    }
}
