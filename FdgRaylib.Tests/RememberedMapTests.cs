using FDG;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.Config;
using FdgRaylib.Lobby;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Bug hunt (Map Maker), 2026-11-19: a lobby opens on the last hosted game's settings, and the map came along whatever
/// had happened to it since. Delete or rename the map in the Map Maker and every later lobby still named it - Launch
/// answered "Could not load terrain layout: File not found". And a 6' x 4' Grimdark Future map followed the player
/// into a Firefight lobby (4' x 4'), where Select Map does not even list it, and the game was played on it.
/// </summary>
[TestFixture, NonParallelizable]
public class RememberedMapTests
{
    private string _dir = "";
    private ETerrainPlacementMode _mode;
    private string? _path;
    private int _points;
    private EGameSystem _system;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-remembered-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var h = UserConfig.Current.HostSettings;
        (_mode, _path, _points, _system) = (h.TerrainPlacementMode, h.TerrainLayoutPath, h.ArmyPoints, h.GameSystem);
    }

    [TearDown]
    public void TearDown()
    {
        var h = UserConfig.Current.HostSettings;
        (h.TerrainPlacementMode, h.TerrainLayoutPath, h.ArmyPoints, h.GameSystem) = (_mode, _path, _points, _system);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    /// <summary>A saved map on a table of that size, remembered as the last game's map.</summary>
    private string Remember(float w, float h, bool keepFile = true)
    {
        MapDraft d = MapDraft.New(w, h);
        d.Name = "Remembered";
        d.Pieces.Add(MapPiece.FromKind(TerrainBook.Kind("Forest")!, w / 2, h / 2));
        string path = Path.Combine(_dir, "Remembered" + TerrainLayoutFile.EXTENSION_WITH_PERIOD);
        File.WriteAllText(path, TerrainLoader.SerializeToJson(d.ToLayout()));
        if (!keepFile) File.Delete(path);
        var host = UserConfig.Current.HostSettings;
        host.GameSystem = EGameSystem.GrimdarkFuture;
        host.ArmyPoints = 2000;
        host.TerrainPlacementMode = ETerrainPlacementMode.LoadFromFile;
        host.TerrainLayoutPath = path;
        return path;
    }

    private static async Task<LobbySession> Host(EGameSystem system)
    {
        LobbySession? s = null;
        string err = "";
        // The port is let go as the lobby before this one closes.
        for (int i = 0; i < 50 && s == null; i++)
            if (!LobbySession.TryHost(system, EGameMode.Standard, out s, out err)) { s = null; await Task.Delay(100); }
        Assert.That(s, Is.Not.Null, err);
        return s!;
    }

    [Test]
    public async Task AMapDeletedSinceTheLastGame_IsNotCarriedIntoTheNextLobby()
    {
        Remember(72, 48, keepFile: false);

        using LobbySession lobby = await Host(EGameSystem.GrimdarkFuture);

        Assert.That(lobby.HasMap, Is.False, "the map file is gone, so the lobby is back on Random");
        Assert.That(lobby.Vm.TerrainPlacementMode, Is.EqualTo(ETerrainPlacementMode.AutoFromLayout));
        Assert.That(lobby.Vm.TerrainLayoutPath, Is.Null.Or.Empty);
    }

    [Test]
    public async Task AMapDrawnForAnotherTable_IsNotCarriedIntoAGameThatPlaysOnADifferentOne()
    {
        Remember(72, 48);

        using LobbySession lobby = await Host(EGameSystem.GFFirefight);

        Assume.That(lobby.TableWithoutMap(), Is.EqualTo((48f, 48f)), "Firefight's book table");
        Assert.That(lobby.HasMap, Is.False, "Select Map offers a 4' x 4' game no 6' x 4' map, so the lobby must not open on one");
    }

    [Test]
    public async Task AMapThatStillFitsThisGame_IsStillThereNextTime()
    {
        string path = Remember(72, 48);

        using LobbySession lobby = await Host(EGameSystem.GrimdarkFuture);

        Assume.That(lobby.TableWithoutMap(), Is.EqualTo((72f, 48f)), "Grimdark Future's book table at 2000 pts");
        Assert.That(lobby.HasMap, Is.True, "the last game's map is kept when the picker would offer it");
        Assert.That(lobby.Vm.TerrainLayoutPath, Is.EqualTo(path));
    }
}
